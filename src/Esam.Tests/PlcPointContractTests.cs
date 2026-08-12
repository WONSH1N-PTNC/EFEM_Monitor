using System;
using System.Collections.Generic;
using System.IO;
using Esam.Communication.Abstractions;
using Esam.Communication.Configuration;
using Esam.Communication.Polling;
using Esam.Domain.Models;
using Esam.Services;
using Xunit;

namespace Esam.Tests
{
    /// <summary>
    /// 배포 <c>device-map.json</c> 의 PLC 측정점 키와 <see cref="SnapshotBuilder"/> 가
    /// 실제로 조회하는 키가 일치하는지 대조한다(D19 회귀 방지).
    /// </summary>
    /// <remarks>
    /// <para><b>D19 는 점 하나였다.</b> 코드는 <c>"di.fanStop0"</c> 을 찾고 설정은
    /// <c>"di.fanStop.0"</c> 을 선언했다. 조회가 항상 실패해 송풍팬 정지·과열 알람
    /// 10건(AL-19·22·25·28·31·32~36)이 영원히 울리지 않았다.</para>
    /// <para>통합 테스트 441건이 놓친 이유는 테스트가 <b>자체 맵</b>을 코드와 같은
    /// 방식으로 만들었기 때문이다. 양쪽이 같은 오타를 공유하면 어긋난 사실이 드러나지 않는다.
    /// 그래서 이 테스트는 <b>배포되는 파일</b>을 읽는다.</para>
    /// <para>키가 맞아떨어지는지는 컴파일러가 잡아 주지 못한다. 문자열이기 때문이다.
    /// 잡아 줄 수 있는 것이 이 대조뿐이다.</para>
    /// </remarks>
    public class PlcPointContractTests
    {
        private const string DeviceMapPath = "config/device-map.json";

        /// <summary>
        /// 설정에는 있지만 아직 소비하지 않는 측정점.
        /// </summary>
        /// <remarks>
        /// 목록으로 못박아 두면 새 측정점을 추가하고 배선만 한 뒤 소비를 잊었을 때
        /// 이 테스트가 실패한다. "선언했으니 동작하겠지" 를 막는 장치다.
        /// </remarks>
        private static readonly string[] KnownUnconsumed =
        {
            "temp.fault.0", "temp.fault.1", "temp.fault.2", "temp.fault.3", "temp.fault.4"
        };

        [Fact]
        public void SnapshotBuilder_가_찾는_PLC_키가_배포_설정에_모두_있다()
        {
            HashSet<string> declared = DeclaredPlcKeys();

            foreach (string key in RequiredKeys())
            {
                Assert.True(
                    declared.Contains(key),
                    "device-map.json 의 PLC 타입에 측정점 '" + key + "' 가 없습니다. "
                    + "SnapshotBuilder 가 이 키로 조회하므로 값이 영원히 채워지지 않습니다.");
            }
        }

        [Fact]
        public void 배포_설정의_PLC_키를_빠짐없이_소비한다()
        {
            HashSet<string> consumed = new HashSet<string>(
                ConsumedKeys(), StringComparer.OrdinalIgnoreCase);

            foreach (string known in KnownUnconsumed)
            {
                consumed.Add(known);
            }

            foreach (string declared in DeclaredPlcKeys())
            {
                Assert.True(
                    consumed.Contains(declared),
                    "device-map.json 이 선언한 PLC 측정점 '" + declared + "' 를 "
                    + "읽는 코드가 없습니다. 배선하고 선언까지 했는데 값이 쓰이지 않는 상태입니다.");
            }
        }

        [Fact]
        public void 송풍팬_정지_입력이_스냅샷에_반영된다()
        {
            // D19 의 직접 재현. 설정 파일과 같은 형식의 키를 주고
            // PlcDigitalState 까지 값이 도달하는지 본다.
            SnapshotBuilder builder = new SnapshotBuilder(PlcOnlyMap());

            builder.Apply(Poll(
                Success(
                    "PLC-1",
                    Sample("PLC-1", "di.emo", 0.0),
                    Sample("PLC-1", "di.fanStop.2", 1.0))));

            SystemSnapshot snapshot = builder.Build(null, null, T0);

            Assert.True(
                snapshot.Plc.FanStopAlarms[2],
                "송풍팬 3 정지 입력이 스냅샷에 반영되지 않았습니다. AL-34 가 울리지 않습니다.");

            Assert.False(snapshot.Plc.FanStopAlarms[0]);
        }

        [Fact]
        public void 송풍팬_온도가_보조_계측에_반영된다()
        {
            SnapshotBuilder builder = new SnapshotBuilder(PlcOnlyMap());

            builder.Apply(Poll(
                Success(
                    "PLC-1",
                    Sample("PLC-1", "di.emo", 0.0),
                    Sample("PLC-1", "temp.fan.4", 71.5))));

            SystemSnapshot snapshot = builder.Build(null, null, T0);

            Assert.Equal(71.5, snapshot.Auxiliary.FanTemperatures[4].Value, 3);
            Assert.False(snapshot.Auxiliary.FanTemperatures[0].HasValue);
        }

        [Fact]
        public void 키_조립은_문화권에_영향받지_않는다()
        {
            // 아라비아 숫자가 아닌 자릿수를 쓰는 지역 설정에서 키가 달라지면
            // 조회가 조용히 실패한다. D19 와 증상이 같아진다.
            Assert.Equal("di.fanStop.0", PointKeys.DiFanStop(0));
            Assert.Equal("temp.fan.4", PointKeys.TempFan(4));
            Assert.Equal("temp.fault.3", PointKeys.TempFault(3));
        }

        // ─────────────────────────────────────────────────────────────────────
        // 도우미
        // ─────────────────────────────────────────────────────────────────────

        private static readonly DateTime T0 = new DateTime(2026, 8, 12, 0, 0, 0, DateTimeKind.Utc);

        /// <summary><see cref="SnapshotBuilder"/> 가 반드시 찾아야 하는 키.</summary>
        /// <returns>키 목록.</returns>
        /// <remarks>
        /// 도어(<c>di.door</c>)·메인 차단기(<c>di.mainBreaker</c>)·판넬 온도
        /// (<c>temp.panel</c>)는 <b>배선된 입력이 없어</b> 설정에 선언되지 않는다.
        /// 필수로 넣으면 정상 구성에서 테스트가 실패한다.
        /// </remarks>
        private static IEnumerable<string> RequiredKeys()
        {
            List<string> keys = new List<string>();

            keys.Add(PointKeys.DiEmo);
            keys.Add(PointKeys.DiControlBoxFanTop);
            keys.Add(PointKeys.DiControlBoxFanBottom);

            for (int i = 0; i < 5; i++)
            {
                keys.Add(PointKeys.DiFanStop(i));
                keys.Add(PointKeys.TempFan(i));
            }

            return keys;
        }

        /// <summary><see cref="SnapshotBuilder"/> 가 조회하는 모든 키.</summary>
        /// <returns>키 목록.</returns>
        private static IEnumerable<string> ConsumedKeys()
        {
            List<string> keys = new List<string>(RequiredKeys());

            keys.Add(PointKeys.DiDoor);
            keys.Add(PointKeys.DiMainBreaker);
            keys.Add(PointKeys.TempPanel);

            return keys;
        }

        /// <summary>배포 설정에서 PLC 타입이 선언한 측정점 키를 모은다.</summary>
        /// <returns>키 집합.</returns>
        private static HashSet<string> DeclaredPlcKeys()
        {
            Assert.True(File.Exists(DeviceMapPath), "배포용 device-map.json 이 출력 폴더에 없습니다.");

            ConfigLoadResult result = CommunicationConfigLoader.LoadFromFile(DeviceMapPath);

            Assert.True(result.IsSuccess, "통신 구성 오류:\n" + string.Join("\n", result.Errors));

            HashSet<string> keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            bool found = false;

            foreach (KeyValuePair<string, DeviceTypeDefinition> pair in result.Map.DeviceTypes)
            {
                if (!string.Equals(pair.Value.Driver, PointKeys.DriverPlc, StringComparison.Ordinal))
                {
                    continue;
                }

                found = true;

                foreach (ReadGroupDefinition group in pair.Value.ReadGroups)
                {
                    foreach (PointDefinition point in group.Points)
                    {
                        keys.Add(point.Key);
                    }
                }
            }

            Assert.True(found, "device-map.json 에 driver 가 Plc 인 디바이스 타입이 없습니다.");

            return keys;
        }

        /// <summary>PLC 1대만 있는 최소 구성을 만든다.</summary>
        /// <returns>구성.</returns>
        private static DeviceMap PlcOnlyMap()
        {
            DeviceMap map = new DeviceMap();

            DeviceTypeDefinition type = new DeviceTypeDefinition();
            type.Driver = PointKeys.DriverPlc;
            map.DeviceTypes["LsXbmPlc"] = type;

            map.Devices.Add(new DeviceInstanceDefinition
            {
                Id = "PLC-1",
                Type = "LsXbmPlc",
                Port = "CH1",
                SlaveId = 1
            });

            return map;
        }

        private static PointSample Sample(string deviceId, string key, double value)
        {
            return new PointSample(deviceId, key, value, value, Quality.Good, null, T0);
        }

        private static GroupReadResult Success(string deviceId, params PointSample[] samples)
        {
            return new GroupReadResult(
                deviceId, "digital", PollingTier.Fast, true,
                ModbusFailureKind.None, null, 1.0, samples);
        }

        private static PollCompletedEventArgs Poll(params GroupReadResult[] results)
        {
            return new PollCompletedEventArgs("CH1", T0, 1.0, null, results);
        }
    }
}
