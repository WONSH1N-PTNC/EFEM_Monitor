using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Media;
using Esam.Domain.Alarms;
using Esam.Hmi.Infrastructure;

namespace Esam.Hmi.ViewModels
{
    /// <summary>
    /// 알람 규칙 1건의 편집 행.
    /// </summary>
    /// <remarks>
    /// <para>수치는 <b>문자열로 들고 있는다.</b> <c>double</c> 로 바인딩하면
    /// 입력 도중의 <c>-</c> 나 <c>1.</c> 같은 중간 상태에서 바인딩이 실패해
    /// 입력이 되돌아간다. 파싱은 저장 시점에 한 번만 한다.</para>
    /// <para>원본 규칙을 들고 있다가 저장할 때 <b>복사본에 편집을 얹는다.</b>
    /// 살아 있는 객체를 고친 뒤 검증에 실패하면 되돌려야 하는데,
    /// 되돌릴 것을 만들지 않는 편이 되돌리는 코드보다 낫다(D18).</para>
    /// </remarks>
    public sealed class AlarmRuleRowViewModel : ObservableObject
    {
        /// <summary>임계값을 레시피가 관리할 때 표시할 문구.</summary>
        private const string RecipeManaged = "recipe.json";

        private static readonly ReadOnlyCollection<string> SeverityChoices =
            new ReadOnlyCollection<string>(new[] { "Info", "Warning", "Alarm", "Critical" });

        private static readonly ReadOnlyCollection<string> ResetChoices =
            new ReadOnlyCollection<string>(new[] { "Auto", "Manual" });

        private readonly AlarmRule _source;
        private readonly Action _changed;

        private string _threshold;
        private string _debounce;
        private string _severity;
        private string _resetPolicy;
        private bool _enabled;
        private bool _wasEnabled;

        /// <summary>규칙으로 행을 만든다.</summary>
        /// <param name="rule">원본 규칙.</param>
        /// <param name="changed">값이 바뀌면 알릴 콜백(null 허용).</param>
        /// <exception cref="ArgumentNullException">규칙이 null 일 때.</exception>
        public AlarmRuleRowViewModel(AlarmRule rule, Action changed)
        {
            if (rule == null)
            {
                throw new ArgumentNullException("rule");
            }

            _source = rule;
            _changed = changed;

            Code = rule.Code;
            Name = rule.Name;
            Source = rule.Source;
            Condition = rule.Condition.ToString();
            MessageKo = rule.MessageKo;

            // 임계값을 레시피가 관리하는 규칙은 이 칸을 잠근다.
            // 여기서 고칠 수 있게 하면 같은 숫자가 두 곳에 산다.
            IsThresholdEditable =
                rule.Condition != AlarmConditionType.AboveHighLimit
                && rule.Condition != AlarmConditionType.BelowLowLimit
                && UsesThreshold(rule.Condition);

            _threshold = IsThresholdEditable ? Format(rule.Threshold) : null;
            _debounce = Format(rule.DebounceMs);
            _severity = rule.Severity.ToString();
            _resetPolicy = rule.ResetPolicy.ToString();
            _enabled = rule.Enabled;
            _wasEnabled = rule.Enabled;
        }

        /// <summary>알람 코드. 편집 대상이 아니다.</summary>
        public string Code { get; private set; }

        /// <summary>표시명. 편집 대상이 아니다.</summary>
        public string Name { get; private set; }

        /// <summary>판정 대상 경로. 편집 대상이 아니다.</summary>
        public string Source { get; private set; }

        /// <summary>판정 조건. 편집 대상이 아니다.</summary>
        public string Condition { get; private set; }

        /// <summary>한국어 메시지.</summary>
        public string MessageKo { get; private set; }

        /// <summary>임계값을 화면에서 고칠 수 있는지 여부.</summary>
        public bool IsThresholdEditable { get; private set; }

        /// <summary>선택 가능한 심각도.</summary>
        public IList<string> SeverityChoiceList
        {
            get { return SeverityChoices; }
        }

        /// <summary>선택 가능한 해제 정책.</summary>
        public IList<string> ResetChoiceList
        {
            get { return ResetChoices; }
        }

        /// <summary>임계값 [공학단위].</summary>
        public string Threshold
        {
            get { return _threshold; }
            set { Set(ref _threshold, value); }
        }

        /// <summary>임계값 칸에 대신 표시할 문구. 편집 가능하면 null.</summary>
        public string ThresholdNotice
        {
            get { return IsThresholdEditable ? null : RecipeManaged; }
        }

        /// <summary>확정 시간 [ms].</summary>
        public string DebounceMs
        {
            get { return _debounce; }
            set { Set(ref _debounce, value); }
        }

        /// <summary>심각도.</summary>
        public string Severity
        {
            get { return _severity; }
            set
            {
                if (Set(ref _severity, value))
                {
                    Raise("IsCritical");
                    Raise("SeverityBrush");
                    Notify();
                }
            }
        }

        /// <summary>해제 정책.</summary>
        public string ResetPolicy
        {
            get { return _resetPolicy; }
            set { Set(ref _resetPolicy, value); }
        }

        /// <summary>이 규칙을 사용할지 여부.</summary>
        public bool Enabled
        {
            get { return _enabled; }
            set
            {
                if (Set(ref _enabled, value))
                {
                    Notify();
                }
            }
        }

        /// <summary>마지막 저장 시점의 활성 여부.</summary>
        /// <remarks>
        /// 치명 알람을 <b>이번에 끄려는 것인지</b> 판정하는 데 쓴다.
        /// 원래 꺼져 있던 규칙까지 확인을 요구하면 확인이 형식이 된다.
        /// </remarks>
        public bool WasEnabled
        {
            get { return _wasEnabled; }
        }

        /// <summary>치명 규칙인지 여부.</summary>
        public bool IsCritical
        {
            get
            {
                return string.Equals(
                    _severity, AlarmSeverity.Critical.ToString(), StringComparison.Ordinal);
            }
        }

        /// <summary>심각도 표시 색.</summary>
        public Brush SeverityBrush
        {
            get
            {
                if (IsCritical)
                {
                    return HmiPalette.Bad;
                }

                return string.Equals(
                    _severity, AlarmSeverity.Warning.ToString(), StringComparison.Ordinal)
                    ? HmiPalette.Warn
                    : HmiPalette.TextPrimary;
            }
        }

        /// <summary>검색어에 걸리는지 판정한다.</summary>
        /// <param name="needle">검색어.</param>
        /// <returns>코드·이름·경로 중 하나에 포함되면 true.</returns>
        public bool Matches(string needle)
        {
            return Contains(Code, needle)
                   || Contains(Name, needle)
                   || Contains(Source, needle)
                   || Contains(MessageKo, needle);
        }

        /// <summary>저장이 끝난 뒤 기준선을 현재 값으로 옮긴다.</summary>
        public void MarkSaved()
        {
            _wasEnabled = _enabled;
        }

        /// <summary>편집 결과를 규칙 사본으로 만든다.</summary>
        /// <param name="error">변환 실패 사유(출력). 성공 시 null.</param>
        /// <returns>규칙 사본. 실패하면 null.</returns>
        /// <remarks>
        /// 원본을 고치지 않는다. 검증에 실패했을 때 되돌릴 것이 없어야 한다.
        /// </remarks>
        public AlarmRule ToRule(out string error)
        {
            double debounce;

            if (!TryParse(_debounce, out debounce))
            {
                error = Code + ": 확정 시간이 숫자가 아닙니다.";
                return null;
            }

            if (debounce < 0.0)
            {
                error = Code + ": 확정 시간은 음수일 수 없습니다.";
                return null;
            }

            double threshold = _source.Threshold;

            if (IsThresholdEditable && !TryParse(_threshold, out threshold))
            {
                error = Code + ": 임계값이 숫자가 아닙니다.";
                return null;
            }

            AlarmSeverity severity;

            if (!TryParseEnum(_severity, out severity))
            {
                error = Code + ": 심각도를 해석할 수 없습니다.";
                return null;
            }

            AlarmResetPolicy reset;

            if (!TryParseEnum(_resetPolicy, out reset))
            {
                error = Code + ": 해제 정책을 해석할 수 없습니다.";
                return null;
            }

            AlarmRule copy = new AlarmRule();

            // 편집 대상이 아닌 항목은 원본에서 그대로 옮긴다.
            // 빠뜨리면 저장할 때마다 조용히 지워진다.
            copy.Code = _source.Code;
            copy.Name = _source.Name;
            copy.Source = _source.Source;
            copy.Condition = _source.Condition;
            copy.ReferenceMode = _source.ReferenceMode;
            copy.IndependentThreshold = _source.IndependentThreshold;
            copy.MessageKo = _source.MessageKo;

            copy.Threshold = threshold;
            copy.DebounceMs = debounce;
            copy.Severity = severity;
            copy.ResetPolicy = reset;
            copy.Enabled = _enabled;

            error = null;
            return copy;
        }

        /// <summary>값 변경을 편집기에 알린다.</summary>
        private void Notify()
        {
            if (_changed != null)
            {
                _changed();
            }
        }

        /// <summary>해당 조건이 자체 임계값을 쓰는지 판정한다.</summary>
        /// <param name="condition">판정 조건.</param>
        /// <returns>임계값을 쓰면 true.</returns>
        private static bool UsesThreshold(AlarmConditionType condition)
        {
            return condition == AlarmConditionType.GreaterThan
                   || condition == AlarmConditionType.LessThan;
        }

        /// <summary>대소문자를 무시하고 포함 여부를 본다.</summary>
        /// <param name="text">대상 문자열.</param>
        /// <param name="needle">검색어.</param>
        /// <returns>포함하면 true.</returns>
        private static bool Contains(string text, string needle)
        {
            return text != null
                   && text.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>불변 문화권으로 파싱한다.</summary>
        /// <param name="text">입력 문자열.</param>
        /// <param name="value">파싱 결과.</param>
        /// <returns>성공하면 true.</returns>
        private static bool TryParse(string text, out double value)
        {
            return double.TryParse(
                text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        /// <summary>열거형 이름을 해석한다.</summary>
        /// <typeparam name="T">열거형 타입.</typeparam>
        /// <param name="text">이름.</param>
        /// <param name="value">해석 결과.</param>
        /// <returns>성공하면 true.</returns>
        private static bool TryParseEnum<T>(string text, out T value) where T : struct
        {
            return Enum.TryParse(text, false, out value) && Enum.IsDefined(typeof(T), value);
        }

        /// <summary>표시용 문자열로 만든다.</summary>
        /// <param name="value">값.</param>
        /// <returns>문자열.</returns>
        private static string Format(double value)
        {
            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }
    }
}
