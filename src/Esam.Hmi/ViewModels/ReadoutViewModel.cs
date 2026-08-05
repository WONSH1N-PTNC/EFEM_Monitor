using System.Windows.Media;
using Esam.Hmi.Infrastructure;

namespace Esam.Hmi.ViewModels
{
    /// <summary>
    /// 라벨 + 값 + 단위 형태의 단순 계측 표시 1행.
    /// 우측 패널의 보조 상태(풍속, 온습도, MFC)와 챔버 정보에 사용한다.
    /// </summary>
    public sealed class ReadoutViewModel : ObservableObject
    {
        private string _value;
        private Brush _valueBrush;

        /// <summary>계측 표시 행을 생성한다.</summary>
        /// <param name="label">항목명(풍속 1, Temp (EFEM) 등).</param>
        /// <param name="value">값 문자열.</param>
        /// <param name="unit">단위 문자열.</param>
        /// <param name="highlight">true 면 값을 강조색으로 표시한다(임계 근접 등).</param>
        public ReadoutViewModel(string label, string value, string unit, bool highlight = false)
        {
            Label = label;
            _value = value;
            Unit = unit;
            _valueBrush = highlight ? HmiPalette.Bad : HmiPalette.TextPrimary;
        }

        /// <summary>항목명.</summary>
        public string Label { get; private set; }

        /// <summary>단위 문자열.</summary>
        public string Unit { get; private set; }

        /// <summary>값 문자열.</summary>
        public string Value
        {
            get { return _value; }
            set { Set(ref _value, value); }
        }

        /// <summary>값 색상. 임계 근접 시 ViewModel 이 강조색으로 바꾼다.</summary>
        public Brush ValueBrush
        {
            get { return _valueBrush; }
            set { Set(ref _valueBrush, value); }
        }
    }
}
