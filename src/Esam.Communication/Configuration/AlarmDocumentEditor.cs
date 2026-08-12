using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Esam.Domain.Alarms;

namespace Esam.Communication.Configuration
{
    /// <summary>
    /// <c>alarms.json</c> 원문에서 <b>값 토큰만 바꿔 쓴다.</b>
    /// </summary>
    /// <remarks>
    /// <para><b>왜 역직렬화해서 다시 쓰지 않는가.</b> 이 파일에는 비활성 규칙의 사유,
    /// 임계값의 출처, 코드 체계를 적은 한글 주석이 200여 줄 있다. 규칙 하나를 고치려고
    /// 파일을 통째로 다시 쓰면 그 설명이 전부 사라진다. 정렬과 구획선도 함께 사라진다.
    /// <b>다음 사람이 "이 알람은 왜 꺼져 있지" 를 알 수 없게 된다.</b></para>
    /// <para>Json.NET 의 <c>CommentHandling.Load</c> 는 주석 <i>내용</i>은 살리지만
    /// 다시 쓸 때 줄 주석(<c>//</c>)을 블록 주석으로 바꾸고 파일 전체를 재정렬한다.
    /// 그래서 쓰지 않는다.</para>
    /// <para>대신 원문을 훑어 <b>바꿀 값의 위치만</b> 찾고 그 구간을 치환한다.
    /// 손대지 않은 바이트는 하나도 변하지 않는다.</para>
    /// <para>이 편집기는 <b>규칙을 추가하거나 지우지 않는다.</b> 화면이 그러지 않기 때문이다.
    /// 원문에 없는 코드가 들어오면 파일이 밖에서 바뀐 것이므로 거부한다.</para>
    /// </remarks>
    public static class AlarmDocumentEditor
    {
        /// <summary>편집 가능한 필드 이름.</summary>
        private const string KeyCode = "code";

        private const string KeyThreshold = "threshold";
        private const string KeyDebounce = "debounceMs";
        private const string KeyEnabled = "enabled";
        private const string KeySeverity = "severity";
        private const string KeyResetPolicy = "resetPolicy";

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

            IList<RuleSpan> spans;

            if (!TryScan(json, out spans, out error))
            {
                return false;
            }

            Dictionary<string, RuleSpan> byCode =
                new Dictionary<string, RuleSpan>(StringComparer.OrdinalIgnoreCase);

            foreach (RuleSpan span in spans)
            {
                byCode[span.Code] = span;
            }

            List<Edit> edits = new List<Edit>();

            foreach (AlarmRule rule in edited)
            {
                if (rule == null || string.IsNullOrEmpty(rule.Code))
                {
                    error = "코드가 없는 규칙이 있습니다.";
                    return false;
                }

                RuleSpan span;

                if (!byCode.TryGetValue(rule.Code, out span))
                {
                    // 화면은 규칙을 추가하지 않는다. 원문에 없다는 것은
                    // 파일이 밖에서 바뀌었다는 뜻이다. 덮어쓰면 그 변경을 지운다.
                    error = string.Format(
                        CultureInfo.InvariantCulture,
                        "알람 {0} 이 파일에 없습니다. 파일이 외부에서 변경되었습니다. "
                        + "다시 읽은 뒤 편집하십시오.",
                        rule.Code);

                    return false;
                }

                Collect(span, rule, edits);
            }

            result = ApplyEdits(json, edits);
            return true;
        }

        /// <summary>규칙 1건의 변경 사항을 편집 목록에 담는다.</summary>
        /// <param name="span">원문에서의 위치.</param>
        /// <param name="rule">편집된 규칙.</param>
        /// <param name="edits">편집 목록(출력).</param>
        /// <remarks>
        /// <para><c>threshold</c> 는 <b>원문에 있을 때만</b> 건드린다. 없는 규칙에 넣으면
        /// 임계값을 <c>recipe.json</c> 이 관리하는 압력 규칙에 숫자가 생겨
        /// 값이 두 곳에 사는 상태가 된다. 화면도 그 칸을 잠그지만 여기서도 막는다.</para>
        /// <para><c>enabled</c> 는 반대다. 기본값이 <c>true</c> 라 원문에서 생략되어 있고,
        /// 끄려면 <b>넣어야</b> 한다.</para>
        /// </remarks>
        private static void Collect(RuleSpan span, AlarmRule rule, IList<Edit> edits)
        {
            if (span.Threshold != null)
            {
                Replace(edits, span.Threshold, Number(rule.Threshold));
            }

            Put(edits, span, span.Debounce, KeyDebounce, Number(rule.DebounceMs), rule.DebounceMs != 0.0);
            Put(edits, span, span.Severity, KeySeverity, Quote(rule.Severity.ToString()), true);
            Put(edits, span, span.ResetPolicy, KeyResetPolicy, Quote(rule.ResetPolicy.ToString()), true);

            // enabled 가 true 면 생략이 기본값이다. 굳이 써 넣지 않는다.
            Put(edits, span, span.Enabled, KeyEnabled, rule.Enabled ? "true" : "false", !rule.Enabled);
        }

        /// <summary>값을 교체하거나, 없으면 필요할 때만 새로 넣는다.</summary>
        /// <param name="edits">편집 목록.</param>
        /// <param name="span">규칙 위치.</param>
        /// <param name="existing">기존 값 위치. 없으면 null.</param>
        /// <param name="key">필드 이름.</param>
        /// <param name="text">쓸 값.</param>
        /// <param name="insertWhenMissing">원문에 없을 때 새로 넣을지 여부.</param>
        private static void Put(
            IList<Edit> edits,
            RuleSpan span,
            Span existing,
            string key,
            string text,
            bool insertWhenMissing)
        {
            if (existing != null)
            {
                Replace(edits, existing, text);
                return;
            }

            if (!insertWhenMissing)
            {
                return;
            }

            string inserted = string.Concat(
                ",", Environment.NewLine, span.Indent, Quote(key), ": ", text);

            edits.Add(new Edit(span.LastValueEnd, span.LastValueEnd, inserted));
        }

        /// <summary>값 구간을 교체 목록에 넣는다. 같은 값이면 넣지 않는다.</summary>
        /// <param name="edits">편집 목록.</param>
        /// <param name="span">값 위치.</param>
        /// <param name="text">쓸 값.</param>
        private static void Replace(IList<Edit> edits, Span span, string text)
        {
            // 바뀌지 않은 값은 건드리지 않는다. 파일 diff 를 작게 유지해야
            // 무엇이 실제로 바뀌었는지 나중에 읽을 수 있다.
            if (string.Equals(span.Text, text, StringComparison.Ordinal))
            {
                return;
            }

            // 수는 표기가 달라도 같은 값일 수 있다. 원문의 -100.0 을 -100 으로
            // 고쳐 쓰면 <b>고치지 않은 규칙까지 파일이 바뀐다.</b> 아무것도 편집하지
            // 않고 저장한 사람이 74줄짜리 변경을 만들게 된다.
            if (SameNumber(span.Text, text))
            {
                return;
            }

            edits.Add(new Edit(span.Start, span.End, text));
        }

        /// <summary>두 표기가 같은 수를 가리키는지 판정한다.</summary>
        /// <param name="left">왼쪽 표기.</param>
        /// <param name="right">오른쪽 표기.</param>
        /// <returns>둘 다 수이고 값이 같으면 true.</returns>
        private static bool SameNumber(string left, string right)
        {
            double a;
            double b;

            if (!double.TryParse(left, NumberStyles.Float, CultureInfo.InvariantCulture, out a))
            {
                return false;
            }

            if (!double.TryParse(right, NumberStyles.Float, CultureInfo.InvariantCulture, out b))
            {
                return false;
            }

            return a.Equals(b);
        }

        /// <summary>편집을 뒤에서부터 적용한다.</summary>
        /// <param name="json">원문.</param>
        /// <param name="edits">편집 목록.</param>
        /// <returns>수정된 원문.</returns>
        /// <remarks>
        /// 앞에서부터 적용하면 첫 치환이 뒤쪽 위치를 전부 밀어 버린다.
        /// 뒤에서부터 적용하면 아직 쓰지 않은 위치가 그대로 남는다.
        /// </remarks>
        private static string ApplyEdits(string json, List<Edit> edits)
        {
            if (edits.Count == 0)
            {
                return json;
            }

            edits.Sort(CompareByStartDescending);

            StringBuilder builder = new StringBuilder(json);

            foreach (Edit edit in edits)
            {
                builder.Remove(edit.Start, edit.End - edit.Start);
                builder.Insert(edit.Start, edit.Text);
            }

            return builder.ToString();
        }

        /// <summary>시작 위치 내림차순 비교자.</summary>
        /// <param name="left">왼쪽.</param>
        /// <param name="right">오른쪽.</param>
        /// <returns>비교 결과.</returns>
        private static int CompareByStartDescending(Edit left, Edit right)
        {
            return right.Start.CompareTo(left.Start);
        }

        /// <summary>불변 문화권으로 수를 서식한다.</summary>
        /// <param name="value">값.</param>
        /// <returns>문자열.</returns>
        /// <remarks>
        /// 지역 설정이 쉼표를 소수 구분자로 쓰면 <c>6.5</c> 가 <c>6,5</c> 로 기록되어
        /// 다음 기동에서 JSON 구문 오류가 난다. 장비가 뜨지 않는다.
        /// </remarks>
        private static string Number(double value)
        {
            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }

        /// <summary>문자열을 JSON 문자열 리터럴로 만든다.</summary>
        /// <param name="text">문자열.</param>
        /// <returns>따옴표를 두른 문자열.</returns>
        private static string Quote(string text)
        {
            return string.Concat("\"", text, "\"");
        }

        // ─────────────────────────────────────────────────────────────────────
        // 스캐너 — 주석과 문자열을 건너뛰며 값의 위치만 찾는다
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>원문에서 규칙별 값 위치를 찾는다.</summary>
        /// <param name="json">원문.</param>
        /// <param name="spans">규칙 위치 목록(출력).</param>
        /// <param name="error">실패 사유(출력).</param>
        /// <returns>성공하면 true.</returns>
        private static bool TryScan(string json, out IList<RuleSpan> spans, out string error)
        {
            spans = new List<RuleSpan>();
            error = null;

            int index = IndexOfRulesArray(json);

            if (index < 0)
            {
                error = "알람 설정에서 rules 배열을 찾지 못했습니다.";
                return false;
            }

            // rules 배열 안의 객체를 차례로 훑는다.
            while (index < json.Length)
            {
                index = SkipTrivia(json, index);

                if (index >= json.Length)
                {
                    break;
                }

                char c = json[index];

                if (c == ']')
                {
                    break;
                }

                if (c == ',')
                {
                    index++;
                    continue;
                }

                if (c != '{')
                {
                    error = string.Format(
                        CultureInfo.InvariantCulture,
                        "rules 배열에 객체가 아닌 항목이 있습니다(위치 {0}).", index);

                    return false;
                }

                RuleSpan span;

                if (!TryScanRule(json, ref index, out span, out error))
                {
                    return false;
                }

                if (span != null)
                {
                    spans.Add(span);
                }
            }

            if (spans.Count == 0)
            {
                error = "알람 규칙을 하나도 찾지 못했습니다.";
                return false;
            }

            return true;
        }

        /// <summary>규칙 객체 하나를 훑는다.</summary>
        /// <param name="json">원문.</param>
        /// <param name="index">객체 시작 위치. 끝난 뒤 다음 위치로 옮긴다.</param>
        /// <param name="span">규칙 위치(출력).</param>
        /// <param name="error">실패 사유(출력).</param>
        /// <returns>성공하면 true.</returns>
        private static bool TryScanRule(
            string json, ref int index, out RuleSpan span, out string error)
        {
            span = null;
            error = null;

            RuleSpan found = new RuleSpan();

            index++; // '{'

            while (true)
            {
                index = SkipTrivia(json, index);

                if (index >= json.Length)
                {
                    error = "규칙 객체가 닫히지 않았습니다.";
                    return false;
                }

                char c = json[index];

                if (c == '}')
                {
                    index++;
                    break;
                }

                if (c == ',')
                {
                    index++;
                    continue;
                }

                if (c != '"')
                {
                    error = string.Format(
                        CultureInfo.InvariantCulture,
                        "규칙 객체에서 필드 이름을 찾지 못했습니다(위치 {0}).", index);

                    return false;
                }

                Span name = ReadString(json, ref index);

                index = SkipTrivia(json, index);

                if (index >= json.Length || json[index] != ':')
                {
                    error = string.Format(
                        CultureInfo.InvariantCulture,
                        "필드 {0} 뒤에 ':' 가 없습니다.", name.Text);

                    return false;
                }

                index++; // ':'
                index = SkipTrivia(json, index);

                Span value = ReadValue(json, ref index);

                if (value == null)
                {
                    error = string.Format(
                        CultureInfo.InvariantCulture,
                        "필드 {0} 의 값을 읽지 못했습니다.", name.Text);

                    return false;
                }

                found.Apply(json, name, value);
            }

            if (found.Code == null)
            {
                error = "code 가 없는 규칙이 있습니다.";
                return false;
            }

            span = found;
            return true;
        }

        /// <summary><c>rules</c> 배열의 첫 항목 위치를 찾는다.</summary>
        /// <param name="json">원문.</param>
        /// <returns>위치. 찾지 못하면 -1.</returns>
        private static int IndexOfRulesArray(string json)
        {
            int index = 0;

            while (index < json.Length)
            {
                index = SkipTrivia(json, index);

                if (index >= json.Length)
                {
                    return -1;
                }

                if (json[index] != '"')
                {
                    index++;
                    continue;
                }

                Span name = ReadString(json, ref index);

                // 문자열 안으로 되돌아가지 않는다. 되돌아가면 메시지 본문의
                // 따옴표를 필드 구분자로 잘못 읽는다.
                if (!string.Equals(name.Text, "\"rules\"", StringComparison.Ordinal))
                {
                    continue;
                }

                index = SkipTrivia(json, index);

                if (index < json.Length && json[index] == ':')
                {
                    index = SkipTrivia(json, index + 1);

                    if (index < json.Length && json[index] == '[')
                    {
                        return index + 1;
                    }
                }
            }

            return -1;
        }

        /// <summary>공백과 주석을 건너뛴다.</summary>
        /// <param name="json">원문.</param>
        /// <param name="index">시작 위치.</param>
        /// <returns>다음 유효 문자 위치.</returns>
        private static int SkipTrivia(string json, int index)
        {
            while (index < json.Length)
            {
                char c = json[index];

                if (char.IsWhiteSpace(c))
                {
                    index++;
                    continue;
                }

                if (c == '/' && index + 1 < json.Length && json[index + 1] == '/')
                {
                    while (index < json.Length && json[index] != '\n')
                    {
                        index++;
                    }

                    continue;
                }

                if (c == '/' && index + 1 < json.Length && json[index + 1] == '*')
                {
                    index += 2;

                    while (index + 1 < json.Length
                           && !(json[index] == '*' && json[index + 1] == '/'))
                    {
                        index++;
                    }

                    index += 2;
                    continue;
                }

                break;
            }

            return index;
        }

        /// <summary>문자열 토큰을 읽는다.</summary>
        /// <param name="json">원문.</param>
        /// <param name="index">시작 위치(여는 따옴표). 끝난 뒤 닫는 따옴표 다음으로 옮긴다.</param>
        /// <returns>토큰 위치.</returns>
        private static Span ReadString(string json, ref int index)
        {
            int start = index;

            index++; // 여는 따옴표

            while (index < json.Length)
            {
                char c = json[index];

                if (c == '\\')
                {
                    index += 2;
                    continue;
                }

                index++;

                if (c == '"')
                {
                    break;
                }
            }

            return new Span(start, index, json.Substring(start, index - start));
        }

        /// <summary>값 토큰을 읽는다. 객체·배열은 통째로 건너뛴다.</summary>
        /// <param name="json">원문.</param>
        /// <param name="index">시작 위치. 끝난 뒤 값 다음으로 옮긴다.</param>
        /// <returns>토큰 위치. 읽지 못하면 null.</returns>
        private static Span ReadValue(string json, ref int index)
        {
            if (index >= json.Length)
            {
                return null;
            }

            char c = json[index];

            if (c == '"')
            {
                return ReadString(json, ref index);
            }

            if (c == '{' || c == '[')
            {
                int start = index;
                int depth = 0;

                while (index < json.Length)
                {
                    index = SkipTrivia(json, index);

                    if (index >= json.Length)
                    {
                        break;
                    }

                    char inner = json[index];

                    if (inner == '"')
                    {
                        ReadString(json, ref index);
                        continue;
                    }

                    if (inner == '{' || inner == '[')
                    {
                        depth++;
                    }
                    else if (inner == '}' || inner == ']')
                    {
                        depth--;

                        if (depth == 0)
                        {
                            index++;
                            break;
                        }
                    }

                    index++;
                }

                return new Span(start, index, json.Substring(start, index - start));
            }

            int scalarStart = index;

            while (index < json.Length)
            {
                char scalar = json[index];

                if (scalar == ',' || scalar == '}' || scalar == ']'
                    || char.IsWhiteSpace(scalar) || scalar == '/')
                {
                    break;
                }

                index++;
            }

            return index == scalarStart
                ? null
                : new Span(scalarStart, index, json.Substring(scalarStart, index - scalarStart));
        }

        /// <summary>원문 안의 한 구간.</summary>
        private sealed class Span
        {
            /// <summary>구간을 만든다.</summary>
            /// <param name="start">시작 위치.</param>
            /// <param name="end">끝 위치(제외).</param>
            /// <param name="text">구간 문자열.</param>
            public Span(int start, int end, string text)
            {
                Start = start;
                End = end;
                Text = text;
            }

            /// <summary>시작 위치.</summary>
            public int Start { get; private set; }

            /// <summary>끝 위치(제외).</summary>
            public int End { get; private set; }

            /// <summary>구간 문자열.</summary>
            public string Text { get; private set; }
        }

        /// <summary>치환 1건.</summary>
        private sealed class Edit
        {
            /// <summary>치환을 만든다.</summary>
            /// <param name="start">시작 위치.</param>
            /// <param name="end">끝 위치(제외).</param>
            /// <param name="text">쓸 문자열.</param>
            public Edit(int start, int end, string text)
            {
                Start = start;
                End = end;
                Text = text;
            }

            /// <summary>시작 위치.</summary>
            public int Start { get; private set; }

            /// <summary>끝 위치(제외).</summary>
            public int End { get; private set; }

            /// <summary>쓸 문자열.</summary>
            public string Text { get; private set; }
        }

        /// <summary>규칙 1건의 원문 위치.</summary>
        private sealed class RuleSpan
        {
            /// <summary>알람 코드.</summary>
            public string Code { get; private set; }

            /// <summary><c>threshold</c> 값 위치. 없으면 null.</summary>
            public Span Threshold { get; private set; }

            /// <summary><c>debounceMs</c> 값 위치. 없으면 null.</summary>
            public Span Debounce { get; private set; }

            /// <summary><c>enabled</c> 값 위치. 없으면 null.</summary>
            public Span Enabled { get; private set; }

            /// <summary><c>severity</c> 값 위치. 없으면 null.</summary>
            public Span Severity { get; private set; }

            /// <summary><c>resetPolicy</c> 값 위치. 없으면 null.</summary>
            public Span ResetPolicy { get; private set; }

            /// <summary>마지막 필드 값의 끝 위치. 새 필드를 여기 뒤에 넣는다.</summary>
            public int LastValueEnd { get; private set; }

            /// <summary>필드 줄의 들여쓰기.</summary>
            public string Indent { get; private set; }

            /// <summary>읽은 필드를 반영한다.</summary>
            /// <param name="json">원문.</param>
            /// <param name="name">필드 이름 토큰.</param>
            /// <param name="value">값 토큰.</param>
            public void Apply(string json, Span name, Span value)
            {
                LastValueEnd = value.End;

                if (Indent == null)
                {
                    Indent = IndentOf(json, name.Start);
                }

                string key = Unquote(name.Text);

                if (string.Equals(key, KeyCode, StringComparison.Ordinal))
                {
                    Code = Unquote(value.Text);
                }
                else if (string.Equals(key, KeyThreshold, StringComparison.Ordinal))
                {
                    Threshold = value;
                }
                else if (string.Equals(key, KeyDebounce, StringComparison.Ordinal))
                {
                    Debounce = value;
                }
                else if (string.Equals(key, KeyEnabled, StringComparison.Ordinal))
                {
                    Enabled = value;
                }
                else if (string.Equals(key, KeySeverity, StringComparison.Ordinal))
                {
                    Severity = value;
                }
                else if (string.Equals(key, KeyResetPolicy, StringComparison.Ordinal))
                {
                    ResetPolicy = value;
                }
            }

            /// <summary>따옴표를 벗긴다.</summary>
            /// <param name="text">원문 토큰.</param>
            /// <returns>내용.</returns>
            private static string Unquote(string text)
            {
                return text != null && text.Length >= 2 && text[0] == '"'
                    ? text.Substring(1, text.Length - 2)
                    : text;
            }

            /// <summary>해당 위치가 속한 줄의 들여쓰기를 구한다.</summary>
            /// <param name="json">원문.</param>
            /// <param name="position">위치.</param>
            /// <returns>들여쓰기 문자열.</returns>
            private static string IndentOf(string json, int position)
            {
                int lineStart = position;

                while (lineStart > 0 && json[lineStart - 1] != '\n')
                {
                    lineStart--;
                }

                int cursor = lineStart;

                while (cursor < position && (json[cursor] == ' ' || json[cursor] == '\t'))
                {
                    cursor++;
                }

                return json.Substring(lineStart, cursor - lineStart);
            }
        }
    }
}
