using System;
using System.Collections.Generic;
using System.Threading.Tasks;
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
        public void AutoControl에서_Stop은_Idle로_가고_AutoStopRequested는_Ready로_간다()
        {
            // ★ 두 트리거를 구분하지 않아 종료가 Ready 에서 멈추던 결함의 회귀 방지.
            //
            // Ready 로 남으면 다음 기동에서 Start 트리거가 무시되고
            // 초기화·원점 복귀를 건너뛴 채 재개된다. 밸브의 기계적 원점이
            // 미확정인 상태로 운전하는 것이다.
            //
            // 이 구분을 단정하는 테스트가 없어서, EsamRuntime.Stop 의 문서가
            // "Idle 복귀" 라고 적어 둔 채 실제로는 Ready 에 머물러 있었다.

            // 자동 제어만 끈다 → 대기 상태로 남는다
            SystemStateMachine auto = Create();
            DriveTo(auto, SystemPhase.AutoControl);
            Assert.Equal(SystemPhase.AutoControl, auto.Phase);

            Assert.True(auto.Fire(SystemTrigger.AutoStopRequested));
            Assert.Equal(SystemPhase.Ready, auto.Phase);

            // 운전을 종료한다 → 처음부터 다시 시작해야 한다
            SystemStateMachine stop = Create();
            DriveTo(stop, SystemPhase.AutoControl);

            Assert.True(stop.Fire(SystemTrigger.Stop));
            Assert.Equal(SystemPhase.Idle, stop.Phase);

            // Idle 에서는 Start 가 받아들여진다. Ready 였다면 무시된다.
            Assert.True(stop.Fire(SystemTrigger.Start));
            Assert.Equal(SystemPhase.Init, stop.Phase);
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

        // ── S5: 인터록 트리거 소실 방지 (D2) ────────────────────────────────────

        [Theory]
        [InlineData(SystemPhase.Init)]
        [InlineData(SystemPhase.ValveHoming)]
        [InlineData(SystemPhase.Ready)]
        [InlineData(SystemPhase.AutoControl)]
        [InlineData(SystemPhase.Fault)]
        public void 인터록은_어느_단계에서_발동해도_수용된다(SystemPhase startPhase)
        {
            // ★ 회귀 방지.
            // 종전에는 Ready 와 AutoControl 에서만 처리했다. 원점 복귀 중에 EMO 를 누르면
            // 전이가 무시되고, InterlockGuard 는 엣지를 이미 소비해 다시 시도하지 않았다.
            // 액추에이터는 강제 정지 중인데 단계는 ValveHoming 에 남아 화면에 인터록이 뜨지 않았다.
            SystemStateMachine sm = Create();
            DriveTo(sm, startPhase);

            Assert.True(sm.Fire(SystemTrigger.InterlockRaised));
            Assert.Equal(SystemPhase.Interlocked, sm.Phase);
        }

        [Fact]
        public void SafeStop_중에는_인터록_발동이_상태를_낮추지_않는다()
        {
            // SafeStop 이 Interlocked 보다 상위다. 물리 안전장치가 동작한 뒤에는
            // 원점 복귀를 다시 거쳐야 하므로 Interlocked 로 내려가면 안 된다.
            SystemStateMachine sm = Create();
            sm.Fire(SystemTrigger.SafeStopRaised);

            Assert.False(sm.Fire(SystemTrigger.InterlockRaised));
            Assert.Equal(SystemPhase.SafeStop, sm.Phase);
        }

        // ── S5: 스레드 안전성 (D4) ──────────────────────────────────────────────

        [Fact]
        public void 자동_요청과_인터록_발동이_겹쳐도_상태가_깨지지_않는다()
        {
            // ★ 회귀 방지.
            // Fire 가 판정과 대입으로 나뉘어 있으면 두 스레드가 같은 현재 단계를 읽고
            // 서로 다른 다음 단계를 쓴다. 나중에 쓴 쪽이 이겨
            // "인터록 래치를 안은 채 AutoControl" 같은 불가능한 조합이 만들어진다.
            for (int attempt = 0; attempt < 200; attempt++)
            {
                SystemStateMachine sm = Create();
                DriveTo(sm, SystemPhase.Ready);

                Parallel.Invoke(
                    () => sm.Fire(SystemTrigger.AutoRequested),
                    () => sm.Fire(SystemTrigger.InterlockRaised));

                // 어느 쪽이 이기든 결과는 둘 중 하나여야 한다.
                Assert.True(
                    sm.Phase == SystemPhase.AutoControl || sm.Phase == SystemPhase.Interlocked,
                    "예상 밖 단계: " + sm.Phase);
            }
        }

        [Fact]
        public void 동시에_전이해도_이벤트와_최종_상태가_일치한다()
        {
            // PhaseEnteredUtc 가 다른 전이의 값과 짝지어지면
            // 원점 복귀·이동 타임아웃 판정이 그대로 틀어진다.
            SystemStateMachine sm = Create();
            DriveTo(sm, SystemPhase.Ready);

            List<SystemPhase> observed = new List<SystemPhase>();
            object gate = new object();

            sm.PhaseChanged += (sender, e) =>
            {
                lock (gate)
                {
                    observed.Add(e.To);
                }
            };

            Parallel.For(0, 100, i =>
            {
                sm.Fire(i % 2 == 0 ? SystemTrigger.AutoRequested : SystemTrigger.AutoStopRequested);
            });

            // 전이 횟수만큼만 이벤트가 발생했고, 마지막 이벤트가 최종 상태와 같아야 한다.
            lock (gate)
            {
                Assert.NotEmpty(observed);
                Assert.Equal(sm.Phase, observed[observed.Count - 1]);
            }
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
