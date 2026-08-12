using System;
using System.Collections.Generic;
using System.Globalization;
using Esam.Domain.Alarms;

namespace Esam.Communication.Configuration
{
    /// <summary>
    /// <c>alarms.json</c> 원문에서 <b>값 토큰만</b> 바꿔 쓴다.
    /// </summary>
    /// <remarks>
    /// <para>이 파일에는 비활성 규칙의 사유, 임계값의 출처, 코드 체계를 적은
    /// 한글 주석이 55줄 있다. 규칙 하나를 고치려고 파일을 통째로 다시 쓰면
    /// 그 설명이 전부 사라진다. <b>다음 사람이 "이 알람은 왜 꺼져 있지" 를
    /// 알 수 없게 된다.</b></para>
    /// <para>스캐너는 <see cref="JsonTextScanner"/> 를 공유한다.
    /// <c>device-map.json</c>·<c>recipe.json</c> 도 같은 경로를 쓴다.
    /// 파일마다 스캐너를 따로 두면 한쪽만 고쳐진 채로 남는다.</para>
    /// <para>이 편집기는 <b>규칙을 추가하거나 지우지 않는다.</b> 화면이 그러지 않기 때문이다.
    /// 원문에 없는 코드가 들어오면 파일이 밖에서 바뀐 것이므로 거부한다.</para>
    /// </remarks>
    public static class AlarmDocumentEditor
    {
        /// <summary>편집 결과를 원문에 반영한다.</summary>
        /// <param name="json">원문.</param>
        /// <param name="edited">편집된 규칙 목록.</param>
        /// <param name="result">수정된 원문. 실패 시 null.</param>
        /// <param name="error">실패 사유. 성공 시 null.</param>
        /// <returns>성공하면 true.</returns>
        public static bool TryApply(
            string json, IList<AlarmRule> edited, out string result, out string error)
        {
            result = null;
            error = null;

            if (string.IsNullOrEmpty(json))
            {
                error = "알람 설정 원문이 비어 있습니다.";
                return false;
            }

            if (edited == null || edited.Count == 0)
            {
                error = "저장할 규칙이 없습니다.";
                return false;
            }

            JsonTextObject root;

            if (!JsonTextScanner.TryScan(json, out root, out error))
            {
                return false;
            }

            IList<JsonTextObject> rules = root.Array("rules");

            if (rules.Count == 0)
            {
                error = "알람 규칙을 하나도 찾지 못했습니다.";
                return false;
            }

            Dictionary<string, JsonTextObject> byCode =
                new Dictionary<string, JsonTextObject>(StringComparer.OrdinalIgnoreCase);

            foreach (JsonTextObject rule in rules)
            {
                string code = rule.Text("code");

                if (!string.IsNullOrEmpty(code))
                {
                    byCode[code] = rule;
                }
            }

            JsonTextPatch patch = new JsonTextPatch();

            foreach (AlarmRule rule in edited)
            {
                if (rule == null || string.IsNullOrEmpty(rule.Code))
                {
                    error = "코드가 없는 규칙이 있습니다.";
                    return false;
                }

                JsonTextObject span;

                if (!byCode.TryGetValue(rule.Code, out span))
                {
                    // 화면은 규칙을 추가하지 않는다. 원문에 없다는 것은
                    // 파일이 밖에서 바뀌었다는 뜻이고, 덮어쓰면 그 변경을 지운다.
                    error = string.Format(
                        CultureInfo.InvariantCulture,
                        "알람 {0} 이 파일에 없습니다. 파일이 외부에서 변경되었습니다. "
                        + "다시 읽은 뒤 편집하십시오.",
                        rule.Code);

                    return false;
                }

                Collect(span, rule, patch);
            }

            result = patch.Apply(json);
            return true;
        }

        /// <summary>규칙 1건의 변경 사항을 치환 목록에 담는다.</summary>
        /// <param name="span">원문에서의 위치.</param>
        /// <param name="rule">편집된 규칙.</param>
        /// <param name="patch">치환 목록.</param>
        /// <remarks>
        /// <para><c>threshold</c> 는 <b>원문에 있을 때만</b> 건드린다. 없는 규칙에 넣으면
        /// 임계값을 <c>recipe.json</c> 이 관리하는 압력 규칙에 숫자가 생겨
        /// 값이 두 곳에 사는 상태가 된다. 화면도 그 칸을 잠그지만 여기서도 막는다.</para>
        /// <para><c>enabled</c> 는 반대다. 기본값이 <c>true</c> 라 원문에서 생략되어 있고,
        /// 끄려면 <b>넣어야</b> 한다.</para>
        /// </remarks>
        private static void Collect(JsonTextObject span, AlarmRule rule, JsonTextPatch patch)
        {
            if (span.Value("threshold") != null)
            {
                patch.SetNumber(span, "threshold", rule.Threshold, false);
            }

            patch.SetNumber(span, "debounceMs", rule.DebounceMs, rule.DebounceMs != 0.0);
            patch.SetString(span, "severity", rule.Severity.ToString(), true);
            patch.SetString(span, "resetPolicy", rule.ResetPolicy.ToString(), true);

            // enabled 가 true 면 생략이 기본값이다. 굳이 써 넣지 않는다.
            patch.SetBoolean(span, "enabled", rule.Enabled, !rule.Enabled);
        }
    }
}
