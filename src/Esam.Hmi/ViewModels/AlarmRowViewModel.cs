using System.Windows.Media;
using Esam.Hmi.Infrastructure;

namespace Esam.Hmi.ViewModels
{
    /// <summary>
    /// 알람 1건의 표시 상태. 상단 티커와 우측 ACTIVE ALARM 목록이 함께 사용한다.
    /// </summary>
    public sealed class AlarmRowViewModel : ObservableObject
    {
        private bool _isAcknowledged;

        /// <summary>알람 항목을 생성한다.</summary>
        /// <param name="code">알람 코드(A06, P09 등).</param>
        /// <param name="name">알람 내용.</param>
        /// <param name="severity">심각도 표기(ALARM / WARN / CRITICAL).</param>
        /// <param name="source">발생 위치(CHAMBER, CHAIN 2-1 등).</param>
        /// <param name="time">발생 시각(HH:mm:ss).</param>
        /// <param name="critical">true 면 적색, false 면 황색으로 표시한다.</param>
        public AlarmRowViewModel(
            string code, string name, string severity, string source, string time, bool critical)
        {
            Code = code;
            Name = name;
            Severity = severity;
            Source = source;
            Time = time;
            IsCritical = critical;
        }

        /// <summary>알람 코드.</summary>
        public string Code { get; private set; }

        /// <summary>알람 내용.</summary>
        public string Name { get; private set; }

        /// <summary>심각도 표기.</summary>
        public string Severity { get; private set; }

        /// <summary>발생 위치.</summary>
        public string Source { get; private set; }

        /// <summary>발생 시각.</summary>
        public string Time { get; private set; }

        /// <summary>확정 알람(적색) 여부.</summary>
        public bool IsCritical { get; private set; }

        /// <summary>강조 색상. 코드와 좌측 띠에 사용한다.</summary>
        public Brush AccentBrush
        {
            get { return IsCritical ? HmiPalette.Bad : HmiPalette.Warn; }
        }

        /// <summary>행 배경색.</summary>
        public Brush RowBackground
        {
            get { return IsCritical ? HmiPalette.AlarmRowBad : HmiPalette.AlarmRowWarn; }
        }

        /// <summary>
        /// 사용자가 확인(Ack)했는지 여부.
        /// 미확인 알람은 화면에서 점멸시켜 주의를 끌어야 한다.
        /// </summary>
        public bool IsAcknowledged
        {
            get { return _isAcknowledged; }
            set { Set(ref _isAcknowledged, value); }
        }
    }
}
