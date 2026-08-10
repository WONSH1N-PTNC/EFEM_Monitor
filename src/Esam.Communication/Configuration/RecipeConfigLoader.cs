using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Esam.Domain.Configuration;
using Newtonsoft.Json;

namespace Esam.Communication.Configuration
{
    /// <summary>레시피 로드 결과.</summary>
    public sealed class RecipeLoadResult
    {
        /// <summary>로드에 성공했는지 여부.</summary>
        public bool IsSuccess { get; private set; }

        /// <summary>로드된 레시피. 실패 시 null.</summary>
        public RecipeDefinition Recipe { get; private set; }

        /// <summary>치명적 오류 목록.</summary>
        public IList<string> Errors { get; private set; }

        /// <summary>경고 목록. 로드는 되지만 확인이 필요한 항목.</summary>
        public IList<string> Warnings { get; private set; }

        /// <summary>결과를 생성한다.</summary>
        /// <param name="recipe">레시피.</param>
        /// <param name="errors">오류 목록.</param>
        /// <param name="warnings">경고 목록.</param>
        public RecipeLoadResult(
            RecipeDefinition recipe, IList<string> errors, IList<string> warnings)
        {
            Errors = errors ?? new List<string>();
            Warnings = warnings ?? new List<string>();
            IsSuccess = Errors.Count == 0;
            Recipe = IsSuccess ? recipe : null;
        }
    }

    /// <summary>
    /// <c>recipe.json</c> 로더. 하드웨어 구성과 대조해 검증한다.
    /// </summary>
    /// <remarks>
    /// <para>설정 파일을 역할별로 나누면 <b>참조가 끊어지는 것</b>이 새 위험이 된다.
    /// 레시피가 존재하지 않는 센서를 가리켜도, 센서 레인지를 넘는 값을 담아도
    /// 파일 자체는 문법적으로 유효하다. 그래서 로드 시 <c>device-map</c> 과 대조한다.</para>
    /// <para>실패시킬 것과 알릴 것을 구분한다. 참조 오류·범위 초과·상하한 역전은 오류다.
    /// 하드웨어에 있는데 레시피에 없는 센서는 경고다 — 그 센서로 제어하지 않는 구성일 수 있다.</para>
    /// </remarks>
    public static class RecipeConfigLoader
    {
        /// <summary>JSON 문자열에서 레시피를 읽는다.</summary>
        /// <param name="json">JSON 문자열. 주석을 허용한다.</param>
        /// <param name="map">대조할 통신 구성. null 이면 하드웨어 검증을 생략한다.</param>
        /// <returns>로드 결과.</returns>
        public static RecipeLoadResult LoadFromJson(string json, DeviceMap map)
        {
            List<string> errors = new List<string>();
            List<string> warnings = new List<string>();

            if (string.IsNullOrWhiteSpace(json))
            {
                errors.Add("레시피 설정이 비어 있습니다.");
                return new RecipeLoadResult(null, errors, warnings);
            }

            RecipeDefinition recipe;

            try
            {
                recipe = JsonConvert.DeserializeObject<RecipeDefinition>(
                    json, CommunicationConfigLoader.CreateSettings());
            }
            catch (JsonException ex)
            {
                errors.Add("레시피 구문 오류: " + ex.Message);
                return new RecipeLoadResult(null, errors, warnings);
            }

            if (recipe == null)
            {
                errors.Add("레시피를 해석할 수 없습니다.");
                return new RecipeLoadResult(null, errors, warnings);
            }

            // ── 검증 3: 레시피 자체 정합성 (상하한 역전, 중복, 설정값 위치) ──────
            IList<string> selfErrors;

            if (!recipe.Validate(out selfErrors))
            {
                foreach (string error in selfErrors)
                {
                    errors.Add(error);
                }
            }

            // ── 검증 1·2: 하드웨어 대조 ─────────────────────────────────────────
            if (map != null)
            {
                CrossCheck(recipe, map, errors, warnings);
            }
            else
            {
                warnings.Add(
                    "통신 구성 없이 레시피를 로드했습니다. 센서 존재·레인지 검증을 건너뜁니다.");
            }

            return new RecipeLoadResult(recipe, errors, warnings);
        }

        /// <summary>파일에서 레시피를 읽는다.</summary>
        /// <param name="path">파일 경로.</param>
        /// <param name="map">대조할 통신 구성. null 이면 하드웨어 검증을 생략한다.</param>
        /// <returns>로드 결과.</returns>
        public static RecipeLoadResult LoadFromFile(string path, DeviceMap map)
        {
            List<string> errors = new List<string>();

            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                errors.Add(Format("레시피 파일을 찾을 수 없습니다: {0}", path));
                return new RecipeLoadResult(null, errors, null);
            }

            try
            {
                return LoadFromJson(File.ReadAllText(path), map);
            }
            catch (IOException ex)
            {
                errors.Add("레시피 파일 읽기 실패: " + ex.Message);
                return new RecipeLoadResult(null, errors, null);
            }
            catch (UnauthorizedAccessException ex)
            {
                errors.Add("레시피 파일 접근 거부: " + ex.Message);
                return new RecipeLoadResult(null, errors, null);
            }
        }

        /// <summary>레시피를 JSON 으로 직렬화한다. 화면에서 수정한 값을 저장할 때 쓴다.</summary>
        /// <param name="recipe">레시피.</param>
        /// <returns>JSON 문자열.</returns>
        /// <exception cref="ArgumentNullException">레시피가 null 일 때.</exception>
        public static string ToJson(RecipeDefinition recipe)
        {
            if (recipe == null)
            {
                throw new ArgumentNullException("recipe");
            }

            JsonSerializerSettings settings = CommunicationConfigLoader.CreateSettings();
            settings.Formatting = Formatting.Indented;

            // 저장 시에는 알 수 없는 멤버 검사가 의미 없으므로 되돌린다.
            settings.MissingMemberHandling = MissingMemberHandling.Ignore;

            return JsonConvert.SerializeObject(recipe, settings);
        }

        /// <summary>레시피를 통신 구성과 대조한다.</summary>
        /// <param name="recipe">레시피.</param>
        /// <param name="map">통신 구성.</param>
        /// <param name="errors">오류 목록(출력).</param>
        /// <param name="warnings">경고 목록(출력).</param>
        private static void CrossCheck(
            RecipeDefinition recipe, DeviceMap map, IList<string> errors, IList<string> warnings)
        {
            HashSet<string> covered = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (SensorSetting setting in recipe.Sensors)
            {
                if (setting == null || string.IsNullOrEmpty(setting.DeviceId))
                {
                    continue;
                }

                covered.Add(setting.DeviceId);

                // ── 검증 1: 존재하는 센서인가 ───────────────────────────────────
                DeviceInstanceDefinition device = map.FindDevice(setting.DeviceId);

                if (device == null)
                {
                    errors.Add(Format(
                        "레시피의 센서 {0} 가 통신 구성에 없습니다. 이 설정값은 적용되지 않습니다.",
                        setting.DeviceId));

                    continue;
                }

                DeviceTypeDefinition type = map.FindType(device.Type);

                if (type == null || type.Driver != DriverNames.PressureSensor)
                {
                    errors.Add(Format(
                        "레시피의 {0} 는 압력센서가 아닙니다(driver={1}).",
                        setting.DeviceId, type == null ? "unknown" : type.Driver));

                    continue;
                }

                // ── 검증 2: 센서 레인지 안인가 ──────────────────────────────────
                // 레인지를 넘는 값을 넣으면 도달 불가능한 목표를 영원히 추종한다.
                CheckRange(device, setting.DeviceId, "설정값", setting.SetpointPa, errors);
                CheckRange(device, setting.DeviceId, "하한", setting.LowLimitPa, errors);
                CheckRange(device, setting.DeviceId, "상한", setting.HighLimitPa, errors);
            }

            // 하드웨어에 있는데 레시피에 없는 압력센서는 경고다.
            // 그 센서로 제어하지 않는 구성일 수 있으므로 막지는 않는다.
            foreach (DeviceInstanceDefinition device in map.Devices)
            {
                if (device == null || !device.Enabled || string.IsNullOrEmpty(device.Id))
                {
                    continue;
                }

                DeviceTypeDefinition type = map.FindType(device.Type);

                if (type == null || type.Driver != DriverNames.PressureSensor)
                {
                    continue;
                }

                if (!covered.Contains(device.Id))
                {
                    warnings.Add(Format(
                        "센서 {0} 가 레시피에 없습니다. 이 센서를 제어 기준으로 쓸 수 없습니다.",
                        device.Id));
                }
            }
        }

        /// <summary>값이 센서 레인지 안인지 확인한다.</summary>
        /// <param name="device">디바이스 정의.</param>
        /// <param name="deviceId">디바이스 ID.</param>
        /// <param name="label">값의 이름(오류 문구용).</param>
        /// <param name="value">확인할 값.</param>
        /// <param name="errors">오류 목록(출력).</param>
        private static void CheckRange(
            DeviceInstanceDefinition device,
            string deviceId,
            string label,
            double value,
            IList<string> errors)
        {
            if (device.IsWithinRange(value))
            {
                return;
            }

            errors.Add(Format(
                "센서 {0} 의 {1}({2:F1})이 계측 레인지({3} ~ {4})를 벗어났습니다.",
                deviceId,
                label,
                value,
                device.RangeMin.HasValue
                    ? device.RangeMin.Value.ToString("F1", CultureInfo.InvariantCulture)
                    : "-∞",
                device.RangeMax.HasValue
                    ? device.RangeMax.Value.ToString("F1", CultureInfo.InvariantCulture)
                    : "+∞"));
        }

        /// <summary>불변 문화권으로 문자열을 만든다.</summary>
        /// <param name="format">서식.</param>
        /// <param name="args">인자.</param>
        /// <returns>서식이 적용된 문자열.</returns>
        private static string Format(string format, params object[] args)
        {
            return string.Format(CultureInfo.InvariantCulture, format, args);
        }
    }
}
