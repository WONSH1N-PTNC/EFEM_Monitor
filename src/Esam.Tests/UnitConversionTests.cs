using System;
using System.Collections.Generic;
using Esam.Domain.Configuration;
using Esam.Domain.Control;
using Esam.Domain.Units;
using Xunit;

namespace Esam.Tests
{
    /// <summary>밸브 pulse 변환기 검증. 통신자료 기준 90도 = 5000 pulse.</summary>
    public class ValvePulseConverterTests
    {
        private readonly ValvePulseConverter _c = ValvePulseConverter.Default;

        [Theory]
        [InlineData(0.0, 0)]
        [InlineData(50.0, 2500)]
        [InlineData(100.0, 5000)]
        [InlineData(25.0, 1250)]
        [InlineData(1.0, 50)]
        public void 개도율을_pulse로_변환한다(double percent, int expected)
        {
            Assert.Equal(expected, _c.PercentToPulse(percent));
        }

        [Theory]
        [InlineData(-10.0, 0)]
        [InlineData(150.0, 5000)]
        public void 범위를_벗어난_개도율은_클램프된다(double percent, int expected)
        {
            Assert.Equal(expected, _c.PercentToPulse(percent));
        }

        [Theory]
        [InlineData(0, 0.0)]
        [InlineData(2500, 45.0)]
        [InlineData(5000, 90.0)]
        public void pulse를_각도로_변환한다(int pulse, double expectedDegree)
        {
            Assert.Equal(expectedDegree, _c.PulseToDegree(pulse), 6);
        }

        [Fact]
        public void 왕복_변환에서_값이_보존된다()
        {
            for (int pulse = 0; pulse <= 5000; pulse += 250)
            {
                double percent = _c.PulseToPercent(pulse);
                Assert.Equal(pulse, _c.PercentToPulse(percent));
            }
        }

        [Fact]
        public void 잘못된_생성_인자는_예외를_던진다()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new ValvePulseConverter(0, 90.0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ValvePulseConverter(5000, 0.0));
        }
    }

    /// <summary>이동평균 필터 검증.</summary>
    public class MovingAverageFilterTests
    {
        [Fact]
        public void 창이_찰_때까지는_수집된_샘플만으로_평균을_낸다()
        {
            MovingAverageFilter f = new MovingAverageFilter(5);

            Assert.Equal(10.0, f.Add(10.0), 6);
            Assert.Equal(15.0, f.Add(20.0), 6);
            Assert.False(f.IsWarmedUp);
        }

        [Fact]
        public void 창이_차면_가장_오래된_값이_밀려난다()
        {
            MovingAverageFilter f = new MovingAverageFilter(3);
            f.Add(1.0);
            f.Add(2.0);
            f.Add(3.0);

            Assert.True(f.IsWarmedUp);
            Assert.Equal(2.0, f.Average, 6);

            // 4 를 넣으면 1 이 밀려나 (2+3+4)/3 = 3
            Assert.Equal(3.0, f.Add(4.0), 6);
        }

        [Fact]
        public void 비정상값은_창을_오염시키지_않는다()
        {
            MovingAverageFilter f = new MovingAverageFilter(3);
            f.Add(10.0);

            Assert.Equal(10.0, f.Add(double.NaN), 6);
            Assert.Equal(10.0, f.Add(double.PositiveInfinity), 6);
            Assert.Equal(1, f.SampleCount);
        }

        [Fact]
        public void Reset하면_초기화된다()
        {
            MovingAverageFilter f = new MovingAverageFilter(3);
            f.Add(100.0);
            f.Reset();

            Assert.Equal(0, f.SampleCount);
            Assert.Equal(0.0, f.Average, 6);
        }

        [Fact]
        public void 장시간_반복해도_누적오차가_커지지_않는다()
        {
            MovingAverageFilter f = new MovingAverageFilter(5);
            for (int i = 0; i < 100000; i++)
            {
                f.Add(0.1);
            }

            Assert.Equal(0.1, f.Average, 10);
        }

        [Fact]
        public void 창_크기가_1미만이면_예외를_던진다()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new MovingAverageFilter(0));
        }
    }

    /// <summary>모드 설정 및 제어 설정 검증 규칙 확인.</summary>
    public class ConfigurationTests
    {
        [Theory]
        [InlineData(SensorMode.Sensor1, 4.0, 8.0)]
        [InlineData(SensorMode.Sensor2, -40.0, 20.0)]
        [InlineData(SensorMode.Sensor3, -300.0, -100.0)]
        public void 기본_모드_대역이_ESAM_문서와_일치한다(SensorMode mode, double low, double high)
        {
            ModeSetting setting = new ControlConfig().GetMode(mode);

            Assert.Equal(low, setting.LowLimitPa, 6);
            Assert.Equal(high, setting.HighLimitPa, 6);
        }

        [Fact]
        public void 대역_경계값은_이탈로_판정한다()
        {
            ModeSetting setting = new ModeSetting(0.0, 10.0, 60.0); // -10 ~ 10

            Assert.True(setting.IsInBand(0.0));
            Assert.True(setting.IsInBand(9.99));
            Assert.False(setting.IsInBand(10.0));
            Assert.False(setting.IsInBand(-10.0));
        }

        [Fact]
        public void 체인_번호가_중복되면_검증에_실패한다()
        {
            ControlConfig config = Build.Config();
            config.Chains[1].Id = config.Chains[0].Id;

            IList<string> errors;
            Assert.False(config.Validate(out errors));
            Assert.Contains(errors, e => e.Contains("중복"));
        }

        [Fact]
        public void 체인_정의가_비어있으면_검증에_실패한다()
        {
            ControlConfig config = new ControlConfig();

            IList<string> errors;
            Assert.False(config.Validate(out errors));
            Assert.Contains(errors, e => e.Contains("Chains"));
        }

        [Fact]
        public void 기본_설정은_팬_사양이_확정되어_자동제어에_사용할_수_있다()
        {
            // Open Issue #20 이 닫혔다. JKBLD300V2 폐루프 속도 설정(0x4006) 범위
            // 200~4000 RPM 이 기본값이 되었으므로 기본 설정으로도 자동 제어가 가능하다.
            FanActuatorConfig fan = new FanActuatorConfig();

            Assert.Equal(200.0, fan.MinRpm);
            Assert.Equal(4000.0, fan.MaxRpm);
            Assert.True(fan.IsUsableForAutoControl);
        }

        [Fact]
        public void 팬_MaxRpm이_미확정이면_자동제어에_사용할_수_없다()
        {
            // 이쪽이 원래 검증하려던 것이다. 사양이 확정되면서 기본값이 바뀌었을 뿐,
            // "미확정 사양으로 자동 제어에 들어가지 않는다" 는 규칙은 그대로 필요하다.
            //
            // MaxRpm 이 0 이면 증속 여지를 계산할 수 없다. 그 상태로 밴드 제어에
            // 들어가면 도달하지 못하는 목표를 향해 계속 증속 지령을 낸다.
            FanActuatorConfig fan = new FanActuatorConfig();
            fan.MaxRpm = 0.0;

            Assert.False(fan.IsUsableForAutoControl);

            // MaxRpm 이 MinRpm 이하인 구성도 증속 범위가 없다.
            fan.MaxRpm = fan.MinRpm;
            Assert.False(fan.IsUsableForAutoControl);

            // 조정량이 0 이면 영원히 같은 회전수에 머문다.
            fan.MaxRpm = 4000.0;
            fan.StepRpm = 0.0;
            Assert.False(fan.IsUsableForAutoControl);
        }

        [Fact]
        public void 밸브_속도가_1에서_5_범위를_벗어나면_검증에_실패한다()
        {
            ValveActuatorConfig valve = new ValveActuatorConfig();
            valve.VelocityRpm = 6;

            string error;
            Assert.False(valve.Validate(out error));
            Assert.NotNull(error);
        }

        [Fact]
        public void 정상_설정은_검증을_통과한다()
        {
            IList<string> errors;
            Assert.True(Build.Config().Validate(out errors));
            Assert.Empty(errors);
        }
    }
}
