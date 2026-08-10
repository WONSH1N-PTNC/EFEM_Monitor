using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
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
        private static readonly string[] Sensor1Ids = { "S1-1", "S1-2", "S1-3" };

        private readonly FakeClock _clock;
        private EsamRuntime _runtime;

        /// <summary>테스트 준비.</summary>
        public ServicesIntegrationTests()
        {
            _clock = new FakeClock(Build.T0);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (_runtime != null)
            {
                _runtime.Dispose();
                _runtime = null;
            }
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
                Scale = 1.0,
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

        /// <summary>런타임을 구성한다.</summary>
        private EsamRuntime CreateRuntime(ControlConfig control = null, DeviceMap map = null)
        {
            RuntimeOptions options = new RuntimeOptions();
            options.Transport = TransportMode.Simulation;
            options.Sensor1Ids = Sensor1Ids;

            _runtime = EsamRuntime.Create(
                map ?? CreateMap(), control ?? CreateControl(), options, _clock);

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

        /// <summary>상태머신을 Ready 까지 진행시킨다.</summary>
        private static void AdvanceToReady(EsamRuntime runtime)
        {
            runtime.Engine.StateMachine.Fire(SystemTrigger.Start);
            runtime.Engine.StateMachine.Fire(SystemTrigger.InitCompleted);
            runtime.Engine.StateMachine.Fire(SystemTrigger.HomingCompleted);
        }

        /// <summary>폴링 → 제어 → 플랜트 진행을 지정 횟수 반복한다.</summary>
        /// <param name="runtime">런타임.</param>
        /// <param name="steps">반복 횟수.</param>
        /// <param name="stepMs">1회 진행 시간 [ms].</param>
        private void RunLoop(EsamRuntime runtime, int steps, double stepMs = 200.0)
        {
            for (int i = 0; i < steps; i++)
            {
                PollAll(runtime);
                runtime.Engine.ExecuteStep();
                runtime.Plant.Advance(stepMs / 1000.0);
                _clock.AdvanceMs(stepMs);
            }

            // 마지막 플랜트 상태를 스냅샷에 반영한다.
            PollAll(runtime);
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

            RuntimeOptions options = new RuntimeOptions();
            options.Sensor1Ids = Sensor1Ids;
            options.InterlockRules = rules;

            _runtime = EsamRuntime.Create(CreateMap(), CreateControl(), options, _clock);

            Assert.Contains(_runtime.Warnings, w => w.Contains("IL-01"));
        }

        [Fact]
        public void 안전_입력_PLC가_없으면_경고한다()
        {
            // PLC 미배선 상태에서는 IL-02~IL-05 가 전부 무력하다.
            // IL-04 를 항상 발동시키면 아무것도 검증할 수 없으므로 판정은 끄되, 사실은 남긴다.
            EsamRuntime runtime = CreateRuntime();

            Assert.False(runtime.Control.SafetyInputsConfigured);
            Assert.Contains(runtime.Warnings, w => w.Contains("안전 입력"));
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

            // 밸브 닫힘·팬 정지 상태의 센서 2 압력은 플랜트 base = +20 Pa.
            PressureReading reading = snapshot.FindPressure("S2-1");

            Assert.NotNull(reading);
            Assert.Equal(Quality.Good, reading.Quality);
            Assert.InRange(reading.Pa, 16.0, 24.0);
        }

        [Fact]
        public void 밸브_상태가_pulse와_개도율로_변환된다()
        {
            EsamRuntime runtime = CreateRuntime();

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
            Assert.InRange(degraded.Pa, 16.0, 24.0);

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
        }

        /// <summary>배기를 정상 상태로 되돌린다.</summary>
        private static void RestoreExhaust(EsamRuntime runtime)
        {
            runtime.Plant.Options.Sensor3BasePa = -50.0;
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

            RuntimeOptions options = new RuntimeOptions();
            options.Sensor1Ids = Sensor1Ids;
            options.AlarmRules = rules;

            _runtime = EsamRuntime.Create(CreateMap(), CreateControl(), options, _clock);
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

            Assert.Contains(runtime.Warnings, w => w.Contains("PLC-1"));

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
