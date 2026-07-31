using System;

namespace Esam.Communication.Simulation
{
    /// <summary>
    /// 1차 지연(단일 시정수) 응답 모델. dy/dt = (u - y) / τ 의 이산 해를 사용한다.
    /// </summary>
    /// <remarks>
    /// <para>EFEM 챔버는 유한한 부피를 가지므로 밸브 개도나 팬 회전수를 바꿔도
    /// 압력이 즉시 따라오지 않는다. 이 지연이 곧 제어 헌팅의 원인이므로,
    /// 시뮬레이션에 반드시 포함해야 Dwell·Step 파라미터를 의미 있게 튜닝할 수 있다.</para>
    /// <para>오일러 근사(<c>y += (u-y)*dt/τ</c>) 대신 지수 해
    /// <c>y += (u-y)*(1-exp(-dt/τ))</c> 를 쓴다. dt 가 τ 에 비해 커져도
    /// 발산하지 않고 항상 물리적으로 타당한 값을 유지하기 때문이다.</para>
    /// </remarks>
    public sealed class FirstOrderLag
    {
        private double _timeConstantSec;

        /// <summary>시정수 [초]. 목표값의 63.2%에 도달하는 데 걸리는 시간.</summary>
        public double TimeConstantSec
        {
            get { return _timeConstantSec; }
            set
            {
                if (value < 0.0)
                {
                    throw new ArgumentOutOfRangeException(
                        "value", value, "시정수는 음수일 수 없습니다.");
                }

                _timeConstantSec = value;
            }
        }

        /// <summary>현재 출력값.</summary>
        public double Value { get; private set; }

        /// <summary>1차 지연 모델을 생성한다.</summary>
        /// <param name="timeConstantSec">시정수 [초]. 0 이면 지연 없이 즉시 추종한다.</param>
        /// <param name="initialValue">초기 출력값.</param>
        public FirstOrderLag(double timeConstantSec, double initialValue)
        {
            TimeConstantSec = timeConstantSec;
            Value = initialValue;
        }

        /// <summary>목표값을 향해 지정 시간만큼 진행시킨다.</summary>
        /// <param name="target">목표(입력) 값.</param>
        /// <param name="dtSec">진행 시간 [초].</param>
        /// <returns>갱신된 출력값.</returns>
        public double Advance(double target, double dtSec)
        {
            if (dtSec <= 0.0)
            {
                return Value;
            }

            if (_timeConstantSec <= 0.0)
            {
                Value = target;
                return Value;
            }

            double alpha = 1.0 - Math.Exp(-dtSec / _timeConstantSec);
            Value += (target - Value) * alpha;
            return Value;
        }

        /// <summary>출력값을 강제로 설정한다. 시뮬레이션 초기화에 사용한다.</summary>
        /// <param name="value">설정할 값.</param>
        public void Reset(double value)
        {
            Value = value;
        }
    }

    /// <summary>
    /// 결정적 가우시안 노이즈 생성기(Box-Muller 변환).
    /// </summary>
    /// <remarks>
    /// 시드를 고정하면 항상 같은 수열이 나오므로 단위테스트가 재현 가능하다.
    /// 실측 노이즈를 모사해 이동평균 창 크기(<c>FilterWindowSize</c>)를
    /// 하드웨어 없이 결정할 수 있게 하는 것이 목적이다.
    /// </remarks>
    public sealed class GaussianNoise
    {
        private readonly Random _random;
        private double _spare;
        private bool _hasSpare;

        /// <summary>노이즈 생성기를 생성한다.</summary>
        /// <param name="seed">난수 시드. 같은 시드는 같은 수열을 만든다.</param>
        public GaussianNoise(int seed)
        {
            _random = new Random(seed);
        }

        /// <summary>평균 0, 표준편차 1 인 정규분포 표본을 반환한다.</summary>
        /// <returns>표준정규 표본.</returns>
        public double NextStandard()
        {
            // Box-Muller 는 한 번에 두 개의 독립 표본을 만든다. 하나는 보관해 재사용한다.
            if (_hasSpare)
            {
                _hasSpare = false;
                return _spare;
            }

            double u1;
            double u2;

            do
            {
                u1 = _random.NextDouble();
            }
            while (u1 <= double.Epsilon); // log(0) 방지

            u2 = _random.NextDouble();

            double magnitude = Math.Sqrt(-2.0 * Math.Log(u1));
            double angle = 2.0 * Math.PI * u2;

            _spare = magnitude * Math.Sin(angle);
            _hasSpare = true;

            return magnitude * Math.Cos(angle);
        }

        /// <summary>지정 표준편차의 정규분포 표본을 반환한다.</summary>
        /// <param name="sigma">표준편차. 0 이면 항상 0 을 반환한다.</param>
        /// <returns>노이즈 값.</returns>
        public double Next(double sigma)
        {
            if (sigma <= 0.0)
            {
                return 0.0;
            }

            return NextStandard() * sigma;
        }
    }
}
