using System.Collections.Generic;
using Esam.Domain.Alarms;
using Esam.Domain.Configuration;
using Esam.Domain.Control;
using Esam.Domain.Models;
using Xunit;

namespace Esam.Tests
{
    /// <summary>
    /// 선언(JSON) 기반 알람 판정이 의도대로 동작하는지 검증한다.
    /// 임계값 변경에 재컴파일이 필요 없어야 한다는 요구사항의 핵심 부분이다.
    /// </summary>
    public class AlarmEvaluatorTests
    {
        private static SystemSnapshot SnapshotWith(
            double? efemTemp = null,
            double? humidity = null,
            double? velocity1 = null,
            bool emo = false,
            double? sensorPa = null,
            Quality sensorQuality = Quality.Good)
        {
            Dictionary<string, PressureReading> pressures = new Dictionary<string, PressureReading>();
            if (sensorPa.HasValue)
            {
                pressures["S1-1"] = Build.Pressure("S1-1", sensorPa.Value, sensorQuality);
            }

            AuxiliaryReadings aux = new AuxiliaryReadings(
                new double?[] { velocity1, null, null },
                efemTemp, humidity, null, null,
                new double?[5], null, new double?[2], new double?[2],
                Quality.Good, Build.T0);

            return Build.Snapshot(pressures, null, null, Build.Plc(emo: emo), aux);
        }

        [Fact]
        public void 레시피의_센서별_상한을_초과하면_알람이_발생한다()
        {
            // ★ 규칙에 임계값이 없다. source 의 디바이스 ID 로 레시피를 조회한다.
            // 값을 규칙에 복사해 두면 Config 화면에서 설정을 바꿨을 때
            // 알람만 옛 값으로 남아 화면과 알람이 서로 다른 진실을 말한다.
            AlarmRule rule = new AlarmRule
            {
                Code = "AL-46",
                Name = "EFEM Exhaust Center Front Pressure Sensor High Limit",
                Severity = AlarmSeverity.Alarm,
                Source = "device:S2-1.pressurePa",
                Condition = AlarmConditionType.AboveHighLimit
            };

            ControlConfig config = Build.Config();
            config.Recipe = new RecipeDefinition();
            config.Recipe.Sensors.Add(new SensorSetting("S2-1", -10.0, -40.0, 20.0));

            AlarmEvaluator evaluator = new AlarmEvaluator(new[] { rule });

            Dictionary<string, PressureReading> inBand =
                new Dictionary<string, PressureReading> { { "S2-1", Build.Pressure("S2-1", 15.0) } };

            Assert.Empty(evaluator.Evaluate(Build.Snapshot(inBand), config, Build.T0));

            Dictionary<string, PressureReading> above =
                new Dictionary<string, PressureReading> { { "S2-1", Build.Pressure("S2-1", 25.0) } };

            AlarmState state = Assert.Single(evaluator.Evaluate(Build.Snapshot(above), config, Build.T0));
            Assert.Equal("AL-46", state.Rule.Code);
            Assert.Contains("상한", state.Detail);
        }

        [Fact]
        public void 레시피의_센서별_하한에_미달하면_알람이_발생한다()
        {
            AlarmRule rule = new AlarmRule
            {
                Code = "AL-47",
                Name = "EFEM Exhaust Center Front Pressure Sensor Low Limit",
                Severity = AlarmSeverity.Alarm,
                Source = "device:S2-1.pressurePa",
                Condition = AlarmConditionType.BelowLowLimit
            };

            ControlConfig config = Build.Config();
            config.Recipe = new RecipeDefinition();
            config.Recipe.Sensors.Add(new SensorSetting("S2-1", -10.0, -40.0, 20.0));

            AlarmEvaluator evaluator = new AlarmEvaluator(new[] { rule });

            Dictionary<string, PressureReading> inBand =
                new Dictionary<string, PressureReading> { { "S2-1", Build.Pressure("S2-1", -30.0) } };

            Assert.Empty(evaluator.Evaluate(Build.Snapshot(inBand), config, Build.T0));

            Dictionary<string, PressureReading> below =
                new Dictionary<string, PressureReading> { { "S2-1", Build.Pressure("S2-1", -50.0) } };

            AlarmState state = Assert.Single(evaluator.Evaluate(Build.Snapshot(below), config, Build.T0));
            Assert.Equal("AL-47", state.Rule.Code);
            Assert.Contains("하한", state.Detail);
        }

        [Fact]
        public void 센서별_임계값이_각_센서에_따로_적용된다()
        {
            // 모드별 공통값이었다면 두 센서가 같은 기준으로 판정된다.
            // 센서별 값을 쓰면 같은 압력에서도 한쪽만 울린다.
            AlarmRule[] rules =
            {
                new AlarmRule
                {
                    Code = "AL-46", Source = "device:S2-1.pressurePa",
                    Condition = AlarmConditionType.AboveHighLimit
                },
                new AlarmRule
                {
                    Code = "AL-48", Source = "device:S2-2.pressurePa",
                    Condition = AlarmConditionType.AboveHighLimit
                }
            };

            ControlConfig config = Build.Config();
            config.Recipe = new RecipeDefinition();
            config.Recipe.Sensors.Add(new SensorSetting("S2-1", -10.0, -40.0, 0.0));
            config.Recipe.Sensors.Add(new SensorSetting("S2-2", -10.0, -40.0, 20.0));

            AlarmEvaluator evaluator = new AlarmEvaluator(rules);

            // 둘 다 +10 Pa. S2-1 은 상한 0 을 넘고 S2-2 는 상한 20 안이다.
            Dictionary<string, PressureReading> pressures = new Dictionary<string, PressureReading>
            {
                { "S2-1", Build.Pressure("S2-1", 10.0) },
                { "S2-2", Build.Pressure("S2-2", 10.0) }
            };

            AlarmState state = Assert.Single(evaluator.Evaluate(Build.Snapshot(pressures), config, Build.T0));
            Assert.Equal("AL-46", state.Rule.Code);
        }

        [Fact]
        public void 레시피가_없으면_상하한_알람을_판정하지_않는다()
        {
            // 폴백 임계값을 쓰면 작업자가 설정한 값과 다른 기준으로 알람이 울린다.
            // 조용히 틀리는 쪽이 더 위험하므로 판정하지 않는다.
            // 참조가 끊어진 구성은 로드 단계에서 오류로 막는다.
            AlarmRule rule = new AlarmRule
            {
                Code = "AL-46",
                Source = "device:S2-1.pressurePa",
                Condition = AlarmConditionType.AboveHighLimit
            };

            ControlConfig config = Build.Config();
            Assert.Null(config.Recipe);

            AlarmEvaluator evaluator = new AlarmEvaluator(new[] { rule });

            Dictionary<string, PressureReading> extreme =
                new Dictionary<string, PressureReading> { { "S2-1", Build.Pressure("S2-1", 9999.0) } };

            Assert.Empty(evaluator.Evaluate(Build.Snapshot(extreme), config, Build.T0));
        }

        [Fact]
        public void 상한초과_알람이_발생한다()
        {
            AlarmRule rule = new AlarmRule
            {
                Code = "A06",
                Name = "Temp sensor (EFEM) High limit",
                Severity = AlarmSeverity.Alarm,
                Source = "aux:temperatureEfem",
                Condition = AlarmConditionType.GreaterThan,
                Threshold = 30.0
            };

            AlarmEvaluator evaluator = new AlarmEvaluator(new[] { rule });

            Assert.Empty(evaluator.Evaluate(SnapshotWith(efemTemp: 25.0), Build.Config(), Build.T0));

            IList<AlarmState> raised = evaluator.Evaluate(
                SnapshotWith(efemTemp: 35.0), Build.Config(), Build.T0);

            AlarmState state = Assert.Single(raised);
            Assert.Equal("A06", state.Rule.Code);
            Assert.True(state.IsActive);
            Assert.Equal(35.0, state.TriggerValue, 6);
        }

        [Fact]
        public void 하한미달_알람이_발생한다()
        {
            AlarmRule rule = new AlarmRule
            {
                Code = "A15",
                Name = "풍속1 Low limit",
                Source = "aux:airVelocity[0]",
                Condition = AlarmConditionType.LessThan,
                Threshold = 0.3
            };

            AlarmEvaluator evaluator = new AlarmEvaluator(new[] { rule });

            Assert.Single(evaluator.Evaluate(SnapshotWith(velocity1: 0.1), Build.Config(), Build.T0));
        }

        [Fact]
        public void 디바운스_시간이_지나야_알람이_확정된다()
        {
            AlarmRule rule = new AlarmRule
            {
                Code = "A05",
                Source = "aux:temperatureEfem",
                Condition = AlarmConditionType.GreaterThan,
                Threshold = 30.0,
                DebounceMs = 5000.0
            };

            AlarmEvaluator evaluator = new AlarmEvaluator(new[] { rule });
            SystemSnapshot hot = SnapshotWith(efemTemp: 40.0);

            Assert.Empty(evaluator.Evaluate(hot, Build.Config(), Build.T0));
            Assert.Empty(evaluator.Evaluate(hot, Build.Config(), Build.T0.AddMilliseconds(4999)));
            Assert.Single(evaluator.Evaluate(hot, Build.Config(), Build.T0.AddMilliseconds(5001)));
        }

        [Fact]
        public void 디바운스_중_조건이_해소되면_누적이_초기화된다()
        {
            AlarmRule rule = new AlarmRule
            {
                Code = "A05",
                Source = "aux:temperatureEfem",
                Condition = AlarmConditionType.GreaterThan,
                Threshold = 30.0,
                DebounceMs = 5000.0
            };

            AlarmEvaluator evaluator = new AlarmEvaluator(new[] { rule });

            evaluator.Evaluate(SnapshotWith(efemTemp: 40.0), Build.Config(), Build.T0);
            evaluator.Evaluate(SnapshotWith(efemTemp: 20.0), Build.Config(), Build.T0.AddMilliseconds(3000));

            // 다시 초과해도 처음부터 5초를 채워야 한다.
            Assert.Empty(evaluator.Evaluate(
                SnapshotWith(efemTemp: 40.0), Build.Config(), Build.T0.AddMilliseconds(6000)));
        }

        [Fact]
        public void Auto정책_알람은_조건_해소시_자동_해제된다()
        {
            AlarmRule rule = new AlarmRule
            {
                Code = "A06",
                Source = "aux:temperatureEfem",
                Condition = AlarmConditionType.GreaterThan,
                Threshold = 30.0,
                ResetPolicy = AlarmResetPolicy.Auto
            };

            AlarmEvaluator evaluator = new AlarmEvaluator(new[] { rule });
            evaluator.Evaluate(SnapshotWith(efemTemp: 40.0), Build.Config(), Build.T0);
            Assert.True(evaluator.FindState("A06").IsActive);

            evaluator.Evaluate(SnapshotWith(efemTemp: 20.0), Build.Config(), Build.T0.AddMilliseconds(1000));
            Assert.False(evaluator.FindState("A06").IsActive);
        }

        [Fact]
        public void Manual정책_알람은_조건_해소후에도_Reset해야_해제된다()
        {
            AlarmRule rule = new AlarmRule
            {
                Code = "A10",
                Source = "aux:temperatureEfem",
                Condition = AlarmConditionType.GreaterThan,
                Threshold = 30.0,
                ResetPolicy = AlarmResetPolicy.Manual
            };

            AlarmEvaluator evaluator = new AlarmEvaluator(new[] { rule });
            evaluator.Evaluate(SnapshotWith(efemTemp: 40.0), Build.Config(), Build.T0);
            evaluator.Evaluate(SnapshotWith(efemTemp: 20.0), Build.Config(), Build.T0.AddMilliseconds(1000));

            Assert.True(evaluator.FindState("A10").IsActive);

            evaluator.Reset("A10");
            Assert.False(evaluator.FindState("A10").IsActive);
        }

        [Fact]
        public void OutOfBand_알람은_참조모드의_대역을_사용한다()
        {
            AlarmRule rule = new AlarmRule
            {
                Code = "P01",
                Source = "device:S1-1.pressurePa",
                Condition = AlarmConditionType.OutOfBand,
                ReferenceMode = SensorMode.Sensor1 // 6 ± 2 → 4 ~ 8
            };

            AlarmEvaluator evaluator = new AlarmEvaluator(new[] { rule });

            Assert.Empty(evaluator.Evaluate(SnapshotWith(sensorPa: 6.0), Build.Config(), Build.T0));
            Assert.Single(evaluator.Evaluate(SnapshotWith(sensorPa: 12.0), Build.Config(), Build.T0));
        }

        [Fact]
        public void PLC_비트_알람이_동작한다()
        {
            AlarmRule rule = new AlarmRule
            {
                Code = "EMO",
                Source = "plc:di.emo",
                Condition = AlarmConditionType.BitSet,
                Severity = AlarmSeverity.Critical
            };

            AlarmEvaluator evaluator = new AlarmEvaluator(new[] { rule });

            Assert.Empty(evaluator.Evaluate(SnapshotWith(), Build.Config(), Build.T0));
            Assert.Single(evaluator.Evaluate(SnapshotWith(emo: true), Build.Config(), Build.T0));
        }

        [Fact]
        public void 값을_읽을_수_없으면_임계값_알람은_발생하지_않는다()
        {
            // 통신 실패는 CommFail 규칙이 담당한다. 여기서 알람을 올리면 중복 통보가 된다.
            AlarmRule rule = new AlarmRule
            {
                Code = "P01",
                Source = "device:S1-1.pressurePa",
                Condition = AlarmConditionType.GreaterThan,
                Threshold = 0.0
            };

            AlarmEvaluator evaluator = new AlarmEvaluator(new[] { rule });

            Assert.Empty(evaluator.Evaluate(
                SnapshotWith(sensorPa: 100.0, sensorQuality: Quality.Bad), Build.Config(), Build.T0));
        }

        [Fact]
        public void 비활성_규칙은_판정하지_않는다()
        {
            AlarmRule rule = new AlarmRule
            {
                Code = "A09",
                Source = "aux:temperatureEfem",
                Condition = AlarmConditionType.GreaterThan,
                Threshold = 0.0,
                Enabled = false
            };

            AlarmEvaluator evaluator = new AlarmEvaluator(new[] { rule });

            Assert.Empty(evaluator.Evaluate(SnapshotWith(efemTemp: 100.0), Build.Config(), Build.T0));
        }

        [Fact]
        public void 요약은_최고_심각도와_미확인_여부를_반영한다()
        {
            AlarmRule warn = new AlarmRule
            {
                Code = "W1",
                Source = "aux:temperatureEfem",
                Condition = AlarmConditionType.GreaterThan,
                Threshold = 10.0,
                Severity = AlarmSeverity.Warning
            };

            AlarmRule critical = new AlarmRule
            {
                Code = "C1",
                Source = "aux:temperatureEfem",
                Condition = AlarmConditionType.GreaterThan,
                Threshold = 20.0,
                Severity = AlarmSeverity.Critical
            };

            AlarmEvaluator evaluator = new AlarmEvaluator(new[] { warn, critical });
            evaluator.Evaluate(SnapshotWith(efemTemp: 30.0), Build.Config(), Build.T0);

            AlarmSummary summary = evaluator.BuildSummary();

            Assert.Equal(2, summary.ActiveCount);
            Assert.Equal(AlarmSeverity.Critical, summary.HighestSeverity);
            Assert.True(summary.HasUnacknowledged);

            evaluator.AcknowledgeAll();
            Assert.False(evaluator.BuildSummary().HasUnacknowledged);
        }
    }
}
