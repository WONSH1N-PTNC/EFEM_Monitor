using System;
using Esam.Domain.Configuration;

namespace Esam.Domain.Control
{
    /// <summary>
    /// 체인 1조의 제어 루프 간 유지 상태(가변). 스냅샷과 달리 제어 엔진이 소유하며 매 스텝 갱신된다.
    /// </summary>
    /// <remarks>
    /// 대역 이탈 누적시간과 액추에이터 Dwell 은 "이전 스텝을 기억"해야 하므로 불변 스냅샷에 둘 수 없다.
    /// 제어 엔진 스레드 1개만 접근하므로 락은 사용하지 않는다.
    /// </remarks>
    public sealed class ChainRuntime
    {
        private DateTime _deviationSinceUtc;
        private DateTime _lastValveActionUtc;
        private DateTime _lastFanActionUtc;

        /// <summary>이 런타임이 담당하는 체인 정의.</summary>
        public ChainDefinition Definition { get; private set; }

        /// <summary>직전 스텝의 판정 결과.</summary>
        public ControlResult LastResult { get; private set; }

        /// <summary>대역 이탈이 연속된 시간 [ms]. 대역 복귀 시 0 으로 초기화된다.</summary>
        public double DeviationElapsedMs { get; private set; }

        /// <summary>체인 런타임을 생성한다.</summary>
        /// <param name="definition">체인 정의.</param>
        /// <exception cref="ArgumentNullException">정의가 null 일 때.</exception>
        public ChainRuntime(ChainDefinition definition)
        {
            if (definition == null)
            {
                throw new ArgumentNullException("definition");
            }

            Definition = definition;
            LastResult = ControlResult.Skipped;
            _deviationSinceUtc = DateTime.MinValue;
            _lastValveActionUtc = DateTime.MinValue;
            _lastFanActionUtc = DateTime.MinValue;
        }

        /// <summary>대역 이탈 누적을 시작하거나 계속한다.</summary>
        /// <param name="nowUtc">현재 시각(UTC).</param>
        public void MarkDeviating(DateTime nowUtc)
        {
            if (_deviationSinceUtc == DateTime.MinValue)
            {
                _deviationSinceUtc = nowUtc;
                DeviationElapsedMs = 0.0;
                return;
            }

            DeviationElapsedMs = (nowUtc - _deviationSinceUtc).TotalMilliseconds;
        }

        /// <summary>대역에 복귀했음을 기록하고 이탈 누적을 초기화한다.</summary>
        public void ClearDeviation()
        {
            _deviationSinceUtc = DateTime.MinValue;
            DeviationElapsedMs = 0.0;
        }

        /// <summary>대역 이탈이 설정된 확정 시간을 초과했는지 판정한다.</summary>
        /// <param name="mode">적용 중인 모드 설정.</param>
        /// <returns>확정 시간 초과이면 true.</returns>
        public bool IsDeviationConfirmed(ModeSetting mode)
        {
            // TimeMs 가 0 이면 디바운스 없이 즉시 확정한다(설정으로 비활성화 가능).
            return DeviationElapsedMs >= mode.TimeMs;
        }

        /// <summary>밸브를 다시 조작해도 되는 시점인지(Dwell 경과) 판정한다.</summary>
        /// <param name="nowUtc">현재 시각(UTC).</param>
        /// <param name="dwellMs">안정화 대기 시간 [ms].</param>
        /// <returns>조작 가능하면 true.</returns>
        public bool CanActuateValve(DateTime nowUtc, int dwellMs)
        {
            if (_lastValveActionUtc == DateTime.MinValue)
            {
                return true;
            }

            return (nowUtc - _lastValveActionUtc).TotalMilliseconds >= dwellMs;
        }

        /// <summary>팬을 다시 조작해도 되는 시점인지(Dwell 경과) 판정한다.</summary>
        /// <param name="nowUtc">현재 시각(UTC).</param>
        /// <param name="dwellMs">안정화 대기 시간 [ms].</param>
        /// <returns>조작 가능하면 true.</returns>
        public bool CanActuateFan(DateTime nowUtc, int dwellMs)
        {
            if (_lastFanActionUtc == DateTime.MinValue)
            {
                return true;
            }

            return (nowUtc - _lastFanActionUtc).TotalMilliseconds >= dwellMs;
        }

        /// <summary>밸브를 조작했음을 기록한다.</summary>
        /// <param name="nowUtc">조작 시각(UTC).</param>
        public void MarkValveActuated(DateTime nowUtc)
        {
            _lastValveActionUtc = nowUtc;
        }

        /// <summary>팬을 조작했음을 기록한다.</summary>
        /// <param name="nowUtc">조작 시각(UTC).</param>
        public void MarkFanActuated(DateTime nowUtc)
        {
            _lastFanActionUtc = nowUtc;
        }

        /// <summary>직전 판정 결과를 기록한다.</summary>
        /// <param name="result">판정 결과.</param>
        public void SetResult(ControlResult result)
        {
            LastResult = result;
        }

        /// <summary>모든 유지 상태를 초기화한다. 자동 제어 시작/중지 시 호출한다.</summary>
        public void Reset()
        {
            LastResult = ControlResult.Skipped;
            _deviationSinceUtc = DateTime.MinValue;
            _lastValveActionUtc = DateTime.MinValue;
            _lastFanActionUtc = DateTime.MinValue;
            DeviationElapsedMs = 0.0;
        }
    }
}
