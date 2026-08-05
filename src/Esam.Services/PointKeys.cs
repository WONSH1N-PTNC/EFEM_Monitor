namespace Esam.Services
{
    /// <summary>
    /// 측정점 키와 드라이버 이름의 표준 규약.
    /// </summary>
    /// <remarks>
    /// <para>스냅샷 조립은 device-map.json 의 <c>driver</c> 값과 측정점 <c>key</c> 로
    /// 어떤 타입의 상태를 만들지 결정한다. 즉 <b>이 문자열들이 설정과 코드의 계약</b>이다.</para>
    /// <para>문자열을 코드 곳곳에 흩뿌리면 설정 파일의 키를 하나 바꿨을 때
    /// 어디가 깨지는지 알 수 없다. 한곳에 모아 상수로 두면 컴파일러가 사용처를 찾아 준다.</para>
    /// </remarks>
    public static class PointKeys
    {
        // ── 드라이버 이름 (device-map.json 의 deviceTypes[*].driver) ──────────────

        /// <summary>차압센서. <see cref="PressurePa"/> 를 제공한다.</summary>
        public const string DriverPressureSensor = "PressureSensor";

        /// <summary>스로틀밸브.</summary>
        public const string DriverThrottleValve = "ThrottleValve";

        /// <summary>송풍팬(Modbus 직결).</summary>
        public const string DriverModbusFan = "ModbusFan";

        /// <summary>PLC 디지털 입력 및 온도.</summary>
        public const string DriverPlc = "Plc";

        /// <summary>온습도 센서.</summary>
        public const string DriverTempHumidity = "TempHumidity";

        /// <summary>풍속 센서.</summary>
        public const string DriverAirVelocity = "AirVelocity";

        /// <summary>파티클 센서.</summary>
        public const string DriverParticle = "Particle";

        /// <summary>MFC.</summary>
        public const string DriverMfc = "Mfc";

        /// <summary>FFU.</summary>
        public const string DriverFfu = "Ffu";

        // ── 차압센서 ────────────────────────────────────────────────────────────

        /// <summary>차압 [Pa]. 제어의 기준값이다.</summary>
        public const string PressurePa = "pressurePa";

        /// <summary>센서 자체 상태 코드.</summary>
        public const string DeviceStatus = "deviceStatus";

        // ── 스로틀밸브 ──────────────────────────────────────────────────────────

        /// <summary>현재 위치 [pulse].</summary>
        public const string PositionPulse = "positionPulse";

        /// <summary>모션 상태 코드.</summary>
        public const string MotionStatus = "motionStatus";

        /// <summary>드라이브 알람 코드.</summary>
        public const string AlarmCode = "alarmCode";

        /// <summary>원점 복귀 완료 여부.</summary>
        public const string HomeDone = "homeDone";

        // ── 송풍팬 ──────────────────────────────────────────────────────────────

        /// <summary>현재 회전수 [RPM].</summary>
        public const string Rpm = "rpm";

        /// <summary>운전 상태 코드.</summary>
        public const string RunStatus = "runStatus";

        // ── PLC 디지털 입력 ─────────────────────────────────────────────────────

        /// <summary>송풍팬 정지 알람 접두. 뒤에 0~4 가 붙는다(D10.0 ~ D10.4).</summary>
        public const string DiFanStopPrefix = "di.fanStop";

        /// <summary>제어박스 냉각팬 알람(D10.5).</summary>
        public const string DiControlBoxFan = "di.ctrlBoxFan";

        /// <summary>비상정지(D10.6).</summary>
        public const string DiEmo = "di.emo";

        /// <summary>도어 열림(D10.7).</summary>
        public const string DiDoor = "di.door";

        /// <summary>메인 차단기 OFF(D10.8).</summary>
        public const string DiMainBreaker = "di.mainBreaker";

        // ── PLC 온도 ────────────────────────────────────────────────────────────

        /// <summary>송풍팬 온도 접두. 뒤에 0~4 가 붙는다(D100 ~ D104).</summary>
        public const string TempFanPrefix = "temp.fan";

        /// <summary>판넬(컨트롤박스) 온도(D105).</summary>
        public const string TempPanel = "temp.panel";

        // ── 보조 계측 ───────────────────────────────────────────────────────────

        /// <summary>온도 [℃].</summary>
        public const string Temperature = "temperature";

        /// <summary>습도 [%RH].</summary>
        public const string Humidity = "humidity";

        /// <summary>풍속 [m/s].</summary>
        public const string Velocity = "velocity";

        /// <summary>파티클 농도.</summary>
        public const string Particle = "particle";

        /// <summary>MFC 현재 유량.</summary>
        public const string Flow = "flow";

        /// <summary>MFC 설정 유량.</summary>
        public const string FlowSetpoint = "flowSetpoint";

        /// <summary>FFU 회전수 [RPM].</summary>
        public const string FfuRpm = "ffuRpm";
    }
}
