using System;

namespace Esam.Domain.Units
{
    /// <summary>
    /// 스로틀밸브의 pulse ↔ 개도율(%) ↔ 개도각(도) 변환기.
    /// 통신자료 「쓰로틀 밸브」 시트 기준으로 90도 = 5000 pulse 이다.
    /// </summary>
    public sealed class ValvePulseConverter
    {
        /// <summary>통신자료 기준 기본 변환기(5000 pulse = 90도).</summary>
        public static readonly ValvePulseConverter Default = new ValvePulseConverter(5000, 90.0);

        /// <summary>완전 열림(Full open) 위치의 pulse 값.</summary>
        public int PulsePerFullOpen { get; private set; }

        /// <summary>완전 열림 위치의 각도 [도].</summary>
        public double FullOpenDegree { get; private set; }

        /// <summary>변환기를 생성한다.</summary>
        /// <param name="pulsePerFullOpen">완전 열림 위치의 pulse 값(예: 5000).</param>
        /// <param name="fullOpenDegree">완전 열림 위치의 각도 [도] (예: 90).</param>
        /// <exception cref="ArgumentOutOfRangeException">인자가 0 이하일 때.</exception>
        public ValvePulseConverter(int pulsePerFullOpen, double fullOpenDegree)
        {
            if (pulsePerFullOpen <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    "pulsePerFullOpen", pulsePerFullOpen, "완전 열림 pulse 값은 0보다 커야 합니다.");
            }

            if (fullOpenDegree <= 0.0)
            {
                throw new ArgumentOutOfRangeException(
                    "fullOpenDegree", fullOpenDegree, "완전 열림 각도는 0보다 커야 합니다.");
            }

            PulsePerFullOpen = pulsePerFullOpen;
            FullOpenDegree = fullOpenDegree;
        }

        /// <summary>개도율 [%] 을 pulse 로 변환한다.</summary>
        /// <param name="percent">개도율 [%] (0~100). 범위를 벗어나면 클램프된다.</param>
        /// <returns>pulse 값.</returns>
        public int PercentToPulse(double percent)
        {
            double clamped = Clamp(percent, 0.0, 100.0);

            // 반올림 시 MidpointRounding.AwayFromZero 를 명시해 은행가 반올림(기본값)으로 인한
            // 예상치 못한 1 pulse 오차를 방지한다.
            return (int)Math.Round(clamped / 100.0 * PulsePerFullOpen, MidpointRounding.AwayFromZero);
        }

        /// <summary>pulse 를 개도율 [%] 로 변환한다.</summary>
        /// <param name="pulse">pulse 값.</param>
        /// <returns>개도율 [%].</returns>
        public double PulseToPercent(int pulse)
        {
            return pulse / (double)PulsePerFullOpen * 100.0;
        }

        /// <summary>pulse 를 개도각 [도] 으로 변환한다.</summary>
        /// <param name="pulse">pulse 값.</param>
        /// <returns>개도각 [도].</returns>
        public double PulseToDegree(int pulse)
        {
            return pulse / (double)PulsePerFullOpen * FullOpenDegree;
        }

        /// <summary>개도각 [도] 을 pulse 로 변환한다.</summary>
        /// <param name="degree">개도각 [도].</param>
        /// <returns>pulse 값.</returns>
        public int DegreeToPulse(double degree)
        {
            double clamped = Clamp(degree, 0.0, FullOpenDegree);
            return (int)Math.Round(clamped / FullOpenDegree * PulsePerFullOpen, MidpointRounding.AwayFromZero);
        }

        /// <summary>값을 지정 범위로 제한한다.</summary>
        /// <param name="value">입력값.</param>
        /// <param name="min">하한.</param>
        /// <param name="max">상한.</param>
        /// <returns>제한된 값.</returns>
        private static double Clamp(double value, double min, double max)
        {
            if (double.IsNaN(value))
            {
                return min;
            }

            if (value < min)
            {
                return min;
            }

            return value > max ? max : value;
        }
    }
}
