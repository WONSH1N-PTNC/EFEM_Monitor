using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using Esam.Communication.Abstractions;
using Esam.Communication.Configuration;
using Esam.Domain.Models;

namespace Esam.Communication.Polling
{
    /// <summary>
    /// 디코딩된 측정점 1건. 통신 계층이 상위로 올려보내는 최소 단위이다.
    /// </summary>
    public sealed class PointSample
    {
        /// <summary>디바이스 ID(예: "S1-1").</summary>
        public string DeviceId { get; private set; }

        /// <summary>측정점 키(예: "pressurePa").</summary>
        public string Key { get; private set; }

        /// <summary>스케일·바이어스·영점 오프셋이 모두 적용된 최종 공학값.</summary>
        public double Value { get; private set; }

        /// <summary>영점 오프셋 적용 전 값. Phase 5 튜닝 시 원시값 비교에 사용한다.</summary>
        public double RawValue { get; private set; }

        /// <summary>측정값 신뢰도.</summary>
        public Quality Quality { get; private set; }

        /// <summary>단위 표기(표시용).</summary>
        public string Unit { get; private set; }

        /// <summary>수집 시각(UTC).</summary>
        public DateTime TimestampUtc { get; private set; }

        /// <summary>"디바이스ID.키" 형식의 전체 경로. 알람 규칙의 Source 와 맞춘다.</summary>
        public string Path
        {
            get { return string.Concat(DeviceId, ".", Key); }
        }

        /// <summary>논리값 해석(0 이 아니면 true). Bool 타입 측정점에 사용한다.</summary>
        public bool AsBoolean
        {
            get { return Value != 0.0; }
        }

        /// <summary>측정점 표본을 생성한다.</summary>
        /// <param name="deviceId">디바이스 ID.</param>
        /// <param name="key">측정점 키.</param>
        /// <param name="value">최종 공학값.</param>
        /// <param name="rawValue">영점 오프셋 적용 전 값.</param>
        /// <param name="quality">신뢰도.</param>
        /// <param name="unit">단위 표기.</param>
        /// <param name="timestampUtc">수집 시각(UTC).</param>
        public PointSample(
            string deviceId,
            string key,
            double value,
            double rawValue,
            Quality quality,
            string unit,
            DateTime timestampUtc)
        {
            DeviceId = deviceId;
            Key = key;
            Value = value;
            RawValue = rawValue;
            Quality = quality;
            Unit = unit;
            TimestampUtc = timestampUtc;
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "{0} = {1:F3} {2} ({3})", Path, Value, Unit ?? string.Empty, Quality);
        }
    }

    /// <summary>
    /// 읽기 그룹 1건의 수집 결과.
    /// </summary>
    public sealed class GroupReadResult
    {
        private static readonly ReadOnlyCollection<PointSample> NoSamples =
            new ReadOnlyCollection<PointSample>(new PointSample[0]);

        /// <summary>디바이스 ID.</summary>
        public string DeviceId { get; private set; }

        /// <summary>읽기 그룹 이름.</summary>
        public string GroupName { get; private set; }

        /// <summary>폴링 티어.</summary>
        public PollingTier Tier { get; private set; }

        /// <summary>트랜잭션 성공 여부.</summary>
        public bool IsSuccess { get; private set; }

        /// <summary>실패 원인.</summary>
        public ModbusFailureKind FailureKind { get; private set; }

        /// <summary>실패 상세 설명.</summary>
        public string FailureDetail { get; private set; }

        /// <summary>트랜잭션 소요 시간 [ms].</summary>
        public double ElapsedMs { get; private set; }

        /// <summary>이 그룹에서 디코딩된 측정점 목록. 실패 시 빈 컬렉션.</summary>
        public IReadOnlyList<PointSample> Samples { get; private set; }

        /// <summary>수집 결과를 생성한다.</summary>
        /// <param name="deviceId">디바이스 ID.</param>
        /// <param name="groupName">그룹 이름.</param>
        /// <param name="tier">폴링 티어.</param>
        /// <param name="isSuccess">성공 여부.</param>
        /// <param name="failureKind">실패 원인.</param>
        /// <param name="failureDetail">실패 상세.</param>
        /// <param name="elapsedMs">소요 시간 [ms].</param>
        /// <param name="samples">측정점 목록(null 허용).</param>
        public GroupReadResult(
            string deviceId,
            string groupName,
            PollingTier tier,
            bool isSuccess,
            ModbusFailureKind failureKind,
            string failureDetail,
            double elapsedMs,
            IList<PointSample> samples)
        {
            DeviceId = deviceId;
            GroupName = groupName;
            Tier = tier;
            IsSuccess = isSuccess;
            FailureKind = failureKind;
            FailureDetail = failureDetail;
            ElapsedMs = elapsedMs;

            Samples = samples == null || samples.Count == 0
                ? (IReadOnlyList<PointSample>)NoSamples
                : new ReadOnlyCollection<PointSample>(new List<PointSample>(samples));
        }
    }

    /// <summary>
    /// 폴링 사이클 1회의 결과. <c>ModbusPortWorker.PollCompleted</c> 이벤트로 전달된다.
    /// </summary>
    /// <remarks>
    /// 워커는 스냅샷을 조립하지 않고 이 집합만 발행한다.
    /// <see cref="SystemSnapshot"/> 조립은 상위 DataStore(S4)의 책임이며,
    /// 이렇게 분리하면 워커를 단독으로 테스트할 수 있고
    /// 로깅·진단 같은 다른 구독자를 나중에 붙이기도 쉽다.
    /// </remarks>
    public sealed class PollCompletedEventArgs : EventArgs
    {
        /// <summary>포트 ID.</summary>
        public string PortId { get; private set; }

        /// <summary>사이클 시작 시각(UTC).</summary>
        public DateTime StartedUtc { get; private set; }

        /// <summary>
        /// 이 사이클에서 실제로 소요된 시간 [ms].
        /// DESIGN.md 2.2 (B) 폴링 예산의 <b>실측값</b>이며, 100ms 목표 달성 여부 판정 근거다.
        /// </summary>
        public double CycleMs { get; private set; }

        /// <summary>이 사이클에서 읽은 티어 조합.</summary>
        public IReadOnlyList<PollingTier> TiersPolled { get; private set; }

        /// <summary>그룹별 수집 결과.</summary>
        public IReadOnlyList<GroupReadResult> Results { get; private set; }

        /// <summary>이 사이클에서 성공한 트랜잭션 수.</summary>
        public int SuccessCount { get; private set; }

        /// <summary>이 사이클에서 실패한 트랜잭션 수.</summary>
        public int FailureCount { get; private set; }

        /// <summary>폴링 결과를 생성한다.</summary>
        /// <param name="portId">포트 ID.</param>
        /// <param name="startedUtc">사이클 시작 시각(UTC).</param>
        /// <param name="cycleMs">사이클 소요 시간 [ms].</param>
        /// <param name="tiersPolled">읽은 티어 목록.</param>
        /// <param name="results">그룹별 결과.</param>
        public PollCompletedEventArgs(
            string portId,
            DateTime startedUtc,
            double cycleMs,
            IList<PollingTier> tiersPolled,
            IList<GroupReadResult> results)
        {
            PortId = portId;
            StartedUtc = startedUtc;
            CycleMs = cycleMs;

            TiersPolled = tiersPolled == null
                ? (IReadOnlyList<PollingTier>)new ReadOnlyCollection<PollingTier>(new PollingTier[0])
                : new ReadOnlyCollection<PollingTier>(new List<PollingTier>(tiersPolled));

            List<GroupReadResult> copied = results == null
                ? new List<GroupReadResult>()
                : new List<GroupReadResult>(results);

            Results = new ReadOnlyCollection<GroupReadResult>(copied);

            int success = 0;
            foreach (GroupReadResult result in copied)
            {
                if (result.IsSuccess)
                {
                    success++;
                }
            }

            SuccessCount = success;
            FailureCount = copied.Count - success;
        }

        /// <summary>모든 측정점을 경로별로 펼친 딕셔너리를 만든다.</summary>
        /// <returns>"디바이스ID.키" → 표본 딕셔너리.</returns>
        public IDictionary<string, PointSample> ToPointMap()
        {
            Dictionary<string, PointSample> map =
                new Dictionary<string, PointSample>(StringComparer.OrdinalIgnoreCase);

            foreach (GroupReadResult result in Results)
            {
                foreach (PointSample sample in result.Samples)
                {
                    map[sample.Path] = sample;
                }
            }

            return map;
        }
    }
}
