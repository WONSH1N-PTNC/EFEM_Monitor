using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows.Media;
using Esam.Communication.Configuration;
using Esam.Communication.Diagnostics;
using Esam.Domain.Models;
using Esam.Hmi.Infrastructure;
using Esam.Services;

namespace Esam.Hmi.ViewModels
{
    /// <summary>
    /// 상태 램프 1개.
    /// </summary>
    /// <remarks>
    /// 판정 근거는 <see cref="SystemSnapshot.Devices"/> 다. 값 모델(압력·밸브·팬)에서
    /// 유추하면 보조 계측 5종을 구분할 수 없고, 폴링하지 않는 장치도 드러나지 않는다.
    /// </remarks>
    public sealed class IoLampViewModel : ObservableObject
    {
        private IoLampState _state = IoLampState.NotConfigured;
        private string _detail;

        /// <summary>램프를 생성한다.</summary>
        /// <param name="label">표시명.</param>
        /// <param name="source">가리키는 대상.</param>
        public IoLampViewModel(string label, IoLampSource source)
        {
            Label = label;
            Source = source;
            _detail = string.Empty;
        }

        /// <summary>표시명.</summary>
        public string Label { get; private set; }

        /// <summary>가리키는 대상.</summary>
        public IoLampSource Source { get; private set; }

        /// <summary>현재 상태.</summary>
        public IoLampState State
        {
            get { return _state; }
            private set
            {
                if (Set(ref _state, value))
                {
                    Raise("StateText");
                    Raise("Brush");
                }
            }
        }

        /// <summary>부가 설명(어느 장치가 문제인지).</summary>
        public string Detail
        {
            get { return _detail; }
            private set { Set(ref _detail, value); }
        }

        /// <summary>상태 문구.</summary>
        public string StateText
        {
            get
            {
                switch (_state)
                {
                    case IoLampState.Healthy:
                        return "정상";

                    case IoLampState.Degraded:
                        return "열화";

                    case IoLampState.Failed:
                        return "무응답";

                    case IoLampState.Disabled:
                        return "사용 안 함";

                    case IoLampState.NotImplemented:
                        return "미구현";

                    default:
                        return "구성 없음";
                }
            }
        }

        /// <summary>램프 색.</summary>
        /// <remarks>
        /// "구성 없음" 과 "미구현" 은 회색이다. <b>초록으로 칠하면 없는 기능을
        /// 있다고 표시하는 것</b>이고, 빨강으로 칠하면 고칠 것이 있다는 뜻이 되어
        /// 현장에서 없는 고장을 찾게 된다.
        /// </remarks>
        public Brush Brush
        {
            get
            {
                switch (_state)
                {
                    case IoLampState.Healthy:
                        return HmiPalette.Ok;

                    case IoLampState.Degraded:
                        return HmiPalette.Warn;

                    case IoLampState.Failed:
                        return HmiPalette.Bad;

                    case IoLampState.Disabled:
                        return HmiPalette.TextMuted;

                    default:
                        return HmiPalette.AlarmDotIdle;
                }
            }
        }

        /// <summary>스냅샷으로 상태를 갱신한다.</summary>
        /// <param name="snapshot">현재 스냅샷.</param>
        public void Update(SystemSnapshot snapshot)
        {
            switch (Source)
            {
                case IoLampSource.Fdc:
                    // SECS/GEM 모듈이 없다(11.2 SCREEN 10). 통신 상태를 판정할 근거 자체가 없다.
                    State = IoLampState.NotImplemented;
                    Detail = "SECS/GEM 미구현";
                    break;

                case IoLampSource.CoolingFan:
                    UpdateCoolingFan(snapshot);
                    break;

                case IoLampSource.ControlBoxTemperature:
                    UpdateValueBacked(
                        snapshot, PointKeys.DriverPlc, snapshot.Auxiliary.TemperatureControlBox,
                        "TC 채널 미배정");
                    break;

                case IoLampSource.EfemTemperature:
                    UpdateValueBacked(
                        snapshot, PointKeys.DriverTempHumidity, snapshot.Auxiliary.TemperatureEfem,
                        "값 없음");
                    break;

                case IoLampSource.Humidity:
                    UpdateValueBacked(
                        snapshot, PointKeys.DriverTempHumidity, snapshot.Auxiliary.HumidityEfem,
                        "값 없음");
                    break;

                default:
                    UpdateGroup(snapshot, DriverOf(Source));
                    break;
            }
        }

        /// <summary>대상에 대응하는 드라이버 이름을 고른다.</summary>
        /// <param name="source">램프 대상.</param>
        /// <returns>드라이버 이름.</returns>
        private static string DriverOf(IoLampSource source)
        {
            switch (source)
            {
                case IoLampSource.Ffu:
                    return PointKeys.DriverFfu;

                case IoLampSource.BlowerFan:
                    return PointKeys.DriverModbusFan;

                case IoLampSource.ThrottleValve:
                    return PointKeys.DriverThrottleValve;

                case IoLampSource.PressureSensor:
                    return PointKeys.DriverPressureSensor;

                case IoLampSource.Particle:
                    return PointKeys.DriverParticle;

                case IoLampSource.Mfc:
                    return PointKeys.DriverMfc;

                default:
                    return PointKeys.DriverAirVelocity;
            }
        }

        /// <summary>같은 드라이버를 쓰는 장치 전체로 상태를 정한다.</summary>
        /// <param name="snapshot">스냅샷.</param>
        /// <param name="driver">드라이버 이름.</param>
        /// <remarks>
        /// <b>가장 나쁜 장치를 따른다.</b> 다섯 중 하나가 죽었는데 램프가 초록이면
        /// 그 하나는 아무도 찾지 않는다. 어느 장치인지는 <see cref="Detail"/> 에 적는다.
        /// </remarks>
        private void UpdateGroup(SystemSnapshot snapshot, string driver)
        {
            int total = 0;
            int polled = 0;
            int healthy = 0;
            List<string> failed = new List<string>();
            List<string> degraded = new List<string>();

            foreach (DeviceHealth health in snapshot.Devices.Values)
            {
                if (!string.Equals(health.Driver, driver, StringComparison.Ordinal))
                {
                    continue;
                }

                total++;

                if (!health.IsPolled)
                {
                    continue;
                }

                polled++;

                if (health.IsHealthy)
                {
                    healthy++;
                }
                else if (health.Quality == Quality.Bad || health.Quality == Quality.NoData)
                {
                    failed.Add(health.DeviceId);
                }
                else
                {
                    degraded.Add(health.DeviceId);
                }
            }

            if (total == 0)
            {
                State = IoLampState.NotConfigured;
                Detail = "구성에 없음";
                return;
            }

            if (polled == 0)
            {
                State = IoLampState.Disabled;
                Detail = "폴링 중지";
                return;
            }

            if (failed.Count > 0)
            {
                State = IoLampState.Failed;
                Detail = Join(failed);
                return;
            }

            if (degraded.Count > 0)
            {
                State = IoLampState.Degraded;
                Detail = Join(degraded);
                return;
            }

            State = IoLampState.Healthy;

            Detail = string.Format(
                CultureInfo.InvariantCulture, "{0}대", healthy);
        }

        /// <summary>제어박스 냉각팬 상태를 정한다.</summary>
        /// <param name="snapshot">스냅샷.</param>
        /// <remarks>
        /// 통신이 아니라 <b>PLC 입력 비트</b>가 근거다. 상·하를 따로 적는다.
        /// 합쳐서만 보여 주면 제어함을 열어 봐야 어느 쪽이 멈췄는지 안다.
        /// </remarks>
        private void UpdateCoolingFan(SystemSnapshot snapshot)
        {
            PlcDigitalState plc = snapshot.Plc;

            if (plc.Quality == Quality.Bad || plc.Quality == Quality.NoData)
            {
                State = IoLampState.Failed;
                Detail = "PLC 무응답";
                return;
            }

            if (plc.ControlBoxFanTopAlarm && plc.ControlBoxFanBottomAlarm)
            {
                State = IoLampState.Failed;
                Detail = "상·하 정지";
                return;
            }

            if (plc.ControlBoxFanTopAlarm || plc.ControlBoxFanBottomAlarm)
            {
                State = IoLampState.Failed;
                Detail = plc.ControlBoxFanTopAlarm ? "상부 정지" : "하부 정지";
                return;
            }

            State = plc.Quality == Quality.Good ? IoLampState.Healthy : IoLampState.Degraded;
            Detail = plc.Quality == Quality.Good ? "정상" : "값이 낡음";
        }

        /// <summary>장치 상태와 값 유무를 함께 보고 상태를 정한다.</summary>
        /// <param name="snapshot">스냅샷.</param>
        /// <param name="driver">값을 제공하는 드라이버.</param>
        /// <param name="value">현재 값. null 이면 미수집.</param>
        /// <param name="missingDetail">값이 없을 때 적을 사유.</param>
        /// <remarks>
        /// 장치는 응답하는데 이 항목만 값이 없는 경우가 있다.
        /// 판넬 온도가 그렇다 — PLC 는 살아 있지만 TC 채널이 배정되지 않았다.
        /// 장치 상태만 보면 초록이 되어 <b>없는 계측을 있다고 표시</b>한다.
        /// </remarks>
        private void UpdateValueBacked(
            SystemSnapshot snapshot, string driver, double? value, string missingDetail)
        {
            UpdateGroup(snapshot, driver);

            if (State != IoLampState.Healthy && State != IoLampState.Degraded)
            {
                return;
            }

            if (!value.HasValue)
            {
                State = IoLampState.NotConfigured;
                Detail = missingDetail;
            }
        }

        /// <summary>장치 ID 목록을 짧게 잇는다.</summary>
        /// <param name="ids">장치 ID 목록.</param>
        /// <returns>이어 붙인 문구.</returns>
        private static string Join(IList<string> ids)
        {
            if (ids.Count <= 3)
            {
                return string.Join(", ", ids);
            }

            return string.Format(
                CultureInfo.InvariantCulture,
                "{0} 외 {1}대",
                ids[0],
                ids.Count - 1);
        }
    }

    /// <summary>
    /// PLC 디지털 입력 1점.
    /// </summary>
    /// <remarks>
    /// <para><b>설정된 극성과 현재 판정값을 나란히 놓는다.</b> 판정값만 보면
    /// "EMO 를 누르지 않았는데 EMO 로 읽힌다" 는 사실까지는 알아도
    /// 극성이 뒤집혔는지 배선이 끊겼는지 구분하지 못한다.</para>
    /// <para>커미셔닝 미확정 항목 "PLC 입력 비트 극성" 을 판정하는 자리다.</para>
    /// </remarks>
    public sealed class PlcInputRowViewModel : ObservableObject
    {
        private bool _isActive;
        private bool _hasValue;
        private Quality _quality = Quality.NoData;

        /// <summary>설정된 측정점으로 행을 만든다.</summary>
        /// <param name="point">측정점 정의.</param>
        /// <exception cref="ArgumentNullException">측정점이 null 일 때.</exception>
        public PlcInputRowViewModel(PointDefinition point)
        {
            if (point == null)
            {
                throw new ArgumentNullException("point");
            }

            Key = point.Key;
            Signal = DescribeSignal(point.Key);
            IsWired = true;
            ActiveHigh = point.ActiveHigh;

            Address = string.Format(
                CultureInfo.InvariantCulture, "D10.{0}", point.Bit);
        }

        /// <summary>배선되지 않은 행을 만든다.</summary>
        /// <param name="key">측정점 키.</param>
        /// <param name="signal">신호 이름.</param>
        private PlcInputRowViewModel(string key, string signal)
        {
            Key = key;
            Signal = signal;
            Address = "- -";
            IsWired = false;
        }

        /// <summary>배선되지 않은 입력 행을 만든다.</summary>
        /// <param name="key">측정점 키.</param>
        /// <param name="signal">신호 이름.</param>
        /// <returns>행.</returns>
        public static PlcInputRowViewModel Unwired(string key, string signal)
        {
            return new PlcInputRowViewModel(key, signal);
        }

        /// <summary>측정점 키.</summary>
        public string Key { get; private set; }

        /// <summary>신호 이름.</summary>
        public string Signal { get; private set; }

        /// <summary>주소 표기.</summary>
        public string Address { get; private set; }

        /// <summary>배선된 입력인지 여부.</summary>
        public bool IsWired { get; private set; }

        /// <summary>설정된 극성(true = Active High).</summary>
        public bool ActiveHigh { get; private set; }

        /// <summary>극성 표기.</summary>
        public string PolarityText
        {
            get { return IsWired ? (ActiveHigh ? "Active H" : "Active L") : "- -"; }
        }

        /// <summary>정규화된 판정값 표기.</summary>
        public string StateText
        {
            get
            {
                if (!IsWired)
                {
                    return "미배선";
                }

                if (!_hasValue)
                {
                    return "- -";
                }

                return _isActive ? "1 · 발생" : "0 · 없음";
            }
        }

        /// <summary>판정값 색.</summary>
        public Brush Brush
        {
            get
            {
                if (!IsWired || !_hasValue)
                {
                    return HmiPalette.TextMuted;
                }

                if (_quality != Quality.Good)
                {
                    return HmiPalette.Warn;
                }

                return _isActive ? HmiPalette.Bad : HmiPalette.TextPrimary;
            }
        }

        /// <summary>PLC 상태로 값을 갱신한다.</summary>
        /// <param name="plc">PLC 디지털 입력 상태.</param>
        public void Update(PlcDigitalState plc)
        {
            if (plc == null || !IsWired)
            {
                return;
            }

            bool active;

            if (!TryRead(plc, out active))
            {
                _hasValue = false;
                Raise("StateText");
                Raise("Brush");
                return;
            }

            _hasValue = plc.Quality != Quality.NoData;
            _isActive = active;
            _quality = plc.Quality;

            Raise("StateText");
            Raise("Brush");
        }

        /// <summary>키에 해당하는 비트를 읽는다.</summary>
        /// <param name="plc">PLC 상태.</param>
        /// <param name="active">판정값.</param>
        /// <returns>키를 해석했으면 true.</returns>
        private bool TryRead(PlcDigitalState plc, out bool active)
        {
            active = false;

            if (string.Equals(Key, PointKeys.DiEmo, StringComparison.OrdinalIgnoreCase))
            {
                active = plc.EmoActive;
                return true;
            }

            if (string.Equals(Key, PointKeys.DiControlBoxFanTop, StringComparison.OrdinalIgnoreCase))
            {
                active = plc.ControlBoxFanTopAlarm;
                return true;
            }

            if (string.Equals(Key, PointKeys.DiControlBoxFanBottom, StringComparison.OrdinalIgnoreCase))
            {
                active = plc.ControlBoxFanBottomAlarm;
                return true;
            }

            for (int i = 0; i < plc.FanStopAlarms.Count; i++)
            {
                if (string.Equals(Key, PointKeys.DiFanStop(i), StringComparison.OrdinalIgnoreCase))
                {
                    active = plc.FanStopAlarms[i];
                    return true;
                }
            }

            return false;
        }

        /// <summary>측정점 키에서 신호 이름을 만든다.</summary>
        /// <param name="key">측정점 키.</param>
        /// <returns>신호 이름.</returns>
        private static string DescribeSignal(string key)
        {
            if (string.Equals(key, PointKeys.DiEmo, StringComparison.OrdinalIgnoreCase))
            {
                return "비상정지(EMO)";
            }

            if (string.Equals(key, PointKeys.DiControlBoxFanTop, StringComparison.OrdinalIgnoreCase))
            {
                return "제어박스 상부 냉각팬 정지";
            }

            if (string.Equals(key, PointKeys.DiControlBoxFanBottom, StringComparison.OrdinalIgnoreCase))
            {
                return "제어박스 하부 냉각팬 정지";
            }

            for (int i = 0; i < 5; i++)
            {
                if (string.Equals(key, PointKeys.DiFanStop(i), StringComparison.OrdinalIgnoreCase))
                {
                    return string.Format(
                        CultureInfo.InvariantCulture, "송풍팬 {0} 정지", i + 1);
                }
            }

            return key;
        }
    }

    /// <summary>
    /// 차압센서 1대의 원시값 표시 행.
    /// </summary>
    /// <remarks>
    /// <para>커미셔닝 미확정 항목 <b>"압력 스케일 0.1 Pa/LSB"</b> 를 판정하는 자리다.
    /// 환산값만 보면 10배 틀어져도 그럴듯해 보인다. 센서 자체 표시기의 숫자와
    /// 대조할 수 있도록 <b>환산 전 레지스터 값</b>을 함께 놓는다.</para>
    /// <para>레지스터 값은 스케일의 역산이다. <c>PressureReading.RawRegister</c> 는
    /// 통신 계층에서 채워지지 않아(항상 0) 쓸 수 없다.</para>
    /// </remarks>
    public sealed class PressureRawRowViewModel : ObservableObject
    {
        private readonly double _scale;
        private readonly double _bias;
        private string _register;
        private string _rawPa;
        private string _pa;
        private Quality _quality = Quality.NoData;

        /// <summary>센서 정의로 행을 만든다.</summary>
        /// <param name="device">디바이스 정의.</param>
        /// <param name="scale">압력 측정점의 스케일 [Pa/LSB].</param>
        /// <param name="bias">압력 측정점의 바이어스 [Pa].</param>
        /// <exception cref="ArgumentNullException">디바이스 정의가 null 일 때.</exception>
        public PressureRawRowViewModel(
            DeviceInstanceDefinition device, double scale, double bias)
        {
            if (device == null)
            {
                throw new ArgumentNullException("device");
            }

            DeviceId = device.Id;
            _scale = scale;
            _bias = bias;

            Offset = device.Offset.ToString("0.###", CultureInfo.InvariantCulture);

            ScaleText = scale > 0.0
                ? scale.ToString("0.####", CultureInfo.InvariantCulture)
                : "- -";

            _register = "- -";
            _rawPa = "- -";
            _pa = "- -";
        }

        /// <summary>디바이스 ID.</summary>
        public string DeviceId { get; private set; }

        /// <summary>영점 오프셋 [Pa].</summary>
        public string Offset { get; private set; }

        /// <summary>설정된 스케일 [Pa/LSB].</summary>
        public string ScaleText { get; private set; }

        /// <summary>환산 전 레지스터 값.</summary>
        public string Register
        {
            get { return _register; }
            private set { Set(ref _register, value); }
        }

        /// <summary>영점 보정 전 압력 [Pa].</summary>
        public string RawPa
        {
            get { return _rawPa; }
            private set { Set(ref _rawPa, value); }
        }

        /// <summary>최종 압력 [Pa].</summary>
        public string Pa
        {
            get { return _pa; }
            private set { Set(ref _pa, value); }
        }

        /// <summary>품질 표기.</summary>
        public string QualityText
        {
            get { return _quality.ToString(); }
        }

        /// <summary>품질 색.</summary>
        public Brush Brush
        {
            get
            {
                switch (_quality)
                {
                    case Quality.Good:
                        return HmiPalette.TextPrimary;

                    case Quality.Uncertain:
                    case Quality.Stale:
                        return HmiPalette.Warn;

                    default:
                        return HmiPalette.Bad;
                }
            }
        }

        /// <summary>판독값으로 표시를 갱신한다.</summary>
        /// <param name="reading">판독값. null 이면 값을 지운다.</param>
        public void Update(PressureReading reading)
        {
            if (reading == null)
            {
                Register = "- -";
                RawPa = "- -";
                Pa = "- -";
                _quality = Quality.NoData;
                Raise("QualityText");
                Raise("Brush");
                return;
            }

            _quality = reading.Quality;

            if (reading.Quality == Quality.NoData)
            {
                Register = "- -";
                RawPa = "- -";
                Pa = "- -";
            }
            else
            {
                // 레지스터 = (환산값 - 바이어스) / 스케일. 스케일 역산이다.
                Register = _scale > 0.0
                    ? Math.Round((reading.RawPa - _bias) / _scale, MidpointRounding.AwayFromZero)
                        .ToString("0", CultureInfo.InvariantCulture)
                    : "- -";

                RawPa = reading.RawPa.ToString("0.##", CultureInfo.InvariantCulture);
                Pa = reading.Pa.ToString("0.##", CultureInfo.InvariantCulture);
            }

            Raise("QualityText");
            Raise("Brush");
        }
    }

    /// <summary>
    /// 포트 1개의 통신 통계 행.
    /// </summary>
    public sealed class PortStatusRowViewModel : ObservableObject
    {
        private string _cycleMs = "- -";
        private string _successRate = "- -";
        private string _timeouts = "- -";
        private string _crcErrors = "- -";
        private string _total = "- -";
        private Brush _brush = HmiPalette.TextMuted;

        /// <summary>포트 행을 만든다.</summary>
        /// <param name="portId">포트 ID.</param>
        public PortStatusRowViewModel(string portId)
        {
            PortId = portId;
        }

        /// <summary>포트 ID.</summary>
        public string PortId { get; private set; }

        /// <summary>마지막 사이클 시간 [ms].</summary>
        public string CycleMs
        {
            get { return _cycleMs; }
            private set { Set(ref _cycleMs, value); }
        }

        /// <summary>성공률 [%].</summary>
        public string SuccessRate
        {
            get { return _successRate; }
            private set { Set(ref _successRate, value); }
        }

        /// <summary>타임아웃 누계.</summary>
        public string Timeouts
        {
            get { return _timeouts; }
            private set { Set(ref _timeouts, value); }
        }

        /// <summary>CRC 오류 누계.</summary>
        public string CrcErrors
        {
            get { return _crcErrors; }
            private set { Set(ref _crcErrors, value); }
        }

        /// <summary>전체 트랜잭션 수.</summary>
        public string Total
        {
            get { return _total; }
            private set { Set(ref _total, value); }
        }

        /// <summary>사이클 시간 색.</summary>
        public Brush Brush
        {
            get { return _brush; }
            private set { Set(ref _brush, value); }
        }

        /// <summary>통계로 표시를 갱신한다.</summary>
        /// <param name="statistics">포트 통계.</param>
        public void Update(PortStatistics statistics)
        {
            if (statistics == null || statistics.TotalTransactions == 0)
            {
                // 0 ms 로 적으면 가장 빠른 포트로 보인다.
                CycleMs = "- -";
                SuccessRate = "- -";
                Brush = HmiPalette.TextMuted;
                return;
            }

            CycleMs = statistics.LastCycleMs.ToString("0", CultureInfo.InvariantCulture);
            SuccessRate = statistics.SuccessRatePercent.ToString("0.0", CultureInfo.InvariantCulture);
            Timeouts = statistics.TimeoutCount.ToString(CultureInfo.InvariantCulture);
            CrcErrors = statistics.CrcErrorCount.ToString(CultureInfo.InvariantCulture);
            Total = statistics.TotalTransactions.ToString(CultureInfo.InvariantCulture);

            Brush = statistics.SuccessRatePercent < 99.0
                ? HmiPalette.Bad
                : HmiPalette.TextPrimary;
        }
    }
}
