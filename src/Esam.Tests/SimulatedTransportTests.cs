using System;
using System.Threading;
using Esam.Communication.Abstractions;
using Esam.Communication.Simulation;
using Esam.Domain.Configuration;
using Esam.Domain.Control;
using Xunit;

namespace Esam.Tests
{
    /// <summary>
    /// 시뮬레이션 전송 계층 검증. 실장비 없이 Modbus 왕복 통신을 재현하는지 확인한다.
    /// </summary>
    public class SimulatedTransportTests : IDisposable
    {
        private static readonly string[] Sensor1Ids = { "S1-1", "S1-2", "S1-3" };

        private readonly PlantModel _plant;
        private readonly SimulatedModbusTransport _transport;

        /// <summary>테스트마다 새 플랜트와 전송 계층을 구성한다.</summary>
        public SimulatedTransportTests()
        {
            ControlConfig config = Build.Config();

            _plant = new PlantModel(
                config.Chains, Sensor1Ids, new PlantOptions().WithoutNoise(), 20260731);
            _plant.CompleteAllHoming();

            _transport = new SimulatedModbusTransport("BUS_TEST", _plant, null);

            // 통신자료 기준 주소 배정: 차압센서 S2-1~S2-5 = 슬레이브 4~8
            for (int i = 1; i <= 5; i++)
            {
                _transport.AddSlave(new SimulatedPressureSensor(
                    (byte)(3 + i), _plant, "S2-" + i));
            }

            // 밸브는 별도 버스이지만 테스트 편의상 ID 11~15 로 함께 등록한다.
            for (int i = 1; i <= 5; i++)
            {
                _transport.AddSlave(new SimulatedThrottleValve(
                    (byte)(10 + i), _plant, "V-" + i));
            }

            for (int i = 1; i <= 5; i++)
            {
                _transport.AddSlave(new SimulatedBlowerFan(
                    (byte)(20 + i), _plant, "F-" + i));
            }

            _transport.Open();
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _transport.Dispose();
        }

        // ── 기본 왕복 통신 ──────────────────────────────────────────────────────

        [Fact(Timeout = ServicesIntegrationTests.TestTimeoutMs)]
        public void 포트를_열지_않으면_PortError를_반환한다()
        {
            _transport.Close();

            ModbusResponse response = _transport.Execute(
                ModbusRequest.ReadHolding(4, 0, 1), CancellationToken.None);

            Assert.False(response.IsSuccess);
            Assert.Equal(ModbusFailureKind.PortError, response.FailureKind);
        }

        [Fact(Timeout = ServicesIntegrationTests.TestTimeoutMs)]
        public void 차압센서_압력값을_읽는다()
        {
            // 초기 상태(밸브 닫힘, 팬 정지)의 센서 2 압력은 base = +20 Pa.
            ModbusResponse response = _transport.Execute(
                ModbusRequest.ReadHolding(4, SimulatedPressureSensor.PressureRegister, 1),
                CancellationToken.None);

            Assert.True(response.IsSuccess);

            // 0.1 Pa/LSB 이므로 20 Pa → 200
            Assert.Equal(200, response.GetInt16(0));
            Assert.Equal(20.0, response.GetInt16(0) * SimulatedPressureSensor.PaPerLsb, 6);
        }

        [Fact(Timeout = ServicesIntegrationTests.TestTimeoutMs)]
        public void 음압을_부호있는_레지스터로_정확히_전달한다()
        {
            // 밸브를 완전히 열면 센서 2는 20 - 40 = -20 Pa 로 내려간다.
            _plant.ApplyCommand(ActuatorCommand.SetValvePosition(
                "V-1", 5000, CommandPriority.Automatic, "테스트"));

            for (int i = 0; i < 300; i++)
            {
                _plant.Advance(0.1);
            }

            ModbusResponse response = _transport.Execute(
                ModbusRequest.ReadHolding(4, SimulatedPressureSensor.PressureRegister, 1),
                CancellationToken.None);

            Assert.True(response.IsSuccess);
            Assert.True(response.GetInt16(0) < 0, "음압은 부호 있는 값으로 읽혀야 한다.");
            Assert.InRange(response.GetInt16(0) * SimulatedPressureSensor.PaPerLsb, -21.0, -19.0);
        }

        // ── 밸브 실제 레지스터 시퀀스 ───────────────────────────────────────────

        [Fact(Timeout = ServicesIntegrationTests.TestTimeoutMs)]
        public void 밸브는_위치설정_후_Move명령을_받아야_움직인다()
        {
            // 통신자료 규정: 0x6202 에 위치를 쓴 뒤 0x6002 에 0x10(PR0 Move)을 써야 이동한다.
            ModbusResponse setPosition = _transport.Execute(
                ModbusRequest.WriteSingle(11, SimulatedThrottleValve.PositionSetRegister, 2500),
                CancellationToken.None);

            Assert.True(setPosition.IsSuccess);

            _plant.Advance(3.0);

            int pulse;
            int target;
            bool home;
            _plant.TryGetValve("V-1", out pulse, out target, out home);

            // Move 명령 없이는 움직이지 않아야 한다.
            Assert.Equal(0, pulse);

            ModbusResponse move = _transport.Execute(
                ModbusRequest.WriteSingle(
                    11, SimulatedThrottleValve.CommandRegister, SimulatedThrottleValve.CommandPrMove),
                CancellationToken.None);

            Assert.True(move.IsSuccess);

            _plant.Advance(5.0);
            _plant.TryGetValve("V-1", out pulse, out target, out home);

            Assert.Equal(2500, pulse);
        }

        [Fact(Timeout = ServicesIntegrationTests.TestTimeoutMs)]
        public void 밸브_현재위치를_0x602B에서_읽는다()
        {
            _plant.ApplyCommand(ActuatorCommand.SetValvePosition(
                "V-2", 1234, CommandPriority.Automatic, "테스트"));
            _plant.Advance(5.0);

            ModbusResponse response = _transport.Execute(
                ModbusRequest.ReadHolding(12, SimulatedThrottleValve.CurrentPositionRegister, 1),
                CancellationToken.None);

            Assert.True(response.IsSuccess);
            Assert.Equal(1234, response.GetUInt16(0));
        }

        [Fact(Timeout = ServicesIntegrationTests.TestTimeoutMs)]
        public void Homing_명령으로_원점복귀_상태가_된다()
        {
            ControlConfig config = Build.Config();
            PlantModel fresh = new PlantModel(config.Chains, Sensor1Ids, null, 1);

            using (SimulatedModbusTransport transport =
                   new SimulatedModbusTransport("BUS_HOME", fresh, null))
            {
                transport.AddSlave(new SimulatedThrottleValve(11, fresh, "V-1"));
                transport.Open();

                ModbusResponse before = transport.Execute(
                    ModbusRequest.ReadHolding(11, SimulatedThrottleValve.HomeRegister, 1),
                    CancellationToken.None);

                Assert.True(before.IsSuccess);
                Assert.Equal(0, before.GetUInt16(0));

                transport.Execute(
                    ModbusRequest.WriteSingle(
                        11,
                        SimulatedThrottleValve.CommandRegister,
                        SimulatedThrottleValve.CommandHoming),
                    CancellationToken.None);

                ModbusResponse after = transport.Execute(
                    ModbusRequest.ReadHolding(11, SimulatedThrottleValve.HomeRegister, 1),
                    CancellationToken.None);

                Assert.Equal(1, after.GetUInt16(0));
            }
        }

        [Fact(Timeout = ServicesIntegrationTests.TestTimeoutMs)]
        public void 알람_리셋은_지정값_0x1111에만_반응한다()
        {
            SimulatedThrottleValve valve = (SimulatedThrottleValve)_transport.FindSlave(11);
            valve.InjectAlarm(0x0021);

            ModbusResponse withAlarm = _transport.Execute(
                ModbusRequest.ReadHolding(11, SimulatedThrottleValve.AlarmRegister, 1),
                CancellationToken.None);

            Assert.Equal(0x0021, withAlarm.GetUInt16(0));

            // 잘못된 값은 무시된다.
            _transport.Execute(
                ModbusRequest.WriteSingle(11, SimulatedThrottleValve.AlarmResetRegister, 0x0001),
                CancellationToken.None);

            ModbusResponse stillAlarm = _transport.Execute(
                ModbusRequest.ReadHolding(11, SimulatedThrottleValve.AlarmRegister, 1),
                CancellationToken.None);

            Assert.Equal(0x0021, stillAlarm.GetUInt16(0));

            // 규정된 0x1111 만 알람을 해제한다.
            _transport.Execute(
                ModbusRequest.WriteSingle(
                    11,
                    SimulatedThrottleValve.AlarmResetRegister,
                    SimulatedThrottleValve.AlarmResetValue),
                CancellationToken.None);

            ModbusResponse cleared = _transport.Execute(
                ModbusRequest.ReadHolding(11, SimulatedThrottleValve.AlarmRegister, 1),
                CancellationToken.None);

            Assert.Equal(0, cleared.GetUInt16(0));
        }

        // ── 송풍팬 (Modbus 직결) ────────────────────────────────────────────────

        [Fact(Timeout = ServicesIntegrationTests.TestTimeoutMs)]
        public void 팬_RPM을_설정하고_현재값을_읽는다()
        {
            ModbusResponse write = _transport.Execute(
                ModbusRequest.WriteSingle(21, SimulatedBlowerFan.RpmSetRegister, 1500),
                CancellationToken.None);

            Assert.True(write.IsSuccess);

            _plant.Advance(10.0);

            ModbusResponse read = _transport.Execute(
                ModbusRequest.ReadHolding(21, SimulatedBlowerFan.CurrentRpmRegister, 2),
                CancellationToken.None);

            Assert.True(read.IsSuccess);
            Assert.Equal(1500, read.GetUInt16(0));
            Assert.Equal(2, read.GetUInt16(1)); // 2 = 정속 운전
        }

        [Fact(Timeout = ServicesIntegrationTests.TestTimeoutMs)]
        public void 팬_현재값과_상태를_한번의_트랜잭션으로_읽는다()
        {
            // COMM_MAP.md 1.3 의 설계 요청(연속 주소 배치)이 지켜지는지 확인한다.
            // 이것이 지켜지면 BUS_C 폴링 사이클이 절반으로 줄어든다.
            ModbusResponse read = _transport.Execute(
                ModbusRequest.ReadHolding(22, SimulatedBlowerFan.CurrentRpmRegister, 2),
                CancellationToken.None);

            Assert.True(read.IsSuccess);
            Assert.Equal(2, read.Registers.Length);
        }

        // ── 장애 시나리오 ───────────────────────────────────────────────────────

        [Fact(Timeout = ServicesIntegrationTests.TestTimeoutMs)]
        public void 미등록_슬레이브는_타임아웃으로_처리된다()
        {
            // 실제 버스에서 응답 없는 주소를 폴링하면 타임아웃이 된다.
            ModbusResponse response = _transport.Execute(
                ModbusRequest.ReadHolding(99, 0, 1), CancellationToken.None);

            Assert.False(response.IsSuccess);
            Assert.Equal(ModbusFailureKind.Timeout, response.FailureKind);
        }

        [Fact(Timeout = ServicesIntegrationTests.TestTimeoutMs)]
        public void 정의되지_않은_주소는_예외응답을_반환한다()
        {
            // device-map.json 의 주소 오타를 시뮬레이션에서 잡아낼 수 있어야 한다.
            ModbusResponse response = _transport.Execute(
                ModbusRequest.ReadHolding(4, 0x7FFF, 1), CancellationToken.None);

            Assert.False(response.IsSuccess);
            Assert.Equal(ModbusFailureKind.ExceptionResponse, response.FailureKind);
            Assert.Equal(ModbusExceptionCode.IllegalDataAddress, response.ExceptionCode);
        }

        [Fact(Timeout = ServicesIntegrationTests.TestTimeoutMs)]
        public void 슬레이브를_분리하면_통신이_끊긴다()
        {
            // 인터록 IL-04(통신 상실) 시나리오를 재현하기 위한 기능이다.
            Assert.True(_transport.DetachSlave(4));

            ModbusResponse response = _transport.Execute(
                ModbusRequest.ReadHolding(4, 0, 1), CancellationToken.None);

            Assert.Equal(ModbusFailureKind.Timeout, response.FailureKind);
        }

        [Fact(Timeout = ServicesIntegrationTests.TestTimeoutMs)]
        public void 슬레이브_ID_중복_등록은_즉시_예외를_던진다()
        {
            // 실제 버스에서 ID 중복은 응답 충돌을 일으키는 치명적 배선 오류다.
            // DESIGN.md 2.2 (A)에서 지적한 ID 충돌을 설정 단계에서 잡기 위한 장치다.
            Assert.Throws<ArgumentException>(() =>
                _transport.AddSlave(new SimulatedPressureSensor(4, _plant, "S2-1")));
        }

        [Fact(Timeout = ServicesIntegrationTests.TestTimeoutMs)]
        public void 타임아웃_확률을_주입하면_모든_트랜잭션이_실패한다()
        {
            SimulationTransportOptions options = new SimulationTransportOptions();
            options.TimeoutProbability = 1.0;

            using (SimulatedModbusTransport faulty =
                   new SimulatedModbusTransport("BUS_FAULT", _plant, options))
            {
                faulty.AddSlave(new SimulatedPressureSensor(4, _plant, "S2-1"));
                faulty.Open();

                for (int i = 0; i < 5; i++)
                {
                    ModbusResponse response = faulty.Execute(
                        ModbusRequest.ReadHolding(4, 0, 1), CancellationToken.None);

                    Assert.Equal(ModbusFailureKind.Timeout, response.FailureKind);
                }

                Assert.Equal(5, faulty.Statistics.TimeoutCount);
                Assert.Equal(5, faulty.Statistics.ConsecutiveFailures);
                Assert.Equal(0.0, faulty.Statistics.SuccessRatePercent);
            }
        }

        [Fact(Timeout = ServicesIntegrationTests.TestTimeoutMs)]
        public void 취소_토큰이_설정되면_즉시_중단한다()
        {
            using (CancellationTokenSource cts = new CancellationTokenSource())
            {
                cts.Cancel();

                ModbusResponse response = _transport.Execute(
                    ModbusRequest.ReadHolding(4, 0, 1), cts.Token);

                Assert.Equal(ModbusFailureKind.Canceled, response.FailureKind);
            }
        }

        // ── 통계 ────────────────────────────────────────────────────────────────

        [Fact(Timeout = ServicesIntegrationTests.TestTimeoutMs)]
        public void 통계가_성공률과_응답시간을_집계한다()
        {
            for (int i = 0; i < 10; i++)
            {
                _transport.Execute(ModbusRequest.ReadHolding(4, 0, 1), CancellationToken.None);
            }

            // 미등록 슬레이브로 실패 2건 발생
            _transport.Execute(ModbusRequest.ReadHolding(99, 0, 1), CancellationToken.None);
            _transport.Execute(ModbusRequest.ReadHolding(98, 0, 1), CancellationToken.None);

            Assert.Equal(12, _transport.Statistics.TotalTransactions);
            Assert.Equal(10, _transport.Statistics.SuccessCount);
            Assert.Equal(2, _transport.Statistics.TimeoutCount);
            Assert.Equal(2, _transport.Statistics.ConsecutiveFailures);

            Assert.InRange(_transport.Statistics.SuccessRatePercent, 83.0, 84.0);
            Assert.True(_transport.Statistics.AverageResponseMs > 0.0);
        }

        [Fact(Timeout = ServicesIntegrationTests.TestTimeoutMs)]
        public void 성공하면_연속실패_카운터가_초기화된다()
        {
            _transport.Execute(ModbusRequest.ReadHolding(99, 0, 1), CancellationToken.None);
            Assert.Equal(1, _transport.Statistics.ConsecutiveFailures);

            _transport.Execute(ModbusRequest.ReadHolding(4, 0, 1), CancellationToken.None);
            Assert.Equal(0, _transport.Statistics.ConsecutiveFailures);

            Assert.Equal(1, _transport.Statistics.MaxConsecutiveFailures);
        }

        [Fact(Timeout = ServicesIntegrationTests.TestTimeoutMs)]
        public void 통계를_초기화할_수_있다()
        {
            _transport.Execute(ModbusRequest.ReadHolding(4, 0, 1), CancellationToken.None);
            Assert.Equal(1, _transport.Statistics.TotalTransactions);

            _transport.Statistics.Reset();

            Assert.Equal(0, _transport.Statistics.TotalTransactions);
            Assert.Equal(100.0, _transport.Statistics.SuccessRatePercent);
        }
    }
}
