using System;
using System.Collections.Generic;
using System.Globalization;
using Esam.Domain.Configuration;
using Esam.Domain.Control;
using Esam.Domain.Models;

namespace Esam.Domain.Alarms
{
    /// <summary>
    /// 선언된 <see cref="AlarmRule"/> 목록을 스냅샷에 적용해 알람 발생/해제를 판정한다.
    /// </summary>
    /// <remarks>
    /// <para>알람 엔진은 인터록과 달리 제어 루프 주기(기본 200ms)로 호출되면 충분하다.
    /// 안전 기능이 아니라 통보 기능이기 때문이다.</para>
    /// <para>이 클래스는 규칙별 <see cref="AlarmState"/> 를 내부에 유지하므로 스레드 안전하지 않다.
    /// 알람 엔진 스레드 1개에서만 <see cref="Evaluate"/> 를 호출해야 한다.</para>
    /// </remarks>
    public sealed class AlarmEvaluator
    {
        private readonly IDictionary<string, AlarmState> _states;
        private readonly IList<AlarmRule> _rules;

        /// <summary>등록된 규칙 수.</summary>
        public int RuleCount
        {
            get { return _rules.Count; }
        }

        /// <summary>알람 평가기를 생성한다.</summary>
        /// <param name="rules">알람 규칙 목록.</param>
        /// <exception cref="ArgumentNullException">규칙 목록이 null 일 때.</exception>
        public AlarmEvaluator(IEnumerable<AlarmRule> rules)
        {
            if (rules == null)
            {
                throw new ArgumentNullException("rules");
            }

            _rules = new List<AlarmRule>();
            _states = new Dictionary<string, AlarmState>(StringComparer.OrdinalIgnoreCase);

            foreach (AlarmRule rule in rules)
            {
                if (rule == null || string.IsNullOrEmpty(rule.Code))
                {
                    continue;
                }

                _rules.Add(rule);
                _states[rule.Code] = new AlarmState(rule);
            }
        }

        /// <summary>모든 규칙을 판정하고 새로 발생한 알람 목록을 반환한다.</summary>
        /// <param name="snapshot">현재 시스템 스냅샷.</param>
        /// <param name="config">제어 설정(OutOfBand 판정에 필요).</param>
        /// <param name="nowUtc">현재 시각(UTC).</param>
        /// <returns>이번 스캔에서 새로 발생한 알람의 상태 목록.</returns>
        public IList<AlarmState> Evaluate(SystemSnapshot snapshot, ControlConfig config, DateTime nowUtc)
        {
            List<AlarmState> newlyRaised = new List<AlarmState>();

            if (snapshot == null)
            {
                return newlyRaised;
            }

            SnapshotValueResolver resolver = new SnapshotValueResolver(snapshot);

            foreach (AlarmRule rule in _rules)
            {
                if (!rule.Enabled)
                {
                    continue;
                }

                AlarmState state = _states[rule.Code];

                double value;
                string detail;
                bool met = TestCondition(rule, resolver, config, out value, out detail);

                if (state.Update(met, value, detail, nowUtc))
                {
                    newlyRaised.Add(state);
                }
            }

            return newlyRaised;
        }

        /// <summary>현재 활성 상태인 알람 요약을 만든다.</summary>
        /// <returns>알람 요약.</returns>
        public AlarmSummary BuildSummary()
        {
            List<string> activeCodes = new List<string>();
            AlarmSeverity highest = AlarmSeverity.None;
            bool hasUnacknowledged = false;

            foreach (AlarmRule rule in _rules)
            {
                AlarmState state = _states[rule.Code];
                if (!state.IsActive)
                {
                    continue;
                }

                activeCodes.Add(rule.Code);

                if (rule.Severity > highest)
                {
                    highest = rule.Severity;
                }

                if (!state.IsAcknowledged)
                {
                    hasUnacknowledged = true;
                }
            }

            return new AlarmSummary(activeCodes, hasUnacknowledged, highest);
        }

        /// <summary>지정 코드의 알람 상태를 조회한다.</summary>
        /// <param name="code">알람 코드.</param>
        /// <returns>알람 상태. 없으면 null.</returns>
        public AlarmState FindState(string code)
        {
            AlarmState state;
            return _states.TryGetValue(code ?? string.Empty, out state) ? state : null;
        }

        /// <summary>모든 활성 알람을 확인(Ack) 처리한다.</summary>
        public void AcknowledgeAll()
        {
            foreach (KeyValuePair<string, AlarmState> pair in _states)
            {
                if (pair.Value.IsActive)
                {
                    pair.Value.Acknowledge();
                }
            }
        }

        /// <summary>
        /// Manual 정책 알람을 해제한다. 조건이 아직 성립 중이면 다음 스캔에서 다시 발생한다.
        /// </summary>
        /// <param name="code">해제할 알람 코드. null 이면 전체.</param>
        public void Reset(string code)
        {
            if (string.IsNullOrEmpty(code))
            {
                foreach (KeyValuePair<string, AlarmState> pair in _states)
                {
                    pair.Value.Reset();
                }

                return;
            }

            AlarmState state;
            if (_states.TryGetValue(code, out state))
            {
                state.Reset();
            }
        }

        /// <summary>규칙 1건의 조건 성립 여부를 판정한다.</summary>
        /// <param name="rule">알람 규칙.</param>
        /// <param name="resolver">값 해석기.</param>
        /// <param name="config">제어 설정.</param>
        /// <param name="value">판정에 사용한 값(출력).</param>
        /// <param name="detail">사유 설명(출력).</param>
        /// <returns>조건이 성립하면 true.</returns>
        private static bool TestCondition(
            AlarmRule rule,
            IAlarmValueResolver resolver,
            ControlConfig config,
            out double value,
            out string detail)
        {
            value = 0.0;
            detail = null;

            switch (rule.Condition)
            {
                case AlarmConditionType.GreaterThan:
                    if (!resolver.TryGetNumeric(rule.Source, out value))
                    {
                        // 값을 읽을 수 없는 경우는 CommFail 규칙이 담당한다.
                        // 여기서 알람을 올리면 통신 알람과 중복되므로 성립하지 않은 것으로 본다.
                        return false;
                    }

                    if (value > rule.Threshold)
                    {
                        detail = Format("{0} = {1:F2} > 상한 {2:F2}", rule.Source, value, rule.Threshold);
                        return true;
                    }

                    return false;

                case AlarmConditionType.LessThan:
                    if (!resolver.TryGetNumeric(rule.Source, out value))
                    {
                        return false;
                    }

                    if (value < rule.Threshold)
                    {
                        detail = Format("{0} = {1:F2} < 하한 {2:F2}", rule.Source, value, rule.Threshold);
                        return true;
                    }

                    return false;

                case AlarmConditionType.OutOfBand:
                    return TestOutOfBand(rule, resolver, config, out value, out detail);

                case AlarmConditionType.BitSet:
                    bool bit;
                    if (!resolver.TryGetBoolean(rule.Source, out bit))
                    {
                        return false;
                    }

                    if (bit)
                    {
                        value = 1.0;
                        detail = Format("{0} 비트 ON", rule.Source);
                        return true;
                    }

                    return false;

                case AlarmConditionType.CommFail:
                    bool ignoredAlarm;
                    if (resolver.IsCommFailed(rule.Source, out ignoredAlarm))
                    {
                        detail = Format("{0} 통신 실패", rule.Source);
                        return true;
                    }

                    return false;

                case AlarmConditionType.CommFailOrAlarmCode:
                    bool deviceAlarm;
                    bool commFailed = resolver.IsCommFailed(rule.Source, out deviceAlarm);
                    if (commFailed || deviceAlarm)
                    {
                        detail = commFailed
                            ? Format("{0} 통신 실패", rule.Source)
                            : Format("{0} 장치 알람 발생", rule.Source);
                        return true;
                    }

                    return false;

                default:
                    return false;
            }
        }

        /// <summary>OutOfBand 조건을 판정한다. 참조 모드의 정상 대역을 벗어나면 성립이다.</summary>
        /// <param name="rule">알람 규칙.</param>
        /// <param name="resolver">값 해석기.</param>
        /// <param name="config">제어 설정.</param>
        /// <param name="value">판정 값(출력).</param>
        /// <param name="detail">사유 설명(출력).</param>
        /// <returns>대역을 벗어나면 true.</returns>
        private static bool TestOutOfBand(
            AlarmRule rule,
            IAlarmValueResolver resolver,
            ControlConfig config,
            out double value,
            out string detail)
        {
            value = 0.0;
            detail = null;

            if (config == null || !rule.ReferenceMode.HasValue)
            {
                return false;
            }

            ModeSetting mode;
            if (config.Modes == null
                || !config.Modes.TryGetValue(rule.ReferenceMode.Value, out mode)
                || mode == null)
            {
                return false;
            }

            if (!resolver.TryGetNumeric(rule.Source, out value))
            {
                return false;
            }

            if (mode.IsInBand(value))
            {
                return false;
            }

            detail = Format(
                "{0} = {1:F2} Pa 가 정상 대역({2:F2} ~ {3:F2} Pa)을 벗어남",
                rule.Source, value, mode.LowLimitPa, mode.HighLimitPa);
            return true;
        }

        /// <summary>로캘 무관 문자열 포맷 도우미.</summary>
        /// <param name="format">형식 문자열.</param>
        /// <param name="args">인자.</param>
        /// <returns>포맷된 문자열.</returns>
        private static string Format(string format, params object[] args)
        {
            return string.Format(CultureInfo.InvariantCulture, format, args);
        }
    }
}
