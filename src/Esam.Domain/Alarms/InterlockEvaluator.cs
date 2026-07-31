using System;
using System.Collections.Generic;
using System.Globalization;
using Esam.Domain.Configuration;
using Esam.Domain.Control;
using Esam.Domain.Models;

namespace Esam.Domain.Alarms
{
    /// <summary>
    /// 인터록 판정 결과 전체. 발동 목록과 실행해야 할 액추에이터 지령을 함께 담는다.
    /// </summary>
    public sealed class InterlockEvaluation
    {
        /// <summary>발동(래치 포함) 중인 인터록 목록. 비어 있으면 안전 조건 없음.</summary>
        public IList<InterlockTrip> Trips { get; private set; }

        /// <summary>즉시 실행해야 할 지령 목록. 모두 <see cref="CommandPriority.Interlock"/> 우선순위이다.</summary>
        public IList<ActuatorCommand> Commands { get; private set; }

        /// <summary>인터록이 하나라도 발동했는지 여부.</summary>
        public bool HasTrip
        {
            get { return Trips.Count > 0; }
        }

        /// <summary>전 체인 정지가 필요한지 여부.</summary>
        public bool RequiresSystemStop { get; private set; }

        /// <summary>인터록 판정 결과를 생성한다.</summary>
        /// <param name="trips">발동 목록.</param>
        /// <param name="commands">실행할 지령 목록.</param>
        /// <param name="requiresSystemStop">전 체인 정지 필요 여부.</param>
        public InterlockEvaluation(
            IList<InterlockTrip> trips, IList<ActuatorCommand> commands, bool requiresSystemStop)
        {
            Trips = trips ?? new List<InterlockTrip>();
            Commands = commands ?? new List<ActuatorCommand>();
            RequiresSystemStop = requiresSystemStop;
        }
    }

    /// <summary>
    /// 인터록 판정기. DESIGN.md 5.2 의 IL-01 ~ IL-05 를 구현한다.
    /// </summary>
    /// <remarks>
    /// <para><b>이 클래스는 폴링 스레드에서 매 사이클 호출된다.</b>
    /// 서비스나 UI 를 경유하면 지연이 누적되어 안전 기능으로 성립하지 않기 때문이다.
    /// 따라서 예외를 던지지 않고, 힙 할당을 최소화하도록 작성했다.</para>
    /// <para>세 가지 상태를 내부에 유지한다.</para>
    /// <list type="number">
    ///   <item><description><b>래치(latch)</b>: <see cref="AlarmResetPolicy.Manual"/> 규칙은 물리 조건이
    ///     해소되어도 <see cref="Reset"/> 을 호출하기 전까지 발동 상태를 유지한다.
    ///     원인 확인 없이 자동 재가동되는 것을 막기 위함이다.</description></item>
    ///   <item><description><b>히스테리시스</b>: 임계값 근처에서 발동/해제가 반복되는 채터링을 막는다.
    ///     발동은 임계값 초과, 해제는 (임계값 − <see cref="InterlockRule.ClearHysteresisPa"/>) 이하일 때만 이루어진다.</description></item>
    ///   <item><description><b>Scope</b>: 규칙의 <see cref="InterlockScope"/> 설정에 따라 체인 단위/전체 정지를 결정한다.</description></item>
    /// </list>
    /// <para>상태를 가지므로 스레드 안전하지 않다. 폴링 스레드 1개에서만 호출해야 한다.</para>
    /// </remarks>
    public sealed class InterlockEvaluator
    {
        private readonly IDictionary<string, InterlockRule> _rules;

        /// <summary>래치된 인터록의 키 집합. 키 형식은 "규칙ID" 또는 "규칙ID:체인번호".</summary>
        private readonly HashSet<string> _latched;

        /// <summary>인터록 판정기를 생성한다.</summary>
        /// <param name="rules">규칙 목록. null 이면 기본 규칙 집합을 사용한다.</param>
        public InterlockEvaluator(IEnumerable<InterlockRule> rules)
        {
            _rules = new Dictionary<string, InterlockRule>(StringComparer.OrdinalIgnoreCase);
            _latched = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            IEnumerable<InterlockRule> source = rules ?? CreateDefaultRules();
            foreach (InterlockRule rule in source)
            {
                if (rule != null && !string.IsNullOrEmpty(rule.Id))
                {
                    _rules[rule.Id] = rule;
                }
            }
        }

        /// <summary>현재 래치되어 있는 인터록이 하나라도 있는지 여부.</summary>
        public bool HasLatched
        {
            get { return _latched.Count > 0; }
        }

        /// <summary>
        /// DESIGN.md 5.2 표의 기본 인터록 규칙 집합을 만든다.
        /// 설정 파일이 없거나 로드에 실패해도 안전 기능이 동작하도록 하는 최후 방어선이다.
        /// </summary>
        /// <returns>기본 규칙 목록.</returns>
        public static IList<InterlockRule> CreateDefaultRules()
        {
            List<InterlockRule> rules = new List<InterlockRule>();

            rules.Add(new InterlockRule
            {
                Id = "IL-01",
                Name = "센서 3 상한 도달 → 밸브 Close + 팬 OFF",
                Scope = InterlockScope.Chain,
                Enabled = true,
                ResetPolicy = AlarmResetPolicy.Manual,
                ClearHysteresisPa = 20.0
            });

            rules.Add(new InterlockRule
            {
                Id = "IL-02",
                Name = "EMO 작동 → 전 체인 SafeStop",
                Scope = InterlockScope.System,
                Enabled = true,
                ResetPolicy = AlarmResetPolicy.Manual
            });

            rules.Add(new InterlockRule
            {
                Id = "IL-03",
                Name = "메인 차단기 OFF → 전 체인 SafeStop",
                Scope = InterlockScope.System,
                Enabled = true,
                ResetPolicy = AlarmResetPolicy.Manual
            });

            rules.Add(new InterlockRule
            {
                Id = "IL-04",
                Name = "통신 상실 → 자동 제어 중단",
                Scope = InterlockScope.System,
                Enabled = true,
                ResetPolicy = AlarmResetPolicy.Auto
            });

            // IL-05(Door Open)는 정책 미확정이므로 기본 비활성이다(DESIGN.md Open Issue #7).
            rules.Add(new InterlockRule
            {
                Id = "IL-05",
                Name = "도어 열림 (정책 미확정)",
                Scope = InterlockScope.System,
                Enabled = false,
                ResetPolicy = AlarmResetPolicy.Auto
            });

            return rules;
        }

        /// <summary>
        /// 스냅샷을 판정해 발동해야 할 인터록과 지령을 산출한다.
        /// </summary>
        /// <param name="snapshot">현재 시스템 스냅샷.</param>
        /// <param name="config">제어 설정(체인 정의와 센서3 임계값 참조).</param>
        /// <param name="nowUtc">현재 시각(UTC).</param>
        /// <returns>판정 결과.</returns>
        public InterlockEvaluation Evaluate(
            SystemSnapshot snapshot, ControlConfig config, DateTime nowUtc)
        {
            List<InterlockTrip> trips = new List<InterlockTrip>();
            List<ActuatorCommand> commands = new List<ActuatorCommand>();
            bool systemStop = false;

            if (snapshot == null || config == null)
            {
                return new InterlockEvaluation(trips, commands, false);
            }

            // ── IL-02 / IL-03: EMO, 메인 차단기 (최우선) ─────────────────────────
            // 물리 안전장치이므로 다른 어떤 조건보다 먼저 판정한다.
            systemStop |= EvaluateSystemRule(
                "IL-02", snapshot.Plc.EmoActive, "비상정지(EMO) 작동", config, nowUtc, trips);

            systemStop |= EvaluateSystemRule(
                "IL-03", snapshot.Plc.MainBreakerOff, "메인 차단기 OFF", config, nowUtc, trips);

            // ── IL-05: 도어 열림 (정책 확정 시 활성화) ────────────────────────────
            systemStop |= EvaluateSystemRule(
                "IL-05", snapshot.Plc.DoorOpen, "도어 열림", config, nowUtc, trips);

            // ── IL-04: 통신 상실 ──────────────────────────────────────────────────
            // PLC 품질이 Bad 이면 안전 입력(EMO/차단기) 자체를 신뢰할 수 없으므로 전체 정지한다.
            systemStop |= EvaluateSystemRule(
                "IL-04", snapshot.Plc.Quality == Quality.Bad,
                "PLC 통신 상실 — 안전 입력 판정 불가", config, nowUtc, trips);

            if (systemStop)
            {
                // 전체 정지 시에는 체인별 판정을 생략하고 모든 액추에이터에 정지 지령을 낸다.
                AppendStopCommands(config.Chains, commands, "인터록: 전 체인 안전 정지");
                return new InterlockEvaluation(trips, commands, true);
            }

            // ── IL-01: 센서 3 상한 도달 (체인 단위) ───────────────────────────────
            bool il01SystemWide = EvaluateSensor3HighLimit(snapshot, config, nowUtc, trips, commands);

            return new InterlockEvaluation(trips, commands, il01SystemWide);
        }

        /// <summary>
        /// 시스템 범위 인터록 1건을 래치 규칙에 따라 판정한다.
        /// </summary>
        /// <param name="ruleId">규칙 ID.</param>
        /// <param name="conditionMet">이번 스캔에서 물리 조건이 성립했는지.</param>
        /// <param name="reason">발동 사유.</param>
        /// <param name="config">제어 설정.</param>
        /// <param name="nowUtc">현재 시각(UTC).</param>
        /// <param name="trips">발동 목록(출력).</param>
        /// <returns>이 규칙 때문에 정지가 필요하면 true.</returns>
        private bool EvaluateSystemRule(
            string ruleId,
            bool conditionMet,
            string reason,
            ControlConfig config,
            DateTime nowUtc,
            List<InterlockTrip> trips)
        {
            InterlockRule rule;
            if (!_rules.TryGetValue(ruleId, out rule) || !rule.Enabled)
            {
                // 비활성 규칙은 래치도 남기지 않는다.
                _latched.Remove(ruleId);
                return false;
            }

            // 시스템 규칙은 디지털 입력이므로 히스테리시스가 없다.
            // 해제 조건은 단순히 "물리 조건이 더 이상 성립하지 않음"이다.
            bool tripped = ResolveLatch(ruleId, rule, conditionMet, !conditionMet);
            if (!tripped)
            {
                return false;
            }

            string detail = _latched.Contains(ruleId) && !conditionMet
                ? reason + " (조건 해소됨 — Reset 대기)"
                : reason;

            bool systemWide = rule.Scope == InterlockScope.System;
            trips.Add(new InterlockTrip(ruleId, detail, AllChainIds(config), systemWide, nowUtc));
            return true;
        }

        /// <summary>
        /// IL-01 을 판정한다. 센서 3 이 상한을 넘은 체인의 밸브를 닫고 팬을 정지시킨다.
        /// </summary>
        /// <param name="snapshot">시스템 스냅샷.</param>
        /// <param name="config">제어 설정.</param>
        /// <param name="nowUtc">현재 시각(UTC).</param>
        /// <param name="trips">발동 목록(출력).</param>
        /// <param name="commands">지령 목록(출력).</param>
        /// <returns>규칙 Scope 가 System 이고 한 체인이라도 발동했으면 true.</returns>
        private bool EvaluateSensor3HighLimit(
            SystemSnapshot snapshot,
            ControlConfig config,
            DateTime nowUtc,
            List<InterlockTrip> trips,
            List<ActuatorCommand> commands)
        {
            InterlockRule rule;
            if (!_rules.TryGetValue("IL-01", out rule) || !rule.Enabled)
            {
                return false;
            }

            ModeSetting sensor3;
            if (config.Modes == null || !config.Modes.TryGetValue(SensorMode.Sensor3, out sensor3)
                || sensor3 == null)
            {
                // 센서 3 임계값이 설정되지 않았다면 판정 자체가 불가능하다.
                // 안전 기능을 조용히 무력화하지 않도록 호출측이 설정 검증에서 걸러야 한다.
                return false;
            }

            if (config.Chains == null)
            {
                return false;
            }

            double tripThreshold = sensor3.HighLimitPa;

            // 채터링 방지: 해제는 발동 임계값보다 히스테리시스만큼 낮아져야 이루어진다.
            double clearThreshold = tripThreshold - Math.Abs(rule.ClearHysteresisPa);

            bool anyTripped = false;
            bool systemWide = rule.Scope == InterlockScope.System;

            foreach (ChainDefinition chain in config.Chains)
            {
                if (chain == null || !chain.Enabled)
                {
                    continue;
                }

                string key = "IL-01:" + chain.Id.ToString(CultureInfo.InvariantCulture);

                PressureReading reading = snapshot.FindPressure(chain.Sensor3Id);
                if (reading == null || reading.Quality != Quality.Good)
                {
                    // 센서 3 을 읽을 수 없으면 새로 발동시킬 수는 없다(오동작 방지).
                    // 다만 이미 래치된 인터록을 값 없음으로 풀어주지도 않는다.
                    if (_latched.Contains(key))
                    {
                        anyTripped = true;

                        string holdReason = Format(
                            "체인 {0} 인터록 래치 유지 (센서 {1} 판독 불가)", chain.Id, chain.Sensor3Id);

                        // 지령만 내고 Trip 을 기록하지 않으면 상태머신·UI 는 "인터록 없음"으로 보게 된다.
                        // 액추에이터는 강제 정지 중인데 화면은 정상으로 보이는 위험한 불일치이므로 반드시 함께 기록한다.
                        trips.Add(new InterlockTrip(
                            "IL-01", holdReason, new List<int> { chain.Id }, systemWide, nowUtc));
                        AppendChainStop(chain, commands, holdReason);
                    }

                    continue;
                }

                bool raiseCondition = reading.Pa > tripThreshold;
                bool clearCondition = reading.Pa <= clearThreshold;

                if (!ResolveLatch(key, rule, raiseCondition, clearCondition))
                {
                    continue;
                }

                anyTripped = true;

                string reason = Format(
                    "센서 {0} 상한 초과: {1:F1} Pa > {2:F1} Pa (해제 기준 {3:F1} Pa)",
                    chain.Sensor3Id, reading.Pa, tripThreshold, clearThreshold);

                trips.Add(new InterlockTrip(
                    "IL-01", reason, new List<int> { chain.Id }, systemWide, nowUtc));

                // 인터록 표 그대로: 스로틀밸브 Close + 송풍팬 OFF
                AppendChainStop(chain, commands, reason);
            }

            if (anyTripped && systemWide)
            {
                // Scope 가 System 으로 설정되어 있으면 발동 체인 외 나머지도 함께 정지시킨다.
                AppendStopCommands(config.Chains, commands, "인터록 IL-01(Scope=System): 전 체인 안전 정지");
                return true;
            }

            return false;
        }

        /// <summary>
        /// 래치 규칙을 적용해 최종 발동 여부를 결정한다.
        /// </summary>
        /// <param name="key">래치 키.</param>
        /// <param name="rule">규칙.</param>
        /// <param name="raiseCondition">발동 조건 성립 여부.</param>
        /// <param name="clearCondition">해제 조건 성립 여부(히스테리시스 반영).</param>
        /// <returns>발동 상태이면 true.</returns>
        private bool ResolveLatch(
            string key, InterlockRule rule, bool raiseCondition, bool clearCondition)
        {
            if (raiseCondition)
            {
                _latched.Add(key);
                return true;
            }

            if (!_latched.Contains(key))
            {
                return false;
            }

            // 이미 래치된 상태. Manual 정책이면 Reset 전까지 유지한다.
            if (rule.ResetPolicy == AlarmResetPolicy.Manual)
            {
                return true;
            }

            // Auto 정책은 히스테리시스를 넘어 확실히 회복되었을 때만 해제한다.
            if (clearCondition)
            {
                _latched.Remove(key);
                return false;
            }

            return true;
        }

        /// <summary>
        /// 래치된 인터록을 해제한다. 물리 조건이 아직 성립 중이면 다음 스캔에서 다시 발동한다.
        /// </summary>
        /// <param name="ruleId">해제할 규칙 ID. null 또는 빈 문자열이면 전체 해제.</param>
        public void Reset(string ruleId)
        {
            if (string.IsNullOrEmpty(ruleId))
            {
                _latched.Clear();
                return;
            }

            _latched.Remove(ruleId);

            // 체인 단위 래치("IL-01:3")도 함께 제거한다.
            string prefix = ruleId + ":";
            List<string> toRemove = new List<string>();
            foreach (string key in _latched)
            {
                if (key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    toRemove.Add(key);
                }
            }

            foreach (string key in toRemove)
            {
                _latched.Remove(key);
            }
        }

        /// <summary>체인 1조에 정지 지령(밸브 Close + 팬 OFF)을 추가한다.</summary>
        /// <param name="chain">체인 정의.</param>
        /// <param name="commands">지령 목록(출력).</param>
        /// <param name="reason">지령 사유.</param>
        private static void AppendChainStop(
            ChainDefinition chain, List<ActuatorCommand> commands, string reason)
        {
            if (!string.IsNullOrEmpty(chain.ValveId))
            {
                commands.Add(ActuatorCommand.CloseValve(
                    chain.ValveId, CommandPriority.Interlock, reason));
            }

            if (!string.IsNullOrEmpty(chain.FanId))
            {
                commands.Add(ActuatorCommand.StopFan(
                    chain.FanId, CommandPriority.Interlock, reason));
            }
        }

        /// <summary>모든 체인에 정지 지령을 추가한다.</summary>
        /// <param name="chains">체인 정의 목록.</param>
        /// <param name="commands">지령 목록(출력).</param>
        /// <param name="reason">지령 사유.</param>
        private static void AppendStopCommands(
            IList<ChainDefinition> chains, List<ActuatorCommand> commands, string reason)
        {
            if (chains == null)
            {
                return;
            }

            foreach (ChainDefinition chain in chains)
            {
                if (chain == null)
                {
                    continue;
                }

                // Enabled 여부와 무관하게 정지시킨다. 안전 정지는 예외를 두지 않는다.
                AppendChainStop(chain, commands, reason);
            }
        }

        /// <summary>모든 체인 번호 목록을 만든다.</summary>
        /// <param name="config">제어 설정.</param>
        /// <returns>체인 번호 목록.</returns>
        private static IList<int> AllChainIds(ControlConfig config)
        {
            List<int> ids = new List<int>();
            if (config.Chains != null)
            {
                foreach (ChainDefinition chain in config.Chains)
                {
                    if (chain != null)
                    {
                        ids.Add(chain.Id);
                    }
                }
            }

            return ids;
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
