using System.Globalization;
using Esam.Communication.Configuration;

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
        // ── 드라이버 이름 ────────────────────────────────────────────────────────
        //
        // 실체는 Esam.Communication.Configuration.DriverNames 에 있다.
        // 설정 파일을 읽는 계층이 Communication 이므로 계약도 그쪽에 두는 것이 맞다.
        // 여기서는 기존 사용처가 깨지지 않도록 그대로 노출한다.

        /// <summary>차압센서·압력센서. <see cref="PressurePa"/> 를 제공한다.</summary>
        public const string DriverPressureSensor = DriverNames.PressureSensor;

        /// <summary>스로틀밸브.</summary>
        public const string DriverThrottleValve = DriverNames.ThrottleValve;

        /// <summary>송풍팬(Modbus 직결).</summary>
        public const string DriverModbusFan = DriverNames.ModbusFan;

        /// <summary>PLC 디지털 입력 및 온도.</summary>
        public const string DriverPlc = DriverNames.Plc;

        /// <summary>온습도 센서.</summary>
        public const string DriverTempHumidity = DriverNames.TempHumidity;

        /// <summary>풍속 센서.</summary>
        public const string DriverAirVelocity = DriverNames.AirVelocity;

        /// <summary>파티클 센서.</summary>
        public const string DriverParticle = DriverNames.Particle;

        /// <summary>MFC.</summary>
        public const string DriverMfc = DriverNames.Mfc;

        /// <summary>FFU.</summary>
        public const string DriverFfu = DriverNames.Ffu;

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

        /// <summary>
        /// 팬 회전수 설정값 되읽기 [RPM]. JKBLD300V2 는 0x4006(폐루프)이다.
        /// </summary>
        /// <remarks>
        /// <b>제어에 쓰지 않는다.</b> 제어기는 자기가 낸 지령을 적분 상태로 들고 있어야 하고,
        /// 되읽은 값은 드라이버가 지령을 거부·클램프했는지 대조하는 진단용이다.
        /// 이 값을 제어 경로에 넣으면 통신 지연만큼 적분이 뒤처진다.
        /// </remarks>
        public const string RpmSetpoint = "rpmSetpoint";

        // ── PLC 디지털 입력 ─────────────────────────────────────────────────────
        //
        // ESAM_IO List_260806.xlsx 기준. Modbus 로는 슬레이브 25 의 0x000A 한 워드에
        // 8점이 비트마스크로 들어온다. PLC DATA 표기 D10.n 이 곧 비트 n 이다.
        //
        //   bit0 D10.0  마스크   1   EMO1 비상정지
        //   bit1 D10.1  마스크   2   EC BLDC 냉각팬 정지
        //   bit2 D10.2  마스크   4   EL BLDC 냉각팬 정지
        //   bit3 D10.3  마스크   8   ER BLDC 냉각팬 정지
        //   bit4 D10.4  마스크  16   SL BLDC 냉각팬 정지
        //   bit5 D10.5  마스크  32   SR BLDC 냉각팬 정지
        //   bit6 D10.6  마스크  64   컨트롤박스 팬 T 정지
        //   bit7 D10.7  마스크 128   컨트롤박스 팬 B 정지
        //
        // 260801 판은 D10.1~8 로 적었으나 마스크 값은 같았다. 비트 인덱스는 변하지 않았다.

        /// <summary>송풍팬 정지 알람 키를 만든다(bit1 ~ bit5).</summary>
        /// <param name="index">송풍팬 번호 - 1 (0~4).</param>
        /// <returns><c>device-map.json</c> 과 같은 형식의 키.</returns>
        /// <remarks>
        /// <para><b>접두 상수를 노출하지 않고 메서드로 바꾼 이유가 D19 다.</b>
        /// 종전에는 호출부가 <c>"di.fanStop" + index</c> 로 조립했고, 설정 파일은
        /// <c>"di.fanStop.0"</c> 이었다. 점 하나 차이로 조회가 항상 실패해
        /// <b>송풍팬 정지·과열 알람 10건이 영원히 울리지 않았다.</b></para>
        /// <para>조립 규칙이 호출부에 있으면 규칙이 호출부 수만큼 생긴다.
        /// 여기 한 곳에 두어야 다음에 형식이 바뀔 때 한 번만 고친다.</para>
        /// </remarks>
        public static string DiFanStop(int index)
        {
            return Indexed("di.fanStop", index);
        }

        /// <summary>컨트롤박스 상부 팬 정지(bit6).</summary>
        public const string DiControlBoxFanTop = "di.controlBoxFanT";

        /// <summary>컨트롤박스 하부 팬 정지(bit7).</summary>
        public const string DiControlBoxFanBottom = "di.controlBoxFanB";

        /// <summary>비상정지(bit0).</summary>
        public const string DiEmo = "di.emo";

        /// <summary>
        /// 도어 열림. <b>배선된 입력이 없다.</b>
        /// </summary>
        /// <remarks>
        /// ESAM_IO List_260806.xlsx 의 DI 8점에 도어 접점이 없다. 인터록 IL-05 는
        /// 신호원이 없어 비활성 상태로 유지된다(DESIGN.md Open Issue #7).
        /// 키는 배선 추가 시 즉시 쓸 수 있도록 남겨 둔다.
        /// </remarks>
        public const string DiDoor = "di.door";

        /// <summary>
        /// 메인 차단기 OFF. <b>배선된 입력이 없다.</b>
        /// </summary>
        /// <remarks>
        /// 도어와 같은 이유로 IL-03 도 비활성이다. SPARE DI 2점이 남아 있으므로
        /// HW 팀이 배선하면 이 키에 매핑하면 된다.
        /// </remarks>
        public const string DiMainBreaker = "di.mainBreaker";

        // ── PLC 온도 ────────────────────────────────────────────────────────────

        /// <summary>BLDC 온도 키를 만든다(0x0064 ~ 0x0068, K형 열전대).</summary>
        /// <param name="index">송풍팬 번호 - 1 (0~4).</param>
        /// <returns><c>device-map.json</c> 과 같은 형식의 키.</returns>
        public static string TempFan(int index)
        {
            return Indexed("temp.fan", index);
        }

        /// <summary>BLDC 온도센서 단선 키를 만든다(0x006E ~ 0x0072).</summary>
        /// <param name="index">송풍팬 번호 - 1 (0~4).</param>
        /// <returns><c>device-map.json</c> 과 같은 형식의 키.</returns>
        /// <remarks>
        /// <b>아직 소비하는 코드가 없다.</b> 열전대가 끊기면 온도가 0 ℃ 로 읽히는데,
        /// 지금은 그것을 정상값과 구분하지 못한다. 별도 항목으로 남아 있다.
        /// </remarks>
        public static string TempFault(int index)
        {
            return Indexed("temp.fault", index);
        }

        /// <summary>
        /// 판넬(컨트롤박스) 온도. <b>현재 배선된 채널이 없다.</b>
        /// </summary>
        /// <remarks>
        /// TC Module 2 의 CH1~CH3 이 IO List 에서 비어 있다. 채널이 배정되면 매핑한다.
        /// </remarks>
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

        /// <summary>"접두.인덱스" 형식의 측정점 키를 만든다.</summary>
        /// <param name="prefix">접두(예: "di.fanStop").</param>
        /// <param name="index">0 이상의 인덱스.</param>
        /// <returns>조립된 키.</returns>
        /// <remarks>
        /// 인덱스는 <see cref="CultureInfo.InvariantCulture"/> 로 서식한다.
        /// 아라비아 숫자가 아닌 자릿수를 쓰는 지역 설정에서 키가 달라지면
        /// 조회가 조용히 실패한다.
        /// </remarks>
        private static string Indexed(string prefix, int index)
        {
            return prefix + "." + index.ToString(CultureInfo.InvariantCulture);
        }
    }
}
