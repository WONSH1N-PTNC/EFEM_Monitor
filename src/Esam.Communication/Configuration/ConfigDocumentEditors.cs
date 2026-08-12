using System;
using System.Collections.Generic;
using System.Globalization;
using Esam.Domain.Configuration;

namespace Esam.Communication.Configuration
{
    /// <summary>
    /// <c>device-map.json</c> 원문에서 편집 가능한 값만 바꿔 쓴다.
    /// </summary>
    /// <remarks>
    /// <para>화면이 고치는 것은 <b>포트 설정</b>(포트 이름·보레이트 등)과
    /// <b>영점 오프셋</b>뿐이다. 레지스터 주소나 스케일은 하드웨어 사양이라
    /// 화면에서 건드리지 않는다.</para>
    /// <para>이 파일의 주석 55줄에는 폴링 예산 계산, 압력 스케일이 잠정값이라는 사실,
    /// 시뮬레이션 슬레이브와 값이 짝이라는 사실이 적혀 있다.
    /// <b>현장에서 COM 포트를 한 번 바꾸는 것만으로 그 근거가 사라지면 안 된다.</b></para>
    /// </remarks>
    public static class DeviceMapDocumentEditor
    {
        /// <summary>편집 결과를 원문에 반영한다.</summary>
        /// <param name="json">원문.</param>
        /// <param name="map">편집된 통신 구성.</param>
        /// <param name="result">수정된 원문. 실패 시 null.</param>
        /// <param name="error">실패 사유. 성공 시 null.</param>
        /// <returns>성공하면 true.</returns>
        public static bool TryApply(
            string json, DeviceMap map, out string result, out string error)
        {
            result = null;

            if (map == null)
            {
                error = "통신 구성이 없습니다.";
                return false;
            }

            JsonTextObject root;

            if (!JsonTextScanner.TryScan(json, out root, out error))
            {
                return false;
            }

            JsonTextPatch patch = new JsonTextPatch();

            if (!ApplyPorts(root, map, patch, out error))
            {
                return false;
            }

            if (!ApplyDevices(root, map, patch, out error))
            {
                return false;
            }

            result = patch.Apply(json);
            return true;
        }

        /// <summary>포트 설정을 반영한다.</summary>
        /// <param name="root">최상위 객체.</param>
        /// <param name="map">통신 구성.</param>
        /// <param name="patch">치환 목록.</param>
        /// <param name="error">실패 사유(출력).</param>
        /// <returns>성공하면 true.</returns>
        private static bool ApplyPorts(
            JsonTextObject root, DeviceMap map, JsonTextPatch patch, out string error)
        {
            error = null;

            IList<JsonTextObject> ports = root.Array("ports");

            foreach (PortDefinition port in map.Ports)
            {
                if (port == null || port.Serial == null)
                {
                    continue;
                }

                JsonTextObject serial = FindSerial(ports, port.Serial.PortId);

                if (serial == null)
                {
                    // 화면은 포트를 추가하지 않는다. 없다는 것은 파일이 밖에서
                    // 바뀌었다는 뜻이고, 덮어쓰면 그 변경을 지운다.
                    error = string.Format(
                        CultureInfo.InvariantCulture,
                        "포트 {0} 이 파일에 없습니다. 파일이 외부에서 변경되었습니다. "
                        + "다시 읽은 뒤 편집하십시오.",
                        port.Serial.PortId);

                    return false;
                }

                patch.SetString(serial, "portName", port.Serial.PortName, true);
                patch.SetNumber(serial, "baudRate", port.Serial.BaudRate, true);
                patch.SetString(serial, "parity", port.Serial.Parity.ToString(), true);
                patch.SetNumber(serial, "dataBits", port.Serial.DataBits, true);
                patch.SetNumber(serial, "stopBits", (int)port.Serial.StopBits, true);
                patch.SetNumber(serial, "responseTimeoutMs", port.Serial.ResponseTimeoutMs, true);
                patch.SetNumber(serial, "retryCount", port.Serial.RetryCount, true);
            }

            return true;
        }

        /// <summary>디바이스별 영점 오프셋과 사용 여부를 반영한다.</summary>
        /// <param name="root">최상위 객체.</param>
        /// <param name="map">통신 구성.</param>
        /// <param name="patch">치환 목록.</param>
        /// <param name="error">실패 사유(출력).</param>
        /// <returns>성공하면 true.</returns>
        private static bool ApplyDevices(
            JsonTextObject root, DeviceMap map, JsonTextPatch patch, out string error)
        {
            error = null;

            Dictionary<string, JsonTextObject> byId =
                new Dictionary<string, JsonTextObject>(StringComparer.OrdinalIgnoreCase);

            foreach (JsonTextObject device in root.Array("devices"))
            {
                string id = device.Text("id");

                if (!string.IsNullOrEmpty(id))
                {
                    byId[id] = device;
                }
            }

            foreach (DeviceInstanceDefinition device in map.Devices)
            {
                if (device == null || string.IsNullOrEmpty(device.Id))
                {
                    continue;
                }

                JsonTextObject span;

                if (!byId.TryGetValue(device.Id, out span))
                {
                    error = string.Format(
                        CultureInfo.InvariantCulture,
                        "디바이스 {0} 이 파일에 없습니다. 파일이 외부에서 변경되었습니다.",
                        device.Id);

                    return false;
                }

                // 영점은 0 이 기본값이라 원문에서 생략되어 있다.
                // 교정하면 넣어야 한다.
                patch.SetNumber(span, "offset", device.Offset, device.Offset != 0.0);

                // enabled 는 true 가 기본값이라 생략되어 있다. 끌 때만 넣는다.
                patch.SetBoolean(span, "enabled", device.Enabled, !device.Enabled);
            }

            return true;
        }

        /// <summary>포트 ID 로 <c>serial</c> 객체를 찾는다.</summary>
        /// <param name="ports">포트 객체 목록.</param>
        /// <param name="portId">포트 ID.</param>
        /// <returns>객체. 없으면 null.</returns>
        private static JsonTextObject FindSerial(IList<JsonTextObject> ports, string portId)
        {
            foreach (JsonTextObject port in ports)
            {
                JsonTextObject serial = port.Object("serial");

                if (serial == null)
                {
                    continue;
                }

                if (string.Equals(serial.Text("portId"), portId, StringComparison.OrdinalIgnoreCase))
                {
                    return serial;
                }
            }

            return null;
        }
    }

    /// <summary>
    /// <c>recipe.json</c> 원문에서 센서별 설정값만 바꿔 쓴다.
    /// </summary>
    /// <remarks>
    /// 이 파일은 52줄 중 29줄이 주석이다. 상한과 하한을 독립으로 둔 이유,
    /// 인터록이 이 대역을 쓰지 않는 이유가 거기 적혀 있다.
    /// <b>재직렬화하면 파일의 절반 이상이 사라진다.</b>
    /// </remarks>
    public static class RecipeDocumentEditor
    {
        /// <summary>편집 결과를 원문에 반영한다.</summary>
        /// <param name="json">원문.</param>
        /// <param name="recipe">편집된 레시피.</param>
        /// <param name="result">수정된 원문. 실패 시 null.</param>
        /// <param name="error">실패 사유. 성공 시 null.</param>
        /// <returns>성공하면 true.</returns>
        public static bool TryApply(
            string json, RecipeDefinition recipe, out string result, out string error)
        {
            result = null;

            if (recipe == null)
            {
                error = "레시피가 없습니다.";
                return false;
            }

            JsonTextObject root;

            if (!JsonTextScanner.TryScan(json, out root, out error))
            {
                return false;
            }

            Dictionary<string, JsonTextObject> byId =
                new Dictionary<string, JsonTextObject>(StringComparer.OrdinalIgnoreCase);

            foreach (JsonTextObject sensor in root.Array("sensors"))
            {
                string id = sensor.Text("deviceId");

                if (!string.IsNullOrEmpty(id))
                {
                    byId[id] = sensor;
                }
            }

            JsonTextPatch patch = new JsonTextPatch();

            foreach (SensorSetting setting in recipe.Sensors)
            {
                if (setting == null || string.IsNullOrEmpty(setting.DeviceId))
                {
                    continue;
                }

                JsonTextObject span;

                if (!byId.TryGetValue(setting.DeviceId, out span))
                {
                    // 화면은 센서를 추가하지 않는다. 센서 구성은 device-map 이 정한다.
                    error = string.Format(
                        CultureInfo.InvariantCulture,
                        "센서 {0} 이 레시피 파일에 없습니다. 파일이 외부에서 변경되었습니다.",
                        setting.DeviceId);

                    return false;
                }

                patch.SetNumber(span, "setpointPa", setting.SetpointPa, true);
                patch.SetNumber(span, "lowLimitPa", setting.LowLimitPa, true);
                patch.SetNumber(span, "highLimitPa", setting.HighLimitPa, true);
            }

            result = patch.Apply(json);
            return true;
        }
    }
}
