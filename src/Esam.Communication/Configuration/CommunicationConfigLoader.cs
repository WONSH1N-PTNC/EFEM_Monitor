using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;

namespace Esam.Communication.Configuration
{
    /// <summary>
    /// 설정 로드 결과. 오류와 경고를 분리해 반환한다.
    /// </summary>
    public sealed class ConfigLoadResult
    {
        /// <summary>로드·검증에 성공했는지 여부.</summary>
        public bool IsSuccess { get; private set; }

        /// <summary>로드된 구성. 실패 시 null.</summary>
        public DeviceMap Map { get; private set; }

        /// <summary>치명 오류 목록. 하나라도 있으면 실행할 수 없다.</summary>
        public IList<string> Errors { get; private set; }

        /// <summary>경고 목록. 실행은 가능하나 확인이 필요한 사항(주소 미확정 등).</summary>
        public IList<string> Warnings { get; private set; }

        /// <summary>로드 결과를 생성한다.</summary>
        /// <param name="map">로드된 구성.</param>
        /// <param name="errors">오류 목록.</param>
        /// <param name="warnings">경고 목록.</param>
        public ConfigLoadResult(DeviceMap map, IList<string> errors, IList<string> warnings)
        {
            Errors = errors ?? new List<string>();
            Warnings = warnings ?? new List<string>();
            IsSuccess = Errors.Count == 0 && map != null;
            Map = IsSuccess ? map : null;
        }

        /// <summary>오류·경고를 사람이 읽을 수 있는 여러 줄 문자열로 만든다.</summary>
        /// <returns>진단 메시지.</returns>
        public string BuildReport()
        {
            System.Text.StringBuilder builder = new System.Text.StringBuilder();

            builder.AppendLine(IsSuccess ? "설정 로드 성공" : "설정 로드 실패");

            if (Errors.Count > 0)
            {
                builder.AppendLine();
                builder.AppendLine(string.Format(
                    CultureInfo.InvariantCulture, "오류 {0}건:", Errors.Count));

                foreach (string error in Errors)
                {
                    builder.AppendLine("  [오류] " + error);
                }
            }

            if (Warnings.Count > 0)
            {
                builder.AppendLine();
                builder.AppendLine(string.Format(
                    CultureInfo.InvariantCulture, "경고 {0}건:", Warnings.Count));

                foreach (string warning in Warnings)
                {
                    builder.AppendLine("  [경고] " + warning);
                }
            }

            return builder.ToString();
        }
    }

    /// <summary>
    /// ports.json / device-map.json 로더.
    /// </summary>
    /// <remarks>
    /// <para>설정 파일은 현장 엔지니어가 직접 편집하므로 다음을 허용한다.</para>
    /// <list type="bullet">
    ///   <item><description><b>주석</b>(<c>//</c>, <c>/* */</c>) — 각 주소가 무엇인지
    ///     파일 안에 적어 둘 수 있어야 한다. 레지스터 주소는 숫자만 보면 의미를 알 수 없다.</description></item>
    ///   <item><description><b>camelCase</b> — JSON 관례에 맞춘 이름을 C# 속성에 자동 대응시킨다.</description></item>
    ///   <item><description><b>열거형 문자열</b> — <c>"tier": "Fast"</c> 처럼 이름으로 쓸 수 있다.</description></item>
    /// </list>
    /// <para><b>알 수 없는 속성은 오류로 처리한다.</b> 오타를 조용히 무시하면
    /// 설정이 반영되지 않은 채 운전되는 최악의 상황이 생긴다.
    /// 예를 들어 <c>"slaveId"</c> 를 <c>"slaveID"</c> 로 잘못 쓰면
    /// 기본값 0 으로 남아 통신이 되지 않는데 원인을 찾기 어렵다.</para>
    /// </remarks>
    public static class CommunicationConfigLoader
    {
        /// <summary>표준 직렬화 설정을 만든다.</summary>
        /// <returns>직렬화 설정.</returns>
        public static JsonSerializerSettings CreateSettings()
        {
            JsonSerializerSettings settings = new JsonSerializerSettings();

            // JSON 은 camelCase, C# 은 PascalCase 관례를 각각 유지한다.
            settings.ContractResolver = new CamelCasePropertyNamesContractResolver();

            // 열거형을 "Fast", "Int16" 같은 문자열로 쓸 수 있게 한다. 숫자보다 읽기 쉽다.
            settings.Converters.Add(new StringEnumConverter());

            // 오타를 반드시 드러낸다.
            settings.MissingMemberHandling = MissingMemberHandling.Error;

            // null 을 명시한 항목은 기본값을 유지하도록 무시한다.
            settings.NullValueHandling = NullValueHandling.Ignore;

            settings.FloatParseHandling = FloatParseHandling.Double;
            settings.Culture = CultureInfo.InvariantCulture;

            return settings;
        }

        /// <summary>JSON 문자열에서 구성을 로드하고 검증한다.</summary>
        /// <param name="json">device-map JSON 문자열.</param>
        /// <returns>로드 결과.</returns>
        public static ConfigLoadResult LoadFromJson(string json)
        {
            List<string> errors = new List<string>();
            List<string> warnings = new List<string>();

            if (string.IsNullOrEmpty(json))
            {
                errors.Add("설정 내용이 비어 있습니다.");
                return new ConfigLoadResult(null, errors, warnings);
            }

            DeviceMap map;

            try
            {
                map = JsonConvert.DeserializeObject<DeviceMap>(json, CreateSettings());
            }
            catch (JsonException ex)
            {
                // Newtonsoft 는 오류 위치(줄/열)를 메시지에 포함하므로 그대로 노출한다.
                errors.Add("JSON 파싱 실패: " + ex.Message);
                return new ConfigLoadResult(null, errors, warnings);
            }

            if (map == null)
            {
                errors.Add("설정을 역직렬화한 결과가 null 입니다.");
                return new ConfigLoadResult(null, errors, warnings);
            }

            IList<string> validationErrors;
            IList<string> validationWarnings;
            map.Validate(out validationErrors, out validationWarnings);

            errors.AddRange(validationErrors);
            warnings.AddRange(validationWarnings);

            return new ConfigLoadResult(map, errors, warnings);
        }

        /// <summary>파일에서 구성을 로드하고 검증한다.</summary>
        /// <param name="path">device-map.json 경로.</param>
        /// <returns>로드 결과.</returns>
        public static ConfigLoadResult LoadFromFile(string path)
        {
            List<string> errors = new List<string>();

            if (string.IsNullOrEmpty(path))
            {
                errors.Add("설정 파일 경로가 지정되지 않았습니다.");
                return new ConfigLoadResult(null, errors, null);
            }

            if (!File.Exists(path))
            {
                errors.Add(string.Format(
                    CultureInfo.InvariantCulture, "설정 파일을 찾을 수 없습니다: {0}", path));
                return new ConfigLoadResult(null, errors, null);
            }

            string json;

            try
            {
                json = File.ReadAllText(path, System.Text.Encoding.UTF8);
            }
            catch (IOException ex)
            {
                errors.Add(string.Format(
                    CultureInfo.InvariantCulture, "설정 파일을 읽을 수 없습니다({0}): {1}", path, ex.Message));
                return new ConfigLoadResult(null, errors, null);
            }
            catch (UnauthorizedAccessException ex)
            {
                errors.Add(string.Format(
                    CultureInfo.InvariantCulture, "설정 파일 접근이 거부되었습니다({0}): {1}", path, ex.Message));
                return new ConfigLoadResult(null, errors, null);
            }

            ConfigLoadResult result = LoadFromJson(json);

            if (!result.IsSuccess)
            {
                // 어느 파일에서 난 오류인지 알 수 있게 경로를 앞에 붙인다.
                List<string> prefixed = new List<string>();
                foreach (string error in result.Errors)
                {
                    prefixed.Add(string.Format(
                        CultureInfo.InvariantCulture, "{0}: {1}", Path.GetFileName(path), error));
                }

                return new ConfigLoadResult(null, prefixed, result.Warnings);
            }

            return result;
        }

        /// <summary>구성을 JSON 문자열로 직렬화한다. 설정 화면의 저장 기능에 사용한다.</summary>
        /// <param name="map">구성.</param>
        /// <returns>JSON 문자열(들여쓰기 적용).</returns>
        public static string ToJson(DeviceMap map)
        {
            JsonSerializerSettings settings = CreateSettings();
            settings.Formatting = Formatting.Indented;

            // 저장 시에는 알 수 없는 멤버 검사가 의미 없으므로 되돌린다.
            settings.MissingMemberHandling = MissingMemberHandling.Ignore;

            return JsonConvert.SerializeObject(map, settings);
        }
    }
}
