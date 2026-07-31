using System.Collections.Generic;
using System.Collections.ObjectModel;
using Esam.Domain.Control;

namespace Esam.Domain.Models
{
    /// <summary>
    /// 제어 엔진의 현재 상태 요약. 스냅샷에 포함되어 UI 상단바와 Operate 화면에 표시된다.
    /// </summary>
    public sealed class ControlStatus
    {
        private static readonly ReadOnlyCollection<ChainStatus> EmptyChains =
            new ReadOnlyCollection<ChainStatus>(new ChainStatus[0]);

        /// <summary>모든 값이 초기 상태인 인스턴스.</summary>
        public static readonly ControlStatus Initial;

        /// <summary>
        /// 정적 생성자. <see cref="Initial"/> 이 <c>EmptyChains</c> 를 사용하므로
        /// 필드 선언 순서에 의존하지 않도록 여기서 명시적으로 초기화한다.
        /// </summary>
        static ControlStatus()
        {
            Initial = new ControlStatus(SystemPhase.Idle, SensorMode.Sensor2, false, null, null);
        }

        /// <summary>시스템 운전 단계.</summary>
        public SystemPhase Phase { get; private set; }

        /// <summary>적용 중인 센서 모드.</summary>
        public SensorMode Mode { get; private set; }

        /// <summary>자동 제어 활성 여부.</summary>
        public bool IsAutoEnabled { get; private set; }

        /// <summary>체인별 제어 결과(체인 번호 오름차순).</summary>
        public IReadOnlyList<ChainStatus> Chains { get; private set; }

        /// <summary>발동 중인 인터록 ID 목록. 비어 있으면 인터록 없음.</summary>
        public IReadOnlyList<string> ActiveInterlockIds { get; private set; }

        /// <summary>인터록이 하나라도 발동 중인지 여부.</summary>
        public bool HasActiveInterlock
        {
            get { return ActiveInterlockIds.Count > 0; }
        }

        /// <summary>제어 상태 요약을 생성한다.</summary>
        /// <param name="phase">시스템 운전 단계.</param>
        /// <param name="mode">적용 중인 센서 모드.</param>
        /// <param name="isAutoEnabled">자동 제어 활성 여부.</param>
        /// <param name="chains">체인별 제어 결과(null 허용).</param>
        /// <param name="activeInterlockIds">발동 중인 인터록 ID 목록(null 허용).</param>
        public ControlStatus(
            SystemPhase phase,
            SensorMode mode,
            bool isAutoEnabled,
            IList<ChainStatus> chains,
            IList<string> activeInterlockIds)
        {
            Phase = phase;
            Mode = mode;
            IsAutoEnabled = isAutoEnabled;

            Chains = chains == null
                ? (IReadOnlyList<ChainStatus>)EmptyChains
                : new ReadOnlyCollection<ChainStatus>(new List<ChainStatus>(chains));

            ActiveInterlockIds = activeInterlockIds == null
                ? (IReadOnlyList<string>)new ReadOnlyCollection<string>(new string[0])
                : new ReadOnlyCollection<string>(new List<string>(activeInterlockIds));
        }
    }
}
