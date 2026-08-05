using System.Windows.Media;
using Esam.Hmi.Infrastructure;

namespace Esam.Hmi.ViewModels
{
    /// <summary>
    /// 체인 1조(차압센서 + 스로틀밸브 + 송풍팬) 카드의 표시 상태.
    /// </summary>
    /// <remarks>
    /// <para>카드는 위에서 아래로 <b>급기 → 밸브 → 배기</b> 순서로 배치된다.
    /// 실제 기류 방향과 화면 배치를 일치시켜, 도식을 따로 그리지 않고도
    /// 어느 지점의 값인지 직관적으로 알 수 있게 한 것이 디자인의 의도다.</para>
    /// <para>카드 하단의 알람 바는 <b>이 체인에 속한 알람만</b> 점등한다.
    /// 상단 배너가 전체 알람을 보여주므로, 하단 바는 "어느 체인의 문제인가"를
    /// 즉시 좁혀 주는 역할을 한다.</para>
    /// </remarks>
    public sealed class ChainCardViewModel : ObservableObject
    {
        private string _name;
        private string _statusText;
        private Brush _statusBrush;
        private Brush _accentBrush;
        private string _alarmCode;
        private string _alarmText;
        private Brush _alarmForeground;
        private Brush _alarmBackground;
        private Brush _alarmDot;

        /// <summary>체인 카드를 생성한다.</summary>
        /// <param name="chainId">체인 번호(1~5).</param>
        public ChainCardViewModel(int chainId)
        {
            ChainId = chainId;
            _name = "CHAIN 2-" + chainId.ToString(System.Globalization.CultureInfo.InvariantCulture);
            _statusText = "NORMAL";
            _statusBrush = HmiPalette.Ok;
            _accentBrush = HmiPalette.Ok;

            Sensor = new GaugeViewModel();
            Valve = new GaugeViewModel();
            Fan = new GaugeViewModel();

            SetNoAlarm();
        }

        /// <summary>체인 번호(1~5).</summary>
        public int ChainId { get; private set; }

        /// <summary>카드 제목(CHAIN 2-1).</summary>
        public string Name
        {
            get { return _name; }
            set { Set(ref _name, value); }
        }

        /// <summary>상태 배지 문자열(NORMAL / DEVIATING / OUT OF BAND).</summary>
        public string StatusText
        {
            get { return _statusText; }
            set { Set(ref _statusText, value); }
        }

        /// <summary>상태 배지 색상.</summary>
        public Brush StatusBrush
        {
            get { return _statusBrush; }
            set { Set(ref _statusBrush, value); }
        }

        /// <summary>카드 상단 강조선 색상. 상태색과 같게 맞춘다.</summary>
        public Brush AccentBrush
        {
            get { return _accentBrush; }
            set { Set(ref _accentBrush, value); }
        }

        /// <summary>차압센서 게이지(제어 기준값).</summary>
        public GaugeViewModel Sensor { get; private set; }

        /// <summary>스로틀밸브 게이지.</summary>
        public GaugeViewModel Valve { get; private set; }

        /// <summary>송풍팬 게이지.</summary>
        public GaugeViewModel Fan { get; private set; }

        /// <summary>하단 알람 바의 코드. 알람이 없으면 "—".</summary>
        public string AlarmCode
        {
            get { return _alarmCode; }
            set { Set(ref _alarmCode, value); }
        }

        /// <summary>하단 알람 바의 내용. 알람이 없으면 "NO ALARM".</summary>
        public string AlarmText
        {
            get { return _alarmText; }
            set { Set(ref _alarmText, value); }
        }

        /// <summary>알람 바 글자색.</summary>
        public Brush AlarmForeground
        {
            get { return _alarmForeground; }
            set { Set(ref _alarmForeground, value); }
        }

        /// <summary>알람 바 배경색.</summary>
        public Brush AlarmBackground
        {
            get { return _alarmBackground; }
            set { Set(ref _alarmBackground, value); }
        }

        /// <summary>알람 바 좌측 점 색상.</summary>
        public Brush AlarmDot
        {
            get { return _alarmDot; }
            set { Set(ref _alarmDot, value); }
        }

        /// <summary>
        /// 이 체인에 확정 알람이 있는지 여부.
        /// 상태 배지가 계측값만 보고 NORMAL 을 표시하면
        /// 하단에 빨간 알람 바가 켜져 있는데도 정상으로 보이는 모순이 생긴다.
        /// </summary>
        public bool HasCriticalAlarm { get; private set; }

        /// <summary>이 체인에 알람이 없는 상태로 설정한다.</summary>
        public void SetNoAlarm()
        {
            HasCriticalAlarm = false;
            AlarmCode = "—";
            AlarmText = "NO ALARM";
            AlarmForeground = HmiPalette.TextDisabled;
            AlarmBackground = HmiPalette.AlarmRowIdle;
            AlarmDot = HmiPalette.AlarmDotIdle;
        }

        /// <summary>이 체인의 알람을 설정한다.</summary>
        /// <param name="code">알람 코드.</param>
        /// <param name="text">알람 내용.</param>
        /// <param name="critical">true 면 확정 알람 색(적), false 면 경고 색(황).</param>
        public void SetAlarm(string code, string text, bool critical)
        {
            HasCriticalAlarm = critical;
            AlarmCode = code;
            AlarmText = text;
            AlarmForeground = critical ? HmiPalette.Bad : HmiPalette.Warn;
            AlarmBackground = critical ? HmiPalette.AlarmRowBad : HmiPalette.AlarmRowWarn;
            AlarmDot = critical ? HmiPalette.Bad : HmiPalette.Warn;
        }
    }
}
