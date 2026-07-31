namespace Esam.Domain.Models
{
    /// <summary>
    /// 측정값의 신뢰도. 제어 로직은 <see cref="Good"/> 이 아닌 값으로 액추에이터를 움직이지 않는다.
    /// </summary>
    public enum Quality
    {
        /// <summary>아직 한 번도 수집되지 않음(초기 상태).</summary>
        NoData = 0,

        /// <summary>정상 수집값.</summary>
        Good = 1,

        /// <summary>수집은 되었으나 센서 범위 초과 등으로 의심스러운 값.</summary>
        Uncertain = 2,

        /// <summary>갱신 주기를 초과해 낡은 값(통신 지연).</summary>
        Stale = 3,

        /// <summary>통신 실패 또는 장치 에러로 사용 불가.</summary>
        Bad = 4
    }
}
