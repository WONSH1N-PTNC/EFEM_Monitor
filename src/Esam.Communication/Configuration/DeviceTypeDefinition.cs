using System;
using System.Collections.Generic;
using System.Globalization;

namespace Esam.Communication.Configuration
{
    /// <summary>
    /// 디바이스 종류 1개의 통신 명세. device-map.json 의 <c>deviceTypes</c> 항목에 대응한다.
    /// </summary>
    /// <remarks>
    /// 같은 품번의 장치가 여러 대 있을 때(차압센서 13대, 밸브 5대) 명세를 한 번만 기술하고
    /// 인스턴스는 슬레이브 주소만 지정하도록 하는 것이 목적이다.
    /// 레지스터 명세가 바뀌면 이 정의 1곳만 수정하면 된다.
    /// </remarks>
    public sealed class DeviceTypeDefinition
    {
        /// <summary>드라이버 종류 이름(예: "PressureSensor", "ThrottleValve", "ModbusFan", "Plc").</summary>
        public string Driver { get; set; }

        /// <summary>읽기 그룹 목록.</summary>
        public IList<ReadGroupDefinition> ReadGroups { get; set; }

        /// <summary>명령 목록. 키는 논리 명령명(예: "homing", "prMove", "setPosition").</summary>
        public IDictionary<string, CommandDefinition> Commands { get; set; }

        /// <summary>단위 변환 및 한계값. 밸브 pulse 환산, 팬 RPM 한계 등.</summary>
        public DeviceConversion Conversion { get; set; }

        /// <summary>기본값으로 초기화한다.</summary>
        public DeviceTypeDefinition()
        {
            ReadGroups = new List<ReadGroupDefinition>();
            Commands = new Dictionary<string, CommandDefinition>(StringComparer.OrdinalIgnoreCase);
            Conversion = new DeviceConversion();
        }

        /// <summary>지정한 논리 명령명의 정의를 찾는다.</summary>
        /// <param name="name">명령명.</param>
        /// <returns>명령 정의. 없으면 null.</returns>
        public CommandDefinition FindCommand(string name)
        {
            CommandDefinition command;
            if (Commands != null && !string.IsNullOrEmpty(name)
                && Commands.TryGetValue(name, out command))
            {
                return command;
            }

            return null;
        }

        /// <summary>정의의 유효성을 검증한다.</summary>
        /// <param name="typeName">디바이스 종류 이름(오류 메시지용).</param>
        /// <param name="errors">검증 실패 사유를 추가할 목록.</param>
        /// <returns>유효하면 true.</returns>
        public bool Validate(string typeName, IList<string> errors)
        {
            int before = errors.Count;

            if (string.IsNullOrEmpty(Driver))
            {
                errors.Add(string.Format(
                    CultureInfo.InvariantCulture, "{0}: driver 는 필수입니다.", typeName));
            }

            if (ReadGroups == null || ReadGroups.Count == 0)
            {
                // 쓰기 전용 장치는 없으므로 읽기 그룹이 하나도 없으면 설정 실수다.
                errors.Add(string.Format(
                    CultureInfo.InvariantCulture, "{0}: 읽기 그룹(readGroups)이 비어 있습니다.", typeName));
            }
            else
            {
                HashSet<string> seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                // 측정점 키는 그룹 안에서만이 아니라 디바이스 전체에서 유일해야 한다.
                // 두 그룹이 같은 키를 쓰면 이동평균 필터를 공유해 서로 다른 신호가 섞이고,
                // 상위로 올라가는 "디바이스ID.키" 경로도 충돌해 한쪽 값이 사라진다.
                HashSet<string> seenPointKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (ReadGroupDefinition group in ReadGroups)
                {
                    if (group == null)
                    {
                        errors.Add(string.Format(
                            CultureInfo.InvariantCulture, "{0}: 읽기 그룹에 null 항목이 있습니다.", typeName));
                        continue;
                    }

                    group.Validate(typeName, errors);

                    if (!string.IsNullOrEmpty(group.Name) && !seenNames.Add(group.Name))
                    {
                        errors.Add(string.Format(
                            CultureInfo.InvariantCulture,
                            "{0}: 읽기 그룹 이름 '{1}' 가 중복되었습니다.", typeName, group.Name));
                    }

                    if (group.Points == null)
                    {
                        continue;
                    }

                    foreach (PointDefinition point in group.Points)
                    {
                        if (point == null || string.IsNullOrEmpty(point.Key))
                        {
                            continue;
                        }

                        if (!seenPointKeys.Add(point.Key))
                        {
                            errors.Add(string.Format(
                                CultureInfo.InvariantCulture,
                                "{0}: 측정점 key '{1}' 가 여러 읽기 그룹에 중복되었습니다.",
                                typeName, point.Key));
                        }
                    }
                }
            }

            if (Commands != null)
            {
                foreach (KeyValuePair<string, CommandDefinition> pair in Commands)
                {
                    if (pair.Value == null)
                    {
                        errors.Add(string.Format(
                            CultureInfo.InvariantCulture,
                            "{0}: 명령 '{1}' 의 정의가 null 입니다.", typeName, pair.Key));
                        continue;
                    }

                    pair.Value.Validate(
                        string.Format(CultureInfo.InvariantCulture, "{0}.commands.{1}", typeName, pair.Key),
                        errors);
                }
            }

            return errors.Count == before;
        }
    }

    /// <summary>
    /// 디바이스 단위 변환 파라미터. device-map.json 의 <c>conversion</c> 에 대응한다.
    /// </summary>
    public sealed class DeviceConversion
    {
        /// <summary>밸브 완전 열림 위치 [pulse]. 통신자료 기준 5000.</summary>
        public int PulsePerFullOpen { get; set; }

        /// <summary>밸브 완전 열림 각도 [도]. 통신자료 기준 90.</summary>
        public double FullOpenDegree { get; set; }

        /// <summary>위치 도달 판정 허용오차 [pulse].</summary>
        public int PositionTolerancePulse { get; set; }

        /// <summary>이동 완료 타임아웃 [ms].</summary>
        public int MoveTimeoutMs { get; set; }

        /// <summary>원점 복귀 타임아웃 [ms].</summary>
        public int HomingTimeoutMs { get; set; }

        /// <summary>팬 최소 회전수 [RPM].</summary>
        public double MinRpm { get; set; }

        /// <summary>팬 최대 회전수 [RPM]. 0 이면 미확정(Open Issue #20).</summary>
        public double MaxRpm { get; set; }

        /// <summary>회전수 도달 판정 허용오차 [RPM].</summary>
        public double RpmTolerance { get; set; }

        /// <summary>가감속 완료 타임아웃 [ms].</summary>
        public int RampTimeoutMs { get; set; }

        /// <summary>통신자료 기준 기본값으로 초기화한다.</summary>
        public DeviceConversion()
        {
            PulsePerFullOpen = 5000;
            FullOpenDegree = 90.0;
            PositionTolerancePulse = 20;
            MoveTimeoutMs = 10000;
            HomingTimeoutMs = 30000;
            MinRpm = 0.0;
            MaxRpm = 0.0;
            RpmTolerance = 50.0;
            RampTimeoutMs = 15000;
        }
    }
}
