using System;
using System.Collections.Generic;
using Esam.Domain.Models;

namespace Esam.Persistence
{
    /// <summary>
    /// 한 시점의 트렌드 1행. <c>trend</c> 테이블의 열 구성과 1:1 대응한다.
    /// </summary>
    /// <remarks>
    /// <para><b>왜 스냅샷을 그대로 넘기지 않는가.</b> <see cref="SystemSnapshot"/> 은
    /// 사전(dictionary)으로 되어 있어 "어느 센서가 어느 열인가" 가 정해져 있지 않다.
    /// 그 대응은 <b>스키마의 지식</b>이므로 저장 계층이 갖는다.</para>
    /// <para>값이 없는 항목은 <c>null</c> 로 둔다. 0 으로 채우면 <b>측정하지 않은 것과
    /// 0 Pa 를 구분할 수 없다.</b> 풍속 0 m/s 는 유효한 측정값이다.</para>
    /// <para>품질이 <see cref="Quality.Good"/> 이 아닌 값도 <c>null</c> 이다.
    /// 통신이 끊긴 센서의 낡은 값을 기록하면, 나중에 트렌드를 보는 사람은
    /// 그 구간을 정상 운전으로 읽는다.</para>
    /// </remarks>
    public sealed class TrendRow
    {
        /// <summary>차압센서 열 순서. 스키마의 <c>s11 … s35</c> 에 대응한다.</summary>
        /// <remarks>
        /// 이 배열이 <b>열 이름과 디바이스 ID 의 계약</b>이다. 순서를 바꾸면
        /// 과거 DB 의 열 의미가 달라지므로, 바꿀 때는 스키마 버전을 올려야 한다.
        /// </remarks>
        public static readonly string[] SensorIds =
        {
            "S1-1", "S1-2", "S1-3",
            "S2-1", "S2-2", "S2-3", "S2-4", "S2-5",
            "S3-1", "S3-2", "S3-3", "S3-4", "S3-5"
        };

        /// <summary>스로틀밸브 열 순서. <c>v1_pct … v5_pct</c>.</summary>
        public static readonly string[] ValveIds = { "V-1", "V-2", "V-3", "V-4", "V-5" };

        /// <summary>송풍팬 열 순서. <c>f1_rpm … f5_rpm</c>.</summary>
        public static readonly string[] FanIds = { "F-1", "F-2", "F-3", "F-4", "F-5" };

        /// <summary>수집 시각(Unix 밀리초, UTC).</summary>
        public long TimestampMs { get; set; }

        /// <summary>차압 [Pa]. 길이 13.</summary>
        public double?[] Pressures { get; private set; }

        /// <summary>밸브 개도 [%]. 길이 5.</summary>
        public double?[] ValvePercents { get; private set; }

        /// <summary>팬 회전수 [RPM]. 길이 5.</summary>
        public double?[] FanRpms { get; private set; }

        /// <summary>FFU 회전수 [RPM].</summary>
        public double? FfuRpm { get; set; }

        /// <summary>MFC 1~2 유량. 길이 2.</summary>
        public double?[] MfcFlows { get; private set; }

        /// <summary>풍속 1~3 [m/s]. 길이 3.</summary>
        public double?[] AirVelocities { get; private set; }

        /// <summary>EFEM 온도 [℃].</summary>
        public double? TemperatureEfem { get; set; }

        /// <summary>EFEM 습도 [%RH].</summary>
        public double? HumidityEfem { get; set; }

        /// <summary>파티클 농도.</summary>
        public double? Particle { get; set; }

        /// <summary>컨트롤박스 온도 [℃].</summary>
        public double? TemperatureControlBox { get; set; }

        /// <summary>센서 모드(<see cref="Domain.Control.SensorMode"/> 의 정수값).</summary>
        public int ControlMode { get; set; }

        /// <summary>운전 단계(<see cref="Domain.Control.SystemPhase"/> 의 정수값).</summary>
        public int ControlPhase { get; set; }

        /// <summary>이 시점의 활성 알람 코드. 조회 시 이벤트 마커로 쓴다.</summary>
        /// <remarks>
        /// 스키마의 <c>alarm_bits</c> 는 비트맵을 의도했지만, 코드 체계가
        /// AL-01~66 + DG-01~08 로 <b>고정되어 있지 않다</b>(알람 설정 화면에서
        /// 활성 여부가 바뀐다). 비트 위치를 코드에 박으면 규칙이 두 곳에 생긴다.
        /// 쉼표로 이은 문자열로 두고, 조회 쪽에서 나눈다.
        /// </remarks>
        public string ActiveAlarmCodes { get; set; }

        /// <summary>빈 행을 만든다.</summary>
        public TrendRow()
        {
            Pressures = new double?[SensorIds.Length];
            ValvePercents = new double?[ValveIds.Length];
            FanRpms = new double?[FanIds.Length];
            MfcFlows = new double?[2];
            AirVelocities = new double?[3];
        }

        /// <summary>스냅샷을 트렌드 1행으로 옮긴다.</summary>
        /// <param name="snapshot">스냅샷.</param>
        /// <returns>트렌드 행.</returns>
        /// <exception cref="ArgumentNullException">스냅샷이 null 일 때.</exception>
        public static TrendRow FromSnapshot(SystemSnapshot snapshot)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException("snapshot");
            }

            TrendRow row = new TrendRow();
            row.TimestampMs = ToUnixMs(snapshot.TimestampUtc);

            for (int i = 0; i < SensorIds.Length; i++)
            {
                PressureReading reading = snapshot.FindPressure(SensorIds[i]);

                row.Pressures[i] = reading != null && reading.Quality == Quality.Good
                    ? (double?)reading.Pa
                    : null;
            }

            for (int i = 0; i < ValveIds.Length; i++)
            {
                ValveState valve = snapshot.FindValve(ValveIds[i]);

                row.ValvePercents[i] = valve != null && valve.Quality == Quality.Good
                    ? (double?)valve.PositionPercent
                    : null;
            }

            for (int i = 0; i < FanIds.Length; i++)
            {
                FanState fan = snapshot.FindFan(FanIds[i]);

                row.FanRpms[i] = fan != null && fan.Quality == Quality.Good
                    ? (double?)fan.Rpm
                    : null;
            }

            AuxiliaryReadings aux = snapshot.Auxiliary;

            if (aux != null)
            {
                row.FfuRpm = aux.FfuRpm;
                row.TemperatureEfem = aux.TemperatureEfem;
                row.HumidityEfem = aux.HumidityEfem;
                row.Particle = aux.Particle;
                row.TemperatureControlBox = aux.TemperatureControlBox;

                Copy(aux.MfcFlows, row.MfcFlows);
                Copy(aux.AirVelocities, row.AirVelocities);
            }

            if (snapshot.Control != null)
            {
                row.ControlMode = (int)snapshot.Control.Mode;
                row.ControlPhase = (int)snapshot.Control.Phase;
            }

            if (snapshot.Alarms != null && snapshot.Alarms.ActiveCodes.Count > 0)
            {
                row.ActiveAlarmCodes = string.Join(",", snapshot.Alarms.ActiveCodes);
            }

            return row;
        }

        /// <summary>UTC 시각을 Unix 밀리초로 바꾼다.</summary>
        /// <param name="utc">UTC 시각.</param>
        /// <returns>Unix 밀리초.</returns>
        /// <remarks>
        /// 밀리초로 두는 이유는 폴링 주기가 218 ms 이기 때문이다.
        /// 초 단위로 저장하면 같은 초에 들어온 여러 표본이 구분되지 않는다.
        /// </remarks>
        public static long ToUnixMs(DateTime utc)
        {
            return (long)(utc - Epoch).TotalMilliseconds;
        }

        /// <summary>Unix 밀리초를 UTC 시각으로 바꾼다.</summary>
        /// <param name="unixMs">Unix 밀리초.</param>
        /// <returns>UTC 시각.</returns>
        public static DateTime FromUnixMs(long unixMs)
        {
            return Epoch.AddMilliseconds(unixMs);
        }

        /// <summary>Unix 기준시각.</summary>
        private static readonly DateTime Epoch =
            new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        /// <summary>고정 길이 배열로 복사한다.</summary>
        /// <param name="source">원본.</param>
        /// <param name="target">대상.</param>
        private static void Copy(IReadOnlyList<double?> source, double?[] target)
        {
            if (source == null)
            {
                return;
            }

            int count = Math.Min(source.Count, target.Length);

            for (int i = 0; i < count; i++)
            {
                target[i] = source[i];
            }
        }
    }
}
