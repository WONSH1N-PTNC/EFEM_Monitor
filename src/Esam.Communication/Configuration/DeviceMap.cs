using System;
using System.Collections.Generic;
using System.Globalization;

namespace Esam.Communication.Configuration
{
    /// <summary>
    /// 전체 통신 구성. ports.json + device-map.json 을 합친 런타임 표현이다.
    /// </summary>
    /// <remarks>
    /// <para><see cref="Validate"/> 는 COMM_MAP.md 4.7 의 검증 규칙을 구현한다.
    /// 특히 <b>포트별 슬레이브 ID 유일성</b>은 DESIGN.md 2.2 (A)에서 지적한
    /// 실제 배선 위험(센서 1~13 / 밸브 1~5 / 팬 1~5 중복)을 잡아내는 항목이다.
    /// 같은 버스에 ID 가 겹치면 두 장치가 동시에 응답해 프레임이 깨지고,
    /// 증상은 간헐적 CRC 오류로만 나타나 원인 추적이 매우 어렵다.</para>
    /// </remarks>
    public sealed class DeviceMap
    {
        /// <summary>스키마 버전. 호환되지 않는 변경 시 올린다.</summary>
        public string SchemaVersion { get; set; }

        /// <summary>포트 목록.</summary>
        public IList<PortDefinition> Ports { get; set; }

        /// <summary>디바이스 종류 명세. 키는 종류 이름.</summary>
        public IDictionary<string, DeviceTypeDefinition> DeviceTypes { get; set; }

        /// <summary>설치된 디바이스 목록.</summary>
        public IList<DeviceInstanceDefinition> Devices { get; set; }

        /// <summary>기본값으로 초기화한다.</summary>
        public DeviceMap()
        {
            SchemaVersion = "1.0";
            Ports = new List<PortDefinition>();
            DeviceTypes = new Dictionary<string, DeviceTypeDefinition>(StringComparer.OrdinalIgnoreCase);
            Devices = new List<DeviceInstanceDefinition>();
        }

        /// <summary>지정 ID 의 포트 정의를 찾는다.</summary>
        /// <param name="portId">포트 ID.</param>
        /// <returns>포트 정의. 없으면 null.</returns>
        public PortDefinition FindPort(string portId)
        {
            if (Ports == null || string.IsNullOrEmpty(portId))
            {
                return null;
            }

            foreach (PortDefinition port in Ports)
            {
                if (port != null && string.Equals(port.PortId, portId, StringComparison.OrdinalIgnoreCase))
                {
                    return port;
                }
            }

            return null;
        }

        /// <summary>지정 종류 이름의 명세를 찾는다.</summary>
        /// <param name="typeName">종류 이름.</param>
        /// <returns>종류 명세. 없으면 null.</returns>
        public DeviceTypeDefinition FindType(string typeName)
        {
            DeviceTypeDefinition type;
            if (DeviceTypes != null && !string.IsNullOrEmpty(typeName)
                && DeviceTypes.TryGetValue(typeName, out type))
            {
                return type;
            }

            return null;
        }

        /// <summary>지정 ID 의 디바이스 정의를 찾는다.</summary>
        /// <param name="deviceId">디바이스 ID.</param>
        /// <returns>디바이스 정의. 없으면 null.</returns>
        public DeviceInstanceDefinition FindDevice(string deviceId)
        {
            if (Devices == null || string.IsNullOrEmpty(deviceId))
            {
                return null;
            }

            foreach (DeviceInstanceDefinition device in Devices)
            {
                if (device != null && string.Equals(device.Id, deviceId, StringComparison.OrdinalIgnoreCase))
                {
                    return device;
                }
            }

            return null;
        }

        /// <summary>지정 포트에 속한 활성 디바이스 목록을 반환한다.</summary>
        /// <param name="portId">포트 ID.</param>
        /// <returns>디바이스 목록.</returns>
        public IList<DeviceInstanceDefinition> GetDevicesOnPort(string portId)
        {
            List<DeviceInstanceDefinition> result = new List<DeviceInstanceDefinition>();

            if (Devices == null)
            {
                return result;
            }

            foreach (DeviceInstanceDefinition device in Devices)
            {
                if (device != null && device.Enabled
                    && string.Equals(device.Port, portId, StringComparison.OrdinalIgnoreCase))
                {
                    result.Add(device);
                }
            }

            return result;
        }

        /// <summary>
        /// 구성 전체를 검증한다(COMM_MAP.md 4.7).
        /// </summary>
        /// <param name="errors">검증 실패 사유 목록. 유효하면 빈 목록.</param>
        /// <param name="warnings">경고 목록(실행은 가능하나 확인이 필요한 사항).</param>
        /// <returns>치명 오류가 없으면 true.</returns>
        public bool Validate(out IList<string> errors, out IList<string> warnings)
        {
            List<string> found = new List<string>();
            List<string> notes = new List<string>();

            ValidatePorts(found);
            ValidateDeviceTypes(found, notes);
            ValidateDevices(found, notes);

            errors = found;
            warnings = notes;
            return found.Count == 0;
        }

        /// <summary>포트 정의를 검증한다.</summary>
        /// <param name="errors">오류 목록.</param>
        private void ValidatePorts(List<string> errors)
        {
            if (Ports == null || Ports.Count == 0)
            {
                errors.Add("포트 정의(ports)가 비어 있습니다.");
                return;
            }

            HashSet<string> seenPortIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> seenPortNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (PortDefinition port in Ports)
            {
                if (port == null)
                {
                    errors.Add("포트 목록에 null 항목이 있습니다.");
                    continue;
                }

                port.Validate(errors);

                if (!string.IsNullOrEmpty(port.PortId) && !seenPortIds.Add(port.PortId))
                {
                    errors.Add(string.Format(
                        CultureInfo.InvariantCulture, "포트 ID '{0}' 가 중복되었습니다.", port.PortId));
                }

                if (port.Serial != null && !string.IsNullOrEmpty(port.Serial.PortName)
                    && !seenPortNames.Add(port.Serial.PortName))
                {
                    // 같은 COM 포트를 두 논리 포트가 열면 반이중 직렬화가 깨진다.
                    errors.Add(string.Format(
                        CultureInfo.InvariantCulture,
                        "COM 포트 '{0}' 가 두 개 이상의 논리 포트에 배정되었습니다.", port.Serial.PortName));
                }
            }
        }

        /// <summary>디바이스 종류 명세를 검증한다.</summary>
        /// <param name="errors">오류 목록.</param>
        /// <param name="warnings">경고 목록.</param>
        private void ValidateDeviceTypes(List<string> errors, List<string> warnings)
        {
            if (DeviceTypes == null || DeviceTypes.Count == 0)
            {
                errors.Add("디바이스 종류 정의(deviceTypes)가 비어 있습니다.");
                return;
            }

            foreach (KeyValuePair<string, DeviceTypeDefinition> pair in DeviceTypes)
            {
                if (pair.Value == null)
                {
                    errors.Add(string.Format(
                        CultureInfo.InvariantCulture, "디바이스 종류 '{0}' 의 정의가 null 입니다.", pair.Key));
                    continue;
                }

                pair.Value.Validate(pair.Key, errors);

                // 주소 미확정 그룹은 오류가 아니라 경고다.
                // 명세 미확보 상태(Open Issue #5, #9)에서도 나머지 장치는 정상 폴링해야 하기 때문이다.
                if (pair.Value.ReadGroups == null)
                {
                    continue;
                }

                foreach (ReadGroupDefinition group in pair.Value.ReadGroups)
                {
                    if (group != null && group.IsAddressUnspecified)
                    {
                        warnings.Add(string.Format(
                            CultureInfo.InvariantCulture,
                            "{0}.{1}: 시작 주소가 미확정(TBD)이므로 폴링에서 제외됩니다.",
                            pair.Key, group.Name));
                    }
                }
            }
        }

        /// <summary>디바이스 인스턴스를 검증한다. 슬레이브 ID 충돌 검사가 핵심이다.</summary>
        /// <param name="errors">오류 목록.</param>
        /// <param name="warnings">경고 목록.</param>
        private void ValidateDevices(List<string> errors, List<string> warnings)
        {
            if (Devices == null || Devices.Count == 0)
            {
                errors.Add("디바이스 정의(devices)가 비어 있습니다.");
                return;
            }

            HashSet<string> seenDeviceIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // 포트별 슬레이브 ID 점유 현황. 키는 "포트ID|슬레이브ID".
            Dictionary<string, string> slaveOwners =
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (DeviceInstanceDefinition device in Devices)
            {
                if (device == null)
                {
                    errors.Add("디바이스 목록에 null 항목이 있습니다.");
                    continue;
                }

                device.Validate(errors);

                if (!string.IsNullOrEmpty(device.Id) && !seenDeviceIds.Add(device.Id))
                {
                    errors.Add(string.Format(
                        CultureInfo.InvariantCulture, "디바이스 ID '{0}' 가 중복되었습니다.", device.Id));
                }

                if (!string.IsNullOrEmpty(device.Port) && FindPort(device.Port) == null)
                {
                    errors.Add(string.Format(
                        CultureInfo.InvariantCulture,
                        "device '{0}': 포트 '{1}' 가 ports 에 정의되어 있지 않습니다.", device.Id, device.Port));
                }

                if (!string.IsNullOrEmpty(device.Type) && FindType(device.Type) == null)
                {
                    errors.Add(string.Format(
                        CultureInfo.InvariantCulture,
                        "device '{0}': 종류 '{1}' 가 deviceTypes 에 정의되어 있지 않습니다.",
                        device.Id, device.Type));
                }

                if (!device.Enabled)
                {
                    // 비활성 장치는 버스에 접근하지 않으므로 ID 충돌 검사 대상이 아니다.
                    continue;
                }

                if (string.IsNullOrEmpty(device.Port) || device.SlaveId == 0)
                {
                    continue;
                }

                string key = string.Format(
                    CultureInfo.InvariantCulture, "{0}|{1}", device.Port, device.SlaveId);

                string owner;
                if (slaveOwners.TryGetValue(key, out owner))
                {
                    errors.Add(string.Format(
                        CultureInfo.InvariantCulture,
                        "포트 '{0}' 에서 슬레이브 ID {1} 가 '{2}' 와 '{3}' 에 중복 배정되었습니다. " +
                        "같은 버스에 ID 가 겹치면 응답이 충돌해 통신이 깨집니다.",
                        device.Port, device.SlaveId, owner, device.Id));
                }
                else
                {
                    slaveOwners[key] = device.Id;
                }
            }

            // 활성 디바이스가 하나도 없는 포트는 워커를 띄울 필요가 없다.
            if (Ports == null)
            {
                return;
            }

            foreach (PortDefinition port in Ports)
            {
                if (port == null || string.IsNullOrEmpty(port.PortId))
                {
                    continue;
                }

                if (GetDevicesOnPort(port.PortId).Count == 0)
                {
                    warnings.Add(string.Format(
                        CultureInfo.InvariantCulture,
                        "포트 '{0}' 에 활성 디바이스가 없습니다.", port.PortId));
                }
            }
        }
    }
}
