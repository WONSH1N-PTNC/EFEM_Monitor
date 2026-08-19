using System;
using System.Collections.Generic;
using Esam.Communication.Configuration;
using Esam.Domain.Configuration;
using Esam.Domain.Control;
using Esam.Services;
using Xunit;

namespace Esam.Tests
{
    /// <summary>
    /// 장애 단계에서 빠져나오는 경로 검증(D23).
    /// </summary>
    /// <remarks>
    /// <para>상태머신은 <c>Fault → ResetRequested → Init</c> 전이를 정의해 두었는데,
    /// <b><c>ResetRequested</c> 를 발생시키는 코드가 프로덕션에 하나도 없었다.</b>
    /// 테스트에만 있었다.</para>
    /// <para>배너의 "장애 해제" 는 SafeStop 을 <b>Fault 로 내려보내는</b> 명령이다.
    /// 도착지에서는 그 버튼이 사라진다. 결과적으로 한 번 Fault 에 들어가면
    /// 프로그램을 다시 띄우는 것 말고는 복구 수단이 없었다.</para>
    /// <para>그리고 기동할 때마다 Fault 로 들어갔다 — 포트 경합 때문이다.
    /// 그 원인은 <c>InterlockEvaluatorTests</c> 의 IL-04 유예 테스트가 지킨다.</para>
    /// </remarks>
    public sealed class FaultRecoveryTests : IDisposable
    {
        private readonly List<EsamRuntime> _runtimes = new List<EsamRuntime>();

        /// <inheritdoc />
        public void Dispose()
        {
            foreach (EsamRuntime runtime in _runtimes)
            {
                try
                {
                    runtime.Stop(0);
                    runtime.Dispose();
                }
                catch (Exception)
                {
                    // 정리 실패가 테스트 결과를 바꾸면 안 된다.
                }
            }
        }

        [Fact]
        public void 장애_단계에서_기동_시퀀스를_다시_시작할_수_있다()
        {
            EsamRuntime runtime = Create();

            runtime.Engine.StateMachine.Fire(SystemTrigger.FaultRaised);
            Assert.Equal(SystemPhase.Fault, runtime.Engine.StateMachine.Phase);

            Assert.True(runtime.RequestRestart());

            // Ready 로 바로 가지 않는다. 장애 뒤에는 밸브의 기계적 원점을
            // 신뢰할 수 없으므로 Init 부터 다시 시작해 원점 복귀를 거친다.
            Assert.Equal(SystemPhase.Init, runtime.Engine.StateMachine.Phase);
        }

        [Fact]
        public void 장애가_아니면_재시작을_받아들이지_않는다()
        {
            EsamRuntime runtime = Create();

            Assert.Equal(SystemPhase.Idle, runtime.Engine.StateMachine.Phase);
            Assert.False(runtime.RequestRestart());
            Assert.Equal(SystemPhase.Idle, runtime.Engine.StateMachine.Phase);
        }

        [Fact]
        public void 비상정지_상태에서는_재시작을_받아들이지_않는다()
        {
            // 물리 조건이 남아 있을 수 있다. 그 해제는 ResetRuntimeFault 의 몫이다.
            EsamRuntime runtime = Create();

            runtime.Engine.StateMachine.Fire(SystemTrigger.SafeStopRaised);
            Assert.Equal(SystemPhase.SafeStop, runtime.Engine.StateMachine.Phase);

            Assert.False(runtime.RequestRestart());
            Assert.Equal(SystemPhase.SafeStop, runtime.Engine.StateMachine.Phase);
        }

        [Fact]
        public void 재시작하면_다시_준비_단계까지_갈_수_있다()
        {
            // 복구가 "전이만 성공" 으로 끝나면 안 된다. Init 부터 다시 흘러
            // 사람이 운전할 수 있는 상태로 돌아와야 한다.
            EsamRuntime runtime = Create();

            runtime.Engine.StateMachine.Fire(SystemTrigger.FaultRaised);
            runtime.RequestRestart();

            runtime.Engine.StateMachine.Fire(SystemTrigger.InitCompleted);
            runtime.Engine.StateMachine.Fire(SystemTrigger.HomingCompleted);

            Assert.Equal(SystemPhase.Ready, runtime.Engine.StateMachine.Phase);
            Assert.Null(runtime.DescribeManualDenial());
        }

        [Fact]
        public void 안전경로_장애로_올린_정지는_해제하면_장애_단계로_간다()
        {
            // 이 전이가 D23 의 입구다. SafeStop 에서 해제하면 Ready 가 아니라
            // Fault 로 간다 — 의도된 설계이고, 그래서 Fault 에서 나올 길이 필요하다.
            EsamRuntime runtime = Create();

            runtime.Engine.StateMachine.Fire(SystemTrigger.SafeStopRaised);
            runtime.Engine.StateMachine.Fire(SystemTrigger.SafeStopCleared);

            Assert.Equal(SystemPhase.Fault, runtime.Engine.StateMachine.Phase);
            Assert.True(runtime.RequestRestart());
        }

        /// <summary>시뮬레이션 런타임을 조립한다(기동하지 않는다).</summary>
        /// <returns>런타임.</returns>
        private EsamRuntime Create()
        {
            ConfigLoadResult map = CommunicationConfigLoader.LoadFromFile("config/device-map.json");
            Assert.True(map.IsSuccess, "통신 구성 오류:\n" + string.Join("\n", map.Errors));

            ControlLoadResult control = ControlConfigLoader.LoadFromFile("config/control.json");
            Assert.True(control.IsSuccess, "제어 설정 오류:\n" + string.Join("\n", control.Errors));

            RuntimeOptions options = new RuntimeOptions();
            options.Transport = TransportMode.Simulation;
            options.AlarmRulesPath = "config/alarms.json";
            options.RecipePath = "config/recipe.json";

            EsamRuntime runtime = EsamRuntime.Create(map.Map, control.Config, options, null);
            _runtimes.Add(runtime);

            return runtime;
        }
    }
}
