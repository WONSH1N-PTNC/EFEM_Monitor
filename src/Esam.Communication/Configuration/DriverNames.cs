namespace Esam.Communication.Configuration
{
    /// <summary>
    /// <c>device-map.json</c> 의 <c>deviceTypes[*].driver</c> 값.
    /// </summary>
    /// <remarks>
    /// <para>이 문자열들은 <b>설정 파일과 코드의 계약</b>이다. 설정 파일을 읽는 계층이
    /// Communication 이므로 계약도 여기 있어야 한다.</para>
    /// <para>종전에는 <c>Esam.Services.PointKeys</c> 에만 있었다. 그래서 Communication 계층의
    /// 로더가 드라이버 이름을 알아야 할 때 문자열을 다시 적어야 했다.
    /// 같은 계약이 두 곳에 있으면 한쪽만 바뀌었을 때 컴파일러가 잡아주지 못한다.
    /// <c>PointKeys</c> 는 이제 이 상수를 그대로 노출한다.</para>
    /// </remarks>
    public static class DriverNames
    {
        /// <summary>차압센서·압력센서. 압력값을 제공한다.</summary>
        public const string PressureSensor = "PressureSensor";

        /// <summary>스로틀밸브.</summary>
        public const string ThrottleValve = "ThrottleValve";

        /// <summary>송풍팬(Modbus 직결).</summary>
        public const string ModbusFan = "ModbusFan";

        /// <summary>PLC 디지털 입력 및 온도.</summary>
        public const string Plc = "Plc";

        /// <summary>온습도 센서.</summary>
        public const string TempHumidity = "TempHumidity";

        /// <summary>풍속 센서.</summary>
        public const string AirVelocity = "AirVelocity";

        /// <summary>파티클 센서.</summary>
        public const string Particle = "Particle";

        /// <summary>MFC.</summary>
        public const string Mfc = "Mfc";

        /// <summary>FFU.</summary>
        public const string Ffu = "Ffu";
    }
}
