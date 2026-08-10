using System;
using System.Globalization;

namespace Esam.Domain.Configuration
{
    /// <summary>
    /// 센서 1대의 운전 설정값. <c>recipe.json</c> 의 <c>sensors[]</c> 항목 1건에 대응한다.
    /// </summary>
    /// <remarks>
    /// <para><b>ECID 마스터다.</b> <c>ESAM_IO List</c> 의 ECID 시트 39항목은
    /// 압력센서 13대 × (설정값 + 상한 + 하한) 이며, 이 타입 13개가 그 실체다.
    /// 상위(GEM)가 ECID 로 읽고 쓰는 값이 여기 있다.</para>
    /// <para><b>상한과 하한을 독립적으로 갖는다.</b> 종전 <see cref="ModeSetting"/> 은
    /// <c>Setpoint ± Band</c> 로 대칭을 강제했다. 배기 계통은 상한 여유와 하한 여유가
    /// 다를 수 있으므로 독립적으로 두는 편이 물리적으로 맞다.</para>
    /// <para><b>이탈 확정 시간(Time)은 여기 없다.</b> 모드별 공통이므로
    /// <c>control.json</c> 에 남는다(2026-08-10 확정). 센서별로 다르게 둘 이유가 없고,
    /// ECID 항목에도 포함되지 않는다.</para>
    /// </remarks>
    public sealed class SensorSetting
    {
        /// <summary>대상 센서의 디바이스 ID. <c>device-map.json</c> 의 ID 와 일치해야 한다.</summary>
        public string DeviceId { get; set; }

        /// <summary>목표 압력 [Pa].</summary>
        public double SetpointPa { get; set; }

        /// <summary>정상 대역 상한 [Pa].</summary>
        public double HighLimitPa { get; set; }

        /// <summary>정상 대역 하한 [Pa].</summary>
        public double LowLimitPa { get; set; }

        /// <summary>대역 폭 [Pa]. 상하한이 비대칭이면 넓은 쪽을 반환한다. 표시용이다.</summary>
        public double BandPa
        {
            get
            {
                double upper = HighLimitPa - SetpointPa;
                double lower = SetpointPa - LowLimitPa;

                return upper > lower ? upper : lower;
            }
        }

        /// <summary>기본값으로 초기화한다.</summary>
        public SensorSetting()
        {
        }

        /// <summary>값을 지정해 초기화한다.</summary>
        /// <param name="deviceId">센서 디바이스 ID.</param>
        /// <param name="setpointPa">목표 압력 [Pa].</param>
        /// <param name="lowLimitPa">대역 하한 [Pa].</param>
        /// <param name="highLimitPa">대역 상한 [Pa].</param>
        public SensorSetting(string deviceId, double setpointPa, double lowLimitPa, double highLimitPa)
        {
            DeviceId = deviceId;
            SetpointPa = setpointPa;
            LowLimitPa = lowLimitPa;
            HighLimitPa = highLimitPa;
        }

        /// <summary>
        /// 모드별 이탈 확정 시간과 합쳐 제어 파라미터를 만든다.
        /// </summary>
        /// <param name="timeSec">해당 모드의 이탈 확정 시간 [초].</param>
        /// <returns>제어 정책에 넘길 파라미터.</returns>
        /// <remarks>
        /// 이것이 recipe 와 control 을 잇는 유일한 지점이다.
        /// <see cref="ModeSetting"/> 은 더 이상 파일에서 역직렬화되지 않고
        /// 두 설정을 합친 런타임 값 객체가 된다.
        /// </remarks>
        public ModeSetting ToModeSetting(double timeSec)
        {
            return new ModeSetting(SetpointPa, LowLimitPa, HighLimitPa, timeSec);
        }

        /// <summary>설정값의 유효성을 검증한다.</summary>
        /// <param name="error">검증 실패 사유. 성공 시 null.</param>
        /// <returns>유효하면 true.</returns>
        /// <remarks>
        /// 상하한이 뒤집히면 알람이 영구 발생하거나 영구 침묵한다.
        /// 어느 쪽이든 조용히 넘어가면 안 되므로 로드 단계에서 막는다.
        /// </remarks>
        public bool Validate(out string error)
        {
            if (string.IsNullOrEmpty(DeviceId))
            {
                error = "센서 설정의 deviceId 는 필수입니다.";
                return false;
            }

            if (IsNotFinite(SetpointPa) || IsNotFinite(LowLimitPa) || IsNotFinite(HighLimitPa))
            {
                error = Format("센서 {0}: 설정값에 유효하지 않은 수치가 있습니다.", DeviceId);
                return false;
            }

            if (LowLimitPa >= HighLimitPa)
            {
                error = Format(
                    "센서 {0}: 하한({1:F1} Pa)이 상한({2:F1} Pa) 이상입니다. 대역이 성립하지 않습니다.",
                    DeviceId, LowLimitPa, HighLimitPa);

                return false;
            }

            // 설정값이 대역 밖이면 제어가 시작부터 이탈 상태로 판정된다.
            if (SetpointPa <= LowLimitPa || SetpointPa >= HighLimitPa)
            {
                error = Format(
                    "센서 {0}: 설정값({1:F1} Pa)이 대역({2:F1} ~ {3:F1} Pa) 밖입니다.",
                    DeviceId, SetpointPa, LowLimitPa, HighLimitPa);

                return false;
            }

            error = null;
            return true;
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return Format(
                "{0}: Set={1:F1} Pa ({2:F1} ~ {3:F1})",
                DeviceId, SetpointPa, LowLimitPa, HighLimitPa);
        }

        /// <summary>유한한 실수가 아닌지 판정한다.</summary>
        /// <param name="value">검사할 값.</param>
        /// <returns>NaN 또는 무한이면 true.</returns>
        private static bool IsNotFinite(double value)
        {
            return double.IsNaN(value) || double.IsInfinity(value);
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
}
