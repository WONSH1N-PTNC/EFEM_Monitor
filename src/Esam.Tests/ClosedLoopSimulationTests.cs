using System;
using System.Collections.Generic;
using Esam.Communication.Simulation;
using Esam.Domain.Configuration;
using Esam.Domain.Control;
using Esam.Domain.Models;
using Xunit;

namespace Esam.Tests
{
    /// <summary>
    /// 폐루프 시뮬레이션 검증. <b>S2 단계의 핵심 산출물</b>이다.
    /// </summary>
    /// <remarks>
    /// 지금까지의 테스트는 제어 알고리즘이 "정해진 입력에 정해진 지령을 내는가"를 봤다.
    /// 이 테스트는 그 지령을 실제 플랜트에 적용했을 때 <b>압력이 목표 대역으로 수렴하는가</b>를 본다.
    /// 하드웨어 없이 제어 파라미터(Step, Dwell)를 결정할 수 있게 하는 것이 목적이다.
    /// </remarks>
    public class ClosedLoopSimulationTests
    {
        private const double DtSec = 0.2;                 // ControlPeriodMs 200 과 동일
        private const int ControlPeriodMs = 200;
        private static readonly string[] Sensor1Ids = { "S1-1", "S1-2", "S1-3" };

        /// <summary>폐루프 실행 결과 요약.</summary>
        private sealed class LoopResult
        {
            public int ValveCommands;
            public int FanCommands;
            public double MaxPulse;
            public double FinalPulse;
            public double MaxRpm;
            public double FinalRpm;
            public double FinalPv;
            public double MinPv;
            public double MaxPv;
            public int DirectionReversals;
        }

        /// <summary>
        /// 5개 체인을 모두 자동 제어하며 지정 스텝만큼 폐루프를 돌린다.
        /// </summary>
        /// <param name="mode">센서 모드.</param>
        /// <param name="valveDwellMs">밸브 Dwell [ms].</param>
        /// <param name="fanDwellMs">팬 Dwell [ms].</param>
        /// <param name="steps">제어 스텝 수.</param>
        /// <param name="modeOverride">모드 설정을 덮어쓸 값. null 이면 기본값을 사용한다.</param>
        /// <returns>체인 1 기준 실행 결과.</returns>
        private static LoopResult RunClosedLoop(
            SensorMode mode, int valveDwellMs, int fanDwellMs, int steps, ModeSetting modeOverride = null)
        {
            ControlConfig config = Build.Config(mode);
            config.Valve.DwellMs = valveDwellMs;
            config.Fan.DwellMs = fanDwellMs;

            if (modeOverride != null)
            {
                config.Modes[mode] = modeOverride;
            }

            PlantModel plant = new PlantModel(
                config.Chains, Sensor1Ids, new PlantOptions().WithoutNoise(), 20260731);
            plant.CompleteAllHoming();

            BandControlPolicy policy = new BandControlPolicy();
            ModeSetting setting = config.GetMode(mode);

            List<ChainRuntime> runtimes = new List<ChainRuntime>();
            foreach (ChainDefinition chain in config.Chains)
            {
                runtimes.Add(new ChainRuntime(chain));
            }

            LoopResult result = new LoopResult();
            result.MinPv = double.MaxValue;
            result.MaxPv = double.MinValue;

            int previousDirection = 0;

            for (int step = 0; step < steps; step++)
            {
                DateTime nowUtc = Build.T0.AddMilliseconds((double)step * ControlPeriodMs);
                List<ActuatorCommand> pending = new List<ActuatorCommand>();

                for (int i = 0; i < runtimes.Count; i++)
                {
                    ChainRuntime runtime = runtimes[i];
                    ChainDefinition definition = runtime.Definition;

                    string sensorId = mode == SensorMode.Sensor1
                        ? "S1-1"
                        : (mode == SensorMode.Sensor2 ? definition.Sensor2Id : definition.Sensor3Id);

                    double pv;
                    plant.TryGetTruePressure(sensorId, out pv);

                    ChainControlContext context = new ChainControlContext(
                        runtime, pv, Quality.Good,
                        ReadValve(plant, definition.ValveId),
                        ReadFan(plant, definition.FanId),
                        setting, config.Valve, config.Fan, nowUtc);

                    ControlDecision decision = policy.Step(context);

                    // 체인 1을 대표로 삼아 지표를 수집한다(5개 체인이 동일하게 동작한다).
                    if (i == 0)
                    {
                        result.MinPv = Math.Min(result.MinPv, pv);
                        result.MaxPv = Math.Max(result.MaxPv, pv);
                        result.FinalPv = pv;

                        int direction = 0;
                        if (decision.Result == ControlResult.DeviatingHigh)
                        {
                            direction = 1;
                        }
                        else if (decision.Result == ControlResult.DeviatingLow)
                        {
                            direction = -1;
                        }

                        if (direction != 0)
                        {
                            if (previousDirection != 0 && direction != previousDirection)
                            {
                                result.DirectionReversals++;
                            }

                            previousDirection = direction;
                        }

                        foreach (ActuatorCommand command in decision.Commands)
                        {
                            if (command.Target == ActuatorTarget.Valve)
                            {
                                result.ValveCommands++;
                            }
                            else
                            {
                                result.FanCommands++;
                            }
                        }
                    }

                    pending.AddRange(decision.Commands);
                }

                plant.ApplyCommands(pending);
                plant.Advance(DtSec);

                int pulse;
                int targetPulse;
                bool home;
                plant.TryGetValve("V-1", out pulse, out targetPulse, out home);
                result.MaxPulse = Math.Max(result.MaxPulse, pulse);
                result.FinalPulse = pulse;

                double rpm;
                double targetRpm;
                plant.TryGetFan("F-1", out rpm, out targetRpm);
                result.MaxRpm = Math.Max(result.MaxRpm, rpm);
                result.FinalRpm = rpm;
            }

            return result;
        }

        /// <summary>플랜트 상태에서 밸브 스냅샷을 만든다.</summary>
        /// <param name="plant">가상 플랜트.</param>
        /// <param name="valveId">밸브 ID.</param>
        /// <returns>밸브 상태.</returns>
        private static ValveState ReadValve(PlantModel plant, string valveId)
        {
            int pulse;
            int targetPulse;
            bool home;
            plant.TryGetValve(valveId, out pulse, out targetPulse, out home);

            return new ValveState(
                valveId,
                pulse,
                targetPulse,
                pulse / 5000.0 * 100.0,
                pulse / 5000.0 * 90.0,
                pulse == targetPulse ? ValveMotionStatus.Idle : ValveMotionStatus.Moving,
                0,
                home,
                Quality.Good,
                Build.T0);
        }

        /// <summary>플랜트 상태에서 팬 스냅샷을 만든다.</summary>
        /// <param name="plant">가상 플랜트.</param>
        /// <param name="fanId">팬 ID.</param>
        /// <returns>팬 상태.</returns>
        private static FanState ReadFan(PlantModel plant, string fanId)
        {
            double rpm;
            double targetRpm;
            plant.TryGetFan(fanId, out rpm, out targetRpm);

            FanRunStatus status;
            if (targetRpm <= 0.0 && rpm <= 10.0)
            {
                status = FanRunStatus.Stopped;
            }
            else if (Math.Abs(rpm - targetRpm) <= 10.0)
            {
                status = FanRunStatus.Running;
            }
            else
            {
                status = FanRunStatus.Ramping;
            }

            return new FanState(fanId, rpm, targetRpm, status, 0, Quality.Good, Build.T0);
        }

        // ── 수렴 검증 ───────────────────────────────────────────────────────────

        [Fact]
        public void Sensor2_모드는_목표_대역으로_수렴한다()
        {
            // 문서 기본값(-10 Pa ± 30 → 대역 -40 ~ 20)은 초기 압력 +20 Pa 가 이미 경계에 걸려
            // 밸브 한 스텝만으로 대역에 들어와 버린다. 수렴 능력을 실제로 검증하려면
            // 대역을 좁혀 제어기가 목표까지 밸브를 몰고 가도록 해야 한다.
            ModeSetting narrow = new ModeSetting(-10.0, 5.0, 120.0); // 대역 -15 ~ -5 Pa

            LoopResult result = RunClosedLoop(SensorMode.Sensor2, 1000, 1000, 500, narrow);

            Assert.InRange(result.FinalPv, -15.0, -5.0);

            // 초기 +20 Pa 에서 목표까지 실제로 밸브를 구동했음을 확인한다.
            Assert.True(result.ValveCommands > 5,
                "좁은 대역에서는 밸브를 여러 번 조작해야 수렴한다.");

            // 밸브만으로 도달하므로 팬은 개입하지 않고, 포화되지도 않는다.
            Assert.Equal(0, result.FanCommands);
            Assert.True(result.FinalPulse < 5000);
        }

        [Fact]
        public void 문서_기본_대역은_초기상태에서_거의_즉시_충족된다()
        {
            // Sensor2 기본 대역(-40 ~ 20 Pa)은 폭이 60 Pa 로 매우 넓다.
            // 밸브 닫힘 상태의 초기 압력(+20 Pa)이 상한에 걸려 있어 한 스텝이면 대역에 들어온다.
            // 즉 이 설정은 "압력을 -10 Pa 로 맞추는" 것이 아니라 "±30 Pa 안에 두는" 운전이다.
            // 설정 의도 확인이 필요한 사항이므로 현재 동작을 명시적으로 고정해 둔다.
            LoopResult result = RunClosedLoop(SensorMode.Sensor2, 1000, 1000, 500);

            Assert.InRange(result.FinalPv, -40.0, 20.0);
            Assert.Equal(1, result.ValveCommands);
            Assert.Equal(0, result.FanCommands);
        }

        [Fact]
        public void Sensor3_모드는_목표_대역으로_수렴한다()
        {
            // Sensor3: -200 Pa ± 100 → 대역 -300 ~ -100 Pa. 초기 -50 Pa.
            LoopResult result = RunClosedLoop(SensorMode.Sensor3, 1000, 1000, 800);

            Assert.InRange(result.FinalPv, -300.0, -100.0);
        }

        [Fact]
        public void Sensor1_모드는_좁은_대역에도_수렴한다()
        {
            // Sensor1: 6 Pa ± 2 → 대역 4 ~ 8 Pa. 세 모드 중 가장 좁아 수렴이 까다롭다.
            LoopResult result = RunClosedLoop(SensorMode.Sensor1, 1000, 1000, 600);

            Assert.InRange(result.FinalPv, 4.0, 8.0);
        }

        // ── Dwell 파라미터의 효과 ───────────────────────────────────────────────

        [Fact]
        public void Dwell이_충분하면_단조_수렴하고_팬을_건드리지_않는다()
        {
            LoopResult result = RunClosedLoop(SensorMode.Sensor1, 1000, 1000, 600);

            // 대역 아래로 언더슈트하지 않는다.
            Assert.True(result.MinPv > 4.0,
                "Dwell 이 충분하면 하한(4 Pa) 아래로 내려가지 않아야 한다.");

            // 방향 전환(증가 → 감소)이 없다 = 헌팅 없음.
            Assert.Equal(0, result.DirectionReversals);

            // 밸브만으로 해결되므로 팬 지령이 없다.
            Assert.Equal(0, result.FanCommands);

            // 밸브가 포화되지 않아 제어 여유가 남는다.
            Assert.True(result.MaxPulse < 5000,
                "Dwell 이 충분하면 밸브가 완전 열림까지 가지 않아야 한다.");
        }

        [Fact]
        public void Dwell이_없으면_오버슈트하여_대역을_이탈한다()
        {
            // 이것이 Dwell 파라미터가 필요한 이유의 수치적 근거다.
            // 압력 시정수(3초)보다 지령 주기(0.2초)가 훨씬 빠르면
            // 제어기가 압력 변화를 보기 전에 계속 밸브를 열어 과도 조작한다.
            LoopResult noDwell = RunClosedLoop(SensorMode.Sensor1, 0, 0, 600);
            LoopResult withDwell = RunClosedLoop(SensorMode.Sensor1, 1000, 1000, 600);

            // 대역 하한(4 Pa) 아래로 언더슈트한다.
            Assert.True(noDwell.MinPv < 4.0,
                "Dwell 이 없으면 하한 아래로 언더슈트해야 한다(그것이 문제의 증거다).");

            // 밸브가 완전 열림까지 포화된다.
            Assert.Equal(5000.0, noDwell.MaxPulse);

            // 불필요하게 팬까지 기동한다.
            Assert.True(noDwell.FanCommands > 0,
                "밸브 포화 후 팬이 개입하므로 팬 지령이 발생한다.");

            // Dwell 이 있는 쪽이 모든 면에서 낫다.
            Assert.True(withDwell.MaxPulse < noDwell.MaxPulse);
            Assert.True(withDwell.MinPv > noDwell.MinPv);
            Assert.True(withDwell.FanCommands < noDwell.FanCommands);
        }

        [Fact]
        public void 수렴_후에는_지령이_더_발생하지_않는다()
        {
            // 정상 대역에 들어오면 밸브·팬을 유지해야 한다(순서도 "위치 유지 / 팬 유지").
            LoopResult shortRun = RunClosedLoop(SensorMode.Sensor1, 1000, 1000, 400);
            LoopResult longRun = RunClosedLoop(SensorMode.Sensor1, 1000, 1000, 600);

            Assert.Equal(shortRun.ValveCommands, longRun.ValveCommands);
            Assert.Equal(shortRun.FinalPulse, longRun.FinalPulse);
        }

        [Fact]
        public void 노이즈가_있어도_대역_안에_머문다()
        {
            // 실제 센서 노이즈가 섞이면 대역 경계에서 지령이 튈 수 있다.
            // 이동평균 필터와 Dwell 조합이 이를 억제하는지 확인한다.
            ControlConfig config = Build.Config(SensorMode.Sensor1);
            config.Valve.DwellMs = 1000;
            config.Fan.DwellMs = 1000;

            PlantModel plant = new PlantModel(
                config.Chains, Sensor1Ids, new PlantOptions(), 20260731);
            plant.CompleteAllHoming();

            BandControlPolicy policy = new BandControlPolicy();
            ModeSetting setting = config.GetMode(SensorMode.Sensor1);

            List<ChainRuntime> runtimes = new List<ChainRuntime>();
            List<Esam.Domain.Units.MovingAverageFilter> filters =
                new List<Esam.Domain.Units.MovingAverageFilter>();

            foreach (ChainDefinition chain in config.Chains)
            {
                runtimes.Add(new ChainRuntime(chain));
                filters.Add(new Esam.Domain.Units.MovingAverageFilter(config.FilterWindowSize));
            }

            double lastTruePv = 0.0;

            for (int step = 0; step < 800; step++)
            {
                DateTime nowUtc = Build.T0.AddMilliseconds((double)step * ControlPeriodMs);
                List<ActuatorCommand> pending = new List<ActuatorCommand>();

                for (int i = 0; i < runtimes.Count; i++)
                {
                    ChainDefinition definition = runtimes[i].Definition;

                    double noisy;
                    plant.TryGetPressure("S1-1", out noisy);
                    double filtered = filters[i].Add(noisy);

                    ChainControlContext context = new ChainControlContext(
                        runtimes[i], filtered, Quality.Good,
                        ReadValve(plant, definition.ValveId),
                        ReadFan(plant, definition.FanId),
                        setting, config.Valve, config.Fan, nowUtc);

                    pending.AddRange(policy.Step(context).Commands);
                }

                plant.ApplyCommands(pending);
                plant.Advance(DtSec);
                plant.TryGetTruePressure("S1-1", out lastTruePv);
            }

            // 노이즈로 인해 경계를 약간 넘나들 수 있으므로 참값 기준으로 여유를 둔다.
            Assert.InRange(lastTruePv, 3.0, 9.0);
        }
    }
}
