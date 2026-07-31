using System;

namespace Esam.Domain.Units
{
    /// <summary>
    /// 고정 창 크기 이동평균 필터. 차압센서의 수 Pa 단위 노이즈를 억제한다.
    /// </summary>
    /// <remarks>
    /// 링버퍼와 누적합을 사용해 창 크기와 무관하게 갱신 비용이 O(1) 이다.
    /// 200ms × 13채널 주기로 호출되므로 할당이 발생하지 않도록 설계했다.
    /// 이 클래스는 스레드 안전하지 않다. 채널별로 폴링 스레드 1개만 접근해야 한다.
    /// </remarks>
    public sealed class MovingAverageFilter
    {
        private readonly double[] _buffer;
        private int _writeIndex;
        private int _count;
        private double _sum;

        /// <summary>필터 창 크기.</summary>
        public int WindowSize
        {
            get { return _buffer.Length; }
        }

        /// <summary>현재까지 누적된 샘플 수(최대 <see cref="WindowSize"/>).</summary>
        public int SampleCount
        {
            get { return _count; }
        }

        /// <summary>창이 가득 찼는지 여부. 초기 과도구간 판정에 사용한다.</summary>
        public bool IsWarmedUp
        {
            get { return _count >= _buffer.Length; }
        }

        /// <summary>현재 평균값. 샘플이 없으면 0 을 반환한다.</summary>
        public double Average
        {
            get { return _count == 0 ? 0.0 : _sum / _count; }
        }

        /// <summary>이동평균 필터를 생성한다.</summary>
        /// <param name="windowSize">창 크기. 1 이면 필터링하지 않는 것과 같다.</param>
        /// <exception cref="ArgumentOutOfRangeException">창 크기가 1 미만일 때.</exception>
        public MovingAverageFilter(int windowSize)
        {
            if (windowSize < 1)
            {
                throw new ArgumentOutOfRangeException(
                    "windowSize", windowSize, "필터 창 크기는 1 이상이어야 합니다.");
            }

            _buffer = new double[windowSize];
        }

        /// <summary>새 샘플을 추가하고 갱신된 평균을 반환한다.</summary>
        /// <param name="sample">새 측정값.</param>
        /// <returns>갱신된 이동평균.</returns>
        public double Add(double sample)
        {
            if (double.IsNaN(sample) || double.IsInfinity(sample))
            {
                // 비정상 값은 창을 오염시키므로 무시하고 직전 평균을 유지한다.
                return Average;
            }

            if (_count == _buffer.Length)
            {
                // 창이 가득 찼으면 가장 오래된 값을 누적합에서 빼고 덮어쓴다.
                _sum -= _buffer[_writeIndex];
            }
            else
            {
                _count++;
            }

            _buffer[_writeIndex] = sample;
            _sum += sample;
            _writeIndex = (_writeIndex + 1) % _buffer.Length;

            // 부동소수 누적 오차가 장시간 운전 중 커지지 않도록 창이 한 바퀴 돌 때마다 재계산한다.
            if (_writeIndex == 0)
            {
                Recalculate();
            }

            return Average;
        }

        /// <summary>필터 상태를 초기화한다. 영점 교정 후 호출한다.</summary>
        public void Reset()
        {
            Array.Clear(_buffer, 0, _buffer.Length);
            _writeIndex = 0;
            _count = 0;
            _sum = 0.0;
        }

        /// <summary>누적합을 버퍼로부터 다시 계산해 부동소수 오차를 제거한다.</summary>
        private void Recalculate()
        {
            double total = 0.0;
            for (int i = 0; i < _count; i++)
            {
                total += _buffer[i];
            }

            _sum = total;
        }
    }
}
