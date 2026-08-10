using System.Collections.Generic;
using Esam.Domain.Alarms;
using Esam.Domain.Configuration;
using Esam.Domain.Control;
using Esam.Domain.Models;
using Xunit;

namespace Esam.Tests
{
    /// <summary>
    /// DESIGN.md 5.2 인터록 표(IL-01 ~ IL-05)의 동작을 검증한다.
    /// 안전 기능이므로 정상 동작뿐 아니라 "오동작하지 않는 것"도 함께 검증한다.
    /// </summary>
    public class InterlockEvaluatorTests
    {
        private static InterlockEvaluator CreateDefault()
        {
            return new InterlockEvaluator(InterlockEvaluator.CreateDefaultRules());
        }

        private static SystemSnapshot SnapshotWithSensor3(double pa, Quality quality = Quality.Good)
        {
            Dictionary<string, PressureReading> pressures = new Dictionary<string, PressureReading>();
            Dictionary<string, ValveState> valves = new Dictionary<string, ValveState>();
            Dictionary<string, FanState> fans = new Dictionary<string, FanState>();

            for (int i = 1; i <= 5; i++)
            {
                // 체인 1의 센서 3만 인자값으로 두고 나머지는 정상 운전점(-200 Pa)으로 채운다.
                double value = i == 1 ? pa : -200.0;
                pressures["S3-" + i] = Build.Pressure("S3-" + i, value, i == 1 ? quality : Quality.Good);
                valves["V-" + i] = Build.Valve("V-" + i, 2500);
                fans["F-" + i] = Build.Fan("F-" + i, 1000, 1000);
            }

            return Build.Snapshot(pressures, valves, fans);
        }

        // ── IL-01: 센서 3 상한 도달 ─────────────────────────────────────────────

        [Fact]
        public void IL01_센서3이_대기압을_넘으면_해당_체인의_밸브를_닫고_팬을_정지시킨다()
        {
            // 안전 임계값은 운전 대역과 분리된 절대값 0 Pa(대기압)이다.
            // 배기 음압을 잃으면 오염이 확산되며, 그것이 IL-01 이 막는 사건이다.
            // 운전 대역 상한(-100 Pa)을 쓰면 밸브 닫힘 상태(-50 Pa)에서도 조건이 참이 되어
            // 전원 투입 직후 래치되고 장비가 기동하지 못한다.
            InterlockEvaluation result = CreateDefault().Evaluate(
                SnapshotWithSensor3(50.0), Build.Config(), Build.T0);

            Assert.True(result.HasTrip);
            Assert.False(result.RequiresSystemStop);

            InterlockTrip trip = Assert.Single(result.Trips);
            Assert.Equal("IL-01", trip.RuleId);
            Assert.Equal<int>(new[] { 1 }, trip.AffectedChainIds);

            Assert.Equal(2, result.Commands.Count);
            Assert.Contains(result.Commands, c =>
                c.Kind == ActuatorCommandKind.CloseValve && c.DeviceId == "V-1");
            Assert.Contains(result.Commands, c =>
                c.Kind == ActuatorCommandKind.StopFan && c.DeviceId == "F-1");

            // 모든 인터록 지령은 최우선 순위여야 한다.
            Assert.All(result.Commands, c => Assert.Equal(CommandPriority.Interlock, c.Priority));
        }

        [Fact]
        public void IL01_센서3이_정상범위이면_발동하지_않는다()
        {
            InterlockEvaluation result = CreateDefault().Evaluate(
                SnapshotWithSensor3(-200.0), Build.Config(), Build.T0);

            Assert.False(result.HasTrip);
            Assert.Empty(result.Commands);
        }

        [Fact]
        public void IL01_밸브_닫힘_상태의_압력으로는_발동하지_않는다()
        {
            // ★ 회귀 방지. 전원 투입 직후 센서 3 은 -50 Pa 부근이다.
            // 이 값으로 발동하면 Manual 래치와 겹쳐 장비가 영구히 기동 불가가 된다.
            // 음압이 유지되는 한 배기 계통에 위험은 없다.
            InterlockEvaluation result = CreateDefault().Evaluate(
                SnapshotWithSensor3(-50.0), Build.Config(), Build.T0);

            Assert.False(result.HasTrip);
            Assert.Empty(result.Commands);
        }

        [Fact]
        public void IL01_임계값은_운전_설정_변경에_영향받지_않는다()
        {
            // 안전 임계값이 운전 파라미터에서 파생되면, 작업자가 Config 화면에서
            // 설정값을 바꾸는 것으로 안전 임계값을 움직일 수 있게 된다.
            //
            // 이 설정의 대역 상한은 -500 + 100 = -400 Pa 다.
            // 파생 방식이었다면 정지 상태 압력(-50 Pa)이 -400 Pa 를 넘으므로 즉시 발동했을 것이다.
            ControlConfig config = Build.Config();
            config.Modes[SensorMode.Sensor3] = new ModeSetting(-500.0, 100.0, 300.0);

            Assert.Equal(-400.0, config.Modes[SensorMode.Sensor3].HighLimitPa);

            // 임계값은 0 Pa 로 고정이므로 -50 Pa 에서는 발동하지 않는다.
            Assert.False(CreateDefault()
                .Evaluate(SnapshotWithSensor3(-50.0), config, Build.T0).HasTrip);

            // 대기압을 넘으면 설정과 무관하게 발동한다.
            Assert.True(CreateDefault()
                .Evaluate(SnapshotWithSensor3(50.0), config, Build.T0).HasTrip);
        }

        [Fact]
        public void IL01_센서3_품질이_나쁘면_오동작하지_않는다()
        {
            // 통신 실패 상태의 값(+50)으로 인터록을 발동시키면 오동작이다.
            // 이 경우는 알람 P00/P10~P14 가 담당한다.
            InterlockEvaluation result = CreateDefault().Evaluate(
                SnapshotWithSensor3(50.0, Quality.Bad), Build.Config(), Build.T0);

            Assert.False(result.HasTrip);
        }

        [Fact]
        public void IL01_비활성_체인은_판정하지_않는다()
        {
            ControlConfig config = Build.Config();
            config.Chains[0].Enabled = false;

            InterlockEvaluation result = CreateDefault().Evaluate(
                SnapshotWithSensor3(50.0), config, Build.T0);

            Assert.False(result.HasTrip);
        }

        // ── IL-02 / IL-03: EMO, 메인 차단기 ─────────────────────────────────────

        [Fact]
        public void IL02_EMO가_작동하면_전_체인을_정지시킨다()
        {
            Dictionary<string, PressureReading> pressures = new Dictionary<string, PressureReading>();
            SystemSnapshot snapshot = Build.Snapshot(pressures, null, null, Build.Plc(emo: true));

            InterlockEvaluation result = CreateDefault().Evaluate(snapshot, Build.Config(), Build.T0);

            Assert.True(result.RequiresSystemStop);
            Assert.Contains(result.Trips, t => t.RuleId == "IL-02");

            // 체인 5조 × (밸브 Close + 팬 Stop) = 10건
            Assert.Equal(10, result.Commands.Count);
        }

        [Fact]
        public void IL03_메인차단기_인터록은_입력이_미배선이라_기본_비활성이다()
        {
            // IO List_260801.xlsx 의 디지털 입력 8점에 메인 차단기 접점이 없다.
            // 규칙을 켜 두면 항상 false 를 읽어 "정상"으로 보고하므로,
            // 구현되어 동작 중이라는 착각을 준다.
            SystemSnapshot snapshot = Build.Snapshot(plc: Build.Plc(breakerOff: true));

            InterlockEvaluation result = CreateDefault().Evaluate(snapshot, Build.Config(), Build.T0);

            Assert.False(result.HasTrip);
        }

        [Fact]
        public void IL03_활성화하면_메인차단기_OFF에_전체_정지한다()
        {
            // 배선이 추가되면 Enabled 만 켜서 쓸 수 있어야 한다.
            List<InterlockRule> rules = new List<InterlockRule>(InterlockEvaluator.CreateDefaultRules());
            foreach (InterlockRule rule in rules)
            {
                if (rule.Id == "IL-03")
                {
                    rule.Enabled = true;
                }
            }

            InterlockEvaluation result = new InterlockEvaluator(rules).Evaluate(
                Build.Snapshot(plc: Build.Plc(breakerOff: true)), Build.Config(), Build.T0);

            Assert.True(result.RequiresSystemStop);
            Assert.Contains(result.Trips, t => t.RuleId == "IL-03");
        }

        [Fact]
        public void 미배선_인터록은_구성_경고로_보고된다()
        {
            // 조용히 비활성화하는 것이 가장 위험하다. 반드시 드러나야 한다.
            List<string> warnings = new List<string>();
            CreateDefault().CollectWarnings(warnings);

            Assert.Contains(warnings, w => w.Contains("IL-03"));
            Assert.Contains(warnings, w => w.Contains("IL-05"));
        }

        [Fact]
        public void 전체정지가_발동하면_비활성_체인도_함께_정지시킨다()
        {
            // 안전 정지는 예외를 두지 않는다.
            ControlConfig config = Build.Config();
            config.Chains[0].Enabled = false;
            config.Chains[1].Enabled = false;

            InterlockEvaluation result = CreateDefault().Evaluate(
                Build.Snapshot(plc: Build.Plc(emo: true)), config, Build.T0);

            Assert.Equal(10, result.Commands.Count);
        }

        // ── IL-04: 통신 상실 ────────────────────────────────────────────────────

        [Theory]
        [InlineData(Quality.Bad)]
        [InlineData(Quality.NoData)]
        [InlineData(Quality.Stale)]
        [InlineData(Quality.Uncertain)]
        public void IL04_PLC_품질이_Good이_아니면_안전입력을_믿을_수_없으므로_전체_정지한다(Quality quality)
        {
            // ★ 회귀 방지. 예전에는 Bad 만 검사했다.
            // 한 번도 응답하지 않은 PLC 는 영구히 NoData 로 남으므로,
            // Bad 만 보면 EmoActive·MainBreakerOff 가 계속 false 로 읽히고
            // IL-02·IL-03·IL-05 는 물론 이 규칙 자신까지 전부 무력화된다.
            SystemSnapshot snapshot = Build.Snapshot(plc: Build.Plc(quality: quality));

            InterlockEvaluation result = CreateDefault().Evaluate(snapshot, Build.Config(), Build.T0);

            Assert.True(result.RequiresSystemStop);
            Assert.Contains(result.Trips, t => t.RuleId == "IL-04");
        }

        [Fact]
        public void IL04_안전입력이_구성되지_않았으면_판정하지_않는다()
        {
            // PLC 가 아직 배선되지 않은 단계에서 항상 발동하면 아무것도 검증할 수 없다.
            // 이 경우 "안전 입력이 없다"는 사실은 런타임 조립 경고로 보고한다.
            ControlConfig config = Build.Config();
            config.SafetyInputsConfigured = false;

            InterlockEvaluation result = CreateDefault().Evaluate(
                Build.Snapshot(plc: Build.Plc(quality: Quality.NoData)), config, Build.T0);

            Assert.False(result.HasTrip);
        }

        // ── IL-05: 도어 (정책 미확정 → 기본 비활성) ─────────────────────────────

        [Fact]
        public void IL05_도어_인터록은_정책_미확정이므로_기본_비활성이다()
        {
            SystemSnapshot snapshot = Build.Snapshot(plc: Build.Plc(door: true));

            InterlockEvaluation result = CreateDefault().Evaluate(snapshot, Build.Config(), Build.T0);

            Assert.False(result.HasTrip);
        }

        [Fact]
        public void IL05_활성화하면_도어_열림에_전체_정지한다()
        {
            List<InterlockRule> rules = new List<InterlockRule>(InterlockEvaluator.CreateDefaultRules());
            foreach (InterlockRule rule in rules)
            {
                if (rule.Id == "IL-05")
                {
                    rule.Enabled = true;
                }
            }

            InterlockEvaluation result = new InterlockEvaluator(rules).Evaluate(
                Build.Snapshot(plc: Build.Plc(door: true)), Build.Config(), Build.T0);

            Assert.True(result.RequiresSystemStop);
            Assert.Contains(result.Trips, t => t.RuleId == "IL-05");
        }

        // ── 래치 / 히스테리시스 (Manual 정책) ───────────────────────────────────

        [Fact]
        public void IL01은_Manual정책이므로_압력이_회복되어도_Reset전까지_유지된다()
        {
            // 원인 확인 없이 자동 재가동되면 안 된다.
            InterlockEvaluator evaluator = CreateDefault();
            ControlConfig config = Build.Config();

            evaluator.Evaluate(SnapshotWithSensor3(50.0), config, Build.T0);
            Assert.True(evaluator.HasLatched);

            // 압력이 완전히 정상으로 회복되어도 래치는 유지된다.
            InterlockEvaluation afterRecovery = evaluator.Evaluate(
                SnapshotWithSensor3(-200.0), config, Build.T0.AddSeconds(10));

            Assert.True(afterRecovery.HasTrip);
            Assert.Equal(2, afterRecovery.Commands.Count);

            // Reset 후에야 해제된다.
            evaluator.Reset("IL-01");
            InterlockEvaluation afterReset = evaluator.Evaluate(
                SnapshotWithSensor3(-200.0), config, Build.T0.AddSeconds(20));

            Assert.False(afterReset.HasTrip);
            Assert.False(evaluator.HasLatched);
        }

        [Fact]
        public void 래치된_상태에서_센서를_읽을_수_없어도_인터록이_풀리지_않는다()
        {
            // 통신이 끊겼다고 안전 정지가 해제되면 위험하다.
            InterlockEvaluator evaluator = CreateDefault();
            ControlConfig config = Build.Config();

            evaluator.Evaluate(SnapshotWithSensor3(50.0), config, Build.T0);

            InterlockEvaluation result = evaluator.Evaluate(
                SnapshotWithSensor3(50.0, Quality.Bad), config, Build.T0.AddSeconds(5));

            Assert.True(result.HasTrip);
            Assert.Equal(2, result.Commands.Count);
        }

        [Fact]
        public void Auto정책_인터록은_조건이_해소되면_스스로_해제된다()
        {
            // IL-04(통신 상실)는 Auto 정책이다.
            InterlockEvaluator evaluator = CreateDefault();
            ControlConfig config = Build.Config();

            Assert.True(evaluator
                .Evaluate(Build.Snapshot(plc: Build.Plc(quality: Quality.Bad)), config, Build.T0)
                .RequiresSystemStop);

            InterlockEvaluation recovered = evaluator.Evaluate(
                Build.Snapshot(plc: Build.Plc()), config, Build.T0.AddSeconds(1));

            Assert.False(recovered.HasTrip);
            Assert.False(evaluator.HasLatched);
        }

        [Fact]
        public void 히스테리시스_구간에서는_Auto정책이어도_해제되지_않는다()
        {
            // IL-01 을 Auto 로 바꿔 히스테리시스 동작만 분리 검증한다.
            // 발동 임계 0 Pa, ClearHysteresis 20 → 해제 임계 -20 Pa.
            List<InterlockRule> rules = new List<InterlockRule>(InterlockEvaluator.CreateDefaultRules());
            foreach (InterlockRule rule in rules)
            {
                if (rule.Id == "IL-01")
                {
                    rule.ResetPolicy = AlarmResetPolicy.Auto;
                }
            }

            InterlockEvaluator evaluator = new InterlockEvaluator(rules);
            ControlConfig config = Build.Config();

            evaluator.Evaluate(SnapshotWithSensor3(50.0), config, Build.T0);

            // -10 Pa: 발동 임계(0)보다는 낮지만 해제 임계(-20)에는 못 미쳤다 → 유지
            Assert.True(evaluator
                .Evaluate(SnapshotWithSensor3(-10.0), config, Build.T0.AddSeconds(1)).HasTrip);

            // -30 Pa: 해제 임계를 넘어섰다 → 해제
            Assert.False(evaluator
                .Evaluate(SnapshotWithSensor3(-30.0), config, Build.T0.AddSeconds(2)).HasTrip);
        }

        [Fact]
        public void IL01_Scope를_System으로_바꾸면_전_체인을_정지시킨다()
        {
            List<InterlockRule> rules = new List<InterlockRule>(InterlockEvaluator.CreateDefaultRules());
            foreach (InterlockRule rule in rules)
            {
                if (rule.Id == "IL-01")
                {
                    rule.Scope = InterlockScope.System;
                }
            }

            InterlockEvaluation result = new InterlockEvaluator(rules).Evaluate(
                SnapshotWithSensor3(50.0), Build.Config(), Build.T0);

            Assert.True(result.RequiresSystemStop);

            // 체인 1 개별 지령 2건 + 전 체인 정지 10건
            Assert.Equal(12, result.Commands.Count);
        }

        // ── 방어적 동작 ─────────────────────────────────────────────────────────

        [Fact]
        public void 스냅샷이_null이면_예외_없이_빈_결과를_반환한다()
        {
            // 안전 판정기가 예외를 던져 폴링 루프를 중단시키면 안 된다.
            InterlockEvaluation result = CreateDefault().Evaluate(null, Build.Config(), Build.T0);

            Assert.False(result.HasTrip);
            Assert.Empty(result.Commands);
        }
    }
}
