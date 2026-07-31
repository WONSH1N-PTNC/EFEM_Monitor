using System;
using Esam.Domain.Configuration;
using Esam.Domain.Control;
using Esam.Domain.Models;
using Xunit;

namespace Esam.Tests
{
    /// <summary>
    /// ESAM 운용방법 설명자료 p.10~12 순서도가 코드로 정확히 구현되었는지 검증한다.
    /// </summary>
    public class BandControlPolicyTests
    {
        private readonly BandControlPolicy _policy = new BandControlPolicy();

        // ── 1. 정상 대역 ────────────────────────────────────────────────────────

        [Theory]
        [InlineData(SensorMode.Sensor1, 6.0)]    // Set 6 Pa, Band ±2 → 4~8
        [InlineData(SensorMode.Sensor2, -10.0)]  // Set -10 Pa, Band ±30 → -40~20
        [InlineData(SensorMode.Sensor3, -200.0)] // Set -200 Pa, Band ±100 → -300~-100
        public void 정상대역이면_지령을_내지_않는다(SensorMode mode, double pv)
        {
            ControlConfig config = Build.Config(mode);
            ChainRuntime runtime = new ChainRuntime(config.Chains[0]);

            ChainControlContext ctx = Build.Context(
                runtime, pv, Build.Valve("V-1", 2500), Build.Fan("F-1", 1000, 1000),
                config, mode, Build.T0);

            ControlDecision decision = _policy.Step(ctx);

            Assert.Equal(ControlResult.InBand, decision.Result);
            Assert.Empty(decision.Commands);
            Assert.Equal(0.0, runtime.DeviationElapsedMs);
        }

        // ── 2. 하한 이탈: 밸브 감소 + 팬 OFF ────────────────────────────────────

        [Fact]
        public void 하한이탈이면_밸브를_감소시키고_팬을_정지시킨다()
        {
            ControlConfig config = Build.Config();
            ChainRuntime runtime = new ChainRuntime(config.Chains[0]);

            // Sensor2 대역은 -40 ~ 20 Pa. -50 Pa 는 하한 이탈.
            ChainControlContext ctx = Build.Context(
                runtime, -50.0, Build.Valve("V-1", 2500), Build.Fan("F-1", 1000, 1000),
                config, SensorMode.Sensor2, Build.T0);

            ControlDecision decision = _policy.Step(ctx);

            Assert.Equal(ControlResult.DeviatingLow, decision.Result);
            Assert.Equal(2, decision.Commands.Count);

            ActuatorCommand valveCmd = decision.Commands[0];
            Assert.Equal(ActuatorCommandKind.SetValvePosition, valveCmd.Kind);
            Assert.Equal("V-1", valveCmd.DeviceId);
            Assert.Equal(2400.0, valveCmd.Value); // 2500 - StepPulse(100)

            ActuatorCommand fanCmd = decision.Commands[1];
            Assert.Equal(ActuatorCommandKind.StopFan, fanCmd.Kind);
            Assert.Equal("F-1", fanCmd.DeviceId);
        }

        [Fact]
        public void 하한이탈_밸브가_이미_닫혀있으면_Time_경과_후_ErrorLow가_된다()
        {
            ControlConfig config = Build.Config();
            ModeSetting mode = config.GetMode(SensorMode.Sensor2); // TimeSec = 120
            ChainRuntime runtime = new ChainRuntime(config.Chains[0]);

            ValveState closedValve = Build.Valve("V-1", 0);
            FanState stoppedFan = Build.Fan("F-1", 0, 0, status: FanRunStatus.Stopped);

            // 첫 스텝: 이탈 시작. 아직 확정 시간(120초) 미달이므로 DeviatingLow.
            ControlDecision first = _policy.Step(Build.Context(
                runtime, -100.0, closedValve, stoppedFan, config, SensorMode.Sensor2, Build.T0));

            Assert.Equal(ControlResult.DeviatingLow, first.Result);
            Assert.Empty(first.Commands);

            // Time 경과 전: 여전히 DeviatingLow
            ControlDecision middle = _policy.Step(Build.Context(
                runtime, -100.0, closedValve, stoppedFan, config, SensorMode.Sensor2,
                Build.T0.AddMilliseconds(mode.TimeMs - 1)));

            Assert.Equal(ControlResult.DeviatingLow, middle.Result);

            // Time 경과 후: ErrorLow 확정
            ControlDecision confirmed = _policy.Step(Build.Context(
                runtime, -100.0, closedValve, stoppedFan, config, SensorMode.Sensor2,
                Build.T0.AddMilliseconds(mode.TimeMs + 1)));

            Assert.Equal(ControlResult.ErrorLow, confirmed.Result);
            Assert.Empty(confirmed.Commands);
        }

        // ── 3. 상한 이탈: 밸브 증가 우선 ────────────────────────────────────────

        [Fact]
        public void 상한이탈이면_밸브를_먼저_증가시키고_팬은_건드리지_않는다()
        {
            ControlConfig config = Build.Config();
            ChainRuntime runtime = new ChainRuntime(config.Chains[0]);

            // Sensor2 상한 20 Pa. 30 Pa 는 상한 이탈. 밸브는 2500(45도)로 여유 있음.
            ControlDecision decision = _policy.Step(Build.Context(
                runtime, 30.0, Build.Valve("V-1", 2500), Build.Fan("F-1", 1000, 1000),
                config, SensorMode.Sensor2, Build.T0));

            Assert.Equal(ControlResult.DeviatingHigh, decision.Result);
            Assert.Single(decision.Commands);

            ActuatorCommand cmd = decision.Commands[0];
            Assert.Equal(ActuatorCommandKind.SetValvePosition, cmd.Kind);
            Assert.Equal(2600.0, cmd.Value); // 2500 + StepPulse(100)
        }

        [Fact]
        public void 상한이탈_밸브가_포화되면_그때_팬을_증속한다()
        {
            ControlConfig config = Build.Config();
            ChainRuntime runtime = new ChainRuntime(config.Chains[0]);

            // 밸브가 이미 5000 pulse(90도) 포화 상태
            ControlDecision decision = _policy.Step(Build.Context(
                runtime, 30.0, Build.Valve("V-1", 5000), Build.Fan("F-1", 1000, 1000),
                config, SensorMode.Sensor2, Build.T0));

            Assert.Equal(ControlResult.DeviatingHigh, decision.Result);
            Assert.Single(decision.Commands);

            ActuatorCommand cmd = decision.Commands[0];
            Assert.Equal(ActuatorCommandKind.SetFanRpm, cmd.Kind);
            Assert.Equal("F-1", cmd.DeviceId);
            Assert.Equal(1100.0, cmd.Value); // 1000 + StepRpm(100)
        }

        [Fact]
        public void 상한이탈_밸브포화_팬최대이면_Time_경과_후_ErrorHigh가_된다()
        {
            ControlConfig config = Build.Config();
            ModeSetting mode = config.GetMode(SensorMode.Sensor2);
            ChainRuntime runtime = new ChainRuntime(config.Chains[0]);

            ValveState saturated = Build.Valve("V-1", 5000);
            FanState maxFan = Build.Fan("F-1", 3000, 3000); // MaxRpm = 3000

            ControlDecision first = _policy.Step(Build.Context(
                runtime, 30.0, saturated, maxFan, config, SensorMode.Sensor2, Build.T0));

            Assert.Equal(ControlResult.DeviatingHigh, first.Result);
            Assert.Empty(first.Commands);

            ControlDecision confirmed = _policy.Step(Build.Context(
                runtime, 30.0, saturated, maxFan, config, SensorMode.Sensor2,
                Build.T0.AddMilliseconds(mode.TimeMs + 1)));

            Assert.Equal(ControlResult.ErrorHigh, confirmed.Result);
        }

        [Fact]
        public void 팬_MaxRpm이_미설정이면_밸브포화_후_증속하지_않고_Skip한다()
        {
            // DESIGN.md Open Issue #20 — 팬 사양 미확보 상태 보호 동작
            ControlConfig config = Build.Config();
            config.Fan.MaxRpm = 0.0;

            ChainRuntime runtime = new ChainRuntime(config.Chains[0]);

            ControlDecision decision = _policy.Step(Build.Context(
                runtime, 30.0, Build.Valve("V-1", 5000), Build.Fan("F-1", 0, 0),
                config, SensorMode.Sensor2, Build.T0));

            Assert.Equal(ControlResult.Skipped, decision.Result);
            Assert.Empty(decision.Commands);
        }

        // ── 4. 안전 가드 ────────────────────────────────────────────────────────

        [Fact]
        public void 측정값_품질이_나쁘면_제어를_건너뛴다()
        {
            ControlConfig config = Build.Config();
            ChainRuntime runtime = new ChainRuntime(config.Chains[0]);

            ControlDecision decision = _policy.Step(Build.Context(
                runtime, 100.0, Build.Valve("V-1", 2500), Build.Fan("F-1", 1000),
                config, SensorMode.Sensor2, Build.T0, Quality.Bad));

            Assert.Equal(ControlResult.Skipped, decision.Result);
            Assert.Empty(decision.Commands);
        }

        [Fact]
        public void 밸브_원점복귀가_안되어_있으면_제어를_건너뛴다()
        {
            ControlConfig config = Build.Config();
            ChainRuntime runtime = new ChainRuntime(config.Chains[0]);

            ControlDecision decision = _policy.Step(Build.Context(
                runtime, 100.0, Build.Valve("V-1", 2500, homeDone: false), Build.Fan("F-1", 1000),
                config, SensorMode.Sensor2, Build.T0));

            Assert.Equal(ControlResult.Skipped, decision.Result);
            Assert.Empty(decision.Commands);
        }

        [Fact]
        public void 밸브에_알람이_있으면_제어를_건너뛴다()
        {
            ControlConfig config = Build.Config();
            ChainRuntime runtime = new ChainRuntime(config.Chains[0]);

            ControlDecision decision = _policy.Step(Build.Context(
                runtime, 100.0, Build.Valve("V-1", 2500, alarmCode: 0x21), Build.Fan("F-1", 1000),
                config, SensorMode.Sensor2, Build.T0));

            Assert.Equal(ControlResult.Skipped, decision.Result);
        }

        [Fact]
        public void 밸브_지령은_설정된_최대_최소를_넘지_않는다()
        {
            ControlConfig config = Build.Config();
            config.Valve.StepPulse = 1000;
            ChainRuntime runtime = new ChainRuntime(config.Chains[0]);

            // 4500 + 1000 = 5500 이지만 MaxPulse(5000)로 클램프되어야 한다.
            ControlDecision high = _policy.Step(Build.Context(
                runtime, 30.0, Build.Valve("V-1", 4500), Build.Fan("F-1", 0),
                config, SensorMode.Sensor2, Build.T0));

            Assert.Single(high.Commands);
            Assert.Equal(5000.0, high.Commands[0].Value);

            // 500 - 1000 = -500 이지만 MinPulse(0)로 클램프되어야 한다.
            ChainRuntime runtime2 = new ChainRuntime(config.Chains[1]);
            ControlDecision low = _policy.Step(Build.Context(
                runtime2, -50.0, Build.Valve("V-2", 500), Build.Fan("F-2", 0, 0, status: FanRunStatus.Stopped),
                config, SensorMode.Sensor2, Build.T0));

            Assert.Single(low.Commands);
            Assert.Equal(0.0, low.Commands[0].Value);
        }

        // ── 5. Dwell (헌팅 방지) ────────────────────────────────────────────────

        [Fact]
        public void Dwell_시간이_지나기_전에는_밸브를_다시_움직이지_않는다()
        {
            ControlConfig config = Build.Config();
            config.Valve.DwellMs = 1000;
            ChainRuntime runtime = new ChainRuntime(config.Chains[0]);

            ControlDecision first = _policy.Step(Build.Context(
                runtime, 30.0, Build.Valve("V-1", 2500), Build.Fan("F-1", 0),
                config, SensorMode.Sensor2, Build.T0));

            Assert.Single(first.Commands);

            // 500ms 후 — Dwell(1000ms) 미경과이므로 지령이 없어야 한다.
            ControlDecision second = _policy.Step(Build.Context(
                runtime, 30.0, Build.Valve("V-1", 2600, targetPulse: 2600), Build.Fan("F-1", 0),
                config, SensorMode.Sensor2, Build.T0.AddMilliseconds(500)));

            Assert.Equal(ControlResult.DeviatingHigh, second.Result);
            Assert.Empty(second.Commands);

            // 1100ms 후 — Dwell 경과이므로 다시 지령이 나가야 한다.
            ControlDecision third = _policy.Step(Build.Context(
                runtime, 30.0, Build.Valve("V-1", 2600, targetPulse: 2600), Build.Fan("F-1", 0),
                config, SensorMode.Sensor2, Build.T0.AddMilliseconds(1100)));

            Assert.Single(third.Commands);
            Assert.Equal(2700.0, third.Commands[0].Value);
        }

        // ── 6. 대역 복귀 시 이탈 누적 초기화 ────────────────────────────────────

        [Fact]
        public void 대역에_복귀하면_이탈_누적시간이_초기화된다()
        {
            ControlConfig config = Build.Config();
            ChainRuntime runtime = new ChainRuntime(config.Chains[0]);

            _policy.Step(Build.Context(
                runtime, 30.0, Build.Valve("V-1", 5000), Build.Fan("F-1", 3000, 3000),
                config, SensorMode.Sensor2, Build.T0));

            _policy.Step(Build.Context(
                runtime, 30.0, Build.Valve("V-1", 5000), Build.Fan("F-1", 3000, 3000),
                config, SensorMode.Sensor2, Build.T0.AddMilliseconds(5000)));

            Assert.True(runtime.DeviationElapsedMs > 0.0);

            // 정상 대역으로 복귀
            _policy.Step(Build.Context(
                runtime, -10.0, Build.Valve("V-1", 5000), Build.Fan("F-1", 3000, 3000),
                config, SensorMode.Sensor2, Build.T0.AddMilliseconds(6000)));

            Assert.Equal(0.0, runtime.DeviationElapsedMs);
            Assert.Equal(ControlResult.InBand, runtime.LastResult);
        }

        [Fact]
        public void 컨텍스트가_null이면_예외를_던진다()
        {
            Assert.Throws<ArgumentNullException>(() => _policy.Step(null));
        }
    }
}
