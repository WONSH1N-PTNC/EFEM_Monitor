using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Esam.Communication.Abstractions;
using Esam.Communication.Configuration;
using Esam.Communication.Polling;
using Esam.Communication.Simulation;
using Esam.Domain.Alarms;
using Esam.Domain.Configuration;
using Esam.Domain.Control;
using Esam.Domain.Models;
using Esam.Services;
using Xunit;

namespace Esam.Tests
{
    /// <summary>
    /// S4 통합 검증. 워커 → 스냅샷 → 인터록 → 제어 루프의 종단 동작을 확인한다.
    /// </summary>
    /// <remarks>
    /// 하드웨어 없이 시뮬레이션 전송 계층 위에서 전체 시스템을 돌린다.
    /// 개별 계층 단위테스트가 모두 통과해도 배선이 틀리면 아무것도 동작하지 않으므로,
    /// 실제로 "시스템이 돌아가는가"를 판정하는 것은 이 테스트다.
    ///
    /// 시간과 플랜트를 수동으로 진행시켜 결정적으로 만들었다. 실제 타이머를 쓰면
    /// CI 부하에 따라 결과가 흔들려 회귀 검증에 쓸 수 없다.
    /// </remarks>
    public class ServicesIntegrationTests : IDisposable
    {
        /// <summary>
        /// 루프 헬퍼가 스스로 포기할 시간 [ms].
        /// </summary>
        /// <remarks>
        /// <para><b><c>Fact(Timeout = ...)</c> 은 쓸 수 없다.</b> xUnit 은 async 테스트에만
        /// 적용하며, 동기 테스트에 붙이면 "Tests marked with Timeout are only supported
        /// for async tests" 로 <b>테스트 자체가 실패</b>한다. 이 프로젝트의 테스트는
        /// 결정적 실행을 위해 전부 동기다.</para>
        /// <para>그래서 시간 상한을 루프 헬퍼 안에서 직접 확인한다.
        /// 가장 무거운 시나리오가 수 초 수준이므로 20초면 여유가 충분하다.
        /// 이 값에 걸린다면 성능 문제가 아니라 진행이 멈춘 것이다.</para>
        /// <para><b>한계를 분명히 해 둔다.</b> 이 검사는 반복 <i>사이</i>에서만 동작하므로,
        /// 한 번의 호출이 돌아오지 않으면 잡지 못한다. 그 경우를 막는 것은
        /// <c>ModbusPortWorker</c> 의 지령 처리 상한이다(D13). 이쪽은 그 밖의
        /// 진행 정지를 드러내는 두 번째 그물이다.</para>
        /// </remarks>
        private const int LoopBudgetMs = 20000;

        private static readonly string[] Sensor1Ids = { "S1-1", "S1-2", "S1-3" };

        private readonly FakeClock _clock;
        private readonly Stopwatch _budget = Stopwatch.StartNew();

        /// <summary>이 테스트가 만든 런타임 전부. 하나도 빠뜨리지 않고 정리한다.</summary>
        /// <remarks>
        /// <c>_runtime</c> 하나만 정리하면 <b>한 테스트에서 두 번째 런타임을 만들 때</b>
        /// 첫 번째가 조용히 샌다. 시뮬레이션 전송 계층과 이벤트 구독이 남고,
        /// 테스트가 60건이므로 누적되면 다른 테스트의 결과까지 흔든다.
        /// 규약에 의존하지 않고 생성 지점에서 등록한다.
        /// </remarks>
        private readonly List<EsamRuntime> _created = new List<EsamRuntime>();

        private EsamRuntime _runtime;

        /// <summary>테스트 준비.</summary>
        public ServicesIntegrationTests()
        {
            _clock = new FakeClock(Build.T0);
        }

        /// <inheritdoc />
        /// <remarks>
        /// <para>런타임이 정리되지 않으면 워커 스레드와 시뮬레이션 전송 계층이 남는다.
        /// 테스트가 60건이므로 누적되면 스레드풀과 메모리를 잠식하고,
        /// 남은 스레드가 이벤트를 계속 발생시켜 <b>다른 테스트의 결과를 흔든다.</b></para>
        /// <para>런타임을 지역 변수로만 받는 테스트가 있으면 이 정리에서 빠진다.
        /// 그래서 모든 생성 경로가 <c>_runtime</c> 에 대입한다.
        /// 한 테스트에서 두 번째 런타임을 만들면 첫 번째가 여기서 새므로,
        /// 아래 <c>런타임_생성은_항상_정리_대상에_등록된다</c> 가 그 규약을 지킨다.</para>
        /// </remarks>
        public void Dispose()
        {
            // 역순으로 정리한다. 나중에 만든 것이 먼저 사라지는 편이
            // 자원 의존이 있을 때 안전하다.
            for (int i = _created.Count - 1; i >= 0; i--)
            {
                try
                {
                    _created[i].Dispose();
                }
                catch (Exception)
                {
                    // 정리 실패가 테스트 결과를 덮어써서는 안 된다.
                    // 실제 검증 실패가 정리 예외로 가려지면 원인을 찾을 수 없다.
                }
            }

            _created.Clear();
            _runtime = null;
        }

        /// <summary>런타임을 정리 대상으로 등록한다.</summary>
        /// <param name="runtime">등록할 런타임.</param>
        /// <returns>같은 런타임.</returns>
        private EsamRuntime Track(EsamRuntime runtime)
        {
            if (runtime != null)
            {
                _created.Add(runtime);
            }

            return runtime;
        }

        /// <summary>루프가 예산을 넘겼으면 즉시 실패시킨다.</summary>
        /// <param name="where">어느 루프인지.</param>
        /// <remarks>
        /// 멈춘 테스트는 원인을 알려주지 않는다. 실패한 테스트는 알려준다.
        /// 진행이 멈추는 결함(되먹임 루프 등)은 여기서 드러나야 한다.
        /// </remarks>
        private void EnsureWithinBudget(string where)
        {
            if (_budget.ElapsedMilliseconds <= LoopBudgetMs)
            {
                return;
            }

            Assert.Fail(string.Format(
                CultureInfo.InvariantCulture,
                "{0} 가 {1} ms 안에 끝나지 않았습니다. 진행이 멈춘 것으로 판단합니다.",
                where, LoopBudgetMs));
        }

        // ── 구성 도우미 ─────────────────────────────────────────────────────────

        /// <summary>
        /// 통합 테스트용 구성을 만든다. IO List_260801.xlsx 의 2채널 구성을 따른다.
        /// CH1 에 차압센서 13대, CH2 에 밸브 5대(ID 1~5) + 블로워 5대(ID 6~10).
        /// </summary>
        /// <remarks>
        /// 레지스터 주소는 <see cref="SimulatedPressureSensor"/> 등 시뮬레이션 슬레이브가
        /// 실제로 매핑한 주소와 일치해야 한다. 어긋나면 예외 응답(0x02)이 돌아온다.
        /// </remarks>
        private static DeviceMap CreateMap()
        {
            DeviceMap map = new DeviceMap();

            map.Ports.Add(CreatePort("CH1", "COM3", 19200));
            map.Ports.Add(CreatePort("CH2", "COM4", 38400));

            map.DeviceTypes["DiffPressure"] = CreateSensorType();
            map.DeviceTypes["ThrottleValve"] = CreateValveType();
            map.DeviceTypes["BlowerFan"] = CreateFanType();

            string[] sensorIds =
            {
                "S1-1", "S1-2", "S1-3",
                "S2-1", "S2-2", "S2-3", "S2-4", "S2-5",
                "S3-1", "S3-2", "S3-3", "S3-4", "S3-5"
            };

            for (int i = 0; i < sensorIds.Length; i++)
            {
                map.Devices.Add(new DeviceInstanceDefinition
                {
                    Id = sensorIds[i],
                    Type = "DiffPressure",
                    Port = "CH1",
                    SlaveId = (byte)(i + 1),
                    RangeMin = -2000.0,
                    RangeMax = 2000.0,

                    // 필터는 제어 엔진이 별도로 적용한다. 이중 필터링은 위상 지연만 늘린다.
                    FilterWindowSize = 1
                });
            }

            // 밸브와 블로워가 같은 버스를 공유하므로 슬레이브 ID 가 겹치면 안 된다.
            // IO List 기준 밸브 1~5, 블로워 6~10.
            for (int i = 1; i <= 5; i++)
            {
                map.Devices.Add(new DeviceInstanceDefinition
                {
                    Id = "V-" + i, Type = "ThrottleValve", Port = "CH2", SlaveId = (byte)i
                });

                map.Devices.Add(new DeviceInstanceDefinition
                {
                    Id = "F-" + i, Type = "BlowerFan", Port = "CH2", SlaveId = (byte)(i + 5)
                });
            }

            return map;
        }

        private static PortDefinition CreatePort(string id, string name, int baud)
        {
            PortDefinition port = new PortDefinition();
            port.Serial.PortId = id;
            port.Serial.PortName = name;
            port.Serial.BaudRate = baud;
            return port;
        }

        private static DeviceTypeDefinition CreateSensorType()
        {
            DeviceTypeDefinition type = new DeviceTypeDefinition();
            type.Driver = PointKeys.DriverPressureSensor;

            ReadGroupDefinition group = new ReadGroupDefinition();
            group.Name = "pressure";
            group.Tier = PollingTier.Fast;
            group.StartAddress = "0x4001";
            group.Count = 1;
            group.Points.Add(new PointDefinition
            {
                Key = PointKeys.PressurePa,
                Type = PointDataType.Int16,

                // 시뮬레이션 슬레이브의 인코딩(0.1 Pa/LSB)과 일치시킨다.
                // 어긋나면 측정값이 배수만큼 틀린 채 각 계층은 자기 기준으로 일관해
                // 어디에서도 오류가 나지 않는다.
                Scale = SimulatedPressureSensor.PaPerLsb,
                Unit = "Pa",
                ApplyCalibration = true
            });

            type.ReadGroups.Add(group);
            return type;
        }

        private static DeviceTypeDefinition CreateValveType()
        {
            DeviceTypeDefinition type = new DeviceTypeDefinition();
            type.Driver = PointKeys.DriverThrottleValve;

            ReadGroupDefinition position = new ReadGroupDefinition();
            position.Name = "position";
            position.Tier = PollingTier.Fast;
            position.StartAddress = "0x602B";
            position.Count = 1;
            position.Points.Add(new PointDefinition
            {
                Key = PointKeys.PositionPulse, Type = PointDataType.UInt16, Unit = "pulse"
            });

            ReadGroupDefinition home = new ReadGroupDefinition();
            home.Name = "home";
            home.Tier = PollingTier.Fast;
            home.StartAddress = "0x0147";
            home.Count = 1;
            home.Points.Add(new PointDefinition
            {
                Key = PointKeys.HomeDone, Type = PointDataType.Bool, Bit = 0, ActiveHigh = true
            });

            type.ReadGroups.Add(position);
            type.ReadGroups.Add(home);

            // 밸브 이동은 위치 설정(0x6202) → PR0 Move(0x6002←0x10) 2단 시퀀스다.
            type.Commands["setPosition"] =
                new CommandDefinition { FunctionCode = 6, Address = "0x6202", Value = "$arg" };
            type.Commands["prMove"] =
                new CommandDefinition { FunctionCode = 6, Address = "0x6002", Value = "0x0010" };
            type.Commands["homing"] =
                new CommandDefinition { FunctionCode = 6, Address = "0x6002", Value = "0x0020" };
            type.Commands["quickStop"] =
                new CommandDefinition { FunctionCode = 6, Address = "0x6002", Value = "0x0040" };

            return type;
        }

        private static DeviceTypeDefinition CreateFanType()
        {
            DeviceTypeDefinition type = new DeviceTypeDefinition();
            type.Driver = PointKeys.DriverModbusFan;

            // JKBLD300V2: 현재속도(0x4041)와 고장코드(0x4042)가 연속이라 1트랜잭션으로 읽는다.
            ReadGroupDefinition runtime = new ReadGroupDefinition();
            runtime.Name = "runtime";
            runtime.Tier = PollingTier.Fast;
            runtime.StartAddress = "0x4041";
            runtime.Count = 2;
            runtime.Points.Add(new PointDefinition
            {
                Key = PointKeys.Rpm, Offset = 0, Type = PointDataType.UInt16, Unit = "RPM"
            });
            runtime.Points.Add(new PointDefinition
            {
                Key = PointKeys.AlarmCode, Offset = 1, Type = PointDataType.UInt16
            });

            type.ReadGroups.Add(runtime);

            // 폐루프 RPM 지령(0x4006). 개루프(0x4007)는 RPM 이 아니라 듀티 % 라서 쓰지 않는다.
            type.Commands["setRpm"] =
                new CommandDefinition { FunctionCode = 6, Address = "0x4006", Value = "$arg" };
            type.Commands["start"] =
                new CommandDefinition { FunctionCode = 6, Address = "0x4034", Value = "1" };
            type.Commands["stop"] =
                new CommandDefinition { FunctionCode = 6, Address = "0x4034", Value = "0" };

            // JKBLD300V2 폐루프 설정 레지스터의 유효 범위.
            type.Conversion.MinRpm = 200.0;
            type.Conversion.MaxRpm = 4000.0;
            return type;
        }

        /// <summary>제어 설정을 만든다.</summary>
        /// <param name="setpointPa">센서 2 목표 압력.</param>
        /// <param name="bandPa">센서 2 대역 폭.</param>
        /// <param name="outOfBandSec">대역 이탈 확정 시간.</param>
        private static ControlConfig CreateControl(
            double setpointPa = -10.0, double bandPa = 5.0, double outOfBandSec = 1.0)
        {
            ControlConfig control = Build.Config(SensorMode.Sensor2);
            control.Modes[SensorMode.Sensor2] = new ModeSetting(setpointPa, bandPa, outOfBandSec);
            control.Valve.DwellMs = 500;
            control.Fan.DwellMs = 500;

            // 필터 창을 1로 두어 지연 없이 반응하게 한다. 필터 자체는 별도 단위테스트가 있다.
            control.FilterWindowSize = 1;
            return control;
        }

        /// <summary>
        /// 제어 설정의 모드별 대역을 센서 13대에 펼쳐 레시피를 만든다.
        /// </summary>
        /// <param name="control">제어 설정.</param>
        /// <returns>테스트용 레시피.</returns>
        /// <remarks>
        /// <para>배포용 <c>config/recipe.json</c> 을 그대로 쓰면 테스트가 대역을 조정할 수 없다.
        /// 반대로 레시피를 아예 주지 않으면 모드별 공통값 경로만 지나고
        /// <b>센서별 조회 경로를 한 번도 검증하지 않는다.</b></para>
        /// <para>그래서 테스트가 지정한 대역으로 센서별 레시피를 만들어 넘긴다.
        /// 의도한 대역을 유지하면서 <c>GetSetting</c> 경로를 실제로 지나간다.</para>
        /// </remarks>
        private static RecipeDefinition BuildRecipe(ControlConfig control)
        {
            RecipeDefinition recipe = new RecipeDefinition();
            recipe.Name = "테스트 레시피";

            string[] groups = { "S1-", "S2-", "S3-" };
            SensorMode[] modes = { SensorMode.Sensor1, SensorMode.Sensor2, SensorMode.Sensor3 };
            int[] counts = { 3, 5, 5 };

            for (int g = 0; g < groups.Length; g++)
            {
                ModeSetting mode = control.GetMode(modes[g]);

                for (int i = 1; i <= counts[g]; i++)
                {
                    recipe.Sensors.Add(new SensorSetting(
                        groups[g] + i, mode.SetpointPa, mode.LowLimitPa, mode.HighLimitPa));
                }
            }

            return recipe;
        }

        /// <summary>런타임을 구성한다.</summary>
        private EsamRuntime CreateRuntime(ControlConfig control = null, DeviceMap map = null)
        {
            ControlConfig resolved = control ?? CreateControl();

            RuntimeOptions options = new RuntimeOptions();
            options.Transport = TransportMode.Simulation;
            options.Sensor1Ids = Sensor1Ids;
            options.Recipe = BuildRecipe(resolved);

            _runtime = Track(EsamRuntime.Create(map ?? CreateMap(), resolved, options, _clock));

            // 이 구성에는 안전 입력 PLC 가 없어 차단 경고가 뜬다.
            // 테스트는 그 사실을 알고 진행한다는 뜻으로 명시 확인한다.
            _runtime.AcknowledgeWarnings();

            OpenTransports(_runtime);
            return _runtime;
        }

        /// <summary>
        /// 전송 계층을 연다.
        /// </summary>
        /// <remarks>
        /// 평소에는 <c>ModbusPortWorker.Start()</c> 가 열지만, 이 테스트는 스레드를 띄우지 않고
        /// <c>ExecuteCycle</c> 을 직접 호출한다. 열지 않으면 모든 트랜잭션이 PortError 로 실패해
        /// 조립 결과가 전부 NoData 가 된다.
        /// </remarks>
        private static void OpenTransports(EsamRuntime runtime)
        {
            foreach (ModbusPortWorker worker in runtime.Workers)
            {
                runtime.FindTransport(worker.PortId).Open();
            }
        }

        /// <summary>폴링 1사이클을 전 포트에 대해 수동 실행한다.</summary>
        private static void PollAll(EsamRuntime runtime)
        {
            foreach (ModbusPortWorker worker in runtime.Workers)
            {
                worker.ExecuteCycle(CancellationToken.None);
            }
        }

        /// <summary>
        /// 기동 시퀀스를 실제로 돌려 Ready 까지 진행시킨다.
        /// </summary>
        /// <remarks>
        /// 종전에는 InitCompleted·HomingCompleted 를 직접 발생시켰다. 그러면 원점 복귀 경로를
        /// 한 번도 지나지 않아, 시퀀스가 깨져 있어도 어떤 테스트에서도 드러나지 않는다.
        /// 폴링과 제어 스텝을 실제로 반복해 Ready 에 도달시킨다.
        /// </remarks>
        private void AdvanceToReady(EsamRuntime runtime)
        {
            runtime.Engine.StateMachine.Fire(SystemTrigger.Start);

            for (int i = 0; i < 200; i++)
            {
                EnsureWithinBudget("AdvanceToReady");

                PollAll(runtime);
                runtime.Engine.ExecuteStep();
                runtime.Plant.Advance(0.2);
                _clock.AdvanceMs(200);

                if (runtime.Engine.StateMachine.Phase == SystemPhase.Ready)
                {
                    return;
                }
            }

            Assert.Fail(
                "기동 시퀀스가 Ready 에 도달하지 못했습니다. 현재 단계: "
                + runtime.Engine.StateMachine.Phase);
        }

        /// <summary>폴링 → 제어 → 플랜트 진행을 지정 횟수 반복한다.</summary>
        /// <param name="runtime">런타임.</param>
        /// <param name="steps">반복 횟수.</param>
        /// <param name="stepMs">1회 진행 시간 [ms].</param>
        private void RunLoop(EsamRuntime runtime, int steps, double stepMs = 200.0)
        {
            for (int i = 0; i < steps; i++)
            {
                EnsureWithinBudget("RunLoop");

                PollAll(runtime);
                runtime.Engine.ExecuteStep();
                runtime.Plant.Advance(stepMs / 1000.0);
                _clock.AdvanceMs(stepMs);
            }

            // 마지막 플랜트 상태를 스냅샷에 반영한다.
            PollAll(runtime);
        }

        /// <summary>런타임 장애를 1건 유발해 구성 경고를 추가시킨다.</summary>
        /// <param name="runtime">런타임.</param>
        /// <remarks>
        /// 장애 보고는 래치된다. 같은 장애가 해소되기 전에는 다시 알리지 않으므로,
        /// 실패만 반복 주입하면 경고가 한 번만 추가된다.
        /// 성공을 먼저 기록해 래치를 풀고 나서 다시 임계까지 올려야 매번 추가된다.
        /// </remarks>
        private void InjectRuntimeWarning(EsamRuntime runtime)
        {
            runtime.Diagnostics.RecordEvaluationSuccess();

            for (int i = 0; i < runtime.Diagnostics.EvaluationFailureThreshold; i++)
            {
                runtime.Diagnostics.RecordEvaluationFailure(
                    new InvalidOperationException("주입된 판정 예외"), _clock.UtcNow);
            }
        }

        /// <summary>지정 포트의 시뮬레이션 전송 계층을 얻는다.</summary>
        private static SimulatedModbusTransport Transport(EsamRuntime runtime, string portId)
        {
            SimulatedModbusTransport transport =
                runtime.FindTransport(portId) as SimulatedModbusTransport;

            Assert.NotNull(transport);
            return transport;
        }

        // ── 구성 및 배선 ────────────────────────────────────────────────────────

        [Fact]
        public void 런타임이_포트_2개와_체인_5개로_구성된다()
        {
            EsamRuntime runtime = CreateRuntime();

            Assert.Equal(2, runtime.Workers.Count);
            Assert.Equal(5, runtime.Control.Chains.Count);
            Assert.NotNull(runtime.Store);
            Assert.NotNull(runtime.Engine);
            Assert.NotNull(runtime.Interlock);
            Assert.NotNull(runtime.Plant);
        }

        [Fact]
        public void 구성_검증에_실패하면_런타임을_만들지_않는다()
        {
            // 슬레이브 ID 충돌은 실제 버스에서 두 장치가 동시에 응답해 프레임을 깨뜨린다.
            // 검증 실패 상태로 통신을 시작하게 해서는 안 된다.
            DeviceMap broken = CreateMap();
            broken.Devices.Add(new DeviceInstanceDefinition
            {
                Id = "S9-9", Type = "DiffPressure", Port = "CH1", SlaveId = 1
            });

            RuntimeOptions options = new RuntimeOptions();
            options.Sensor1Ids = Sensor1Ids;

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                () => EsamRuntime.Create(broken, CreateControl(), options, _clock));

            Assert.Contains("슬레이브", ex.Message);
        }

        [Fact]
        public void IL01_임계값은_운전_대역과_분리된_절대값이다()
        {
            // 안전 임계값이 운전 대역에서 파생되면 두 가지가 깨진다.
            //   1) 작업자가 Config 에서 설정값을 바꾸면 안전 임계값도 함께 움직인다.
            //   2) 대역 상한 -100 Pa 는 밸브 닫힘 상태(-50 Pa)보다 아래라 정지 중에도 조건이 참이다.
            // 0 Pa 는 대기압이라는 물리 경계이고, 두 문제 모두에서 자유롭다.
            EsamRuntime runtime = CreateRuntime();
            InterlockRule rule = runtime.Interlock.FindRule("IL-01");

            Assert.NotNull(rule);
            Assert.True(rule.ThresholdPa.HasValue, "안전 임계값은 명시되어야 한다.");
            Assert.Equal(0.0, rule.ThresholdPa.Value);

            // 운전 설정을 바꿔도 임계값은 그대로다.
            runtime.Control.Modes[SensorMode.Sensor3] = new ModeSetting(-500.0, 300.0, 300.0);
            Assert.Equal(0.0, runtime.Interlock.FindRule("IL-01").ThresholdPa.Value);
        }

        [Fact]
        public void IL01_임계값_미지정은_구성_경고로_보고된다()
        {
            // 폴백을 쓰면 전원 투입 직후 래치되어 기동이 불가능해진다. 조용히 넘어가면 안 된다.
            List<InterlockRule> rules = new List<InterlockRule>(InterlockEvaluator.CreateDefaultRules());

            foreach (InterlockRule rule in rules)
            {
                if (rule.Id == "IL-01")
                {
                    rule.ThresholdPa = null;
                }
            }

            ControlConfig control = CreateControl();

            RuntimeOptions options = new RuntimeOptions();
            options.Sensor1Ids = Sensor1Ids;
            options.InterlockRules = rules;
            options.Recipe = BuildRecipe(control);

            _runtime = Track(EsamRuntime.Create(CreateMap(), control, options, _clock));

            Assert.Contains(_runtime.Warnings, w => w.Message.Contains("IL-01"));
        }

        [Fact]
        public void 안전_입력_PLC가_없으면_경고한다()
        {
            // PLC 미배선 상태에서는 IL-02~IL-05 가 전부 무력하다.
            // IL-04 를 항상 발동시키면 아무것도 검증할 수 없으므로 판정은 끄되, 사실은 남긴다.
            EsamRuntime runtime = CreateRuntime();

            Assert.False(runtime.Control.SafetyInputsConfigured);
            Assert.Contains(runtime.Warnings, w => w.Message.Contains("안전 입력"));
        }

        // ── 스냅샷 조립 ─────────────────────────────────────────────────────────

        [Fact]
        public void 폴링_결과가_스냅샷으로_조립된다()
        {
            EsamRuntime runtime = CreateRuntime();
            PollAll(runtime);

            SystemSnapshot snapshot = runtime.Store.Current;

            Assert.Equal(13, snapshot.Pressures.Count);
            Assert.Equal(5, snapshot.Valves.Count);
            Assert.Equal(5, snapshot.Fans.Count);

            PressureReading reading = snapshot.FindPressure("S2-1");

            Assert.NotNull(reading);
            Assert.Equal(Quality.Good, reading.Quality);

            // ★ 절대값 대신 플랜트의 참값과 대조한다.
            // 리터럴로 적으면 스케일 팩터가 어긋났을 때 통과할 수 있다.
            // 스케일이 어긋나면 측정값이 배수만큼 틀리지만 각 계층은 자기 기준으로
            // 일관하므로 어디에서도 오류가 나지 않는다. 왕복이 참값으로 돌아오는지가
            // 확인해야 할 성질이다.
            double truePv;
            Assert.True(runtime.Plant.TryGetTruePressure("S2-1", out truePv));

            // 허용오차는 인코딩 양자화(1 LSB)와 센서 노이즈에서 나온다.
            // 센서 2 의 노이즈 표준편차는 0.8 Pa 이므로 4 Pa 는 5σ 다.
            // 스케일이 어긋나면(예: 10배) 20 Pa 가 200 Pa 로 읽히므로 이 폭으로도 확실히 잡힌다.
            Assert.InRange(reading.Pa, truePv - 4.0, truePv + 4.0);

            // 밸브 닫힘·팬 정지 상태이므로 플랜트 base = +20 Pa 부근이어야 한다.
            // 참값 자체가 엉뚱하면 위 대조는 둘이 함께 틀려도 통과한다.
            Assert.InRange(truePv, 16.0, 24.0);
        }

        [Fact]
        public void 밸브_상태가_pulse와_개도율로_변환된다()
        {
            EsamRuntime runtime = CreateRuntime();

            // 원점 복귀를 마친 상태로 둔다.
            // 이 테스트가 검증하는 것은 단위 변환이지 원점 복귀 시퀀스가 아니다.
            // 시퀀스 자체는 별도 테스트가 실제로 돌려서 확인한다.
            //
            // PreHomeValves 기본값이 false 로 바뀐 뒤(D3) 플랜트는 미복귀 상태로 시작하므로
            // 명시하지 않으면 IsHomeDone 이 false 다.
            runtime.Plant.CompleteAllHoming();

            runtime.Plant.ApplyCommand(ActuatorCommand.SetValvePosition(
                "V-1", 2500, CommandPriority.Automatic, "테스트"));

            // 슬루율 1000 pulse/s → 2500 pulse 도달에 2.5초.
            runtime.Plant.Advance(5.0);
            PollAll(runtime);

            ValveState valve = runtime.Store.Current.FindValve("V-1");

            Assert.NotNull(valve);
            Assert.Equal(2500, valve.PositionPulse);
            Assert.Equal(50.0, valve.PositionPercent, 1);
            Assert.Equal(45.0, valve.PositionDegree, 1);
            Assert.True(valve.IsHomeDone);
        }

        [Fact]
        public void 통신_실패한_디바이스는_품질이_Bad로_격하된다()
        {
            // 낡은 값을 Good 으로 남기면 통신이 끊긴 센서를 근거로 밸브를 움직인다.
            EsamRuntime runtime = CreateRuntime();
            PollAll(runtime);

            Assert.Equal(Quality.Good, runtime.Store.Current.FindPressure("S2-1").Quality);

            // S2-1 은 센서 목록 4번째이므로 슬레이브 4번이다.
            Assert.True(Transport(runtime, "CH1").DetachSlave(4));

            PollAll(runtime);

            PressureReading degraded = runtime.Store.Current.FindPressure("S2-1");

            Assert.Equal(Quality.Bad, degraded.Quality);

            // 값 자체는 참고용으로 남긴다. 0 으로 지우면 트렌드에 가짜 급락이 기록된다.
            double truePv;
            Assert.True(runtime.Plant.TryGetTruePressure("S2-1", out truePv));
            Assert.InRange(degraded.Pa, truePv - 4.0, truePv + 4.0);

            // 같은 버스의 다른 센서는 영향을 받지 않는다.
            Assert.Equal(Quality.Good, runtime.Store.Current.FindPressure("S2-2").Quality);
        }

        [Fact]
        public void 스냅샷은_교체될_때마다_회차가_증가한다()
        {
            EsamRuntime runtime = CreateRuntime();

            long before = runtime.Store.Revision;
            PollAll(runtime);

            Assert.True(runtime.Store.Revision > before);
        }

        [Fact]
        public void PLC가_없으면_디지털_입력이_NoData로_남는다()
        {
            // NoData 와 Bad 를 구분하는 것이 중요하다. Bad 이면 IL-04(통신 상실)가 발동해
            // 아직 배선되지 않은 PLC 때문에 전 체인이 정지한다.
            EsamRuntime runtime = CreateRuntime();
            PollAll(runtime);

            Assert.Equal(Quality.NoData, runtime.Store.Current.Plc.Quality);
            Assert.False(runtime.Interlock.IsTripped);
        }

        // ── 자동 운전 진입 ──────────────────────────────────────────────────────

        [Fact]
        public void 자동_운전은_Ready_단계에서만_진입한다()
        {
            EsamRuntime runtime = CreateRuntime();

            Assert.Equal(SystemPhase.Idle, runtime.Engine.StateMachine.Phase);
            Assert.False(runtime.Engine.RequestAuto());

            AdvanceToReady(runtime);

            Assert.Equal(SystemPhase.Ready, runtime.Engine.StateMachine.Phase);
            Assert.True(runtime.Engine.RequestAuto());
            Assert.Equal(SystemPhase.AutoControl, runtime.Engine.StateMachine.Phase);
        }

        [Fact]
        public void 팬_사양이_미확보면_자동_운전을_거부한다()
        {
            // MaxRpm 이 0 이면 증속 제어가 불가능하다(Open Issue #20).
            // 그 상태로 자동 운전에 들어가면 압력 과다 시 대응 수단이 없다.
            ControlConfig control = CreateControl();
            control.Fan.MaxRpm = 0.0;

            EsamRuntime runtime = CreateRuntime(control);
            AdvanceToReady(runtime);

            Assert.Equal(SystemPhase.Ready, runtime.Engine.StateMachine.Phase);
            Assert.False(runtime.Engine.RequestAuto());
        }

        [Fact]
        public void 전원_투입_직후_인터록이_발동하지_않는다()
        {
            // ★ 회귀 방지.
            // 밸브 닫힘 상태의 센서 3 압력은 -50 Pa 로, 운전 대역 상한(-100 Pa)보다 위다.
            // 대역 상한을 안전 임계값으로 쓰면 전원 투입 직후 래치되어(Manual 정책)
            // 장비가 영구히 기동 불가 상태가 된다. 임계값 0 Pa 는 이 구간을 포함하지 않는다.
            EsamRuntime runtime = CreateRuntime();

            for (int i = 0; i < 20; i++)
            {
                PollAll(runtime);
                runtime.Plant.Advance(0.2);
                _clock.AdvanceMs(200);
            }

            PressureReading sensor3 = runtime.Store.Current.FindPressure("S3-1");

            // 대역 상한보다 위이지만 대기압보다는 아래다. 정지 상태로서 정상이다.
            Assert.True(sensor3.Pa > -100.0, "정지 상태 압력은 운전 대역 상한보다 위다.");
            Assert.True(sensor3.Pa < 0.0, "정지 상태에서도 음압은 유지된다.");

            Assert.False(runtime.Interlock.IsTripped);

            AdvanceToReady(runtime);
            Assert.True(runtime.Engine.RequestAuto());
        }

        // ── S5: 구성 경고와 종료 파킹 (D10, D5) ────────────────────────────────

        [Fact]
        public void 안전_기능이_빠진_구성에서는_자동_운전에_들어갈_수_없다()
        {
            // ★ D10. 종전에는 경고가 있어도 아무 일 없이 자동 운전이 시작됐다.
            // 화면 연결(S7) 전에도 효력이 생기도록 진입 지점에서 막는다.
            ControlConfig control = CreateControl();

            RuntimeOptions options = new RuntimeOptions();
            options.Sensor1Ids = Sensor1Ids;
            options.Recipe = BuildRecipe(control);

            _runtime = Track(EsamRuntime.Create(CreateMap(), control, options, _clock));
            OpenTransports(_runtime);

            // 안전 입력 PLC 가 없으므로 차단 경고가 있어야 한다.
            Assert.True(_runtime.HasBlockingWarnings);
            Assert.False(_runtime.WarningsAcknowledged);

            AdvanceToReady(_runtime);

            Assert.False(_runtime.Engine.RequestAuto());
            Assert.Contains("안전", _runtime.Engine.LastAutoRejectReason);
            Assert.Equal(SystemPhase.Ready, _runtime.Engine.StateMachine.Phase);

            // 명시 확인 후에만 진입할 수 있다.
            _runtime.AcknowledgeWarnings();

            Assert.True(_runtime.Engine.RequestAuto());
            Assert.Equal(SystemPhase.AutoControl, _runtime.Engine.StateMachine.Phase);
        }

        [Fact]
        public void 확인해도_경고_목록은_사라지지_않는다()
        {
            // 확인은 "없애는 것" 이 아니라 "인지했음을 기록하는 것" 이다.
            // 목록이 사라지면 화면과 로그에서 근거가 없어진다.
            EsamRuntime runtime = CreateRuntime();

            Assert.True(runtime.WarningsAcknowledged);
            Assert.True(runtime.HasBlockingWarnings);
            Assert.NotEmpty(runtime.Warnings);
        }

        [Fact]
        public void 구성_경고는_심각도로_구분된다()
        {
            // "안전 입력이 없다" 와 "MFC 주소가 미확정이다" 가 같은 무게로 섞이면
            // 목록을 봐도 무엇이 중요한지 알 수 없다.
            EsamRuntime runtime = CreateRuntime();

            Assert.Contains(runtime.Warnings, w => w.IsBlocking);
            Assert.Contains(runtime.Warnings, w => w.Code == "SAFE-01");

            foreach (ConfigWarning warning in runtime.Warnings)
            {
                Assert.False(string.IsNullOrEmpty(warning.Code));
                Assert.False(string.IsNullOrEmpty(warning.Message));
            }
        }

        [Fact]
        public void 경고_목록을_열거하는_중에_추가해도_예외가_없다()
        {
            // ★ Warnings 가 내부 리스트를 그대로 반환하면
            // 워커 스레드가 경고를 추가하는 순간 열거 중인 화면에서
            // InvalidOperationException 이 터진다.
            //
            // 그 예외가 발생하는 곳이 "경고를 보여주려던 화면" 이라는 점이 특히 나쁘다.
            // 안전 기능이 동작하지 않는다는 사실을 알리려는 순간에 화면이 죽는다.
            EsamRuntime runtime = CreateRuntime();

            Assert.NotEmpty(runtime.Warnings);

            int before = runtime.Warnings.Count;

            // 열거 도중에 경고를 추가한다. 실제로는 워커 스레드가 하는 일이지만,
            // 같은 스레드에서 해도 내부 리스트를 직접 반환한다면 동일하게 터진다.
            // 스레드를 띄우면 타이밍에 따라 통과해 버리는 불안정한 테스트가 된다.
            int seen = 0;

            foreach (ConfigWarning warning in runtime.Warnings)
            {
                Assert.NotNull(warning);
                seen++;

                InjectRuntimeWarning(runtime);
            }

            Assert.True(seen > 0);

            // 루프 안의 호출이 실제로 경고를 추가했는지 확인한다.
            // 추가되지 않았다면 위 열거는 경합을 재현하지 못한 것이다.
            Assert.True(
                runtime.Warnings.Count > before,
                "열거 중 경고가 추가되지 않아 경합을 재현하지 못했습니다.");
        }

        [Fact]
        public void 경고_목록_사본을_수정해도_런타임에_반영되지_않는다()
        {
            // 사본을 주는 결정의 이면이다. 외부가 목록을 바꿔 안전 경고를
            // 지워버릴 수 없어야 한다.
            EsamRuntime runtime = CreateRuntime();

            int before = runtime.Warnings.Count;

            runtime.Warnings.Clear();
            runtime.Warnings.Add(ConfigWarning.Advisory("FAKE", "위조 경고", null));

            Assert.Equal(before, runtime.Warnings.Count);
            Assert.DoesNotContain(runtime.Warnings, w => w.Code == "FAKE");
        }

        // ── 테스트 자원 관리 ────────────────────────────────────────────────────

        [Fact]
        public void 런타임을_해제하면_포트가_닫힌다()
        {
            // 닫지 않으면 실장비에서는 시리얼 포트 핸들이, 테스트에서는
            // 시뮬레이션 객체와 이벤트 구독이 남는다.
            EsamRuntime runtime = CreateRuntime();

            foreach (ModbusPortWorker worker in runtime.Workers)
            {
                Assert.True(runtime.FindTransport(worker.PortId).IsOpen);
            }

            List<IModbusTransport> transports = new List<IModbusTransport>();

            foreach (ModbusPortWorker worker in runtime.Workers)
            {
                transports.Add(runtime.FindTransport(worker.PortId));
            }

            runtime.Dispose();

            Assert.NotEmpty(transports);

            foreach (IModbusTransport transport in transports)
            {
                Assert.False(transport.IsOpen, "해제 후에도 포트가 열려 있습니다.");
            }
        }

        [Fact]
        public void 런타임_해제는_두_번_호출해도_안전하다()
        {
            // Dispose 를 테스트 본문과 Dispose() 에서 모두 호출하는 경우가 있다.
            // 두 번째 호출이 던지면 실제 검증 결과가 그 예외로 가려진다.
            EsamRuntime runtime = CreateRuntime();

            runtime.Dispose();
            runtime.Dispose();
        }

        [Fact]
        public void 한_테스트에서_만든_런타임은_모두_정리_대상이_된다()
        {
            // ★ _runtime 하나만 정리하던 종전 방식에서는 두 번째 런타임을 만들면
            // 첫 번째가 조용히 샜다. 생성 지점에서 등록하므로 이제 새지 않는다.
            EsamRuntime first = CreateRuntime();
            EsamRuntime second = CreateRuntime();

            Assert.NotSame(first, second);

            // 두 번째를 만든 뒤에도 첫 번째가 목록에 남아 있어야 한다.
            Assert.Contains(first, _created);
            Assert.Contains(second, _created);
        }

        [Fact]
        public void Describe는_경고_본문을_출력한다()
        {
            // 건수만 찍으면 로그를 봐도 원인을 알 수 없다.
            EsamRuntime runtime = CreateRuntime();
            string text = runtime.Describe();

            Assert.Contains("안전 입력", text);
            Assert.Contains("SAFE-01", text);
        }

        [Fact]
        public void 종료하면_밸브가_닫히고_팬이_멈춘다()
        {
            // ★ D5. 종료하면 폴링이 멈춰 인터록 평가도 함께 끝난다.
            // 그런데 밸브는 열려 있고 팬은 계속 돈다. 아무도 보지 않는 상태로 남는다.
            EsamRuntime runtime = CreateRuntime();
            AdvanceToReady(runtime);
            runtime.Engine.RequestAuto();

            RunLoop(runtime, 100);

            int pulseBefore;
            int targetBefore;
            bool homeBefore;
            runtime.Plant.TryGetValve("V-1", out pulseBefore, out targetBefore, out homeBefore);
            Assert.True(targetBefore > 0, "자동 제어가 밸브를 열어 둔 상태여야 한다.");

            // 워커 스레드를 띄우지 않았으므로 파킹 대기는 건너뛴다.
            // 지령은 큐에 남고, 사이클을 돌려 처리한다.
            runtime.Stop(0);

            PollAll(runtime);
            runtime.Plant.Advance(6.0);
            PollAll(runtime);

            int pulse;
            int target;
            bool home;
            runtime.Plant.TryGetValve("V-1", out pulse, out target, out home);

            Assert.Equal(0, target);
            Assert.Equal(0, pulse);

            double rpm;
            double targetRpm;
            runtime.Plant.TryGetFan("F-1", out rpm, out targetRpm);
            Assert.Equal(0.0, targetRpm);
        }

        [Fact]
        public void 파킹은_비활성_체인도_포함한다()
        {
            // 안전 정지에 예외를 두지 않는다.
            ControlConfig control = CreateControl();
            control.Chains[0].Enabled = false;

            EsamRuntime runtime = CreateRuntime(control);

            // 체인 5조 × (밸브 Close + 팬 OFF) = 10건
            Assert.Equal(10, runtime.Engine.ParkActuators("테스트"));
        }

        [Fact]
        public void 자동_운전을_끄면_액추에이터를_그대로_둔다()
        {
            // 자동 제어를 끄는 것과 기류를 멈추는 것은 다른 요구다.
            // 웨이퍼 처리 중에 송풍팬을 세우면 오히려 봉쇄가 무너진다.
            // 폴링이 계속되므로 인터록이 지킨다.
            EsamRuntime runtime = CreateRuntime();
            AdvanceToReady(runtime);
            runtime.Engine.RequestAuto();

            RunLoop(runtime, 100);

            int pulseBefore;
            int targetBefore;
            bool home;
            runtime.Plant.TryGetValve("V-1", out pulseBefore, out targetBefore, out home);
            Assert.True(targetBefore > 0);

            runtime.Engine.StopAuto();
            PollAll(runtime);
            runtime.Plant.Advance(2.0);
            PollAll(runtime);

            int pulseAfter;
            int targetAfter;
            runtime.Plant.TryGetValve("V-1", out pulseAfter, out targetAfter, out home);

            Assert.Equal(targetBefore, targetAfter);
            Assert.Equal(SystemPhase.Ready, runtime.Engine.StateMachine.Phase);
        }

        [Fact]
        public void 알람_규칙이_기본_경로에서_로드된다()
        {
            // ★ D9. 종전에는 RuntimeOptions 가 AlarmRules 를 채우는 코드가 없어
            // DESIGN 5.1 의 알람 31종이 어떤 구성에서도 동작하지 않았다.
            EsamRuntime runtime = CreateRuntime();

            Assert.NotNull(runtime.Alarms);
            Assert.True(
                runtime.Alarms.RuleCount >= 31,
                "알람 규칙이 31종에 미치지 못합니다: " + runtime.Alarms.RuleCount);
        }

        // ── S5: 안전 경로 실패 감지 (D6, D11) ──────────────────────────────────

        [Fact]
        public void 판정_예외가_연속되면_SafeStop으로_보낸다()
        {
            // ★ D11. 종전에는 폴링 완료 처리의 예외가 포트 워커의 catch-all 로 흘러가
            // 흔적 없이 사라졌다. 워커는 살아남지만 인터록 평가는 그 사이클부터 수행되지 않고,
            // 예외가 결정적이면 인터록이 영구히 꺼진 채 운전이 계속된다.
            EsamRuntime runtime = CreateRuntime();
            AdvanceToReady(runtime);

            // 판정 경로 자체를 깨뜨린다. 이벤트 구독자 예외는 삼켜지므로 소용이 없다.
            // 활성 센서 모드의 설정을 제거하면 BuildStatus() → GetSetting() → GetMode() 가
            // InvalidOperationException 을 던진다. OnPollCompleted 의 첫 단계다.
            // 레시피가 있어도 이탈 확정 시간(Time)은 모드별 공통값에서 오므로 경로가 유지된다.
            runtime.Control.Modes.Remove(runtime.Control.ActiveMode);

            int threshold = runtime.Diagnostics.EvaluationFailureThreshold;

            for (int i = 0; i < threshold + 2; i++)
            {
                PollAll(runtime);
            }

            Assert.True(
                runtime.Diagnostics.TotalEvaluationFailures >= threshold,
                "판정 예외가 집계되지 않았습니다: " + runtime.Diagnostics.TotalEvaluationFailures);

            Assert.Equal(SystemPhase.SafeStop, runtime.Engine.StateMachine.Phase);
            Assert.Contains(runtime.Warnings, w => w.Code.StartsWith("RUN-", StringComparison.Ordinal));
        }

        [Fact]
        public void 판정이_정상이면_예외_카운터가_초기화된다()
        {
            EsamRuntime runtime = CreateRuntime();

            PollAll(runtime);
            PollAll(runtime);

            Assert.Equal(0, runtime.Diagnostics.ConsecutiveEvaluationFailures);
            Assert.Equal(0L, runtime.Diagnostics.TotalEvaluationFailures);
        }

        [Fact]
        public void 인터록_지령이_담당_워커에서_실패하면_센다()
        {
            // ★ D6. CommandFailed 구독자가 하나도 없어, 안전 지령이 장치에 닿지 못해도
            // 아무도 알지 못했다. CloseValve 는 위치 설정 → PR0 이동 2단 시퀀스라
            // 두 번째가 타임아웃하면 밸브가 전혀 움직이지 않는다.
            EsamRuntime runtime = CreateRuntime();
            AdvanceToReady(runtime);
            runtime.Engine.RequestAuto();

            RunLoop(runtime, 100);

            // 밸브를 분리해 인터록 지령이 실패하게 만든다.
            SimulatedModbusTransport bus = Transport(runtime, "CH2");

            for (byte slave = 1; slave <= 5; slave++)
            {
                Assert.True(bus.DetachSlave(slave));
            }

            LoseExhaust(runtime);

            for (int i = 0; i < 10; i++)
            {
                PollAll(runtime);
                _clock.AdvanceMs(200);
            }

            Assert.True(runtime.Interlock.IsTripped);
            Assert.True(
                runtime.Diagnostics.TotalInterlockCommandFailures > 0,
                "인터록 지령 실패가 집계되지 않았습니다.");
        }

        [Fact]
        public void 담당하지_않는_포트의_실패는_세지_않는다()
        {
            // 인터록 지령은 전 워커에 뿌리므로 담당하지 않는 워커에서는 반드시 실패한다.
            // 그것을 세면 정상 동작이 장애로 집계된다.
            EsamRuntime runtime = CreateRuntime();
            AdvanceToReady(runtime);
            runtime.Engine.RequestAuto();

            RunLoop(runtime, 100);

            LoseExhaust(runtime);
            RunLoop(runtime, 10);

            Assert.True(runtime.Interlock.IsTripped);

            // 밸브·팬은 정상이므로 담당 워커는 성공한다.
            // CH1 워커는 이 디바이스들을 담당하지 않아 실패하지만 집계 대상이 아니다.
            Assert.Equal(0L, runtime.Diagnostics.TotalInterlockCommandFailures);
        }

        [Fact]
        public void 인터록_후_밸브가_열린_채로_남으면_실효_실패를_보고한다()
        {
            // Tripped 는 "지령을 큐에 넣었다" 는 뜻이지 "밸브가 닫혔다" 는 뜻이 아니다.
            EsamRuntime runtime = CreateRuntime();
            AdvanceToReady(runtime);
            runtime.Engine.RequestAuto();

            RunLoop(runtime, 100);

            int pulse;
            int target;
            bool home;
            runtime.Plant.TryGetValve("V-1", out pulse, out target, out home);
            Assert.True(pulse > 0, "밸브가 열려 있는 상태여야 한다.");

            bool reported = false;
            runtime.Diagnostics.FaultDetected += (sender, e) =>
            {
                if (e.Kind == RuntimeFaultKind.InterlockNotEffective)
                {
                    reported = true;
                }
            };

            // 인터록을 발동시키되 밸브는 움직이지 않게 한다.
            // 플랜트를 진행시키지 않으면 지령이 실행되어도 위치가 그대로다.
            LoseExhaust(runtime);
            runtime.Plant.Advance(2.0);

            SimulatedModbusTransport bus = Transport(runtime, "CH2");

            for (byte slave = 1; slave <= 5; slave++)
            {
                bus.DetachSlave(slave);
            }

            for (int i = 0; i < 20; i++)
            {
                PollAll(runtime);
                _clock.AdvanceMs(2000);
            }

            Assert.True(runtime.Interlock.IsTripped);
            Assert.True(reported || runtime.Diagnostics.TotalInterlockCommandFailures > 0,
                "인터록이 효력을 내지 못한 사실이 보고되지 않았습니다.");
        }

        // ── S5: 데이터 신선도와 지령 라우팅 (D8, D7) ──────────────────────────

        [Fact]
        public void 낡은_측정값으로는_인터록을_판정하지_않는다()
        {
            // ★ D8. SnapshotBuilder 의 Stale 임계값은 Slow 티어까지 덮어야 해서 15초다.
            // 그래서 Fast 센서가 14초 갱신되지 않아도 품질은 Good 으로 남는다.
            // 250ms 응답을 목표로 하는 안전 기능이 그 값으로 판정해서는 안 된다.
            EsamRuntime runtime = CreateRuntime();
            AdvanceToReady(runtime);
            runtime.Engine.RequestAuto();

            RunLoop(runtime, 100);

            // 배기를 상실시키되 센서는 갱신되지 않게 한다.
            LoseExhaust(runtime);
            runtime.Plant.Advance(2.0);

            // 시계만 앞으로 돌린다. 폴링을 하지 않으므로 측정값이 낡는다.
            _clock.AdvanceMs(5000);

            InterlockEvaluation evaluation = runtime.Interlock.Evaluate(runtime.Store.Current);

            // 값이 낡았으므로 새로 발동시키지 않고, 판정 불가로 보고해야 한다.
            Assert.True(
                evaluation.HasUnjudgeableChain,
                "낡은 값을 판정 불가로 보고하지 않았습니다.");
        }

        [Fact]
        public void 신선한_값이면_판정_불가로_보고하지_않는다()
        {
            EsamRuntime runtime = CreateRuntime();
            AdvanceToReady(runtime);
            runtime.Engine.RequestAuto();

            RunLoop(runtime, 20);

            InterlockEvaluation evaluation = runtime.Interlock.Evaluate(runtime.Store.Current);

            Assert.False(evaluation.HasUnjudgeableChain);
            Assert.Equal(0, runtime.Diagnostics.ConsecutiveBlindCycles);
        }

        [Fact]
        public void 운전_중_판정_불가가_계속되면_SafeStop으로_보낸다()
        {
            // 센서 3 을 읽지 못하면 배기 상실을 감지할 수단이 없다.
            // "발동하지 않음" 과 "판정하지 못함" 을 같게 취급하면
            // 눈을 감은 상태를 안전하다고 보고하게 된다.
            EsamRuntime runtime = CreateRuntime();
            AdvanceToReady(runtime);
            runtime.Engine.RequestAuto();

            RunLoop(runtime, 20);

            // 센서 3 전량(슬레이브 9~13)을 분리한다.
            SimulatedModbusTransport bus = Transport(runtime, "CH1");

            for (byte slave = 9; slave <= 13; slave++)
            {
                Assert.True(bus.DetachSlave(slave));
            }

            for (int i = 0; i < runtime.Diagnostics.BlindCycleThreshold + 4; i++)
            {
                PollAll(runtime);
                _clock.AdvanceMs(250);
            }

            Assert.Equal(SystemPhase.SafeStop, runtime.Engine.StateMachine.Phase);
        }

        [Fact]
        public void 정지_중에는_판정_불가를_문제로_보지_않는다()
        {
            // 기동 전에는 측정값이 없는 것이 정상이고, 액추에이터도 움직이지 않는다.
            EsamRuntime runtime = CreateRuntime();

            SimulatedModbusTransport bus = Transport(runtime, "CH1");

            for (byte slave = 9; slave <= 13; slave++)
            {
                bus.DetachSlave(slave);
            }

            for (int i = 0; i < runtime.Diagnostics.BlindCycleThreshold + 4; i++)
            {
                PollAll(runtime);
                _clock.AdvanceMs(250);
            }

            Assert.Equal(0, runtime.Diagnostics.ConsecutiveBlindCycles);
            Assert.NotEqual(SystemPhase.SafeStop, runtime.Engine.StateMachine.Phase);
        }

        [Fact]
        public void 인터록_지령은_담당_포트에만_투입된다()
        {
            // ★ D7. 종전에는 전 워커에 뿌리고 담당하지 않는 워커가 무시하게 했다.
            // 담당하지 않는 워커에서는 반드시 실패하므로 CommandFailed 가 쏟아지고,
            // 담당 워커는 같은 지령을 중복 실행한다.
            EsamRuntime runtime = CreateRuntime();
            AdvanceToReady(runtime);
            runtime.Engine.RequestAuto();

            RunLoop(runtime, 100);

            int failures = 0;

            foreach (ModbusPortWorker worker in runtime.Workers)
            {
                worker.CommandFailed += (sender, e) =>
                {
                    if (e.Command.Priority == CommandPriority.Interlock)
                    {
                        failures++;
                    }
                };
            }

            LoseExhaust(runtime);
            RunLoop(runtime, 20);

            Assert.True(runtime.Interlock.IsTripped);

            // 밸브·팬은 모두 CH2 담당이다. 담당 포트로만 갔다면 실패가 없어야 한다.
            Assert.Equal(0, failures);
        }

        [Fact]
        public void 인터록_지령은_매_사이클_반복_투입하지_않는다()
        {
            // 인터록은 래치되므로 발동이 지속되는 동안 매 사이클 같은 지령이 만들어진다.
            // 그대로 투입하면 안전 기능이 활성인 동안 버스가 가장 바빠져
            // 2차 위험 검출이 늦어진다. 정확히 반대로 가는 것이다.
            EsamRuntime runtime = CreateRuntime();
            AdvanceToReady(runtime);
            runtime.Engine.RequestAuto();

            RunLoop(runtime, 100);

            LoseExhaust(runtime);
            runtime.Plant.Advance(2.0);

            // 첫 발동에서는 지령이 나간다.
            PollAll(runtime);
            Assert.True(runtime.Interlock.IsTripped);

            int pendingAfterFirst = 0;

            foreach (ModbusPortWorker worker in runtime.Workers)
            {
                pendingAfterFirst += worker.PendingCommandCount;
            }

            // 시간을 진행시키지 않고 여러 사이클을 돌려도 지령이 쌓이지 않아야 한다.
            for (int i = 0; i < 10; i++)
            {
                runtime.Interlock.Evaluate(runtime.Store.Current);
            }

            int pendingAfterRepeat = 0;

            foreach (ModbusPortWorker worker in runtime.Workers)
            {
                pendingAfterRepeat += worker.PendingCommandCount;
            }

            Assert.Equal(pendingAfterFirst, pendingAfterRepeat);
        }

        [Fact]
        public void 재투입_간격이_지나면_다시_확인_사살한다()
        {
            // 한 번만 보내고 마는 것도 안 된다. 지령이 유실되면 복구할 방법이 없어진다.
            EsamRuntime runtime = CreateRuntime();
            AdvanceToReady(runtime);
            runtime.Engine.RequestAuto();

            RunLoop(runtime, 100);

            LoseExhaust(runtime);
            runtime.Plant.Advance(2.0);
            PollAll(runtime);

            Assert.True(runtime.Interlock.IsTripped);

            // 큐를 비운 뒤 재투입 간격을 넘긴다.
            PollAll(runtime);
            _clock.AdvanceMs(runtime.Interlock.ReassertIntervalMs + 100);

            runtime.Interlock.Evaluate(runtime.Store.Current);

            int pending = 0;

            foreach (ModbusPortWorker worker in runtime.Workers)
            {
                pending += worker.PendingCommandCount;
            }

            Assert.True(pending > 0, "재투입 간격이 지났는데도 지령이 다시 나가지 않았습니다.");
        }

        // ── S5: 기동 시퀀스 (D3) ────────────────────────────────────────────────

        [Fact]
        public void 기동_시퀀스가_원점_복귀를_실제로_지령한다()
        {
            // ★ 회귀 방지.
            // 종전에는 조립 루트가 HomingCompleted 를 확인 없이 발생시켜,
            // 원점 복귀 지령이 프로덕션 경로에서 한 번도 전송되지 않았다.
            EsamRuntime runtime = CreateRuntime();

            // 전원 투입 직후에는 원점이 미확정이다.
            PollAll(runtime);
            Assert.False(runtime.Store.Current.FindValve("V-1").IsHomeDone);

            runtime.Engine.StateMachine.Fire(SystemTrigger.Start);
            Assert.Equal(SystemPhase.Init, runtime.Engine.StateMachine.Phase);

            // 통신이 확인되면 원점 복귀 단계로 넘어간다.
            PollAll(runtime);
            runtime.Engine.ExecuteStep();
            Assert.Equal(SystemPhase.ValveHoming, runtime.Engine.StateMachine.Phase);

            // 원점 복귀 지령이 5대 모두에 나가야 한다.
            PollAll(runtime);
            Assert.Equal(5, runtime.Engine.ExecuteStep());

            // 지령이 실행되면 완료 상태가 되고 Ready 로 넘어간다.
            PollAll(runtime);
            runtime.Engine.ExecuteStep();

            Assert.Equal(SystemPhase.Ready, runtime.Engine.StateMachine.Phase);
            Assert.True(runtime.Store.Current.FindValve("V-1").IsHomeDone);
        }

        [Fact]
        public void 원점_복귀_지령은_매_스텝_반복하지_않는다()
        {
            // 복귀 중인 드라이브에 같은 지령을 다시 보내면 동작을 재시작해 영영 끝나지 않는다.
            EsamRuntime runtime = CreateRuntime();

            runtime.Engine.StateMachine.Fire(SystemTrigger.Start);
            PollAll(runtime);
            runtime.Engine.ExecuteStep();   // Init → ValveHoming

            Assert.Equal(5, runtime.Engine.ExecuteStep());   // 1회차: 5대 지령

            // 폴링 없이 다시 스텝을 밟아도 추가 지령이 나가지 않는다.
            Assert.Equal(0, runtime.Engine.ExecuteStep());
            Assert.Equal(0, runtime.Engine.ExecuteStep());
        }

        [Fact]
        public void 원점_복귀가_끝나지_않으면_타임아웃_후_Fault가_된다()
        {
            // 미완료 상태로 Ready 에 올리면 제어가 성립하지 않는 채 운전에 들어간다.
            // 밴드 제어가 매 스텝 Skipped 를 반환하는데 화면은 정상으로 보인다.
            EsamRuntime runtime = CreateRuntime();

            // 밸브가 원점 복귀 지령에 응답하지 않는 상황을 만든다(슬레이브 1~5 분리).
            SimulatedModbusTransport bus = Transport(runtime, "CH2");

            runtime.Engine.StateMachine.Fire(SystemTrigger.Start);
            PollAll(runtime);
            runtime.Engine.ExecuteStep();   // Init → ValveHoming

            for (byte slave = 1; slave <= 5; slave++)
            {
                Assert.True(bus.DetachSlave(slave));
            }

            for (int i = 0; i < 400; i++)
            {
                PollAll(runtime);
                runtime.Engine.ExecuteStep();
                _clock.AdvanceMs(200);

                if (runtime.Engine.StateMachine.Phase == SystemPhase.Fault)
                {
                    break;
                }
            }

            Assert.Equal(SystemPhase.Fault, runtime.Engine.StateMachine.Phase);
        }

        [Fact]
        public void 장애로_올린_SafeStop은_정상_판정으로_풀리지_않는다()
        {
            // ★ 회귀 방지. 인터록이 올리지 않은 SafeStop 을 인터록이 풀고 있었다.
            //
            // 판정 불가가 계속되어 SafeStop 으로 갔는데, 바로 다음 사이클의 판정이
            // "인터록 미발동" 이므로 SafeStopCleared 를 내서 Fault 로 내려갔다.
            // 그리고 그 사이클에서 판정 불가 카운터까지 0 으로 되돌아가므로
            // 다시 올릴 수도 없었다. 장애를 알리고 즉시 잊는 동작이다.
            EsamRuntime runtime = CreateRuntime();
            AdvanceToReady(runtime);
            runtime.Engine.RequestAuto();

            RunLoop(runtime, 20);

            // 센서 3 전량을 분리해 판정 불가를 만든다.
            SimulatedModbusTransport bus = Transport(runtime, "CH1");

            for (byte slave = 9; slave <= 13; slave++)
            {
                Assert.True(bus.DetachSlave(slave));
            }

            for (int i = 0; i < runtime.Diagnostics.BlindCycleThreshold + 4; i++)
            {
                PollAll(runtime);
                _clock.AdvanceMs(250);
            }

            Assert.Equal(SystemPhase.SafeStop, runtime.Engine.StateMachine.Phase);
            Assert.Contains(runtime.Warnings, w => w.Code == "RUN-3");

            // 센서를 되살린다. 인터록은 이제 정상 판정을 하지만
            // 장애로 올린 정지는 그것으로 풀리지 않는다.
            for (byte slave = 9; slave <= 13; slave++)
            {
                bus.AddSlave(new SimulatedPressureSensor(
                    slave, runtime.Plant, "S3-" + (slave - 8)));
            }

            for (int i = 0; i < 20; i++)
            {
                PollAll(runtime);
                _clock.AdvanceMs(250);
            }

            Assert.Equal(SystemPhase.SafeStop, runtime.Engine.StateMachine.Phase);

            // 작업자가 원인을 확인하고 해제해야 내려간다.
            Assert.True(runtime.ResetRuntimeFault());

            // Ready 가 아니라 Fault 다. 원점 복귀를 다시 거쳐야 한다.
            Assert.Equal(SystemPhase.Fault, runtime.Engine.StateMachine.Phase);

            // 두 번 호출하면 해제할 것이 없다.
            Assert.False(runtime.ResetRuntimeFault());
        }

        [Fact]
        public void Stop_후_재시작하면_원점_복귀를_다시_거친다()
        {
            // Stop 이 단계를 되돌리지 않으면 Start 트리거가 무시되고
            // 초기화·원점 복귀를 건너뛴 채 자동 운전 상태에서 재개된다.
            EsamRuntime runtime = CreateRuntime();
            AdvanceToReady(runtime);
            runtime.Engine.RequestAuto();

            Assert.Equal(SystemPhase.AutoControl, runtime.Engine.StateMachine.Phase);

            runtime.Stop();
            Assert.Equal(SystemPhase.Idle, runtime.Engine.StateMachine.Phase);

            // 재시작하면 Init 부터 시작한다.
            runtime.Engine.StateMachine.Fire(SystemTrigger.Start);
            Assert.Equal(SystemPhase.Init, runtime.Engine.StateMachine.Phase);
        }

        [Fact]
        public void 원점_복귀_중_인터록이_발동하면_단계에_반영된다()
        {
            // ★ 회귀 방지.
            // 종전에는 InterlockRaised 가 Ready·AutoControl 에서만 처리되고,
            // 가드는 엣지를 이미 소비해 재시도하지 않았다.
            // 액추에이터는 강제 정지 중인데 화면에는 인터록이 뜨지 않았다.
            EsamRuntime runtime = CreateRuntime();

            runtime.Engine.StateMachine.Fire(SystemTrigger.Start);
            PollAll(runtime);
            runtime.Engine.ExecuteStep();

            Assert.Equal(SystemPhase.ValveHoming, runtime.Engine.StateMachine.Phase);

            LoseExhaust(runtime);
            runtime.Plant.Advance(2.0);
            PollAll(runtime);

            Assert.True(runtime.Interlock.IsTripped);
            Assert.Equal(SystemPhase.Interlocked, runtime.Engine.StateMachine.Phase);
        }

        [Fact]
        public void 전_체인_정지는_Interlocked가_아니라_SafeStop으로_간다()
        {
            // ★ D1. EMO·차단기·안전입력 상실은 물리 안전장치가 동작한 상황이다.
            // Interlocked 는 해제 시 Ready 로 바로 복귀하지만,
            // SafeStop 은 Fault → Init → 원점 복귀를 거치게 되어 있다.
            // 밸브 위치를 다시 확인하지 않고 재가동해서는 안 된다.
            List<InterlockRule> rules = new List<InterlockRule>(InterlockEvaluator.CreateDefaultRules());

            foreach (InterlockRule rule in rules)
            {
                if (rule.Id == "IL-01")
                {
                    // 전 체인 정지로 승격시켜 SafeStop 경로를 검증한다.
                    rule.Scope = InterlockScope.System;
                }
            }

            ControlConfig control = CreateControl();

            RuntimeOptions options = new RuntimeOptions();
            options.Sensor1Ids = Sensor1Ids;
            options.InterlockRules = rules;
            options.Recipe = BuildRecipe(control);

            _runtime = Track(EsamRuntime.Create(CreateMap(), control, options, _clock));
            _runtime.AcknowledgeWarnings();
            OpenTransports(_runtime);

            AdvanceToReady(_runtime);
            _runtime.Engine.RequestAuto();

            RunLoop(_runtime, 100);

            LoseExhaust(_runtime);
            RunLoop(_runtime, 20);

            Assert.True(_runtime.Interlock.RequiresSystemStop);
            Assert.Equal(SystemPhase.SafeStop, _runtime.Engine.StateMachine.Phase);

            // SafeStop 에서는 어떤 트리거로도 빠져나갈 수 없다.
            Assert.False(_runtime.Engine.RequestAuto());
            Assert.Equal(SystemPhase.SafeStop, _runtime.Engine.StateMachine.Phase);
        }

        [Fact]
        public void SafeStop은_해제되면_Ready가_아니라_Fault로_간다()
        {
            List<InterlockRule> rules = new List<InterlockRule>(InterlockEvaluator.CreateDefaultRules());

            foreach (InterlockRule rule in rules)
            {
                if (rule.Id == "IL-01")
                {
                    rule.Scope = InterlockScope.System;
                }
            }

            ControlConfig control = CreateControl();

            RuntimeOptions options = new RuntimeOptions();
            options.Sensor1Ids = Sensor1Ids;
            options.InterlockRules = rules;
            options.Recipe = BuildRecipe(control);

            _runtime = Track(EsamRuntime.Create(CreateMap(), control, options, _clock));
            _runtime.AcknowledgeWarnings();
            OpenTransports(_runtime);

            AdvanceToReady(_runtime);
            _runtime.Engine.RequestAuto();
            RunLoop(_runtime, 100);

            LoseExhaust(_runtime);
            RunLoop(_runtime, 20);
            Assert.Equal(SystemPhase.SafeStop, _runtime.Engine.StateMachine.Phase);

            // 배기 복구 후 Reset 해야 래치가 풀린다.
            RestoreExhaust(_runtime);
            RunLoop(_runtime, 30);
            _runtime.Interlock.Reset("IL-01");
            PollAll(_runtime);

            // 물리 안전장치 동작 후에는 원점 복귀를 다시 거쳐야 하므로 Fault 로 간다.
            Assert.Equal(SystemPhase.Fault, _runtime.Engine.StateMachine.Phase);
        }

        // ── C2: 센서별 설정값 (recipe) ──────────────────────────────────────────

        [Fact]
        public void 센서별로_다른_설정값이_적용된다()
        {
            // ★ 종전에는 모드별 공통값을 전 체인이 공유했다.
            // 배기 저항이 통로마다 다르면 통로별로 다른 설정값이 필요하다.
            ControlConfig control = CreateControl();

            RecipeDefinition recipe = new RecipeDefinition();
            recipe.Sensors.Add(new SensorSetting("S2-1", -10.0, -15.0,  -5.0));
            recipe.Sensors.Add(new SensorSetting("S2-2", -20.0, -25.0, -15.0));
            recipe.Sensors.Add(new SensorSetting("S2-3", -30.0, -35.0, -25.0));
            recipe.Sensors.Add(new SensorSetting("S2-4", -40.0, -45.0, -35.0));
            recipe.Sensors.Add(new SensorSetting("S2-5", -50.0, -55.0, -45.0));

            RuntimeOptions options = new RuntimeOptions();
            options.Sensor1Ids = Sensor1Ids;
            options.Recipe = recipe;

            _runtime = Track(EsamRuntime.Create(CreateMap(), control, options, _clock));
            _runtime.AcknowledgeWarnings();
            OpenTransports(_runtime);

            PollAll(_runtime);

            ControlStatus status = _runtime.Store.Current.Control;

            Assert.Equal(5, status.Chains.Count);
            Assert.Equal(-10.0, status.Chains[0].SetpointPa);
            Assert.Equal(-30.0, status.Chains[2].SetpointPa);
            Assert.Equal(-50.0, status.Chains[4].SetpointPa);
        }

        [Fact]
        public void 비대칭_대역이_제어에_그대로_전달된다()
        {
            // recipe 는 상한과 하한을 독립적으로 준다.
            ControlConfig control = CreateControl();

            RecipeDefinition recipe = new RecipeDefinition();

            for (int i = 1; i <= 5; i++)
            {
                // 하한 여유 30 Pa, 상한 여유 5 Pa — 비대칭
                recipe.Sensors.Add(new SensorSetting("S2-" + i, -10.0, -40.0, -5.0));
            }

            RuntimeOptions options = new RuntimeOptions();
            options.Sensor1Ids = Sensor1Ids;
            options.Recipe = recipe;

            _runtime = Track(EsamRuntime.Create(CreateMap(), control, options, _clock));
            _runtime.AcknowledgeWarnings();
            OpenTransports(_runtime);

            PollAll(_runtime);

            ChainStatus chain = _runtime.Store.Current.Control.Chains[0];

            Assert.Equal(-40.0, chain.LowLimitPa);
            Assert.Equal(-5.0, chain.HighLimitPa);
        }

        [Fact]
        public void 레시피에_센서가_빠지면_자동_운전을_거부한다()
        {
            // 공통값으로 메우면 그 체인만 조용히 다른 기준으로 제어된다.
            // 일부 통로만 제어되면서 화면은 정상으로 보이는 상태를 막는다.
            ControlConfig control = CreateControl();

            RecipeDefinition recipe = new RecipeDefinition();

            // S2-5 를 일부러 빼둔다.
            for (int i = 1; i <= 4; i++)
            {
                recipe.Sensors.Add(new SensorSetting("S2-" + i, -10.0, -15.0, -5.0));
            }

            RuntimeOptions options = new RuntimeOptions();
            options.Sensor1Ids = Sensor1Ids;
            options.Recipe = recipe;

            _runtime = Track(EsamRuntime.Create(CreateMap(), control, options, _clock));
            _runtime.AcknowledgeWarnings();
            OpenTransports(_runtime);

            AdvanceToReady(_runtime);

            Assert.False(_runtime.Engine.RequestAuto());
            Assert.Contains("S2-5", _runtime.Engine.LastAutoRejectReason);
        }

        [Fact]
        public void 레시피가_없으면_모드별_공통값으로_동작한다()
        {
            // 레시피 도입 전 거동이다. 제어는 성립하므로 막지 않는다.
            ControlConfig control = CreateControl(-10.0, 5.0, 1.0);

            RuntimeOptions options = new RuntimeOptions();
            options.Sensor1Ids = Sensor1Ids;
            options.RecipePath = null;

            _runtime = Track(EsamRuntime.Create(CreateMap(), control, options, _clock));
            _runtime.AcknowledgeWarnings();
            OpenTransports(_runtime);

            Assert.Null(_runtime.Control.Recipe);

            PollAll(_runtime);

            // 전 체인이 같은 목표를 공유한다.
            ControlStatus status = _runtime.Store.Current.Control;

            foreach (ChainStatus chain in status.Chains)
            {
                Assert.Equal(-10.0, chain.SetpointPa);
                Assert.Equal(-15.0, chain.LowLimitPa);
                Assert.Equal(-5.0, chain.HighLimitPa);
            }
        }

        [Fact]
        public void 레시피가_없으면_구성_경고로_알린다()
        {
            // 조용히 넘어가면 통로별로 값을 넣었다고 믿은 채 공통값으로 운전하게 된다.
            RuntimeOptions options = new RuntimeOptions();
            options.Sensor1Ids = Sensor1Ids;
            options.RecipePath = null;

            _runtime = Track(EsamRuntime.Create(CreateMap(), CreateControl(), options, _clock));

            Assert.Contains(_runtime.Warnings, w => w.Code == "RCP-01");
        }

        [Fact]
        public void 센서별_설정으로_각_통로가_다른_압력에_수렴한다()
        {
            // 센서별 설정값이 실제로 제어에 반영되는지 종단 검증.
            // 플랜트는 체인마다 동일한 물리 모델이므로, 수렴점 차이는 설정값 차이에서만 나온다.
            ControlConfig control = CreateControl();

            RecipeDefinition recipe = new RecipeDefinition();
            recipe.Sensors.Add(new SensorSetting("S2-1",  -5.0, -10.0,  0.0));
            recipe.Sensors.Add(new SensorSetting("S2-2", -10.0, -15.0, -5.0));
            recipe.Sensors.Add(new SensorSetting("S2-3", -15.0, -20.0, -10.0));
            recipe.Sensors.Add(new SensorSetting("S2-4", -20.0, -25.0, -15.0));
            recipe.Sensors.Add(new SensorSetting("S2-5", -25.0, -30.0, -20.0));

            RuntimeOptions options = new RuntimeOptions();
            options.Sensor1Ids = Sensor1Ids;
            options.Recipe = recipe;

            _runtime = Track(EsamRuntime.Create(CreateMap(), control, options, _clock));
            _runtime.AcknowledgeWarnings();
            OpenTransports(_runtime);

            AdvanceToReady(_runtime);
            Assert.True(_runtime.Engine.RequestAuto());

            RunLoop(_runtime, 400);

            double[] expected = { -5.0, -10.0, -15.0, -20.0, -25.0 };

            for (int i = 0; i < 5; i++)
            {
                double truePv;
                Assert.True(_runtime.Plant.TryGetTruePressure("S2-" + (i + 1), out truePv));

                Assert.InRange(truePv, expected[i] - 5.5, expected[i] + 5.5);
            }
        }

        // ── 자동 제어 ───────────────────────────────────────────────────────────

        [Fact]
        public void 자동_제어가_압력을_목표_대역으로_수렴시킨다()
        {
            // ★ S4 의 핵심 검증.
            // 폴링 → 스냅샷 → 제어 판단 → 지령 → 밸브 구동 → 압력 변화가
            // 한 바퀴 돌아 실제로 수렴하는지 확인한다.
            EsamRuntime runtime = CreateRuntime();
            AdvanceToReady(runtime);

            Assert.True(runtime.Engine.RequestAuto());

            RunLoop(runtime, 400);

            Assert.Equal(SystemPhase.AutoControl, runtime.Engine.StateMachine.Phase);

            // 노이즈를 배제한 참값으로 판정한다. 측정값은 σ=0.8 Pa 만큼 흔들린다.
            double truePv;
            Assert.True(runtime.Plant.TryGetTruePressure("S2-1", out truePv));
            Assert.InRange(truePv, -15.0, -5.0);

            // 제어가 실제로 밸브를 구동했는지 확인한다.
            // 목표 -10 Pa 는 개도 약 75%(3750 pulse) 에서 성립한다.
            int pulse;
            int target;
            bool home;
            Assert.True(runtime.Plant.TryGetValve("V-1", out pulse, out target, out home));
            Assert.InRange(pulse, 2500, 5000);
        }

        [Fact]
        public void 자동_제어가_전_체인을_동시에_수렴시킨다()
        {
            // 체인 5조가 서로 다른 포트의 밸브를 쓰므로, 지령이 올바른 워커로 전달되어야 한다.
            EsamRuntime runtime = CreateRuntime();
            AdvanceToReady(runtime);
            runtime.Engine.RequestAuto();

            RunLoop(runtime, 400);

            for (int i = 1; i <= 5; i++)
            {
                double truePv;
                Assert.True(runtime.Plant.TryGetTruePressure("S2-" + i, out truePv));
                Assert.InRange(truePv, -15.0, -5.0);
            }
        }

        [Fact]
        public void 자동_제어가_아니면_지령을_만들지_않는다()
        {
            // Homing 중이거나 Ready 상태에서 자동 지령이 나가면 안 된다.
            EsamRuntime runtime = CreateRuntime();
            PollAll(runtime);

            Assert.Equal(0, runtime.Engine.ExecuteStep());

            AdvanceToReady(runtime);
            PollAll(runtime);

            // Ready 는 수동 조작만 허용한다.
            Assert.Equal(0, runtime.Engine.ExecuteStep());
        }

        [Fact]
        public void 품질이_나쁜_센서로는_제어하지_않는다()
        {
            EsamRuntime runtime = CreateRuntime();
            AdvanceToReady(runtime);
            runtime.Engine.RequestAuto();

            // 전 체인의 센서 2를 분리한다(슬레이브 4~8).
            SimulatedModbusTransport busA = Transport(runtime, "CH1");

            for (byte slave = 4; slave <= 8; slave++)
            {
                Assert.True(busA.DetachSlave(slave));
            }

            PollAll(runtime);
            _clock.AdvanceMs(5000);
            PollAll(runtime);

            Assert.Equal(0, runtime.Engine.ExecuteStep());
        }

        // ── 인터록 ──────────────────────────────────────────────────────────────

        /// <summary>
        /// 시설 배기 상실을 모사한다.
        /// </summary>
        /// <remarks>
        /// 센서 3 모델은 <c>base − 200·u − 150·f</c> 다. base 를 200 Pa 로 올리면
        /// 개도 75%·팬 정지에서도 +50 Pa 가 되어 대기압을 넘는다.
        /// 이것이 IL-01 이 막아야 하는 사건, 즉 배기 음압을 잃어 오염이 확산되는 조건이다.
        /// </remarks>
        private static void LoseExhaust(EsamRuntime runtime)
        {
            runtime.Plant.Options.Sensor3BasePa = 200.0;

            // ★ 목표값만 바꾸면 센서가 읽는 값은 그대로다.
            // PlantModel 의 1차 지연은 Advance 를 불러야 움직이고,
            // 시뮬레이션 전송 계층의 자동 진행은 꺼져 있다.
            //
            // 이 호출이 없으면 "배기를 상실시켰다" 고 적어 놓고 압력은 -177 Pa 에
            // 그대로 머문다. IL-01 임계값 0 Pa 를 넘지 못해 인터록이 발동하지 않고,
            // 그 뒤의 모든 단정이 무의미해진다.
            //
            // 헬퍼가 진행까지 책임진다. 호출부 17곳에서 매번 기억해야 하는
            // 규약으로 두면 언젠가 또 빠진다.
            runtime.Plant.Advance(2.0);
        }

        /// <summary>배기를 정상 상태로 되돌린다.</summary>
        private static void RestoreExhaust(EsamRuntime runtime)
        {
            runtime.Plant.Options.Sensor3BasePa = -50.0;

            // 상동. 복구도 실제로 값이 돌아와야 의미가 있다.
            runtime.Plant.Advance(2.0);
        }

        [Fact]
        public void 배기_상실시_인터록이_발동하고_밸브가_닫힌다()
        {
            EsamRuntime runtime = CreateRuntime();
            AdvanceToReady(runtime);
            runtime.Engine.RequestAuto();

            bool tripped = false;
            runtime.Interlock.Tripped += (sender, e) => tripped = true;

            RunLoop(runtime, 100);
            Assert.False(tripped, "정상 운전 중에는 발동하지 않는다.");

            LoseExhaust(runtime);
            RunLoop(runtime, 20);

            Assert.True(tripped, "센서 3 이 대기압(0 Pa)을 넘으면 인터록이 발동해야 한다.");
            Assert.True(runtime.Interlock.IsTripped);
            Assert.Equal(SystemPhase.Interlocked, runtime.Engine.StateMachine.Phase);

            // 인터록 지령은 최우선으로 처리되므로 밸브가 닫힘 위치로 향한다.
            PollAll(runtime);
            runtime.Plant.Advance(6.0);
            PollAll(runtime);

            int pulse;
            int target;
            bool home;
            Assert.True(runtime.Plant.TryGetValve("V-1", out pulse, out target, out home));
            Assert.Equal(0, target);
            Assert.Equal(0, pulse);
        }

        [Fact]
        public void 인터록_지령이_같은_사이클의_자동_지령에_되돌려지지_않는다()
        {
            // ★ 회귀 방지. 인터록의 실효를 0으로 만들던 결함이다.
            // 큐가 하위 우선순위를 지령 '종류'로 비교하면 CloseValve 와 SetValvePosition 이
            // 종류가 달라 자동 지령이 남고, 워커가 밸브를 닫은 직후 같은 사이클에 다시 연다.
            EsamRuntime runtime = CreateRuntime();
            AdvanceToReady(runtime);
            runtime.Engine.RequestAuto();

            // 자동 제어가 밸브를 열고 있는 상태를 만든다.
            RunLoop(runtime, 100);

            int pulseBefore;
            int targetBefore;
            bool homeBefore;
            runtime.Plant.TryGetValve("V-1", out pulseBefore, out targetBefore, out homeBefore);
            Assert.True(targetBefore > 0, "자동 제어가 밸브를 열고 있어야 한다.");

            LoseExhaust(runtime);

            // 자동 지령 생성 직후에 인터록 판정이 오는 순서를 만든다.
            runtime.Engine.ExecuteStep();
            PollAll(runtime);

            Assert.True(runtime.Interlock.IsTripped);

            // 이후 여러 사이클을 돌려도 밸브는 닫힌 상태를 유지해야 한다.
            for (int i = 0; i < 30; i++)
            {
                PollAll(runtime);
                runtime.Engine.ExecuteStep();
                runtime.Plant.Advance(0.2);
                _clock.AdvanceMs(200);
            }

            int pulse;
            int target;
            bool home;
            runtime.Plant.TryGetValve("V-1", out pulse, out target, out home);

            Assert.Equal(0, target);
            Assert.Equal(0, pulse);
        }

        [Fact]
        public void 인터록이_발동하면_자동_지령이_멈춘다()
        {
            EsamRuntime runtime = CreateRuntime();
            AdvanceToReady(runtime);
            runtime.Engine.RequestAuto();

            RunLoop(runtime, 100);

            LoseExhaust(runtime);
            RunLoop(runtime, 20);

            Assert.Equal(SystemPhase.Interlocked, runtime.Engine.StateMachine.Phase);
            Assert.Equal(0, runtime.Engine.ExecuteStep());
        }

        [Fact]
        public void 인터록은_Manual_정책이라_Reset_전까지_유지된다()
        {
            EsamRuntime runtime = CreateRuntime();
            AdvanceToReady(runtime);
            runtime.Engine.RequestAuto();

            RunLoop(runtime, 100);

            LoseExhaust(runtime);
            RunLoop(runtime, 20);
            Assert.True(runtime.Interlock.IsTripped);

            // 배기가 복구되어도 래치가 유지된다. 원인 확인 없는 자동 재가동을 막는다.
            RestoreExhaust(runtime);
            RunLoop(runtime, 50);

            Assert.True(runtime.Interlock.IsTripped);

            // Reset 후에야 해제된다.
            runtime.Interlock.Reset("IL-01");
            PollAll(runtime);

            Assert.False(runtime.Interlock.IsTripped);
        }

        [Fact]
        public void 조건이_남은_상태에서_Reset하면_즉시_재발동한다()
        {
            // 래치 해제는 원인 제거를 뜻하지 않는다. 원인이 남아 있으면 다시 발동해야 한다.
            // Reset 이 '해제'가 아니라 '무력화'로 동작하면 그 인터록은 없는 것과 같다.
            EsamRuntime runtime = CreateRuntime();
            AdvanceToReady(runtime);
            runtime.Engine.RequestAuto();

            RunLoop(runtime, 100);

            LoseExhaust(runtime);
            RunLoop(runtime, 20);
            Assert.True(runtime.Interlock.IsTripped);

            // 배기를 복구하지 않은 채 Reset.
            runtime.Interlock.Reset("IL-01");
            PollAll(runtime);

            Assert.True(runtime.Interlock.IsTripped, "원인이 남아 있으면 다시 발동해야 한다.");
        }

        [Fact]
        public void 래치된_인터록은_센서를_읽지_못해도_유지된다()
        {
            EsamRuntime runtime = CreateRuntime();
            AdvanceToReady(runtime);
            runtime.Engine.RequestAuto();

            RunLoop(runtime, 100);

            LoseExhaust(runtime);
            RunLoop(runtime, 20);
            Assert.True(runtime.Interlock.IsTripped);

            // S3-1 은 센서 목록 9번째이므로 슬레이브 9번이다.
            // 센서를 읽을 수 없게 되어도 래치가 풀려서는 안 된다.
            Assert.True(Transport(runtime, "CH1").DetachSlave(9));

            PollAll(runtime);

            Assert.True(runtime.Interlock.IsTripped);
            Assert.Contains(runtime.Interlock.LastEvaluation.Trips, t => t.RuleId == "IL-01");
        }

        [Fact]
        public void 평가기를_여러_스레드에서_동시에_호출해도_래치가_유지된다()
        {
            // 실제 배선에서는 포트마다 워커 스레드가 하나이므로 판정이 동시에 일어난다.
            // 락이 없으면 HashSet 이 손상되어 래치가 소실되고,
            // 발동이 더 이상 보고되지 않아 위험이 남은 채 Ready 로 복귀한다.
            EsamRuntime runtime = CreateRuntime();
            AdvanceToReady(runtime);
            runtime.Engine.RequestAuto();

            RunLoop(runtime, 100);
            LoseExhaust(runtime);
            RunLoop(runtime, 20);

            Assert.True(runtime.Interlock.IsTripped);

            SystemSnapshot snapshot = runtime.Store.Current;
            Exception failure = null;

            Parallel.For(0, 200, i =>
            {
                try
                {
                    runtime.Interlock.Evaluate(snapshot);
                }
                catch (Exception ex)
                {
                    failure = ex;
                }
            });

            Assert.Null(failure);
            Assert.True(runtime.Interlock.IsTripped, "동시 평가 후에도 래치가 남아 있어야 한다.");
        }

        // ── 알람 ────────────────────────────────────────────────────────────────

        [Fact]
        public void 알람_규칙이_스냅샷을_평가하고_이력을_남긴다()
        {
            List<AlarmRule> rules = new List<AlarmRule>();
            rules.Add(new AlarmRule
            {
                Code = "P01",
                Name = "차압센서 S2-1 상한 초과",
                Source = "device:S2-1.pressurePa",
                Condition = AlarmConditionType.GreaterThan,
                Threshold = 10.0,
                Severity = AlarmSeverity.Alarm
            });

            ControlConfig alarmControl = CreateControl();

            RuntimeOptions options = new RuntimeOptions();
            options.Sensor1Ids = Sensor1Ids;
            options.AlarmRules = rules;
            options.Recipe = BuildRecipe(alarmControl);

            _runtime = Track(EsamRuntime.Create(CreateMap(), alarmControl, options, _clock));
            _runtime.AcknowledgeWarnings();
            OpenTransports(_runtime);

            // 밸브 닫힘 상태의 압력은 +20 Pa 이므로 임계 10 Pa 를 넘는다.
            PollAll(_runtime);

            Assert.NotNull(_runtime.Alarms);
            Assert.Equal(1, _runtime.Alarms.Summary.ActiveCount);
            Assert.Contains("P01", _runtime.Alarms.Summary.ActiveCodes);

            IList<AlarmHistoryEntry> history = _runtime.Alarms.GetHistory(0);
            Assert.Single(history);
            Assert.Equal("P01", history[0].Code);

            // 두 번째 사이클의 스냅샷에 요약이 실린다.
            PollAll(_runtime);
            Assert.Equal(1, _runtime.Store.Current.Alarms.ActiveCount);

            // 같은 조건이 계속 성립해도 이력이 중복 적재되지 않는다.
            for (int i = 0; i < 5; i++)
            {
                PollAll(_runtime);
            }

            Assert.Single(_runtime.Alarms.GetHistory(0));
        }

        // ── 제어 상태 ───────────────────────────────────────────────────────────

        [Fact]
        public void 제어_상태가_스냅샷에_실린다()
        {
            EsamRuntime runtime = CreateRuntime();
            PollAll(runtime);

            ControlStatus status = runtime.Store.Current.Control;

            Assert.Equal(SensorMode.Sensor2, status.Mode);
            Assert.Equal(5, status.Chains.Count);
            Assert.Equal(1, status.Chains[0].ChainId);
        }

        [Fact]
        public void 주소_미확정_그룹은_폴링에서_제외되고_경고로_보고된다()
        {
            // 하드웨어 명세가 미확보인 장치를 구성에 남겨 두어도 통신이 깨지지 않아야 한다.
            DeviceMap map = CreateMap();

            DeviceTypeDefinition plc = new DeviceTypeDefinition();
            plc.Driver = PointKeys.DriverPlc;

            ReadGroupDefinition digital = new ReadGroupDefinition();
            digital.Name = "digital";
            digital.Tier = PollingTier.Fast;
            digital.StartAddress = "TBD(D10)";
            digital.Count = 1;
            digital.Points.Add(new PointDefinition
            {
                Key = PointKeys.DiEmo, Type = PointDataType.Bool, Bit = 6
            });

            plc.ReadGroups.Add(digital);
            map.DeviceTypes["LsXbmPlc"] = plc;

            map.Devices.Add(new DeviceInstanceDefinition
            {
                Id = "PLC-1", Type = "LsXbmPlc", Port = "CH1", SlaveId = 25
            });

            EsamRuntime runtime = CreateRuntime(null, map);

            Assert.Contains(runtime.Warnings, w => w.Message.Contains("PLC-1"));

            // 폴링해도 예외가 나지 않고, PLC 입력은 NoData 로 남는다.
            PollAll(runtime);

            Assert.Equal(Quality.NoData, runtime.Store.Current.Plc.Quality);

            // PLC 가 구성에 있으므로 안전 입력이 있다고 판단하고, 응답이 없으면 IL-04 가 발동한다.
            // EMO 를 읽을 수 없는 상태로 운전하는 것이 더 위험하기 때문이다.
            Assert.True(runtime.Control.SafetyInputsConfigured);
            Assert.True(runtime.Interlock.IsTripped);
            Assert.Contains(runtime.Interlock.LastEvaluation.Trips, t => t.RuleId == "IL-04");
        }
    }
}
