using System;
using System.Collections.Generic;
using Esam.Communication.Configuration;
using Esam.Communication.Polling;
using Esam.Domain.Models;
using Esam.Hmi.ViewModels;
using Esam.Services;
using Xunit;

namespace Esam.Tests
{
    /// <summary>
    /// I/O Status 화면의 판정 검증.
    /// </summary>
    /// <remarks>
    /// <para>이 화면은 S6 커미셔닝의 <b>판정 수단</b>이다. 여기서 틀리면 사람이
    /// 틀린 화면을 근거로 배선과 스케일을 확정한다. 그래서 "정상으로 보이면
    /// 안 되는 경우" 를 중심으로 짰다.</para>
    /// <para>XAML 은 대상이 아니다. 판단은 전부 ViewModel 에 있다.</para>
    /// </remarks>
    public class IoStatusViewModelTests
    {
        private static readonly DateTime T0 = new DateTime(2026, 8, 12, 0, 0, 0, DateTimeKind.Utc);

        // ─────────────────────────────────────────────────────────────────────
        // 램프
        // ─────────────────────────────────────────────────────────────────────

        [Fact]
        public void 램프는_설계서_순서대로_12종이다()
        {
            // 설명서와 화면의 순서가 다르면 대조할 때마다 눈이 헤맨다.
            IoStatusViewModel vm = new IoStatusViewModel();

            Assert.Equal(12, vm.Lamps.Count);
            Assert.Equal(IoLampSource.Ffu, vm.Lamps[0].Source);
            Assert.Equal(IoLampSource.Fdc, vm.Lamps[4].Source);
            Assert.Equal(IoLampSource.AirVelocity, vm.Lamps[11].Source);
        }

        [Fact]
        public void FDC_는_정상으로_표시되지_않는다()
        {
            // SECS/GEM 모듈이 없다. 초록으로 칠하면 없는 기능을 있다고 표시하는 것이다.
            IoStatusViewModel vm = Ready();

            vm.Apply(Snapshot(), null, T0);

            IoLampViewModel lamp = Lamp(vm, IoLampSource.Fdc);

            Assert.Equal(IoLampState.NotImplemented, lamp.State);
            Assert.NotEqual(IoLampState.Healthy, lamp.State);
        }

        [Fact]
        public void 구성에_없는_장치는_구성_없음_이다()
        {
            // 파티클·MFC·FFU 는 device-map 에 배정되지 않았다.
            IoStatusViewModel vm = Ready();

            vm.Apply(Snapshot(), null, T0);

            Assert.Equal(IoLampState.NotConfigured, Lamp(vm, IoLampSource.Particle).State);
            Assert.Equal(IoLampState.NotConfigured, Lamp(vm, IoLampSource.Mfc).State);
        }

        [Fact]
        public void 하나만_죽어도_그룹_램프는_무응답이_된다()
        {
            // 다섯 중 하나가 죽었는데 초록이면 그 하나는 아무도 찾지 않는다.
            IoStatusViewModel vm = Ready();

            vm.Apply(
                Snapshot(
                    Health("V-1", "ThrottleValve", Quality.Good, 2, 2),
                    Health("V-2", "ThrottleValve", Quality.Bad, 2, 0)),
                null,
                T0);

            IoLampViewModel lamp = Lamp(vm, IoLampSource.ThrottleValve);

            Assert.Equal(IoLampState.Failed, lamp.State);
            Assert.Contains("V-2", lamp.Detail);
        }

        [Fact]
        public void 값이_낡으면_열화로_표시된다()
        {
            IoStatusViewModel vm = Ready();

            vm.Apply(Snapshot(Health("V-1", "ThrottleValve", Quality.Stale, 2, 1)), null, T0);

            Assert.Equal(IoLampState.Degraded, Lamp(vm, IoLampSource.ThrottleValve).State);
        }

        [Fact]
        public void 폴링을_끈_그룹은_사용_안_함_이다()
        {
            // "꺼 두었음" 과 "구성에 없음" 을 같은 색으로 칠하면
            // 커미셔닝에서 멀쩡한 배선을 확인하러 장비를 연다.
            IoStatusViewModel vm = Ready();

            DeviceHealth disabled = new DeviceHealth(
                "V-1", null, "ThrottleValve", "CH2", false, Quality.NoData, DateTime.MinValue, 0, 0);

            vm.Apply(Snapshot(disabled), null, T0);

            Assert.Equal(IoLampState.Disabled, Lamp(vm, IoLampSource.ThrottleValve).State);
        }

        [Fact]
        public void 장치가_살아_있어도_값이_없으면_정상이_아니다()
        {
            // 판넬 온도가 그렇다. PLC 는 응답하지만 TC 채널이 배정되지 않았다.
            // 장치 상태만 보면 초록이 되어 없는 계측을 있다고 표시한다.
            IoStatusViewModel vm = Ready();

            SystemSnapshot snapshot = new SystemSnapshot(
                T0, null, null, null, Plc(Quality.Good), null, null, null,
                Map(Health("PLC-1", "Plc", Quality.Good, 8, 8)));

            vm.Apply(snapshot, null, T0);

            Assert.Equal(
                IoLampState.NotConfigured, Lamp(vm, IoLampSource.ControlBoxTemperature).State);
        }

        [Fact]
        public void 냉각팬은_상부와_하부를_구분해_적는다()
        {
            // 합쳐서만 보여 주면 제어함을 열어 봐야 어느 쪽이 멈췄는지 안다.
            IoStatusViewModel vm = Ready();

            PlcDigitalState plc = new PlcDigitalState(
                new bool[5], false, false, false, false, Quality.Good, T0, false, true);

            vm.Apply(
                new SystemSnapshot(T0, null, null, null, plc, null, null, null, Map()),
                null,
                T0);

            IoLampViewModel lamp = Lamp(vm, IoLampSource.CoolingFan);

            Assert.Equal(IoLampState.Failed, lamp.State);
            Assert.Equal("하부 정지", lamp.Detail);
        }

        [Fact]
        public void PLC_가_무응답이면_냉각팬_램프도_무응답이다()
        {
            // 비트가 0 이라고 "정지 없음" 이 아니다. 읽지 못한 것이다.
            IoStatusViewModel vm = Ready();

            vm.Apply(
                new SystemSnapshot(T0, null, null, null, Plc(Quality.Bad), null, null, null, Map()),
                null,
                T0);

            IoLampViewModel lamp = Lamp(vm, IoLampSource.CoolingFan);

            Assert.Equal(IoLampState.Failed, lamp.State);
            Assert.Equal("PLC 무응답", lamp.Detail);
        }

        // ─────────────────────────────────────────────────────────────────────
        // PLC 디지털 입력 — 극성 판정
        // ─────────────────────────────────────────────────────────────────────

        [Fact]
        public void PLC_입력_행은_설정의_비트와_극성을_보여준다()
        {
            // 판정값만 보면 극성이 뒤집혔는지 배선이 끊겼는지 구분하지 못한다.
            IoStatusViewModel vm = Ready();

            PlcInputRowViewModel emo = Row(vm, PointKeys.DiEmo);

            Assert.Equal("D10.0", emo.Address);
            Assert.Equal("Active H", emo.PolarityText);
            Assert.Equal("비상정지(EMO)", emo.Signal);
        }

        [Fact]
        public void 송풍팬_정지_입력이_행에_반영된다()
        {
            // D19 가 화면에 드러나는 자리다. 키가 어긋나 있으면 여기가 영원히 0 이다.
            IoStatusViewModel vm = Ready();

            bool[] stops = new bool[5];
            stops[2] = true;

            PlcDigitalState plc = new PlcDigitalState(
                stops, false, false, false, false, Quality.Good, T0);

            vm.Apply(
                new SystemSnapshot(T0, null, null, null, plc, null, null, null, Map()),
                null,
                T0);

            Assert.Equal("1 · 발생", Row(vm, PointKeys.DiFanStop(2)).StateText);
            Assert.Equal("0 · 없음", Row(vm, PointKeys.DiFanStop(0)).StateText);
        }

        [Fact]
        public void 배선되지_않은_입력도_행으로_남는다()
        {
            // 표에 없으면 "확인했더니 없더라" 와 "확인하지 않았다" 가 구분되지 않는다.
            IoStatusViewModel vm = Ready();

            PlcInputRowViewModel door = Row(vm, PointKeys.DiDoor);

            Assert.False(door.IsWired);
            Assert.Equal("미배선", door.StateText);
            Assert.Equal("- -", door.PolarityText);
        }

        // ─────────────────────────────────────────────────────────────────────
        // 압력 원시값 — 스케일 판정
        // ─────────────────────────────────────────────────────────────────────

        [Fact]
        public void 압력_행은_환산_전_레지스터를_함께_보여준다()
        {
            // 환산값만 보면 10배 틀어져도 그럴듯해 보인다.
            IoStatusViewModel vm = Ready();

            Dictionary<string, PressureReading> pressures =
                new Dictionary<string, PressureReading>(StringComparer.OrdinalIgnoreCase);

            // scale 0.1 Pa/LSB → 12.5 Pa 는 레지스터 125.
            pressures["S1-1"] = new PressureReading("S1-1", 12.0, 12.5, 0, 0.5, Quality.Good, T0);

            vm.Apply(
                new SystemSnapshot(T0, pressures, null, null, null, null, null, null, Map()),
                null,
                T0);

            PressureRawRowViewModel row = vm.Pressures[0];

            Assert.Equal("125", row.Register);
            Assert.Equal("12.5", row.RawPa);
            Assert.Equal("12", row.Pa);
            Assert.Equal("0.1", row.ScaleText);
        }

        [Fact]
        public void 수신하지_못한_압력은_숫자를_보여주지_않는다()
        {
            IoStatusViewModel vm = Ready();

            vm.Apply(Snapshot(), null, T0);

            Assert.Equal("- -", vm.Pressures[0].Register);
            Assert.Equal("- -", vm.Pressures[0].Pa);
        }

        // ─────────────────────────────────────────────────────────────────────
        // 스냅샷 나이
        // ─────────────────────────────────────────────────────────────────────

        [Fact]
        public void 스냅샷이_멈추면_드러난다()
        {
            // 값이 멈춘 화면과 값이 변하지 않는 공정은 구분되지 않는다.
            IoStatusViewModel vm = Ready();

            vm.Apply(Snapshot(), null, T0.AddSeconds(5.0));

            Assert.True(vm.IsStale);
            Assert.Contains("5000", vm.SnapshotAge);
        }

        [Fact]
        public void 방금_받은_스냅샷은_멈춘_것으로_보지_않는다()
        {
            IoStatusViewModel vm = Ready();

            vm.Apply(Snapshot(), null, T0.AddMilliseconds(120.0));

            Assert.False(vm.IsStale);
        }

        // ─────────────────────────────────────────────────────────────────────
        // 도우미
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>구성을 적용한 화면을 만든다.</summary>
        /// <returns>화면.</returns>
        private static IoStatusViewModel Ready()
        {
            IoStatusViewModel vm = new IoStatusViewModel();
            vm.Rebuild(CreateMap());
            return vm;
        }

        private static IoLampViewModel Lamp(IoStatusViewModel vm, IoLampSource source)
        {
            foreach (IoLampViewModel lamp in vm.Lamps)
            {
                if (lamp.Source == source)
                {
                    return lamp;
                }
            }

            throw new InvalidOperationException("램프가 없습니다: " + source);
        }

        private static PlcInputRowViewModel Row(IoStatusViewModel vm, string key)
        {
            foreach (PlcInputRowViewModel row in vm.PlcInputs)
            {
                if (string.Equals(row.Key, key, StringComparison.OrdinalIgnoreCase))
                {
                    return row;
                }
            }

            throw new InvalidOperationException("입력 행이 없습니다: " + key);
        }

        private static DeviceHealth Health(
            string id, string driver, Quality quality, int points, int good)
        {
            return new DeviceHealth(id, null, driver, "CH1", true, quality, T0, points, good);
        }

        private static PlcDigitalState Plc(Quality quality)
        {
            return new PlcDigitalState(new bool[5], false, false, false, false, quality, T0);
        }

        private static Dictionary<string, DeviceHealth> Map(params DeviceHealth[] entries)
        {
            Dictionary<string, DeviceHealth> map =
                new Dictionary<string, DeviceHealth>(StringComparer.OrdinalIgnoreCase);

            foreach (DeviceHealth entry in entries)
            {
                map[entry.DeviceId] = entry;
            }

            return map;
        }

        private static SystemSnapshot Snapshot(params DeviceHealth[] entries)
        {
            return new SystemSnapshot(
                T0, null, null, null, null, null, null, null, Map(entries));
        }

        /// <summary>차압센서 1대와 PLC 1대로 이루어진 최소 구성을 만든다.</summary>
        /// <returns>구성.</returns>
        private static DeviceMap CreateMap()
        {
            DeviceMap map = new DeviceMap();

            DeviceTypeDefinition sensor = new DeviceTypeDefinition();
            sensor.Driver = PointKeys.DriverPressureSensor;
            sensor.ReadGroups.Add(Group(Point(PointKeys.PressurePa, 0, 0.1)));
            map.DeviceTypes["DiffPressure"] = sensor;

            DeviceTypeDefinition plc = new DeviceTypeDefinition();
            plc.Driver = PointKeys.DriverPlc;

            ReadGroupDefinition digital = new ReadGroupDefinition();
            digital.Name = "digital";
            digital.Points.Add(Bit(PointKeys.DiEmo, 0));

            for (int i = 0; i < 5; i++)
            {
                digital.Points.Add(Bit(PointKeys.DiFanStop(i), i + 1));
            }

            digital.Points.Add(Bit(PointKeys.DiControlBoxFanTop, 6));
            digital.Points.Add(Bit(PointKeys.DiControlBoxFanBottom, 7));

            plc.ReadGroups.Add(digital);
            map.DeviceTypes["LsXbmPlc"] = plc;

            map.Devices.Add(new DeviceInstanceDefinition
            {
                Id = "S1-1", Type = "DiffPressure", Port = "CH1", SlaveId = 1, Offset = 0.5
            });

            map.Devices.Add(new DeviceInstanceDefinition
            {
                Id = "PLC-1", Type = "LsXbmPlc", Port = "CH1", SlaveId = 20
            });

            return map;
        }

        private static ReadGroupDefinition Group(PointDefinition point)
        {
            ReadGroupDefinition group = new ReadGroupDefinition();
            group.Name = "pressure";
            group.Points.Add(point);
            return group;
        }

        private static PointDefinition Point(string key, int offset, double scale)
        {
            PointDefinition point = new PointDefinition();
            point.Key = key;
            point.Offset = offset;
            point.Scale = scale;
            return point;
        }

        private static PointDefinition Bit(string key, int bit)
        {
            PointDefinition point = new PointDefinition();
            point.Key = key;
            point.Type = PointDataType.Bool;
            point.Bit = bit;
            point.ActiveHigh = true;
            return point;
        }
    }
}
