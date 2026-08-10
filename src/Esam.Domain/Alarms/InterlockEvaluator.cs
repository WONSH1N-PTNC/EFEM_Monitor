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

        /// <summary>
        /// 측정값을 신뢰할 수 없어 판정하지 못한 체인 번호 목록.
        /// </summary>
        /// <remarks>
        /// <para><b>"발동하지 않음" 과 "판정하지 못함" 은 다르다.</b> 둘 다 Trips 가 비어 있지만,
        /// 후자는 인터록이 눈을 감고 있는 상태다. 구분하지 않으면 상위는 안전하다고 오해한다.</para>
        /// <para>정책 판단(몇 사이클까지 봐줄 것인가, 넘으면 무엇을 할 것인가)은
        /// 이 계층이 아니라 조립 루트가 한다. 여기서는 사실만 보고한다.</para>
        /// </remarks>
        public IList<int> UnjudgeableChainIds { get; private set; }

        /// <summary>판정하지 못한 체인이 있는지 여부.</summary>
        public bool HasUnjudgeableChain
        {
            get { return UnjudgeableChainIds.Count > 0; }
        }

        /// <summary>인터록 판정 결과를 생성한다.</summary>
        /// <param name="trips">발동 목록.</param>
        /// <param name="commands">실행할 지령 목록.</param>
        /// <param name="requiresSystemStop">전 체인 정지 필요 여부.</param>
        /// <param name="unjudgeableChainIds">측정값 불신으로 판정하지 못한 체인 목록.</param>
        public InterlockEvaluation(
            IList<InterlockTrip> trips,
            IList<ActuatorCommand> commands,
            bool requiresSystemStop,
            IList<int> unjudgeableChainIds)
        {
            Trips = trips ?? new List<InterlockTrip>();
            Commands = commands ?? new List<ActuatorCommand>();
            RequiresSystemStop = requiresSystemStop;
            UnjudgeableChainIds = unjudgeableChainIds ?? new List<int>();
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

        /// <summary>
        /// 상태 보호용 락.
        /// </summary>
        /// <remarks>
        /// <para>당초 설계는 "폴링 스레드 1개에서만 호출"을 전제했으나, 실제 배선은
        /// <b>포트마다 워커 스레드가 하나</b>이고 각 워커가 폴링 완료 시점에 판정을 호출한다.
        /// 즉 3개 스레드가 <see cref="_latched"/> 를 동시에 변형한다.</para>
        /// <para>이것은 이론적 위험이 아니다. <see cref="HashSet{T}"/> 는 확장 중 동시 삽입에
        /// 항목을 잃거나 버킷이 깨진다. 래치 항목이 사라지면 발동이 더 이상 보고되지 않아
        /// 상태머신은 인터록 해제로 판단하고, <b>위험이 남은 채 Ready 로 복귀한다.</b>
        /// 또 UI 스레드의 <see cref="Reset"/> 이 열거 중인 집합을 수정해
        /// <see cref="InvalidOperationException"/> 을 던질 수 있다.</para>
        /// <para>락 경합 비용은 마이크로초 단위이므로 250ms 폴링 예산에 영향이 없다.
        /// 안전 기능에서 락을 아끼는 것은 잘못된 최적화다.</para>
        /// </remarks>
        private readonly object _gate = new object();

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
            get { lock (_gate) { return _latched.Count > 0; } }
        }

        /// <summary>등록된 규칙 수.</summary>
        public int RuleCount
        {
            get { return _rules.Count; }
        }

        /// <summary>지정 ID 의 규칙을 찾는다.</summary>
        /// <param name="ruleId">규칙 ID.</param>
        /// <returns>규칙. 없으면 null.</returns>
        public InterlockRule FindRule(string ruleId)
        {
            if (string.IsNullOrEmpty(ruleId))
            {
                return null;
            }

            InterlockRule rule;
            return _rules.TryGetValue(ruleId, out rule) ? rule : null;
        }

        /// <summary>
        /// 규칙 구성의 미확정 항목을 수집한다.
        /// </summary>
        /// <param name="warnings">경고 목록(출력). 추가된 건수를 세려면 호출 전 개수를 기억해 둔다.</param>
        /// <remarks>
        /// 검증 실패가 아니라 <b>경고</b>다. 미확정 상태에서도 폴백값으로 안전 기능은 동작해야 하며,
        /// 다만 그 사실이 화면과 로그에 드러나야 한다. 조용히 비활성화하는 것이 가장 위험하다.
        /// </remarks>
        public void CollectWarnings(IList<string> warnings)
        {
            if (warnings == null)
            {
                return;
            }

            foreach (KeyValuePair<string, InterlockRule> pair in _rules)
            {
                InterlockRule rule = pair.Value;

                if (!rule.Enabled)
                {
                    warnings.Add(Format(
                        "인터록 {0}({1})이 비활성 상태입니다. 안전 기능이 동작하지 않습니다.",
                        rule.Id, rule.Name));

                    continue;
                }

                if (string.Equals(rule.Id, "IL-01", StringComparison.OrdinalIgnoreCase)
                    && !rule.ThresholdPa.HasValue)
                {
                    // 이 상태로 운전하면 전원 투입 직후 래치되어 기동이 불가능하다.
                    // 경고가 아니라 사실상 구성 오류에 가깝다.
                    warnings.Add(Format(
                        "인터록 {0} 임계값이 지정되지 않았습니다. 운전 대역 상한을 폴백으로 쓰면 "
                        + "밸브 닫힘 상태에서 이미 발동 조건이 성립해 기동이 불가능해집니다.",
                        rule.Id));
                }
            }
        }

        /// <summary>
        /// DESIGN.md 5.2 표의 기본 인터록 규칙 집합을 만든다.
        /// 설정 파일이 없거나 로드에 실패해도 안전 기능이 동작하도록 하는 최후 방어선이다.
        /// </summary>
        /// <returns>기본 규칙 목록.</returns>
        public static IList<InterlockRule> CreateDefaultRules()
        {
            List<InterlockRule> rules = new List<InterlockRule>();

            // IL-01 은 자동 운전 중에만 무장한다.
            // 정지 상태(밸브 닫힘·팬 정지)에서는 센서 3 압력이 대기압 쪽으로 완화되어
            // 발동 조건이 본래 참이 되므로, 항상 무장하면 전원 투입 직후 래치되어
            // 장비가 기동 불가 상태가 된다. 그 상태에서는 보호할 대상도 없다.
            rules.Add(new InterlockRule
            {
                Id = "IL-01",
                Name = "센서 3 상한 도달 → 밸브 Close + 팬 OFF",
                Scope = InterlockScope.Chain,
                Enabled = true,
                ResetPolicy = AlarmResetPolicy.Manual,
                ClearHysteresisPa = 20.0,

                // 0 Pa = 대기압. 배기 덕트가 음압을 잃는 순간이 IL-01 이 막으려는 사건이다.
                // 운전 대역 상한(-100 Pa)을 쓰면 밸브 닫힘 상태(-50 Pa)에서 이미 조건이 참이라
                // 전원 투입 직후 래치되어 장비가 기동하지 못한다(Open Issue #21).
                ThresholdPa = 0.0
            });

            rules.Add(new InterlockRule
            {
                Id = "IL-02",
                Name = "EMO 작동 → 전 체인 SafeStop",
                Scope = InterlockScope.System,
                Enabled = true,
                ResetPolicy = AlarmResetPolicy.Manual
            });

            // IL-03 은 배선된 입력이 없어 비활성이다.
            // IO List_260801.xlsx 의 디지털 입력 8점(0x000A)에 메인 차단기 접점이 없다.
            // 규칙을 살려 두면 항상 false 를 읽어 "정상"으로 보고하므로,
            // 구현되어 동작 중이라는 착각을 준다. 비활성으로 두고 구성 경고로 드러낸다.
            rules.Add(new InterlockRule
            {
                Id = "IL-03",
                Name = "메인 차단기 OFF → 전 체인 SafeStop (입력 미배선)",
                Scope = InterlockScope.System,
                Enabled = false,
                ResetPolicy = AlarmResetPolicy.Manual
            });

            rules.Add(new InterlockRule
            {
                Id = "IL-04",
                Name = "안전 입력 신뢰 불가 → 전 체인 SafeStop",
                Scope = InterlockScope.System,
                Enabled = true,
                ResetPolicy = AlarmResetPolicy.Auto
            });

            // IL-05 도 같은 이유로 비활성이다. 도어 접점 역시 DI 8점에 없다.
            // SPARE DI 가 2점 남아 있으므로 배선되면 Enabled 만 켜면 된다(Open Issue #7).
            rules.Add(new InterlockRule
            {
                Id = "IL-05",
                Name = "도어 열림 → 전 체인 SafeStop (입력 미배선)",
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
            List<int> unjudgeable = new List<int>();
            bool systemStop = false;

            if (snapshot == null || config == null)
            {
                return new InterlockEvaluation(trips, commands, false, unjudgeable);
            }

            // 포트마다 워커 스레드가 하나이므로 이 메서드는 동시에 호출된다.
            // 래치 집합을 보호하지 않으면 발동이 소실되어 위험이 남은 채 Ready 로 복귀한다.
            lock (_gate)
            {
                // ── IL-02 / IL-03: EMO, 메인 차단기 (최우선) ─────────────────────
                // 물리 안전장치이므로 다른 어떤 조건보다 먼저 판정한다.
                systemStop |= EvaluateSystemRule(
                    "IL-02", snapshot.Plc.EmoActive, "비상정지(EMO) 작동", config, nowUtc, trips);

                systemStop |= EvaluateSystemRule(
                    "IL-03", snapshot.Plc.MainBreakerOff, "메인 차단기 OFF", config, nowUtc, trips);

                // ── IL-05: 도어 열림 (정책 확정 시 활성화) ────────────────────────
                systemStop |= EvaluateSystemRule(
                    "IL-05", snapshot.Plc.DoorOpen, "도어 열림", config, nowUtc, trips);

                // ── IL-04: 안전 입력 신뢰 불가 ────────────────────────────────────
                // Good 이 아니면 EMO·차단기 판정 자체를 신뢰할 수 없다.
                // 예전에는 Bad 만 검사했는데, 한 번도 응답하지 않은 PLC 는 영구히 NoData 로 남아
                // IL-02~IL-05 전부가 무력화되었다. NoData·Stale·Uncertain 도 같이 잡는다.
                //
                // 단, PLC 가 아직 구성에 없는 단계에서는 이 규칙이 항상 발동해 아무것도
                // 검증할 수 없게 된다. 그래서 안전 입력이 구성되어 있을 때만 판정한다.
                // "구성되지 않았다"는 사실 자체는 런타임 조립 경고로 보고한다.
                systemStop |= EvaluateSystemRule(
                    "IL-04",
                    config.SafetyInputsConfigured && snapshot.Plc.Quality != Quality.Good,
                    "PLC 통신 상실 — 안전 입력 판정 불가", config, nowUtc, trips);

                if (systemStop)
                {
                    // 전체 정지 시에는 체인별 판정을 생략하고 모든 액추에이터에 정지 지령을 낸다.
                    AppendStopCommands(config.Chains, commands, "인터록: 전 체인 안전 정지");
                    return new InterlockEvaluation(trips, commands, true, unjudgeable);
                }

                // ── IL-01: 센서 3 상한 도달 (체인 단위) ───────────────────────────
                bool il01SystemWide =
                    EvaluateSensor3HighLimit(snapshot, config, nowUtc, trips, commands, unjudgeable);

                return new InterlockEvaluation(trips, commands, il01SystemWide, unjudgeable);
            }
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
            List<ActuatorCommand> commands,
            List<int> unjudgeable)
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

            // 안전 임계값은 규칙에 명시된 값을 쓴다.
            // 폴백(운전 대역 상한)은 작업자의 Config 조작에 따라 움직이고 정지 상태를 포함하므로
            // 인터록으로 성립하지 않는다. 기본 규칙은 0 Pa 를 명시한다(Open Issue #21).
            double tripThreshold = rule.ThresholdPa ?? sensor3.HighLimitPa;

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
                bool latched = _latched.Contains(key);

                PressureReading reading = snapshot.FindPressure(chain.Sensor3Id);

                // 품질 표시만으로는 부족하다. SnapshotBuilder 의 Stale 임계값은 Slow 티어까지
                // 덮어야 해서 15초로 잡혀 있어, Fast 센서가 14초 갱신되지 않아도 Good 으로 남는다.
                // 250ms 응답을 목표로 하는 안전 기능이 15초 낡은 값으로 판정해서는 안 된다.
                bool tooOld = reading != null
                              && rule.MaxDataAgeMs > 0.0
                              && (nowUtc - reading.LastUpdateUtc).TotalMilliseconds > rule.MaxDataAgeMs;

                if (reading == null || reading.Quality != Quality.Good || tooOld)
                {
                    // "발동하지 않음" 이 아니라 "판정하지 못함" 이다. 상위가 구분할 수 있어야 한다.
                    unjudgeable.Add(chain.Id);

                    // 센서 3 을 읽을 수 없으면 새로 발동시킬 수는 없다(오동작 방지).
                    // 다만 이미 래치된 인터록을 값 없음으로 풀어주지도 않는다.
                    if (latched)
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
            // UI 스레드에서 호출된다. 락 없이 _latched 를 열거하면
            // 폴링 스레드의 Add 와 겹쳐 InvalidOperationException 이 발생한다.
            lock (_gate)
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
