using Esam.Domain.Control;
using Xunit;

namespace Esam.Tests
{
    /// <summary>
    /// DESIGN.md 4.1 상태머신의 전이 규칙과 두 가지 안전 원칙을 검증한다.
    /// </summary>
    public class SystemStateMachineTests
    {
        private static SystemStateMachine Create()
        {
            return new SystemStateMachine(new FakeClock(Build.T0));
        }

        [Fact]
        public void 초기_상태는_Idle이다()
        {
            Assert.Equal(SystemPhase.Idle, Create().Phase);
        }

        [Fact]
        public void 정상_기동_경로는_Idle_Init_Homing_Ready_Auto_순서이다()
        {
            SystemStateMachine sm = Create();

            Assert.True(sm.Fire(SystemTrigger.Start));
            Assert.Equal(SystemPhase.Init, sm.Phase);

            Assert.True(sm.Fire(SystemTrigger.InitCompleted));
            Assert.Equal(SystemPhase.ValveHoming, sm.Phase);

            Assert.True(sm.Fire(SystemTrigger.HomingCompleted));
            Assert.Equal(SystemPhase.Ready, sm.Phase);

            Assert.True(sm.Fire(SystemTrigger.AutoRequested));
            Assert.Equal(SystemPhase.AutoControl, sm.Phase);
            Assert.True(sm.IsAutoEnabled);
        }

        [Fact]
        public void Homing을_거치지_않고는_Ready에_도달할_수_없다()
        {
            // 밸브 드라이브가 전원 ON 후 원점 복귀를 요구하므로 이 순서는 강제되어야 한다.
            SystemStateMachine sm = Create();
            sm.Fire(SystemTrigger.Start);
            sm.Fire(SystemTrigger.InitCompleted);

            Assert.Equal(SystemPhase.ValveHoming, sm.Phase);

            // Homing 완료 없이 자동 운전을 요청해도 무시된다.
            Assert.False(sm.Fire(SystemTrigger.AutoRequested));
            Assert.Equal(SystemPhase.ValveHoming, sm.Phase);
        }

        [Theory]
        [InlineData(SystemPhase.Idle)]
        [InlineData(SystemPhase.Init)]
        [InlineData(SystemPhase.Ready)]
        [InlineData(SystemPhase.AutoControl)]
        [InlineData(SystemPhase.Fault)]
        public void SafeStop은_어떤_단계에서도_즉시_적용된다(SystemPhase startPhase)
        {
            SystemStateMachine sm = Create();
            DriveTo(sm, startPhase);

            Assert.True(sm.Fire(SystemTrigger.SafeStopRaised));
            Assert.Equal(SystemPhase.SafeStop, sm.Phase);
        }

        [Theory]
        [InlineData(SystemTrigger.Start)]
        [InlineData(SystemTrigger.AutoRequested)]
        [InlineData(SystemTrigger.ResetRequested)]
        [InlineData(SystemTrigger.Stop)]
        [InlineData(SystemTrigger.InterlockCleared)]
        public void SafeStop_상태에서는_해제_이외의_트리거를_모두_무시한다(SystemTrigger trigger)
        {
            SystemStateMachine sm = Create();
            sm.Fire(SystemTrigger.SafeStopRaised);

            Assert.False(sm.Fire(trigger));
            Assert.Equal(SystemPhase.SafeStop, sm.Phase);
        }

        [Fact]
        public void SafeStop이_해제되면_Ready가_아니라_Fault로_간다()
        {
            // 비상정지 후 자동으로 운전 가능 상태가 되면 안 된다. 원인 확인과 Reset 을 강제한다.
            SystemStateMachine sm = Create();
            sm.Fire(SystemTrigger.SafeStopRaised);

            Assert.True(sm.Fire(SystemTrigger.SafeStopCleared));
            Assert.Equal(SystemPhase.Fault, sm.Phase);
        }

        [Fact]
        public void 인터록이_해제되어도_자동운전으로_바로_복귀하지_않는다()
        {
            SystemStateMachine sm = Create();
            DriveTo(sm, SystemPhase.AutoControl);

            sm.Fire(SystemTrigger.InterlockRaised);
            Assert.Equal(SystemPhase.Interlocked, sm.Phase);

            sm.Fire(SystemTrigger.InterlockCleared);
            Assert.Equal(SystemPhase.Ready, sm.Phase);
            Assert.False(sm.IsAutoEnabled);
        }

        [Fact]
        public void Fault에서_Reset하면_Init부터_다시_시작한다()
        {
            // 밸브 원점을 다시 확인해야 하므로 Ready 로 직행하지 않는다.
            SystemStateMachine sm = Create();
            DriveTo(sm, SystemPhase.AutoControl);

            sm.Fire(SystemTrigger.FaultRaised);
            Assert.Equal(SystemPhase.Fault, sm.Phase);

            sm.Fire(SystemTrigger.ResetRequested);
            Assert.Equal(SystemPhase.Init, sm.Phase);
        }

        [Fact]
        public void Idle과_Fault에서는_액추에이터_지령을_낼_수_없다()
        {
            SystemStateMachine sm = Create();
            Assert.False(sm.CanCommandActuators);

            DriveTo(sm, SystemPhase.AutoControl);
            Assert.True(sm.CanCommandActuators);

            sm.Fire(SystemTrigger.FaultRaised);
            Assert.False(sm.CanCommandActuators);
        }

        [Fact]
        public void 전이가_발생하면_이벤트가_발생한다()
        {
            SystemStateMachine sm = Create();
            PhaseChangedEventArgs captured = null;
            sm.PhaseChanged += (sender, e) => captured = e;

            sm.Fire(SystemTrigger.Start);

            Assert.NotNull(captured);
            Assert.Equal(SystemPhase.Idle, captured.From);
            Assert.Equal(SystemPhase.Init, captured.To);
            Assert.Equal(SystemTrigger.Start, captured.Trigger);
        }

        [Fact]
        public void 정의되지_않은_전이는_예외_없이_무시된다()
        {
            SystemStateMachine sm = Create();

            // Idle 상태에서 HomingCompleted 는 의미가 없다. 예외 대신 무시가 안전하다.
            Assert.False(sm.Fire(SystemTrigger.HomingCompleted));
            Assert.Equal(SystemPhase.Idle, sm.Phase);
        }

        /// <summary>목표 단계까지 상태머신을 진행시킨다.</summary>
        private static void DriveTo(SystemStateMachine sm, SystemPhase target)
        {
            if (target == SystemPhase.Idle)
            {
                return;
            }

            sm.Fire(SystemTrigger.Start);
            if (target == SystemPhase.Init)
            {
                return;
            }

            if (target == SystemPhase.Fault)
            {
                sm.Fire(SystemTrigger.FaultRaised);
                return;
            }

            sm.Fire(SystemTrigger.InitCompleted);
            if (target == SystemPhase.ValveHoming)
            {
                return;
            }

            sm.Fire(SystemTrigger.HomingCompleted);
            if (target == SystemPhase.Ready)
            {
                return;
            }

            sm.Fire(SystemTrigger.AutoRequested);
        }
    }
}
