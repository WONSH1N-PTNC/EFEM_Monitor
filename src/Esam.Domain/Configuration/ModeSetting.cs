using System;

namespace Esam.Domain.Configuration
{
    /// <summary>
    /// 센서 모드 1개의 제어 파라미터. control.json 의 <c>modes.SensorN</c> 에 대응한다.
    /// ESAM 운용방법 설명자료 p.6 Config 화면의 「Set / ± 범위 / Time」 3개 열과 1:1 대응한다.
    /// </summary>
    public sealed class ModeSetting
    {
        /// <summary>목표 압력 [Pa]. (Sensor1=6, Sensor2=-10, Sensor3=-200 이 문서상 기본값)</summary>
        public double SetpointPa { get; set; }

        /// <summary>정상 대역 폭 [Pa]. 대역은 Setpoint ± Band 이다.</summary>
        public double BandPa { get; set; }

        /// <summary>
        /// 대역 이탈이 이 시간 이상 지속되면 에러/알람으로 확정한다 [초].
        /// (문서상 Sensor1=60, Sensor2=120, Sensor3=300. 의미는 DESIGN.md Open Issue #3 협의 중)
        /// </summary>
        public double TimeSec { get; set; }

        /// <summary>정상 대역 하한 [Pa].</summary>
        public double LowLimitPa
        {
            get { return SetpointPa - BandPa; }
        }

        /// <summary>정상 대역 상한 [Pa].</summary>
        public double HighLimitPa
        {
            get { return SetpointPa + BandPa; }
        }

        /// <summary>대역 이탈 확정 시간 [ms].</summary>
        public double TimeMs
        {
            get { return TimeSec * 1000.0; }
        }

        /// <summary>기본값으로 초기화한다.</summary>
        public ModeSetting()
        {
            SetpointPa = 0.0;
            BandPa = 0.0;
            TimeSec = 0.0;
        }

        /// <summary>파라미터를 지정해 초기화한다.</summary>
        /// <param name="setpointPa">목표 압력 [Pa].</param>
        /// <param name="bandPa">대역 폭 [Pa].</param>
        /// <param name="timeSec">이탈 확정 시간 [초].</param>
        public ModeSetting(double setpointPa, double bandPa, double timeSec)
        {
            SetpointPa = setpointPa;
            BandPa = bandPa;
            TimeSec = timeSec;
        }

        /// <summary>주어진 측정값이 정상 대역 안에 있는지 판정한다.</summary>
        /// <param name="valuePa">측정값 [Pa].</param>
        /// <returns>대역 내부이면 true.</returns>
        public bool IsInBand(double valuePa)
        {
            // 순서도가 "압력하한 &lt; 센서 &lt; 압력상한" 이므로 경계값은 대역 밖으로 취급하지 않고
            // 경계 초과부터 이탈로 본다. 부동소수 비교이므로 경계 진동을 막기 위해 개구간으로 둔다.
            return valuePa > LowLimitPa && valuePa < HighLimitPa;
        }

        /// <summary>설정값의 유효성을 검증한다.</summary>
        /// <param name="error">검증 실패 사유. 성공 시 null.</param>
        /// <returns>유효하면 true.</returns>
        public bool Validate(out string error)
        {
            if (BandPa <= 0.0)
            {
                error = "대역 폭(BandPa)은 0보다 커야 합니다.";
                return false;
            }

            if (TimeSec < 0.0)
            {
                error = "이탈 확정 시간(TimeSec)은 음수일 수 없습니다.";
                return false;
            }

            if (double.IsNaN(SetpointPa) || double.IsInfinity(SetpointPa))
            {
                error = "목표값(SetpointPa)이 유효한 수치가 아닙니다.";
                return false;
            }

            error = null;
            return true;
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "Set={0} Pa, Band=±{1} Pa ({2} ~ {3}), Time={4} s",
                SetpointPa, BandPa, LowLimitPa, HighLimitPa, TimeSec);
        }
    }
}
