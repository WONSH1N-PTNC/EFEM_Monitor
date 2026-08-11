using System;
using System.Collections.Generic;
using System.Globalization;
using Esam.Communication.Abstractions;
using Esam.Communication.Configuration;
using Esam.Communication.Modbus;
using Esam.Communication.Polling;
using Esam.Domain;
using Esam.Domain.Alarms;
using Esam.Domain.Configuration;
using Esam.Domain.Control;
using Esam.Domain.Models;

namespace Esam.Services
{
    /// <summary>전송 계층 선택.</summary>
    public enum TransportMode
    {
        /// <summary>실제 RS-485 시리얼 포트를 사용한다.</summary>
        Serial = 0,

        /// <summary>
        /// 가상 플랜트 시뮬레이션을 사용한다.
        /// 하드웨어·레지스터 명세 없이 상위 전체를 검증할 수 있다.
        /// </summary>
        Simulation = 1
    }

    /// <summary>런타임 구성 옵션.</summary>
    public sealed class RuntimeOptions
    {
        /// <summary>전송 계층 선택.</summary>
        public TransportMode Transport { get; set; }

        /// <summary>센서 1 디바이스 ID 목록.</summary>
        public IList<string> Sensor1Ids { get; set; }

        /// <summary>
        /// 알람 규칙 목록. null 이면 <see cref="AlarmRulesPath"/> 에서 읽는다.
        /// </summary>
        /// <remarks>
        /// 테스트에서 규칙 몇 개만 넣고 검증할 때 직접 지정한다.
        /// 실제 운전에서는 파일 로드를 쓴다.
        /// </remarks>
        public IEnumerable<AlarmRule> AlarmRules { get; set; }

        /// <summary>
        /// 알람 규칙 파일 경로. 기본값 <c>config/alarms.json</c>.
        /// </summary>
        /// <remarks>
        /// <para>종전에는 <see cref="AlarmRules"/> 가 null 이면 알람 평가를 아예 하지 않았고,
        /// 이 값을 채우는 코드가 어디에도 없었다. 결과적으로 <b>DESIGN 5.1 의 알람 31종이
        /// 어떤 구성에서도 동작하지 않았다.</b> 게다가 인터록 IL-01 의 주석은
        /// "초기 배기 불량은 대역 이탈 알람이 잡는다" 고 적어 두어,
        /// 존재하지 않는 기능에 안전 판단을 기대고 있었다.</para>
        /// <para>그래서 기본값을 파일 경로로 두고, 파일이 없으면 <b>구성 경고</b>로 드러낸다.
        /// 조용히 알람 없는 상태로 운전하는 것이 가장 위험하다.</para>
        /// </remarks>
        public string AlarmRulesPath { get; set; }

        /// <summary>인터록 규칙 목록. null 이면 기본 규칙을 사용한다.</summary>
        public IEnumerable<InterlockRule> InterlockRules { get; set; }

        /// <summary>
        /// 운전 파라미터. null 이면 <see cref="RecipePath"/> 에서 읽는다.
        /// </summary>
        /// <remarks>테스트에서 몇 개만 넣고 검증할 때 직접 지정한다.</remarks>
        public RecipeDefinition Recipe { get; set; }

        /// <summary>
        /// 레시피 파일 경로. 기본값 <c>config/recipe.json</c>.
        /// </summary>
        /// <remarks>
        /// 읽지 못하면 구성 실패로 막지 않고 <b>모드별 공통값으로 동작</b>한다.
        /// 레시피 도입 전과 같은 거동이라 제어 자체는 성립하기 때문이다.
        /// 다만 센서별 설정이 적용되지 않는다는 사실은 구성 경고로 드러낸다.
        /// </remarks>
        public string RecipePath { get; set; }

        /// <summary>시뮬레이션 난수 시드.</summary>
        public int SimulationSeed { get; set; }

        /// <summary>
        /// 시뮬레이션 시작 시 밸브를 원점 복귀 완료 상태로 둘지 여부. 기본값 false.
        /// </summary>
        /// <remarks>
        /// 기본값이 false 인 이유는 <b>전원 투입 직후 상태를 그대로 재현</b>하기 위해서다.
        /// 실장비는 원점이 미확정인 채로 켜지고, 그 상태를 거쳐야 기동 시퀀스가 검증된다.
        /// true 로 두면 원점 복귀 경로를 한 번도 지나지 않아,
        /// 시퀀스가 깨져 있어도 시뮬레이션에서는 드러나지 않는다.
        /// 원점 복귀 자체가 관심사가 아닌 시나리오에서만 true 로 둔다.
        /// </remarks>
        public bool PreHomeValves { get; set; }

        /// <summary>기본값으로 초기화한다.</summary>
        public RuntimeOptions()
        {
            Transport = TransportMode.Simulation;
            Sensor1Ids = new List<string> { "S1-1", "S1-2", "S1-3" };
            SimulationSeed = 20260805;
            AlarmRulesPath = System.IO.Path.Combine("config", "alarms.json");
            RecipePath = System.IO.Path.Combine("config", "recipe.json");
        }
    }

    /// <summary>
    /// 시스템 조립 루트. 설정을 읽어 통신·제어·알람 계층을 배선하고 수명을 관리한다.
    /// </summary>
    /// <remarks>
    /// <para>DI 컨테이너를 쓰지 않고 여기서 손으로 조립한다. 구성 요소가 열 개 미만이고
    /// 배선이 고정되어 있으므로, 컨테이너 설정을 읽는 것보다 이 코드를 읽는 편이
    /// 시스템 구조를 이해하는 데 빠르다.</para>
    /// <para><b>배선의 핵심은 스레드 경계다.</b></para>
    /// <list type="number">
    ///   <item><description>포트 워커(포트당 1스레드) → 폴링 → <c>PollCompleted</c></description></item>
    ///   <item><description>같은 스레드에서 DataStore 조립 → 인터록 즉시 판정·투입</description></item>
    ///   <item><description>제어 엔진(별도 스레드)이 주기적으로 스냅샷을 끌어가 지령 생성</description></item>
    ///   <item><description>UI(별도 스레드)가 주기적으로 <c>DataStore.Current</c> 를 끌어감</description></item>
    /// </list>
    /// <para>인터록만 폴링 스레드에 두는 이유는 지연을 최소화하기 위해서다.
    /// 나머지는 각자의 주기로 돌아 서로를 붙잡지 않는다.</para>
    /// </remarks>
    public sealed class EsamRuntime : IDisposable
    {
        private readonly Dictionary<string, IModbusTransport> _transports =
            new Dictionary<string, IModbusTransport>(StringComparer.OrdinalIgnoreCase);

        private readonly List<ModbusPortWorker> _workers = new List<ModbusPortWorker>();
        private readonly List<ConfigWarning> _warnings = new List<ConfigWarning>();

        /// <summary>
        /// 구성 경고 목록과 확인 플래그를 보호하는 락.
        /// </summary>
        /// <remarks>
        /// 경고는 조립 시점에만 생기는 것이 아니다. 안전 판정 실패·인터록 지령 실패가
        /// <b>포트 워커 스레드에서</b> 경고를 추가한다. 반대로 화면과 제어 엔진은
        /// 자기 스레드에서 목록을 읽는다. 보호하지 않으면 열거 중 추가로
        /// <c>InvalidOperationException</c> 이 발생한다.
        /// </remarks>
        private readonly object _warningGate = new object();

        /// <summary>차단 경고가 확인(Acknowledge)되었는지 여부.</summary>
        private bool _warningsAcknowledged;

        /// <summary>안전 경로 실패 카운터.</summary>
        private readonly RuntimeDiagnostics _diagnostics = new RuntimeDiagnostics();

        /// <summary>인터록이 발동한 시각. 실효 확인 기준점이다.</summary>
        private DateTime _interlockTrippedSinceUtc = DateTime.MinValue;

        /// <summary>인터록 실효 실패를 이미 보고했는지 여부. 매 사이클 반복 보고를 막는다.</summary>
        private bool _interlockEffectReported;

        /// <summary>
        /// 안전 경로 장애로 SafeStop 을 올렸는지 여부.
        /// </summary>
        /// <remarks>
        /// 누가 올린 정지인지 기억해야 누가 풀 수 있는지 정할 수 있다.
        /// 인터록이 올린 정지는 인터록 해소로 풀리지만, 장애로 올린 정지는
        /// 인터록과 무관하므로 인터록 상태로 풀어서는 안 된다.
        /// </remarks>
        private bool _safeStopByRuntimeFault;

        /// <summary>시각 제공자.</summary>
        private IClock _clock = SystemClock.Instance;

        /// <summary>진단 장애 처리 중 재진입을 막는다.</summary>
        private bool _handlingFault;

        private bool _disposed;

        private EsamRuntime()
        {
        }

        /// <summary>통신 구성.</summary>
        public DeviceMap Map { get; private set; }

        /// <summary>제어 설정.</summary>
        public ControlConfig Control { get; private set; }

        /// <summary>데이터 저장소. UI 는 이곳의 <c>Current</c> 를 끌어간다.</summary>
        public DataStore Store { get; private set; }

        /// <summary>제어 엔진.</summary>
        public ControlEngine Engine { get; private set; }

        /// <summary>인터록 감시자.</summary>
        public InterlockGuard Interlock { get; private set; }

        /// <summary>알람 서비스. 규칙이 없으면 null.</summary>
        public AlarmService Alarms { get; private set; }

        /// <summary>안전 경로 실패 진단. 판정 예외와 인터록 지령 실패를 센다.</summary>
        public RuntimeDiagnostics Diagnostics
        {
            get { return _diagnostics; }
        }

        /// <summary>가상 플랜트. 시뮬레이션 모드에서만 유효하다.</summary>
        public Esam.Communication.Simulation.PlantModel Plant { get; private set; }

        /// <summary>포트 워커 목록.</summary>
        public IList<ModbusPortWorker> Workers
        {
            get { return _workers; }
        }

        /// <summary>구성 경고 목록의 사본.</summary>
        /// <remarks>
        /// <para><b>사본을 반환한다.</b> 조립 시점에만 채워지는 목록이 아니기 때문이다.
        /// <see cref="OnRuntimeFault"/> 가 <b>포트 워커 스레드에서</b> 경고를 추가하므로,
        /// 내부 리스트를 그대로 넘기면 화면이 열거하는 중에 항목이 추가되어
        /// <c>InvalidOperationException</c> 이 발생한다.</para>
        /// <para>그 예외가 터지는 곳이 <b>경고를 보여주려던 화면</b>이라는 점이 특히 나쁘다.
        /// 안전 기능이 동작하지 않는다는 사실을 알리려는 순간에 화면이 죽는다.</para>
        /// <para>사본이므로 여기에 항목을 추가해도 런타임에 반영되지 않는다.</para>
        /// </remarks>
        public IList<ConfigWarning> Warnings
        {
            get
            {
                lock (_warningGate)
                {
                    return new List<ConfigWarning>(_warnings);
                }
            }
        }

        /// <summary>안전 기능이 동작하지 않는 경고가 하나라도 있는지 여부.</summary>
        public bool HasBlockingWarnings
        {
            get
            {
                lock (_warningGate)
                {
                    foreach (ConfigWarning warning in _warnings)
                    {
                        if (warning.IsBlocking)
                        {
                            return true;
                        }
                    }

                    return false;
                }
            }
        }

        /// <summary>차단 경고가 확인되었는지 여부.</summary>
        public bool WarningsAcknowledged
        {
            get { lock (_warningGate) { return _warningsAcknowledged; } }
        }

        /// <summary>
        /// 차단 경고를 확인 처리해 자동 운전 진입을 허용한다.
        /// </summary>
        /// <remarks>
        /// <para>경고를 없애는 것이 아니라 <b>사람이 인지했음을 기록</b>하는 것이다.
        /// 목록은 그대로 남아 화면과 로그에 계속 표시된다.</para>
        /// <para>안전 입력이 배선되지 않은 시운전 단계에서는 이 호출이 필요하다.
        /// 매번 눌러야 하는 것이 번거롭게 느껴질 수 있지만, 그것이 목적이다.
        /// 안전 기능 없이 운전 중이라는 사실을 한 번은 인지해야 한다.</para>
        /// </remarks>
        public void AcknowledgeWarnings()
        {
            lock (_warningGate)
            {
                _warningsAcknowledged = true;
            }
        }

        /// <summary>
        /// 안전 경로 장애로 올린 SafeStop 을 해제한다. 작업자가 원인을 확인한 뒤 호출한다.
        /// </summary>
        /// <returns>해제할 장애가 있었으면 true.</returns>
        /// <remarks>
        /// <para>단계를 Ready 로 되돌리지 않는다. <c>SafeStopCleared</c> 는 Fault 로 가고,
        /// 거기서 재기동하려면 초기화와 원점 복귀를 다시 거쳐야 한다.
        /// 안전 경로가 동작하지 못했던 뒤에는 밸브의 기계적 원점을 신뢰할 수 없다.</para>
        /// <para>진단 카운터도 함께 초기화한다. 남겨 두면 다음 장애가 이전 누적 위에
        /// 얹혀 임계를 즉시 넘긴다.</para>
        /// </remarks>
        public bool ResetRuntimeFault()
        {
            if (!_safeStopByRuntimeFault)
            {
                return false;
            }

            _safeStopByRuntimeFault = false;
            _diagnostics.Reset();

            if (Engine != null && Engine.StateMachine.Phase == SystemPhase.SafeStop)
            {
                Engine.StateMachine.Fire(SystemTrigger.SafeStopCleared);
            }

            return true;
        }

        /// <summary>구성 경고를 추가한다. 어느 스레드에서든 호출할 수 있다.</summary>
        /// <param name="warning">추가할 경고. null 이면 무시한다.</param>
        /// <remarks>
        /// 경고는 조립 시점에만 생기는 것이 아니다. <see cref="OnRuntimeFault"/> 가
        /// 포트 워커 스레드에서 추가하므로 모든 추가를 이 지점으로 모은다.
        /// </remarks>
        private void AddWarning(ConfigWarning warning)
        {
            if (warning == null)
            {
                return;
            }

            lock (_warningGate)
            {
                _warnings.Add(warning);
            }
        }

        /// <summary>
        /// 자동 운전 진입 가능 여부를 판정한다. 제어 엔진이 호출한다.
        /// </summary>
        /// <returns>진입 가능하면 null, 불가하면 거부 사유.</returns>
        private string CheckAutoEntry()
        {
            if (!HasBlockingWarnings || WarningsAcknowledged)
            {
                return null;
            }

            List<string> blocking = new List<string>();

            // Warnings 는 사본을 준다. 워커 스레드가 경고를 추가하는 중일 수 있다.
            foreach (ConfigWarning warning in Warnings)
            {
                if (warning.IsBlocking)
                {
                    blocking.Add(warning.Code + " " + warning.Message);
                }
            }

            return "안전 기능이 동작하지 않는 구성입니다. 확인 후 진행하십시오: "
                   + string.Join(" / ", blocking.ToArray());
        }

        /// <summary>
        /// 런타임을 구성한다.
        /// </summary>
        /// <param name="map">통신 구성.</param>
        /// <param name="control">제어 설정.</param>
        /// <param name="options">런타임 옵션. null 이면 시뮬레이션 기본값.</param>
        /// <param name="clock">시각 제공자.</param>
        /// <returns>구성된 런타임.</returns>
        /// <exception cref="ArgumentNullException">필수 인자가 null 일 때.</exception>
        /// <exception cref="InvalidOperationException">구성 검증에 실패했을 때.</exception>
        public static EsamRuntime Create(
            DeviceMap map, ControlConfig control, RuntimeOptions options, IClock clock)
        {
            if (map == null)
            {
                throw new ArgumentNullException("map");
            }

            if (control == null)
            {
                throw new ArgumentNullException("control");
            }

            RuntimeOptions opts = options ?? new RuntimeOptions();
            IClock resolvedClock = clock ?? SystemClock.Instance;

            // ── 1. 구성 검증 ─────────────────────────────────────────────────────
            // 검증 실패 상태로 통신을 시작하면 엉뚱한 레지스터를 읽거나
            // ID 충돌로 프레임이 깨진다. 반드시 여기서 막는다.
            IList<string> mapErrors;
            IList<string> mapWarnings;

            if (!map.Validate(out mapErrors, out mapWarnings))
            {
                throw new InvalidOperationException(
                    "통신 구성 검증 실패:" + Environment.NewLine + string.Join(Environment.NewLine, mapErrors));
            }

            IList<string> controlErrors;

            if (!control.Validate(out controlErrors))
            {
                throw new InvalidOperationException(
                    "제어 설정 검증 실패:" + Environment.NewLine
                    + string.Join(Environment.NewLine, controlErrors));
            }

            EsamRuntime runtime = new EsamRuntime();
            runtime.Map = map;
            runtime.Control = control;
            runtime._clock = resolvedClock;
            foreach (string mapWarning in mapWarnings)
            {
                runtime.AddWarning(ConfigWarning.Advisory("CFG-MAP", mapWarning, null));
            }

            // ── 안전 입력 유무 판정 ──────────────────────────────────────────────
            // IL-04 는 "PLC 가 있는데 응답하지 않는" 경우에만 성립한다.
            // PLC 가 구성에 없으면 항상 발동해 아무것도 검증할 수 없으므로 판정을 끈다.
            // 다만 그 사실은 반드시 경고로 남긴다. 안전 입력이 하나도 없다는 뜻이기 때문이다.
            control.SafetyInputsConfigured = runtime.HasSafetyInputDevice();

            if (!control.SafetyInputsConfigured)
            {
                runtime.AddWarning(ConfigWarning.Blocking(
                    "SAFE-01",
                    "안전 입력 PLC 가 구성에 없습니다. EMO·메인 차단기·도어 인터록"
                    + "(IL-02·IL-03·IL-04·IL-05)이 동작하지 않습니다.",
                    "device-map.json 에 driver=Plc 디바이스를 추가하고 배선을 확인하십시오."));
            }

            // 설정 로더들이 낸 경고는 일단 지역 목록에 모아 두고 한 번에 넘긴다.
            // 경고 목록 접근을 AddWarning 한 곳으로 모으기 위한 것이다.
            List<ConfigWarning> configWarnings = new List<ConfigWarning>();

            // ── 운전 파라미터 (ECID 마스터) ──────────────────────────────────────
            // device-map 과 대조해 검증한다. 참조가 끊어지면 그 체인이 제어되지 않는다.
            control.Recipe = ResolveRecipe(opts, map, configWarnings);

            // ── 2. 데이터 저장소 ─────────────────────────────────────────────────
            SnapshotBuilder builder = new SnapshotBuilder(map);
            runtime.Store = new DataStore(builder, resolvedClock);

            // ── 3. 알람 / 인터록 ─────────────────────────────────────────────────
            IEnumerable<AlarmRule> alarmRules = ResolveAlarmRules(opts, configWarnings);

            foreach (ConfigWarning configWarning in configWarnings)
            {
                runtime.AddWarning(configWarning);
            }

            if (alarmRules != null)
            {
                runtime.Alarms = new AlarmService(alarmRules, control, resolvedClock);
            }

            InterlockEvaluator evaluator = new InterlockEvaluator(opts.InterlockRules);
            runtime.Interlock = new InterlockGuard(evaluator, control, resolvedClock);

            // 미확정·비활성 인터록을 경고로 올린다. 검증 실패로 막지는 않는다.
            // 폴백값으로도 안전 기능은 동작해야 하고, 미확정 사실만 드러나면 된다.
            List<string> interlockWarnings = new List<string>();
            evaluator.CollectWarnings(interlockWarnings);

            foreach (string text in interlockWarnings)
            {
                // 인터록이 비활성이거나 임계값이 미지정이면 안전 기능이 성립하지 않는다.
                runtime.AddWarning(ConfigWarning.Blocking(
                    "SAFE-02", text, "interlocks 설정과 HW 배선을 확인하십시오."));
            }

            // ── 4. 제어 엔진 ─────────────────────────────────────────────────────
            runtime.Engine = new ControlEngine(runtime.Store, control, null, resolvedClock);

            // ── 5. 전송 계층 + 포트 워커 ─────────────────────────────────────────
            runtime.BuildTransports(opts, resolvedClock);

            // 안전 기능이 빠진 구성으로는 자동 운전에 들어갈 수 없게 막는다.
            // 화면이 없는 단계에서도 효력이 생기도록 진입 지점에 건다.
            runtime.Engine.AutoEntryGuard = runtime.CheckAutoEntry;

            // 안전 판정이 수행되지 못하거나 안전 지령이 닿지 않으면 SafeStop 으로 보낸다.
            runtime._diagnostics.FaultDetected += runtime.OnRuntimeFault;

            // 상태머신 반영은 이벤트가 아니라 폴링마다 상태를 대조해 수행한다(ReconcileInterlock).
            // 이벤트 구독은 이력·화면용으로만 남긴다.
            return runtime;
        }

        /// <summary>
        /// 운전 파라미터를 확정한다. 직접 지정된 것이 있으면 그것을, 없으면 파일을 읽는다.
        /// </summary>
        /// <param name="options">런타임 옵션.</param>
        /// <param name="map">대조할 통신 구성.</param>
        /// <param name="warnings">구성 경고 목록(출력).</param>
        /// <returns>운전 파라미터. 확보하지 못하면 null.</returns>
        /// <remarks>
        /// <para>읽지 못해도 런타임 구성을 실패시키지 않는다. 레시피가 없으면
        /// 모드별 공통값으로 동작하며, 그것이 레시피 도입 전 거동이다. 제어는 성립한다.</para>
        /// <para>다만 <b>센서별 설정이 적용되지 않는다는 사실</b>은 경고로 드러낸다.
        /// 조용히 넘어가면 Config 화면에서 통로별로 값을 넣었다고 믿은 채
        /// 실제로는 공통값으로 운전하게 된다.</para>
        /// <para>파일이 있는데 검증에 실패한 경우는 다르다. 그때는 차단 경고다.
        /// 레시피를 쓰겠다고 선언했는데 참조가 끊어진 상태이므로 구성 오류다.</para>
        /// </remarks>
        private static RecipeDefinition ResolveRecipe(
            RuntimeOptions options, DeviceMap map, IList<ConfigWarning> warnings)
        {
            if (options.Recipe != null)
            {
                return options.Recipe;
            }

            if (string.IsNullOrEmpty(options.RecipePath))
            {
                warnings.Add(ConfigWarning.Advisory(
                    "RCP-01",
                    "레시피 경로가 지정되지 않아 센서별 설정값 대신 모드별 공통값으로 운전합니다.",
                    "RuntimeOptions.RecipePath 를 지정하십시오."));

                return null;
            }

            RecipeLoadResult result = RecipeConfigLoader.LoadFromFile(options.RecipePath, map);

            foreach (string warning in result.Warnings)
            {
                warnings.Add(ConfigWarning.Advisory("RCP-02", warning, null));
            }

            if (result.IsSuccess)
            {
                return result.Recipe;
            }

            foreach (string error in result.Errors)
            {
                warnings.Add(ConfigWarning.Blocking(
                    "RCP-03", "레시피 오류: " + error, "config/recipe.json 을 확인하십시오."));
            }

            warnings.Add(ConfigWarning.Advisory(
                "RCP-01",
                string.Format(
                    CultureInfo.InvariantCulture,
                    "레시피를 읽지 못해({0}) 모드별 공통값으로 운전합니다.",
                    options.RecipePath),
                null));

            return null;
        }

        /// <summary>
        /// 알람 규칙을 확정한다. 직접 지정된 목록이 있으면 그것을, 없으면 파일을 읽는다.
        /// </summary>
        /// <param name="options">런타임 옵션.</param>
        /// <param name="warnings">구성 경고 목록(출력).</param>
        /// <returns>알람 규칙 목록. 확보하지 못하면 null.</returns>
        /// <remarks>
        /// 파일을 읽지 못해도 런타임 구성을 실패시키지 않는다. 알람은 통보 기능이므로
        /// 없다고 해서 운전을 막을 이유는 없다. 다만 <b>알람이 하나도 없다는 사실</b>은
        /// 반드시 경고로 드러낸다. 화면이 조용한 것과 이상이 없는 것은 다르다.
        /// </remarks>
        private static IEnumerable<AlarmRule> ResolveAlarmRules(
            RuntimeOptions options, IList<ConfigWarning> warnings)
        {
            if (options.AlarmRules != null)
            {
                return options.AlarmRules;
            }

            if (string.IsNullOrEmpty(options.AlarmRulesPath))
            {
                warnings.Add(ConfigWarning.Blocking(
                    "ALM-01",
                    "알람 규칙 경로가 지정되지 않아 알람이 하나도 동작하지 않습니다.",
                    "RuntimeOptions.AlarmRulesPath 를 지정하십시오."));

                return null;
            }

            AlarmLoadResult result = AlarmConfigLoader.LoadFromFile(options.AlarmRulesPath);

            // 개별 규칙 비활성·디바운스 경고는 참고 수준이다.
            foreach (string warning in result.Warnings)
            {
                warnings.Add(ConfigWarning.Advisory("ALM-02", warning, null));
            }

            if (!result.IsSuccess)
            {
                foreach (string error in result.Errors)
                {
                    warnings.Add(ConfigWarning.Advisory("ALM-03", "알람 설정 오류: " + error, null));
                }

                // 알람이 전무한 상태는 안전 기능 부재와 같은 무게로 다룬다.
                // 인터록이 걸리기 전에 알려 줄 수단이 하나도 없다는 뜻이다.
                warnings.Add(ConfigWarning.Blocking(
                    "ALM-01",
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "알람 규칙을 읽지 못했습니다({0}). 알람이 하나도 동작하지 않습니다.",
                        options.AlarmRulesPath),
                    "config/alarms.json 을 확인하십시오."));

                return null;
            }

            return result.Rules;
        }

        /// <summary>
        /// 안전 입력을 제공하는 PLC 가 구성에 있는지 판정한다.
        /// </summary>
        /// <returns>PLC 드라이버 디바이스가 하나라도 있으면 true.</returns>
        private bool HasSafetyInputDevice()
        {
            foreach (DeviceInstanceDefinition device in Map.Devices)
            {
                if (device == null || !device.Enabled)
                {
                    continue;
                }

                DeviceTypeDefinition type = Map.FindType(device.Type);

                if (type != null && type.Driver == PointKeys.DriverPlc)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>전송 계층과 포트 워커를 구성한다.</summary>
        private void BuildTransports(RuntimeOptions options, IClock clock)
        {
            if (options.Transport == TransportMode.Simulation)
            {
                Plant = new Esam.Communication.Simulation.PlantModel(
                    Control.Chains,
                    options.Sensor1Ids,
                    new Esam.Communication.Simulation.PlantOptions(),
                    options.SimulationSeed);

                // 기본은 원점 미확정 상태로 시작한다. 제어 엔진의 기동 시퀀스가
                // Homing 을 지령하고 완료를 확인해야 Ready 에 도달한다.
                if (options.PreHomeValves)
                {
                    Plant.CompleteAllHoming();
                }
            }

            foreach (PortDefinition port in Map.Ports)
            {
                if (port == null || string.IsNullOrEmpty(port.PortId))
                {
                    continue;
                }

                IList<DeviceInstanceDefinition> devices = Map.GetDevicesOnPort(port.PortId);

                if (devices.Count == 0)
                {
                    continue;
                }

                IModbusTransport transport = options.Transport == TransportMode.Simulation
                    ? BuildSimulatedTransport(port, devices)
                    : new SerialPortModbusTransport(port.Serial);

                _transports[port.PortId] = transport;

                List<DeviceRuntime> runtimes = new List<DeviceRuntime>();

                foreach (DeviceInstanceDefinition device in devices)
                {
                    DeviceTypeDefinition type = Map.FindType(device.Type);

                    if (type == null)
                    {
                        continue;
                    }

                    DeviceRuntime deviceRuntime = new DeviceRuntime(device, type);
                    runtimes.Add(deviceRuntime);

                    foreach (string skipped in deviceRuntime.SkippedGroups)
                    {
                        AddWarning(ConfigWarning.Advisory(
                            "CFG-ADDR",
                            string.Format(
                                CultureInfo.InvariantCulture,
                                "{0}.{1}: 주소 미확정으로 폴링에서 제외되었습니다.", device.Id, skipped),
                            "레지스터 명세 확보 후 device-map.json 의 startAddress 를 채우십시오."));
                    }
                }

                ModbusPortWorker worker = new ModbusPortWorker(
                    port.PortId, transport, runtimes, port.Polling, null, clock);

                _workers.Add(worker);

                Engine.RegisterWorker(worker);

                // 담당 디바이스 목록을 함께 준다. 지령을 전 워커에 뿌리지 않고
                // 담당 포트에만 보내기 위한 경로표다.
                List<string> ownedIds = new List<string>();

                foreach (DeviceInstanceDefinition owned in devices)
                {
                    ownedIds.Add(owned.Id);
                }

                Interlock.RegisterWorker(worker, ownedIds);

                // 폴링 완료 → 스냅샷 조립 → 인터록 즉시 판정.
                // 세 단계가 같은(워커) 스레드에서 연달아 일어나므로 지연이 최소다.
                worker.PollCompleted += OnPollCompleted;

                // 종전에는 구독자가 하나도 없어, 안전 지령이 장치에 닿지 못해도
                // 아무도 알지 못했다. CloseValve 는 2단 시퀀스라 두 번째가 타임아웃하면
                // 밸브가 전혀 움직이지 않는데, Tripped 는 이미 처리됐다고 알린 뒤다.
                worker.CommandFailed += OnCommandFailed;
                worker.CommandCompleted += OnCommandCompleted;
            }
        }

        /// <summary>시뮬레이션 전송 계층을 구성하고 슬레이브를 등록한다.</summary>
        private IModbusTransport BuildSimulatedTransport(
            PortDefinition port, IList<DeviceInstanceDefinition> devices)
        {
            Esam.Communication.Simulation.SimulationTransportOptions options =
                new Esam.Communication.Simulation.SimulationTransportOptions();

            options.BaudRate = port.Serial.BaudRate;

            Esam.Communication.Simulation.SimulatedModbusTransport transport =
                new Esam.Communication.Simulation.SimulatedModbusTransport(port.PortId, Plant, options);

            foreach (DeviceInstanceDefinition device in devices)
            {
                DeviceTypeDefinition type = Map.FindType(device.Type);
                string driver = type == null ? null : type.Driver;

                switch (driver)
                {
                    case PointKeys.DriverPressureSensor:
                        transport.AddSlave(new Esam.Communication.Simulation.SimulatedPressureSensor(
                            device.SlaveId, Plant, device.Id));
                        break;

                    case PointKeys.DriverThrottleValve:
                        transport.AddSlave(new Esam.Communication.Simulation.SimulatedThrottleValve(
                            device.SlaveId, Plant, device.Id));
                        break;

                    case PointKeys.DriverModbusFan:
                        transport.AddSlave(new Esam.Communication.Simulation.SimulatedBlowerFan(
                            device.SlaveId, Plant, device.Id));
                        break;

                    default:
                        // 시뮬레이션 슬레이브가 없는 장치(PLC·온습도·풍속)는 등록하지 않는다.
                        // 워커는 무응답을 타임아웃으로 처리하고, 스냅샷은 해당 값을 NoData 로 둔다.
                        // 이는 실제로 그 장치들의 레지스터 명세가 미확보인 현재 상태와 동일하다.
                        AddWarning(ConfigWarning.Advisory(
                            "SIM-01",
                            string.Format(
                                CultureInfo.InvariantCulture,
                                "{0}({1}): 시뮬레이션 슬레이브가 없어 무응답으로 동작합니다.",
                                device.Id, driver ?? "unknown"),
                            null));
                        break;
                }
            }

            return transport;
        }

        /// <summary>
        /// 폴링 완료 처리. <b>워커 스레드에서 실행된다.</b>
        /// </summary>
        private void OnPollCompleted(object sender, PollCompletedEventArgs e)
        {
            try
            {
                SystemSnapshot snapshot = Store.Apply(
                    e,
                    Engine.BuildStatus(),
                    Alarms == null ? null : Alarms.Summary);

                // 인터록을 여기서 즉시 판정한다. 제어 타이머를 기다리면 수백 ms 늦는다.
                InterlockEvaluation evaluation = Interlock.Evaluate(snapshot);

                ReconcileInterlock(evaluation);
                VerifyInterlockEffect(evaluation, snapshot);
                VerifyInterlockVision(evaluation);

                if (Alarms != null)
                {
                    Alarms.Evaluate(snapshot);
                }

                _diagnostics.RecordEvaluationSuccess();
            }
            catch (Exception ex)
            {
                // 여기서 잡지 않으면 포트 워커의 catch-all 로 흘러가 흔적 없이 사라진다.
                // 워커는 살아남지만 이번 사이클의 인터록·알람 평가는 수행되지 않았고,
                // 예외가 결정적이면 그 상태가 영구히 이어진다.
                //
                // 예외를 다시 던지지 않는 이유는 폴링 스레드를 죽이면 통신 전체가 멎기 때문이다.
                // 대신 세고, 연속되면 SafeStop 으로 보낸다. 안전 판정이 수행되지 않는 상태를
                // 조용히 넘기는 것보다 장비를 세우는 편이 낫다.
                _diagnostics.RecordEvaluationFailure(ex, _clock.UtcNow);
            }
        }

        /// <summary>
        /// 인터록이 판정 가능한 상태인지 확인한다.
        /// </summary>
        /// <param name="evaluation">이번 사이클 판정 결과.</param>
        /// <remarks>
        /// <para>자동 운전 중에만 확인한다. 기동 전이나 정지 중에는 측정값이 없는 것이 정상이고,
        /// 액추에이터도 움직이지 않아 감시 공백이 위험으로 이어지지 않는다.</para>
        /// <para>운전 중 센서 3 을 계속 읽지 못하면 <b>배기 상실을 감지할 수단이 없다.</b>
        /// 인터록이 발동하지 않는 것과 판정하지 못하는 것을 같게 취급하면,
        /// 눈을 감은 상태를 안전하다고 보고하게 된다.</para>
        /// </remarks>
        private void VerifyInterlockVision(InterlockEvaluation evaluation)
        {
            if (Engine.StateMachine.Phase != SystemPhase.AutoControl)
            {
                _diagnostics.RecordInterlockJudged();
                return;
            }

            if (!evaluation.HasUnjudgeableChain)
            {
                _diagnostics.RecordInterlockJudged();
                return;
            }

            string[] ids = new string[evaluation.UnjudgeableChainIds.Count];

            for (int i = 0; i < ids.Length; i++)
            {
                ids[i] = evaluation.UnjudgeableChainIds[i].ToString(CultureInfo.InvariantCulture);
            }

            string chains = string.Join(", ", ids);

            _diagnostics.RecordInterlockBlind(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "운전 중 체인 {0} 의 센서 3 을 신뢰할 수 없어 인터록이 판정하지 못하고 있습니다. "
                    + "배기 상실을 감지할 수단이 없는 상태입니다.",
                    chains),
                _clock.UtcNow);
        }

        /// <summary>
        /// 인터록 지령이 실제로 효력을 냈는지 확인한다.
        /// </summary>
        /// <param name="evaluation">이번 사이클 판정 결과.</param>
        /// <param name="snapshot">현재 스냅샷.</param>
        /// <remarks>
        /// <para><c>Tripped</c> 이벤트는 "지령을 큐에 넣었다" 는 뜻이지 "밸브가 닫혔다" 는 뜻이 아니다.
        /// <c>CloseValve</c> 는 위치 설정 → PR0 이동 2단 시퀀스라, 두 번째가 타임아웃하면
        /// <b>밸브가 전혀 움직이지 않는다.</b> 그런데 상위는 이미 인터록이 처리됐다고 본다.</para>
        /// <para>발동 후 밸브 이동 시간(<c>MoveTimeoutMs</c>)이 지나도 안전 위치가 아니면 알린다.
        /// 지령을 다시 보내는 것으로는 부족하다. 같은 경로로 다시 실패할 뿐이다.</para>
        /// </remarks>
        private void VerifyInterlockEffect(InterlockEvaluation evaluation, SystemSnapshot snapshot)
        {
            if (!evaluation.HasTrip)
            {
                _interlockTrippedSinceUtc = DateTime.MinValue;

                // 발동이 풀렸으면 다음 발동은 새로 판정한다.
                // 이 플래그를 되돌리지 않으면 최초 1회만 확인하고 그 뒤로는
                // 실효 검증이 프로세스 수명 동안 영구히 꺼진다.
                _interlockEffectReported = false;
                return;
            }

            DateTime nowUtc = _clock.UtcNow;

            if (_interlockTrippedSinceUtc == DateTime.MinValue)
            {
                _interlockTrippedSinceUtc = nowUtc;
                return;
            }

            double elapsedMs = (nowUtc - _interlockTrippedSinceUtc).TotalMilliseconds;

            if (elapsedMs < Control.Valve.MoveTimeoutMs || _interlockEffectReported)
            {
                return;
            }

            foreach (ChainDefinition chain in Control.Chains)
            {
                ValveState valve = snapshot.FindValve(chain.ValveId);

                if (valve != null && valve.Quality == Quality.Good
                    && valve.PositionPulse > Control.Valve.PositionTolerancePulse)
                {
                    _interlockEffectReported = true;

                    _diagnostics.ReportInterlockNotEffective(
                        string.Format(
                            CultureInfo.InvariantCulture,
                            "인터록 발동 후 {0:F0} ms 가 지났으나 밸브 {1} 이 {2} pulse 로 열려 있습니다. "
                            + "안전 지령이 장치에 도달하지 못했을 수 있습니다.",
                            elapsedMs, chain.ValveId, valve.PositionPulse),
                        nowUtc);

                    return;
                }
            }
        }

        /// <summary>
        /// 안전 경로 장애를 처리한다. 임계를 넘은 실패만 도달한다.
        /// </summary>
        /// <param name="sender">이벤트 발신자.</param>
        /// <param name="e">장애 정보.</param>
        /// <remarks>
        /// <para><b>SafeStop 으로 보낸다.</b> Interlocked 가 아니라 SafeStop 인 이유는,
        /// 여기 도달했다는 것은 "안전 기능이 동작하지 못하는 상태" 이지
        /// "안전 조건이 성립한 상태" 가 아니기 때문이다. 전자는 원인을 확인하고
        /// 원점 복귀부터 다시 시작해야 한다.</para>
        /// <para>실패한 경로로 정지 지령을 보내는 것이 무의미해 보일 수 있으나,
        /// 실패는 특정 장치나 특정 사이클에 국한될 수 있다. 보낼 수 있는 것은 보내고,
        /// 무엇보다 <b>자동 제어를 멈추고 작업자에게 알린다.</b></para>
        /// </remarks>
        private void OnRuntimeFault(object sender, RuntimeFaultEventArgs e)
        {
            // 파킹 지령이 다시 실패해 이 핸들러를 재귀 호출하는 것을 막는다.
            if (_handlingFault)
            {
                return;
            }

            _handlingFault = true;

            try
            {
                AddWarning(ConfigWarning.Blocking(
                    "RUN-" + ((int)e.Kind).ToString(CultureInfo.InvariantCulture),
                    e.Detail,
                    "원인을 확인한 뒤 ResetRuntimeFault 로 해제하십시오."));

                // 인터록 판정이 이 정지를 풀지 못하게 표시한다.
                // 이 표시가 없으면 다음 사이클의 정상 판정이 곧바로 해제한다.
                _safeStopByRuntimeFault = true;

                Engine.StateMachine.Fire(SystemTrigger.SafeStopRaised);
                Engine.ParkActuators("안전 경로 장애: " + e.Detail);
            }
            finally
            {
                _handlingFault = false;
            }
        }

        /// <summary>포트 워커의 지령 실패를 처리한다.</summary>
        /// <param name="sender">이벤트 발신자.</param>
        /// <param name="e">실패 정보.</param>
        /// <remarks>
        /// <para><b>담당 워커가 실패한 경우만</b> 문제다. 경로표에 없는 디바이스 지령은
        /// 전 워커로 보내므로(<see cref="InterlockGuard"/>), 담당하지 않는 워커에서는
        /// 반드시 실패한다. 그것은 정상이므로 세지 않는다.</para>
        /// <para>담당 여부는 구성으로 판정한다. 워커가 알려 주는 사유 문자열을 파싱하는 것보다
        /// device-map 을 보는 편이 확실하다.</para>
        /// </remarks>
        private void OnCommandFailed(object sender, CommandFailedEventArgs e)
        {
            if (e == null || e.Command == null)
            {
                return;
            }

            if (e.Command.Priority != CommandPriority.Interlock)
            {
                // 자동·수동 지령 실패는 다음 주기에 다시 시도되므로 알람으로 충분하다.
                return;
            }

            if (!IsOwnedBy(e.PortId, e.Command.DeviceId))
            {
                return;
            }

            _diagnostics.RecordInterlockCommandFailure(e.Command.DeviceId, e.Reason, _clock.UtcNow);
        }

        /// <summary>포트 워커의 지령 전송 성공을 처리한다.</summary>
        /// <param name="sender">이벤트 발신자.</param>
        /// <param name="e">완료 정보.</param>
        /// <remarks>
        /// <para>실패만 세면 <b>복구를 알 수 없다.</b> 장애를 한 번 보고한 뒤에는
        /// 같은 장애를 반복 보고하지 않는데(되먹임 방지), 되살아난 사실을 기록하지 않으면
        /// 그 다음 장애를 새 장애로 구분할 수 없다.</para>
        /// <para>인터록 지령만이 아니라 자동 지령의 성공도 기록한다. 어느 쪽이든
        /// 그 장치에 프레임이 도달했다는 사실은 같다.</para>
        /// </remarks>
        private void OnCommandCompleted(object sender, CommandCompletedEventArgs e)
        {
            if (e == null || e.Command == null)
            {
                return;
            }

            _diagnostics.RecordCommandSuccess(e.Command.DeviceId);
        }

        /// <summary>지정 포트가 해당 디바이스를 담당하는지 판정한다.</summary>
        /// <param name="portId">포트 ID.</param>
        /// <param name="deviceId">디바이스 ID.</param>
        /// <returns>담당하면 true.</returns>
        private bool IsOwnedBy(string portId, string deviceId)
        {
            DeviceInstanceDefinition device = Map.FindDevice(deviceId);

            return device != null
                   && string.Equals(device.Port, portId, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 인터록 판정 결과를 상태머신에 반영한다.
        /// </summary>
        /// <param name="evaluation">이번 사이클의 판정 결과.</param>
        /// <remarks>
        /// <para><b>엣지가 아니라 상태로 판단한다.</b> 종전에는 <c>Tripped</c> 이벤트(false→true 엣지)로
        /// <c>InterlockRaised</c> 를 한 번만 발생시켰다. 그런데 상태머신이 그 트리거를 받지 못하는
        /// 단계에 있으면 전이는 무시되고, 가드는 이미 발동 상태로 넘어가 <b>다시 시도하지 않았다.</b>
        /// 결과적으로 액추에이터는 강제 정지 중인데 단계는 그대로 남아 화면에 인터록이 표시되지 않았다.</para>
        /// <para>매 사이클 현재 상태를 대조하면 전이가 한 번 실패해도 다음 사이클에 복구된다.
        /// 안전 기능에서 "한 번 놓치면 끝"인 구조를 두어서는 안 된다.</para>
        /// <para>전 체인 정지(EMO·차단기·안전입력 상실)는 <see cref="SystemPhase.Interlocked"/> 가 아니라
        /// <see cref="SystemPhase.SafeStop"/> 로 보낸다. Interlocked 는 해제 시 Ready 로 바로 복귀하지만,
        /// SafeStop 은 Fault → Init → 원점 복귀를 거치게 되어 있다. 물리 안전장치가 동작한 뒤에는
        /// 밸브 위치를 다시 확인하고 시작해야 한다.</para>
        /// </remarks>
        private void ReconcileInterlock(InterlockEvaluation evaluation)
        {
            SystemStateMachine machine = Engine.StateMachine;
            SystemPhase phase = machine.Phase;

            if (evaluation.RequiresSystemStop)
            {
                if (phase != SystemPhase.SafeStop)
                {
                    machine.Fire(SystemTrigger.SafeStopRaised);
                }

                return;
            }

            if (evaluation.HasTrip)
            {
                // 전 체인 정지가 걸린 상태에서 체인 인터록이 남아 있어도 SafeStop 을 풀지 않는다.
                if (phase != SystemPhase.Interlocked && phase != SystemPhase.SafeStop)
                {
                    machine.Fire(SystemTrigger.InterlockRaised);
                }

                return;
            }

            // 발동이 모두 해소되었다. 단계에 맞는 해제 트리거를 낸다.
            if (phase == SystemPhase.SafeStop)
            {
                // ★ 인터록이 올리지 않은 SafeStop 은 인터록이 풀 수 없다.
                //
                // 안전 경로 장애(판정 예외·지령 실패·판정 불가)로 올린 SafeStop 을
                // "인터록이 발동하지 않았다" 는 이유로 풀면, 장애를 감지한 바로 다음
                // 사이클에 스스로 해제된다. 그리고 그 사이클에서 판정 불가 카운터까지
                // 0 으로 되돌아가므로 다시 올릴 수도 없다.
                //
                // 결과는 "장애를 알렸고 즉시 잊었다" 다. 화면에는 차단 경고만 남고
                // 단계는 내려간다. 원인은 그대로인데 아무도 보지 않는다.
                //
                // 장애로 올린 정지는 작업자가 원인을 확인하고 ResetRuntimeFault 를
                // 호출할 때까지 유지한다.
                if (_safeStopByRuntimeFault)
                {
                    return;
                }

                machine.Fire(SystemTrigger.SafeStopCleared);
            }
            else if (phase == SystemPhase.Interlocked)
            {
                machine.Fire(SystemTrigger.InterlockCleared);
            }
        }

        /// <summary>전 포트 폴링과 제어 루프를 시작한다.</summary>
        public void Start()
        {
            ThrowIfDisposed();

            foreach (ModbusPortWorker worker in _workers)
            {
                worker.Start();
            }

            // Start 트리거만 낸다. Init → ValveHoming → Ready 진행은 제어 엔진이
            // 스냅샷으로 완료를 확인하며 수행한다.
            //
            // 종전에는 여기서 InitCompleted 와 HomingCompleted 를 확인 없이 발생시켰다.
            // 그 결과 원점 복귀 지령이 프로덕션 경로에서 한 번도 전송되지 않았고,
            // 밸브의 기계적 원점이 미확정인 채로 Ready 에 도달했다.
            Engine.StateMachine.Fire(SystemTrigger.Start);

            Engine.Start();
        }

        /// <summary>
        /// 액추에이터를 안전 위치로 보낸 뒤 제어 루프와 폴링을 중지한다.
        /// </summary>
        /// <param name="parkTimeoutMs">파킹 확인 대기 시간 [ms]. 0 이면 기다리지 않는다.</param>
        /// <remarks>
        /// <para><b>순서가 중요하다.</b> 폴링을 먼저 멈추면 파킹 지령이 큐에만 남고
        /// 전송되지 않는다. 밸브는 열린 채, 팬은 도는 채로 프로그램이 사라지고,
        /// 인터록 평가도 함께 끝나 아무도 보지 않는 상태가 된다.</para>
        /// <list type="number">
        ///   <item><description>제어 엔진 정지 — 새 자동 지령을 막는다</description></item>
        ///   <item><description>전 체인 밸브 Close + 팬 OFF 를 인터록 우선순위로 투입</description></item>
        ///   <item><description>실행 확인까지 대기(최대 <paramref name="parkTimeoutMs"/>)</description></item>
        ///   <item><description>상태머신 Idle 복귀 → 워커 정지</description></item>
        /// </list>
        /// <para>정전이나 강제 종료에서는 이 경로가 실행되지 않는다. 정상 종료만 덮는 한계가 있으나,
        /// 대부분의 종료가 정상 종료이므로 값어치가 있다.</para>
        /// <para>상태머신을 Idle 로 되돌리는 이유는 별개다. 단계가 AutoControl 로 남으면
        /// 재시작 시 Start 트리거가 무시되어 <b>초기화와 원점 복귀를 건너뛴 채</b>
        /// 자동 운전 상태에서 재개된다.</para>
        /// </remarks>
        public void Stop(int parkTimeoutMs = 5000)
        {
            if (Engine != null)
            {
                Engine.Stop();
                Engine.ParkActuators("프로그램 종료: 액추에이터 안전 위치 이동");
            }

            WaitForPark(parkTimeoutMs);

            if (Engine != null)
            {
                Engine.StateMachine.Fire(SystemTrigger.Stop);
            }

            foreach (ModbusPortWorker worker in _workers)
            {
                worker.Stop();
            }
        }

        /// <summary>파킹 지령이 실행될 때까지 기다린다.</summary>
        /// <param name="timeoutMs">최대 대기 시간 [ms].</param>
        /// <remarks>
        /// 워커가 돌고 있지 않으면(테스트나 수동 사이클 실행) 기다릴 이유가 없다.
        /// 그 경우 지령은 큐에 남고, 호출측이 사이클을 돌려 처리한다.
        /// </remarks>
        private void WaitForPark(int timeoutMs)
        {
            if (timeoutMs <= 0)
            {
                return;
            }

            bool anyRunning = false;

            foreach (ModbusPortWorker worker in _workers)
            {
                if (worker.IsRunning)
                {
                    anyRunning = true;
                    break;
                }
            }

            if (!anyRunning)
            {
                return;
            }

            System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();

            while (watch.ElapsedMilliseconds < timeoutMs)
            {
                if (IsParked())
                {
                    return;
                }

                System.Threading.Thread.Sleep(50);
            }

            AddWarning(ConfigWarning.Advisory(
                "STOP-01",
                "종료 시 액추에이터 파킹을 확인하지 못했습니다. 밸브·팬 위치를 직접 확인하십시오.",
                null));
        }

        /// <summary>모든 액추에이터가 안전 위치에 있는지 판정한다.</summary>
        /// <returns>밸브가 닫히고 팬이 멈췄으면 true.</returns>
        private bool IsParked()
        {
            SystemSnapshot snapshot = Store.Current;

            foreach (ChainDefinition chain in Control.Chains)
            {
                ValveState valve = snapshot.FindValve(chain.ValveId);

                if (valve != null && valve.Quality == Quality.Good
                    && valve.PositionPulse > Control.Valve.PositionTolerancePulse)
                {
                    return false;
                }

                FanState fan = snapshot.FindFan(chain.FanId);

                if (fan != null && fan.Quality == Quality.Good && fan.IsRunning)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// 지정 포트의 전송 계층을 반환한다.
        /// </summary>
        /// <param name="portId">포트 ID.</param>
        /// <returns>전송 계층. 없으면 null.</returns>
        /// <remarks>
        /// 진단·테스트 용도다. 시뮬레이션 모드에서 슬레이브를 분리해
        /// 통신 장애를 주입하거나, 실장비에서 포트 통계를 직접 읽을 때 사용한다.
        /// 제어 경로에서는 호출하지 않는다.
        /// </remarks>
        public IModbusTransport FindTransport(string portId)
        {
            if (string.IsNullOrEmpty(portId))
            {
                return null;
            }

            IModbusTransport transport;
            return _transports.TryGetValue(portId, out transport) ? transport : null;
        }

        /// <summary>구성 요약을 사람이 읽을 수 있는 형태로 만든다.</summary>
        /// <returns>요약 문자열.</returns>
        public string Describe()
        {
            System.Text.StringBuilder builder = new System.Text.StringBuilder();

            builder.AppendLine(string.Format(
                CultureInfo.InvariantCulture,
                "ESAM 런타임 — 포트 {0}, 체인 {1}, 알람 규칙 {2}",
                _workers.Count,
                Control.Chains == null ? 0 : Control.Chains.Count,
                Alarms == null ? 0 : Alarms.RuleCount));

            foreach (ModbusPortWorker worker in _workers)
            {
                builder.AppendLine(string.Format(
                    CultureInfo.InvariantCulture,
                    "  {0}: 디바이스 {1}, 사이클 {2:F1} ms, 성공률 {3:F1}%",
                    worker.PortId, worker.DeviceCount,
                    worker.Statistics.LastCycleMs, worker.Statistics.SuccessRatePercent));
            }

            // 건수만 찍으면 로그를 봐도 원인을 알 수 없다. 본문을 낸다.
            foreach (ConfigWarning warning in Warnings)
            {
                builder.AppendLine("  " + warning);
            }

            if (HasBlockingWarnings && !WarningsAcknowledged)
            {
                builder.AppendLine("  ※ 차단 경고가 확인되지 않아 자동 운전에 진입할 수 없습니다.");
            }

            return builder.ToString();
        }

        /// <summary>객체가 해제되었는지 확인한다.</summary>
        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException("EsamRuntime");
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            Stop();

            foreach (ModbusPortWorker worker in _workers)
            {
                worker.PollCompleted -= OnPollCompleted;
                worker.CommandFailed -= OnCommandFailed;
                worker.CommandCompleted -= OnCommandCompleted;
                worker.Dispose();
            }

            foreach (IModbusTransport transport in _transports.Values)
            {
                transport.Dispose();
            }

            if (Engine != null)
            {
                Engine.Dispose();
            }
        }
    }
}
