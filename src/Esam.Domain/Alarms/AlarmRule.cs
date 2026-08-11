using System;
using System.Globalization;
using Esam.Domain.Control;

namespace Esam.Domain.Alarms
{
    /// <summary>
    /// 알람 규칙 정의. alarms.json 의 항목 1건에 대응한다.
    /// </summary>
    /// <remarks>
    /// 알람 31종(ESAM 자료 p.9)의 임계값과 조건을 코드에 하드코딩하지 않고
    /// 선언 데이터로 관리하기 위한 클래스이다. 임계값 변경이나 알람 추가에
    /// 재컴파일이 필요 없어야 한다는 요구사항(EFEM_Plan.md 확장성)을 만족한다.
    /// </remarks>
    public sealed class AlarmRule
    {
        /// <summary>알람 코드(예: "A05", "P01").</summary>
        public string Code { get; set; }

        /// <summary>표시명.</summary>
        public string Name { get; set; }

        /// <summary>심각도.</summary>
        public AlarmSeverity Severity { get; set; }

        /// <summary>
        /// 판정 대상 경로. 예) "device:S1-1.pressurePa", "device:PLC-1.di.emo", "deviceGroup:Valve".
        /// </summary>
        public string Source { get; set; }

        /// <summary>판정 조건 종류.</summary>
        public AlarmConditionType Condition { get; set; }

        /// <summary>임계값. <see cref="AlarmConditionType.GreaterThan"/> / <see cref="AlarmConditionType.LessThan"/> 에서 사용.</summary>
        public double Threshold { get; set; }

        /// <summary>
        /// <see cref="AlarmConditionType.OutOfBand"/> 판정 시 참조할 센서 모드.
        /// 해당 모드의 Setpoint ± Band 를 정상 범위로 사용한다.
        /// </summary>
        public SensorMode? ReferenceMode { get; set; }

        /// <summary>조건이 이 시간 이상 지속되어야 알람으로 확정한다 [ms]. 0 이면 즉시 확정.</summary>
        public double DebounceMs { get; set; }

        /// <summary>해제 정책.</summary>
        public AlarmResetPolicy ResetPolicy { get; set; }

        /// <summary>이 규칙을 사용할지 여부. 임계값 미확정 알람은 false 로 두고 시작한다.</summary>
        public bool Enabled { get; set; }

        /// <summary>
        /// 레시피와 무관한 자체 임계값을 쓴다고 명시한다.
        /// </summary>
        /// <remarks>
        /// <para>레시피가 임계값을 관리하는 센서를 대상으로 <c>threshold</c> 를 직접 쓰면
        /// 값이 두 곳에 생기므로 로더가 경고한다. 그런데 <b>의도적으로 그렇게 해야 하는
        /// 경우</b>가 있다.</para>
        /// <para>예: DG-04 는 배기 음압이 -100 Pa 를 넘어서면 경고한다. 이 값은 운전
        /// 설정과 무관한 고정 기준이다. 인터록(0 Pa) 도달 전에 알리는 것이 목적이므로
        /// 레시피의 하한을 따라 움직이면 안 된다.</para>
        /// <para>이 플래그가 서 있으면 경고하지 않는다. <b>의도를 데이터에 적어 두는 것</b>이
        /// 핵심이다. 항상 뜨는 경고는 읽히지 않고, 그러면 진짜 중복도 함께 묻힌다.</para>
        /// </remarks>
        public bool IndependentThreshold { get; set; }

        /// <summary>사용자에게 표시할 메시지(한국어).</summary>
        public string MessageKo { get; set; }

        /// <summary>기본값으로 초기화한다.</summary>
        public AlarmRule()
        {
            Severity = AlarmSeverity.Alarm;
            Condition = AlarmConditionType.GreaterThan;
            ResetPolicy = AlarmResetPolicy.Auto;
            Enabled = true;
            DebounceMs = 0.0;
        }

        /// <summary>규칙의 유효성을 검증한다.</summary>
        /// <param name="error">검증 실패 사유. 성공 시 null.</param>
        /// <returns>유효하면 true.</returns>
        public bool Validate(out string error)
        {
            if (string.IsNullOrEmpty(Code))
            {
                error = "알람 코드(Code)는 필수입니다.";
                return false;
            }

            if (string.IsNullOrEmpty(Source))
            {
                error = string.Format(CultureInfo.InvariantCulture, "알람 {0}: 판정 대상(Source)은 필수입니다.", Code);
                return false;
            }

            if (Condition == AlarmConditionType.OutOfBand && !ReferenceMode.HasValue)
            {
                error = string.Format(CultureInfo.InvariantCulture, "알람 {0}: OutOfBand 조건에는 ReferenceMode 가 필요합니다.", Code);
                return false;
            }

            // 레시피 조회 조건은 대상이 특정 디바이스여야 한다.
            // deviceGroup:·plc:·aux: 는 어느 센서의 한계를 볼지 정할 수 없다.
            if (Condition == AlarmConditionType.AboveHighLimit
                || Condition == AlarmConditionType.BelowLowLimit)
            {
                string deviceId;

                if (!SnapshotValueResolver.TryGetDeviceId(Source, out deviceId))
                {
                    error = string.Format(
                        CultureInfo.InvariantCulture,
                        "알람 {0}: {1} 조건의 대상은 device:{{id}}.{{member}} 형식이어야 합니다(현재 '{2}').",
                        Code, Condition, Source);

                    return false;
                }
            }

            if (DebounceMs < 0.0)
            {
                error = string.Format(CultureInfo.InvariantCulture, "알람 {0}: DebounceMs 는 음수일 수 없습니다.", Code);
                return false;
            }

            error = null;
            return true;
        }
    }

    /// <summary>
    /// 활성 알람 1건의 런타임 상태. 디바운스 누적과 발생/해제 시각을 관리한다.
    /// </summary>
    public sealed class AlarmState
    {
        private DateTime _conditionSinceUtc;

        /// <summary>대응하는 규칙.</summary>
        public AlarmRule Rule { get; private set; }

        /// <summary>알람이 확정 발생한 상태인지 여부.</summary>
        public bool IsActive { get; private set; }

        /// <summary>사용자가 확인(Ack)했는지 여부.</summary>
        public bool IsAcknowledged { get; private set; }

        /// <summary>알람이 확정 발생한 시각(UTC). 미발생이면 <see cref="DateTime.MinValue"/>.</summary>
        public DateTime RaisedUtc { get; private set; }

        /// <summary>알람 발생 시점의 측정값.</summary>
        public double TriggerValue { get; private set; }

        /// <summary>발생 사유 설명.</summary>
        public string Detail { get; private set; }

        /// <summary>조건이 성립한 채로 지속된 시간 [ms].</summary>
        public double ConditionElapsedMs { get; private set; }

        /// <summary>알람 런타임 상태를 생성한다.</summary>
        /// <param name="rule">대응 규칙.</param>
        /// <exception cref="ArgumentNullException">규칙이 null 일 때.</exception>
        public AlarmState(AlarmRule rule)
        {
            if (rule == null)
            {
                throw new ArgumentNullException("rule");
            }

            Rule = rule;
            _conditionSinceUtc = DateTime.MinValue;
            RaisedUtc = DateTime.MinValue;
        }

        /// <summary>
        /// 조건 성립 여부를 반영해 상태를 갱신한다.
        /// </summary>
        /// <param name="conditionMet">이번 스캔에서 조건이 성립했는지.</param>
        /// <param name="value">판정에 사용한 값.</param>
        /// <param name="detail">사유 설명.</param>
        /// <param name="nowUtc">현재 시각(UTC).</param>
        /// <returns>이번 갱신으로 새로 발생(false→true)했으면 true.</returns>
        public bool Update(bool conditionMet, double value, string detail, DateTime nowUtc)
        {
            if (!conditionMet)
            {
                _conditionSinceUtc = DateTime.MinValue;
                ConditionElapsedMs = 0.0;

                // Auto 정책은 조건 해소 즉시 해제한다.
                // Manual 정책은 사용자가 Reset 을 호출할 때까지 활성 상태를 유지한다.
                if (IsActive && Rule.ResetPolicy == AlarmResetPolicy.Auto)
                {
                    IsActive = false;
                    IsAcknowledged = false;
                    RaisedUtc = DateTime.MinValue;
                }

                return false;
            }

            if (_conditionSinceUtc == DateTime.MinValue)
            {
                _conditionSinceUtc = nowUtc;
                ConditionElapsedMs = 0.0;
            }
            else
            {
                ConditionElapsedMs = (nowUtc - _conditionSinceUtc).TotalMilliseconds;
            }

            if (IsActive || ConditionElapsedMs < Rule.DebounceMs)
            {
                return false;
            }

            IsActive = true;
            IsAcknowledged = false;
            RaisedUtc = nowUtc;
            TriggerValue = value;
            Detail = detail;
            return true;
        }

        /// <summary>사용자가 알람을 확인(Ack)했음을 기록한다.</summary>
        public void Acknowledge()
        {
            IsAcknowledged = true;
        }

        /// <summary>사용자가 알람을 해제(Reset)했음을 기록한다. Manual 정책 알람에 사용한다.</summary>
        public void Reset()
        {
            IsActive = false;
            IsAcknowledged = false;
            RaisedUtc = DateTime.MinValue;
            _conditionSinceUtc = DateTime.MinValue;
            ConditionElapsedMs = 0.0;
        }
    }
}
