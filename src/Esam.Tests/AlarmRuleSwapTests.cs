using System;
using System.Collections.Generic;
using Esam.Domain.Alarms;
using Xunit;

namespace Esam.Tests
{
    /// <summary>
    /// 규칙 목록 교체 시 발생 상태 승계 검증.
    /// </summary>
    /// <remarks>
    /// <para>화면에서 알람 하나의 확정 시간을 고치면 규칙 74건이 전부 새 객체가 된다.
    /// 그때 상태까지 새로 만들면 <b>떠 있던 알람이 저장 한 번에 사라진다.</b>
    /// Manual 정책 알람은 사람이 Reset 하기 전까지 남아 있어야 하는데,
    /// 저장 버튼이 Reset 을 대신하게 된다.</para>
    /// <para>현장에서는 "알람이 저절로 꺼졌다" 로 보인다. 원인을 찾을 단서가 없다.</para>
    /// </remarks>
    public class AlarmRuleSwapTests
    {
        private static readonly DateTime T0 = new DateTime(2026, 8, 12, 0, 0, 0, DateTimeKind.Utc);

        [Fact]
        public void 발생_중이던_알람은_교체_후에도_살아_있다()
        {
            AlarmEvaluator evaluator = new AlarmEvaluator(new[] { Rule("A-1", AlarmResetPolicy.Manual) });

            Activate(evaluator, "A-1");

            AlarmRuleSwapResult result =
                evaluator.ReplaceRules(new[] { Rule("A-1", AlarmResetPolicy.Manual, 7.5) });

            Assert.True(
                evaluator.FindState("A-1").IsActive,
                "떠 있던 알람이 규칙 교체로 사라졌습니다.");

            Assert.Equal(1, result.Carried);
            Assert.Empty(result.DroppedActive);
        }

        [Fact]
        public void 교체_후_새_임계값이_적용된다()
        {
            AlarmEvaluator evaluator = new AlarmEvaluator(new[] { Rule("A-1", AlarmResetPolicy.Manual) });

            Activate(evaluator, "A-1");
            evaluator.ReplaceRules(new[] { Rule("A-1", AlarmResetPolicy.Manual, 7.5) });

            // 상태는 살아 있되 규칙은 새 것을 가리켜야 한다.
            // 옛 규칙을 계속 보면 화면과 판정이 서로 다른 값을 쓴다.
            Assert.Equal(7.5, evaluator.FindState("A-1").Rule.Threshold, 3);
        }

        [Fact]
        public void 확인_없이_사라진_활성_알람은_결과에_남는다()
        {
            // 조용히 없어지면 "알람이 저절로 꺼졌다" 가 된다.
            AlarmEvaluator evaluator = new AlarmEvaluator(
                new[] { Rule("A-1", AlarmResetPolicy.Manual), Rule("A-2", AlarmResetPolicy.Manual) });

            Activate(evaluator, "A-1");

            AlarmRuleSwapResult result =
                evaluator.ReplaceRules(new[] { Rule("A-2", AlarmResetPolicy.Manual) });

            Assert.Contains("A-1", result.DroppedActive);
            Assert.Contains("A-1", result.Removed);
            Assert.True(result.HasStructuralChange);
            Assert.Null(evaluator.FindState("A-1"));
        }

        [Fact]
        public void 발생하지_않은_규칙이_사라져도_경고하지_않는다()
        {
            AlarmEvaluator evaluator = new AlarmEvaluator(
                new[] { Rule("A-1", AlarmResetPolicy.Auto), Rule("A-2", AlarmResetPolicy.Auto) });

            AlarmRuleSwapResult result =
                evaluator.ReplaceRules(new[] { Rule("A-2", AlarmResetPolicy.Auto) });

            Assert.Empty(result.DroppedActive);
            Assert.Contains("A-1", result.Removed);
        }

        [Fact]
        public void 새_규칙은_추가로_보고된다()
        {
            AlarmEvaluator evaluator = new AlarmEvaluator(new[] { Rule("A-1", AlarmResetPolicy.Auto) });

            AlarmRuleSwapResult result = evaluator.ReplaceRules(
                new[] { Rule("A-1", AlarmResetPolicy.Auto), Rule("A-9", AlarmResetPolicy.Auto) });

            Assert.Contains("A-9", result.Added);
            Assert.Equal(1, result.Carried);
            Assert.Equal(2, evaluator.RuleCount);
        }

        [Fact]
        public void 아무것도_바뀌지_않으면_구성_변경도_없다()
        {
            AlarmEvaluator evaluator = new AlarmEvaluator(new[] { Rule("A-1", AlarmResetPolicy.Auto) });

            AlarmRuleSwapResult result = evaluator.ReplaceRules(new[] { Rule("A-1", AlarmResetPolicy.Auto) });

            Assert.False(result.HasStructuralChange);
            Assert.Empty(result.Added);
            Assert.Empty(result.Removed);
        }

        [Fact]
        public void 코드가_다른_규칙에는_상태를_물려주지_않는다()
        {
            // 다른 알람의 발생 이력을 물려받는 것은 되살릴 수 없는 혼동이다.
            AlarmState state = new AlarmState(Rule("A-1", AlarmResetPolicy.Auto));

            Assert.Throws<ArgumentException>(
                delegate { state.Rebind(Rule("A-2", AlarmResetPolicy.Auto)); });
        }

        [Fact]
        public void null_규칙_목록은_거부한다()
        {
            AlarmEvaluator evaluator = new AlarmEvaluator(new[] { Rule("A-1", AlarmResetPolicy.Auto) });

            Assert.Throws<ArgumentNullException>(
                delegate { evaluator.ReplaceRules(null); });
        }

        [Fact]
        public void 코드가_겹치면_뒤의_것을_버린다()
        {
            // 겹친 코드를 그대로 담으면 상태 사전과 규칙 목록의 길이가 어긋난다.
            AlarmEvaluator evaluator = new AlarmEvaluator(new[] { Rule("A-1", AlarmResetPolicy.Auto) });

            evaluator.ReplaceRules(
                new[] { Rule("A-1", AlarmResetPolicy.Auto), Rule("A-1", AlarmResetPolicy.Manual) });

            Assert.Equal(1, evaluator.RuleCount);
        }

        // ─────────────────────────────────────────────────────────────────────
        // 도우미
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>디바운스 없이 즉시 확정되는 규칙을 만든다.</summary>
        /// <param name="code">알람 코드.</param>
        /// <param name="policy">해제 정책.</param>
        /// <param name="threshold">임계값.</param>
        /// <returns>규칙.</returns>
        private static AlarmRule Rule(string code, AlarmResetPolicy policy, double threshold = 5.0)
        {
            AlarmRule rule = new AlarmRule();
            rule.Code = code;
            rule.Name = code;
            rule.Source = "device:S1-1.pressurePa";
            rule.Condition = AlarmConditionType.GreaterThan;
            rule.Threshold = threshold;
            rule.DebounceMs = 0.0;
            rule.ResetPolicy = policy;
            rule.Enabled = true;
            rule.IndependentThreshold = true;
            return rule;
        }

        /// <summary>지정 알람을 발생 상태로 만든다.</summary>
        /// <param name="evaluator">평가기.</param>
        /// <param name="code">알람 코드.</param>
        private static void Activate(AlarmEvaluator evaluator, string code)
        {
            AlarmState state = evaluator.FindState(code);

            Assert.NotNull(state);
            Assert.True(state.Update(true, 99.0, "테스트 발생", T0));
            Assert.True(state.IsActive);
        }
    }
}
