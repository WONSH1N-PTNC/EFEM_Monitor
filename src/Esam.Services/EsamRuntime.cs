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

        /// <summary>알람 규칙 목록. null 이면 알람 평가를 하지 않는다.</summary>
        public IEnumerable<AlarmRule> AlarmRules { get; set; }

        /// <summary>인터록 규칙 목록. null 이면 기본 규칙을 사용한다.</summary>
        public IEnumerable<InterlockRule> InterlockRules { get; set; }

        /// <summary>시뮬레이션 난수 시드.</summary>
        public int SimulationSeed { get; set; }

        /// <summary>기본값으로 초기화한다.</summary>
        public RuntimeOptions()
        {
            Transport = TransportMode.Simulation;
            Sensor1Ids = new List<string> { "S1-1", "S1-2", "S1-3" };
            SimulationSeed = 20260805;
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
        private readonly List<string> _warnings = new List<string>();

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

        /// <summary>가상 플랜트. 시뮬레이션 모드에서만 유효하다.</summary>
        public Esam.Communication.Simulation.PlantModel Plant { get; private set; }

        /// <summary>포트 워커 목록.</summary>
        public IList<ModbusPortWorker> Workers
        {
            get { return _workers; }
        }

        /// <summary>구성 경고 목록(주소 미확정 등).</summary>
        public IList<string> Warnings
        {
            get { return _warnings; }
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
            runtime._warnings.AddRange(mapWarnings);

            // ── 안전 입력 유무 판정 ──────────────────────────────────────────────
            // IL-04 는 "PLC 가 있는데 응답하지 않는" 경우에만 성립한다.
            // PLC 가 구성에 없으면 항상 발동해 아무것도 검증할 수 없으므로 판정을 끈다.
            // 다만 그 사실은 반드시 경고로 남긴다. 안전 입력이 하나도 없다는 뜻이기 때문이다.
            control.SafetyInputsConfigured = runtime.HasSafetyInputDevice();

            if (!control.SafetyInputsConfigured)
            {
                runtime._warnings.Add(
                    "안전 입력 PLC 가 구성에 없습니다. EMO·메인 차단기·도어 인터록"
                    + "(IL-02·IL-03·IL-04·IL-05)이 동작하지 않습니다. 실장비 운전 전에 반드시 배선해야 합니다.");
            }

            // ── 2. 데이터 저장소 ─────────────────────────────────────────────────
            SnapshotBuilder builder = new SnapshotBuilder(map);
            runtime.Store = new DataStore(builder, resolvedClock);

            // ── 3. 알람 / 인터록 ─────────────────────────────────────────────────
            if (opts.AlarmRules != null)
            {
                runtime.Alarms = new AlarmService(opts.AlarmRules, control, resolvedClock);
            }

            InterlockEvaluator evaluator = new InterlockEvaluator(opts.InterlockRules);
            runtime.Interlock = new InterlockGuard(evaluator, control, resolvedClock);

            // 미확정·비활성 인터록을 경고로 올린다. 검증 실패로 막지는 않는다.
            // 폴백값으로도 안전 기능은 동작해야 하고, 미확정 사실만 드러나면 된다.
            evaluator.CollectWarnings(runtime._warnings);

            // ── 4. 제어 엔진 ─────────────────────────────────────────────────────
            runtime.Engine = new ControlEngine(runtime.Store, control, null, resolvedClock);

            // ── 5. 전송 계층 + 포트 워커 ─────────────────────────────────────────
            runtime.BuildTransports(opts, resolvedClock);

            // ── 6. 인터록 발동 시 자동 운전 중단 ─────────────────────────────────
            runtime.Interlock.Tripped += (sender, e) =>
                runtime.Engine.StateMachine.Fire(SystemTrigger.InterlockRaised);

            runtime.Interlock.InterlockCleared += (sender, e) =>
                runtime.Engine.StateMachine.Fire(SystemTrigger.InterlockCleared);

            return runtime;
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

                // 전원 ON 직후 상태를 재현하려면 Homing 을 완료 처리하지 않아야 하지만,
                // 상위 통합 검증이 목적이므로 여기서는 완료 상태로 시작한다.
                // 원점 복귀 시퀀스 검증은 별도 시나리오에서 수행한다.
                Plant.CompleteAllHoming();
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
                        _warnings.Add(string.Format(
                            CultureInfo.InvariantCulture,
                            "{0}.{1}: 주소 미확정으로 폴링에서 제외되었습니다.", device.Id, skipped));
                    }
                }

                ModbusPortWorker worker = new ModbusPortWorker(
                    port.PortId, transport, runtimes, port.Polling, null, clock);

                _workers.Add(worker);

                Engine.RegisterWorker(worker);
                Interlock.RegisterWorker(worker);

                // 폴링 완료 → 스냅샷 조립 → 인터록 즉시 판정.
                // 세 단계가 같은(워커) 스레드에서 연달아 일어나므로 지연이 최소다.
                worker.PollCompleted += OnPollCompleted;
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
                        _warnings.Add(string.Format(
                            CultureInfo.InvariantCulture,
                            "{0}({1}): 시뮬레이션 슬레이브가 없어 무응답으로 동작합니다.",
                            device.Id, driver ?? "unknown"));
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
            SystemSnapshot snapshot = Store.Apply(
                e,
                Engine.BuildStatus(),
                Alarms == null ? null : Alarms.Summary);

            // 인터록을 여기서 즉시 판정한다. 제어 타이머를 기다리면 수백 ms 늦는다.
            Interlock.Evaluate(snapshot);

            if (Alarms != null)
            {
                Alarms.Evaluate(snapshot);
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

            // 상태머신을 Idle → Init → Homing → Ready 로 진행시킨다.
            // 실제 시스템에서는 각 단계 완료를 확인해야 하지만,
            // 여기서는 밸브가 이미 원점 복귀된 상태로 시작하므로 바로 진행한다.
            Engine.StateMachine.Fire(SystemTrigger.Start);
            Engine.StateMachine.Fire(SystemTrigger.InitCompleted);
            Engine.StateMachine.Fire(SystemTrigger.HomingCompleted);

            Engine.Start();
        }

        /// <summary>제어 루프와 폴링을 중지한다.</summary>
        public void Stop()
        {
            if (Engine != null)
            {
                Engine.Stop();
            }

            foreach (ModbusPortWorker worker in _workers)
            {
                worker.Stop();
            }
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

            if (_warnings.Count > 0)
            {
                builder.AppendLine(string.Format(
                    CultureInfo.InvariantCulture, "  경고 {0}건", _warnings.Count));
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
