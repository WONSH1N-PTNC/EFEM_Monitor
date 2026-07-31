namespace Esam.Domain.Configuration
{
    /// <summary>
    /// 스로틀밸브 구동 파라미터. control.json 의 <c>actuator.valve</c> 에 대응한다.
    /// Phase 5 현장 튜닝의 주 대상이므로 전 항목을 실시간 변경 가능하게 설계한다.
    /// </summary>
    public sealed class ValveActuatorConfig
    {
        /// <summary>1회 조정량 [pulse]. 값이 클수록 응답이 빠르지만 오버슈트 위험이 커진다.</summary>
        public int StepPulse { get; set; }

        /// <summary>최소 허용 위치 [pulse]. 0 = 완전 닫힘.</summary>
        public int MinPulse { get; set; }

        /// <summary>최대 허용 위치 [pulse]. 5000 = 90도 완전 열림.</summary>
        public int MaxPulse { get; set; }

        /// <summary>조정 후 다음 조정까지 대기할 안정화 시간 [ms]. 헌팅 방지용.</summary>
        public int DwellMs { get; set; }

        /// <summary>PR0 이동 속도 [RPM] (1~5). 레지스터 0x6203 에 기록된다.</summary>
        public int VelocityRpm { get; set; }

        /// <summary>목표 위치 도달 판정 허용오차 [pulse].</summary>
        public int PositionTolerancePulse { get; set; }

        /// <summary>이동 완료 타임아웃 [ms]. 초과 시 알람 A10.</summary>
        public int MoveTimeoutMs { get; set; }

        /// <summary>원점 복귀 타임아웃 [ms].</summary>
        public int HomingTimeoutMs { get; set; }

        /// <summary>통신자료 기준 기본값으로 초기화한다.</summary>
        public ValveActuatorConfig()
        {
            StepPulse = 100;
            MinPulse = 0;
            MaxPulse = 5000;
            DwellMs = 1000;
            VelocityRpm = 3;
            PositionTolerancePulse = 20;
            MoveTimeoutMs = 10000;
            HomingTimeoutMs = 30000;
        }

        /// <summary>설정값의 유효성을 검증한다.</summary>
        /// <param name="error">검증 실패 사유. 성공 시 null.</param>
        /// <returns>유효하면 true.</returns>
        public bool Validate(out string error)
        {
            if (StepPulse <= 0)
            {
                error = "밸브 조정량(StepPulse)은 0보다 커야 합니다.";
                return false;
            }

            if (MaxPulse <= MinPulse)
            {
                error = "밸브 MaxPulse 는 MinPulse 보다 커야 합니다.";
                return false;
            }

            if (VelocityRpm < 1 || VelocityRpm > 5)
            {
                error = "밸브 속도(VelocityRpm)는 통신자료 기준 1~5 범위여야 합니다.";
                return false;
            }

            if (DwellMs < 0 || MoveTimeoutMs <= 0 || HomingTimeoutMs <= 0)
            {
                error = "밸브 시간 파라미터(Dwell/Timeout)가 유효하지 않습니다.";
                return false;
            }

            error = null;
            return true;
        }
    }

    /// <summary>
    /// 송풍팬 구동 파라미터. control.json 의 <c>actuator.fan</c> 에 대응한다.
    /// </summary>
    /// <remarks>
    /// <see cref="MaxRpm"/> 은 팬 사양 미확보 상태이며(DESIGN.md Open Issue #20),
    /// 0 인 상태로는 증속 제어가 불가능하므로 자동 제어 진입을 차단한다.
    /// </remarks>
    public sealed class FanActuatorConfig
    {
        /// <summary>1회 증속/감속량 [RPM].</summary>
        public double StepRpm { get; set; }

        /// <summary>최소 운전 회전수 [RPM].</summary>
        public double MinRpm { get; set; }

        /// <summary>최대 운전 회전수 [RPM]. 팬 사양 확정 전에는 0 이며 자동 제어가 차단된다.</summary>
        public double MaxRpm { get; set; }

        /// <summary>조정 후 다음 조정까지 대기할 안정화 시간 [ms].</summary>
        public int DwellMs { get; set; }

        /// <summary>이 값 미만으로 감속되면 정지로 간주하고 정지 지령을 보낸다 [RPM].</summary>
        public double OffBelowRpm { get; set; }

        /// <summary>목표 회전수 도달 판정 허용오차 [RPM].</summary>
        public double RpmTolerance { get; set; }

        /// <summary>가감속 완료 타임아웃 [ms]. 초과 시 알람 A11.</summary>
        public int RampTimeoutMs { get; set; }

        /// <summary>기본값으로 초기화한다.</summary>
        public FanActuatorConfig()
        {
            StepRpm = 100.0;
            MinRpm = 0.0;
            MaxRpm = 0.0;
            DwellMs = 1000;
            OffBelowRpm = 100.0;
            RpmTolerance = 50.0;
            RampTimeoutMs = 15000;
        }

        /// <summary>자동 제어에 사용할 수 있을 만큼 사양이 확정되었는지 여부.</summary>
        public bool IsUsableForAutoControl
        {
            get { return MaxRpm > 0.0 && MaxRpm > MinRpm && StepRpm > 0.0; }
        }

        /// <summary>설정값의 유효성을 검증한다.</summary>
        /// <param name="error">검증 실패 사유. 성공 시 null.</param>
        /// <returns>유효하면 true.</returns>
        public bool Validate(out string error)
        {
            if (StepRpm <= 0.0)
            {
                error = "팬 조정량(StepRpm)은 0보다 커야 합니다.";
                return false;
            }

            if (MinRpm < 0.0)
            {
                error = "팬 MinRpm 은 음수일 수 없습니다.";
                return false;
            }

            if (MaxRpm < 0.0)
            {
                error = "팬 MaxRpm 은 음수일 수 없습니다.";
                return false;
            }

            if (MaxRpm > 0.0 && MaxRpm <= MinRpm)
            {
                error = "팬 MaxRpm 은 MinRpm 보다 커야 합니다.";
                return false;
            }

            if (DwellMs < 0 || RampTimeoutMs <= 0)
            {
                error = "팬 시간 파라미터(Dwell/Timeout)가 유효하지 않습니다.";
                return false;
            }

            error = null;
            return true;
        }
    }
}
