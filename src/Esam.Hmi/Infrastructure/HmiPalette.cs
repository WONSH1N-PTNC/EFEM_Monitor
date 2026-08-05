using System.Windows.Media;

namespace Esam.Hmi.Infrastructure
{
    /// <summary>
    /// 코드에서 사용하는 브러시 모음.
    /// </summary>
    /// <remarks>
    /// <para>XAML 리소스(<c>Themes/Palette.xaml</c>)와 같은 색을 코드에서도 써야 하는 경우가 있다.
    /// 예를 들어 상태에 따라 게이지 색이 바뀌는 판정은 ViewModel 이 하므로
    /// ViewModel 이 브러시를 직접 골라야 한다.</para>
    /// <para><c>Application.Current.Resources</c> 조회 대신 정적 필드를 쓰는 이유는
    /// Visual Studio 디자이너에서 <c>Application.Current</c> 가 null 일 수 있어
    /// 디자인 타임 미리보기가 깨지기 때문이다.</para>
    /// <para>모든 브러시는 <c>Freeze()</c> 하여 스레드 간 공유와 렌더링 성능을 확보한다.
    /// 200ms 주기로 갱신되는 화면에서 브러시를 매번 새로 만들면 GC 압력이 커진다.</para>
    /// </remarks>
    public static class HmiPalette
    {
        /// <summary>정상. 대역 안.</summary>
        public static readonly SolidColorBrush Ok = Frozen("#FF3ECF8E");

        /// <summary>정상(밝은 톤). 수치 강조용.</summary>
        public static readonly SolidColorBrush OkSoft = Frozen("#FF5FD39A");

        /// <summary>이탈 진행 중.</summary>
        public static readonly SolidColorBrush Warn = Frozen("#FFFFB020");

        /// <summary>이탈 확정.</summary>
        public static readonly SolidColorBrush Bad = Frozen("#FFFF5F56");

        /// <summary>이탈 확정(밝은 톤).</summary>
        public static readonly SolidColorBrush BadSoft = Frozen("#FFFF8B84");

        /// <summary>액추에이터 강조색(청록). 계측값과 구분한다.</summary>
        public static readonly SolidColorBrush Accent = Frozen("#FF4CC9F0");

        /// <summary>본문 텍스트.</summary>
        public static readonly SolidColorBrush TextPrimary = Frozen("#FFE9F2F9");

        /// <summary>보조 텍스트.</summary>
        public static readonly SolidColorBrush TextSecondary = Frozen("#FF8FA3B4");

        /// <summary>흐린 텍스트.</summary>
        public static readonly SolidColorBrush TextMuted = Frozen("#FF5D6D7C");

        /// <summary>비활성 텍스트.</summary>
        public static readonly SolidColorBrush TextDisabled = Frozen("#FF54677A");

        /// <summary>게이지 트랙(빈 구간).</summary>
        public static readonly SolidColorBrush GaugeTrack = Frozen("#FF1B2530");

        /// <summary>게이지·트렌드의 정상 대역 음영.</summary>
        public static readonly SolidColorBrush GaugeBand = Frozen("#332F9E6E");

        /// <summary>알람 행 배경 — 확정 알람.</summary>
        public static readonly SolidColorBrush AlarmRowBad = Frozen("#FF251416");

        /// <summary>알람 행 배경 — 경고.</summary>
        public static readonly SolidColorBrush AlarmRowWarn = Frozen("#FF241D10");

        /// <summary>알람 행 배경 — 알람 없음.</summary>
        public static readonly SolidColorBrush AlarmRowIdle = Frozen("#FF111A22");

        /// <summary>알람 점 — 알람 없음.</summary>
        public static readonly SolidColorBrush AlarmDotIdle = Frozen("#FF2B3B4A");

        /// <summary>테두리 — 확정 알람.</summary>
        public static readonly SolidColorBrush BorderBad = Frozen("#FF5D2226");

        /// <summary>테두리 — 정상.</summary>
        public static readonly SolidColorBrush BorderNormal = Frozen("#FF1E2831");

        /// <summary>격자선.</summary>
        public static readonly SolidColorBrush GridLine = Frozen("#FF2B3B4A");

        /// <summary>트렌드 플롯 배경.</summary>
        public static readonly SolidColorBrush TrendBackground = Frozen("#FF111A22");

        /// <summary>
        /// 체인별 트렌드 라인색. 순서는 디자인 원안과 동일해야 범례가 일치한다.
        /// </summary>
        public static readonly SolidColorBrush[] ChainLines =
        {
            Frozen("#FF4CC9F0"),
            Frozen("#FF5FD39A"),
            Frozen("#FFF2C14E"),
            Frozen("#FFF2865E"),
            Frozen("#FFB58CF0")
        };

        /// <summary>16진 문자열로 고정(Freeze)된 브러시를 만든다.</summary>
        /// <param name="hex">"#AARRGGBB" 형식 색상.</param>
        /// <returns>고정된 브러시.</returns>
        private static SolidColorBrush Frozen(string hex)
        {
            SolidColorBrush brush = new SolidColorBrush(
                (Color)ColorConverter.ConvertFromString(hex));

            brush.Freeze();
            return brush;
        }
    }
}
