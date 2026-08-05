using System.Collections.Generic;
using Esam.Communication.Configuration;
using Esam.Communication.Polling;
using Xunit;

namespace Esam.Tests
{
    /// <summary>레지스터 주소 문자열 파싱 검증.</summary>
    public class RegisterAddressTests
    {
        [Theory]
        [InlineData("0x602B", 0x602B)]
        [InlineData("0X6202", 0x6202)]
        [InlineData("0x0147", 0x0147)]
        [InlineData("0", 0)]
        [InlineData("24619", 24619)]
        [InlineData("  0x1003  ", 0x1003)]
        public void 십육진수와_십진수를_모두_해석한다(string text, int expected)
        {
            ushort address;

            Assert.True(RegisterAddress.TryParse(text, out address));
            Assert.Equal((ushort)expected, address);
        }

        [Theory]
        [InlineData("TBD")]
        [InlineData("tbd")]
        [InlineData("TBD(D100)")]
        [InlineData("")]
        [InlineData(null)]
        public void 미확정_표기는_주소로_해석하지_않는다(string text)
        {
            // 미확정 주소를 0 으로 해석하면 엉뚱한 레지스터를 읽는다. 반드시 실패해야 한다.
            ushort address;

            Assert.True(RegisterAddress.IsUnspecified(text));
            Assert.False(RegisterAddress.TryParse(text, out address));
        }

        [Theory]
        [InlineData("0xZZZZ")]
        [InlineData("65536")]
        [InlineData("-1")]
        [InlineData("abc")]
        public void 잘못된_형식은_실패시킨다(string text)
        {
            ushort address;

            Assert.False(RegisterAddress.TryParse(text, out address));
        }

        [Fact]
        public void Parse는_실패시_위치를_포함한_예외를_던진다()
        {
            System.FormatException ex = Assert.Throws<System.FormatException>(
                () => RegisterAddress.Parse("bad", "WTDM550.pressure.startAddress"));

            Assert.Contains("WTDM550.pressure.startAddress", ex.Message);
        }

        [Fact]
        public void 십육진수_표기로_되돌릴_수_있다()
        {
            Assert.Equal("0x602B", RegisterAddress.ToHex(0x602B));
        }
    }

    /// <summary>
    /// 포인트 디코더 검증. 통신 오류보다 디코딩 오류가 위험하므로 전 타입을 검증한다.
    /// </summary>
    public class PointDecoderTests
    {
        private static PointDefinition Point(
            PointDataType type, int offset = 0, double scale = 1.0, double bias = 0.0)
        {
            PointDefinition point = new PointDefinition();
            point.Key = "test";
            point.Type = type;
            point.Offset = offset;
            point.Scale = scale;
            point.Bias = bias;
            return point;
        }

        [Fact]
        public void UInt16은_부호없이_해석한다()
        {
            double value;

            Assert.True(PointDecoder.TryDecode(
                Point(PointDataType.UInt16), new ushort[] { 0xF830 }, out value));

            Assert.Equal(63536.0, value, 6);
        }

        [Fact]
        public void Int16은_음수를_올바르게_해석한다()
        {
            // -200 Pa / 0.1 Pa-per-LSB = -2000 = 0xF830
            // 이것을 UInt16 으로 읽으면 63536 이 되어 제어가 정반대로 동작한다.
            double value;

            Assert.True(PointDecoder.TryDecode(
                Point(PointDataType.Int16, scale: 0.1), new ushort[] { 0xF830 }, out value));

            Assert.Equal(-200.0, value, 6);
        }

        [Theory]
        [InlineData(0x0000, 0.0)]
        [InlineData(0x03E8, 100.0)]
        [InlineData(0xFFFF, -0.1)]
        [InlineData(0x8000, -3276.8)]
        [InlineData(0x7FFF, 3276.7)]
        public void Int16_경계값을_정확히_해석한다(int raw, double expected)
        {
            double value;

            Assert.True(PointDecoder.TryDecode(
                Point(PointDataType.Int16, scale: 0.1), new ushort[] { (ushort)raw }, out value));

            Assert.Equal(expected, value, 6);
        }

        [Fact]
        public void 상위워드_우선_32비트를_해석한다()
        {
            double value;

            Assert.True(PointDecoder.TryDecode(
                Point(PointDataType.UInt32), new ushort[] { 0x0001, 0x0000 }, out value));

            Assert.Equal(65536.0, value, 6);
        }

        [Fact]
        public void 하위워드_우선_32비트를_해석한다()
        {
            PointDefinition point = Point(PointDataType.UInt32);
            point.WordOrder = WordOrder.LowWordFirst;

            double value;
            Assert.True(PointDecoder.TryDecode(point, new ushort[] { 0x0000, 0x0001 }, out value));

            Assert.Equal(65536.0, value, 6);
        }

        [Fact]
        public void Int32는_음수를_올바르게_해석한다()
        {
            // 0xFFFFFFFF = -1
            double value;

            Assert.True(PointDecoder.TryDecode(
                Point(PointDataType.Int32), new ushort[] { 0xFFFF, 0xFFFF }, out value));

            Assert.Equal(-1.0, value, 6);
        }

        [Theory]
        [InlineData(0, 0x0001, true)]
        [InlineData(1, 0x0002, true)]
        [InlineData(6, 0x0040, true)]
        [InlineData(8, 0x0100, true)]
        [InlineData(8, 0x0000, false)]
        [InlineData(15, 0x8000, true)]
        public void 비트를_정확히_추출한다(int bit, int register, bool expected)
        {
            // PLC D10.0~D10.8 판독. 워드 1개를 읽어 9개 비트를 추출한다.
            PointDefinition point = Point(PointDataType.Bool);
            point.Bit = bit;

            double value;
            Assert.True(PointDecoder.TryDecode(point, new ushort[] { (ushort)register }, out value));

            Assert.Equal(expected, value != 0.0);
        }

        [Fact]
        public void ActiveLow_비트는_극성을_반전한다()
        {
            // EMO·차단기는 Fail-Safe 관점에서 "정상 = 1" 배선일 수 있다(Open Issue #18).
            // 극성을 코드가 아니라 설정으로 다뤄야 하는 이유다.
            PointDefinition point = Point(PointDataType.Bool);
            point.Bit = 6;
            point.ActiveHigh = false;

            double whenSet;
            double whenClear;
            PointDecoder.TryDecode(point, new ushort[] { 0x0040 }, out whenSet);
            PointDecoder.TryDecode(point, new ushort[] { 0x0000 }, out whenClear);

            Assert.Equal(0.0, whenSet);
            Assert.Equal(1.0, whenClear);
        }

        [Fact]
        public void 논리값에는_배율과_바이어스를_적용하지_않는다()
        {
            PointDefinition point = Point(PointDataType.Bool, scale: 100.0, bias: 50.0);
            point.Bit = 0;

            double value;
            PointDecoder.TryDecode(point, new ushort[] { 0x0001 }, out value);

            Assert.Equal(1.0, value);
        }

        [Fact]
        public void 배율과_바이어스를_함께_적용한다()
        {
            double value;

            Assert.True(PointDecoder.TryDecode(
                Point(PointDataType.UInt16, scale: 0.5, bias: -10.0),
                new ushort[] { 100 }, out value));

            Assert.Equal(40.0, value, 6);
        }

        [Fact]
        public void 오프셋이_범위를_벗어나면_실패한다()
        {
            // 장치가 선언보다 적은 레지스터를 응답하는 경우를 막는다.
            double value;

            Assert.False(PointDecoder.TryDecode(
                Point(PointDataType.UInt16, offset: 5), new ushort[] { 1, 2 }, out value));

            Assert.False(PointDecoder.TryDecode(
                Point(PointDataType.UInt32, offset: 1), new ushort[] { 1, 2 }, out value));
        }

        [Fact]
        public void 인자가_null이면_실패한다()
        {
            double value;

            Assert.False(PointDecoder.TryDecode(null, new ushort[] { 1 }, out value));
            Assert.False(PointDecoder.TryDecode(Point(PointDataType.UInt16), null, out value));
        }

        [Theory]
        [InlineData(-200.0, 0.1, 0xF830)]
        [InlineData(2500.0, 1.0, 2500)]
        [InlineData(0.0, 1.0, 0)]
        public void 공학값을_레지스터로_역변환한다(double value, double scale, int expected)
        {
            PointDefinition point = Point(PointDataType.Int16, scale: scale);

            Assert.Equal((ushort)expected, PointDecoder.EncodeUInt16(point, value));
        }
    }

    /// <summary>디바이스 맵 검증 규칙 확인(COMM_MAP.md 4.7).</summary>
    public class DeviceMapValidationTests
    {
        private static DeviceMap CreateValidMap()
        {
            DeviceMap map = new DeviceMap();

            PortDefinition port = new PortDefinition();
            port.Serial.PortId = "BUS_A";
            port.Serial.PortName = "COM3";
            map.Ports.Add(port);

            DeviceTypeDefinition sensorType = new DeviceTypeDefinition();
            sensorType.Driver = "PressureSensor";

            ReadGroupDefinition group = new ReadGroupDefinition();
            group.Name = "pressure";
            group.StartAddress = "0x0000";
            group.Count = 1;
            group.Points.Add(new PointDefinition
            {
                Key = "pressurePa",
                Type = PointDataType.Int16,
                Scale = 0.1
            });

            sensorType.ReadGroups.Add(group);
            map.DeviceTypes["WTDM550"] = sensorType;

            map.Devices.Add(new DeviceInstanceDefinition
            {
                Id = "S1-1",
                Type = "WTDM550",
                Port = "BUS_A",
                SlaveId = 1
            });

            return map;
        }

        [Fact]
        public void 정상_구성은_검증을_통과한다()
        {
            IList<string> errors;
            IList<string> warnings;

            Assert.True(CreateValidMap().Validate(out errors, out warnings));
            Assert.Empty(errors);
        }

        [Fact]
        public void 같은_포트에_슬레이브_ID가_겹치면_오류다()
        {
            // DESIGN.md 2.2(A) 에서 지적한 실제 배선 위험이다.
            // 같은 버스에 ID 가 겹치면 두 장치가 동시에 응답해 프레임이 깨지고,
            // 증상은 간헐적 CRC 오류로만 나타나 원인 추적이 매우 어렵다.
            DeviceMap map = CreateValidMap();
            map.Devices.Add(new DeviceInstanceDefinition
            {
                Id = "V-1",
                Type = "WTDM550",
                Port = "BUS_A",
                SlaveId = 1
            });

            IList<string> errors;
            IList<string> warnings;

            Assert.False(map.Validate(out errors, out warnings));
            Assert.Contains(errors, e => e.Contains("슬레이브 ID 1") && e.Contains("중복"));
        }

        [Fact]
        public void 다른_포트라면_슬레이브_ID가_같아도_된다()
        {
            // 센서(BUS_A) 1~13 과 밸브(BUS_B) 1~5 가 공존할 수 있어야 한다.
            DeviceMap map = CreateValidMap();

            PortDefinition portB = new PortDefinition();
            portB.Serial.PortId = "BUS_B";
            portB.Serial.PortName = "COM4";
            map.Ports.Add(portB);

            map.Devices.Add(new DeviceInstanceDefinition
            {
                Id = "V-1",
                Type = "WTDM550",
                Port = "BUS_B",
                SlaveId = 1
            });

            IList<string> errors;
            IList<string> warnings;

            Assert.True(map.Validate(out errors, out warnings));
        }

        [Fact]
        public void 비활성_디바이스는_ID_충돌_검사에서_제외된다()
        {
            DeviceMap map = CreateValidMap();
            map.Devices.Add(new DeviceInstanceDefinition
            {
                Id = "S1-spare",
                Type = "WTDM550",
                Port = "BUS_A",
                SlaveId = 1,
                Enabled = false
            });

            IList<string> errors;
            IList<string> warnings;

            Assert.True(map.Validate(out errors, out warnings));
        }

        [Fact]
        public void 같은_COM포트를_두_논리포트가_쓰면_오류다()
        {
            DeviceMap map = CreateValidMap();

            PortDefinition duplicate = new PortDefinition();
            duplicate.Serial.PortId = "BUS_B";
            duplicate.Serial.PortName = "COM3";
            map.Ports.Add(duplicate);

            IList<string> errors;
            IList<string> warnings;

            Assert.False(map.Validate(out errors, out warnings));
            Assert.Contains(errors, e => e.Contains("COM3"));
        }

        [Fact]
        public void 존재하지_않는_포트나_종류를_참조하면_오류다()
        {
            DeviceMap map = CreateValidMap();
            map.Devices.Add(new DeviceInstanceDefinition
            {
                Id = "X-1",
                Type = "UnknownType",
                Port = "BUS_Z",
                SlaveId = 30
            });

            IList<string> errors;
            IList<string> warnings;

            Assert.False(map.Validate(out errors, out warnings));
            Assert.Contains(errors, e => e.Contains("BUS_Z"));
            Assert.Contains(errors, e => e.Contains("UnknownType"));
        }

        [Fact]
        public void 측정점_오프셋이_읽기범위를_넘으면_오류다()
        {
            // 런타임 IndexOutOfRange 대신 설정 로드 시점에 잡아야 한다.
            DeviceMap map = CreateValidMap();
            map.DeviceTypes["WTDM550"].ReadGroups[0].Points.Add(new PointDefinition
            {
                Key = "extra",
                Offset = 3,
                Type = PointDataType.UInt16
            });

            IList<string> errors;
            IList<string> warnings;

            Assert.False(map.Validate(out errors, out warnings));
            Assert.Contains(errors, e => e.Contains("읽기 범위"));
        }

        [Fact]
        public void 주소_미확정은_오류가_아니라_경고다()
        {
            // 명세 미확보 장치가 섞여 있어도 나머지는 정상 폴링되어야 한다.
            DeviceMap map = CreateValidMap();
            map.DeviceTypes["WTDM550"].ReadGroups[0].StartAddress = "TBD";

            IList<string> errors;
            IList<string> warnings;

            Assert.True(map.Validate(out errors, out warnings));
            Assert.Contains(warnings, w => w.Contains("TBD"));
        }

        [Fact]
        public void 폴링_주기_순서가_뒤바뀌면_오류다()
        {
            DeviceMap map = CreateValidMap();
            map.Ports[0].Polling.FastMs = 5000;
            map.Ports[0].Polling.SlowMs = 200;

            IList<string> errors;
            IList<string> warnings;

            Assert.False(map.Validate(out errors, out warnings));
        }

        [Fact]
        public void 배율이_0이면_오류다()
        {
            // 모든 측정값이 0 이 되어버린다. 설정 실수일 가능성이 매우 높다.
            DeviceMap map = CreateValidMap();
            map.DeviceTypes["WTDM550"].ReadGroups[0].Points[0].Scale = 0.0;

            IList<string> errors;
            IList<string> warnings;

            Assert.False(map.Validate(out errors, out warnings));
            Assert.Contains(errors, e => e.Contains("scale"));
        }

        [Fact]
        public void 슬레이브_ID_범위를_벗어나면_오류다()
        {
            DeviceMap map = CreateValidMap();
            map.Devices[0].SlaveId = 0;

            IList<string> errors;
            IList<string> warnings;

            Assert.False(map.Validate(out errors, out warnings));
        }

        [Fact]
        public void 측정점_키가_여러_그룹에_중복되면_오류다()
        {
            // 두 그룹이 같은 키를 쓰면 이동평균 필터를 공유해 서로 다른 신호가 섞이고,
            // 상위로 올라가는 "디바이스ID.키" 경로도 충돌해 한쪽 값이 사라진다.
            DeviceMap map = CreateValidMap();

            ReadGroupDefinition second = new ReadGroupDefinition();
            second.Name = "duplicate";
            second.StartAddress = "0x0010";
            second.Count = 1;
            second.Points.Add(new PointDefinition
            {
                Key = "pressurePa",
                Type = PointDataType.Int16
            });

            map.DeviceTypes["WTDM550"].ReadGroups.Add(second);

            IList<string> errors;
            IList<string> warnings;

            Assert.False(map.Validate(out errors, out warnings));
            Assert.Contains(errors, e => e.Contains("여러 읽기 그룹에 중복"));
        }
    }

    /// <summary>JSON 로더 검증.</summary>
    public class ConfigLoaderTests
    {
        private const string MinimalJson = @"{
  // 주석이 허용되어야 한다
  ""schemaVersion"": ""1.0"",
  ""ports"": [
    {
      ""serial"": { ""portId"": ""BUS_A"", ""portName"": ""COM3"", ""baudRate"": 19200 },
      ""polling"": { ""fastMs"": 200, ""mediumMs"": 1000, ""slowMs"": 5000 }
    }
  ],
  ""deviceTypes"": {
    ""WTDM550"": {
      ""driver"": ""PressureSensor"",
      ""readGroups"": [
        {
          ""name"": ""pressure"",
          ""tier"": ""Fast"",
          ""functionCode"": 3,
          ""startAddress"": ""0x0000"",
          ""count"": 1,
          ""points"": [
            { ""key"": ""pressurePa"", ""offset"": 0, ""type"": ""Int16"", ""scale"": 0.1, ""unit"": ""Pa"" }
          ]
        }
      ]
    }
  },
  ""devices"": [
    { ""id"": ""S1-1"", ""type"": ""WTDM550"", ""port"": ""BUS_A"", ""slaveId"": 1 }
  ]
}";

        [Fact]
        public void 주석이_포함된_JSON을_로드한다()
        {
            ConfigLoadResult result = CommunicationConfigLoader.LoadFromJson(MinimalJson);

            Assert.True(result.IsSuccess, result.BuildReport());
            Assert.Single(result.Map.Devices);
            Assert.Equal("S1-1", result.Map.Devices[0].Id);
        }

        [Fact]
        public void 열거형을_문자열로_읽는다()
        {
            ConfigLoadResult result = CommunicationConfigLoader.LoadFromJson(MinimalJson);

            ReadGroupDefinition group = result.Map.DeviceTypes["WTDM550"].ReadGroups[0];

            Assert.Equal(PollingTier.Fast, group.Tier);
            Assert.Equal(PointDataType.Int16, group.Points[0].Type);
        }

        [Fact]
        public void 십육진수_주소_문자열을_보존한다()
        {
            ConfigLoadResult result = CommunicationConfigLoader.LoadFromJson(MinimalJson);

            ushort address;
            Assert.True(RegisterAddress.TryParse(
                result.Map.DeviceTypes["WTDM550"].ReadGroups[0].StartAddress, out address));

            Assert.Equal(0, address);
        }

        [Fact]
        public void 알_수_없는_속성명은_로드_오류로_처리한다()
        {
            // 오타를 조용히 무시하면 설정이 반영되지 않은 채 운전된다.
            // 예를 들어 slaveId 를 slave_id 로 쓰면 기본값 0 이 남아 통신이 안 되는데
            // 원인을 찾기 매우 어렵다. MissingMemberHandling.Error 로 즉시 드러낸다.
            string typo = MinimalJson.Replace(@"""slaveId"": 1", @"""slave_id"": 1");

            ConfigLoadResult result = CommunicationConfigLoader.LoadFromJson(typo);

            Assert.False(result.IsSuccess);
            Assert.NotEmpty(result.Errors);
        }

        [Fact]
        public void 대소문자만_다른_속성명은_허용된다()
        {
            // Newtonsoft 는 정확히 일치하는 속성이 없으면 대소문자 무시로 한 번 더 찾는다.
            // 따라서 "slaveID" 같은 대소문자 오타는 오류가 아니라 정상 반영된다.
            // 이 동작을 모르면 "오타를 다 잡아준다"고 착각하게 되므로 명시적으로 고정해 둔다.
            string casing = MinimalJson.Replace(@"""slaveId"": 1", @"""slaveID"": 1");

            ConfigLoadResult result = CommunicationConfigLoader.LoadFromJson(casing);

            Assert.True(result.IsSuccess, result.BuildReport());
            Assert.Equal(1, result.Map.Devices[0].SlaveId);
        }

        [Fact]
        public void 잘못된_JSON은_오류_메시지를_반환한다()
        {
            ConfigLoadResult result = CommunicationConfigLoader.LoadFromJson("{ not json");

            Assert.False(result.IsSuccess);
            Assert.Contains(result.Errors, e => e.Contains("JSON"));
        }

        [Fact]
        public void 빈_내용은_오류다()
        {
            Assert.False(CommunicationConfigLoader.LoadFromJson(string.Empty).IsSuccess);
            Assert.False(CommunicationConfigLoader.LoadFromJson(null).IsSuccess);
        }

        [Fact]
        public void 없는_파일은_경로를_포함한_오류를_반환한다()
        {
            ConfigLoadResult result = CommunicationConfigLoader.LoadFromFile("Z:\\없는경로\\device-map.json");

            Assert.False(result.IsSuccess);
            Assert.NotEmpty(result.Errors);
        }

        [Fact]
        public void 직렬화한_결과를_다시_로드할_수_있다()
        {
            ConfigLoadResult first = CommunicationConfigLoader.LoadFromJson(MinimalJson);
            string json = CommunicationConfigLoader.ToJson(first.Map);

            ConfigLoadResult second = CommunicationConfigLoader.LoadFromJson(json);

            Assert.True(second.IsSuccess, second.BuildReport());
            Assert.Equal(first.Map.Devices.Count, second.Map.Devices.Count);
        }

        [Fact]
        public void 진단_보고서에_오류와_경고가_모두_담긴다()
        {
            string withTbd = MinimalJson.Replace(@"""startAddress"": ""0x0000""", @"""startAddress"": ""TBD""");

            ConfigLoadResult result = CommunicationConfigLoader.LoadFromJson(withTbd);

            Assert.True(result.IsSuccess);
            Assert.Contains("경고", result.BuildReport());
        }
    }
}
