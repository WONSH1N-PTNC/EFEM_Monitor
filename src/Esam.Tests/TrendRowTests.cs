using System;
using System.Collections.Generic;
using Esam.Domain.Alarms;
using Esam.Domain.Control;
using Esam.Domain.Models;
using Esam.Persistence;
using Xunit;

namespace Esam.Tests
{
    /// <summary>
    /// 스냅샷 → 트렌드 행 변환 검증.
    /// </summary>
    /// <remarks>
    /// 여기서 틀리면 값이 옆 열에 들어가거나, 통신이 끊긴 구간이 정상 운전처럼 기록된다.
    /// 둘 다 나중에 트렌드를 볼 때까지 아무도 모르는 종류의 결함이다.
    /// </remarks>
    public sealed class TrendRowTests
    {
        private static readonly DateTime SampleUtc =
            new DateTime(2026, 8, 19, 3, 30, 0, DateTimeKind.Utc);

        /// <summary>지정 품질의 차압 판독값을 만든다.</summary>
        private static PressureReading Pressure(string id, double pa, Quality quality)
        {
            return new PressureReading(id, pa, pa, 0, 0.0, quality, SampleUtc);
        }

        [Fact]
        public void 열_계약의_개수가_스키마와_맞는다()
        {
            // 이 세 배열이 열 순서의 유일한 근거다. 개수가 바뀌면 스키마도 바뀌어야 한다.
            Assert.Equal(13, TrendRow.SensorIds.Length);
            Assert.Equal(5, TrendRow.ValveIds.Length);
            Assert.Equal(5, TrendRow.FanIds.Length);
        }

        [Fact]
        public void 품질이_나쁜_값은_기록하지_않는다()
        {
            // 통신이 끊긴 센서의 낡은 값을 남기면 그 구간이 정상 운전으로 읽힌다.
            Dictionary<string, PressureReading> pressures = new Dictionary<string, PressureReading>
            {
                { "S1-1", Pressure("S1-1", -12.5, Quality.Good) },
                { "S1-2", Pressure("S1-2", -11.0, Quality.Stale) },
                { "S1-3", Pressure("S1-3", -10.0, Quality.Bad) }
            };

            SystemSnapshot snapshot = new SystemSnapshot(
                SampleUtc, pressures, null, null, null, null, null, null);

            TrendRow row = TrendRow.FromSnapshot(snapshot);

            Assert.Equal(-12.5, row.Pressures[0]);
            Assert.Null(row.Pressures[1]);
            Assert.Null(row.Pressures[2]);
        }

        [Fact]
        public void 없는_센서는_빈_값으로_둔다()
        {
            TrendRow row = TrendRow.FromSnapshot(SystemSnapshot.CreateEmpty(SampleUtc));

            foreach (double? value in row.Pressures)
            {
                Assert.Null(value);
            }

            Assert.Null(row.FfuRpm);
            Assert.Null(row.ActiveAlarmCodes);
        }

        [Fact]
        public void 밸브와_팬이_선언된_순서로_들어간다()
        {
            Dictionary<string, ValveState> valves = new Dictionary<string, ValveState>
            {
                { "V-1", new ValveState("V-1", 0, 0, 10.0, 9.0, ValveMotionStatus.Idle, 0, true, Quality.Good, SampleUtc) },
                { "V-5", new ValveState("V-5", 0, 0, 50.0, 45.0, ValveMotionStatus.Idle, 0, true, Quality.Good, SampleUtc) }
            };

            Dictionary<string, FanState> fans = new Dictionary<string, FanState>
            {
                { "F-1", new FanState("F-1", 1000.0, 1000.0, FanRunStatus.Unknown, 0, Quality.Good, SampleUtc) },
                { "F-5", new FanState("F-5", 5000.0, 5000.0, FanRunStatus.Unknown, 0, Quality.Good, SampleUtc) }
            };

            SystemSnapshot snapshot = new SystemSnapshot(
                SampleUtc, null, valves, fans, null, null, null, null);

            TrendRow row = TrendRow.FromSnapshot(snapshot);

            Assert.Equal(10.0, row.ValvePercents[0]);
            Assert.Equal(50.0, row.ValvePercents[4]);
            Assert.Null(row.ValvePercents[2]);
            Assert.Equal(1000.0, row.FanRpms[0]);
            Assert.Equal(5000.0, row.FanRpms[4]);
        }

        [Fact]
        public void 보조_계측값과_운전_상태가_옮겨진다()
        {
            AuxiliaryReadings auxiliary = new AuxiliaryReadings(
                new List<double?> { 0.4, null, 0.6 },
                23.4, 45.0, 1200.0, 31.2,
                null, 900.0,
                new List<double?> { 1.5, null },
                null, Quality.Good, SampleUtc);

            ControlStatus control = new ControlStatus(
                SystemPhase.AutoControl, SensorMode.Sensor2, true, null, null);

            SystemSnapshot snapshot = new SystemSnapshot(
                SampleUtc, null, null, null, null, auxiliary, control, null);

            TrendRow row = TrendRow.FromSnapshot(snapshot);

            Assert.Equal(0.4, row.AirVelocities[0]);
            Assert.Null(row.AirVelocities[1]);
            Assert.Equal(0.6, row.AirVelocities[2]);
            Assert.Equal(1.5, row.MfcFlows[0]);
            Assert.Null(row.MfcFlows[1]);
            Assert.Equal(900.0, row.FfuRpm);
            Assert.Equal(23.4, row.TemperatureEfem);
            Assert.Equal(31.2, row.TemperatureControlBox);
            Assert.Equal((int)SystemPhase.AutoControl, row.ControlPhase);
            Assert.Equal((int)SensorMode.Sensor2, row.ControlMode);
        }

        [Fact]
        public void 활성_알람은_쉼표로_이어_적는다()
        {
            AlarmSummary alarms = new AlarmSummary(
                new List<string> { "AL-02", "AL-13" }, true, AlarmSeverity.Alarm);

            SystemSnapshot snapshot = new SystemSnapshot(
                SampleUtc, null, null, null, null, null, null, alarms);

            TrendRow row = TrendRow.FromSnapshot(snapshot);

            Assert.Equal("AL-02,AL-13", row.ActiveAlarmCodes);
        }

        [Fact]
        public void 시각은_밀리초까지_왕복한다()
        {
            // 폴링 주기가 218ms 이므로 초 단위로 저장하면 표본이 뭉개진다.
            DateTime utc = new DateTime(2026, 8, 19, 3, 30, 0, 218, DateTimeKind.Utc);

            long unixMs = TrendRow.ToUnixMs(utc);

            Assert.Equal(utc, TrendRow.FromUnixMs(unixMs));
            Assert.Equal(DateTimeKind.Utc, TrendRow.FromUnixMs(unixMs).Kind);
            Assert.Equal(218, TrendRow.FromUnixMs(unixMs).Millisecond);
        }

        [Fact]
        public void 스냅샷이_없으면_거부한다()
        {
            Assert.Throws<ArgumentNullException>(() => TrendRow.FromSnapshot(null));
        }
    }
}
