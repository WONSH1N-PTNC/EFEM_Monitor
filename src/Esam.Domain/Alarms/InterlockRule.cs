using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Esam.Domain.Alarms
{
    /// <summary>
    /// 인터록 규칙 정의. interlocks.json 의 항목 1건에 대응한다.
    /// </summary>
    /// <remarks>
    /// 인터록은 알람과 달리 <b>디바운스 없이 즉시</b> 동작해야 하는 안전 기능이다.
    /// 따라서 조건 판정과 지령 생성이 폴링 스레드에서 수행된다(DESIGN.md 3.2 원칙 3).
    /// </remarks>
    public sealed class InterlockRule
    {
        /// <summary>인터록 식별자(예: "IL-01").</summary>
        public string Id { get; set; }

        /// <summary>표시명.</summary>
        public string Name { get; set; }

        /// <summary>동작 범위(해당 체인만 / 전 체인).</summary>
        public InterlockScope Scope { get; set; }

        /// <summary>이 규칙을 사용할지 여부. false 이면 판정하지 않는다.</summary>
        public bool Enabled { get; set; }

        /// <summary>해제 정책. Manual 이면 조건 해소 후에도 사용자 Reset 이 필요하다.</summary>
        public AlarmResetPolicy ResetPolicy { get; set; }

        /// <summary>
        /// 해제 시 적용할 히스테리시스 [Pa].
        /// 임계값 근처에서 인터록이 반복 발동/해제되는 채터링을 방지한다.
        /// </summary>
        public double ClearHysteresisPa { get; set; }

        /// <summary>기본값으로 초기화한다.</summary>
        public InterlockRule()
        {
            Enabled = true;
            Scope = InterlockScope.Chain;
            ResetPolicy = AlarmResetPolicy.Manual;
            ClearHysteresisPa = 0.0;
        }
    }

    /// <summary>
    /// 인터록 판정 결과 1건. 어떤 규칙이 어떤 대상에 대해 발동했는지를 나타낸다.
    /// </summary>
    public sealed class InterlockTrip
    {
        /// <summary>발동한 인터록 규칙 ID.</summary>
        public string RuleId { get; private set; }

        /// <summary>발동 사유 설명.</summary>
        public string Reason { get; private set; }

        /// <summary>영향을 받는 체인 번호 목록. 전체 정지이면 모든 체인이 포함된다.</summary>
        public IReadOnlyList<int> AffectedChainIds { get; private set; }

        /// <summary>발동 시각(UTC).</summary>
        public DateTime OccurredUtc { get; private set; }

        /// <summary>전 체인 정지 여부.</summary>
        public bool IsSystemWide { get; private set; }

        /// <summary>인터록 발동 결과를 생성한다.</summary>
        /// <param name="ruleId">규칙 ID.</param>
        /// <param name="reason">발동 사유.</param>
        /// <param name="affectedChainIds">영향 체인 목록.</param>
        /// <param name="isSystemWide">전 체인 정지 여부.</param>
        /// <param name="occurredUtc">발동 시각(UTC).</param>
        public InterlockTrip(
            string ruleId,
            string reason,
            IList<int> affectedChainIds,
            bool isSystemWide,
            DateTime occurredUtc)
        {
            RuleId = ruleId;
            Reason = reason;
            AffectedChainIds = affectedChainIds == null
                ? (IReadOnlyList<int>)new ReadOnlyCollection<int>(new int[0])
                : new ReadOnlyCollection<int>(new List<int>(affectedChainIds));
            IsSystemWide = isSystemWide;
            OccurredUtc = occurredUtc;
        }
    }
}
