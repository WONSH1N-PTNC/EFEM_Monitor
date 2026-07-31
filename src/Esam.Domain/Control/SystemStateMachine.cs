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
    /// <para>이 클래스는 스레드 안전하지 않다. 제어 엔진 스레드에서만 조작한다.</para>
    /// </remarks>
    public sealed class SystemStateMachine
    {
        private readonly IClock _clock;

        /// <summary>현재 운전 단계.</summary>
        public SystemPhase Phase { get; private set; }

        /// <summary>현재 단계에 진입한 시각(UTC).</summary>
        public DateTime PhaseEnteredUtc { get; private set; }

        /// <summary>자동 제어가 활성화되어 있는지 여부.</summary>
        public bool IsAutoEnabled
        {
            get { return Phase == SystemPhase.AutoControl; }
        }

        /// <summary>액추에이터 지령을 내도 되는 단계인지 여부.</summary>
        public bool CanCommandActuators
        {
            get
            {
                return Phase == SystemPhase.ValveHoming
                       || Phase == SystemPhase.Ready
                       || Phase == SystemPhase.AutoControl;
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
            Phase = SystemPhase.Idle;
            PhaseEnteredUtc = clock.UtcNow;
        }

        /// <summary>현재 단계에 머문 시간을 반환한다.</summary>
        /// <returns>경과 시간.</returns>
        public TimeSpan GetElapsedInPhase()
        {
            return _clock.UtcNow - PhaseEnteredUtc;
        }

        /// <summary>트리거를 처리해 상태를 전이시킨다.</summary>
        /// <param name="trigger">발생한 사건.</param>
        /// <returns>실제로 전이가 일어났으면 true, 무시되었으면 false.</returns>
        public bool Fire(SystemTrigger trigger)
        {
            SystemPhase next = Resolve(Phase, trigger);

            if (next == Phase)
            {
                return false;
            }

            SystemPhase previous = Phase;
            Phase = next;
            PhaseEnteredUtc = _clock.UtcNow;

            EventHandler<PhaseChangedEventArgs> handler = PhaseChanged;
            if (handler != null)
            {
                handler(this, new PhaseChangedEventArgs(previous, next, trigger, PhaseEnteredUtc));
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

                    if (trigger == SystemTrigger.InterlockRaised)
                    {
                        return SystemPhase.Interlocked;
                    }

                    return trigger == SystemTrigger.Stop ? SystemPhase.Idle : current;

                case SystemPhase.AutoControl:
                    if (trigger == SystemTrigger.AutoStopRequested || trigger == SystemTrigger.Stop)
                    {
                        return SystemPhase.Ready;
                    }

                    return trigger == SystemTrigger.InterlockRaised ? SystemPhase.Interlocked : current;

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
