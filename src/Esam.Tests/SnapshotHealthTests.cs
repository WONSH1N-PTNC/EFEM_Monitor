using System;
using System.Collections.Generic;
using Esam.Communication.Abstractions;
using Esam.Communication.Configuration;
using Esam.Communication.Polling;
using Esam.Domain.Models;
using Esam.Services;
using Xunit;

namespace Esam.Tests
{
    /// <summary>
    /// 디바이스별 통신 건강 상태(<see cref="DeviceHealth"/>) 조립 검증.
    /// </summary>
    /// <remarks>
    /// <para>I/O Status 화면의 상태 램프가 이 값만 보고 색을 정한다.
    /// 여기서 틀리면 <b>화면이 조용히 거짓말을 한다.</b> 죽은 센서가 초록으로 남고,
    /// 커미셔닝에서 그 램프를 근거로 배선을 정상이라고 판정한다.</para>
    /// <para>그래서 "정상으로 보이면 안 되는 경우" 를 중심으로 짰다.</para>
    /// </remarks>
    public sealed class SnapshotHealthTests
    {
        private static readonly DateTime T0 = new DateTime(2026, 8, 12, 0, 0, 0, DateTimeKind.Utc);

        // ─────────────────────────────────────────────────────────────────────
        // 기본 판정
        // ─────────────────────────────────────────────────────────────────────

        [Fact]
        public void 수신하지_못한_디바이스는_NoData_이며_정상이_아니다()
        {
            SnapshotBuilder builder = new SnapshotBuilder(CreateMap());

            SystemSnapshot snapshot = builder.Build(null, null, T0);
            DeviceHealth health = snapshot.FindDevice("S1-1");

            Assert.NotNull(health);
            Assert.Equal(Quality.NoData, health.Quality);
            Assert.True(health.IsPolled);
            Assert.False(health.IsHealthy);
            Assert.Equal(0, health.PointCount);
            Assert.Equal(DateTime.MinValue, health.LastUpdateUtc);
        }

        [Fact]
        public void 정상_수신하면_Good_이며_측정점_수가_기록된다()
        {
            SnapshotBuilder builder = new SnapshotBuilder(CreateMap());

            builder.Apply(Poll("CH1", Success("S1-1", Sample("S1-1", "pressurePa", 12.5))));

            DeviceHealth health = builder.Build(null, null, T0).FindDevice("S1-1");

            Assert.Equal(Quality.Good, health.Quality);
            Assert.True(health.IsHealthy);
            Assert.Equal(1, health.PointCount);
            Assert.Equal(1, health.GoodPointCount);
            Assert.Equal(T0, health.LastUpdateUtc);
        }

        [Fact]
        public void 그룹_읽기가_실패하면_Bad_로_격하된다()
        {
            SnapshotBuilder builder = new SnapshotBuilder(CreateMap());

            builder.Apply(Poll("CH1", Success("S1-1", Sample("S1-1", "pressurePa", 12.5))));
            builder.Apply(Poll("CH1", Failure("S1-1")));

            DeviceHealth health = builder.Build(null, null, T0).FindDevice("S1-1");

            Assert.Equal(Quality.Bad, health.Quality);
            Assert.False(health.IsHealthy);

            // 값 자체는 남는다. 0 으로 지우면 트렌드에 가짜 급락이 기록된다.
            Assert.Equal(1, health.PointCount);
            Assert.Equal(0, health.GoodPointCount);
        }

        [Fact]
        public void 갱신이_끊기면_Stale_로_격하된다()
        {
            SnapshotBuilder builder = new SnapshotBuilder(CreateMap());
            builder.StaleThresholdMs = 15000.0;

            builder.Apply(Poll("CH1", Success("S1-1", Sample("S1-1", "pressurePa", 12.5))));

            DeviceHealth health = builder
                .Build(null, null, T0.AddMilliseconds(15001.0))
                .FindDevice("S1-1");

            Assert.Equal(Quality.Stale, health.Quality);
            Assert.False(health.IsHealthy);
        }

        [Fact]
        public void 일부_측정점만_정상이면_정상으로_보고하지_않는다()
        {
            // 밸브 위치는 오는데 알람코드 읽기만 실패하는 구성이 실제로 있다.
            // 이때 램프가 초록이면 "알람을 못 읽고 있다" 는 사실이 화면 어디에도 없다.
            SnapshotBuilder builder = new SnapshotBuilder(CreateMap());

            builder.Apply(Poll(
                "CH2",
                Success(
                    "V-1",
                    Sample("V-1", "positionPulse", 2500.0),
                    Sample("V-1", "alarmCode", 0.0, Quality.Bad))));

            DeviceHealth health = builder.Build(null, null, T0).FindDevice("V-1");

            Assert.Equal(Quality.Bad, health.Quality);
            Assert.Equal(2, health.PointCount);
            Assert.Equal(1, health.GoodPointCount);
            Assert.False(health.IsHealthy);
        }

        [Fact]
        public void NoData_측정점은_정상보다_나은_것으로_취급되지_않는다()
        {
            // Quality 열거형은 NoData 가 0 이다. 숫자로 최댓값을 고르면
            // 수신한 적 없는 측정점이 Good(1) 보다 "나은" 것이 되어 램프가 초록으로 남는다.
            SnapshotBuilder builder = new SnapshotBuilder(CreateMap());

            builder.Apply(Poll(
                "CH2",
                Success(
                    "V-1",
                    Sample("V-1", "positionPulse", 2500.0),
                    Sample("V-1", "homeDone", 0.0, Quality.NoData))));

            DeviceHealth health = builder.Build(null, null, T0).FindDevice("V-1");

            Assert.False(health.IsHealthy);
            Assert.Equal(Quality.NoData, health.Quality);
        }

        // ─────────────────────────────────────────────────────────────────────
        // 구성 표시
        // ─────────────────────────────────────────────────────────────────────

        [Fact]
        public void 꺼_둔_디바이스도_목록에_남지만_정상은_아니다()
        {
            // 목록에서 빼면 화면에서 "구성에 아예 없음" 과 "꺼 두었음" 이 같아진다.
            // 커미셔닝에서 이 둘을 혼동하면 멀쩡한 배선을 확인하러 장비를 연다.
            DeviceMap map = CreateMap();
            FindDevice(map, "S1-2").Enabled = false;

            SnapshotBuilder builder = new SnapshotBuilder(map);
            SystemSnapshot snapshot = builder.Build(null, null, T0);

            DeviceHealth health = snapshot.FindDevice("S1-2");

            Assert.NotNull(health);
            Assert.False(health.IsPolled);
            Assert.False(health.IsHealthy);

            // 값 모델에는 들어가지 않는다. 폴링하지 않는 센서의 압력을 만들면 안 된다.
            Assert.False(snapshot.Pressures.ContainsKey("S1-2"));
        }

        [Fact]
        public void 구성에_없는_디바이스는_사전에도_없다()
        {
            SnapshotBuilder builder = new SnapshotBuilder(CreateMap());

            Assert.Null(builder.Build(null, null, T0).FindDevice("MFC-1"));
        }

        [Fact]
        public void 드라이버와_포트와_표시명이_함께_실린다()
        {
            // 램프 분류(12종)와 "포트 단위로 죽었는가" 판정이 이 값에 의존한다.
            SnapshotBuilder builder = new SnapshotBuilder(CreateMap());

            DeviceHealth health = builder.Build(null, null, T0).FindDevice("V-1");

            Assert.Equal("ThrottleValve", health.Driver);
            Assert.Equal("CH2", health.PortId);
            Assert.Equal("EFEM 중앙 스로틀밸브", health.Name);
        }

        [Fact]
        public void 디바이스_조회는_대소문자를_구분하지_않는다()
        {
            SnapshotBuilder builder = new SnapshotBuilder(CreateMap());

            Assert.NotNull(builder.Build(null, null, T0).FindDevice("s1-1"));
        }

        // ─────────────────────────────────────────────────────────────────────
        // 보조 계측 — 5종을 구분해야 하는 이유
        // ─────────────────────────────────────────────────────────────────────

        [Fact]
        public void 보조_계측_장치는_서로_다른_상태를_가진다()
        {
            // 이것이 DeviceHealth 를 만든 이유다. AuxiliaryReadings 는 품질을
            // 하나로 뭉쳐 갖고 있어 온습도가 죽었는지 풍속이 죽었는지 알 수 없다.
            SnapshotBuilder builder = new SnapshotBuilder(CreateMap());

            builder.Apply(Poll(
                "CH1",
                Success("WS-1", Sample("WS-1", "velocity", 0.45)),
                Failure("THD-1")));

            SystemSnapshot snapshot = builder.Build(null, null, T0);

            Assert.Equal(Quality.Good, snapshot.FindDevice("WS-1").Quality);
            Assert.NotEqual(Quality.Good, snapshot.FindDevice("THD-1").Quality);
        }

        [Fact]
        public void 보조_계측_대표품질은_더_이상_항상_정상이_아니다()
        {
            // 종전에는 AuxiliaryReadings.Quality 에 Quality.Good 을 그대로 적어 넣었다.
            // 온습도·풍속이 전부 끊겨도 "정상" 으로 보고되는 값이었다.
            SnapshotBuilder builder = new SnapshotBuilder(CreateMap());

            builder.Apply(Poll("CH1", Success("THD-1", Sample("THD-1", "temperature", 23.4))));
            builder.Apply(Poll("CH1", Failure("THD-1")));

            SystemSnapshot snapshot = builder.Build(null, null, T0);

            Assert.Equal(Quality.Bad, snapshot.Auxiliary.Quality);
        }

        [Fact]
        public void 보조_계측_대표품질은_가장_나쁜_장치를_따른다()
        {
            // 평균이나 다수결로 정하면 다섯 중 하나가 죽어도 대표값이 정상으로 남는다.
            SnapshotBuilder builder = new SnapshotBuilder(CreateMap());

            builder.Apply(Poll(
                "CH1",
                Success("WS-1", Sample("WS-1", "velocity", 0.45)),
                Success("WS-2", Sample("WS-2", "velocity", 0.44)),
                Success("THD-1", Sample("THD-1", "temperature", 23.4))));

            builder.Apply(Poll("CH1", Failure("WS-2")));

            Assert.Equal(Quality.Bad, builder.Build(null, null, T0).Auxiliary.Quality);
        }

        // ─────────────────────────────────────────────────────────────────────
        // 스냅샷 계약
        // ─────────────────────────────────────────────────────────────────────

        [Fact]
        public void 디바이스_사전을_생략하면_빈_사전이_된다()
        {
            // 기존 호출부 수백 곳이 이 경로를 지난다. null 이 되면 화면이 즉시 죽는다.
            SystemSnapshot snapshot =
                new SystemSnapshot(T0, null, null, null, null, null, null, null);

            Assert.NotNull(snapshot.Devices);
            Assert.Empty(snapshot.Devices);
            Assert.Null(snapshot.FindDevice("S1-1"));
        }

        [Fact]
        public void 디바이스_사전은_방어적으로_복사된다()
        {
            Dictionary<string, DeviceHealth> source =
                new Dictionary<string, DeviceHealth>(StringComparer.OrdinalIgnoreCase);

            source["S1-1"] = DeviceHealth.NoData("S1-1", null, "PressureSensor", "CH1", true);

            SystemSnapshot snapshot =
                new SystemSnapshot(T0, null, null, null, null, null, null, null, source);

            source.Remove("S1-1");

            Assert.NotNull(snapshot.FindDevice("S1-1"));
        }

        // ─────────────────────────────────────────────────────────────────────
        // 도우미
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>차압센서 3대·밸브 1대·온습도 1대·풍속 2대로 이루어진 최소 구성을 만든다.</summary>
        private static DeviceMap CreateMap()
        {
            DeviceMap map = new DeviceMap();

            map.DeviceTypes["DiffPressure"] = Type(PointKeys.DriverPressureSensor);
            map.DeviceTypes["ThrottleValve"] = Type(PointKeys.DriverThrottleValve);
            map.DeviceTypes["ThdRt"] = Type(PointKeys.DriverTempHumidity);
            map.DeviceTypes["Beck985"] = Type(PointKeys.DriverAirVelocity);

            map.Devices.Add(Device("S1-1", "DiffPressure", "CH1", 1));
            map.Devices.Add(Device("S1-2", "DiffPressure", "CH1", 2));
            map.Devices.Add(Device("S1-3", "DiffPressure", "CH1", 3));
            map.Devices.Add(Device("THD-1", "ThdRt", "CH1", 14));
            map.Devices.Add(Device("WS-1", "Beck985", "CH1", 15));
            map.Devices.Add(Device("WS-2", "Beck985", "CH1", 16));

            DeviceInstanceDefinition valve = Device("V-1", "ThrottleValve", "CH2", 1);
            valve.Name = "EFEM 중앙 스로틀밸브";
            map.Devices.Add(valve);

            return map;
        }

        private static DeviceTypeDefinition Type(string driver)
        {
            DeviceTypeDefinition type = new DeviceTypeDefinition();
            type.Driver = driver;
            return type;
        }

        private static DeviceInstanceDefinition Device(
            string id, string type, string port, byte slaveId)
        {
            return new DeviceInstanceDefinition
            {
                Id = id,
                Type = type,
                Port = port,
                SlaveId = slaveId
            };
        }

        private static DeviceInstanceDefinition FindDevice(DeviceMap map, string id)
        {
            foreach (DeviceInstanceDefinition device in map.Devices)
            {
                if (string.Equals(device.Id, id, StringComparison.OrdinalIgnoreCase))
                {
                    return device;
                }
            }

            throw new InvalidOperationException("테스트 구성에 " + id + " 가 없습니다.");
        }

        private static PointSample Sample(
            string deviceId, string key, double value, Quality quality = Quality.Good)
        {
            return new PointSample(deviceId, key, value, value, quality, null, T0);
        }

        private static GroupReadResult Success(string deviceId, params PointSample[] samples)
        {
            return new GroupReadResult(
                deviceId, "g", PollingTier.Fast, true,
                ModbusFailureKind.None, null, 1.0, samples);
        }

        private static GroupReadResult Failure(string deviceId)
        {
            return new GroupReadResult(
                deviceId, "g", PollingTier.Fast, false,
                ModbusFailureKind.Timeout, "무응답", 1.0, null);
        }

        private static PollCompletedEventArgs Poll(string portId, params GroupReadResult[] results)
        {
            return new PollCompletedEventArgs(portId, T0, 1.0, null, results);
        }
    }
}
