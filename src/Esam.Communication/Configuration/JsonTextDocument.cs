using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Esam.Communication.Configuration
{
    /// <summary>
    /// 원문 안의 한 구간.
    /// </summary>
    public sealed class JsonTextSpan
    {
        /// <summary>구간을 만든다.</summary>
        /// <param name="start">시작 위치.</param>
        /// <param name="end">끝 위치(제외).</param>
        /// <param name="text">구간 문자열.</param>
        public JsonTextSpan(int start, int end, string text)
        {
            Start = start;
            End = end;
            Text = text;
        }

        /// <summary>시작 위치.</summary>
        public int Start { get; private set; }

        /// <summary>끝 위치(제외).</summary>
        public int End { get; private set; }

        /// <summary>구간 문자열(따옴표를 포함한 원문 그대로).</summary>
        public string Text { get; private set; }

        /// <summary>문자열 리터럴이면 따옴표를 벗긴 내용.</summary>
        public string Unquoted
        {
            get
            {
                return Text != null && Text.Length >= 2 && Text[0] == '"'
                    ? Text.Substring(1, Text.Length - 2)
                    : Text;
            }
        }
    }

    /// <summary>
    /// 원문에서 객체 하나가 차지하는 위치와 그 안의 필드 위치.
    /// </summary>
    /// <remarks>
    /// 값 자체가 아니라 <b>값이 원문 어디에 있는지</b>를 담는다.
    /// 이것이 있어야 주석과 정렬을 건드리지 않고 값만 바꿀 수 있다.
    /// </remarks>
    public sealed class JsonTextObject
    {
        private readonly Dictionary<string, JsonTextSpan> _values =
            new Dictionary<string, JsonTextSpan>(StringComparer.Ordinal);

        private readonly Dictionary<string, JsonTextObject> _objects =
            new Dictionary<string, JsonTextObject>(StringComparer.Ordinal);

        private readonly Dictionary<string, IList<JsonTextObject>> _arrays =
            new Dictionary<string, IList<JsonTextObject>>(StringComparer.Ordinal);

        /// <summary>객체 시작 위치(<c>{</c>).</summary>
        public int Start { get; internal set; }

        /// <summary>객체 끝 위치(<c>}</c> 다음).</summary>
        public int End { get; internal set; }

        /// <summary>마지막 필드 값의 끝 위치. 새 필드를 여기 뒤에 넣는다.</summary>
        public int LastValueEnd { get; internal set; }

        /// <summary>필드 줄의 들여쓰기.</summary>
        public string Indent { get; internal set; }

        /// <summary>스칼라·문자열 필드의 값 위치를 찾는다.</summary>
        /// <param name="key">필드 이름.</param>
        /// <returns>값 위치. 없으면 null.</returns>
        public JsonTextSpan Value(string key)
        {
            JsonTextSpan span;
            return _values.TryGetValue(key ?? string.Empty, out span) ? span : null;
        }

        /// <summary>중첩 객체를 찾는다.</summary>
        /// <param name="key">필드 이름.</param>
        /// <returns>객체. 없으면 null.</returns>
        public JsonTextObject Object(string key)
        {
            JsonTextObject value;
            return _objects.TryGetValue(key ?? string.Empty, out value) ? value : null;
        }

        /// <summary>객체 배열을 찾는다.</summary>
        /// <param name="key">필드 이름.</param>
        /// <returns>객체 목록. 없으면 빈 목록.</returns>
        public IList<JsonTextObject> Array(string key)
        {
            IList<JsonTextObject> value;

            return _arrays.TryGetValue(key ?? string.Empty, out value)
                ? value
                : new List<JsonTextObject>();
        }

        /// <summary>문자열 필드의 내용을 읽는다.</summary>
        /// <param name="key">필드 이름.</param>
        /// <returns>내용. 없으면 null.</returns>
        public string Text(string key)
        {
            JsonTextSpan span = Value(key);
            return span == null ? null : span.Unquoted;
        }

        /// <summary>스칼라 값을 등록한다.</summary>
        /// <param name="key">필드 이름.</param>
        /// <param name="span">값 위치.</param>
        internal void AddValue(string key, JsonTextSpan span)
        {
            _values[key] = span;
        }

        /// <summary>중첩 객체를 등록한다.</summary>
        /// <param name="key">필드 이름.</param>
        /// <param name="child">객체.</param>
        internal void AddObject(string key, JsonTextObject child)
        {
            _objects[key] = child;
        }

        /// <summary>객체 배열을 등록한다.</summary>
        /// <param name="key">필드 이름.</param>
        /// <param name="items">객체 목록.</param>
        internal void AddArray(string key, IList<JsonTextObject> items)
        {
            _arrays[key] = items;
        }
    }

    /// <summary>
    /// 주석을 보존한 채 JSON 원문의 <b>값 토큰만</b> 바꾸기 위한 스캐너.
    /// </summary>
    /// <remarks>
    /// <para><b>왜 역직렬화해서 다시 쓰지 않는가.</b> ESAM 의 설정 파일에는 값의 근거를
    /// 적은 한글 주석이 많다 — <c>alarms.json</c> 55줄, <c>device-map.json</c> 55줄,
    /// <c>recipe.json</c> 은 52줄 중 29줄이다. 압력 스케일이 잠정값이라는 사실,
    /// 시뮬레이션 슬레이브와 짝이라는 사실이 전부 거기 적혀 있다.</para>
    /// <para>파일을 통째로 다시 쓰면 그 설명이 사라진다. 현장에서 COM 포트를 한 번
    /// 바꾸는 것만으로 <b>다음 사람이 판단 근거를 잃는다.</b></para>
    /// <para>Json.NET 의 <c>CommentHandling.Load</c> 는 주석 내용은 살리지만 다시 쓸 때
    /// 줄 주석을 블록 주석으로 바꾸고 파일 전체를 재정렬한다. 그래서 쓰지 않는다.</para>
    /// <para>이 스캐너는 값을 해석하지 않는다. <b>위치만</b> 찾는다. 해석과 검증은
    /// 기존 로더가 그대로 맡는다. 규칙을 두 곳에 두지 않기 위해서다.</para>
    /// </remarks>
    public static class JsonTextScanner
    {
        /// <summary>원문 전체를 훑어 최상위 객체의 구조를 만든다.</summary>
        /// <param name="json">원문.</param>
        /// <param name="root">최상위 객체(출력).</param>
        /// <param name="error">실패 사유(출력).</param>
        /// <returns>성공하면 true.</returns>
        public static bool TryScan(string json, out JsonTextObject root, out string error)
        {
            root = null;
            error = null;

            if (string.IsNullOrEmpty(json))
            {
                error = "원문이 비어 있습니다.";
                return false;
            }

            int index = SkipTrivia(json, 0);

            if (index >= json.Length || json[index] != '{')
            {
                error = "최상위가 객체가 아닙니다.";
                return false;
            }

            return TryScanObject(json, ref index, out root, out error);
        }

        /// <summary>객체 하나를 훑는다.</summary>
        /// <param name="json">원문.</param>
        /// <param name="index">객체 시작 위치. 끝난 뒤 다음 위치로 옮긴다.</param>
        /// <param name="result">객체(출력).</param>
        /// <param name="error">실패 사유(출력).</param>
        /// <returns>성공하면 true.</returns>
        private static bool TryScanObject(
            string json, ref int index, out JsonTextObject result, out string error)
        {
            result = null;
            error = null;

            JsonTextObject found = new JsonTextObject();
            found.Start = index;

            index++; // '{'

            while (true)
            {
                index = SkipTrivia(json, index);

                if (index >= json.Length)
                {
                    error = "객체가 닫히지 않았습니다.";
                    return false;
                }

                char c = json[index];

                if (c == '}')
                {
                    index++;
                    found.End = index;
                    break;
                }

                if (c == ',')
                {
                    index++;
                    continue;
                }

                if (c != '"')
                {
                    error = Format("필드 이름을 찾지 못했습니다(위치 {0}).", index);
                    return false;
                }

                JsonTextSpan name = ReadString(json, ref index);

                if (found.Indent == null)
                {
                    found.Indent = IndentOf(json, name.Start);
                }

                index = SkipTrivia(json, index);

                if (index >= json.Length || json[index] != ':')
                {
                    error = Format("필드 {0} 뒤에 ':' 가 없습니다.", name.Unquoted);
                    return false;
                }

                index++; // ':'
                index = SkipTrivia(json, index);

                if (index >= json.Length)
                {
                    error = Format("필드 {0} 의 값이 없습니다.", name.Unquoted);
                    return false;
                }

                string key = name.Unquoted;

                if (json[index] == '{')
                {
                    JsonTextObject child;

                    if (!TryScanObject(json, ref index, out child, out error))
                    {
                        return false;
                    }

                    found.AddObject(key, child);
                    found.LastValueEnd = child.End;
                    continue;
                }

                if (json[index] == '[')
                {
                    IList<JsonTextObject> items;
                    int arrayEnd;

                    if (!TryScanArray(json, ref index, out items, out arrayEnd, out error))
                    {
                        return false;
                    }

                    found.AddArray(key, items);
                    found.LastValueEnd = arrayEnd;
                    continue;
                }

                JsonTextSpan value = ReadScalar(json, ref index);

                if (value == null)
                {
                    error = Format("필드 {0} 의 값을 읽지 못했습니다.", key);
                    return false;
                }

                found.AddValue(key, value);
                found.LastValueEnd = value.End;
            }

            result = found;
            return true;
        }

        /// <summary>배열을 훑는다. 객체가 아닌 항목은 건너뛴다.</summary>
        /// <param name="json">원문.</param>
        /// <param name="index">배열 시작 위치. 끝난 뒤 다음 위치로 옮긴다.</param>
        /// <param name="items">객체 목록(출력).</param>
        /// <param name="end">배열 끝 위치(출력).</param>
        /// <param name="error">실패 사유(출력).</param>
        /// <returns>성공하면 true.</returns>
        private static bool TryScanArray(
            string json,
            ref int index,
            out IList<JsonTextObject> items,
            out int end,
            out string error)
        {
            items = new List<JsonTextObject>();
            error = null;

            index++; // '['

            while (true)
            {
                index = SkipTrivia(json, index);

                if (index >= json.Length)
                {
                    end = index;
                    error = "배열이 닫히지 않았습니다.";
                    return false;
                }

                char c = json[index];

                if (c == ']')
                {
                    index++;
                    end = index;
                    return true;
                }

                if (c == ',')
                {
                    index++;
                    continue;
                }

                if (c == '{')
                {
                    JsonTextObject child;

                    if (!TryScanObject(json, ref index, out child, out error))
                    {
                        end = index;
                        return false;
                    }

                    items.Add(child);
                    continue;
                }

                if (c == '[')
                {
                    // 중첩 배열은 값 위치를 등록하지 않는다. 이 프로젝트의 설정 파일에
                    // 그런 구조가 없고, 없는 구조를 위한 코드는 검증되지 않은 채 남는다.
                    SkipContainer(json, ref index);
                    continue;
                }

                if (c == '"')
                {
                    ReadString(json, ref index);
                    continue;
                }

                if (ReadScalar(json, ref index) == null)
                {
                    end = index;
                    error = Format("배열 항목을 읽지 못했습니다(위치 {0}).", index);
                    return false;
                }
            }
        }

        /// <summary>객체나 배열 하나를 통째로 건너뛴다.</summary>
        /// <param name="json">원문.</param>
        /// <param name="index">여는 괄호 위치. 끝난 뒤 닫는 괄호 다음으로 옮긴다.</param>
        private static void SkipContainer(string json, ref int index)
        {
            int depth = 0;

            while (index < json.Length)
            {
                index = SkipTrivia(json, index);

                if (index >= json.Length)
                {
                    return;
                }

                char c = json[index];

                if (c == '"')
                {
                    ReadString(json, ref index);
                    continue;
                }

                if (c == '{' || c == '[')
                {
                    depth++;
                }
                else if (c == '}' || c == ']')
                {
                    depth--;

                    if (depth == 0)
                    {
                        index++;
                        return;
                    }
                }

                index++;
            }
        }

        /// <summary>공백과 주석을 건너뛴다.</summary>
        /// <param name="json">원문.</param>
        /// <param name="index">시작 위치.</param>
        /// <returns>다음 유효 문자 위치.</returns>
        internal static int SkipTrivia(string json, int index)
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
        /// <param name="index">여는 따옴표 위치. 끝난 뒤 닫는 따옴표 다음으로 옮긴다.</param>
        /// <returns>토큰 위치.</returns>
        internal static JsonTextSpan ReadString(string json, ref int index)
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

            return new JsonTextSpan(start, index, json.Substring(start, index - start));
        }

        /// <summary>문자열·수·리터럴 토큰을 읽는다.</summary>
        /// <param name="json">원문.</param>
        /// <param name="index">시작 위치. 끝난 뒤 값 다음으로 옮긴다.</param>
        /// <returns>토큰 위치. 읽지 못하면 null.</returns>
        private static JsonTextSpan ReadScalar(string json, ref int index)
        {
            if (index >= json.Length)
            {
                return null;
            }

            if (json[index] == '"')
            {
                return ReadString(json, ref index);
            }

            int start = index;

            while (index < json.Length)
            {
                char c = json[index];

                if (c == ',' || c == '}' || c == ']' || char.IsWhiteSpace(c) || c == '/')
                {
                    break;
                }

                index++;
            }

            return index == start
                ? null
                : new JsonTextSpan(start, index, json.Substring(start, index - start));
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

        /// <summary>불변 문화권으로 문자열을 만든다.</summary>
        /// <param name="format">서식.</param>
        /// <param name="args">인자.</param>
        /// <returns>서식이 적용된 문자열.</returns>
        private static string Format(string format, params object[] args)
        {
            return string.Format(CultureInfo.InvariantCulture, format, args);
        }
    }

    /// <summary>
    /// 값 치환 목록을 모아 원문에 한 번에 적용한다.
    /// </summary>
    /// <remarks>
    /// <para>뒤에서부터 적용한다. 앞에서부터 하면 첫 치환이 뒤쪽 위치를 전부 밀어 버린다.</para>
    /// <para><b>같은 값이면 건드리지 않는다.</b> 표기가 다른 같은 수(<c>-100.0</c> 와
    /// <c>-100</c>)도 그대로 둔다. 그러지 않으면 아무것도 편집하지 않고 저장한 사람이
    /// 파일 전체를 바꿔 놓게 되고, 그러면 다음 리뷰에서 무엇이 실제로 바뀌었는지 읽을 수 없다.</para>
    /// </remarks>
    public sealed class JsonTextPatch
    {
        private readonly List<Edit> _edits = new List<Edit>();

        /// <summary>바뀔 값이 하나라도 있는지 여부.</summary>
        public bool HasChanges
        {
            get { return _edits.Count > 0; }
        }

        /// <summary>수 필드를 설정한다.</summary>
        /// <param name="owner">대상 객체.</param>
        /// <param name="key">필드 이름.</param>
        /// <param name="value">값.</param>
        /// <param name="insertWhenMissing">원문에 없을 때 새로 넣을지 여부.</param>
        public void SetNumber(JsonTextObject owner, string key, double value, bool insertWhenMissing)
        {
            Put(owner, key, Number(value), insertWhenMissing);
        }

        /// <summary>문자열 필드를 설정한다.</summary>
        /// <param name="owner">대상 객체.</param>
        /// <param name="key">필드 이름.</param>
        /// <param name="value">값.</param>
        /// <param name="insertWhenMissing">원문에 없을 때 새로 넣을지 여부.</param>
        public void SetString(JsonTextObject owner, string key, string value, bool insertWhenMissing)
        {
            Put(owner, key, Quote(value), insertWhenMissing);
        }

        /// <summary>논리 필드를 설정한다.</summary>
        /// <param name="owner">대상 객체.</param>
        /// <param name="key">필드 이름.</param>
        /// <param name="value">값.</param>
        /// <param name="insertWhenMissing">원문에 없을 때 새로 넣을지 여부.</param>
        public void SetBoolean(JsonTextObject owner, string key, bool value, bool insertWhenMissing)
        {
            Put(owner, key, value ? "true" : "false", insertWhenMissing);
        }

        /// <summary>모아 둔 치환을 원문에 적용한다.</summary>
        /// <param name="json">원문.</param>
        /// <returns>수정된 원문.</returns>
        public string Apply(string json)
        {
            if (_edits.Count == 0)
            {
                return json;
            }

            _edits.Sort(CompareByStartDescending);

            StringBuilder builder = new StringBuilder(json);

            foreach (Edit edit in _edits)
            {
                builder.Remove(edit.Start, edit.End - edit.Start);
                builder.Insert(edit.Start, edit.Text);
            }

            return builder.ToString();
        }

        /// <summary>값을 교체하거나, 없으면 필요할 때만 새로 넣는다.</summary>
        /// <param name="owner">대상 객체.</param>
        /// <param name="key">필드 이름.</param>
        /// <param name="text">쓸 값.</param>
        /// <param name="insertWhenMissing">원문에 없을 때 새로 넣을지 여부.</param>
        private void Put(JsonTextObject owner, string key, string text, bool insertWhenMissing)
        {
            if (owner == null)
            {
                return;
            }

            JsonTextSpan existing = owner.Value(key);

            if (existing != null)
            {
                if (string.Equals(existing.Text, text, StringComparison.Ordinal)
                    || SameNumber(existing.Text, text))
                {
                    return;
                }

                _edits.Add(new Edit(existing.Start, existing.End, text));
                return;
            }

            if (!insertWhenMissing)
            {
                return;
            }

            string inserted = string.Concat(
                ",", Environment.NewLine, owner.Indent ?? string.Empty, Quote(key), ": ", text);

            _edits.Add(new Edit(owner.LastValueEnd, owner.LastValueEnd, inserted));
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
    }
}
