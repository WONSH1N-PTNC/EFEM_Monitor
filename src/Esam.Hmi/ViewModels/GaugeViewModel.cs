using System.Windows.Media;
using Esam.Hmi.Infrastructure;

namespace Esam.Hmi.ViewModels
{
    /// <summary>
    /// 게이지 1개의 표시 상태. 센서·밸브·팬 게이지가 이 하나의 모델을 공유한다.
    /// </summary>
    /// <remarks>
    /// <para>세 종류 게이지의 구성이 동일하기 때문에 모델을 통합했다.
    /// 중앙 대표값 + 단위, 우측에 제목과 두 줄 상세가 붙는 구조다.
    /// 디자인 원안의 세 카드 블록을 비교하면 이 구조가 그대로 반복된다.</para>
    /// <list type="bullet">
    ///   <item><description>센서: <c>-13.8 Pa</c> / 센서 2 (PV) / DEV -3.8 / SP -10 Pa ± 30 Pa</description></item>
    ///   <item><description>밸브: <c>43.3 % OPEN</c> / V-1 스로틀밸브 / 38.9° / 90° / 2,163 pulse</description></item>
    ///   <item><description>팬: <c>1,881 RPM</c> / F-1 송풍팬 / 센서 3 · -202 Pa / DRIVER 36.8 °C</description></item>
    /// </list>
    /// </remarks>
    public sealed class GaugeViewModel : ObservableObject
    {
        private double _ratio;
        private double _bandStart;
        private double _bandEnd;
        private bool _showBand;
        private string _valueText;
        private string _unitText;
        private string _title;
        private string _detailPrefix;
        private string _detailValue;
        private string _detailSecondary;
        private Brush _arcBrush;
        private Brush _valueBrush;
        private Brush _detailValueBrush;

        /// <summary>게이지를 생성한다.</summary>
        public GaugeViewModel()
        {
            _arcBrush = HmiPalette.Ok;
            _valueBrush = HmiPalette.TextPrimary;
            _detailValueBrush = HmiPalette.OkSoft;
            _valueText = string.Empty;
            _unitText = string.Empty;
            _title = string.Empty;
        }

        /// <summary>아크 채움 비율(0~1).</summary>
        public double Ratio
        {
            get { return _ratio; }
            set { Set(ref _ratio, value); }
        }

        /// <summary>정상 대역 시작 비율(0~1).</summary>
        public double BandStart
        {
            get { return _bandStart; }
            set { Set(ref _bandStart, value); }
        }

        /// <summary>정상 대역 끝 비율(0~1).</summary>
        public double BandEnd
        {
            get { return _bandEnd; }
            set { Set(ref _bandEnd, value); }
        }

        /// <summary>정상 대역 음영 표시 여부. 차압 게이지만 true 다.</summary>
        public bool ShowBand
        {
            get { return _showBand; }
            set { Set(ref _showBand, value); }
        }

        /// <summary>중앙 대표값 문자열.</summary>
        public string ValueText
        {
            get { return _valueText; }
            set { Set(ref _valueText, value); }
        }

        /// <summary>단위 문자열(Pa, RPM, % OPEN).</summary>
        public string UnitText
        {
            get { return _unitText; }
            set { Set(ref _unitText, value); }
        }

        /// <summary>제목(센서 2 (PV), V-1 스로틀밸브 등).</summary>
        public string Title
        {
            get { return _title; }
            set { Set(ref _title, value); }
        }

        /// <summary>상세 1행의 접두 라벨(DEV 등). 없으면 빈 문자열.</summary>
        public string DetailPrefix
        {
            get { return _detailPrefix; }
            set { Set(ref _detailPrefix, value); }
        }

        /// <summary>상세 1행의 값. 색을 따로 주어 강조한다.</summary>
        public string DetailValue
        {
            get { return _detailValue; }
            set { Set(ref _detailValue, value); }
        }

        /// <summary>상세 2행(설정값, pulse, 드라이버 온도 등).</summary>
        public string DetailSecondary
        {
            get { return _detailSecondary; }
            set { Set(ref _detailSecondary, value); }
        }

        /// <summary>아크 색상. 상태에 따라 ViewModel 이 바꾼다.</summary>
        public Brush ArcBrush
        {
            get { return _arcBrush; }
            set { Set(ref _arcBrush, value); }
        }

        /// <summary>중앙 값 텍스트 색상.</summary>
        public Brush ValueBrush
        {
            get { return _valueBrush; }
            set { Set(ref _valueBrush, value); }
        }

        /// <summary>상세 1행 값의 색상.</summary>
        public Brush DetailValueBrush
        {
            get { return _detailValueBrush; }
            set { Set(ref _detailValueBrush, value); }
        }
    }
}
