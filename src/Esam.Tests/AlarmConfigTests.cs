using System.Collections.Generic;
using System.IO;
using Esam.Communication.Configuration;
using Esam.Domain.Alarms;
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

        private static AlarmLoadResult LoadShipped()
        {
            Assert.True(File.Exists(AlarmPath), "배포용 alarms.json 이 출력 폴더에 없습니다.");
            return AlarmConfigLoader.LoadFromFile(AlarmPath);
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
        public void DESIGN_5_1이_요구하는_알람_종수를_충족한다()
        {
            // DESIGN.md 5.1 은 31종을 요구한다.
            IList<AlarmRule> rules = LoadShipped().Rules;

            Assert.True(
                rules.Count >= 31,
                "알람이 31종에 미치지 못합니다: " + rules.Count);
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
                if (rule.Code == "P09")
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

            HashSet<string> allowed = new HashSet<string>
            {
                "A05", "A12", "A13", "A18", "A19"
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

            Assert.True(false, "EMO 알람이 정의되어 있지 않습니다.");
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
