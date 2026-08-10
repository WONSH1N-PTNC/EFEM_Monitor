using System;

namespace Esam.Domain.Configuration
{
    /// <summary>
    /// 제어 정책에 넘기는 파라미터. 목표값·대역·이탈 확정 시간을 담는다.
    /// </summary>
    /// <remarks>
    /// <para><b>이 타입은 파일에서 역직렬화되지 않는다(2026-08-10 변경).</b>
    /// <c>recipe.json</c> 의 센서별 설정값과 <c>control.json</c> 의 모드별 이탈 확정 시간을
    /// 합쳐 만드는 런타임 값 객체다. <see cref="SensorSetting.ToModeSetting"/> 이 만든다.</para>
    /// <para>대역을 두 방식으로 표현할 수 있다.</para>
    /// <list type="bullet">
    ///   <item><description><b>대칭</b> — <c>Setpoint ± Band</c>. 기존 구성과 테스트가 쓰는 방식</description></item>
    ///   <item><description><b>비대칭</b> — 상한·하한을 독립적으로 지정. recipe 가 쓰는 방식</description></item>
    /// </list>
    /// <para>배기 계통은 상한 여유와 하한 여유가 다를 수 있으므로 비대칭이 물리적으로 맞다.
    /// 대칭 생성자는 <c>Band</c> 로부터 상하한을 계산하므로 동작이 종전과 같다.</para>
    /// </remarks>
    public sealed class ModeSetting
    {
        /// <summary>상한을 명시적으로 지정한 경우의 값. null 이면 <c>Setpoint + Band</c> 로 계산한다.</summary>
        private double? _highLimitPa;

        /// <summary>하한을 명시적으로 지정한 경우의 값. null 이면 <c>Setpoint - Band</c> 로 계산한다.</summary>
        private double? _lowLimitPa;

        /// <summary>목표 압력 [Pa].</summary>
        public double SetpointPa { get; set; }

        /// <summary>
        /// 정상 대역 폭 [Pa]. 대칭 대역에서 <c>Setpoint ± Band</c> 로 쓴다.
        /// 비대칭으로 생성한 경우 넓은 쪽 편차를 반환한다.
        /// </summary>
        public double BandPa
        {
            get
            {
                if (!_lowLimitPa.HasValue && !_highLimitPa.HasValue)
                {
                    return _symmetricBandPa;
                }

                double upper = HighLimitPa - SetpointPa;
                double lower = SetpointPa - LowLimitPa;

                return upper > lower ? upper : lower;
            }

            set
            {
                // 대역 폭을 직접 설정하면 대칭 모드로 되돌린다.
                _symmetricBandPa = value;
                _lowLimitPa = null;
                _highLimitPa = null;
            }
        }

        /// <summary>대칭 대역 폭.</summary>
        private double _symmetricBandPa;

        /// <summary>
        /// 대역 이탈이 이 시간 이상 지속되면 에러/알람으로 확정한다 [초].
        /// (문서상 Sensor1=60, Sensor2=120, Sensor3=300. 의미는 DESIGN.md Open Issue #3 협의 중)
        /// </summary>
        public double TimeSec { get; set; }

        /// <summary>정상 대역 하한 [Pa].</summary>
        public double LowLimitPa
        {
            get { return _lowLimitPa ?? (SetpointPa - _symmetricBandPa); }
        }

        /// <summary>정상 대역 상한 [Pa].</summary>
        public double HighLimitPa
        {
            get { return _highLimitPa ?? (SetpointPa + _symmetricBandPa); }
        }

        /// <summary>상하한을 독립적으로 지정했는지 여부.</summary>
        public bool IsAsymmetric
        {
            get { return _lowLimitPa.HasValue || _highLimitPa.HasValue; }
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
            _symmetricBandPa = 0.0;
            TimeSec = 0.0;
        }

        /// <summary>대칭 대역으로 초기화한다.</summary>
        /// <param name="setpointPa">목표 압력 [Pa].</param>
        /// <param name="bandPa">대역 폭 [Pa]. 대역은 <c>Setpoint ± Band</c> 다.</param>
        /// <param name="timeSec">이탈 확정 시간 [초].</param>
        public ModeSetting(double setpointPa, double bandPa, double timeSec)
        {
            SetpointPa = setpointPa;
            _symmetricBandPa = bandPa;
            TimeSec = timeSec;
        }

        /// <summary>상한·하한을 독립적으로 지정해 초기화한다.</summary>
        /// <param name="setpointPa">목표 압력 [Pa].</param>
        /// <param name="lowLimitPa">대역 하한 [Pa].</param>
        /// <param name="highLimitPa">대역 상한 [Pa].</param>
        /// <param name="timeSec">이탈 확정 시간 [초].</param>
        /// <remarks>
        /// <c>recipe.json</c> 의 센서별 설정값이 이 경로로 들어온다.
        /// 인자 순서가 대칭 생성자와 다르므로(band 하나 대신 low·high 둘) 혼동하지 않는다.
        /// </remarks>
        public ModeSetting(double setpointPa, double lowLimitPa, double highLimitPa, double timeSec)
        {
            SetpointPa = setpointPa;
            _lowLimitPa = lowLimitPa;
            _highLimitPa = highLimitPa;
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
            if (LowLimitPa >= HighLimitPa)
            {
                error = IsAsymmetric
                    ? "대역 하한이 상한 이상입니다."
                    : "대역 폭(BandPa)은 0보다 커야 합니다.";

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
            return IsAsymmetric
                ? string.Format(
                    System.Globalization.CultureInfo.InvariantCulture,
                    "Set={0} Pa ({1} ~ {2}), Time={3} s",
                    SetpointPa, LowLimitPa, HighLimitPa, TimeSec)
                : string.Format(
                    System.Globalization.CultureInfo.InvariantCulture,
                    "Set={0} Pa, Band=±{1} Pa ({2} ~ {3}), Time={4} s",
                    SetpointPa, BandPa, LowLimitPa, HighLimitPa, TimeSec);
        }
    }
}
