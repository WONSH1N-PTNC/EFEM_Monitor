using System.Collections.Generic;
using System.Collections.ObjectModel;
using Esam.Domain.Alarms;

namespace Esam.Domain.Models
{
    /// <summary>
    /// 현재 활성 알람의 요약. UI 상단바의 알람 표시등과 팝업 배지에 사용한다.
    /// 상세 내역은 알람 엔진이 별도로 보관하며, 스냅샷에는 요약만 담아 크기를 줄인다.
    /// </summary>
    public sealed class AlarmSummary
    {
        /// <summary>활성 알람이 없는 상태.</summary>
        public static readonly AlarmSummary None = new AlarmSummary(null, false);

        /// <summary>활성 알람 코드 목록(심각도 내림차순).</summary>
        public IReadOnlyList<string> ActiveCodes { get; private set; }

        /// <summary>활성 알람 중 가장 높은 심각도.</summary>
        public AlarmSeverity HighestSeverity { get; private set; }

        /// <summary>활성 알람 개수.</summary>
        public int ActiveCount
        {
            get { return ActiveCodes.Count; }
        }

        /// <summary>미확인(Ack 되지 않은) 알람이 존재하는지 여부. UI 점멸 트리거.</summary>
        public bool HasUnacknowledged { get; private set; }

        /// <summary>알람 요약을 생성한다.</summary>
        /// <param name="activeCodes">활성 알람 코드 목록(null 허용).</param>
        /// <param name="hasUnacknowledged">미확인 알람 존재 여부.</param>
        /// <param name="highestSeverity">최고 심각도.</param>
        public AlarmSummary(
            IList<string> activeCodes,
            bool hasUnacknowledged,
            AlarmSeverity highestSeverity = AlarmSeverity.None)
        {
            ActiveCodes = activeCodes == null
                ? (IReadOnlyList<string>)new ReadOnlyCollection<string>(new string[0])
                : new ReadOnlyCollection<string>(new List<string>(activeCodes));

            HasUnacknowledged = hasUnacknowledged;
            HighestSeverity = highestSeverity;
        }
    }
}
