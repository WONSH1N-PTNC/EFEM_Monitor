using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Esam.Domain.Configuration;
using Esam.Domain.Control;
using Newtonsoft.Json;

namespace Esam.Communication.Configuration
{
    /// <summary>모드별 공통값 1건의 파일 표현.</summary>
    public sealed class ModeSettingDocument
    {
        /// <summary>센서 모드.</summary>
        public SensorMode Mode { get; set; }

        /// <summary>목표 압력 [Pa].</summary>
        public double SetpointPa { get; set; }

        /// <summary>허용 대역 [Pa].</summary>
        public double BandPa { get; set; }

        /// <summary>이탈 확정 시간 [s].</summary>
        public double TimeSec { get; set; }
    }

    /// <summary>제어 설정 파일의 최상위 구조.</summary>
    public sealed class ControlConfigDocument
    {
        /// <summary>스키마 버전.</summary>
        public string SchemaVersion { get; set; }

        /// <summary>제어 기준 센서 모드.</summary>
        public SensorMode ActiveMode { get; set; }

        /// <summary>제어 방식.</summary>
        public ControlPolicyKind Policy { get; set; }

        /// <summary>제어 루프 주기 [ms].</summary>
        public int ControlPeriodMs { get; set; }

        /// <summary>센서 1 기준 디바이스 ID.</summary>
        public string Sensor1Reference { get; set; }

        /// <summary>이동평균 창 크기.</summary>
        public int FilterWindowSize { get; set; }

        /// <summary>원본과 필터값을 함께 기록할지 여부.</summary>
        public bool LogFilteredAndRaw { get; set; }

        /// <summary>안전 입력의 첫 수신을 기다리는 시간 [ms].</summary>
        public int SafetyInputGraceMs { get; set; }

        /// <summary>모드별 공통값.</summary>
        public IList<ModeSettingDocument> Modes { get; set; }

        /// <summary>스로틀밸브 파라미터.</summary>
        public ValveActuatorConfig Valve { get; set; }

        /// <summary>송풍팬 파라미터.</summary>
        public FanActuatorConfig Fan { get; set; }

        /// <summary>기류 순환 통로.</summary>
        public IList<ChainDefinition> Chains { get; set; }

        /// <summary>데이터 기록 설정.</summary>
        /// <remarks>
        /// 절이 없으면 null 이다. 그때는 코드 기본값을 쓰고 <b>경고로 알린다</b>.
        /// 조용히 기본값으로 돌면 파일에 기록 설정을 적었다고 착각하게 된다.
        /// </remarks>
        public LoggingConfig Logging { get; set; }

        /// <summary>빈 문서를 만든다.</summary>
        public ControlConfigDocument()
        {
            Modes = new List<ModeSettingDocument>();
            Chains = new List<ChainDefinition>();
        }
    }

    /// <summary>제어 설정 로드 결과.</summary>
    public sealed class ControlLoadResult
    {
        /// <summary>로드에 성공했는지 여부.</summary>
        public bool IsSuccess { get; private set; }

        /// <summary>로드된 제어 설정. 실패 시 null.</summary>
        public ControlConfig Config { get; private set; }

        /// <summary>치명적 오류 목록.</summary>
        public IList<string> Errors { get; private set; }

        /// <summary>경고 목록.</summary>
        public IList<string> Warnings { get; private set; }

        /// <summary>결과를 생성한다.</summary>
        /// <param name="config">제어 설정.</param>
        /// <param name="errors">오류 목록.</param>
        /// <param name="warnings">경고 목록.</param>
        public ControlLoadResult(ControlConfig config, IList<string> errors, IList<string> warnings)
        {
            Errors = errors ?? new List<string>();
            Warnings = warnings ?? new List<string>();
            IsSuccess = Errors.Count == 0;
            Config = IsSuccess ? config : null;
        }
    }

    /// <summary>
    /// <c>control.json</c> 로더.
    /// </summary>
    /// <remarks>
    /// <para>이 파일이 생기기 전까지 Step·Dwell·밸브 최대 개도 같은 제어 파라미터가
    /// <b>전부 코드 기본값</b>이었다. 현장에서 하나를 조정하려면 재컴파일이 필요했고,
    /// 재컴파일이 필요하면 아무도 조정하지 않는다.</para>
    /// <para>통로 활성화도 여기 있다. 종전에는 설정 화면에서만 바뀌고 저장되지 않아
    /// <b>재시작하면 전부 켜진 상태로 돌아갔다.</b></para>
    /// <para>검증은 <see cref="ControlConfig.Validate"/> 를 그대로 쓴다.
    /// 로더가 따로 검사하면 규칙이 두 곳에 생긴다.</para>
    /// </remarks>
    public static class ControlConfigLoader
    {
        /// <summary>JSON 문자열에서 제어 설정을 읽는다.</summary>
        /// <param name="json">JSON 문자열. 주석을 허용한다.</param>
        /// <returns>로드 결과.</returns>
        public static ControlLoadResult LoadFromJson(string json)
        {
            List<string> errors = new List<string>();
            List<string> warnings = new List<string>();

            if (string.IsNullOrWhiteSpace(json))
            {
                errors.Add("제어 설정이 비어 있습니다.");
                return new ControlLoadResult(null, errors, warnings);
            }

            ControlConfigDocument document;

            try
            {
                document = JsonConvert.DeserializeObject<ControlConfigDocument>(
                    json, CommunicationConfigLoader.CreateSettings());
            }
            catch (JsonException ex)
            {
                errors.Add("제어 설정 구문 오류: " + ex.Message);
                return new ControlLoadResult(null, errors, warnings);
            }

            if (document == null)
            {
                errors.Add("제어 설정을 해석하지 못했습니다.");
                return new ControlLoadResult(null, errors, warnings);
            }

            ControlConfig config = Build(document, errors, warnings);

            if (errors.Count > 0)
            {
                return new ControlLoadResult(null, errors, warnings);
            }

            IList<string> validationErrors;

            // ★ 검증은 도메인 모델이 한다. 여기서 따로 검사하면 규칙이 두 곳에 생기고,
            // 한쪽만 바뀌었을 때 로드는 통과하는데 제어가 성립하지 않는 상태가 된다.
            if (!config.Validate(out validationErrors))
            {
                foreach (string error in validationErrors)
                {
                    errors.Add(error);
                }
            }

            return new ControlLoadResult(config, errors, warnings);
        }

        /// <summary>파일에서 제어 설정을 읽는다.</summary>
        /// <param name="path">파일 경로.</param>
        /// <returns>로드 결과.</returns>
        public static ControlLoadResult LoadFromFile(string path)
        {
            List<string> errors = new List<string>();

            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                errors.Add(Format("제어 설정 파일을 찾을 수 없습니다: {0}", path));
                return new ControlLoadResult(null, errors, null);
            }

            try
            {
                return LoadFromJson(File.ReadAllText(path));
            }
            catch (IOException ex)
            {
                errors.Add("제어 설정 파일 읽기 실패: " + ex.Message);
                return new ControlLoadResult(null, errors, null);
            }
            catch (UnauthorizedAccessException ex)
            {
                errors.Add("제어 설정 파일 접근 거부: " + ex.Message);
                return new ControlLoadResult(null, errors, null);
            }
        }

        /// <summary>문서를 제어 설정으로 옮긴다.</summary>
        /// <param name="document">파일 표현.</param>
        /// <param name="errors">오류 목록(출력).</param>
        /// <param name="warnings">경고 목록(출력).</param>
        /// <returns>제어 설정.</returns>
        private static ControlConfig Build(
            ControlConfigDocument document, IList<string> errors, IList<string> warnings)
        {
            ControlConfig config = new ControlConfig();

            config.ActiveMode = document.ActiveMode;
            config.Policy = document.Policy;
            config.ControlPeriodMs = document.ControlPeriodMs;
            config.Sensor1Reference = document.Sensor1Reference;
            config.FilterWindowSize = document.FilterWindowSize;
            config.LogFilteredAndRaw = document.LogFilteredAndRaw;
            // SafetyInputsConfigured 는 파일에서 읽지 않는다. device-map 에 PLC 가
            // 있는지로 조립 루트가 판정한다. 파일에 두면 읽히지 않는 값이 남는다.
            config.SafetyInputGraceMs = document.SafetyInputGraceMs;

            if (document.Logging != null)
            {
                config.Logging = document.Logging;
            }
            else
            {
                warnings.Add("logging 절이 없습니다. 기록 설정을 코드 기본값으로 사용합니다.");
            }

            if (document.Valve != null)
            {
                config.Valve = document.Valve;
            }

            if (document.Fan != null)
            {
                config.Fan = document.Fan;
            }

            if (document.Modes != null && document.Modes.Count > 0)
            {
                foreach (ModeSettingDocument mode in document.Modes)
                {
                    if (mode == null)
                    {
                        continue;
                    }

                    config.Modes[mode.Mode] =
                        new ModeSetting(mode.SetpointPa, mode.BandPa, mode.TimeSec);
                }
            }

            if (document.Chains == null || document.Chains.Count == 0)
            {
                // 통로가 없으면 제어할 대상이 없다. 기본값으로 조용히 채우면
                // 파일을 고쳤는데 아무 일도 일어나지 않는 상태가 된다.
                errors.Add("기류 순환 통로가 하나도 정의되어 있지 않습니다.");
                return config;
            }

            config.Chains = new List<ChainDefinition>(document.Chains);

            int enabled = 0;

            foreach (ChainDefinition chain in config.Chains)
            {
                if (chain != null && chain.Enabled)
                {
                    enabled++;
                }
            }

            if (enabled == 0)
            {
                // 오류는 아니다. 정비 중 전 통로를 꺼 두는 경우가 있다.
                // 다만 조용히 넘어가면 "왜 아무것도 안 도는지" 를 찾게 된다.
                warnings.Add("활성 상태인 기류 순환 통로가 하나도 없습니다. 자동 운전이 성립하지 않습니다.");
            }

            return config;
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

    /// <summary>
    /// <c>control.json</c> 원문에서 편집 가능한 값만 바꿔 쓴다.
    /// </summary>
    /// <remarks>
    /// 이 파일의 주석에는 dwell 을 두는 이유, 팬 최소 회전수가 매뉴얼 값이라는 사실,
    /// 센서 1 이 통로와 1:1 이 아닌 이유가 적혀 있다.
    /// 다른 설정 파일과 같은 이유로 <see cref="JsonTextScanner"/> 를 쓴다.
    /// </remarks>
    public static class ControlDocumentEditor
    {
        /// <summary>편집 결과를 원문에 반영한다.</summary>
        /// <param name="json">원문.</param>
        /// <param name="config">편집된 제어 설정.</param>
        /// <param name="result">수정된 원문. 실패 시 null.</param>
        /// <param name="error">실패 사유. 성공 시 null.</param>
        /// <returns>성공하면 true.</returns>
        public static bool TryApply(
            string json, ControlConfig config, out string result, out string error)
        {
            result = null;

            if (config == null)
            {
                error = "제어 설정이 없습니다.";
                return false;
            }

            JsonTextObject root;

            if (!JsonTextScanner.TryScan(json, out root, out error))
            {
                return false;
            }

            JsonTextPatch patch = new JsonTextPatch();

            patch.SetString(root, "activeMode", config.ActiveMode.ToString(), true);
            patch.SetNumber(root, "filterWindowSize", config.FilterWindowSize, true);

            ApplyActuators(root, config, patch);

            if (!ApplyChains(root, config, patch, out error))
            {
                return false;
            }

            result = patch.Apply(json);
            return true;
        }

        /// <summary>액추에이터 파라미터를 반영한다.</summary>
        /// <param name="root">최상위 객체.</param>
        /// <param name="config">제어 설정.</param>
        /// <param name="patch">치환 목록.</param>
        private static void ApplyActuators(
            JsonTextObject root, ControlConfig config, JsonTextPatch patch)
        {
            JsonTextObject valve = root.Object("valve");

            if (valve != null && config.Valve != null)
            {
                patch.SetNumber(valve, "stepPulse", config.Valve.StepPulse, true);
                patch.SetNumber(valve, "minPulse", config.Valve.MinPulse, true);
                patch.SetNumber(valve, "maxPulse", config.Valve.MaxPulse, true);
                patch.SetNumber(valve, "dwellMs", config.Valve.DwellMs, true);
                patch.SetNumber(valve, "positionTolerancePulse", config.Valve.PositionTolerancePulse, true);
            }

            JsonTextObject fan = root.Object("fan");

            if (fan != null && config.Fan != null)
            {
                patch.SetNumber(fan, "stepRpm", config.Fan.StepRpm, true);
                patch.SetNumber(fan, "minRpm", config.Fan.MinRpm, true);
                patch.SetNumber(fan, "maxRpm", config.Fan.MaxRpm, true);
                patch.SetNumber(fan, "dwellMs", config.Fan.DwellMs, true);
                patch.SetNumber(fan, "rpmTolerance", config.Fan.RpmTolerance, true);
            }
        }

        /// <summary>통로 활성화를 반영한다.</summary>
        /// <param name="root">최상위 객체.</param>
        /// <param name="config">제어 설정.</param>
        /// <param name="patch">치환 목록.</param>
        /// <param name="error">실패 사유(출력).</param>
        /// <returns>성공하면 true.</returns>
        private static bool ApplyChains(
            JsonTextObject root, ControlConfig config, JsonTextPatch patch, out string error)
        {
            error = null;

            Dictionary<string, JsonTextObject> byId =
                new Dictionary<string, JsonTextObject>(StringComparer.Ordinal);

            foreach (JsonTextObject chain in root.Array("chains"))
            {
                JsonTextSpan id = chain.Value("id");

                if (id != null)
                {
                    byId[id.Text] = chain;
                }
            }

            foreach (ChainDefinition chain in config.Chains)
            {
                if (chain == null)
                {
                    continue;
                }

                string key = chain.Id.ToString(CultureInfo.InvariantCulture);
                JsonTextObject span;

                if (!byId.TryGetValue(key, out span))
                {
                    error = string.Format(
                        CultureInfo.InvariantCulture,
                        "통로 {0} 이 파일에 없습니다. 파일이 외부에서 변경되었습니다.",
                        chain.Id);

                    return false;
                }

                // enabled 는 true 가 기본값이라 원문에서 생략되어 있다. 끌 때만 넣는다.
                patch.SetBoolean(span, "enabled", chain.Enabled, !chain.Enabled);
            }

            return true;
        }
    }
}
