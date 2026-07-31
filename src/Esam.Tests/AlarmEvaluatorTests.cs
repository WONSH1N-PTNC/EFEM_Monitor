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
