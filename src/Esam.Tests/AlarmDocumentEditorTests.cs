using System;
using System.Collections.Generic;
using System.IO;
using Esam.Communication.Configuration;
using Esam.Domain.Alarms;
using Xunit;

namespace Esam.Tests
{
    /// <summary>
    /// <c>alarms.json</c> 부분 수정 저장 검증.
    /// </summary>
    /// <remarks>
    /// <para>이 파일에는 <b>비활성 규칙의 사유</b>가 주석으로 적혀 있다.
    /// "동작하는 줄 알았는데 아니었다" 를 막으려고 남긴 설명이다.
    /// 저장 한 번에 그것이 사라지면 다음 사람은 알람이 왜 꺼져 있는지 알 수 없다.</para>
    /// <para>그래서 검증의 중심은 값이 바뀌었는가가 아니라
    /// <b>바꾸지 않은 것이 그대로인가</b>이다.</para>
    /// </remarks>
    public class AlarmDocumentEditorTests
    {
        private const string AlarmPath = "config/alarms.json";

        // ─────────────────────────────────────────────────────────────────────
        // 배포 파일 왕복
        // ─────────────────────────────────────────────────────────────────────

        [Fact]
        public void 아무것도_바꾸지_않으면_파일이_한_글자도_변하지_않는다()
        {
            string json = Shipped();
            IList<AlarmRule> rules = Parse(json);

            string result = Apply(json, rules);

            Assert.Equal(json, result);
        }

        [Fact]
        public void 임계값_하나를_바꿔도_주석이_전부_남는다()
        {
            string json = Shipped();
            IList<AlarmRule> rules = Parse(json);

            AlarmRule target = Find(rules, "DG-04");
            target.Threshold = -120.0;

            string result = Apply(json, rules);

            Assert.Equal(CountLineComments(json), CountLineComments(result));
            Assert.Contains("비활성 규칙은 소스가 없거나 사양이 미확정인 것뿐이다", result);
        }

        [Fact]
        public void 바꾼_줄_말고는_모두_같은_줄로_남는다()
        {
            // 재직렬화하면 정렬과 빈 줄이 전부 달라진다. 그러면 다음 리뷰에서
            // 무엇이 실제로 바뀌었는지 읽을 수 없다.
            string json = Shipped();
            IList<AlarmRule> rules = Parse(json);

            Find(rules, "AL-02").DebounceMs = 500.0;

            string[] before = json.Replace("\r\n", "\n").Split('\n');
            string[] after = Apply(json, rules).Replace("\r\n", "\n").Split('\n');

            Assert.Equal(before.Length, after.Length);

            int changed = 0;

            for (int i = 0; i < before.Length; i++)
            {
                if (!string.Equals(before[i], after[i], StringComparison.Ordinal))
                {
                    changed++;
                }
            }

            Assert.Equal(1, changed);
        }

        [Fact]
        public void 저장한_결과가_로더를_다시_통과한다()
        {
            // 저장은 로드와 같은 검증을 거쳐야 한다. 통과하지 못하는 파일을 쓰면
            // 다음 기동에서 장비가 뜨지 않는다.
            string json = Shipped();
            IList<AlarmRule> rules = Parse(json);

            Find(rules, "AL-32").Enabled = false;
            Find(rules, "AL-33").Severity = AlarmSeverity.Warning;

            string result = Apply(json, rules);
            AlarmLoadResult verified = AlarmConfigLoader.LoadFromJson(result);

            Assert.True(
                verified.IsSuccess,
                "저장 결과가 로드에 실패했습니다:\n" + string.Join("\n", verified.Errors));
        }

        // ─────────────────────────────────────────────────────────────────────
        // 값이 실제로 반영되는가
        // ─────────────────────────────────────────────────────────────────────

        [Fact]
        public void 편집한_값이_다시_읽힌다()
        {
            string json = Shipped();
            IList<AlarmRule> rules = Parse(json);

            Find(rules, "AL-17").DebounceMs = 1500.0;
            Find(rules, "AL-17").ResetPolicy = AlarmResetPolicy.Auto;
            Find(rules, "DG-04").Threshold = -95.5;

            IList<AlarmRule> reloaded = Parse(Apply(json, rules));

            Assert.Equal(1500.0, Find(reloaded, "AL-17").DebounceMs);
            Assert.Equal(AlarmResetPolicy.Auto, Find(reloaded, "AL-17").ResetPolicy);
            Assert.Equal(-95.5, Find(reloaded, "DG-04").Threshold, 3);
        }

        [Fact]
        public void 끄면_enabled_필드가_새로_생긴다()
        {
            // enabled 는 기본값이 true 라 원문에서 생략되어 있다.
            // 끄려면 없는 필드를 넣어야 한다.
            string json = Shipped();
            IList<AlarmRule> rules = Parse(json);

            Assert.True(Find(rules, "AL-02").Enabled);
            Find(rules, "AL-02").Enabled = false;

            IList<AlarmRule> reloaded = Parse(Apply(json, rules));

            Assert.False(Find(reloaded, "AL-02").Enabled);
        }

        [Fact]
        public void 다시_켜면_원래대로_읽힌다()
        {
            string json = Shipped();
            IList<AlarmRule> rules = Parse(json);

            Assert.False(Find(rules, "AL-03").Enabled);
            Find(rules, "AL-03").Enabled = true;

            IList<AlarmRule> reloaded = Parse(Apply(json, rules));

            Assert.True(Find(reloaded, "AL-03").Enabled);
        }

        [Fact]
        public void 임계값이_없던_규칙에는_임계값을_넣지_않는다()
        {
            // 압력 규칙의 임계값은 recipe.json 이 관리한다.
            // 여기에 숫자가 생기면 값이 두 곳에 살고, 어느 쪽이 적용되는지 알 수 없다.
            string json = Shipped();
            IList<AlarmRule> rules = Parse(json);

            AlarmRule pressure = FindByCondition(rules, AlarmConditionType.AboveHighLimit);
            pressure.Threshold = 999.0;

            IList<AlarmRule> reloaded = Parse(Apply(json, rules));

            Assert.Equal(0.0, Find(reloaded, pressure.Code).Threshold);
            Assert.DoesNotContain("999", Apply(json, rules));
        }

        // ─────────────────────────────────────────────────────────────────────
        // 거부해야 하는 경우
        // ─────────────────────────────────────────────────────────────────────

        [Fact]
        public void 원문에_없는_코드는_거부한다()
        {
            // 화면은 규칙을 추가하지 않는다. 없다는 것은 파일이 밖에서
            // 바뀌었다는 뜻이고, 덮어쓰면 그 변경을 지운다.
            string json = Shipped();
            List<AlarmRule> rules = new List<AlarmRule>(Parse(json));

            AlarmRule added = new AlarmRule();
            added.Code = "AL-99";
            added.Source = "device:V-1";
            rules.Add(added);

            string result;
            string error;

            Assert.False(AlarmDocumentEditor.TryApply(json, rules, out result, out error));
            Assert.Null(result);
            Assert.Contains("AL-99", error);
        }

        [Fact]
        public void 원문이_비면_거부한다()
        {
            string result;
            string error;

            Assert.False(
                AlarmDocumentEditor.TryApply(string.Empty, Parse(Shipped()), out result, out error));

            Assert.NotNull(error);
        }

        [Fact]
        public void 규칙_목록이_비면_거부한다()
        {
            string result;
            string error;

            Assert.False(
                AlarmDocumentEditor.TryApply(Shipped(), new List<AlarmRule>(), out result, out error));

            Assert.NotNull(error);
        }

        [Fact]
        public void 소수점은_문화권에_영향받지_않는다()
        {
            // 쉼표를 소수 구분자로 쓰는 지역 설정에서 6,5 로 기록되면
            // 다음 기동에서 JSON 구문 오류가 나고 장비가 뜨지 않는다.
            string json = Shipped();
            IList<AlarmRule> rules = Parse(json);

            Find(rules, "DG-04").Threshold = -95.5;

            System.Globalization.CultureInfo saved =
                System.Threading.Thread.CurrentThread.CurrentCulture;

            try
            {
                System.Threading.Thread.CurrentThread.CurrentCulture =
                    new System.Globalization.CultureInfo("de-DE");

                string result = Apply(json, rules);

                Assert.Contains("-95.5", result);
                Assert.DoesNotContain("-95,5", result);
            }
            finally
            {
                System.Threading.Thread.CurrentThread.CurrentCulture = saved;
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // 도우미
        // ─────────────────────────────────────────────────────────────────────

        private static string Shipped()
        {
            Assert.True(File.Exists(AlarmPath), "배포용 alarms.json 이 출력 폴더에 없습니다.");
            return File.ReadAllText(AlarmPath);
        }

        private static IList<AlarmRule> Parse(string json)
        {
            AlarmLoadResult result = AlarmConfigLoader.LoadFromJson(json);

            Assert.True(result.IsSuccess, "원문 로드 실패:\n" + string.Join("\n", result.Errors));

            return result.Rules;
        }

        private static string Apply(string json, IList<AlarmRule> rules)
        {
            string result;
            string error;

            Assert.True(
                AlarmDocumentEditor.TryApply(json, rules, out result, out error),
                "부분 수정 실패: " + error);

            return result;
        }

        private static AlarmRule Find(IList<AlarmRule> rules, string code)
        {
            foreach (AlarmRule rule in rules)
            {
                if (string.Equals(rule.Code, code, StringComparison.OrdinalIgnoreCase))
                {
                    return rule;
                }
            }

            throw new InvalidOperationException("규칙이 없습니다: " + code);
        }

        private static AlarmRule FindByCondition(IList<AlarmRule> rules, AlarmConditionType condition)
        {
            foreach (AlarmRule rule in rules)
            {
                if (rule.Condition == condition)
                {
                    return rule;
                }
            }

            throw new InvalidOperationException("조건에 맞는 규칙이 없습니다: " + condition);
        }

        private static int CountLineComments(string json)
        {
            int count = 0;
            int index = 0;

            while (true)
            {
                index = json.IndexOf("//", index, StringComparison.Ordinal);

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
