using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Esam.Communication.Abstractions;
using Esam.Communication.Configuration;
using Esam.Communication.Diagnostics;
using Esam.Domain;
using Esam.Domain.Control;

namespace Esam.Communication.Polling
{
    /// <summary>
    /// 포트 1개를 담당하는 폴링 워커. 포트당 전용 스레드 1개로 동작한다.
    /// </summary>
    /// <remarks>
    /// <para><b>왜 포트당 1스레드인가</b>: RS-485 는 반이중이라 한 버스에서 두 트랜잭션이
    /// 겹치면 프레임이 깨진다. 전송 계층도 세마포어로 직렬화하지만, 애초에 스레드를
    /// 하나만 두면 경쟁이 발생하지 않는다. 포트가 서로 다르면 완전히 병렬로 돌아
    /// 전체 사이클은 가장 느린 포트가 결정한다(DESIGN.md 2.2 C).</para>
    /// <para><b>티어 스케줄링</b>: Fast 는 매 사이클, Medium/Slow 는 경과 시간이
    /// 설정 주기를 넘겼을 때만 읽는다. 이것이 버스 부하를 줄이는 핵심이다.</para>
    /// <para><b>지령 선점</b>: 사이클 시작 시 인터록 지령을 먼저 처리한다.
    /// 읽기 도중 인터록이 들어오면 남은 읽기를 중단하고 즉시 실행한다.
    /// 안전 지령이 폴링 한 바퀴를 기다리면 최대 수백 ms 가 지연되기 때문이다.</para>
    /// <para><b>결과 발행</b>: 스냅샷을 조립하지 않고 <see cref="PollCompleted"/> 이벤트로
    /// 수집값 집합만 내보낸다. 조립은 상위 DataStore(S4)의 책임이다.</para>
    /// </remarks>
    public sealed class ModbusPortWorker : IDisposable
    {
        private readonly IModbusTransport _transport;
        private readonly IList<DeviceRuntime> _devices;
        private readonly PollingTierPeriods _periods;
        private readonly ICommandTranslator _translator;
        private readonly IClock _clock;
        private readonly CommandQueue _commands = new CommandQueue();
        private readonly Dictionary<string, DeviceRuntime> _deviceIndex;
        private readonly List<PollingTier> _tierBuffer = new List<PollingTier>(3);
        private readonly List<GroupReadResult> _resultBuffer = new List<GroupReadResult>();

        // 티어 스케줄링은 Stopwatch 대신 IClock 기준 시각으로 관리한다.
        // Stopwatch 는 Start() 를 거치지 않으면 경과시간이 0 이라
        // 기동 직후 Medium/Slow 를 한 번도 읽지 않는 문제가 생긴다.
        // MinValue 로 두면 "아직 읽은 적 없음" = 즉시 읽어야 함 이 자연스럽게 표현된다.
        private DateTime _lastMediumPollUtc = DateTime.MinValue;
        private DateTime _lastSlowPollUtc = DateTime.MinValue;

        private CancellationTokenSource _cancellation;
        private Task _loopTask;
        private bool _disposed;

        /// <summary>포트 ID.</summary>
        public string PortId { get; private set; }

        /// <summary>이 포트의 통신 품질 통계.</summary>
        public PortStatistics Statistics { get; private set; }

        /// <summary>워커가 실행 중인지 여부.</summary>
        public bool IsRunning
        {
            get
            {
                Task task = _loopTask;
                return task != null && !task.IsCompleted;
            }
        }

        /// <summary>이 포트에 등록된 디바이스 수.</summary>
        public int DeviceCount
        {
            get { return _devices.Count; }
        }

        /// <summary>대기 중인 지령 개수.</summary>
        public int PendingCommandCount
        {
            get { return _commands.Count; }
        }

        /// <summary>폴링 사이클 1회가 끝날 때 발생한다.</summary>
        public event EventHandler<PollCompletedEventArgs> PollCompleted;

        /// <summary>지령 실행이 실패했을 때 발생한다. 알람 판정과 로그에 사용한다.</summary>
        public event EventHandler<CommandFailedEventArgs> CommandFailed;

        /// <summary>지령이 끝까지 전송되었을 때 발생한다. 실패 연속 횟수를 되돌리는 데 쓴다.</summary>
        public event EventHandler<CommandCompletedEventArgs> CommandCompleted;

        /// <summary>포트 워커를 생성한다.</summary>
        /// <param name="portId">포트 ID.</param>
        /// <param name="transport">전송 계층(실장비 또는 시뮬레이션).</param>
        /// <param name="devices">이 포트에 속한 디바이스 런타임 목록.</param>
        /// <param name="periods">폴링 티어 주기.</param>
        /// <param name="translator">지령 변환기. null 이면 기본 선언적 변환기를 사용한다.</param>
        /// <param name="clock">시각 제공자. null 이면 시스템 시계를 사용한다.</param>
        /// <exception cref="ArgumentNullException">필수 인자가 null 일 때.</exception>
        public ModbusPortWorker(
            string portId,
            IModbusTransport transport,
            IList<DeviceRuntime> devices,
            PollingTierPeriods periods,
            ICommandTranslator translator,
            IClock clock)
        {
            if (transport == null)
            {
                throw new ArgumentNullException("transport");
            }

            if (devices == null)
            {
                throw new ArgumentNullException("devices");
            }

            PortId = portId;
            _transport = transport;
            _devices = devices;
            _periods = periods ?? new PollingTierPeriods();
            _translator = translator ?? new DeclarativeCommandTranslator();
            _clock = clock ?? SystemClock.Instance;
            Statistics = new PortStatistics(portId);

            _deviceIndex = new Dictionary<string, DeviceRuntime>(StringComparer.OrdinalIgnoreCase);
            foreach (DeviceRuntime device in devices)
            {
                if (device != null && !string.IsNullOrEmpty(device.DeviceId))
                {
                    _deviceIndex[device.DeviceId] = device;
                }
            }
        }

        /// <summary>액추에이터 지령을 큐에 넣는다. 어느 스레드에서든 호출할 수 있다.</summary>
        /// <param name="command">지령.</param>
        public void EnqueueCommand(ActuatorCommand command)
        {
            _commands.Enqueue(command);
        }

        /// <summary>여러 지령을 큐에 넣는다.</summary>
        /// <param name="commands">지령 목록.</param>
        public void EnqueueCommands(IEnumerable<ActuatorCommand> commands)
        {
            _commands.EnqueueRange(commands);
        }

        /// <summary>자동 제어 지령만 비운다.</summary>
        public void ClearAutomaticCommands()
        {
            _commands.ClearAutomatic();
        }

        /// <summary>폴링 루프를 시작한다.</summary>
        /// <exception cref="InvalidOperationException">이미 실행 중일 때.</exception>
        public void Start()
        {
            ThrowIfDisposed();

            if (IsRunning)
            {
                throw new InvalidOperationException(string.Format(
                    CultureInfo.InvariantCulture, "포트 {0} 워커가 이미 실행 중입니다.", PortId));
            }

            // ★ 포트를 열지 못해도 워커는 시작한다.
            //
            // 없는 COM 포트 이름은 커미셔닝에서 가장 흔한 실수다. 그것으로
            // 프로그램이 죽으면 화면을 띄워 원인을 볼 방법도, 설정을 고칠 방법도 없다.
            //
            // 열지 못하면 모든 트랜잭션이 PortError 로 실패하고, 스냅샷은 NoData 가 된다.
            // 안전 판정(IL-04)이 그 상태를 전체 정지로 다루므로 위험하지 않다.
            // 사유는 CommandFailed 로 알린다.
            TryOpenTransport();

            _cancellation = new CancellationTokenSource();
            CancellationToken token = _cancellation.Token;

            // 기동 직후에는 전 티어를 한 번 읽어 초기 상태를 모두 확보한다.
            _lastMediumPollUtc = DateTime.MinValue;
            _lastSlowPollUtc = DateTime.MinValue;

            // LongRunning: 포트 전용 스레드를 확보해 스레드풀 고갈에 영향받지 않게 한다.
            _loopTask = Task.Factory.StartNew(
                () => RunLoop(token),
                token,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default);
        }

        /// <summary>
        /// 전송 계층을 연다. 실패하면 사유를 알리고 false 를 반환한다.
        /// </summary>
        /// <returns>열려 있으면 true.</returns>
        /// <remarks>
        /// 예외를 밖으로 내지 않는다. 케이블이 빠졌다가 다시 꽂히는 상황을
        /// 프로그램 재시작 없이 회복할 수 있어야 한다.
        /// </remarks>
        public bool TryOpenTransport()
        {
            if (_transport.IsOpen)
            {
                return true;
            }

            try
            {
                _transport.Open();
                return true;
            }
            catch (Exception ex)
            {
                LastOpenError = ex.Message;

                RaiseCommandFailed(new CommandFailedEventArgs(
                    PortId, null, "포트를 열 수 없습니다: " + ex.Message, _clock.UtcNow));

                return false;
            }
        }

        /// <summary>마지막 포트 열기 실패 사유. 성공했으면 null.</summary>
        public string LastOpenError { get; private set; }

        /// <summary>폴링 루프를 중지한다.</summary>
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
                // 취소로 인한 예외는 정상 종료로 간주한다.
                completed = true;
            }

            _loopTask = null;
            _cancellation = null;
            cancellation.Dispose();

            return completed;
        }

        /// <summary>
        /// 폴링 사이클 1회를 수행한다. 테스트에서 스레드 없이 직접 호출할 수 있다.
        /// </summary>
        /// <param name="token">취소 토큰.</param>
        /// <returns>이 사이클의 결과.</returns>
        public PollCompletedEventArgs ExecuteCycle(CancellationToken token)
        {
            DateTime startedUtc = _clock.UtcNow;
            Stopwatch cycleWatch = Stopwatch.StartNew();

            _tierBuffer.Clear();
            _resultBuffer.Clear();

            // 취소된 상태에서는 티어 타임스탬프를 건드리지 않고 즉시 반환한다.
            // 여기서 갱신해 버리면 실제로 읽지도 않은 Medium/Slow 주기가 소진된다.
            if (token.IsCancellationRequested)
            {
                cycleWatch.Stop();
                return new PollCompletedEventArgs(
                    PortId, startedUtc, cycleWatch.Elapsed.TotalMilliseconds, _tierBuffer, _resultBuffer);
            }

            // ── 1. 지령 처리 (인터록 우선) ────────────────────────────────────────
            ProcessCommands(token);

            // ── 2. 읽을 티어 결정 ─────────────────────────────────────────────────
            _tierBuffer.Add(PollingTier.Fast);

            bool mediumDue = IsTierDue(_lastMediumPollUtc, _periods.MediumMs, startedUtc);
            bool slowDue = IsTierDue(_lastSlowPollUtc, _periods.SlowMs, startedUtc);

            if (mediumDue)
            {
                _tierBuffer.Add(PollingTier.Medium);
            }

            if (slowDue)
            {
                _tierBuffer.Add(PollingTier.Slow);
            }

            // ── 3. 읽기 수행 ──────────────────────────────────────────────────────
            bool completedAllReads = true;

            foreach (DeviceRuntime device in _devices)
            {
                if (token.IsCancellationRequested)
                {
                    completedAllReads = false;
                    break;
                }

                foreach (PreparedReadGroup group in device.ReadGroups)
                {
                    if (token.IsCancellationRequested)
                    {
                        completedAllReads = false;
                        break;
                    }

                    if (!_tierBuffer.Contains(group.Definition.Tier))
                    {
                        continue;
                    }

                    ModbusResponse response = _transport.Execute(group.Request, token);
                    Statistics.Record(response);

                    _resultBuffer.Add(device.Decode(group, response, _clock.UtcNow));

                    // 읽기 중 인터록이 들어오면 남은 읽기를 미루고 즉시 처리한다.
                    if (_commands.HasInterlockCommand)
                    {
                        ProcessCommands(token);
                    }
                }
            }

            // 티어 주기는 실제로 다 읽었을 때만 소진시킨다.
            // 중간에 취소된 사이클에서 갱신해 버리면 읽지도 않은 Medium/Slow 주기를 낭비한다.
            if (completedAllReads)
            {
                if (mediumDue)
                {
                    _lastMediumPollUtc = startedUtc;
                }

                if (slowDue)
                {
                    _lastSlowPollUtc = startedUtc;
                }
            }

            cycleWatch.Stop();
            double cycleMs = cycleWatch.Elapsed.TotalMilliseconds;
            Statistics.RecordCycle(cycleMs);

            PollCompletedEventArgs args = new PollCompletedEventArgs(
                PortId, startedUtc, cycleMs, _tierBuffer, _resultBuffer);

            RaisePollCompleted(args);
            return args;
        }

        /// <summary>해당 티어를 이번 사이클에 읽어야 하는지 판정한다.</summary>
        /// <param name="lastPollUtc">마지막으로 읽은 시각. <see cref="DateTime.MinValue"/> 이면 미수집.</param>
        /// <param name="periodMs">티어 주기 [ms].</param>
        /// <param name="nowUtc">현재 시각(UTC).</param>
        /// <returns>읽어야 하면 true.</returns>
        private static bool IsTierDue(DateTime lastPollUtc, int periodMs, DateTime nowUtc)
        {
            if (lastPollUtc == DateTime.MinValue)
            {
                return true;
            }

            return (nowUtc - lastPollUtc).TotalMilliseconds >= periodMs;
        }

        /// <summary>폴링 루프 본체.</summary>
        /// <param name="token">취소 토큰.</param>
        private void RunLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                Stopwatch iteration = Stopwatch.StartNew();

                // 닫혀 있으면 매 사이클 다시 열어 본다.
                // 케이블을 다시 꽂거나 변환기 전원이 돌아왔을 때
                // 프로그램을 재시작하지 않고 회복해야 한다.
                if (!_transport.IsOpen)
                {
                    TryOpenTransport();
                }

                try
                {
                    ExecuteCycle(token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    // 폴링 루프는 어떤 예외로도 죽지 않아야 한다.
                    // 루프가 멈추면 통신이 완전히 끊기고, 상위는 그것조차 알 수 없다.
                    RaiseCommandFailed(new CommandFailedEventArgs(
                        PortId, null, "폴링 사이클에서 예외 발생: " + ex.Message, _clock.UtcNow));
                }

                // Fast 주기를 맞추기 위해 남은 시간만큼 대기한다.
                double remainingMs = _periods.FastMs - iteration.Elapsed.TotalMilliseconds;

                if (remainingMs > 1.0)
                {
                    try
                    {
                        // 취소 요청에 즉시 반응하도록 WaitHandle 로 대기한다.
                        token.WaitHandle.WaitOne((int)remainingMs);
                    }
                    catch (ObjectDisposedException)
                    {
                        break;
                    }
                }
            }
        }

        /// <summary>대기 중인 지령을 우선순위 순으로 실행한다.</summary>
        /// <param name="token">취소 토큰.</param>
        private void ProcessCommands(CancellationToken token)
        {
            ActuatorCommand command;

            // 이 사이클에 처리할 지령 수의 상한.
            //
            // ★ 이 상한이 없으면 폴링 사이클이 끝나지 않을 수 있다.
            // 지령 실패는 상위에서 장애로 escalation 되고, 그 처리가 다시
            // 안전 지령을 투입한다. 그 지령이 또 실패하면 되먹임이 성립한다.
            //
            //   지령 실패 → 장애 보고 → 파킹 지령 투입 → 지령 실패 → …
            //
            // 큐에서 꺼내는 순서로 재귀가 아니므로 스택 가드로는 막지 못한다.
            // 그리고 이 루프가 끝나지 않으면 읽기도, 인터록 판정도, 화면 갱신도
            // 영구히 멈춘다. 통신 자체가 죽는 것과 같다.
            //
            // 남긴 지령은 버리지 않는다. 큐에 그대로 남아 다음 사이클에 처리되고,
            // 인터록 지령은 발동이 지속되는 동안 재투입되므로 유실되지 않는다.
            int budget = (_devices.Count * 4) + 8;

            // 취소를 꺼낸 뒤에 확인하면 그 지령이 조용히 버려진다.
            // 인터록 지령이라면 안전 지령 유실이므로 반드시 꺼내기 전에 확인한다.
            while (!token.IsCancellationRequested && _commands.TryDequeue(out command))
            {
                if (--budget < 0)
                {
                    // 상한에 걸린 사실 자체가 이상 신호다. 조용히 넘기지 않는다.
                    RaiseCommandFailed(new CommandFailedEventArgs(
                        PortId, command,
                        string.Format(CultureInfo.InvariantCulture,
                            "이번 사이클의 지령 처리 상한을 초과했습니다(대기 {0}건). "
                            + "지령이 실행보다 빠르게 쌓이고 있습니다.",
                            _commands.Count + 1),
                        _clock.UtcNow));

                    // 꺼낸 지령을 되돌린다. 버리면 안전 지령이 유실된다.
                    _commands.Enqueue(command);
                    return;
                }

                DeviceRuntime device;
                if (!_deviceIndex.TryGetValue(command.DeviceId, out device))
                {
                    RaiseCommandFailed(new CommandFailedEventArgs(
                        PortId, command,
                        string.Format(CultureInfo.InvariantCulture,
                            "대상 디바이스 '{0}' 가 이 포트에 없습니다.", command.DeviceId),
                        _clock.UtcNow));
                    continue;
                }

                IList<ModbusRequest> requests;
                string reason;

                if (!_translator.TryTranslate(command, device, out requests, out reason))
                {
                    RaiseCommandFailed(new CommandFailedEventArgs(
                        PortId, command, reason, _clock.UtcNow));
                    continue;
                }

                ExecuteCommandSequence(command, requests, token);
            }
        }

        /// <summary>
        /// 지령 시퀀스를 순서대로 실행한다. 중간에 실패하면 남은 단계를 중단한다.
        /// </summary>
        /// <param name="command">원본 지령.</param>
        /// <param name="requests">요청 시퀀스.</param>
        /// <param name="token">취소 토큰.</param>
        private void ExecuteCommandSequence(
            ActuatorCommand command, IList<ModbusRequest> requests, CancellationToken token)
        {
            for (int i = 0; i < requests.Count; i++)
            {
                ModbusResponse response = _transport.Execute(requests[i], token);
                Statistics.Record(response);

                if (response.IsSuccess)
                {
                    continue;
                }

                // 밸브는 "위치 설정 → Move" 2단계다. 1단계가 실패한 상태에서 2단계를 보내면
                // 이전에 남아 있던 위치값으로 이동해 버린다. 반드시 중단해야 한다.
                RaiseCommandFailed(new CommandFailedEventArgs(
                    PortId, command,
                    string.Format(CultureInfo.InvariantCulture,
                        "지령 시퀀스 {0}/{1} 단계 실패: {2} ({3})",
                        i + 1, requests.Count, response.FailureKind, response.FailureDetail),
                    _clock.UtcNow));

                return;
            }

            // 전 단계가 통했다. 상위가 실패 연속 횟수를 되돌릴 수 있도록 알린다.
            RaiseCommandCompleted(new CommandCompletedEventArgs(PortId, command, _clock.UtcNow));
        }

        /// <summary>지령 완료 이벤트를 발생시킨다.</summary>
        /// <param name="args">이벤트 인자.</param>
        private void RaiseCommandCompleted(CommandCompletedEventArgs args)
        {
            EventHandler<CommandCompletedEventArgs> handler = CommandCompleted;
            if (handler == null)
            {
                return;
            }

            try
            {
                handler(this, args);
            }
            catch (Exception)
            {
                // 상동. 구독자의 예외가 폴링 루프를 죽여서는 안 된다.
            }
        }

        /// <summary>폴링 완료 이벤트를 발생시킨다.</summary>
        /// <param name="args">이벤트 인자.</param>
        private void RaisePollCompleted(PollCompletedEventArgs args)
        {
            EventHandler<PollCompletedEventArgs> handler = PollCompleted;
            if (handler == null)
            {
                return;
            }

            try
            {
                handler(this, args);
            }
            catch (Exception)
            {
                // 구독자의 예외가 폴링 루프를 멈추게 해서는 안 된다.
            }
        }

        /// <summary>지령 실패 이벤트를 발생시킨다.</summary>
        /// <param name="args">이벤트 인자.</param>
        private void RaiseCommandFailed(CommandFailedEventArgs args)
        {
            EventHandler<CommandFailedEventArgs> handler = CommandFailed;
            if (handler == null)
            {
                return;
            }

            try
            {
                handler(this, args);
            }
            catch (Exception)
            {
                // 상동.
            }
        }

        /// <summary>객체가 해제되었는지 확인한다.</summary>
        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException("ModbusPortWorker");
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
    }

    /// <summary>지령 실행 실패 정보.</summary>
    public sealed class CommandFailedEventArgs : EventArgs
    {
        /// <summary>포트 ID.</summary>
        public string PortId { get; private set; }

        /// <summary>실패한 지령. 폴링 자체의 예외이면 null.</summary>
        public ActuatorCommand Command { get; private set; }

        /// <summary>실패 사유.</summary>
        public string Reason { get; private set; }

        /// <summary>발생 시각(UTC).</summary>
        public DateTime OccurredUtc { get; private set; }

        /// <summary>지령 실패 정보를 생성한다.</summary>
        /// <param name="portId">포트 ID.</param>
        /// <param name="command">실패한 지령(null 허용).</param>
        /// <param name="reason">실패 사유.</param>
        /// <param name="occurredUtc">발생 시각(UTC).</param>
        public CommandFailedEventArgs(
            string portId, ActuatorCommand command, string reason, DateTime occurredUtc)
        {
            PortId = portId;
            Command = command;
            Reason = reason;
            OccurredUtc = occurredUtc;
        }
    }

    /// <summary>지령이 끝까지 전송된 사실을 알린다.</summary>
    /// <remarks>
    /// <para>실패만 알리면 상위는 <b>복구를 알 수 없다.</b> 실패 연속 횟수가
    /// 임계를 넘으면 장애로 보고하는데, 그 뒤 장치가 되살아나도 카운터가
    /// 내려가지 않으면 다음 장애를 새 장애로 구분할 수 없다.</para>
    /// <para>성공을 알리는 이벤트가 필요한 이유가 이것이다.
    /// 전송 성공은 "액추에이터를 움직일 수단이 살아 있다" 는 뜻이다.</para>
    /// </remarks>
    public sealed class CommandCompletedEventArgs : EventArgs
    {
        /// <summary>포트 ID.</summary>
        public string PortId { get; private set; }

        /// <summary>전송된 지령.</summary>
        public ActuatorCommand Command { get; private set; }

        /// <summary>완료 시각(UTC).</summary>
        public DateTime OccurredUtc { get; private set; }

        /// <summary>지령 완료 정보를 생성한다.</summary>
        /// <param name="portId">포트 ID.</param>
        /// <param name="command">전송된 지령.</param>
        /// <param name="occurredUtc">완료 시각(UTC).</param>
        public CommandCompletedEventArgs(
            string portId, ActuatorCommand command, DateTime occurredUtc)
        {
            PortId = portId;
            Command = command;
            OccurredUtc = occurredUtc;
        }
    }
}
