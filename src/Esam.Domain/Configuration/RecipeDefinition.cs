using System;
using System.Collections.Generic;
using System.Globalization;

namespace Esam.Domain.Configuration
{
    /// <summary>
    /// 운전 파라미터 집합. <c>recipe.json</c> 전체에 대응한다.
    /// </summary>
    /// <remarks>
    /// <para><b>ECID 마스터다.</b> 상위(GEM)가 장비 상수로 읽고 쓰는 값이 여기 모인다.
    /// <c>ESAM_IO List</c> 의 ECID 39항목 = 압력센서 13대 × (설정값 + 상한 + 하한).</para>
    /// <para><b>이 파일에만 임계값 숫자가 있다.</b> <c>alarms.json</c> 은 "어느 센서의 어느 한계를
    /// 볼 것인가" 만 지정하고 값을 갖지 않는다. 값을 두 곳에 두면 Config 화면에서 바꿨을 때
    /// 알람만 옛 값으로 남아, 화면과 알람이 서로 다른 진실을 말하게 된다.
    /// 그 상태는 현장에서 원인을 찾기 매우 어렵다.</para>
    /// <para><b>하드웨어 사양은 여기 없다.</b> 센서 레인지·밸브 최대 pulse·팬 최대 RPM 은
    /// <c>device-map.json</c> 의 것이며, 레시피 값은 그 범위 안에서만 유효하다.
    /// 검증은 로드 시 수행한다.</para>
    /// </remarks>
    public sealed class RecipeDefinition
    {
        /// <summary>스키마 버전.</summary>
        public string SchemaVersion { get; set; }

        /// <summary>레시피 이름. 화면 표시와 로그에 쓴다.</summary>
        public string Name { get; set; }

        /// <summary>센서별 설정값 목록.</summary>
        public IList<SensorSetting> Sensors { get; set; }

        /// <summary>빈 레시피를 만든다.</summary>
        public RecipeDefinition()
        {
            Sensors = new List<SensorSetting>();
        }

        /// <summary>지정 센서의 설정값을 찾는다.</summary>
        /// <param name="deviceId">센서 디바이스 ID.</param>
        /// <returns>설정값. 없으면 null.</returns>
        public SensorSetting Find(string deviceId)
        {
            if (string.IsNullOrEmpty(deviceId) || Sensors == null)
            {
                return null;
            }

            foreach (SensorSetting setting in Sensors)
            {
                if (setting != null
                    && string.Equals(setting.DeviceId, deviceId, StringComparison.OrdinalIgnoreCase))
                {
                    return setting;
                }
            }

            return null;
        }

        /// <summary>
        /// 지정 센서의 설정값과 모드별 이탈 확정 시간을 합쳐 제어 파라미터를 만든다.
        /// </summary>
        /// <param name="deviceId">센서 디바이스 ID.</param>
        /// <param name="timeSec">해당 모드의 이탈 확정 시간 [초].</param>
        /// <returns>제어 파라미터. 센서 설정이 없으면 null.</returns>
        /// <remarks>
        /// <b>null 을 기본값으로 대체하지 않는다.</b> 설정이 없는 센서로 제어를 시작하면
        /// 엉뚱한 목표를 추종한다. 호출측이 null 을 확인해 제어를 건너뛰거나,
        /// 애초에 로드 검증에서 걸러야 한다.
        /// </remarks>
        public ModeSetting GetModeSetting(string deviceId, double timeSec)
        {
            SensorSetting setting = Find(deviceId);

            return setting == null ? null : setting.ToModeSetting(timeSec);
        }

        /// <summary>
        /// 레시피 자체의 유효성을 검증한다. 하드웨어 대조는 로더가 수행한다.
        /// </summary>
        /// <param name="errors">검증 실패 사유 목록. 유효하면 빈 목록.</param>
        /// <returns>유효하면 true.</returns>
        public bool Validate(out IList<string> errors)
        {
            List<string> found = new List<string>();
            errors = found;

            if (Sensors == null || Sensors.Count == 0)
            {
                found.Add("레시피에 센서 설정이 하나도 없습니다.");
                return false;
            }

            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (SensorSetting setting in Sensors)
            {
                if (setting == null)
                {
                    found.Add("null 센서 설정 항목이 있습니다.");
                    continue;
                }

                string error;

                if (!setting.Validate(out error))
                {
                    found.Add(error);
                    continue;
                }

                // 같은 센서가 두 번 나오면 나중 것이 앞의 것을 덮어 하나가 조용히 사라진다.
                if (!seen.Add(setting.DeviceId))
                {
                    found.Add(string.Format(
                        CultureInfo.InvariantCulture,
                        "센서 설정 중복: {0}", setting.DeviceId));
                }
            }

            return found.Count == 0;
        }
    }
}
