using System;

namespace Esam.Domain.Control
{
    /// <summary>
    /// 상태 전이 발생 시 전달되는 정보.
    /// </summary>
    public sealed class PhaseChangedEventArgs : EventArgs
    {
        /// <summary>전이 전 단계.</summary>
        public SystemPhase From { get; private set; }

        /// <summary>전이 후 단계.</summary>
        public SystemPhase To { get; private set; }

        /// <summary>전이를 유발한 사건.</summary>
        public SystemTrigger Trigger { get; private set; }

        /// <summary>전이 시각(UTC).</summary>
        public DateTime OccurredUtc { get; private set; }

        /// <summary>상태 전이 정보를 생성한다.</summary>
        /// <param name="from">전이 전 단계.</param>
        /// <param name="to">전이 후 단계.</param>
        /// <param name="trigger">유발 사건.</param>
        /// <param name="occurredUtc">전이 시각(UTC).</param>
        public PhaseChangedEventArgs(
            SystemPhase from, SystemPhase to, SystemTrigger trigger, DateTime occurredUtc)
        {
            From = from;
            To = to;
            Trigger = trigger;
            OccurredUtc = occurredUtc;
        }
    }

    /// <summary>
    /// 시스템 전체 운전 단계를 관리하는 상태머신. DESIGN.md 4.1 다이어그램의 구현이다.
    /// </summary>
    /// <remarks>
    /// <para>안전 원칙 두 가지를 코드로 강제한다.</para>
    /// <list type="number">
    ///   <item><description><b>SafeStop 최우선</b>: EMO/메인 차단기 조건은 어떤 단계에서든 즉시 SafeStop 으로 전이하며,
    ///     물리 조건이 해제되기 전에는 어떤 트리거로도 빠져나갈 수 없다.</description></item>
    ///   <item><description><b>Homing 필수</b>: 밸브 드라이브가 전원 ON 후 원점 복귀를 요구하므로
    ///     Init → ValveHoming 을 거치지 않고는 Ready 에 도달할 수 없다.</description></item>
    /// </list>
    /// <para><b>이 클래스는 스레드 안전하다.</b> 당초에는 "제어 엔진 스레드에서만 조작" 을
    /// 전제했으나 실제 배선에서는 다섯 경로가 <see cref="Fire"/> 를 호출한다.
    /// 포트 워커 3스레드(인터록 발동/해제), 제어 스레드, UI 스레드(자동 요청/정지/리셋)다.</para>
    /// <para>보호하지 않으면 다음이 일어난다. 작업자가 자동 버튼을 누르는 순간 폴링 스레드가
    /// 인터록을 발동시키면, 두 스레드가 각각 <c>Ready→AutoControl</c> 과 <c>Ready→Interlocked</c> 를
    /// 결정하고 나중에 쓴 쪽이 이긴다. <b>인터록 래치를 안은 채 AutoControl 에 들어가</b>
    /// 제어기가 자동 지령을 재개하고, 인터록은 매 사이클 밸브를 닫는다.
    /// 밸브가 200ms 주기로 열렸다 닫히는 동안 화면은 정상 자동 운전으로 보인다.</para>
    /// <para><see cref="PhaseEnteredUtc"/> 가 <see cref="Phase"/> 와 다른 전이의 값으로
    /// 짝지어질 수도 있다. 원점 복귀·이동 타임아웃이 이 값을 쓰므로 그대로 오판으로 이어진다.</para>
    /// </remarks>
    public sealed class SystemStateMachine
    {
        private readonly IClock _clock;

        /// <summary>상태 보호용 락. 다섯 경로가 동시에 접근한다.</summary>
        private readonly object _gate = new object();

        private SystemPhase _phase;
        private DateTime _phaseEnteredUtc;

        /// <summary>현재 운전 단계.</summary>
        public SystemPhase Phase
        {
            get { lock (_gate) { return _phase; } }
        }

        /// <summary>현재 단계에 진입한 시각(UTC).</summary>
        public DateTime PhaseEnteredUtc
        {
            get { lock (_gate) { return _phaseEnteredUtc; } }
        }

        /// <summary>자동 제어가 활성화되어 있는지 여부.</summary>
        public bool IsAutoEnabled
        {
            get { lock (_gate) { return _phase == SystemPhase.AutoControl; } }
        }

        /// <summary>액추에이터 지령을 내도 되는 단계인지 여부.</summary>
        public bool CanCommandActuators
        {
            get
            {
                lock (_gate)
                {
                    return _phase == SystemPhase.ValveHoming
                           || _phase == SystemPhase.Ready
                           || _phase == SystemPhase.AutoControl;
                }
            }
        }

        /// <summary>상태 전이가 발생하면 발생하는 이벤트.</summary>
        public event EventHandler<PhaseChangedEventArgs> PhaseChanged;

        /// <summary>상태머신을 생성한다.</summary>
        /// <param name="clock">시각 제공자.</param>
        /// <exception cref="ArgumentNullException">시각 제공자가 null 일 때.</exception>
        public SystemStateMachine(IClock clock)
        {
            if (clock == null)
            {
                throw new ArgumentNullException("clock");
            }

            _clock = clock;
            _phase = SystemPhase.Idle;
            _phaseEnteredUtc = clock.UtcNow;
        }

        /// <summary>현재 단계에 머문 시간을 반환한다.</summary>
        /// <returns>경과 시간.</returns>
        public TimeSpan GetElapsedInPhase()
        {
            DateTime entered;

            lock (_gate)
            {
                entered = _phaseEnteredUtc;
            }

            return _clock.UtcNow - entered;
        }

        /// <summary>트리거를 처리해 상태를 전이시킨다.</summary>
        /// <param name="trigger">발생한 사건.</param>
        /// <returns>실제로 전이가 일어났으면 true, 무시되었으면 false.</returns>
        public bool Fire(SystemTrigger trigger)
        {
            SystemPhase previous;
            SystemPhase next;
            DateTime enteredUtc;

            // 판정과 대입이 하나의 원자 단위여야 한다.
            // 나눠 놓으면 두 스레드가 같은 현재 단계를 읽고 서로 다른 다음 단계를 쓴다.
            lock (_gate)
            {
                previous = _phase;
                next = Resolve(_phase, trigger);

                if (next == _phase)
                {
                    return false;
                }

                enteredUtc = _clock.UtcNow;
                _phase = next;
                _phaseEnteredUtc = enteredUtc;
            }

            // 이벤트는 락 밖에서 발생시킨다.
            // 구독자가 Fire 를 다시 호출하면(예: 인터록 해제 → 자동 재요청)
            // 락 안에서는 재진입이 되어 상태가 꼬이거나, 다른 락을 잡으면 교착이 된다.
            EventHandler<PhaseChangedEventArgs> handler = PhaseChanged;

            if (handler != null)
            {
                handler(this, new PhaseChangedEventArgs(previous, next, trigger, enteredUtc));
            }

            return true;
        }

        /// <summary>
        /// 현재 단계와 트리거로부터 다음 단계를 결정한다.
        /// 정의되지 않은 조합은 현재 단계를 그대로 반환해 무시한다(예외를 던지지 않음).
        /// 운전 중 예상 밖 트리거로 프로그램이 중단되는 것보다 무시가 안전하기 때문이다.
        /// </summary>
        /// <param name="current">현재 단계.</param>
        /// <param name="trigger">트리거.</param>
        /// <returns>다음 단계.</returns>
        private static SystemPhase Resolve(SystemPhase current, SystemTrigger trigger)
        {
            // ── 최우선 규칙: 비상정지는 모든 단계를 무시하고 즉시 적용된다 ──────────
            if (trigger == SystemTrigger.SafeStopRaised)
            {
                return SystemPhase.SafeStop;
            }

            // SafeStop 단계에서는 해제 트리거만 받아들인다.
            if (current == SystemPhase.SafeStop)
            {
                return trigger == SystemTrigger.SafeStopCleared ? SystemPhase.Fault : SystemPhase.SafeStop;
            }

            // ── 차우선 규칙: 치명 장애도 대부분의 단계에서 즉시 적용된다 ────────────
            if (trigger == SystemTrigger.FaultRaised)
            {
                return SystemPhase.Fault;
            }

            // ── 인터록은 어느 단계에서 발동하든 받아들인다 ──────────────────────────
            // 종전에는 Ready 와 AutoControl 에서만 처리했다. 그런데 인터록은 원점 복귀 중이나
            // 초기화 중에도 발동할 수 있고, 오히려 그때가 밸브가 움직이는 구간이라 더 위험하다.
            // 무시하면 액추에이터 정지 지령은 나가는데 단계는 그대로여서
            // 상태머신과 화면은 "인터록 없음" 으로 보인다.
            if (trigger == SystemTrigger.InterlockRaised)
            {
                return SystemPhase.Interlocked;
            }

            switch (current)
            {
                case SystemPhase.Idle:
                    return trigger == SystemTrigger.Start ? SystemPhase.Init : current;

                case SystemPhase.Init:
                    if (trigger == SystemTrigger.InitCompleted)
                    {
                        return SystemPhase.ValveHoming;
                    }

                    return trigger == SystemTrigger.Stop ? SystemPhase.Idle : current;

                case SystemPhase.ValveHoming:
                    if (trigger == SystemTrigger.HomingCompleted)
                    {
                        return SystemPhase.Ready;
                    }

                    return trigger == SystemTrigger.Stop ? SystemPhase.Idle : current;

                case SystemPhase.Ready:
                    if (trigger == SystemTrigger.AutoRequested)
                    {
                        return SystemPhase.AutoControl;
                    }

                    return trigger == SystemTrigger.Stop ? SystemPhase.Idle : current;

                case SystemPhase.AutoControl:
                    // 두 트리거를 구분한다. 구분하지 않으면 종료가 Ready 에서 멈춘다.
                    //
                    //   AutoStopRequested — 자동 제어만 끈다. 장비는 대기 상태로 남는다
                    //   Stop             — 운전을 종료한다. 처음부터 다시 시작해야 한다
                    //
                    // 종전에는 둘 다 Ready 로 보냈다. 그러면 프로그램을 종료했다가
                    // 다시 켰을 때 단계가 Ready 로 남아 Start 트리거가 무시되고,
                    // 초기화와 원점 복귀를 건너뛴 채 재개된다.
                    // 밸브의 기계적 원점이 미확정인 상태로 운전하는 것이다.
                    if (trigger == SystemTrigger.AutoStopRequested)
                    {
                        return SystemPhase.Ready;
                    }

                    return trigger == SystemTrigger.Stop ? SystemPhase.Idle : current;

                case SystemPhase.Interlocked:
                    // 인터록 해제 후에도 자동으로 운전에 복귀하지 않는다.
                    // 원인 확인 없이 재가동되는 것을 막기 위해 Ready 로만 복귀시킨다.
                    if (trigger == SystemTrigger.InterlockCleared)
                    {
                        return SystemPhase.Ready;
                    }

                    return trigger == SystemTrigger.Stop ? SystemPhase.Idle : current;

                case SystemPhase.Fault:
                    // Reset 후에는 반드시 Init 부터 다시 시작한다(밸브 원점 재확인 목적).
                    if (trigger == SystemTrigger.ResetRequested)
                    {
                        return SystemPhase.Init;
                    }

                    return trigger == SystemTrigger.Stop ? SystemPhase.Idle : current;

                default:
                    return current;
            }
        }
    }
}
