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

        /// <summary>
        /// 이 체인에 마지막으로 지령한 팬 회전수 [RPM]. 아직 지령한 적이 없으면 null.
        /// </summary>
        /// <remarks>
        /// <para><b>제어기의 적분 상태이므로 측정값이 아니라 지령값을 유지해야 한다.</b>
        /// 밴드 제어는 "현재값 + StepRpm" 으로 다음 지령을 만들고,
        /// "현재값 >= MaxRpm - Tolerance" 로 증속 여력 소진을 판정한다.</para>
        /// <para>여기에 측정 RPM 을 쓰면 두 가지가 깨진다.
        /// 첫째, 팬이 아직 목표까지 램프업하지 못한 사이에 다음 스텝이 뒤처진 값에서
        /// 계산되어 증속이 느려진다. 둘째, <b>부하 때문에 팬이 MaxRpm 에 물리적으로
        /// 도달하지 못하면 포화 판정이 영영 성립하지 않는다.</b> 밸브가 이미 포화된 뒤
        /// 팬이 마지막 대응 수단인 구조에서, 제어 권한을 다 쓴 상태가 보고되지 않는 것은
        /// "대응 수단이 없는데 화면은 정상"인 위험한 불일치다.</para>
        /// <para>드라이버가 지령을 거부하거나 클램프하는 경우는 별도로 설정값
        /// 레지스터를 되읽어 대조한다. 그것은 진단 경로이지 제어 경로가 아니다.</para>
        /// </remarks>
        public double? LastFanCommandRpm { get; private set; }

        /// <summary>팬을 조작했음을 기록한다.</summary>
        /// <param name="nowUtc">조작 시각(UTC).</param>
        /// <param name="commandedRpm">이번에 지령한 회전수 [RPM]. 지령을 내지 않았으면 null.</param>
        public void MarkFanActuated(DateTime nowUtc, double? commandedRpm)
        {
            _lastFanActionUtc = nowUtc;

            if (commandedRpm.HasValue)
            {
                LastFanCommandRpm = commandedRpm;
            }
        }

        /// <summary>
        /// 팬 지령 이력을 실제 설정값으로 맞춘다. 자동 운전 진입 시 1회 호출한다.
        /// </summary>
        /// <param name="setpointRpm">드라이버에서 되읽은 설정값 [RPM].</param>
        /// <remarks>
        /// 수동 운전이나 이전 세션에서 팬이 이미 돌고 있을 수 있다.
        /// 이력을 비운 채 자동에 진입하면 현재 설정값을 무시하고 처음부터 증속하게 된다.
        /// </remarks>
        public void SeedFanCommand(double setpointRpm)
        {
            if (setpointRpm > 0.0)
            {
                LastFanCommandRpm = setpointRpm;
            }
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

            // 지령 이력도 비운다. 자동 운전을 다시 시작할 때는
            // 드라이버에서 되읽은 설정값으로 SeedFanCommand 를 호출해 맞춘다.
            LastFanCommandRpm = null;
        }
    }
}
