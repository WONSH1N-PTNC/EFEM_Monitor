using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Esam.Domain.Alarms;
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
        /// <summary>JSON 문자열에서 규칙을 읽는다.</summary>
        /// <param name="json">JSON 문자열. 주석을 허용한다.</param>
        /// <returns>로드 결과.</returns>
        public static AlarmLoadResult LoadFromJson(string json)
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

            Validate(document.Rules, errors, warnings);

            return new AlarmLoadResult(document.Rules, errors, warnings);
        }

        /// <summary>파일에서 규칙을 읽는다.</summary>
        /// <param name="path">파일 경로.</param>
        /// <returns>로드 결과.</returns>
        public static AlarmLoadResult LoadFromFile(string path)
        {
            List<string> errors = new List<string>();

            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                errors.Add(Format("알람 설정 파일을 찾을 수 없습니다: {0}", path));
                return new AlarmLoadResult(null, errors, null);
            }

            try
            {
                return LoadFromJson(File.ReadAllText(path));
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
        /// <param name="errors">오류 목록(출력).</param>
        /// <param name="warnings">경고 목록(출력).</param>
        private static void Validate(
            IList<AlarmRule> rules, IList<string> errors, IList<string> warnings)
        {
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
            }

            if (enabledCount == 0)
            {
                errors.Add("활성 상태인 알람 규칙이 하나도 없습니다.");
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
