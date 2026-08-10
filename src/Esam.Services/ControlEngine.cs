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

        /// <summary>
        /// 이번 기동에서 원점 복귀를 이미 지령한 밸브 ID 집합.
        /// </summary>
        /// <remarks>
        /// 매 스텝 같은 지령을 반복하면 드라이브가 복귀 동작을 재시작해 영영 끝나지 않는다.
        /// </remarks>
        private readonly HashSet<string> _homingRequested =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

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

        /// <summary>
        /// 자동 운전 진입 전 확인할 추가 조건. null 이면 검사하지 않는다.
        /// </summary>
        /// <remarks>
        /// <para>진입 가능하면 null 을, 불가하면 거부 사유를 반환하는 함수다.
        /// 조립 루트가 "안전 기능이 동작하지 않는 구성인지" 를 여기에 물린다.</para>
        /// <para>제어 엔진이 구성 경고를 직접 알게 하지 않는 이유는, 그러면
        /// Domain·Services 계층이 조립 관심사를 떠안기 때문이다. 판정 결과만 받는다.</para>
        /// </remarks>
        public Func<string> AutoEntryGuard { get; set; }

        /// <summary>직전 자동 운전 요청이 거부된 사유. 성공했거나 요청이 없으면 null.</summary>
        public string LastAutoRejectReason { get; private set; }

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
            SystemSnapshot snapshot = _store.Current;

            foreach (ChainRuntime runtime in _runtimes)
            {
                string sensorId = ResolveSensorId(runtime.Definition);

                // 화면에 표시할 목표·상하한도 센서별이다. 공통값을 보여주면
                // 작업자가 실제 적용값과 다른 숫자를 보게 된다.
                ModeSetting mode = _config.GetSetting(sensorId, _config.ActiveMode);

                double pv = 0.0;
                PressureReading reading = snapshot.FindPressure(sensorId);

                if (reading != null)
                {
                    pv = reading.Pa;
                }

                chains.Add(new ChainStatus(
                    runtime.Definition.Id,
                    runtime.Definition.Name,
                    runtime.LastResult,
                    pv,
                    mode == null ? 0.0 : mode.SetpointPa,
                    mode == null ? 0.0 : mode.LowLimitPa,
                    mode == null ? 0.0 : mode.HighLimitPa,
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

            SystemPhase phase = _stateMachine.Phase;

            // 기동 단계는 이 루프가 진행시킨다. 별도 스레드를 두지 않는 이유는
            // 초기화·원점 복귀 모두 "스냅샷을 보고 완료를 판정" 하는 일이라
            // 제어 스텝과 성격이 같고, 주기도 같아도 충분하기 때문이다.
            if (phase == SystemPhase.Init)
            {
                AdvanceInit();
                watch.Stop();
                _lastStepMs = watch.Elapsed.TotalMilliseconds;
                Interlocked.Increment(ref _stepCount);
                return 0;
            }

            if (phase == SystemPhase.ValveHoming)
            {
                int homingCommands = AdvanceHoming();
                watch.Stop();
                _lastStepMs = watch.Elapsed.TotalMilliseconds;
                Interlocked.Increment(ref _stepCount);
                return homingCommands;
            }

            // 자동 제어 단계가 아니면 지령을 만들지 않는다.
            // Ready 는 수동 조작만 허용하고, 인터록·SafeStop 에서는 아무것도 내지 않는다.
            if (phase != SystemPhase.AutoControl)
            {
                watch.Stop();
                _lastStepMs = watch.Elapsed.TotalMilliseconds;
                Interlocked.Increment(ref _stepCount);
                return 0;
            }

            SystemSnapshot snapshot = _store.Current;
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

                // 설정값은 센서별이다. 레시피가 있으면 그 센서의 값을, 없으면 모드별 공통값을 쓴다.
                ModeSetting mode = _config.GetSetting(sensorId, _config.ActiveMode);

                if (mode == null)
                {
                    // 레시피에 이 센서가 없다. 공통값으로 메우면 이 체인만 조용히
                    // 다른 기준으로 제어되므로 건너뛴다. 사유는 결과에 남는다.
                    runtime.SetResult(ControlResult.Skipped);
                    continue;
                }

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

        /// <summary>
        /// 전 체인 액추에이터를 안전 위치로 보내는 지령을 투입한다.
        /// </summary>
        /// <param name="reason">지령 사유. 로그에 남는다.</param>
        /// <returns>투입한 지령 수.</returns>
        /// <remarks>
        /// <para>프로그램 종료 시 호출한다. 종료하면 폴링 스레드가 멈춰
        /// <b>인터록 평가도 함께 끝나는데</b>, 밸브는 열려 있고 팬은 계속 돈다.
        /// 아무도 보지 않는 상태로 남는 것이 문제다.</para>
        /// <para>자동 운전을 끄는 것(<see cref="StopAuto"/>)과는 다르다.
        /// 그때는 폴링이 계속되므로 인터록이 지킨다. 웨이퍼 처리 중에 기류를 끊지 않기 위해
        /// 액추에이터를 그대로 둔다.</para>
        /// <para>우선순위는 <see cref="CommandPriority.Interlock"/> 이다.
        /// 큐에 남은 자동·수동 지령보다 먼저 실행되어야 한다.</para>
        /// </remarks>
        public int ParkActuators(string reason)
        {
            List<ActuatorCommand> commands = new List<ActuatorCommand>();

            foreach (ChainRuntime runtime in _runtimes)
            {
                ChainDefinition definition = runtime.Definition;

                // 비활성 체인도 포함한다. 안전 정지에 예외를 두지 않는다.
                if (!string.IsNullOrEmpty(definition.ValveId))
                {
                    commands.Add(ActuatorCommand.CloseValve(
                        definition.ValveId, CommandPriority.Interlock, reason));
                }

                if (!string.IsNullOrEmpty(definition.FanId))
                {
                    commands.Add(ActuatorCommand.StopFan(
                        definition.FanId, CommandPriority.Interlock, reason));
                }
            }

            if (commands.Count > 0)
            {
                Dispatch(commands);
            }

            return commands.Count;
        }

        /// <summary>
        /// 초기화 단계를 진행한다. 모든 밸브를 읽을 수 있게 되면 원점 복귀로 넘어간다.
        /// </summary>
        /// <remarks>
        /// 통신이 성립하지 않은 채 원점 복귀로 넘어가면 지령이 나가지 않는데도
        /// 완료를 기다리게 되어, 원인이 "타임아웃" 으로만 보인다.
        /// 여기서 통신을 먼저 확인하면 실패 원인이 초기화 단계에 남는다.
        /// </remarks>
        private void AdvanceInit()
        {
            SystemSnapshot snapshot = _store.Current;
            int required = 0;
            int readable = 0;

            foreach (ChainRuntime runtime in _runtimes)
            {
                if (!runtime.Definition.Enabled || string.IsNullOrEmpty(runtime.Definition.ValveId))
                {
                    continue;
                }

                required++;
                ValveState valve = snapshot.FindValve(runtime.Definition.ValveId);

                if (valve != null && valve.Quality == Quality.Good)
                {
                    readable++;
                }
            }

            if (required > 0 && readable == required)
            {
                _stateMachine.Fire(SystemTrigger.InitCompleted);
                return;
            }

            // 통신이 끝내 성립하지 않으면 장애로 확정한다.
            // 원점 복귀 타임아웃과 같은 값을 쓴다. 둘 다 "장비가 응답하지 않는" 상황이다.
            if (_stateMachine.GetElapsedInPhase().TotalMilliseconds > _config.Valve.HomingTimeoutMs)
            {
                _stateMachine.Fire(SystemTrigger.FaultRaised);
            }
        }

        /// <summary>
        /// 원점 복귀 단계를 진행한다. 미완료 밸브에 Homing 을 지령하고 완료를 확인한다.
        /// </summary>
        /// <returns>이번 스텝에서 투입한 지령 수.</returns>
        /// <remarks>
        /// <para><b>종전에는 이 단계가 없었다.</b> 조립 루트가 <c>HomingCompleted</c> 를
        /// 확인 없이 발생시켜, 원점 복귀 지령이 프로덕션 경로에서 한 번도 전송되지 않았다.</para>
        /// <para>그 상태로 실장비를 돌리면 두 가지 중 하나가 된다.
        /// <c>homeDone</c> 이 false 면 <see cref="ValveState.IsControllable"/> 이 false 라
        /// 밴드 제어가 매 스텝 Skipped 를 반환한다. 화면은 자동 운전인데 아무것도 제어되지 않는다.
        /// 반대로 <c>homeDone</c> 이 참으로 읽히면 기계적 원점이 미확정이라
        /// 이후 모든 pulse 지령이 알 수 없는 만큼 어긋난다. 인터록의 "0 pulse 로 닫기" 도 마찬가지다.</para>
        /// <para>지령은 매 스텝 반복해서 내지 않는다. 드라이브가 원점 복귀 중일 때
        /// 같은 지령을 다시 받으면 동작을 재시작할 수 있기 때문이다.</para>
        /// </remarks>
        private int AdvanceHoming()
        {
            SystemSnapshot snapshot = _store.Current;
            List<ActuatorCommand> commands = new List<ActuatorCommand>();

            int required = 0;
            int completed = 0;

            foreach (ChainRuntime runtime in _runtimes)
            {
                ChainDefinition definition = runtime.Definition;

                if (!definition.Enabled || string.IsNullOrEmpty(definition.ValveId))
                {
                    continue;
                }

                required++;
                ValveState valve = snapshot.FindValve(definition.ValveId);

                if (valve != null && valve.Quality == Quality.Good && valve.IsHomeDone)
                {
                    completed++;
                    continue;
                }

                // 아직 지령하지 않은 밸브에만 1회 보낸다.
                if (_homingRequested.Add(definition.ValveId))
                {
                    commands.Add(ActuatorCommand.HomeValve(
                        definition.ValveId, "기동 시퀀스: 전원 투입 후 원점 복귀"));
                }
            }

            if (required > 0 && completed == required)
            {
                _homingRequested.Clear();
                _stateMachine.Fire(SystemTrigger.HomingCompleted);
                return 0;
            }

            if (_stateMachine.GetElapsedInPhase().TotalMilliseconds > _config.Valve.HomingTimeoutMs)
            {
                // 미완료 상태로 Ready 에 올려 보내면 제어가 성립하지 않는 채로 운전에 들어간다.
                _homingRequested.Clear();
                _stateMachine.Fire(SystemTrigger.FaultRaised);
                return 0;
            }

            if (commands.Count > 0)
            {
                Dispatch(commands);
            }

            return commands.Count;
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
                LastAutoRejectReason = "제어 설정 검증 실패: " + string.Join(" / ", ToArray(errors));
                return false;
            }

            if (_config.Fan != null && !_config.Fan.IsUsableForAutoControl)
            {
                LastAutoRejectReason =
                    "송풍팬 회전수 사양이 확정되지 않아 증속 제어가 불가능합니다.";
                return false;
            }

            string missingSettings = CheckSettingsAvailable();

            if (missingSettings != null)
            {
                LastAutoRejectReason = missingSettings;
                return false;
            }

            Func<string> guard = AutoEntryGuard;

            if (guard != null)
            {
                string reason = guard();

                if (!string.IsNullOrEmpty(reason))
                {
                    LastAutoRejectReason = reason;
                    return false;
                }
            }

            LastAutoRejectReason = null;

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


        /// <summary>
        /// 자동 운전 진입 전에 전 체인의 설정값이 확보되었는지 확인한다.
        /// </summary>
        /// <returns>모두 확보되었으면 null, 아니면 거부 사유.</returns>
        /// <remarks>
        /// 레시피에 센서가 빠져 있으면 그 체인은 제어되지 않는다. 그 상태로 자동 운전에
        /// 들어가면 일부 통로만 제어되면서 화면은 정상으로 보인다.
        /// 진입 전에 막는 편이 낫다.
        /// </remarks>
        private string CheckSettingsAvailable()
        {
            List<string> missing = new List<string>();

            foreach (ChainRuntime runtime in _runtimes)
            {
                if (!runtime.Definition.Enabled)
                {
                    continue;
                }

                string sensorId = ResolveSensorId(runtime.Definition);

                if (_config.GetSetting(sensorId, _config.ActiveMode) == null)
                {
                    missing.Add(sensorId);
                }
            }

            if (missing.Count == 0)
            {
                return null;
            }

            return string.Format(
                CultureInfo.InvariantCulture,
                "레시피에 설정값이 없는 센서가 있어 해당 체인을 제어할 수 없습니다: {0}",
                string.Join(", ", missing.ToArray()));
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

        /// <summary>문자열 목록을 배열로 만든다.</summary>
        /// <param name="items">문자열 목록.</param>
        /// <returns>배열.</returns>
        private static string[] ToArray(IList<string> items)
        {
            string[] result = new string[items.Count];
            items.CopyTo(result, 0);
            return result;
        }
    }
}
