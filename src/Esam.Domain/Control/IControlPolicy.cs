namespace Esam.Domain.Control
{
    /// <summary>
    /// 압력 제어 알고리즘의 추상화.
    /// 1차 릴리스는 <see cref="BandControlPolicy"/>(ESAM 순서도)를 사용하고,
    /// 향후 PID 를 도입할 경우 이 인터페이스의 다른 구현으로 교체한다(DESIGN.md Open Issue #4).
    /// </summary>
    public interface IControlPolicy
    {
        /// <summary>알고리즘 이름. 로그와 HMI 표시용.</summary>
        string Name { get; }

        /// <summary>1회 제어 스텝을 수행하고 액추에이터 지령을 산출한다.</summary>
        /// <param name="context">제어에 필요한 입력 일체.</param>
        /// <returns>판정 결과와 생성된 지령.</returns>
        ControlDecision Step(ChainControlContext context);
    }
}
