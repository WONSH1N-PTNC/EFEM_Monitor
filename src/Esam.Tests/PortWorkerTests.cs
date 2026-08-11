using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using Esam.Communication.Abstractions;
using Esam.Communication.Configuration;
using Esam.Communication.Polling;
using Esam.Communication.Simulation;
using Esam.Domain.Configuration;
using Esam.Domain.Control;
using Esam.Domain.Models;
using Xunit;

namespace Esam.Tests
{
    /// <summary>우선순위 명령 큐 검증.</summary>
    public class CommandQueueTests
    {
        private static ActuatorCommand Valve(int pulse, CommandPriority priority, string id = "V-1")
        {
            return ActuatorCommand.SetValvePosition(id, pulse, priority, "테스트");
        }

        [Fact(Timeout = ServicesIntegrationTests.TestTimeoutMs)]
        public void 인터록_지령이_항상_먼저_나온다()
        {
            // 자동 제어가 200ms 마다 지령을 쌓는 상황에서도
            // 인터록은 즉시 앞으로 나가야 안전 기능이 성립한다.
            CommandQueue queue = new CommandQueue();

            queue.Enqueue(Valve(1000, CommandPriority.Automatic));
            queue.Enqueue(Valve(2000, CommandPriority.Manual, "V-2"));
            queue.Enqueue(ActuatorCommand.CloseValve("V-3", CommandPriority.Interlock, "인터록"));

            ActuatorCommand first;
            Assert.True(queue.TryDequeue(out first));
            Assert.Equal(CommandPriority.Interlock, first.Priority);

            ActuatorCommand second;
            Assert.True(queue.TryDequeue(out second));
            Assert.Equal(CommandPriority.Manual, second.Priority);

            ActuatorCommand third;
            Assert.True(queue.TryDequeue(out third));
            Assert.Equal(CommandPriority.Automatic, third.Priority);
        }

        [Fact(Timeout = ServicesIntegrationTests.TestTimeoutMs)]
        public void 같은_대상의_중복_지령은_최신값으로_병합한다()
        {
            // 통신이 잠시 느려졌을 때 오래된 지령이 뒤늦게 실행되어
            // 밸브가 역주행하는 것을 막는다.
            CommandQueue queue = new CommandQueue();

            queue.Enqueue(Valve(2400, CommandPriority.Automatic));
            queue.Enqueue(Valve(2300, CommandPriority.Automatic));
            queue.Enqueue(Valve(2200, CommandPriority.Automatic));

            Assert.Equal(1, queue.Count);

            ActuatorCommand command;
            Assert.True(queue.TryDequeue(out command));
            Assert.Equal(2200.0, command.Value);
        }

        [Fact(Timeout = ServicesIntegrationTests.TestTimeoutMs)]
        public void 다른_대상의_지령은_병합하지_않는다()
        {
            CommandQueue queue = new CommandQueue();

            queue.Enqueue(Valve(1000, CommandPriority.Automatic, "V-1"));
            queue.Enqueue(Valve(2000, CommandPriority.Automatic, "V-2"));

            Assert.Equal(2, queue.Count);
        }

        [Fact(Timeout = ServicesIntegrationTests.TestTimeoutMs)]
        public void 인터록_지령은_병합하지_않는다()
        {
            // 안전 지령은 하나도 유실되어서는 안 된다.
            CommandQueue queue = new CommandQueue();

            queue.Enqueue(ActuatorCommand.CloseValve("V-1", CommandPriority.Interlock, "1차"));
            queue.Enqueue(ActuatorCommand.CloseValve("V-1", CommandPriority.Interlock, "2차"));

            Assert.Equal(2, queue.Count);
        }

        [Fact(Timeout = ServicesIntegrationTests.TestTimeoutMs)]
        public void 자동지령만_비울_수_있다()
        {
            CommandQueue queue = new CommandQueue();

            queue.Enqueue(Valve(1000, CommandPriority.Automatic));
            queue.Enqueue(Valve(2000, CommandPriority.Manual, "V-2"));
            queue.Enqueue(ActuatorCommand.CloseValve("V-3", CommandPriority.Interlock, "인터록"));

            queue.ClearAutomatic();

            Assert.Equal(2, queue.Count);
            Assert.True(queue.HasInterlockCommand);
        }

        [Fact(Timeout = ServicesIntegrationTests.TestTimeoutMs)]
        public void 배치로_꺼내면_우선순위_순서를_유지한다()
        {
            CommandQueue queue = new CommandQueue();

            queue.Enqueue(Valve(1000, CommandPriority.Automatic));
            queue.Enqueue(ActuatorCommand.CloseValve("V-2", CommandPriority.Interlock, "인터록"));
            queue.Enqueue(Valve(3000, CommandPriority.Manual, "V-3"));

            IList<ActuatorCommand> batch = queue.DequeueBatch(0);

            Assert.Equal(3, batch.Count);
            Assert.Equal(CommandPriority.Interlock, batch[0].Priority);
            Assert.Equal(CommandPriority.Manual, batch[1].Priority);
            Assert.Equal(CommandPriority.Automatic, batch[2].Priority);
            Assert.Equal(0, queue.Count);
        }

        [Fact(Timeout = ServicesIntegrationTests.TestTimeoutMs)]
        public void 빈_큐에서는_꺼내지_못한다()
        {
            ActuatorCommand command;

            Assert.False(new CommandQueue().TryDequeue(out command));
            Assert.Null(command);
        }

        [Fact(Timeout = ServicesIntegrationTests.TestTimeoutMs)]
        public void 수동_지령은_낡은_자동_지령을_무효화한다()
        {
            // 이 처리가 없으면 워커가 Manual 을 먼저 실행한 뒤 Automatic 을 실행해
            // 작업자 조작이 낡은 자동 지령에 덮여 밸브가 되돌아간다.
            CommandQueue queue = new CommandQueue();

            queue.Enqueue(Valve(2000, CommandPriority.Automatic));
            queue.Enqueue(Valve(3000, CommandPriority.Manual));

            Assert.Equal(1, queue.Count);

            ActuatorCommand command;
            Assert.True(queue.TryDequeue(out command));
            Assert.Equal(CommandPriority.Manual, command.Priority);
            Assert.Equal(3000.0, command.Value);
        }

        [Fact(Timeout = ServicesIntegrationTests.TestTimeoutMs)]
        public void 인터록_지령은_같은_장치의_하위_지령을_종류와_무관하게_제거한다()
        {
            // ★ 회귀 방지. 종류(Kind)까지 비교하면 인터록의 실효가 0이 된다.
            // 인터록은 CloseValve, 자동 제어는 SetValvePosition 이라 종류가 다르므로,
            // 종류로 비교하면 자동 지령이 남아 워커가 밸브를 닫은 직후 다시 연다.
            CommandQueue queue = new CommandQueue();

            queue.Enqueue(Valve(2000, CommandPriority.Automatic));
            queue.Enqueue(Valve(3000, CommandPriority.Manual));

            Assert.Equal(1, queue.Count);

            queue.Enqueue(ActuatorCommand.CloseValve("V-1", CommandPriority.Interlock, "인터록"));

            // 하위 우선순위 지령이 남아 있으면 안 된다.
            IList<ActuatorCommand> batch = queue.DequeueBatch(0);

            ActuatorCommand only = Assert.Single(batch);
            Assert.Equal(CommandPriority.Interlock, only.Priority);
            Assert.Equal(ActuatorCommandKind.CloseValve, only.Kind);
        }

        [Fact(Timeout = ServicesIntegrationTests.TestTimeoutMs)]
        public void 인터록_지령끼리는_병합하지_않는다()
        {
            // 안전 지령은 모두 실행되어야 한다. 같은 장치라도 합치지 않는다.
            CommandQueue queue = new CommandQueue();

            queue.Enqueue(ActuatorCommand.CloseValve("V-1", CommandPriority.Interlock, "1차"));
            queue.Enqueue(Valve(4000, CommandPriority.Interlock));

            IList<ActuatorCommand> batch = queue.DequeueBatch(0);

            Assert.Equal(2, batch.Count);
            Assert.All(batch, c => Assert.Equal(CommandPriority.Interlock, c.Priority));
        }

        [Fact(Timeout = ServicesIntegrationTests.TestTimeoutMs)]
        public void 다른_대상의_하위_지령은_유지된다()
        {
            CommandQueue queue = new CommandQueue();

            queue.Enqueue(Valve(2000, CommandPriority.Automatic, "V-1"));
            queue.Enqueue(Valve(3000, CommandPriority.Manual, "V-2"));

            Assert.Equal(2, queue.Count);
        }

        [Fact(Timeout = ServicesIntegrationTests.TestTimeoutMs)]
        public void null_지령은_무시한다()
        {
            CommandQueue queue = new CommandQueue();
            queue.Enqueue(null);
            queue.EnqueueRange(null);

            Assert.Equal(0, queue.Count);
        }
    }

    /// <summary>선언적 지령 변환 검증.</summary>
    public class CommandTranslatorTests
    {
        private static DeviceRuntime CreateValve()
        {
            DeviceTypeDefinition type = new DeviceTypeDefinition();
            type.Driver = "ThrottleValve";
            type.Commands["setPosition"] =
                new CommandDefinition { FunctionCode = 6, Address = "0x6202", Value = "$arg" };
            type.Commands["prMove"] =
                new CommandDefinition { FunctionCode = 6, Address = "0x6002", Value = "0x0010" };
            type.Commands["homing"] =
                new CommandDefinition { FunctionCode = 6, Address = "0x6002", Value = "0x0020" };
            type.Commands["quickStop"] =
                new CommandDefinition { FunctionCode = 6, Address = "0x6002", Value = "0x0040" };

            ReadGroupDefinition group = new ReadGroupDefinition();
            group.Name = "position";
            group.StartAddress = "0x602B";
            group.Count = 1;
            group.Points.Add(new PointDefinition { Key = "positionPulse" });
            type.ReadGroups.Add(group);

            DeviceInstanceDefinition instance = new DeviceInstanceDefinition
            {
                Id = "V-1",
                Type = "ThrottleValve",
                Port = "BUS_B",
                SlaveId = 1
            };

            return new DeviceRuntime(instance, type);
        }

        private static DeviceRuntime CreateFan(double maxRpm = 3000.0, bool withStop = true)
        {
            DeviceTypeDefinition type = new DeviceTypeDefinition();
            type.Driver = "ModbusFan";
            type.Commands["setRpm"] =
                new CommandDefinition { FunctionCode = 6, Address = "0x2000", Value = "$arg" };

            if (withStop)
            {
                type.Commands["stop"] =
                    new CommandDefinition { FunctionCode = 6, Address = "0x2001", Value = "0" };
            }

            type.Conversion.MaxRpm = maxRpm;

            ReadGroupDefinition group = new ReadGroupDefinition();
            group.Name = "runtime";
            group.StartAddress = "0x0000";
            group.Count = 1;
            group.Points.Add(new PointDefinition { Key = "rpm" });
            type.ReadGroups.Add(group);

            return new DeviceRuntime(
                new DeviceInstanceDefinition
                {
                    Id = "F-1",
                    Type = "ModbusFan",
                    Port = "BUS_C",
                    SlaveId = 1
                },
                type);
        }

        private readonly DeclarativeCommandTranslator _translator = new DeclarativeCommandTranslator();

        [Fact(Timeout = ServicesIntegrationTests.TestTimeoutMs)]
        public void 밸브_이동은_위치설정_후_Move_2단계로_펼쳐진다()
        {
            // 통신자료 규정: 0x6202 에 위치를 쓴 뒤 0x6002 ← 0x10 을 써야 실제로 움직인다.
            // 순서가 뒤바뀌면 이전에 남아 있던 위치값으로 이동한다.
            IList<ModbusRequest> requests;
            string reason;

            Assert.True(_translator.TryTranslate(
                ActuatorCommand.SetValvePosition("V-1", 2500, CommandPriority.Automatic, "테스트"),
                CreateValve(), out requests, out reason));

            Assert.Equal(2, requests.Count);

            Assert.Equal(0x6202, requests[0].StartAddress);
            Assert.Equal(2500, requests[0].Values[0]);

            Assert.Equal(0x6002, requests[1].StartAddress);
            Assert.Equal(0x0010, requests[1].Values[0]);
        }

        [Fact(Timeout = ServicesIntegrationTests.TestTimeoutMs)]
        public void 밸브_Close는_위치_0으로_이동시킨다()
        {
            IList<ModbusRequest> requests;
            string reason;

            Assert.True(_translator.TryTranslate(
                ActuatorCommand.CloseValve("V-1", CommandPriority.Interlock, "인터록"),
                CreateValve(), out requests, out reason));

            Assert.Equal(2, requests.Count);
            Assert.Equal(0, requests[0].Values[0]);
        }

        [Fact(Timeout = ServicesIntegrationTests.TestTimeoutMs)]
        public void 밸브_지령은_pulse_한계로_제한된다()
        {
            IList<ModbusRequest> requests;
            string reason;

            _translator.TryTranslate(
                ActuatorCommand.SetValvePosition("V-1", 99999, CommandPriority.Manual, "테스트"),
                CreateValve(), out requests, out reason);

            Assert.Equal(5000, requests[0].Values[0]);
        }

        [Fact(Timeout = ServicesIntegrationTests.TestTimeoutMs)]
        public void Homing과_QuickStop은_단일_요청이다()
        {
            DeviceRuntime valve = CreateValve();
            IList<ModbusRequest> requests;
            string reason;

            Assert.True(_translator.TryTranslate(
                ActuatorCommand.HomeValve("V-1", "테스트"), valve, out requests, out reason));

            Assert.Single(requests);
            Assert.Equal(0x0020, requests[0].Values[0]);

            Assert.True(_translator.TryTranslate(
                new ActuatorCommand(ActuatorTarget.Valve, "V-1", ActuatorCommandKind.QuickStopValve,
                    0.0, CommandPriority.Interlock, "테스트"),
                valve, out requests, out reason));

            Assert.Single(requests);
            Assert.Equal(0x0040, requests[0].Values[0]);
        }

        [Fact(Timeout = ServicesIntegrationTests.TestTimeoutMs)]
        public void 팬_RPM은_최대치로_제한된다()
        {
            IList<ModbusRequest> requests;
            string reason;

            Assert.True(_translator.TryTranslate(
                ActuatorCommand.SetFanRpm("F-1", 9999, CommandPriority.Automatic, "테스트"),
                CreateFan(3000.0), out requests, out reason));

            Assert.Equal(3000, requests[0].Values[0]);
        }

        [Fact(Timeout = ServicesIntegrationTests.TestTimeoutMs)]
        public void 팬_정지는_전용_명령이_없으면_RPM_0으로_대체한다()
        {
            // 안전 정지 경로이므로 전용 명령 부재를 이유로 실패시키지 않는다.
            IList<ModbusRequest> requests;
            string reason;

            Assert.True(_translator.TryTranslate(
                ActuatorCommand.StopFan("F-1", CommandPriority.Interlock, "인터록"),
                CreateFan(3000.0, withStop: false), out requests, out reason));

            Assert.Single(requests);
            Assert.Equal(0x2000, requests[0].StartAddress);
            Assert.Equal(0, requests[0].Values[0]);
        }

        [Fact(Timeout = ServicesIntegrationTests.TestTimeoutMs)]
        public void 명령_정의가_없으면_사유와_함께_실패한다()
        {
            DeviceTypeDefinition bare = new DeviceTypeDefinition();
            bare.Driver = "ThrottleValve";

            ReadGroupDefinition group = new ReadGroupDefinition();
            group.Name = "position";
            group.StartAddress = "0x602B";
            group.Count = 1;
            group.Points.Add(new PointDefinition { Key = "positionPulse" });
            bare.ReadGroups.Add(group);

            DeviceRuntime device = new DeviceRuntime(
                new DeviceInstanceDefinition { Id = "V-9", Type = "T", Port = "P", SlaveId = 9 }, bare);

            IList<ModbusRequest> requests;
            string reason;

            Assert.False(_translator.TryTranslate(
                ActuatorCommand.SetValvePosition("V-9", 100, CommandPriority.Automatic, "테스트"),
                device, out requests, out reason));

            Assert.Contains("setPosition", reason);
        }

        [Fact(Timeout = ServicesIntegrationTests.TestTimeoutMs)]
        public void 주소_미확정_명령은_실패한다()
        {
            DeviceRuntime fan = CreateFan();
            fan.Type.Commands["setRpm"].Address = "TBD";

            IList<ModbusRequest> requests;
            string reason;

            Assert.False(_translator.TryTranslate(
                ActuatorCommand.SetFanRpm("F-1", 1000, CommandPriority.Automatic, "테스트"),
                fan, out requests, out reason));

            Assert.Contains("미확정", reason);
        }

        [Fact(Timeout = ServicesIntegrationTests.TestTimeoutMs)]
        public void 대상_디바이스가_null이면_실패한다()
        {
            IList<ModbusRequest> requests;
            string reason;

            Assert.False(_translator.TryTranslate(
                ActuatorCommand.CloseValve("V-1", CommandPriority.Interlock, "테스트"),
                null, out requests, out reason));
        }

        [Fact(Timeout = ServicesIntegrationTests.TestTimeoutMs)]
        public void 개도율을_pulse로_변환한다()
        {
            Assert.Equal(2500, DeclarativeCommandTranslator.PercentToPulse(CreateValve(), 50.0));
            Assert.Equal(5000, DeclarativeCommandTranslator.PercentToPulse(CreateValve(), 100.0));
        }
    }

    /// <summary>
    /// 포트 워커 통합 검증. 시뮬레이션 전송 계층 위에서 실제 폴링을 수행한다.
    /// </summary>
    public class ModbusPortWorkerTests : IDisposable
    {
        private const int SensorCount = 13;
        private static readonly string[] Sensor1Ids = { "S1-1", "S1-2", "S1-3" };

        private readonly PlantModel _plant;
        private readonly SimulatedModbusTransport _transport;
        private readonly List<DeviceRuntime> _devices = new List<DeviceRuntime>();
        private readonly ModbusPortWorker _worker;

        /// <summary>차압센서 13대를 BUS_A 구성으로 세운다.</summary>
        public ModbusPortWorkerTests()
        {
            ControlConfig control = Build.Config();

            _plant = new PlantModel(
                control.Chains, Sensor1Ids, new PlantOptions().WithoutNoise(), 20260731);
            _plant.CompleteAllHoming();

            _transport = new SimulatedModbusTransport("BUS_A", _plant, null);

            DeviceTypeDefinition sensorType = CreateSensorType();

            // S1-1~1-3 = 슬레이브 1~3, S2-1~2-5 = 4~8, S3-1~3-5 = 9~13
            string[] sensorIds =
            {
                "S1-1", "S1-2", "S1-3",
                "S2-1", "S2-2", "S2-3", "S2-4", "S2-5",
                "S3-1", "S3-2", "S3-3", "S3-4", "S3-5"
            };

            for (int i = 0; i < sensorIds.Length; i++)
            {
                byte slaveId = (byte)(i + 1);

                _transport.AddSlave(new SimulatedPressureSensor(slaveId, _plant, sensorIds[i]));

                _devices.Add(new DeviceRuntime(
                    new DeviceInstanceDefinition
                    {
                        Id = sensorIds[i],
                        Type = "WTDM550",
                        Port = "BUS_A",
                        SlaveId = slaveId,
                        RangeMin = -2000.0,
                        RangeMax = 2000.0,
                        FilterWindowSize = 1
                    },
                    sensorType));
            }

            _transport.Open();

            _worker = new ModbusPortWorker(
                "BUS_A", _transport, _devices, new PollingTierPeriods(),
                null, new FakeClock(Build.T0));
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _worker.Dispose();
            _transport.Dispose();
        }

        /// <summary>차압센서 종류 명세를 만든다(Fast 압력 + Slow 상태).</summary>
        private static DeviceTypeDefinition CreateSensorType()
        {
            DeviceTypeDefinition type = new DeviceTypeDefinition();
            type.Driver = "PressureSensor";

            ReadGroupDefinition pressure = new ReadGroupDefinition();
            pressure.Name = "pressure";
            pressure.Tier = PollingTier.Fast;
            pressure.FunctionCode = 3;
            // ★ 시뮬레이션 슬레이브의 실제 주소를 상수로 참조한다.
            // 리터럴을 적어 두면 IO List 가 바뀔 때 여기만 남아,
            // 읽기가 전부 예외 응답(0x02)으로 실패하면서도 원인이 드러나지 않는다.
            pressure.StartAddress = ToHex(SimulatedPressureSensor.PressureRegister);
            pressure.Count = 1;
            pressure.Points.Add(new PointDefinition
            {
                Key = "pressurePa",
                Offset = 0,
                Type = PointDataType.Int16,
                Scale = 0.1,
                Unit = "Pa",

                // 주 계측값만 영점 오프셋·이동평균·레인지 검증 대상이다.
                ApplyCalibration = true
            });

            ReadGroupDefinition status = new ReadGroupDefinition();
            status.Name = "status";
            status.Tier = PollingTier.Slow;
            status.FunctionCode = 3;
            status.StartAddress = ToHex(SimulatedPressureSensor.StatusRegister);
            status.Count = 1;
            status.Points.Add(new PointDefinition { Key = "deviceStatus", Type = PointDataType.UInt16 });

            type.ReadGroups.Add(pressure);
            type.ReadGroups.Add(status);
            return type;
        }

        /// <summary>레지스터 주소를 설정 파일과 같은 16진 표기로 만든다.</summary>
        /// <param name="address">레지스터 주소.</param>
        /// <returns>"0x4001" 형태의 문자열.</returns>
        private static string ToHex(ushort address)
        {
            return "0x" + address.ToString("X4", CultureInfo.InvariantCulture);
        }

        // ── 폴링 동작 ───────────────────────────────────────────────────────────

        [Fact(Timeout = ServicesIntegrationTests.TestTimeoutMs)]
        public void 첫_사이클은_Fast와_Medium_Slow를_모두_읽는다()
        {
            // 시작 직후에는 모든 티어가 "읽을 때가 됨" 상태다.
            PollCompletedEventArgs result = _worker.ExecuteCycle(CancellationToken.None);

            Assert.Contains(PollingTier.Fast, result.TiersPolled);
            Assert.Contains(PollingTier.Slow, result.TiersPolled);

            // 센서 13대 × (Fast 압력 + Slow 상태) = 26 트랜잭션
            Assert.Equal(SensorCount * 2, result.Results.Count);
        }

        [Fact(Timeout = ServicesIntegrationTests.TestTimeoutMs)]
        public void 두번째_사이클부터는_Fast만_읽는다()
        {
            _worker.ExecuteCycle(CancellationToken.None);
            PollCompletedEventArgs second = _worker.ExecuteCycle(CancellationToken.None);

            // 티어를 나눈 목적이 버스 부하 감소다. Slow 그룹이 매 사이클 읽히면 의미가 없다.
            Assert.DoesNotContain(PollingTier.Slow, second.TiersPolled);
            Assert.Equal(SensorCount, second.Results.Count);
        }

        [Fact(Timeout = ServicesIntegrationTests.TestTimeoutMs)]
        public void 압력값을_공학단위로_디코딩한다()
        {
            PollCompletedEventArgs result = _worker.ExecuteCycle(CancellationToken.None);
            IDictionary<string, PointSample> points = result.ToPointMap();

            Assert.True(points.ContainsKey("S2-1.pressurePa"));

            // 초기 상태(밸브 닫힘)의 센서 2 압력은 base = +20 Pa
            PointSample sample = points["S2-1.pressurePa"];

            Assert.Equal(20.0, sample.Value, 6);
            Assert.Equal(Quality.Good, sample.Quality);
            Assert.Equal("Pa", sample.Unit);
        }

        [Fact(Timeout = ServicesIntegrationTests.TestTimeoutMs)]
        public void 음압을_부호있게_디코딩한다()
        {
            _plant.ApplyCommand(ActuatorCommand.SetValvePosition(
                "V-1", 5000, CommandPriority.Automatic, "테스트"));

            for (int i = 0; i < 300; i++)
            {
                _plant.Advance(0.1);
            }

            PollCompletedEventArgs result = _worker.ExecuteCycle(CancellationToken.None);
            PointSample sample = result.ToPointMap()["S2-1.pressurePa"];

            Assert.InRange(sample.Value, -21.0, -19.0);
        }

        [Fact(Timeout = ServicesIntegrationTests.TestTimeoutMs)]
        public void 통신_실패는_결과에_실패로_기록된다()
        {
            _transport.DetachSlave(4); // S2-1 분리

            PollCompletedEventArgs result = _worker.ExecuteCycle(CancellationToken.None);

            Assert.True(result.FailureCount > 0);

            GroupReadResult failed = null;
            foreach (GroupReadResult item in result.Results)
            {
                if (item.DeviceId == "S2-1" && !item.IsSuccess)
                {
                    failed = item;
                    break;
                }
            }

            Assert.NotNull(failed);
            Assert.Equal(ModbusFailureKind.Timeout, failed.FailureKind);
            Assert.Empty(failed.Samples);
        }

        [Fact(Timeout = ServicesIntegrationTests.TestTimeoutMs)]
        public void 연속_실패가_누적되어_통신상실을_판정할_수_있다()
        {
            // 인터록 IL-04(통신 상실) 판정의 근거 데이터다.
            _transport.DetachSlave(4);

            DeviceRuntime sensor = _devices[3]; // S2-1

            for (int i = 0; i < 3; i++)
            {
                _worker.ExecuteCycle(CancellationToken.None);
            }

            Assert.True(sensor.IsCommunicationLost(3));
            Assert.False(_devices[4].IsCommunicationLost(3)); // S2-2 는 정상
        }

        [Fact(Timeout = ServicesIntegrationTests.TestTimeoutMs)]
        public void 사이클타임_실측값이_설계문서_추정과_일치한다()
        {
            // ★ S3 의 핵심 산출물.
            // DESIGN.md 2.2(B): 19200bps 단일 버스에서 차압센서 13채널 순차 폴링은
            // 약 220~430ms 이므로 100ms 목표를 만족할 수 없다.
            // 시뮬레이션 전송 계층이 실장비와 같은 기준으로 프레임 시간을 계산하므로
            // 여기서 나온 값은 실제 버스 시간의 추정치다.
            _worker.ExecuteCycle(CancellationToken.None); // 첫 사이클(Slow 포함)은 제외

            PollCompletedEventArgs fastOnly = _worker.ExecuteCycle(CancellationToken.None);

            double busTimeMs = 0.0;
            foreach (GroupReadResult result in fastOnly.Results)
            {
                busTimeMs += result.ElapsedMs;
            }

            Assert.Equal(SensorCount, fastOnly.Results.Count);

            // 트랜잭션 1건 약 18.6ms × 13 ≈ 242ms
            Assert.InRange(busTimeMs, 200.0, 300.0);

            // 결론: 단일 버스로는 100ms 목표 달성 불가 → 포트 분할 필요(협의 항목 #1)
            Assert.True(busTimeMs > 100.0,
                "단일 버스 13채널로 100ms 를 만족할 수 없다는 것이 설계 결론이다.");
        }

        [Fact(Timeout = ServicesIntegrationTests.TestTimeoutMs)]
        public void 포트를_4채널씩_나누면_100ms대에_들어온다()
        {
            // DESIGN.md 2.2(B) 안 1 의 근거. 앞 4대만 폴링해 비교한다.
            List<DeviceRuntime> quarter = new List<DeviceRuntime>();
            for (int i = 0; i < 4; i++)
            {
                quarter.Add(_devices[i]);
            }

            using (ModbusPortWorker splitWorker = new ModbusPortWorker(
                       "BUS_A1", _transport, quarter, new PollingTierPeriods(),
                       null, new FakeClock(Build.T0)))
            {
                splitWorker.ExecuteCycle(CancellationToken.None);
                PollCompletedEventArgs result = splitWorker.ExecuteCycle(CancellationToken.None);

                double busTimeMs = 0.0;
                foreach (GroupReadResult item in result.Results)
                {
                    busTimeMs += item.ElapsedMs;
                }

                Assert.Equal(4, result.Results.Count);
                Assert.InRange(busTimeMs, 50.0, 100.0);
            }
        }

        // ── 지령 처리 ───────────────────────────────────────────────────────────

        [Fact(Timeout = ServicesIntegrationTests.TestTimeoutMs)]
        public void 주소_미확정_그룹은_폴링에서_제외된다()
        {
            // 레지스터 명세가 확보되지 않은 장치가 섞여 있어도 나머지는 정상 폴링되어야 한다.
            DeviceTypeDefinition type = new DeviceTypeDefinition();
            type.Driver = "Plc";

            ReadGroupDefinition unknown = new ReadGroupDefinition();
            unknown.Name = "digital";
            unknown.StartAddress = "TBD(D10)";
            unknown.Count = 1;
            unknown.Points.Add(new PointDefinition { Key = "di.emo", Type = PointDataType.Bool });
            type.ReadGroups.Add(unknown);

            DeviceRuntime plc = new DeviceRuntime(
                new DeviceInstanceDefinition
                {
                    Id = "PLC-1",
                    Type = "LsXbmPlc",
                    Port = "BUS_A",
                    SlaveId = 25
                },
                type);

            Assert.Empty(plc.ReadGroups);
            Assert.Single(plc.SkippedGroups);
            Assert.Equal("digital", plc.SkippedGroups[0]);
        }

        [Fact(Timeout = ServicesIntegrationTests.TestTimeoutMs)]
        public void 지령_실패시_사유와_함께_이벤트가_발생한다()
        {
            CommandFailedEventArgs captured = null;
            _worker.CommandFailed += (sender, e) => captured = e;

            // 이 포트에 없는 디바이스로 지령을 보낸다.
            _worker.EnqueueCommand(ActuatorCommand.CloseValve("V-99", CommandPriority.Interlock, "테스트"));
            _worker.ExecuteCycle(CancellationToken.None);

            Assert.NotNull(captured);
            Assert.Contains("V-99", captured.Reason);
        }

        [Fact(Timeout = ServicesIntegrationTests.TestTimeoutMs)]
        public void 폴링_완료_이벤트가_발생한다()
        {
            PollCompletedEventArgs captured = null;
            _worker.PollCompleted += (sender, e) => captured = e;

            _worker.ExecuteCycle(CancellationToken.None);

            Assert.NotNull(captured);
            Assert.Equal("BUS_A", captured.PortId);
            Assert.True(captured.SuccessCount > 0);
        }

        [Fact(Timeout = ServicesIntegrationTests.TestTimeoutMs)]
        public void 구독자_예외가_폴링을_멈추지_않는다()
        {
            // 로깅이나 UI 구독자가 터져도 통신은 계속되어야 한다.
            _worker.PollCompleted += (sender, e) => { throw new InvalidOperationException("의도된 예외"); };

            PollCompletedEventArgs result = _worker.ExecuteCycle(CancellationToken.None);

            Assert.NotNull(result);
            Assert.True(result.SuccessCount > 0);
        }

        [Fact(Timeout = ServicesIntegrationTests.TestTimeoutMs)]
        public void 취소_토큰이_설정되면_읽기를_중단한다()
        {
            using (CancellationTokenSource cts = new CancellationTokenSource())
            {
                cts.Cancel();

                PollCompletedEventArgs result = _worker.ExecuteCycle(cts.Token);

                Assert.Empty(result.Results);
            }
        }

        [Fact(Timeout = ServicesIntegrationTests.TestTimeoutMs)]
        public void 통계가_트랜잭션을_집계한다()
        {
            _worker.ExecuteCycle(CancellationToken.None);

            Assert.Equal(SensorCount * 2, _worker.Statistics.TotalTransactions);
            Assert.Equal(100.0, _worker.Statistics.SuccessRatePercent, 6);
            Assert.True(_worker.Statistics.LastCycleMs >= 0.0);
        }

        [Fact(Timeout = ServicesIntegrationTests.TestTimeoutMs)]
        public void 영점_오프셋을_적용하면_측정값이_보정된다()
        {
            DeviceRuntime sensor = _devices[3]; // S2-1, 현재 참값 +20 Pa

            sensor.SetZeroOffset(20.0);

            PollCompletedEventArgs result = _worker.ExecuteCycle(CancellationToken.None);
            PointSample sample = result.ToPointMap()["S2-1.pressurePa"];

            Assert.Equal(0.0, sample.Value, 6);
            Assert.Equal(20.0, sample.RawValue, 6);
        }

        [Fact(Timeout = ServicesIntegrationTests.TestTimeoutMs)]
        public void 영점_오프셋은_상태_레지스터를_오염시키지_않는다()
        {
            // ApplyCalibration 이 없던 시절의 결함: 오프셋 20 을 교정하면
            // deviceStatus 가 0 이 아니라 -20 으로 보고되었다.
            // 상태·알람 코드에 영점 오프셋을 적용하면 값의 의미가 깨진다.
            DeviceRuntime sensor = _devices[3]; // S2-1
            sensor.SetZeroOffset(20.0);

            // 첫 사이클에서 Slow 티어(status 그룹)까지 읽는다.
            PollCompletedEventArgs result = _worker.ExecuteCycle(CancellationToken.None);
            IDictionary<string, PointSample> points = result.ToPointMap();

            Assert.True(points.ContainsKey("S2-1.deviceStatus"));

            PointSample status = points["S2-1.deviceStatus"];

            Assert.Equal(0.0, status.Value, 6);
            Assert.Equal(Quality.Good, status.Quality);

            // 주 계측값에는 정상적으로 오프셋이 적용된다.
            Assert.Equal(0.0, points["S2-1.pressurePa"].Value, 6);
        }
    }
}
