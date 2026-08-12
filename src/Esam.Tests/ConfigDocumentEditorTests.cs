using System;
using System.IO;
using Esam.Communication.Configuration;
using Esam.Domain.Configuration;
using Xunit;

namespace Esam.Tests
{
    /// <summary>
    /// <c>device-map.json</c>·<c>recipe.json</c> 부분 수정 저장 검증(D21).
    /// </summary>
    /// <remarks>
    /// <para>두 파일은 저장할 때마다 <b>주석이 전부 사라지고 있었다.</b>
    /// <c>device-map.json</c> 55줄, <c>recipe.json</c> 은 52줄 중 29줄이다.</para>
    /// <para>사라지는 것 중에는 <b>커미셔닝 미확정 항목의 근거</b>가 있다.
    /// 압력 스케일 0.1 Pa/LSB 가 잠정값이라는 사실, 시뮬레이션 슬레이브와 짝이라
    /// 한쪽만 바꾸면 안 된다는 사실이 주석에만 적혀 있다.
    /// 현장에서 COM 포트를 한 번 바꾸면 그 판단 근거를 잃는다.</para>
    /// <para>그래서 검증의 중심은 <b>바꾸지 않은 것이 그대로인가</b>이다.</para>
    /// </remarks>
    public class ConfigDocumentEditorTests
    {
        private const string DeviceMapPath = "config/device-map.json";
        private const string RecipePath = "config/recipe.json";

        // ─────────────────────────────────────────────────────────────────────
        // device-map.json
        // ─────────────────────────────────────────────────────────────────────

        [Fact]
        public void 통신구성을_바꾸지_않으면_파일이_한_글자도_변하지_않는다()
        {
            string json = File.ReadAllText(DeviceMapPath);
            DeviceMap map = LoadMap(json);

            Assert.Equal(json, ApplyMap(json, map));
        }

        [Fact]
        public void 포트_이름을_바꿔도_주석이_전부_남는다()
        {
            string json = File.ReadAllText(DeviceMapPath);
            DeviceMap map = LoadMap(json);

            map.Ports[0].Serial.PortName = "COM7";

            string result = ApplyMap(json, map);

            Assert.Equal(CountComments(json), CountComments(result));

            // 커미셔닝 미확정 항목의 근거가 여기 있다.
            Assert.Contains("0.1 Pa/LSB", result);
            Assert.Contains("시뮬레이션 슬레이브도 같은 값을 쓴다", result);
            Assert.Contains("218.4 ms", result);
        }

        [Fact]
        public void 포트_설정이_다시_읽힌다()
        {
            string json = File.ReadAllText(DeviceMapPath);
            DeviceMap map = LoadMap(json);

            map.Ports[0].Serial.PortName = "COM7";
            map.Ports[0].Serial.BaudRate = 38400;

            DeviceMap reloaded = LoadMap(ApplyMap(json, map));

            Assert.Equal("COM7", reloaded.Ports[0].Serial.PortName);
            Assert.Equal(38400, reloaded.Ports[0].Serial.BaudRate);
        }

        [Fact]
        public void 영점_오프셋이_없던_디바이스에_새로_생긴다()
        {
            // 영점은 0 이 기본값이라 원문에서 생략되어 있다.
            // Maintenance 의 영점 교정이 이 경로를 쓴다.
            string json = File.ReadAllText(DeviceMapPath);
            DeviceMap map = LoadMap(json);

            DeviceInstanceDefinition sensor = FindDevice(map, "S1-1");

            Assert.Equal(0.0, sensor.Offset);
            sensor.Offset = 0.35;

            DeviceMap reloaded = LoadMap(ApplyMap(json, map));

            Assert.Equal(0.35, FindDevice(reloaded, "S1-1").Offset, 3);
        }

        [Fact]
        public void 바꾼_줄_말고는_모두_같은_줄로_남는다()
        {
            string json = File.ReadAllText(DeviceMapPath);
            DeviceMap map = LoadMap(json);

            map.Ports[1].Serial.PortName = "COM9";

            string[] before = json.Replace("\r\n", "\n").Split('\n');
            string[] after = ApplyMap(json, map).Replace("\r\n", "\n").Split('\n');

            Assert.Equal(before.Length, after.Length);
            Assert.Equal(1, CountDifferences(before, after));
        }

        [Fact]
        public void 파일에_없는_디바이스는_거부한다()
        {
            // 화면은 디바이스를 추가하지 않는다. 없다는 것은 파일이 밖에서
            // 바뀌었다는 뜻이고, 덮어쓰면 그 변경을 지운다.
            string json = File.ReadAllText(DeviceMapPath);
            DeviceMap map = LoadMap(json);

            map.Devices.Add(new DeviceInstanceDefinition
            {
                Id = "S9-9", Type = "DiffPressure", Port = "CH1", SlaveId = 99
            });

            string result;
            string error;

            Assert.False(DeviceMapDocumentEditor.TryApply(json, map, out result, out error));
            Assert.Null(result);
            Assert.Contains("S9-9", error);
        }

        [Fact]
        public void 저장한_결과가_로더를_다시_통과한다()
        {
            string json = File.ReadAllText(DeviceMapPath);
            DeviceMap map = LoadMap(json);

            map.Ports[0].Serial.ResponseTimeoutMs = 500;
            FindDevice(map, "S2-1").Offset = -1.25;

            ConfigLoadResult verified =
                CommunicationConfigLoader.LoadFromJson(ApplyMap(json, map));

            Assert.True(
                verified.IsSuccess,
                "저장 결과가 로드에 실패했습니다:\n" + string.Join("\n", verified.Errors));
        }

        // ─────────────────────────────────────────────────────────────────────
        // recipe.json
        // ─────────────────────────────────────────────────────────────────────

        [Fact]
        public void 레시피를_바꾸지_않으면_파일이_한_글자도_변하지_않는다()
        {
            string json = File.ReadAllText(RecipePath);
            RecipeDefinition recipe = LoadRecipe(json);

            Assert.Equal(json, ApplyRecipe(json, recipe));
        }

        [Fact]
        public void 설정값을_바꿔도_주석이_전부_남는다()
        {
            // 이 파일은 절반 이상이 주석이다. 재직렬화하면 그 절반이 사라진다.
            string json = File.ReadAllText(RecipePath);
            RecipeDefinition recipe = LoadRecipe(json);

            recipe.Sensors[0].SetpointPa = 6.5;

            string result = ApplyRecipe(json, recipe);

            Assert.Equal(CountComments(json), CountComments(result));
            Assert.Contains("독립적으로 둔다", result);
            Assert.Contains("안전 임계값이 운전 파라미터에 종속되면", result);
        }

        [Fact]
        public void 설정값이_다시_읽힌다()
        {
            string json = File.ReadAllText(RecipePath);
            RecipeDefinition recipe = LoadRecipe(json);

            recipe.Sensors[0].SetpointPa = 6.5;
            recipe.Sensors[0].LowLimitPa = 4.5;
            recipe.Sensors[0].HighLimitPa = 8.5;

            RecipeDefinition reloaded = LoadRecipe(ApplyRecipe(json, recipe));

            Assert.Equal(6.5, reloaded.Sensors[0].SetpointPa, 3);
            Assert.Equal(4.5, reloaded.Sensors[0].LowLimitPa, 3);
            Assert.Equal(8.5, reloaded.Sensors[0].HighLimitPa, 3);
        }

        [Fact]
        public void 파일에_없는_센서는_거부한다()
        {
            string json = File.ReadAllText(RecipePath);
            RecipeDefinition recipe = LoadRecipe(json);

            recipe.Sensors.Add(new SensorSetting("S9-9", 1.0, 0.0, 2.0));

            string result;
            string error;

            Assert.False(RecipeDocumentEditor.TryApply(json, recipe, out result, out error));
            Assert.Contains("S9-9", error);
        }

        [Fact]
        public void 소수점은_문화권에_영향받지_않는다()
        {
            string json = File.ReadAllText(RecipePath);
            RecipeDefinition recipe = LoadRecipe(json);

            recipe.Sensors[0].SetpointPa = 6.5;

            System.Globalization.CultureInfo saved =
                System.Threading.Thread.CurrentThread.CurrentCulture;

            try
            {
                System.Threading.Thread.CurrentThread.CurrentCulture =
                    new System.Globalization.CultureInfo("de-DE");

                string result = ApplyRecipe(json, recipe);

                Assert.Contains("6.5", result);
                Assert.DoesNotContain("6,5", result);
            }
            finally
            {
                System.Threading.Thread.CurrentThread.CurrentCulture = saved;
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // 스캐너 자체
        // ─────────────────────────────────────────────────────────────────────

        [Fact]
        public void 중첩_객체와_배열을_모두_찾는다()
        {
            string json = File.ReadAllText(DeviceMapPath);

            JsonTextObject root;
            string error;

            Assert.True(JsonTextScanner.TryScan(json, out root, out error), error);

            Assert.Equal(2, root.Array("ports").Count);
            Assert.Equal(29, root.Array("devices").Count);
            Assert.NotNull(root.Object("deviceTypes"));
            Assert.NotNull(root.Array("ports")[0].Object("serial"));
            Assert.Equal("CH1", root.Array("ports")[0].Object("serial").Text("portId"));
        }

        [Fact]
        public void 최상위가_객체가_아니면_거부한다()
        {
            JsonTextObject root;
            string error;

            Assert.False(JsonTextScanner.TryScan("[1, 2, 3]", out root, out error));
            Assert.NotNull(error);
        }

        [Fact]
        public void 주석만_있는_원문도_구조를_읽는다()
        {
            string json = "// 머리말\n{\n  // 필드 설명\n  \"a\": 1 // 꼬리\n}\n";

            JsonTextObject root;
            string error;

            Assert.True(JsonTextScanner.TryScan(json, out root, out error), error);
            Assert.NotNull(root.Value("a"));
            Assert.Equal("1", root.Value("a").Text);
        }

        // ─────────────────────────────────────────────────────────────────────
        // 도우미
        // ─────────────────────────────────────────────────────────────────────

        private static DeviceMap LoadMap(string json)
        {
            ConfigLoadResult result = CommunicationConfigLoader.LoadFromJson(json);

            Assert.True(result.IsSuccess, "통신 구성 로드 실패:\n" + string.Join("\n", result.Errors));

            return result.Map;
        }

        private static RecipeDefinition LoadRecipe(string json)
        {
            RecipeLoadResult result = RecipeConfigLoader.LoadFromJson(
                json, LoadMap(File.ReadAllText(DeviceMapPath)));

            Assert.True(result.IsSuccess, "레시피 로드 실패:\n" + string.Join("\n", result.Errors));

            return result.Recipe;
        }

        private static string ApplyMap(string json, DeviceMap map)
        {
            string result;
            string error;

            Assert.True(
                DeviceMapDocumentEditor.TryApply(json, map, out result, out error),
                "부분 수정 실패: " + error);

            return result;
        }

        private static string ApplyRecipe(string json, RecipeDefinition recipe)
        {
            string result;
            string error;

            Assert.True(
                RecipeDocumentEditor.TryApply(json, recipe, out result, out error),
                "부분 수정 실패: " + error);

            return result;
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

            throw new InvalidOperationException("디바이스가 없습니다: " + id);
        }

        private static int CountDifferences(string[] before, string[] after)
        {
            int changed = 0;

            for (int i = 0; i < before.Length && i < after.Length; i++)
            {
                if (!string.Equals(before[i], after[i], StringComparison.Ordinal))
                {
                    changed++;
                }
            }

            return changed;
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
