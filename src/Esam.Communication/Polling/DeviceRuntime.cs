using System;
using System.Collections.Generic;
using System.Globalization;
using Esam.Communication.Abstractions;
using Esam.Communication.Configuration;
using Esam.Domain.Models;
using Esam.Domain.Units;

namespace Esam.Communication.Polling
{
    /// <summary>
    /// 미리 조립해 둔 읽기 그룹. 폴링 루프가 매 사이클 재사용한다.
    /// </summary>
    public sealed class PreparedReadGroup
    {
        /// <summary>그룹 정의.</summary>
        public ReadGroupDefinition Definition { get; private set; }

        /// <summary>이 그룹의 Modbus 요청(사이클마다 재사용).</summary>
        public ModbusRequest Request { get; private set; }

        /// <summary>이 그룹을 마지막으로 읽은 시각(UTC).</summary>
        public DateTime LastPolledUtc { get; internal set; }

        /// <summary>연속 실패 횟수. 통신 품질 판정과 알람 P00 에 사용한다.</summary>
        public int ConsecutiveFailures { get; internal set; }

        /// <summary>미리 조립한 읽기 그룹을 생성한다.</summary>
        /// <param name="definition">그룹 정의.</param>
        /// <param name="request">조립된 요청.</param>
        public PreparedReadGroup(ReadGroupDefinition definition, ModbusRequest request)
        {
            Definition = definition;
            Request = request;
            LastPolledUtc = DateTime.MinValue;
            ConsecutiveFailures = 0;
        }
    }

    /// <summary>
    /// 디바이스 1대의 런타임 상태. 설정을 실행 가능한 형태로 미리 변환해 보관한다.
    /// </summary>
    /// <remarks>
    /// <para>매 사이클 <see cref="ModbusRequest"/> 를 새로 만들면 초당 수백 건의 할당이 발생해
    /// GC 압력이 커진다. 요청은 불변이므로 생성 시점에 한 번만 조립해 재사용한다.</para>
    /// <para>주소가 미확정(TBD)인 그룹은 조립 대상에서 제외되므로,
    /// 레지스터 명세가 확보되지 않은 장치가 섞여 있어도 나머지는 정상 폴링된다.</para>
    /// </remarks>
    public sealed class DeviceRuntime
    {
        private readonly List<PreparedReadGroup> _groups = new List<PreparedReadGroup>();
        private readonly Dictionary<string, MovingAverageFilter> _filters;
        private readonly List<string> _skippedGroups = new List<string>();

        /// <summary>디바이스 인스턴스 정의.</summary>
        public DeviceInstanceDefinition Instance { get; private set; }

        /// <summary>디바이스 종류 명세.</summary>
        public DeviceTypeDefinition Type { get; private set; }

        /// <summary>디바이스 ID.</summary>
        public string DeviceId
        {
            get { return Instance.Id; }
        }

        /// <summary>슬레이브 주소.</summary>
        public byte SlaveId
        {
            get { return Instance.SlaveId; }
        }

        /// <summary>폴링 가능한 읽기 그룹 목록.</summary>
        public IList<PreparedReadGroup> ReadGroups
        {
            get { return _groups; }
        }

        /// <summary>주소 미확정으로 제외된 그룹 이름 목록. 진단 화면 표시용.</summary>
        public IList<string> SkippedGroups
        {
            get { return _skippedGroups; }
        }

        /// <summary>현재 적용 중인 영점 오프셋.</summary>
        public double ZeroOffset { get; private set; }

        /// <summary>디바이스 런타임을 생성한다.</summary>
        /// <param name="instance">디바이스 인스턴스 정의.</param>
        /// <param name="type">디바이스 종류 명세.</param>
        /// <exception cref="ArgumentNullException">인자가 null 일 때.</exception>
        public DeviceRuntime(DeviceInstanceDefinition instance, DeviceTypeDefinition type)
        {
            if (instance == null)
            {
                throw new ArgumentNullException("instance");
            }

            if (type == null)
            {
                throw new ArgumentNullException("type");
            }

            Instance = instance;
            Type = type;
            ZeroOffset = instance.Offset;

            _filters = new Dictionary<string, MovingAverageFilter>(StringComparer.OrdinalIgnoreCase);

            PrepareGroups();
        }

        /// <summary>읽기 그룹을 미리 조립한다.</summary>
        private void PrepareGroups()
        {
            if (Type.ReadGroups == null)
            {
                return;
            }

            foreach (ReadGroupDefinition group in Type.ReadGroups)
            {
                if (group == null)
                {
                    continue;
                }

                ushort startAddress;
                ModbusFunctionCode functionCode;

                // 개수가 규격 범위를 벗어나면 ModbusRequest 가 예외를 던진다.
                // 검증되지 않은 설정으로 생성자에서 터지는 대신 그룹을 건너뛰고 보고한다.
                bool countValid = group.Count >= 1 && group.Count <= 125;

                if (!countValid
                    || !RegisterAddress.TryParse(group.StartAddress, out startAddress)
                    || !group.TryGetFunctionCode(out functionCode))
                {
                    // 주소·함수코드·개수 중 하나라도 유효하지 않으면 폴링하지 않는다.
                    _skippedGroups.Add(group.Name);
                    continue;
                }

                ModbusRequest request = functionCode == ModbusFunctionCode.ReadInputRegisters
                    ? ModbusRequest.ReadInput(SlaveId, startAddress, (ushort)group.Count)
                    : ModbusRequest.ReadHolding(SlaveId, startAddress, (ushort)group.Count);

                _groups.Add(new PreparedReadGroup(group, request));

                // 필터는 측정점 단위로 유지한다. 창 크기 1 이면 필터를 만들지 않는다.
                if (Instance.FilterWindowSize <= 1 || group.Points == null)
                {
                    continue;
                }

                foreach (PointDefinition point in group.Points)
                {
                    if (point == null || string.IsNullOrEmpty(point.Key)
                        || point.Type == PointDataType.Bool || !point.ApplyCalibration)
                    {
                        // 논리값과 상태·알람 코드에는 이동평균이 의미가 없다.
                        // 필터는 ApplyCalibration 이 지정된 주 계측값에만 만든다.
                        continue;
                    }

                    _filters[point.Key] = new MovingAverageFilter(Instance.FilterWindowSize);
                }
            }
        }

        /// <summary>
        /// 트랜잭션 응답을 디코딩해 측정점 표본으로 변환한다.
        /// </summary>
        /// <param name="group">대상 읽기 그룹.</param>
        /// <param name="response">트랜잭션 결과.</param>
        /// <param name="nowUtc">수집 시각(UTC).</param>
        /// <returns>그룹 수집 결과.</returns>
        public GroupReadResult Decode(
            PreparedReadGroup group, ModbusResponse response, DateTime nowUtc)
        {
            if (group == null)
            {
                throw new ArgumentNullException("group");
            }

            if (response == null)
            {
                throw new ArgumentNullException("response");
            }

            group.LastPolledUtc = nowUtc;

            if (!response.IsSuccess)
            {
                group.ConsecutiveFailures++;

                return new GroupReadResult(
                    DeviceId, group.Definition.Name, group.Definition.Tier,
                    false, response.FailureKind, response.FailureDetail,
                    response.ElapsedMs, null);
            }

            group.ConsecutiveFailures = 0;

            List<PointSample> samples = new List<PointSample>(
                group.Definition.Points == null ? 0 : group.Definition.Points.Count);

            if (group.Definition.Points != null)
            {
                foreach (PointDefinition point in group.Definition.Points)
                {
                    if (point == null)
                    {
                        continue;
                    }

                    double decoded;
                    if (!PointDecoder.TryDecode(point, response.Registers, out decoded))
                    {
                        // 응답 길이가 선언보다 짧은 경우. 값을 추측하지 않고 Bad 로 표시한다.
                        samples.Add(new PointSample(
                            DeviceId, point.Key, 0.0, 0.0, Quality.Bad, point.Unit, nowUtc));
                        continue;
                    }

                    samples.Add(BuildSample(point, decoded, nowUtc));
                }
            }

            return new GroupReadResult(
                DeviceId, group.Definition.Name, group.Definition.Tier,
                true, ModbusFailureKind.None, null, response.ElapsedMs, samples);
        }

        /// <summary>필터와 영점 오프셋, 레인지 검증을 적용해 표본을 만든다.</summary>
        /// <param name="point">측정점 정의.</param>
        /// <param name="decoded">디코딩된 값.</param>
        /// <param name="nowUtc">수집 시각(UTC).</param>
        /// <returns>측정점 표본.</returns>
        private PointSample BuildSample(PointDefinition point, double decoded, DateTime nowUtc)
        {
            // 논리값과 상태·알람 코드는 원시값 그대로 올린다.
            // 영점 오프셋이나 이동평균을 적용하면 값의 의미가 깨진다.
            // (예: 오프셋 20 을 교정한 뒤 deviceStatus 가 -20 으로 보이는 문제)
            if (point.Type == PointDataType.Bool || !point.ApplyCalibration)
            {
                return new PointSample(
                    DeviceId, point.Key, decoded, decoded, Quality.Good, point.Unit, nowUtc);
            }

            double filtered = decoded;

            MovingAverageFilter filter;
            if (_filters.TryGetValue(point.Key, out filter))
            {
                filtered = filter.Add(decoded);
            }

            double finalValue = filtered - ZeroOffset;

            // 센서 레인지를 벗어난 값은 배선 오류나 센서 고장을 뜻한다.
            // 값을 버리지 않고 Uncertain 으로 표시해, 제어는 사용하지 않되 로그에는 남긴다.
            Quality quality = Instance.IsWithinRange(decoded) ? Quality.Good : Quality.Uncertain;

            return new PointSample(
                DeviceId, point.Key, finalValue, decoded, quality, point.Unit, nowUtc);
        }

        /// <summary>
        /// 영점 오프셋을 갱신한다. Maintenance 화면의 영점 교정에서 호출한다.
        /// </summary>
        /// <param name="offset">새 오프셋.</param>
        public void SetZeroOffset(double offset)
        {
            ZeroOffset = offset;
            Instance.Offset = offset;

            // 오프셋이 바뀌면 필터에 남은 과거 값이 새 기준과 섞이므로 초기화한다.
            foreach (KeyValuePair<string, MovingAverageFilter> pair in _filters)
            {
                pair.Value.Reset();
            }
        }

        /// <summary>이 디바이스가 통신 상실 상태인지 판정한다.</summary>
        /// <param name="threshold">연속 실패 허용 횟수.</param>
        /// <returns>어느 그룹이든 임계치를 넘겼으면 true.</returns>
        public bool IsCommunicationLost(int threshold)
        {
            foreach (PreparedReadGroup group in _groups)
            {
                if (group.ConsecutiveFailures >= threshold)
                {
                    return true;
                }
            }

            return false;
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "{0} ({1}) slave {2} on {3} — 그룹 {4}개, 제외 {5}개",
                DeviceId, Instance.Type, SlaveId, Instance.Port, _groups.Count, _skippedGroups.Count);
        }
    }
}
