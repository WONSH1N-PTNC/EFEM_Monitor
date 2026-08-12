using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Esam.Domain.Alarms
{
    /// <summary>
    /// 규칙 목록 교체 결과.
    /// </summary>
    /// <remarks>
    /// 무엇이 달라졌는지 <b>화면에 적기 위해</b> 있다. 조용히 교체하면
    /// "저장했더니 알람이 하나 사라졌다" 를 아무도 알아채지 못한다.
    /// </remarks>
    public sealed class AlarmRuleSwapResult
    {
        /// <summary>새로 생긴 코드.</summary>
        public IReadOnlyList<string> Added { get; private set; }

        /// <summary>없어진 코드.</summary>
        public IReadOnlyList<string> Removed { get; private set; }

        /// <summary>없어진 코드 중 <b>발생 중이던</b> 것.</summary>
        /// <remarks>
        /// 화면에 반드시 남겨야 한다. 떠 있던 알람이 규칙과 함께 사라진 것은
        /// 조건이 해소된 것과 완전히 다른 사건이다.
        /// </remarks>
        public IReadOnlyList<string> DroppedActive { get; private set; }

        /// <summary>발생 상태를 승계한 규칙 수.</summary>
        public int Carried { get; private set; }

        /// <summary>구성이 실제로 달라졌는지 여부.</summary>
        public bool HasStructuralChange
        {
            get { return Added.Count > 0 || Removed.Count > 0; }
        }

        /// <summary>교체 결과를 만든다.</summary>
        /// <param name="added">새로 생긴 코드.</param>
        /// <param name="removed">없어진 코드.</param>
        /// <param name="droppedActive">없어진 코드 중 발생 중이던 것.</param>
        /// <param name="carried">발생 상태를 승계한 규칙 수.</param>
        public AlarmRuleSwapResult(
            IList<string> added, IList<string> removed, IList<string> droppedActive, int carried)
        {
            Added = Freeze(added);
            Removed = Freeze(removed);
            DroppedActive = Freeze(droppedActive);
            Carried = carried;
        }

        /// <summary>목록을 읽기전용 사본으로 만든다.</summary>
        /// <param name="source">원본(null 허용).</param>
        /// <returns>읽기전용 목록.</returns>
        private static IReadOnlyList<string> Freeze(IList<string> source)
        {
            return new ReadOnlyCollection<string>(
                source == null ? new List<string>() : new List<string>(source));
        }
    }
}
