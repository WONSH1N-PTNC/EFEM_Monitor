using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Esam.Communication.Configuration;
using Esam.Domain.Alarms;
using Esam.Domain.Configuration;
using Esam.Domain.Control;
using Xunit;

namespace Esam.Tests
{
    /// <summary>
    /// 배포되는 <c>config/alarms.json</c> 자체를 검증한다.
    /// </summary>
    /// <remarks>
    /// 샘플 JSON 을 따로 만들어 검증하면 배포본이 깨져도 테스트는 통과한다.
    /// csproj 에서 실제 파일을 출력 폴더로 복사해 그것을 읽는다.
    /// </remarks>
    public class AlarmConfigTests
    {
        private const string AlarmPath = "config/alarms.json";

        private const string RecipePath = "config/recipe.json";
        private const string DeviceMapPath = "config/device-map.json";

        private static AlarmLoadResult LoadShipped()
        {
            Assert.True(File.Exists(AlarmPath), "배포용 alarms.json 이 출력 폴더에 없습니다.");
            return AlarmConfigLoader.LoadFromFile(AlarmPath, LoadShippedRecipe());
        }

        /// <summary>배포용 레시피를 읽는다. 임계값 참조 검증에 필요하다.</summary>
        /// <returns>레시피.</returns>
        /// <remarks>
        /// 레시피 없이 알람만 읽으면 <c>AboveHighLimit</c>·<c>BelowLowLimit</c> 의 참조가
        /// 끊어졌는지 확인하지 못한다. 배포 파일 검증의 핵심이 그 대조이므로 함께 읽는다.
        /// </remarks>
        private static RecipeDefinition LoadShippedRecipe()
        {
            Assert.True(File.Exists(DeviceMapPath), "배포용 device-map.json 이 없습니다.");
            Assert.True(File.Exists(RecipePath), "배포용 recipe.json 이 없습니다.");

            ConfigLoadResult map = CommunicationConfigLoader.LoadFromFile(DeviceMapPath);
            Assert.True(map.IsSuccess, "통신 구성 오류:\n" + string.Join("\n", map.Errors));

            RecipeLoadResult recipe = RecipeConfigLoader.LoadFromFile(RecipePath, map.Map);
            Assert.True(recipe.IsSuccess, "레시피 오류:\n" + string.Join("\n", recipe.Errors));

            return recipe.Recipe;
        }

        [Fact]
        public void 배포용_alarms_json이_오류_없이_로드된다()
        {
            AlarmLoadResult result = LoadShipped();

            Assert.True(
                result.IsSuccess,
                "알람 설정 오류:\n" + string.Join("\n", result.Errors));

            Assert.NotEmpty(result.Rules);
        }

        [Fact]
        public void Alarm_LIST_66종이_빠짐없이_정의된다()
        {
            // ESAM_IO List_260806 의 Alarm LIST 시트 No. 를 코드로 쓴다.
            // 하나가 빠지면 그 사건은 화면에도 상위에도 나타나지 않는다.
            IList<AlarmRule> rules = LoadShipped().Rules;

            HashSet<string> codes = new HashSet<string>();

            foreach (AlarmRule rule in rules)
            {
                codes.Add(rule.Code);
            }

            List<string> missing = new List<string>();

            for (int no = 1; no <= 66; no++)
            {
                string code = "AL-" + no.ToString("00", CultureInfo.InvariantCulture);

                if (!codes.Contains(code))
                {
                    missing.Add(code);
                }
            }

            Assert.Empty(missing);
        }

        [Fact]
        public void 사내_진단_알람은_DG_범위에_분리된다()
        {
            // 고객 사양 66종과 사내 진단을 코드 범위로 구분한다.
            // 상위(GEM)에는 AL-** 만 보고하므로 경계가 코드에서 보여야 한다.
            IList<AlarmRule> rules = LoadShipped().Rules;

            int al = 0;
            int dg = 0;

            foreach (AlarmRule rule in rules)
            {
                if (rule.Code.StartsWith("AL-", StringComparison.Ordinal))
                {
                    al++;
                }
                else if (rule.Code.StartsWith("DG-", StringComparison.Ordinal))
                {
                    dg++;
                }
                else
                {
                    Assert.Fail("AL- 도 DG- 도 아닌 알람 코드: " + rule.Code);
                }
            }

            Assert.Equal(66, al);
            Assert.True(dg > 0, "사내 진단 알람이 하나도 없습니다.");
        }

        [Fact]
        public void 압력_알람은_상한과_하한을_따로_갖는다()
        {
            // Alarm LIST 가 High Limit 과 Low Limit 을 나눴다.
            // 하나로 묶으면 어느 쪽으로 벗어났는지 알 수 없어 대응이 갈린다.
            IList<AlarmRule> rules = LoadShipped().Rules;

            int high = 0;
            int low = 0;

            foreach (AlarmRule rule in rules)
            {
                if (rule.Condition == AlarmConditionType.AboveHighLimit)
                {
                    high++;
                }
                else if (rule.Condition == AlarmConditionType.BelowLowLimit)
                {
                    low++;
                }
            }

            // 압력센서 13대 × 2
            Assert.Equal(13, high);
            Assert.Equal(13, low);
        }

        [Fact]
        public void 압력_알람은_임계값을_직접_갖지_않는다()
        {
            // ★ 규칙에 숫자를 두면 Config 화면에서 설정을 바꿨을 때 알람만 옛 값으로 남는다.
            // 화면과 알람이 서로 다른 진실을 말하는 상태는 현장에서 원인을 찾기 매우 어렵다.
            foreach (AlarmRule rule in LoadShipped().Rules)
            {
                if (rule.Condition != AlarmConditionType.AboveHighLimit
                    && rule.Condition != AlarmConditionType.BelowLowLimit)
                {
                    continue;
                }

                Assert.Equal(0.0, rule.Threshold);
            }
        }

        [Fact]
        public void 압력_알람이_차압센서_13대를_모두_덮는다()
        {
            // 센서 하나가 빠지면 그 체인만 조용히 감시 밖에 놓인다.
            // Sensor 1 은 EC·SL·SR 3곳에만 설치되므로 S1-1~1-3 만 대상이다.
            IList<AlarmRule> rules = LoadShipped().Rules;

            string[] sensors =
            {
                "S1-1", "S1-2", "S1-3",
                "S2-1", "S2-2", "S2-3", "S2-4", "S2-5",
                "S3-1", "S3-2", "S3-3", "S3-4", "S3-5"
            };

            foreach (string sensor in sensors)
            {
                string expected = "device:" + sensor + ".pressurePa";

                Assert.Contains(
                    rules,
                    r => r.Enabled && string.Equals(r.Source, expected, System.StringComparison.OrdinalIgnoreCase));
            }
        }

        [Fact]
        public void OutOfBand_규칙은_모두_참조_모드를_지정한다()
        {
            // referenceMode 가 없으면 판정 자체가 성립하지 않는데 조용히 false 만 반환한다.
            // 알람이 등록되어 있는데 영원히 울리지 않는 상태가 된다.
            foreach (AlarmRule rule in LoadShipped().Rules)
            {
                if (rule.Condition == AlarmConditionType.OutOfBand)
                {
                    Assert.True(
                        rule.ReferenceMode.HasValue,
                        "알람 " + rule.Code + " 에 referenceMode 가 없습니다.");
                }
            }
        }

        [Fact]
        public void 대역_이탈_알람은_인터록보다_먼저_울린다()
        {
            // IL-01 은 Manual 래치라 걸리면 장비가 멈춘다.
            // 배기 음압 저하를 먼저 알려야 작업자가 대응할 여지가 생긴다.
            IList<AlarmRule> rules = LoadShipped().Rules;

            AlarmRule warning = null;

            foreach (AlarmRule rule in rules)
            {
                if (rule.Code == "DG-04")
                {
                    warning = rule;
                    break;
                }
            }

            Assert.NotNull(warning);
            Assert.True(warning.Enabled);

            // 인터록 임계값(0 Pa)보다 낮은 지점에서 경고가 나와야 한다.
            InterlockRule interlock = new InterlockEvaluator(
                InterlockEvaluator.CreateDefaultRules()).FindRule("IL-01");

            Assert.NotNull(interlock);
            Assert.True(interlock.ThresholdPa.HasValue);
            Assert.True(
                warning.Threshold < interlock.ThresholdPa.Value,
                "경고 임계값이 인터록 임계값보다 낮아야 합니다.");
        }

        [Fact]
        public void 비활성_알람은_사양_미확보_장치에만_있다()
        {
            // 확보된 장치의 알람이 비활성이면 감시 공백이 생긴다.
            // 명세가 없는 장치(MFC·파티클·FFU·컨트롤박스 온도)만 허용한다.
            IList<AlarmRule> rules = LoadShipped().Rules;

            // AL-01  상위(FDC) 통신 — SECS/GEM 모듈 미구현(S7)
            // AL-66  제어 PC 온도 — 취득 경로 없음
            // AL-03·06·09·12·15  밸브 홈센서 — 알람코드 0x2203 비트 정의 미확정
            HashSet<string> allowed = new HashSet<string>
            {
                "AL-01", "AL-66",
                "AL-03", "AL-06", "AL-09", "AL-12", "AL-15"
            };

            foreach (AlarmRule rule in rules)
            {
                if (!rule.Enabled)
                {
                    Assert.True(
                        allowed.Contains(rule.Code),
                        "예상 밖 비활성 알람: " + rule.Code + " (" + rule.Name + ")");
                }
            }
        }

        [Fact]
        public void EMO_알람은_Manual_해제_정책이다()
        {
            // 비상정지는 원인 확인 없이 이력에서 사라지면 안 된다.
            foreach (AlarmRule rule in LoadShipped().Rules)
            {
                if (rule.Source == "plc:di.emo")
                {
                    Assert.Equal(AlarmSeverity.Critical, rule.Severity);
                    Assert.Equal(AlarmResetPolicy.Manual, rule.ResetPolicy);
                    return;
                }
            }

            Assert.Fail("EMO 알람이 정의되어 있지 않습니다.");
        }

        // ── 레시피 참조 (검증 4·5) ──────────────────────────────────────────────

        [Fact]
        public void 레시피에_없는_센서를_가리키면_오류로_막는다()
        {
            // ★ 참조가 끊어지면 임계값을 가져올 수 없어 알람이 영원히 발생하지 않는다.
            // 화면의 알람 목록에는 정상으로 보이므로 아무도 모른다.
            const string Json = @"{
              ""rules"": [
                { ""code"": ""AL-90"", ""source"": ""device:S9-9.pressurePa"",
                  ""condition"": ""AboveHighLimit"" }
              ]
            }";

            AlarmLoadResult result = AlarmConfigLoader.LoadFromJson(Json, LoadShippedRecipe());

            Assert.False(result.IsSuccess);
            Assert.Contains(result.Errors, e => e.Contains("S9-9"));
        }

        [Fact]
        public void 레시피_대상에_threshold를_직접_쓰면_경고한다()
        {
            // 값이 두 곳에 생기면 어느 쪽이 적용되는지 알 수 없다.
            // 예외적 필요가 있을 수 있어 막지는 않고 드러낸다.
            const string Json = @"{
              ""rules"": [
                { ""code"": ""AL-91"", ""source"": ""device:S2-1.pressurePa"",
                  ""condition"": ""GreaterThan"", ""threshold"": 100 }
              ]
            }";

            AlarmLoadResult result = AlarmConfigLoader.LoadFromJson(Json, LoadShippedRecipe());

            Assert.True(result.IsSuccess);
            Assert.Contains(result.Warnings, w => w.Contains("두 곳"));
        }

        [Fact]
        public void independentThreshold를_명시하면_중복_경고를_내지_않는다()
        {
            // 의도를 데이터에 적어 두면 경고가 사라진다.
            // 항상 뜨는 경고는 읽히지 않고, 그러면 진짜 중복도 함께 묻힌다.
            const string Json = @"{
              ""rules"": [
                { ""code"": ""AL-92"", ""source"": ""device:S2-1.pressurePa"",
                  ""condition"": ""GreaterThan"", ""threshold"": 100,
                  ""independentThreshold"": true }
              ]
            }";

            AlarmLoadResult result = AlarmConfigLoader.LoadFromJson(Json, LoadShippedRecipe());

            Assert.True(result.IsSuccess);
            Assert.DoesNotContain(result.Warnings, w => w.Contains("두 곳"));
        }

        [Fact]
        public void 레시피_없이_읽으면_참조_검증을_건너뛴_사실을_알린다()
        {
            // 조용히 넘어가면 검증했다고 착각한다.
            const string Json = @"{
              ""rules"": [
                { ""code"": ""AL-93"", ""source"": ""device:S2-1.pressurePa"",
                  ""condition"": ""AboveHighLimit"" }
              ]
            }";

            AlarmLoadResult result = AlarmConfigLoader.LoadFromJson(Json);

            Assert.True(result.IsSuccess);
            Assert.Contains(result.Warnings, w => w.Contains("건너뜁니다"));
        }

        [Fact]
        public void 해석할_수_없는_경로는_오류로_막는다()
        {
            // ★ 오타 하나로 안전 통보가 사라진다.
            // "값이 없다" 와 "경로를 모른다" 는 해석기 반환값으로 구분되지 않으므로
            // 로드 시점에 형식을 확인해야 한다.
            const string Json = @"{
              ""rules"": [
                { ""code"": ""AL-94"", ""source"": ""plc:di.emoo"",
                  ""condition"": ""BitSet"" }
              ]
            }";

            AlarmLoadResult result = AlarmConfigLoader.LoadFromJson(Json);

            Assert.False(result.IsSuccess);
            Assert.Contains(result.Errors, e => e.Contains("해석할 수 없습니다"));
        }

        [Fact]
        public void 비활성_규칙의_경로는_검사하지_않는다()
        {
            // 소스가 아직 없어서 비활성인 규칙(AL-01 SECS/GEM, AL-66 PC 온도)이 있다.
            // 그것까지 오류로 막으면 배포 설정이 로드되지 않는다.
            const string Json = @"{
              ""rules"": [
                { ""code"": ""AL-95"", ""source"": ""gem:link"", ""condition"": ""CommFail"",
                  ""enabled"": false },
                { ""code"": ""AL-96"", ""source"": ""plc:di.emo"", ""condition"": ""BitSet"" }
              ]
            }";

            AlarmLoadResult result = AlarmConfigLoader.LoadFromJson(Json);

            Assert.True(result.IsSuccess, string.Join("\n", result.Errors));
        }

        // ── 로더 자체 검증 ──────────────────────────────────────────────────────

        [Fact]
        public void 코드가_중복되면_오류로_막는다()
        {
            // 나중 것이 앞의 것을 덮어 하나가 조용히 사라진다.
            const string Json = @"{
              ""rules"": [
                { ""code"": ""X01"", ""source"": ""aux:particle"", ""condition"": ""GreaterThan"", ""threshold"": 1 },
                { ""code"": ""X01"", ""source"": ""aux:particle"", ""condition"": ""LessThan"",    ""threshold"": 0 }
              ]
            }";

            AlarmLoadResult result = AlarmConfigLoader.LoadFromJson(Json);

            Assert.False(result.IsSuccess);
            Assert.Contains(result.Errors, e => e.Contains("중복"));
        }

        [Fact]
        public void 활성_규칙이_하나도_없으면_오류로_막는다()
        {
            const string Json = @"{
              ""rules"": [
                { ""code"": ""X01"", ""source"": ""aux:particle"", ""condition"": ""GreaterThan"",
                  ""threshold"": 1, ""enabled"": false }
              ]
            }";

            AlarmLoadResult result = AlarmConfigLoader.LoadFromJson(Json);

            Assert.False(result.IsSuccess);
        }

        [Fact]
        public void 폴링_주기보다_짧은_디바운스는_경고로_알린다()
        {
            // 250ms 폴링에서 100ms 디바운스는 사실상 즉시와 같다.
            // 설정한 사람은 완충 효과를 기대하지만 실제로는 없다.
            const string Json = @"{
              ""rules"": [
                { ""code"": ""X01"", ""source"": ""aux:particle"", ""condition"": ""GreaterThan"",
                  ""threshold"": 1, ""debounceMs"": 100 }
              ]
            }";

            AlarmLoadResult result = AlarmConfigLoader.LoadFromJson(Json);

            Assert.True(result.IsSuccess);
            Assert.Contains(result.Warnings, w => w.Contains("디바운스"));
        }

        [Fact]
        public void 알_수_없는_키는_오류로_막는다()
        {
            // 오타를 조용히 넘기면 설정한 값이 적용되지 않은 채 운전에 들어간다.
            const string Json = @"{
              ""rules"": [
                { ""code"": ""X01"", ""source"": ""aux:particle"", ""condition"": ""GreaterThan"",
                  ""thresold"": 1 }
              ]
            }";

            AlarmLoadResult result = AlarmConfigLoader.LoadFromJson(Json);

            Assert.False(result.IsSuccess);
        }
    }
}
