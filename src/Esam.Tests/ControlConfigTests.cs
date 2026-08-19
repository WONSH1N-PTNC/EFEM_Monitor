using System;
using System.Collections.Generic;
using System.IO;
using Esam.Communication.Configuration;
using Esam.Domain.Configuration;
using Esam.Domain.Control;
using Xunit;

namespace Esam.Tests
{
    /// <summary>
    /// 배포되는 <c>control.json</c> 을 검증한다(C5).
    /// </summary>
    /// <remarks>
    /// <para>이 파일이 생기기 전까지 Step·Dwell·밸브 최대 개도 같은 제어 파라미터가
    /// <b>전부 코드 기본값</b>이었다. 현장에서 하나를 조정하려면 재컴파일이 필요했고,
    /// 재컴파일이 필요하면 아무도 조정하지 않는다.</para>
    /// <para>통로 활성화도 여기 있다. 종전에는 화면에서만 바뀌고 저장되지 않아
    /// <b>재시작하면 전부 켜진 상태로 돌아갔다</b>(D22).</para>
    /// </remarks>
    public class ControlConfigTests
    {
        private const string ControlPath = "config/control.json";
        private const string DeviceMapPath = "config/device-map.json";

        // ─────────────────────────────────────────────────────────────────────
        // 배포 파일
        // ─────────────────────────────────────────────────────────────────────

        [Fact]
        public void 배포용_control_json이_오류_없이_로드된다()
        {
            ControlLoadResult result = LoadShipped();

            Assert.True(
                result.IsSuccess,
                "제어 설정 오류:\n" + string.Join("\n", result.Errors));
        }

        [Fact]
        public void 통로_5조가_모두_정의된다()
        {
            ControlConfig config = LoadShipped().Config;

            Assert.Equal(5, config.Chains.Count);

            for (int i = 1; i <= 5; i++)
            {
                ChainDefinition chain = FindChain(config, i);

                Assert.Equal("V-" + i, chain.ValveId);
                Assert.Equal("F-" + i, chain.FanId);
                Assert.Equal("S2-" + i, chain.Sensor2Id);
                Assert.Equal("S3-" + i, chain.Sensor3Id);
                Assert.True(chain.Enabled);
            }
        }

        [Fact]
        public void 통로가_참조하는_디바이스가_통신_구성에_모두_있다()
        {
            // 참조가 끊어지면 그 통로는 영원히 제어되지 않는데,
            // 화면에는 정상으로 보인다.
            ControlConfig config = LoadShipped().Config;
            DeviceMap map = LoadMap();

            HashSet<string> ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (DeviceInstanceDefinition device in map.Devices)
            {
                ids.Add(device.Id);
            }

            // 끊어진 참조를 모아 한 번에 알린다. 하나씩 단언하면 첫 번째에서 멈춰
            // 나머지가 성한지 알 수 없다. 통로 구성을 고칠 때는 전부 보여야 한다.
            List<string> missing = new List<string>();

            foreach (ChainDefinition chain in config.Chains)
            {
                Collect(missing, ids, chain.Id, "밸브", chain.ValveId);
                Collect(missing, ids, chain.Id, "팬", chain.FanId);
                Collect(missing, ids, chain.Id, "센서 1", chain.Sensor1Id);
                Collect(missing, ids, chain.Id, "센서 2", chain.Sensor2Id);
                Collect(missing, ids, chain.Id, "센서 3", chain.Sensor3Id);
            }

            if (!ids.Contains(config.Sensor1Reference))
            {
                missing.Add("sensor1Reference: " + config.Sensor1Reference);
            }

            Assert.True(
                missing.Count == 0,
                "통신 구성에 없는 디바이스를 참조합니다:\n" + string.Join("\n", missing));
        }

        /// <summary>참조가 끊어졌으면 목록에 담는다.</summary>
        /// <param name="missing">누락 목록.</param>
        /// <param name="ids">구성에 있는 디바이스 ID.</param>
        /// <param name="chainId">통로 번호.</param>
        /// <param name="role">역할 표기.</param>
        /// <param name="deviceId">참조하는 디바이스 ID.</param>
        private static void Collect(
            List<string> missing, HashSet<string> ids, int chainId, string role, string deviceId)
        {
            if (string.IsNullOrEmpty(deviceId) || ids.Contains(deviceId))
            {
                return;
            }

            missing.Add(string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "통로 {0} {1}: {2}", chainId, role, deviceId));
        }

        [Fact]
        public void 파일_값이_코드_기본값과_일치한다()
        {
            // 파일을 신설하면서 값이 달라지면 그 순간부터 설비가 다르게 움직인다.
            // C5 는 값을 옮기는 작업이지 바꾸는 작업이 아니다.
            ControlConfig fromFile = LoadShipped().Config;
            ControlConfig defaults = new ControlConfig();

            Assert.Equal(defaults.ActiveMode, fromFile.ActiveMode);
            Assert.Equal(defaults.Policy, fromFile.Policy);
            Assert.Equal(defaults.ControlPeriodMs, fromFile.ControlPeriodMs);
            Assert.Equal(defaults.Sensor1Reference, fromFile.Sensor1Reference);
            Assert.Equal(defaults.FilterWindowSize, fromFile.FilterWindowSize);

            // ★ C5 때 이 대조에서 빠져 있던 종류의 값이다.
            // 파일이 코드 기본값과 달라지면 그 순간부터 안전 판정이 다르게 움직인다.
            Assert.Equal(defaults.SafetyInputGraceMs, fromFile.SafetyInputGraceMs);

            Assert.Equal(defaults.Valve.StepPulse, fromFile.Valve.StepPulse);
            Assert.Equal(defaults.Valve.MaxPulse, fromFile.Valve.MaxPulse);
            Assert.Equal(defaults.Valve.DwellMs, fromFile.Valve.DwellMs);

            Assert.Equal(defaults.Fan.StepRpm, fromFile.Fan.StepRpm, 3);
            Assert.Equal(defaults.Fan.MinRpm, fromFile.Fan.MinRpm, 3);
            Assert.Equal(defaults.Fan.MaxRpm, fromFile.Fan.MaxRpm, 3);
        }

        [Fact]
        public void 모드별_확정_시간이_그대로_옮겨졌다()
        {
            ControlConfig fromFile = LoadShipped().Config;
            ControlConfig defaults = new ControlConfig();

            foreach (SensorMode mode in new[] { SensorMode.Sensor1, SensorMode.Sensor2, SensorMode.Sensor3 })
            {
                Assert.Equal(defaults.GetMode(mode).SetpointPa, fromFile.GetMode(mode).SetpointPa, 3);
                Assert.Equal(defaults.GetMode(mode).BandPa, fromFile.GetMode(mode).BandPa, 3);
                Assert.Equal(defaults.GetMode(mode).TimeSec, fromFile.GetMode(mode).TimeSec, 3);
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // 거부해야 하는 경우
        // ─────────────────────────────────────────────────────────────────────

        [Fact]
        public void 안전입력_구성_여부는_파일에서_읽지_않는다()
        {
            // device-map 에 PLC 가 있는지로 조립 루트가 판정한다.
            // 파일에 두면 읽히지 않는 값이 남아, 고쳐도 아무 일이 없다.
            string json = File.ReadAllText(ControlPath);

            JsonTextObject root;
            string error;

            Assert.True(JsonTextScanner.TryScan(json, out root, out error), error);
            Assert.Null(root.Value("safetyInputsConfigured"));
        }

        [Fact]
        public void 통로가_없으면_거부한다()
        {
            // 통로가 없으면 제어할 대상이 없다. 기본값으로 조용히 채우면
            // 파일을 고쳤는데 아무 일도 일어나지 않는 상태가 된다.
            ControlLoadResult result = ControlConfigLoader.LoadFromJson(
                "{ \"activeMode\": \"Sensor2\", \"chains\": [] }");

            Assert.False(result.IsSuccess);
            Assert.Null(result.Config);
        }

        [Fact]
        public void 전_통로를_끄면_경고하되_거부하지_않는다()
        {
            // 정비 중 전 통로를 꺼 두는 경우가 있다. 다만 조용히 넘어가면
            // "왜 아무것도 안 도는지" 를 찾게 된다.
            string json = File.ReadAllText(ControlPath);
            ControlConfig config = LoadShipped().Config;

            foreach (ChainDefinition chain in config.Chains)
            {
                chain.Enabled = false;
            }

            ControlLoadResult result = ControlConfigLoader.LoadFromJson(Apply(json, config));

            Assert.True(result.IsSuccess, string.Join("\n", result.Errors));
            Assert.NotEmpty(result.Warnings);
        }

        [Fact]
        public void 빈_원문은_거부한다()
        {
            Assert.False(ControlConfigLoader.LoadFromJson(string.Empty).IsSuccess);
        }

        [Fact]
        public void 구문_오류는_사유를_남긴다()
        {
            ControlLoadResult result = ControlConfigLoader.LoadFromJson("{ \"chains\": [ }");

            Assert.False(result.IsSuccess);
            Assert.NotEmpty(result.Errors);
        }

        // ─────────────────────────────────────────────────────────────────────
        // 주석 보존 저장
        // ─────────────────────────────────────────────────────────────────────

        [Fact]
        public void 아무것도_바꾸지_않으면_파일이_한_글자도_변하지_않는다()
        {
            string json = File.ReadAllText(ControlPath);

            Assert.Equal(json, Apply(json, LoadShipped().Config));
        }

        [Fact]
        public void 통로를_꺼도_주석이_전부_남는다()
        {
            string json = File.ReadAllText(ControlPath);
            ControlConfig config = LoadShipped().Config;

            FindChain(config, 3).Enabled = false;

            string result = Apply(json, config);

            Assert.Equal(CountComments(json), CountComments(result));
            Assert.Contains("센서 1 은 EC·SL·SR 3곳에만", result);
            Assert.Contains("JKBLD300V2", result);
        }

        [Fact]
        public void 꺼_둔_통로가_다시_읽힌다()
        {
            // D22 의 직접 재현. 종전에는 메모리에만 남아 재시작하면 켜졌다.
            string json = File.ReadAllText(ControlPath);
            ControlConfig config = LoadShipped().Config;

            FindChain(config, 3).Enabled = false;

            ControlConfig reloaded = ControlConfigLoader.LoadFromJson(Apply(json, config)).Config;

            Assert.False(FindChain(reloaded, 3).Enabled);
            Assert.True(FindChain(reloaded, 1).Enabled);
        }

        [Fact]
        public void 액추에이터_파라미터가_다시_읽힌다()
        {
            string json = File.ReadAllText(ControlPath);
            ControlConfig config = LoadShipped().Config;

            config.Valve.StepPulse = 150;
            config.Fan.StepRpm = 50.0;

            ControlConfig reloaded = ControlConfigLoader.LoadFromJson(Apply(json, config)).Config;

            Assert.Equal(150, reloaded.Valve.StepPulse);
            Assert.Equal(50.0, reloaded.Fan.StepRpm, 3);
        }

        [Fact]
        public void 파일에_없는_통로는_거부한다()
        {
            string json = File.ReadAllText(ControlPath);
            ControlConfig config = LoadShipped().Config;

            ChainDefinition added = new ChainDefinition();
            added.Id = 9;
            added.Name = "통로 9";
            added.ValveId = "V-9";
            added.FanId = "F-9";
            added.Sensor1Id = "S1-1";
            added.Sensor2Id = "S2-1";
            added.Sensor3Id = "S3-1";
            config.Chains.Add(added);

            string result;
            string error;

            Assert.False(ControlDocumentEditor.TryApply(json, config, out result, out error));
            Assert.Contains("9", error);
        }

        // ─────────────────────────────────────────────────────────────────────
        // 도우미
        // ─────────────────────────────────────────────────────────────────────

        private static ControlLoadResult LoadShipped()
        {
            Assert.True(File.Exists(ControlPath), "배포용 control.json 이 출력 폴더에 없습니다.");

            return ControlConfigLoader.LoadFromFile(ControlPath);
        }

        private static DeviceMap LoadMap()
        {
            ConfigLoadResult result = CommunicationConfigLoader.LoadFromFile(DeviceMapPath);

            Assert.True(result.IsSuccess, "통신 구성 오류:\n" + string.Join("\n", result.Errors));

            return result.Map;
        }

        private static string Apply(string json, ControlConfig config)
        {
            string result;
            string error;

            Assert.True(
                ControlDocumentEditor.TryApply(json, config, out result, out error),
                "부분 수정 실패: " + error);

            return result;
        }

        private static ChainDefinition FindChain(ControlConfig config, int id)
        {
            foreach (ChainDefinition chain in config.Chains)
            {
                if (chain.Id == id)
                {
                    return chain;
                }
            }

            throw new InvalidOperationException("통로가 없습니다: " + id);
        }

        private static int CountComments(string text)
        {
            int count = 0;
            int index = 0;

            while (true)
            {
                index = text.IndexOf("//", index, StringComparison.Ordinal);

                if (index < 0)
                {
                    return count;
                }

                count++;
                index += 2;
            }
        }
    }
}
