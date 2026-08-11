using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Esam.Domain.Alarms;
using Esam.Domain.Configuration;
using Newtonsoft.Json;

namespace Esam.Communication.Configuration
{
    /// <summary>알람 규칙 파일의 최상위 구조.</summary>
    public sealed class AlarmConfigDocument
    {
        /// <summary>스키마 버전.</summary>
        public string SchemaVersion { get; set; }

        /// <summary>알람 규칙 목록.</summary>
        public IList<AlarmRule> Rules { get; set; }

        /// <summary>빈 문서를 만든다.</summary>
        public AlarmConfigDocument()
        {
            Rules = new List<AlarmRule>();
        }
    }

    /// <summary>알람 규칙 로드 결과.</summary>
    public sealed class AlarmLoadResult
    {
        /// <summary>로드에 성공했는지 여부.</summary>
        public bool IsSuccess { get; private set; }

        /// <summary>로드된 규칙 목록. 실패 시 빈 목록.</summary>
        public IList<AlarmRule> Rules { get; private set; }

        /// <summary>치명적 오류 목록.</summary>
        public IList<string> Errors { get; private set; }

        /// <summary>경고 목록. 로드는 되지만 확인이 필요한 항목.</summary>
        public IList<string> Warnings { get; private set; }

        /// <summary>결과를 생성한다.</summary>
        /// <param name="rules">규칙 목록.</param>
        /// <param name="errors">오류 목록.</param>
        /// <param name="warnings">경고 목록.</param>
        public AlarmLoadResult(IList<AlarmRule> rules, IList<string> errors, IList<string> warnings)
        {
            Rules = rules ?? new List<AlarmRule>();
            Errors = errors ?? new List<string>();
            Warnings = warnings ?? new List<string>();
            IsSuccess = Errors.Count == 0;
        }
    }

    /// <summary>
    /// <c>alarms.json</c> 로더.
    /// </summary>
    /// <remarks>
    /// <para>알람 정의를 코드가 아니라 데이터로 두는 이유는 단순하다.
    /// 임계값 변경에 재컴파일이 필요하면 현장에서 손댈 수 없고, 결국 아무도 조정하지 않는다.</para>
    /// <para>검증은 <b>실패시키는 것</b>과 <b>알리는 것</b>을 구분한다.
    /// 코드 중복이나 잘못된 조건 조합은 오류다. 값을 읽을 수 없는 경로나
    /// 비활성 규칙은 경고로 남기고 로드는 통과시킨다.
    /// 명세 미확보 장치(MFC·파티클) 때문에 전체 알람이 뜨지 않는 편이 더 위험하다.</para>
    /// </remarks>
    public static class AlarmConfigLoader
    {
        /// <summary>JSON 문자열에서 규칙을 읽는다. 레시피 대조를 생략한다.</summary>
        /// <param name="json">JSON 문자열. 주석을 허용한다.</param>
        /// <returns>로드 결과.</returns>
        public static AlarmLoadResult LoadFromJson(string json)
        {
            return LoadFromJson(json, null);
        }

        /// <summary>JSON 문자열에서 규칙을 읽고 레시피와 대조한다.</summary>
        /// <param name="json">JSON 문자열. 주석을 허용한다.</param>
        /// <param name="recipe">운전 파라미터. null 이면 참조 검증을 생략한다.</param>
        /// <returns>로드 결과.</returns>
        /// <remarks>
        /// 레시피를 함께 넘겨야 <c>AboveHighLimit</c>·<c>BelowLowLimit</c> 의 참조가
        /// 끊어졌는지 알 수 있다. null 이면 그 검증을 건너뛰고 경고로 남긴다.
        /// </remarks>
        public static AlarmLoadResult LoadFromJson(string json, RecipeDefinition recipe)
        {
            List<string> errors = new List<string>();
            List<string> warnings = new List<string>();

            if (string.IsNullOrWhiteSpace(json))
            {
                errors.Add("알람 설정이 비어 있습니다.");
                return new AlarmLoadResult(null, errors, warnings);
            }

            AlarmConfigDocument document;

            try
            {
                document = JsonConvert.DeserializeObject<AlarmConfigDocument>(
                    json, CommunicationConfigLoader.CreateSettings());
            }
            catch (JsonException ex)
            {
                errors.Add("알람 설정 구문 오류: " + ex.Message);
                return new AlarmLoadResult(null, errors, warnings);
            }

            if (document == null || document.Rules == null || document.Rules.Count == 0)
            {
                errors.Add("알람 규칙이 하나도 정의되어 있지 않습니다.");
                return new AlarmLoadResult(null, errors, warnings);
            }

            Validate(document.Rules, recipe, errors, warnings);

            return new AlarmLoadResult(document.Rules, errors, warnings);
        }

        /// <summary>파일에서 규칙을 읽는다. 레시피 대조를 생략한다.</summary>
        /// <param name="path">파일 경로.</param>
        /// <returns>로드 결과.</returns>
        public static AlarmLoadResult LoadFromFile(string path)
        {
            return LoadFromFile(path, null);
        }

        /// <summary>파일에서 규칙을 읽고 레시피와 대조한다.</summary>
        /// <param name="path">파일 경로.</param>
        /// <param name="recipe">운전 파라미터. null 이면 참조 검증을 생략한다.</param>
        /// <returns>로드 결과.</returns>
        public static AlarmLoadResult LoadFromFile(string path, RecipeDefinition recipe)
        {
            List<string> errors = new List<string>();

            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                errors.Add(Format("알람 설정 파일을 찾을 수 없습니다: {0}", path));
                return new AlarmLoadResult(null, errors, null);
            }

            try
            {
                return LoadFromJson(File.ReadAllText(path), recipe);
            }
            catch (IOException ex)
            {
                errors.Add("알람 설정 파일 읽기 실패: " + ex.Message);
                return new AlarmLoadResult(null, errors, null);
            }
            catch (UnauthorizedAccessException ex)
            {
                errors.Add("알람 설정 파일 접근 거부: " + ex.Message);
                return new AlarmLoadResult(null, errors, null);
            }
        }

        /// <summary>규칙 목록을 검증한다.</summary>
        /// <param name="rules">규칙 목록.</param>
        /// <param name="recipe">운전 파라미터. null 이면 참조 검증을 생략한다.</param>
        /// <param name="errors">오류 목록(출력).</param>
        /// <param name="warnings">경고 목록(출력).</param>
        private static void Validate(
            IList<AlarmRule> rules,
            RecipeDefinition recipe,
            IList<string> errors,
            IList<string> warnings)
        {
            bool anyRecipeCondition = false;
            HashSet<string> codes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int enabledCount = 0;

            foreach (AlarmRule rule in rules)
            {
                if (rule == null)
                {
                    errors.Add("null 규칙 항목이 있습니다.");
                    continue;
                }

                string error;

                if (!rule.Validate(out error))
                {
                    errors.Add(Format("알람 {0}: {1}", rule.Code ?? "(코드 없음)", error));
                    continue;
                }

                // 코드가 겹치면 나중 것이 앞의 것을 덮어 하나가 조용히 사라진다.
                if (!codes.Add(rule.Code))
                {
                    errors.Add(Format("알람 코드 중복: {0}", rule.Code));
                }

                if (rule.Enabled)
                {
                    enabledCount++;
                }
                else
                {
                    warnings.Add(Format("알람 {0}({1})이 비활성 상태입니다.", rule.Code, rule.Name));
                }

                // OutOfBand + referenceMode 누락은 AlarmRule.Validate 가 이미 잡는다.

                // 폴링 주기보다 짧은 디바운스는 의미가 없다.
                // 0 은 "즉시" 라는 명시적 의도이므로 통과시킨다.
                if (rule.DebounceMs > 0.0 && rule.DebounceMs < 250.0)
                {
                    warnings.Add(Format(
                        "알람 {0}: 디바운스 {1:F0} ms 는 폴링 주기(250 ms)보다 짧아 효과가 없습니다.",
                        rule.Code, rule.DebounceMs));
                }

                // ── 경로 검증: 해석할 수 없는 경로는 영원히 울리지 않는다 ──────
                // "값이 없다" 와 "경로를 모른다" 는 해석기 반환값으로 구분되지 않으므로
                // 여기서 형식을 확인해야 한다. 오타 하나로 안전 통보가 사라진다.
                //
                // 비활성 규칙은 검사하지 않는다. 소스가 아직 없어서 비활성인 것들이며
                // (AL-01 SECS/GEM, AL-66 PC 온도) 그 사실은 별도 경고로 이미 드러난다.
                if (rule.Enabled && !SnapshotValueResolver.IsSupportedPath(rule.Source))
                {
                    errors.Add(Format(
                        "알람 {0}: 판정 대상 '{1}' 을 해석할 수 없습니다. "
                        + "이 알람은 등록되지만 영원히 발생하지 않습니다.",
                        rule.Code, rule.Source));
                }

                bool usesRecipe = rule.Condition == AlarmConditionType.AboveHighLimit
                                  || rule.Condition == AlarmConditionType.BelowLowLimit;

                if (usesRecipe)
                {
                    anyRecipeCondition = true;
                }

                // ── 검증 4: 레시피 참조가 살아 있는가 ───────────────────────────
                // 끊어지면 알람이 등록됐는데 영원히 울리지 않는다.
                // 화면의 알람 목록에는 정상으로 보이므로 아무도 모른다.
                if (usesRecipe && recipe != null)
                {
                    string deviceId;

                    if (SnapshotValueResolver.TryGetDeviceId(rule.Source, out deviceId)
                        && recipe.Find(deviceId) == null)
                    {
                        errors.Add(Format(
                            "알람 {0}: 대상 센서 {1} 가 레시피에 없습니다. "
                            + "임계값을 가져올 수 없어 이 알람은 영원히 발생하지 않습니다.",
                            rule.Code, deviceId));
                    }
                }

                // ── 검증 5: 임계값이 두 곳에 있는가 ─────────────────────────────
                // 레시피가 관리하는 센서인데 규칙이 직접 threshold 를 쓰면
                // 어느 쪽이 적용되는지 알 수 없다. 예외적 필요가 있을 수 있어
                // 막지는 않고 드러낸다.
                if (recipe != null && !usesRecipe && !rule.IndependentThreshold
                    && (rule.Condition == AlarmConditionType.GreaterThan
                        || rule.Condition == AlarmConditionType.LessThan))
                {
                    string deviceId;

                    if (SnapshotValueResolver.TryGetDeviceId(rule.Source, out deviceId)
                        && recipe.Find(deviceId) != null)
                    {
                        warnings.Add(Format(
                            "알람 {0}: 센서 {1} 은 레시피가 임계값을 관리하는데 "
                            + "규칙이 threshold({2:F2})를 직접 씁니다. 값이 두 곳에 생깁니다. "
                            + "의도한 것이면 independentThreshold 를 true 로 두십시오.",
                            rule.Code, deviceId, rule.Threshold));
                    }
                }
            }

            if (enabledCount == 0)
            {
                errors.Add("활성 상태인 알람 규칙이 하나도 없습니다.");
            }

            // 레시피 없이 로드하면 검증 4·5 를 수행하지 못한다.
            // 조용히 넘어가면 참조가 끊어진 규칙을 검증했다고 착각한다.
            if (anyRecipeCondition && recipe == null)
            {
                warnings.Add(
                    "레시피 없이 알람 설정을 로드했습니다. "
                    + "임계값을 레시피에서 가져오는 규칙의 참조 검증을 건너뜁니다.");
            }
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
