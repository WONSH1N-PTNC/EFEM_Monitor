using Esam.Domain.Control;

namespace Esam.Domain.Models
{
    /// <summary>
    /// 체인 1조(차압센서 + 스로틀밸브 + 송풍팬)의 제어 결과 요약.
    /// HMI Operate 화면의 열 1개에 대응한다.
    /// </summary>
    public sealed class ChainStatus
    {
        /// <summary>체인 번호(1~5).</summary>
        public int ChainId { get; private set; }

        /// <summary>체인 표시명(예: "Chain 2-1").</summary>
        public string Name { get; private set; }

        /// <summary>직전 제어 스텝의 판정 결과.</summary>
        public ControlResult Result { get; private set; }

        /// <summary>제어에 사용한 실제 측정값 [Pa].</summary>
        public double ProcessValuePa { get; private set; }

        /// <summary>적용 중인 목표값 [Pa].</summary>
        public double SetpointPa { get; private set; }

        /// <summary>정상 대역 하한 [Pa].</summary>
        public double LowLimitPa { get; private set; }

        /// <summary>정상 대역 상한 [Pa].</summary>
        public double HighLimitPa { get; private set; }

        /// <summary>대역 이탈이 연속된 시간 [ms]. 설정된 Time 을 넘으면 에러로 확정된다.</summary>
        public double DeviationElapsedMs { get; private set; }

        /// <summary>제어 결과가 에러(더 이상 대응 불가)인지 여부.</summary>
        public bool IsError
        {
            get { return Result == ControlResult.ErrorLow || Result == ControlResult.ErrorHigh; }
        }

        /// <summary>체인 상태 요약을 생성한다.</summary>
        /// <param name="chainId">체인 번호.</param>
        /// <param name="name">체인 표시명.</param>
        /// <param name="result">제어 판정 결과.</param>
        /// <param name="processValuePa">측정값 [Pa].</param>
        /// <param name="setpointPa">목표값 [Pa].</param>
        /// <param name="lowLimitPa">대역 하한 [Pa].</param>
        /// <param name="highLimitPa">대역 상한 [Pa].</param>
        /// <param name="deviationElapsedMs">대역 이탈 지속시간 [ms].</param>
        public ChainStatus(
            int chainId,
            string name,
            ControlResult result,
            double processValuePa,
            double setpointPa,
            double lowLimitPa,
            double highLimitPa,
            double deviationElapsedMs)
        {
            ChainId = chainId;
            Name = name;
            Result = result;
            ProcessValuePa = processValuePa;
            SetpointPa = setpointPa;
            LowLimitPa = lowLimitPa;
            HighLimitPa = highLimitPa;
            DeviationElapsedMs = deviationElapsedMs;
        }
    }
}
