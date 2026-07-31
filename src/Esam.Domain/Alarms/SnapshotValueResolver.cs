using System;
using Esam.Domain.Models;

namespace Esam.Domain.Alarms
{
    /// <summary>
    /// 알람 규칙의 <see cref="AlarmRule.Source"/> 경로 문자열을 실제 측정값으로 변환하는 역할.
    /// </summary>
    /// <remarks>
    /// 알람 정의를 JSON 선언으로 관리하려면 "문자열 경로 → 값" 해석이 필요하다.
    /// 이 인터페이스로 분리해 두면 향후 FDC/외부 소스 값도 같은 방식으로 알람 판정에 넣을 수 있다.
    /// </remarks>
    public interface IAlarmValueResolver
    {
        /// <summary>경로에 해당하는 수치값을 조회한다.</summary>
        /// <param name="source">경로 문자열(예: "device:S1-1.pressurePa").</param>
        /// <param name="value">조회된 값.</param>
        /// <returns>조회에 성공하면 true.</returns>
        bool TryGetNumeric(string source, out double value);

        /// <summary>경로에 해당하는 논리값을 조회한다.</summary>
        /// <param name="source">경로 문자열(예: "device:PLC-1.di.emo").</param>
        /// <param name="value">조회된 값.</param>
        /// <returns>조회에 성공하면 true.</returns>
        bool TryGetBoolean(string source, out bool value);

        /// <summary>경로 대상의 통신이 실패 상태인지 조회한다.</summary>
        /// <param name="source">경로 문자열.</param>
        /// <param name="hasDeviceAlarm">장치 자체 알람코드가 0이 아니면 true.</param>
        /// <returns>통신 실패이면 true. 대상을 찾지 못한 경우도 true 로 본다(보수적 판정).</returns>
        bool IsCommFailed(string source, out bool hasDeviceAlarm);
    }

    /// <summary>
    /// <see cref="SystemSnapshot"/> 을 대상으로 하는 기본 값 해석기.
    /// </summary>
    /// <remarks>
    /// 지원 경로 형식:
    /// <list type="bullet">
    ///   <item><description><c>device:{id}.pressurePa</c> — 차압센서 압력</description></item>
    ///   <item><description><c>device:{id}.rpm</c> — 팬 회전수</description></item>
    ///   <item><description><c>device:{id}.positionPercent</c> — 밸브 개도율</description></item>
    ///   <item><description><c>aux:temperatureEfem | humidityEfem | particle | temperatureControlBox | ffuRpm</c></description></item>
    ///   <item><description><c>aux:airVelocity[0..2]</c> — 풍속 1~3</description></item>
    ///   <item><description><c>plc:di.emo | di.door | di.mainBreaker | di.ctrlBoxFan | di.fanStop[0..4]</c></description></item>
    ///   <item><description><c>deviceGroup:Valve | Fan | Pressure</c> — 그룹 통신/알람 판정</description></item>
    /// </list>
    /// </remarks>
    public sealed class SnapshotValueResolver : IAlarmValueResolver
    {
        private readonly SystemSnapshot _snapshot;

        /// <summary>해석기를 생성한다.</summary>
        /// <param name="snapshot">대상 스냅샷.</param>
        /// <exception cref="ArgumentNullException">스냅샷이 null 일 때.</exception>
        public SnapshotValueResolver(SystemSnapshot snapshot)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException("snapshot");
            }

            _snapshot = snapshot;
        }

        /// <inheritdoc />
        public bool TryGetNumeric(string source, out double value)
        {
            value = 0.0;

            string scheme;
            string path;
            if (!TrySplit(source, out scheme, out path))
            {
                return false;
            }

            if (string.Equals(scheme, "device", StringComparison.OrdinalIgnoreCase))
            {
                return TryGetDeviceNumeric(path, out value);
            }

            if (string.Equals(scheme, "aux", StringComparison.OrdinalIgnoreCase))
            {
                return TryGetAuxiliary(path, out value);
            }

            return false;
        }

        /// <inheritdoc />
        public bool TryGetBoolean(string source, out bool value)
        {
            value = false;

            string scheme;
            string path;
            if (!TrySplit(source, out scheme, out path))
            {
                return false;
            }

            if (!string.Equals(scheme, "plc", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            PlcDigitalState plc = _snapshot.Plc;

            if (Eq(path, "di.emo"))
            {
                value = plc.EmoActive;
                return true;
            }

            if (Eq(path, "di.door"))
            {
                value = plc.DoorOpen;
                return true;
            }

            if (Eq(path, "di.mainBreaker"))
            {
                value = plc.MainBreakerOff;
                return true;
            }

            if (Eq(path, "di.ctrlBoxFan"))
            {
                value = plc.ControlBoxFanAlarm;
                return true;
            }

            int index;
            if (TryParseIndexed(path, "di.fanStop", out index)
                && index >= 0 && index < plc.FanStopAlarms.Count)
            {
                value = plc.FanStopAlarms[index];
                return true;
            }

            return false;
        }

        /// <inheritdoc />
        public bool IsCommFailed(string source, out bool hasDeviceAlarm)
        {
            hasDeviceAlarm = false;

            string scheme;
            string path;
            if (!TrySplit(source, out scheme, out path))
            {
                // 경로 자체를 해석할 수 없으면 판정 불가 → 보수적으로 실패로 본다.
                return true;
            }

            if (string.Equals(scheme, "deviceGroup", StringComparison.OrdinalIgnoreCase))
            {
                return IsGroupFailed(path, out hasDeviceAlarm);
            }

            if (!string.Equals(scheme, "device", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            string deviceId = StripMember(path);

            ValveState valve = _snapshot.FindValve(deviceId);
            if (valve != null)
            {
                hasDeviceAlarm = valve.HasAlarm;
                return valve.Quality == Quality.Bad || valve.Quality == Quality.NoData;
            }

            FanState fan = _snapshot.FindFan(deviceId);
            if (fan != null)
            {
                hasDeviceAlarm = fan.HasAlarm;
                return fan.Quality == Quality.Bad || fan.Quality == Quality.NoData;
            }

            PressureReading pressure = _snapshot.FindPressure(deviceId);
            if (pressure != null)
            {
                return pressure.Quality == Quality.Bad || pressure.Quality == Quality.NoData;
            }

            return true;
        }

        /// <summary>디바이스 그룹 전체의 통신/알람 상태를 판정한다.</summary>
        /// <param name="group">그룹명("Valve", "Fan", "Pressure").</param>
        /// <param name="hasDeviceAlarm">그룹 내 장치 알람 존재 여부.</param>
        /// <returns>그룹 내 하나라도 통신 실패이면 true.</returns>
        private bool IsGroupFailed(string group, out bool hasDeviceAlarm)
        {
            hasDeviceAlarm = false;
            bool anyFailed = false;

            if (Eq(group, "Valve") || Eq(group, "ThrottleValve"))
            {
                foreach (ValveState valve in _snapshot.Valves.Values)
                {
                    if (valve.Quality == Quality.Bad || valve.Quality == Quality.NoData)
                    {
                        anyFailed = true;
                    }

                    if (valve.HasAlarm)
                    {
                        hasDeviceAlarm = true;
                    }
                }

                return anyFailed;
            }

            if (Eq(group, "Fan") || Eq(group, "BlowerFan"))
            {
                foreach (FanState fan in _snapshot.Fans.Values)
                {
                    if (fan.Quality == Quality.Bad || fan.Quality == Quality.NoData)
                    {
                        anyFailed = true;
                    }

                    if (fan.HasAlarm)
                    {
                        hasDeviceAlarm = true;
                    }
                }

                return anyFailed;
            }

            if (Eq(group, "Pressure") || Eq(group, "WTDM550"))
            {
                foreach (PressureReading reading in _snapshot.Pressures.Values)
                {
                    if (reading.Quality == Quality.Bad || reading.Quality == Quality.NoData)
                    {
                        anyFailed = true;
                    }
                }

                return anyFailed;
            }

            return true;
        }

        /// <summary>디바이스 경로의 수치값을 조회한다.</summary>
        /// <param name="path">"{id}.{member}" 형식 경로.</param>
        /// <param name="value">조회 결과.</param>
        /// <returns>성공하면 true.</returns>
        private bool TryGetDeviceNumeric(string path, out double value)
        {
            value = 0.0;

            int dot = path.LastIndexOf('.');
            if (dot <= 0 || dot >= path.Length - 1)
            {
                return false;
            }

            string id = path.Substring(0, dot);
            string member = path.Substring(dot + 1);

            PressureReading pressure = _snapshot.FindPressure(id);
            if (pressure != null && Eq(member, "pressurePa"))
            {
                if (pressure.Quality != Quality.Good)
                {
                    return false;
                }

                value = pressure.Pa;
                return true;
            }

            FanState fan = _snapshot.FindFan(id);
            if (fan != null && Eq(member, "rpm"))
            {
                if (fan.Quality != Quality.Good)
                {
                    return false;
                }

                value = fan.Rpm;
                return true;
            }

            ValveState valve = _snapshot.FindValve(id);
            if (valve != null && valve.Quality == Quality.Good)
            {
                if (Eq(member, "positionPercent"))
                {
                    value = valve.PositionPercent;
                    return true;
                }

                if (Eq(member, "positionPulse"))
                {
                    value = valve.PositionPulse;
                    return true;
                }
            }

            return false;
        }

        /// <summary>보조 계측값을 조회한다.</summary>
        /// <param name="path">항목명.</param>
        /// <param name="value">조회 결과.</param>
        /// <returns>값이 존재하면 true.</returns>
        private bool TryGetAuxiliary(string path, out double value)
        {
            value = 0.0;
            AuxiliaryReadings aux = _snapshot.Auxiliary;

            if (Eq(path, "temperatureEfem"))
            {
                return Unwrap(aux.TemperatureEfem, out value);
            }

            if (Eq(path, "humidityEfem"))
            {
                return Unwrap(aux.HumidityEfem, out value);
            }

            if (Eq(path, "particle"))
            {
                return Unwrap(aux.Particle, out value);
            }

            if (Eq(path, "temperatureControlBox"))
            {
                return Unwrap(aux.TemperatureControlBox, out value);
            }

            if (Eq(path, "ffuRpm"))
            {
                return Unwrap(aux.FfuRpm, out value);
            }

            int index;
            if (TryParseIndexed(path, "airVelocity", out index)
                && index >= 0 && index < aux.AirVelocities.Count)
            {
                return Unwrap(aux.AirVelocities[index], out value);
            }

            if (TryParseIndexed(path, "fanTemperature", out index)
                && index >= 0 && index < aux.FanTemperatures.Count)
            {
                return Unwrap(aux.FanTemperatures[index], out value);
            }

            if (TryParseIndexed(path, "mfcFlow", out index)
                && index >= 0 && index < aux.MfcFlows.Count)
            {
                return Unwrap(aux.MfcFlows[index], out value);
            }

            return false;
        }

        /// <summary>"scheme:path" 형식을 분리한다.</summary>
        /// <param name="source">원본 문자열.</param>
        /// <param name="scheme">스킴(출력).</param>
        /// <param name="path">경로(출력).</param>
        /// <returns>분리에 성공하면 true.</returns>
        private static bool TrySplit(string source, out string scheme, out string path)
        {
            scheme = null;
            path = null;

            if (string.IsNullOrEmpty(source))
            {
                return false;
            }

            int colon = source.IndexOf(':');
            if (colon <= 0 || colon >= source.Length - 1)
            {
                return false;
            }

            scheme = source.Substring(0, colon);
            path = source.Substring(colon + 1);
            return true;
        }

        /// <summary>"{id}.{member}" 에서 id 부분만 떼어낸다.</summary>
        /// <param name="path">경로.</param>
        /// <returns>디바이스 ID.</returns>
        private static string StripMember(string path)
        {
            int dot = path.LastIndexOf('.');
            return dot > 0 ? path.Substring(0, dot) : path;
        }

        /// <summary>"name[index]" 형식을 파싱한다.</summary>
        /// <param name="path">경로.</param>
        /// <param name="expectedName">기대하는 이름.</param>
        /// <param name="index">파싱된 인덱스(출력).</param>
        /// <returns>형식이 일치하면 true.</returns>
        private static bool TryParseIndexed(string path, string expectedName, out int index)
        {
            index = -1;

            int open = path.IndexOf('[');
            if (open <= 0 || !path.EndsWith("]", StringComparison.Ordinal))
            {
                return false;
            }

            string name = path.Substring(0, open);
            if (!Eq(name, expectedName))
            {
                return false;
            }

            string digits = path.Substring(open + 1, path.Length - open - 2);
            return int.TryParse(digits, out index);
        }

        /// <summary>nullable 값을 벗겨낸다.</summary>
        /// <param name="source">원본 nullable 값.</param>
        /// <param name="value">값(출력).</param>
        /// <returns>값이 있으면 true.</returns>
        private static bool Unwrap(double? source, out double value)
        {
            if (source.HasValue)
            {
                value = source.Value;
                return true;
            }

            value = 0.0;
            return false;
        }

        /// <summary>대소문자 무시 문자열 비교.</summary>
        /// <param name="a">문자열 1.</param>
        /// <param name="b">문자열 2.</param>
        /// <returns>같으면 true.</returns>
        private static bool Eq(string a, string b)
        {
            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }
    }
}
