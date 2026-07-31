namespace Esam.Communication.Simulation
{
    /// <summary>
    /// 가상 플랜트 모델의 물리 파라미터.
    /// </summary>
    /// <remarks>
    /// <para><b>이 값들은 실측 데이터가 아니다.</b> 기본값은 ESAM 문서의 목표 운전점
    /// (센서1 = 6 Pa, 센서2 = -10 Pa, 센서3 = -200 Pa)이 밸브 개도 50%, 팬 33% 부근에서
    /// 나오도록 역산한 것이다. Phase 5 현장 시운전에서 실제 스텝 응답을 측정한 뒤
    /// 이 값을 갱신하면, 이후 제어 파라미터 변경을 현장에 가지 않고 검증할 수 있다.</para>
    /// <para>부호 규약: 밸브를 열거나 팬을 증속하면 배기량이 늘어 <b>압력이 낮아진다</b>.
    /// ESAM 순서도의 "압력상한 초과 → 밸브 위치 증가"와 일치한다.</para>
    /// </remarks>
    public sealed class PlantOptions
    {
        /// <summary>밸브 완전 닫힘·팬 정지 상태의 센서 1 압력 [Pa].</summary>
        public double Sensor1BasePa { get; set; }

        /// <summary>센서 1 에 대한 밸브 개도(0~1)의 영향 계수 [Pa].</summary>
        public double Sensor1ValveGain { get; set; }

        /// <summary>센서 1 에 대한 팬 회전수(0~1)의 영향 계수 [Pa].</summary>
        public double Sensor1FanGain { get; set; }

        /// <summary>센서 1 응답 시정수 [초]. EFEM 내부 부피가 커서 가장 느리다.</summary>
        public double Sensor1TauSec { get; set; }

        /// <summary>센서 1 측정 노이즈 표준편차 [Pa].</summary>
        public double Sensor1NoiseSigmaPa { get; set; }

        /// <summary>밸브 완전 닫힘·팬 정지 상태의 센서 2 압력 [Pa].</summary>
        public double Sensor2BasePa { get; set; }

        /// <summary>센서 2 에 대한 밸브 개도의 영향 계수 [Pa].</summary>
        public double Sensor2ValveGain { get; set; }

        /// <summary>센서 2 에 대한 팬 회전수의 영향 계수 [Pa].</summary>
        public double Sensor2FanGain { get; set; }

        /// <summary>센서 2 응답 시정수 [초].</summary>
        public double Sensor2TauSec { get; set; }

        /// <summary>센서 2 측정 노이즈 표준편차 [Pa].</summary>
        public double Sensor2NoiseSigmaPa { get; set; }

        /// <summary>밸브 완전 닫힘·팬 정지 상태의 센서 3 압력 [Pa].</summary>
        public double Sensor3BasePa { get; set; }

        /// <summary>센서 3 에 대한 밸브 개도의 영향 계수 [Pa].</summary>
        public double Sensor3ValveGain { get; set; }

        /// <summary>센서 3 에 대한 팬 회전수의 영향 계수 [Pa].</summary>
        public double Sensor3FanGain { get; set; }

        /// <summary>센서 3 응답 시정수 [초]. 배기 후단이라 가장 빠르다.</summary>
        public double Sensor3TauSec { get; set; }

        /// <summary>센서 3 측정 노이즈 표준편차 [Pa].</summary>
        public double Sensor3NoiseSigmaPa { get; set; }

        /// <summary>
        /// 밸브 이동 속도 [pulse/초]. 실제 값은 밸브 명세 확정 후 갱신해야 한다
        /// (DESIGN.md Open Issue #5). 기본값 1000 은 전 구간(5000 pulse) 5초를 뜻한다.
        /// </summary>
        public double ValveSlewPulsePerSec { get; set; }

        /// <summary>팬 가감속 속도 [RPM/초]. 팬 명세 확정 후 갱신 대상(Open Issue #20).</summary>
        public double FanRampRpmPerSec { get; set; }

        /// <summary>밸브 완전 열림 위치 [pulse]. 통신자료 기준 5000(=90도).</summary>
        public int ValveFullOpenPulse { get; set; }

        /// <summary>팬 최대 회전수 [RPM]. 시뮬레이션 기준값.</summary>
        public double FanMaxRpm { get; set; }

        /// <summary>ESAM 목표 운전점 기준 기본값으로 초기화한다.</summary>
        public PlantOptions()
        {
            // 센서 1: 20 - 15*0.5 - 20*0.333 ≈ 5.8 Pa (목표 6 Pa)
            Sensor1BasePa = 20.0;
            Sensor1ValveGain = 15.0;
            Sensor1FanGain = 20.0;
            Sensor1TauSec = 3.0;
            Sensor1NoiseSigmaPa = 0.2;

            // 센서 2: 20 - 40*0.5 - 30*0.333 = -10 Pa (목표 -10 Pa)
            Sensor2BasePa = 20.0;
            Sensor2ValveGain = 40.0;
            Sensor2FanGain = 30.0;
            Sensor2TauSec = 1.5;
            Sensor2NoiseSigmaPa = 0.8;

            // 센서 3: -50 - 200*0.5 - 150*0.333 = -200 Pa (목표 -200 Pa)
            Sensor3BasePa = -50.0;
            Sensor3ValveGain = 200.0;
            Sensor3FanGain = 150.0;
            Sensor3TauSec = 1.0;
            Sensor3NoiseSigmaPa = 3.0;

            ValveSlewPulsePerSec = 1000.0;
            FanRampRpmPerSec = 500.0;
            ValveFullOpenPulse = 5000;
            FanMaxRpm = 3000.0;
        }

        /// <summary>노이즈를 모두 0 으로 만든 설정을 반환한다. 결정적 수렴 테스트용.</summary>
        /// <returns>노이즈 없는 설정 사본.</returns>
        public PlantOptions WithoutNoise()
        {
            PlantOptions copy = Clone();
            copy.Sensor1NoiseSigmaPa = 0.0;
            copy.Sensor2NoiseSigmaPa = 0.0;
            copy.Sensor3NoiseSigmaPa = 0.0;
            return copy;
        }

        /// <summary>설정 사본을 만든다.</summary>
        /// <returns>동일한 값을 갖는 새 인스턴스.</returns>
        public PlantOptions Clone()
        {
            return new PlantOptions
            {
                Sensor1BasePa = Sensor1BasePa,
                Sensor1ValveGain = Sensor1ValveGain,
                Sensor1FanGain = Sensor1FanGain,
                Sensor1TauSec = Sensor1TauSec,
                Sensor1NoiseSigmaPa = Sensor1NoiseSigmaPa,
                Sensor2BasePa = Sensor2BasePa,
                Sensor2ValveGain = Sensor2ValveGain,
                Sensor2FanGain = Sensor2FanGain,
                Sensor2TauSec = Sensor2TauSec,
                Sensor2NoiseSigmaPa = Sensor2NoiseSigmaPa,
                Sensor3BasePa = Sensor3BasePa,
                Sensor3ValveGain = Sensor3ValveGain,
                Sensor3FanGain = Sensor3FanGain,
                Sensor3TauSec = Sensor3TauSec,
                Sensor3NoiseSigmaPa = Sensor3NoiseSigmaPa,
                ValveSlewPulsePerSec = ValveSlewPulsePerSec,
                FanRampRpmPerSec = FanRampRpmPerSec,
                ValveFullOpenPulse = ValveFullOpenPulse,
                FanMaxRpm = FanMaxRpm
            };
        }
    }
}
