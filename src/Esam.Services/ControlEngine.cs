using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Esam.Communication.Polling;
using Esam.Domain;
using Esam.Domain.Configuration;
using Esam.Domain.Control;
using Esam.Domain.Models;
using Esam.Domain.Units;

namespace Esam.Services
{
    /// <summary>
    /// 자동 압력 제어 루프. 스냅샷을 읽어 제어 정책을 실행하고 지령을 투입한다.
    /// </summary>
    /// <remarks>
    /// <para><b>폴링과 독립된 주기로 돈다.</b> 통신 사이클이 250ms 걸리더라도
    /// 제어 판단은 설정된 <c>ControlPeriodMs</c> 로 유지된다. 두 주기를 묶으면
    /// 통신이 느려질 때 제어까지 같이 느려져 튜닝한 Dwell 값이 무의미해진다.</para>
    /// <para><b>스냅샷을 끌어간다(pull).</b> 폴링 스레드가 제어를 호출하는 구조가 아니라,
    /// 제어 루프가 최신 스냅샷을 읽는다. 통신이 잠시 멈춰도 제어 루프는 계속 돌며
    /// 품질 불량을 감지해 액추에이터를 건드리지 않는다.</para>
    /// <para><b>인터록보다 낮은 우선순위다.</b> 여기서 만드는 지령은
    /// <see cref="CommandPriority.Automatic"/> 이므로, 큐에서 인터록·수동 지령에 밀린다.</para>
    /// </remarks>
    public sealed class ControlEngine : IDisposable
    {
        private readonly DataStore _store;
        private readonly ControlConfig _config;
        private readonly IControlPolicy _policy;
        private readonly SystemStateMachine _stateMachine;
        private readonly IClock _clock;
        private readonly List<ChainRuntime> _runtimes = new List<ChainRuntime>();
        private readonly List<ModbusPortWorker> _workers = new List<ModbusPortWorker>();
        private readonly Dictionary<string, MovingAverageFilter> _filters;
        private readonly object _gate = new object();

        private CancellationTokenSource _cancellation;
        private Task _loopTask;
        private bool _disposed;
        private long _stepCount;
        private double _lastStepMs;

        /// <summary>제어 엔진을 생성한다.</summary>
        /// <param name="store">데이터 저장소.</param>
        /// <param name="config">제어 설정.</param>
        /// <param name="policy">제어 정책. null 이면 밴드 제어를 사용한다.</param>
        /// <param name="clock">시각 제공자.</param>
        /// <exception cref="ArgumentNullException">필수 인자가 null 일 때.</exception>
        public ControlEngine(
            DataStore store, ControlConfig config, IControlPolicy policy, IClock clock)
        {
            if (store == null)
            {
                throw new ArgumentNullException("store");
            }

            if (config == null)
            {
                throw new ArgumentNullException("config");
            }

            _store = store;
            _config = config;
            _policy = policy ?? new BandControlPolicy();
            _clock = clock ?? SystemClock.Instance;
            _stateMachine = new SystemStateMachine(_clock);

            _filters = new Dictionary<string, MovingAverageFilter>(StringComparer.OrdinalIgnoreCase);

            if (config.Chains != null)
            {
                foreach (ChainDefinition chain in config.Chains)
                {
                    if (chain != null)
                    {
                        _runtimes.Add(new ChainRuntime(chain));
                    }
                }
            }
        }

        /// <summary>시스템 상태머신. 화면과 인터록이 상태 전이를 요청한다.</summary>
        public SystemStateMachine StateMachine
        {
            get { return _stateMachine; }
        }

        /// <summary>제어 스텝 수행 횟수.</summary>
        public long StepCount
        {
            get { return Interlocked.Read(ref _stepCount); }
        }

        /// <summary>직전 제어 스텝 소요 시간 [ms]. 진단용.</summary>
        public double LastStepMs
        {
            get { return _lastStepMs; }
        }

        /// <summary>루프 실행 여부.</summary>
        public bool IsRunning
        {
            get
            {
                Task task = _loopTask;
                return task != null && !task.IsCompleted;
            }
        }

        /// <summary>현재 제어 상태 요약. DataStore 가 스냅샷에 넣는다.</summary>
        public ControlStatus BuildStatus()
        {
            List<ChainStatus> chains = new List<ChainStatus>();
            ModeSetting mode = ResolveMode();

            foreach (ChainRuntime runtime in _runtimes)
            {
                double pv = 0.0;

                SystemSnapshot snapshot = _store.Current;
                PressureReading reading = snapshot.FindPressure(ResolveSensorId(runtime.Definition));

                if (reading != null)
                {
                    pv = reading.Pa;
                }

                chains.Add(new ChainStatus(
                    runtime.Definition.Id,
                    runtime.Definition.Name,
                    runtime.LastResult,
                    pv,
                    mode.SetpointPa,
                    mode.LowLimitPa,
                    mode.HighLimitPa,
                    runtime.DeviationElapsedMs));
            }

            return new ControlStatus(
                _stateMachine.Phase, _config.ActiveMode, _stateMachine.IsAutoEnabled, chains, null);
        }

        /// <summary>지령을 투입할 포트 워커를 등록한다.</summary>
        /// <param name="worker">포트 워커.</param>
        public void RegisterWorker(ModbusPortWorker worker)
        {
            if (worker == null)
            {
                return;
            }

            lock (_gate)
            {
                if (!_workers.Contains(worker))
                {
                    _workers.Add(worker);
                }
            }
        }

        /// <summary>제어 루프를 시작한다.</summary>
        public void Start()
        {
            ThrowIfDisposed();

            if (IsRunning)
            {
                return;
            }

            _cancellation = new CancellationTokenSource();
            CancellationToken token = _cancellation.Token;

            _loopTask = Task.Factory.StartNew(
                () => RunLoop(token), token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }

        /// <summary>제어 루프를 중지하고 자동 지령을 비운다.</summary>
        /// <param name="timeoutMs">종료 대기 시간 [ms].</param>
        /// <returns>정상 종료되었으면 true.</returns>
        public bool Stop(int timeoutMs = 3000)
        {
            CancellationTokenSource cancellation = _cancellation;
            Task task = _loopTask;

            if (cancellation == null || task == null)
            {
                return true;
            }

            cancellation.Cancel();

            bool completed;

            try
            {
                completed = task.Wait(timeoutMs);
            }
            catch (AggregateException)
            {
                completed = true;
            }

            _loopTask = null;
            _cancellation = null;
            cancellation.Dispose();

            // 큐에 남은 자동 지령을 비운다. 정지 후 뒤늦게 실행되면 예상 밖으로 액추에이터가 움직인다.
            ClearAutomaticCommands();

            return completed;
        }

        /// <summary>
        /// 제어 스텝 1회를 수행한다. 테스트에서 스레드 없이 직접 호출할 수 있다.
        /// </summary>
        /// <returns>이번 스텝에서 투입한 지령 수.</returns>
        public int ExecuteStep()
        {
            Stopwatch watch = Stopwatch.StartNew();

            int dispatched = 0;

            // 자동 제어 단계가 아니면 지령을 만들지 않는다.
            // Homing 중이거나 인터록 상태에서 자동 지령이 나가면 안 된다.
            if (_stateMachine.Phase != SystemPhase.AutoControl)
            {
                watch.Stop();
                _lastStepMs = watch.Elapsed.TotalMilliseconds;
                Interlocked.Increment(ref _stepCount);
                return 0;
            }

            SystemSnapshot snapshot = _store.Current;
            ModeSetting mode = ResolveMode();
            DateTime nowUtc = _clock.UtcNow;

            List<ActuatorCommand> commands = new List<ActuatorCommand>();

            foreach (ChainRuntime runtime in _runtimes)
            {
                ChainDefinition definition = runtime.Definition;

                if (!definition.Enabled)
                {
                    continue;
                }

                string sensorId = ResolveSensorId(definition);
                PressureReading reading = snapshot.FindPressure(sensorId);

                double pv;
                Quality quality;

                if (reading == null)
                {
                    pv = 0.0;
                    quality = Quality.NoData;
                }
                else
                {
                    pv = ApplyFilter(sensorId, reading);
                    quality = reading.Quality;
                }

                ChainControlContext context = new ChainControlContext(
                    runtime,
                    pv,
                    quality,
                    snapshot.FindValve(definition.ValveId),
                    snapshot.FindFan(definition.FanId),
                    mode,
                    _config.Valve,
                    _config.Fan,
                    nowUtc);

                ControlDecision decision = _policy.Step(context);

                foreach (ActuatorCommand command in decision.Commands)
                {
                    commands.Add(command);
                }
            }

            // 단계를 투입 직전에 한 번 더 확인한다.
            // 스텝 시작 시점에만 검사하면, 판정 중에 폴링 스레드가 인터록을 발동시킨 경우
            // 이미 Interlocked 인데 자동 지령이 큐에 들어간다. 인터록이 닫은 밸브를 다시 여는 경로다.
            if (commands.Count > 0 && _stateMachine.Phase == SystemPhase.AutoControl)
            {
                Dispatch(commands);
                dispatched = commands.Count;
            }

            watch.Stop();
            _lastStepMs = watch.Elapsed.TotalMilliseconds;
            Interlocked.Increment(ref _stepCount);

            return dispatched;
        }

        /// <summary>자동 운전을 요청한다.</summary>
        /// <returns>자동 제어 단계로 진입했으면 true.</returns>
        /// <remarks>
        /// 설정 검증을 통과하지 못하면 진입을 거부한다.
        /// 특히 팬 MaxRpm 이 0(사양 미확보)이면 증속 제어가 불가능하므로 막아야 한다.
        /// </remarks>
        public bool RequestAuto()
        {
            IList<string> errors;

            if (!_config.Validate(out errors))
            {
                return false;
            }

            if (_config.Fan != null && !_config.Fan.IsUsableForAutoControl)
            {
                return false;
            }

            // 팬 지령 적분 상태를 드라이버의 현재 설정값으로 맞춘다.
            // 이 단계가 없으면 수동 운전으로 이미 팬이 돌고 있어도 제어기는
            // 지령 이력이 없다고 보고 최소값부터 다시 증속한다.
            SystemSnapshot snapshot = _store.Current;

            foreach (ChainRuntime runtime in _runtimes)
            {
                runtime.Reset();

                FanState fan = snapshot.FindFan(runtime.Definition.FanId);

                if (fan != null && fan.Quality == Quality.Good)
                {
                    runtime.SeedFanCommand(fan.TargetRpm);
                }
            }

            return _stateMachine.Fire(SystemTrigger.AutoRequested);
        }

        /// <summary>자동 운전을 중지한다.</summary>
        public void StopAuto()
        {
            _stateMachine.Fire(SystemTrigger.AutoStopRequested);
            ClearAutomaticCommands();
        }

        /// <summary>제어 루프 본체.</summary>
        private void RunLoop(CancellationToken token)
        {
            int period = _config.ControlPeriodMs > 0 ? _config.ControlPeriodMs : 200;

            while (!token.IsCancellationRequested)
            {
                Stopwatch iteration = Stopwatch.StartNew();

                try
                {
                    ExecuteStep();
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception)
                {
                    // 제어 루프는 어떤 예외로도 죽지 않아야 한다.
                    // 루프가 멈추면 압력이 그대로 방치되고 상위는 그것조차 알 수 없다.
                }

                double remaining = period - iteration.Elapsed.TotalMilliseconds;

                if (remaining > 1.0)
                {
                    try
                    {
                        token.WaitHandle.WaitOne((int)remaining);
                    }
                    catch (ObjectDisposedException)
                    {
                        break;
                    }
                }
            }
        }

        /// <summary>적용 중인 모드 설정을 가져온다.</summary>
        private ModeSetting ResolveMode()
        {
            return _config.GetMode(_config.ActiveMode);
        }

        /// <summary>체인이 참조할 센서 ID 를 결정한다.</summary>
        /// <remarks>
        /// Sensor1 모드의 "Average" 방식은 아직 확정되지 않았다(Open Issue #16).
        /// 확정 전까지는 고정 ID 참조만 지원하고, Average 가 지정되면 첫 센서를 쓴다.
        /// </remarks>
        private string ResolveSensorId(ChainDefinition definition)
        {
            string resolved = definition.ResolveSensorId(_config.ActiveMode, _config.Sensor1Reference);

            if (!string.IsNullOrEmpty(resolved))
            {
                return resolved;
            }

            return string.IsNullOrEmpty(definition.Sensor1Id) ? "S1-1" : definition.Sensor1Id;
        }

        /// <summary>
        /// 측정값에 이동평균을 적용한다.
        /// </summary>
        /// <remarks>
        /// 통신 계층에도 디바이스별 필터가 있지만, 제어 기준값에는 제어 설정의
        /// 창 크기를 따로 적용한다. 표시용 평활과 제어용 평활의 요구가 다를 수 있고,
        /// Phase 5 튜닝에서 제어 쪽만 조정해야 하는 상황이 생긴다.
        /// </remarks>
        private double ApplyFilter(string sensorId, PressureReading reading)
        {
            if (_config.FilterWindowSize <= 1 || reading.Quality != Quality.Good)
            {
                // 품질이 나쁜 값을 창에 넣으면 회복 후에도 한동안 오염된 평균이 나온다.
                return reading.Pa;
            }

            MovingAverageFilter filter;

            if (!_filters.TryGetValue(sensorId, out filter))
            {
                filter = new MovingAverageFilter(_config.FilterWindowSize);
                _filters[sensorId] = filter;
            }

            return filter.Add(reading.Pa);
        }

        /// <summary>등록된 워커에 지령을 투입한다.</summary>
        private void Dispatch(IList<ActuatorCommand> commands)
        {
            List<ModbusPortWorker> targets;

            lock (_gate)
            {
                targets = new List<ModbusPortWorker>(_workers);
            }

            foreach (ModbusPortWorker worker in targets)
            {
                worker.EnqueueCommands(commands);
            }
        }

        /// <summary>모든 워커의 자동 지령을 비운다.</summary>
        private void ClearAutomaticCommands()
        {
            List<ModbusPortWorker> targets;

            lock (_gate)
            {
                targets = new List<ModbusPortWorker>(_workers);
            }

            foreach (ModbusPortWorker worker in targets)
            {
                worker.ClearAutomaticCommands();
            }
        }

        /// <summary>객체가 해제되었는지 확인한다.</summary>
        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException("ControlEngine");
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            Stop();
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "ControlEngine[{0}] 체인 {1}, 스텝 {2}, 직전 {3:F2} ms",
                _stateMachine.Phase, _runtimes.Count, StepCount, LastStepMs);
        }
    }
}
