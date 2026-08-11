using System.Collections.Generic;
using System.IO;
using Esam.Communication.Configuration;
using Esam.Domain.Configuration;
using Xunit;

namespace Esam.Tests
{
    /// <summary>
    /// 배포되는 <c>config/recipe.json</c> 을 <c>config/device-map.json</c> 과 대조해 검증한다.
    /// </summary>
    /// <remarks>
    /// 설정 파일을 역할별로 나누면 참조가 끊어지는 것이 새 위험이다.
    /// 두 배포 파일을 함께 읽어 실제로 맞물리는지 확인한다.
    /// </remarks>
    public class RecipeConfigTests
    {
        private const string RecipePath = "config/recipe.json";
        private const string DeviceMapPath = "config/device-map.json";

        private static DeviceMap LoadShippedMap()
        {
            Assert.True(File.Exists(DeviceMapPath), "배포용 device-map.json 이 출력 폴더에 없습니다.");

            ConfigLoadResult result = CommunicationConfigLoader.LoadFromFile(DeviceMapPath);

            Assert.True(result.IsSuccess, "통신 구성 오류:\n" + string.Join("\n", result.Errors));
            return result.Map;
        }

        private static RecipeLoadResult LoadShipped()
        {
            Assert.True(File.Exists(RecipePath), "배포용 recipe.json 이 출력 폴더에 없습니다.");
            return RecipeConfigLoader.LoadFromFile(RecipePath, LoadShippedMap());
        }

        // ── 배포 파일 검증 ──────────────────────────────────────────────────────

        [Fact]
        public void 배포_설정의_압력_스케일이_시뮬레이션과_일치한다()
        {
            // ★ 스케일 팩터는 아직 미확정이다(COMM_MAP.md §7 TBD).
            // 미확정인 값을 두 곳에 따로 적어 두면 어긋난 것을 아무도 모른다.
            //
            // 어긋나면 측정값이 배수만큼 틀린다. 10배 틀린 압력으로 제어하면
            // 목표의 1/10 지점에 수렴하고, 화면·알람·상위 보고까지 전부 함께 틀린다.
            // 그런데 각 계층은 자기 기준으로는 일관되므로 어디도 오류를 내지 않는다.
            //
            // 실장비 매뉴얼을 확보하면 두 값을 함께 바꿔야 한다.
            // 한쪽만 바꾸면 이 테스트가 알려준다.
            DeviceMap map = LoadShippedMap();

            DeviceTypeDefinition type = map.FindType("DiffPressure");
            Assert.NotNull(type);

            PointDefinition pressure = null;

            foreach (ReadGroupDefinition group in type.ReadGroups)
            {
                foreach (PointDefinition point in group.Points)
                {
                    if (point.Key == "pressurePa")
                    {
                        pressure = point;
                    }
                }
            }

            Assert.NotNull(pressure);

            Assert.Equal(
                Esam.Communication.Simulation.SimulatedPressureSensor.PaPerLsb,
                pressure.Scale,
                6);
        }

        [Fact]
        public void 배포용_recipe_json이_통신_구성과_맞물린다()
        {
            RecipeLoadResult result = LoadShipped();

            Assert.True(
                result.IsSuccess,
                "레시피 오류:\n" + string.Join("\n", result.Errors));
        }

        [Fact]
        public void 차압센서_13대_전량에_설정값이_있다()
        {
            // 설정값이 없는 센서는 제어 기준으로 쓸 수 없다.
            // 하나가 빠지면 그 체인만 조용히 제어 밖에 놓인다.
            RecipeDefinition recipe = LoadShipped().Recipe;

            string[] sensors =
            {
                "S1-1", "S1-2", "S1-3",
                "S2-1", "S2-2", "S2-3", "S2-4", "S2-5",
                "S3-1", "S3-2", "S3-3", "S3-4", "S3-5"
            };

            foreach (string sensor in sensors)
            {
                Assert.NotNull(recipe.Find(sensor));
            }

            Assert.Equal(sensors.Length, recipe.Sensors.Count);
        }

        [Fact]
        public void ECID_항목_수가_39개로_맞는다()
        {
            // ESAM_IO List 의 ECID 시트 = 센서 13대 × (설정값 + 상한 + 하한).
            // 센서 수가 바뀌면 상위 연동 매핑도 함께 바뀌어야 하므로 여기서 고정한다.
            RecipeDefinition recipe = LoadShipped().Recipe;

            Assert.Equal(39, recipe.Sensors.Count * 3);
        }

        [Fact]
        public void 배포_레시피에_남는_경고가_없다()
        {
            // 하드웨어에 있는데 레시피에 없는 압력센서가 있으면 경고가 뜬다.
            // 배포본에서는 그런 누락이 없어야 한다.
            RecipeLoadResult result = LoadShipped();

            Assert.Empty(result.Warnings);
        }

        [Fact]
        public void 모드별_시간과_합쳐_제어_파라미터가_된다()
        {
            // recipe(센서별 값) + control(모드별 Time) → ModeSetting.
            // 이것이 두 파일을 잇는 유일한 지점이다.
            RecipeDefinition recipe = LoadShipped().Recipe;

            ModeSetting setting = recipe.GetModeSetting("S2-1", 120.0);

            Assert.NotNull(setting);
            Assert.Equal(-10.0, setting.SetpointPa);
            Assert.Equal(-40.0, setting.LowLimitPa);
            Assert.Equal(20.0, setting.HighLimitPa);
            Assert.Equal(120000.0, setting.TimeMs);
        }

        [Fact]
        public void 설정이_없는_센서는_null을_반환한다()
        {
            // 기본값으로 대체하면 엉뚱한 목표를 추종한다.
            // 호출측이 null 을 확인해 제어를 건너뛰어야 한다.
            RecipeDefinition recipe = LoadShipped().Recipe;

            Assert.Null(recipe.GetModeSetting("S9-9", 120.0));
        }

        // ── 로더 검증 규칙 ──────────────────────────────────────────────────────

        [Fact]
        public void 통신_구성에_없는_센서는_오류로_막는다()
        {
            // 검증 1. 존재하지 않는 센서의 설정값은 조용히 무시된다.
            const string Json = @"{
              ""sensors"": [
                { ""deviceId"": ""NOPE-1"", ""setpointPa"": -10, ""lowLimitPa"": -40, ""highLimitPa"": 20 }
              ]
            }";

            RecipeLoadResult result = RecipeConfigLoader.LoadFromJson(Json, LoadShippedMap());

            Assert.False(result.IsSuccess);
            Assert.Contains(result.Errors, e => e.Contains("통신 구성에 없습니다"));
        }

        [Fact]
        public void 센서_레인지를_벗어난_값은_오류로_막는다()
        {
            // 검증 2. 레인지(±2000 Pa)를 넘는 목표는 영원히 도달하지 못한다.
            const string Json = @"{
              ""sensors"": [
                { ""deviceId"": ""S2-1"", ""setpointPa"": 5000, ""lowLimitPa"": 4000, ""highLimitPa"": 6000 }
              ]
            }";

            RecipeLoadResult result = RecipeConfigLoader.LoadFromJson(Json, LoadShippedMap());

            Assert.False(result.IsSuccess);
            Assert.Contains(result.Errors, e => e.Contains("레인지"));
        }

        [Fact]
        public void 상하한이_뒤집히면_오류로_막는다()
        {
            // 검증 3. 알람이 영구 발생하거나 영구 침묵한다.
            const string Json = @"{
              ""sensors"": [
                { ""deviceId"": ""S2-1"", ""setpointPa"": -10, ""lowLimitPa"": 20, ""highLimitPa"": -40 }
              ]
            }";

            RecipeLoadResult result = RecipeConfigLoader.LoadFromJson(Json, null);

            Assert.False(result.IsSuccess);
            Assert.Contains(result.Errors, e => e.Contains("상한"));
        }

        [Fact]
        public void 설정값이_대역_밖이면_오류로_막는다()
        {
            // 제어가 시작부터 이탈 상태로 판정된다.
            const string Json = @"{
              ""sensors"": [
                { ""deviceId"": ""S2-1"", ""setpointPa"": 100, ""lowLimitPa"": -40, ""highLimitPa"": 20 }
              ]
            }";

            RecipeLoadResult result = RecipeConfigLoader.LoadFromJson(Json, null);

            Assert.False(result.IsSuccess);
            Assert.Contains(result.Errors, e => e.Contains("대역"));
        }

        [Fact]
        public void 센서_설정_중복은_오류로_막는다()
        {
            // 나중 것이 앞의 것을 덮어 하나가 조용히 사라진다.
            const string Json = @"{
              ""sensors"": [
                { ""deviceId"": ""S2-1"", ""setpointPa"": -10, ""lowLimitPa"": -40, ""highLimitPa"": 20 },
                { ""deviceId"": ""S2-1"", ""setpointPa"": -20, ""lowLimitPa"": -50, ""highLimitPa"": 10 }
              ]
            }";

            RecipeLoadResult result = RecipeConfigLoader.LoadFromJson(Json, null);

            Assert.False(result.IsSuccess);
            Assert.Contains(result.Errors, e => e.Contains("중복"));
        }

        [Fact]
        public void 압력센서가_아닌_장치는_오류로_막는다()
        {
            // 밸브나 팬에 압력 설정값을 주는 것은 구성 실수다.
            const string Json = @"{
              ""sensors"": [
                { ""deviceId"": ""V-1"", ""setpointPa"": -10, ""lowLimitPa"": -40, ""highLimitPa"": 20 }
              ]
            }";

            RecipeLoadResult result = RecipeConfigLoader.LoadFromJson(Json, LoadShippedMap());

            Assert.False(result.IsSuccess);
            Assert.Contains(result.Errors, e => e.Contains("압력센서가 아닙니다"));
        }

        [Fact]
        public void 센서가_하나도_없으면_오류로_막는다()
        {
            RecipeLoadResult result = RecipeConfigLoader.LoadFromJson(@"{ ""sensors"": [] }", null);

            Assert.False(result.IsSuccess);
        }

        [Fact]
        public void 알_수_없는_키는_오류로_막는다()
        {
            // 오타를 조용히 넘기면 설정한 값이 적용되지 않은 채 운전에 들어간다.
            const string Json = @"{
              ""sensors"": [
                { ""deviceId"": ""S2-1"", ""setpoint"": -10, ""lowLimitPa"": -40, ""highLimitPa"": 20 }
              ]
            }";

            RecipeLoadResult result = RecipeConfigLoader.LoadFromJson(Json, null);

            Assert.False(result.IsSuccess);
        }

        [Fact]
        public void 레시피에_빠진_센서는_경고로_알린다()
        {
            // 그 센서로 제어하지 않는 구성일 수 있으므로 막지 않는다.
            // 다만 제어 기준으로 쓸 수 없다는 사실은 드러나야 한다.
            const string Json = @"{
              ""sensors"": [
                { ""deviceId"": ""S2-1"", ""setpointPa"": -10, ""lowLimitPa"": -40, ""highLimitPa"": 20 }
              ]
            }";

            RecipeLoadResult result = RecipeConfigLoader.LoadFromJson(Json, LoadShippedMap());

            Assert.True(result.IsSuccess);
            Assert.Contains(result.Warnings, w => w.Contains("S3-1"));
        }

        [Fact]
        public void 통신_구성_없이_로드하면_검증_생략을_경고한다()
        {
            // 검증을 건너뛴 사실이 드러나야 한다. 조용히 통과하면 안 된다.
            const string Json = @"{
              ""sensors"": [
                { ""deviceId"": ""S2-1"", ""setpointPa"": -10, ""lowLimitPa"": -40, ""highLimitPa"": 20 }
              ]
            }";

            RecipeLoadResult result = RecipeConfigLoader.LoadFromJson(Json, null);

            Assert.True(result.IsSuccess);
            Assert.Contains(result.Warnings, w => w.Contains("검증을 건너뜁니다"));
        }

        // ── 왕복 변환 ───────────────────────────────────────────────────────────

        [Fact]
        public void 저장하고_다시_읽어도_값이_보존된다()
        {
            // 화면에서 수정한 레시피를 저장하는 경로다.
            RecipeDefinition original = LoadShipped().Recipe;

            string json = RecipeConfigLoader.ToJson(original);
            RecipeLoadResult reloaded = RecipeConfigLoader.LoadFromJson(json, LoadShippedMap());

            Assert.True(reloaded.IsSuccess, string.Join("\n", reloaded.Errors));
            Assert.Equal(original.Sensors.Count, reloaded.Recipe.Sensors.Count);

            foreach (SensorSetting before in original.Sensors)
            {
                SensorSetting after = reloaded.Recipe.Find(before.DeviceId);

                Assert.NotNull(after);
                Assert.Equal(before.SetpointPa, after.SetpointPa);
                Assert.Equal(before.LowLimitPa, after.LowLimitPa);
                Assert.Equal(before.HighLimitPa, after.HighLimitPa);
            }
        }

        // ── ModeSetting 비대칭 대역 ─────────────────────────────────────────────

        [Fact]
        public void 비대칭_대역이_그대로_유지된다()
        {
            // 종전 ModeSetting 은 Setpoint ± Band 로 대칭을 강제했다.
            // 배기는 상한 여유와 하한 여유가 다를 수 있어 비대칭이 필요하다.
            SensorSetting setting = new SensorSetting("S2-1", -10.0, -40.0, 20.0);
            ModeSetting mode = setting.ToModeSetting(120.0);

            Assert.True(mode.IsAsymmetric);
            Assert.Equal(-40.0, mode.LowLimitPa);
            Assert.Equal(20.0, mode.HighLimitPa);

            // 넓은 쪽 편차를 대역 폭으로 보고한다(표시용).
            Assert.Equal(30.0, mode.BandPa);
        }

        [Fact]
        public void 대칭_생성자는_기존과_동일하게_동작한다()
        {
            // 기존 구성과 테스트가 쓰는 경로다. 회귀가 없어야 한다.
            ModeSetting mode = new ModeSetting(-10.0, 30.0, 120.0);

            Assert.False(mode.IsAsymmetric);
            Assert.Equal(-40.0, mode.LowLimitPa);
            Assert.Equal(20.0, mode.HighLimitPa);
            Assert.Equal(30.0, mode.BandPa);
        }

        [Fact]
        public void 대역_폭을_직접_설정하면_대칭으로_되돌아간다()
        {
            ModeSetting mode = new ModeSetting(-10.0, -40.0, 20.0, 120.0);
            Assert.True(mode.IsAsymmetric);

            mode.BandPa = 5.0;

            Assert.False(mode.IsAsymmetric);
            Assert.Equal(-15.0, mode.LowLimitPa);
            Assert.Equal(-5.0, mode.HighLimitPa);
        }

        [Fact]
        public void 비대칭_대역에서도_대역_판정이_맞는다()
        {
            ModeSetting mode = new ModeSetting(-10.0, -40.0, 20.0, 120.0);

            Assert.True(mode.IsInBand(-10.0));
            Assert.True(mode.IsInBand(19.0));
            Assert.True(mode.IsInBand(-39.0));

            Assert.False(mode.IsInBand(20.0));
            Assert.False(mode.IsInBand(-40.0));
            Assert.False(mode.IsInBand(25.0));
        }
    }
}
