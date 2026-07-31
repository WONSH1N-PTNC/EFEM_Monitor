using System;
using System.Collections.Generic;
using Esam.Communication.Simulation;
using Esam.Domain.Configuration;
using Esam.Domain.Control;
using Esam.Domain.Units;
using Xunit;

namespace Esam.Tests
{
    /// <summary>1차 지연 및 노이즈 모델 검증.</summary>
    public class FirstOrderLagTests
    {
        [Fact]
        public void 시정수_경과_시_목표의_63퍼센트에_도달한다()
        {
            // 1차 지연의 정의: t = τ 에서 최종값의 1 - 1/e = 63.212%
            FirstOrderLag lag = new FirstOrderLag(2.0, 0.0);

            // 작은 스텝으로 2초 진행
            for (int i = 0; i < 200; i++)
            {
                lag.Advance(100.0, 0.01);
            }

            Assert.Equal(63.212, lag.Value, 1);
        }

        [Fact]
        public void 큰_dt에서도_발산하지_않고_목표를_넘지_않는다()
        {
            // 오일러 근사를 쓰면 dt > 2τ 에서 진동·발산한다. 지수 해는 항상 안정하다.
            FirstOrderLag lag = new FirstOrderLag(0.5, 0.0);

            lag.Advance(100.0, 10.0);

            Assert.InRange(lag.Value, 99.9, 100.0);
        }

        [Fact]
        public void 시정수가_0이면_즉시_추종한다()
        {
            FirstOrderLag lag = new FirstOrderLag(0.0, 0.0);

            Assert.Equal(50.0, lag.Advance(50.0, 0.1), 6);
        }

        [Fact]
        public void 음수_시정수는_거부한다()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new FirstOrderLag(-1.0, 0.0));
        }

        [Fact]
        public void 같은_시드는_같은_노이즈_수열을_만든다()
        {
            // 시뮬레이션 테스트가 재현 가능해야 회귀 검증이 성립한다.
            GaussianNoise a = new GaussianNoise(12345);
            GaussianNoise b = new GaussianNoise(12345);

            for (int i = 0; i < 50; i++)
            {
                Assert.Equal(a.Next(1.0), b.Next(1.0), 12);
            }
        }

        [Fact]
        public void 노이즈의_표준편차가_지정값에_수렴한다()
        {
            GaussianNoise noise = new GaussianNoise(777);
            const double sigma = 0.8;
            const int n = 200000;

            double sum = 0.0;
            double sumSq = 0.0;

            for (int i = 0; i < n; i++)
            {
                double v = noise.Next(sigma);
                sum += v;
                sumSq += v * v;
            }

            double mean = sum / n;
            double std = Math.Sqrt((sumSq / n) - (mean * mean));

            Assert.InRange(mean, -0.02, 0.02);
            Assert.InRange(std, sigma * 0.97, sigma * 1.03);
        }

        [Fact]
        public void 시그마가_0이면_노이즈가_없다()
        {
            GaussianNoise noise = new GaussianNoise(1);

            for (int i = 0; i < 10; i++)
            {
                Assert.Equal(0.0, noise.Next(0.0));
            }
        }

        [Fact]
        public void 이동평균_필터가_노이즈를_실제로_줄인다()
        {
            // 창 크기 N 의 이동평균은 백색노이즈 표준편차를 1/sqrt(N) 로 줄인다.
            // FilterWindowSize 기본값 5 의 근거를 수치로 확인한다.
            GaussianNoise noise = new GaussianNoise(2024);
            MovingAverageFilter filter = new MovingAverageFilter(5);
            const int n = 100000;

            double sumSq = 0.0;
            int counted = 0;

            for (int i = 0; i < n; i++)
            {
                double filtered = filter.Add(noise.Next(1.0));

                if (filter.IsWarmedUp)
                {
                    sumSq += filtered * filtered;
                    counted++;
                }
            }

            double std = Math.Sqrt(sumSq / counted);
            double expected = 1.0 / Math.Sqrt(5.0); // ≈ 0.447

            Assert.InRange(std, expected * 0.93, expected * 1.07);
        }
    }

    /// <summary>가상 플랜트 모델의 물리 동작 검증.</summary>
    public class PlantBehaviorTests
    {
        private static readonly string[] Sensor1Ids = { "S1-1", "S1-2", "S1-3" };

        private static PlantModel CreatePlant(PlantOptions options = null)
        {
            ControlConfig config = Build.Config();
            PlantModel plant = new PlantModel(
                config.Chains, Sensor1Ids, (options ?? new PlantOptions()).WithoutNoise(), 42);
            plant.CompleteAllHoming();
            return plant;
        }

        [Fact]
        public void 초기상태는_밸브_닫힘_팬_정지이다()
        {
            PlantModel plant = CreatePlant();

            int pulse;
            int target;
            bool home;
            Assert.True(plant.TryGetValve("V-1", out pulse, out target, out home));
            Assert.Equal(0, pulse);

            double rpm;
            double rpmTarget;
            Assert.True(plant.TryGetFan("F-1", out rpm, out rpmTarget));
            Assert.Equal(0.0, rpm);
        }

        [Fact]
        public void 전원_ON_직후에는_원점복귀가_완료되지_않은_상태다()
        {
            // 밸브 드라이브는 전원 ON 후 Homing 을 요구한다.
            // 이 조건을 시뮬레이션으로 재현할 수 있어야 상태머신을 검증할 수 있다.
            ControlConfig config = Build.Config();
            PlantModel plant = new PlantModel(config.Chains, Sensor1Ids, new PlantOptions(), 1);

            int pulse;
            int target;
            bool home;
            plant.TryGetValve("V-1", out pulse, out target, out home);

            Assert.False(home);

            plant.ApplyCommand(ActuatorCommand.HomeValve("V-1", "테스트"));
            plant.TryGetValve("V-1", out pulse, out target, out home);

            Assert.True(home);
        }

        [Fact]
        public void 밸브를_열면_압력이_내려간다()
        {
            // ESAM 순서도의 "압력상한 초과 → 밸브 위치 증가"가 성립하려면
            // 밸브 개도 증가가 압력 하강이어야 한다. 부호 규약의 회귀 방지 테스트.
            PlantModel plant = CreatePlant();

            double before;
            plant.TryGetTruePressure("S2-1", out before);

            plant.ApplyCommand(ActuatorCommand.SetValvePosition(
                "V-1", 5000, CommandPriority.Automatic, "테스트"));

            for (int i = 0; i < 300; i++)
            {
                plant.Advance(0.1);
            }

            double after;
            plant.TryGetTruePressure("S2-1", out after);

            Assert.True(after < before);
        }

        [Fact]
        public void 팬을_증속하면_압력이_추가로_내려간다()
        {
            PlantModel plant = CreatePlant();

            for (int i = 0; i < 200; i++)
            {
                plant.Advance(0.1);
            }

            double before;
            plant.TryGetTruePressure("S2-1", out before);

            plant.ApplyCommand(ActuatorCommand.SetFanRpm(
                "F-1", 3000, CommandPriority.Automatic, "테스트"));

            for (int i = 0; i < 300; i++)
            {
                plant.Advance(0.1);
            }

            double after;
            plant.TryGetTruePressure("S2-1", out after);

            Assert.True(after < before - 20.0);
        }

        [Fact]
        public void 밸브는_지정_속도로만_이동한다()
        {
            // 지령 즉시 반영 모델이면 Dwell 파라미터 검증이 불가능하다.
            PlantOptions options = new PlantOptions();
            options.ValveSlewPulsePerSec = 1000.0;

            PlantModel plant = CreatePlant(options);

            plant.ApplyCommand(ActuatorCommand.SetValvePosition(
                "V-1", 5000, CommandPriority.Automatic, "테스트"));

            plant.Advance(1.0); // 1초 → 최대 1000 pulse

            int pulse;
            int target;
            bool home;
            plant.TryGetValve("V-1", out pulse, out target, out home);

            Assert.Equal(1000, pulse);
            Assert.Equal(5000, target);
        }

        [Fact]
        public void 기본_파라미터는_ESAM_목표_운전점을_재현한다()
        {
            // 밸브 50%, 팬 33% 에서 센서2 ≈ -10 Pa, 센서3 ≈ -200 Pa, 센서1 ≈ 6 Pa 가 나와야
            // 이 모델로 제어 파라미터를 튜닝하는 것이 의미를 갖는다.
            PlantModel plant = CreatePlant();

            for (int chain = 1; chain <= 5; chain++)
            {
                plant.ApplyCommand(ActuatorCommand.SetValvePosition(
                    "V-" + chain, 2500, CommandPriority.Automatic, "테스트"));
                plant.ApplyCommand(ActuatorCommand.SetFanRpm(
                    "F-" + chain, 1000, CommandPriority.Automatic, "테스트"));
            }

            for (int i = 0; i < 600; i++)
            {
                plant.Advance(0.1);
            }

            double s1;
            double s2;
            double s3;
            plant.TryGetTruePressure("S1-1", out s1);
            plant.TryGetTruePressure("S2-1", out s2);
            plant.TryGetTruePressure("S3-1", out s3);

            Assert.InRange(s1, 4.0, 8.0);       // 목표 6 Pa ± 2
            Assert.InRange(s2, -13.0, -7.0);    // 목표 -10 Pa
            Assert.InRange(s3, -210.0, -190.0); // 목표 -200 Pa
        }

        [Fact]
        public void 센서1은_전_체인의_평균에_반응한다()
        {
            // 센서 1은 EFEM 내부 공통이므로 한 체인만 움직여도 영향이 있지만
            // 전 체인을 움직였을 때보다 작아야 한다.
            PlantModel one = CreatePlant();
            one.ApplyCommand(ActuatorCommand.SetValvePosition(
                "V-1", 5000, CommandPriority.Automatic, "테스트"));

            PlantModel all = CreatePlant();
            for (int chain = 1; chain <= 5; chain++)
            {
                all.ApplyCommand(ActuatorCommand.SetValvePosition(
                    "V-" + chain, 5000, CommandPriority.Automatic, "테스트"));
            }

            for (int i = 0; i < 600; i++)
            {
                one.Advance(0.1);
                all.Advance(0.1);
            }

            double oneChain;
            double allChains;
            one.TryGetTruePressure("S1-1", out oneChain);
            all.TryGetTruePressure("S1-1", out allChains);

            Assert.True(allChains < oneChain);
        }

        [Fact]
        public void 알_수_없는_디바이스_ID는_무시한다()
        {
            PlantModel plant = CreatePlant();

            Assert.False(plant.ApplyCommand(ActuatorCommand.CloseValve(
                "V-99", CommandPriority.Automatic, "테스트")));

            double dummy;
            Assert.False(plant.TryGetPressure("S9-9", out dummy));
        }

        [Fact]
        public void 체인_정의가_비어있으면_예외를_던진다()
        {
            Assert.Throws<ArgumentException>(() =>
                new PlantModel(new List<ChainDefinition>(), Sensor1Ids, null, 0));
        }
    }
}
