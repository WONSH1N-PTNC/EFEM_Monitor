using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Esam.Domain.Models
{
    /// <summary>
    /// 특정 시점의 시스템 전체 상태를 담은 불변 스냅샷.
    /// </summary>
    /// <remarks>
    /// DESIGN.md 3.2 스레딩 모델의 핵심 자료구조이다.
    /// 통신 스레드가 완성된 스냅샷을 통째로 발행하고, UI 와 제어 엔진은 이를 읽기만 한다.
    /// 부분 갱신이 없으므로 "센서 1은 갱신되고 센서 2는 이전 값"인 tearing 이 원천적으로 발생하지 않는다.
    /// </remarks>
    public sealed class SystemSnapshot
    {
        // 비어 있는 경우에도 채워진 경우와 동일한 비교자를 사용해 조회 동작을 일관되게 유지한다.
        private static readonly ReadOnlyDictionary<string, PressureReading> EmptyPressures =
            new ReadOnlyDictionary<string, PressureReading>(
                new Dictionary<string, PressureReading>(StringComparer.OrdinalIgnoreCase));

        private static readonly ReadOnlyDictionary<string, ValveState> EmptyValves =
            new ReadOnlyDictionary<string, ValveState>(
                new Dictionary<string, ValveState>(StringComparer.OrdinalIgnoreCase));

        private static readonly ReadOnlyDictionary<string, FanState> EmptyFans =
            new ReadOnlyDictionary<string, FanState>(
                new Dictionary<string, FanState>(StringComparer.OrdinalIgnoreCase));

        private static readonly ReadOnlyDictionary<string, DeviceHealth> EmptyDevices =
            new ReadOnlyDictionary<string, DeviceHealth>(
                new Dictionary<string, DeviceHealth>(StringComparer.OrdinalIgnoreCase));

        /// <summary>스냅샷 생성 시각(UTC).</summary>
        public DateTime TimestampUtc { get; private set; }

        /// <summary>차압센서 판독값. 키는 센서 ID("S1-1" 등).</summary>
        public IReadOnlyDictionary<string, PressureReading> Pressures { get; private set; }

        /// <summary>스로틀밸브 상태. 키는 밸브 ID("V-1" 등).</summary>
        public IReadOnlyDictionary<string, ValveState> Valves { get; private set; }

        /// <summary>송풍팬 상태. 키는 팬 ID("F-1" 등).</summary>
        public IReadOnlyDictionary<string, FanState> Fans { get; private set; }

        /// <summary>디바이스별 통신 건강 상태. 키는 디바이스 ID.</summary>
        /// <remarks>
        /// 값 모델(<see cref="Pressures"/> 등)과 달리 <b>폴링하지 않는 디바이스도 포함한다.</b>
        /// I/O Status 화면이 "구성에 없음" 과 "꺼 두었음" 을 구분해 표시하기 위한 것이다.
        /// </remarks>
        public IReadOnlyDictionary<string, DeviceHealth> Devices { get; private set; }

        /// <summary>PLC 디지털 입력 상태.</summary>
        public PlcDigitalState Plc { get; private set; }

        /// <summary>보조 계측값(온습도·풍속·파티클·FFU·MFC).</summary>
        public AuxiliaryReadings Auxiliary { get; private set; }

        /// <summary>제어 엔진 상태 요약.</summary>
        public ControlStatus Control { get; private set; }

        /// <summary>활성 알람 요약.</summary>
        public AlarmSummary Alarms { get; private set; }

        /// <summary>시스템 스냅샷을 생성한다.</summary>
        /// <param name="timestampUtc">스냅샷 시각(UTC).</param>
        /// <param name="pressures">차압센서 판독값(null 허용).</param>
        /// <param name="valves">밸브 상태(null 허용).</param>
        /// <param name="fans">팬 상태(null 허용).</param>
        /// <param name="plc">PLC 디지털 입력(null 이면 NoData).</param>
        /// <param name="auxiliary">보조 계측값(null 이면 Empty).</param>
        /// <param name="control">제어 상태(null 이면 Initial).</param>
        /// <param name="alarms">알람 요약(null 이면 None).</param>
        /// <param name="devices">디바이스별 통신 건강 상태(null 허용).</param>
        /// <remarks>
        /// <paramref name="devices"/> 를 선택 인자로 둔 이유는 기존 호출부를 그대로 두기
        /// 위해서다. 필수로 만들면 스냅샷을 직접 만드는 테스트 수백 곳이 한꺼번에 깨지고,
        /// 그 수정은 검증이 아니라 기계적 치환이 된다.
        /// </remarks>
        public SystemSnapshot(
            DateTime timestampUtc,
            IDictionary<string, PressureReading> pressures,
            IDictionary<string, ValveState> valves,
            IDictionary<string, FanState> fans,
            PlcDigitalState plc,
            AuxiliaryReadings auxiliary,
            ControlStatus control,
            AlarmSummary alarms,
            IDictionary<string, DeviceHealth> devices = null)
        {
            TimestampUtc = timestampUtc;

            // 생성자에서 방어적 복사를 수행해 이후 원본 딕셔너리가 바뀌어도 스냅샷이 불변임을 보장한다.
            Pressures = pressures == null
                ? (IReadOnlyDictionary<string, PressureReading>)EmptyPressures
                : new ReadOnlyDictionary<string, PressureReading>(
                    new Dictionary<string, PressureReading>(pressures, StringComparer.OrdinalIgnoreCase));

            Valves = valves == null
                ? (IReadOnlyDictionary<string, ValveState>)EmptyValves
                : new ReadOnlyDictionary<string, ValveState>(
                    new Dictionary<string, ValveState>(valves, StringComparer.OrdinalIgnoreCase));

            Fans = fans == null
                ? (IReadOnlyDictionary<string, FanState>)EmptyFans
                : new ReadOnlyDictionary<string, FanState>(
                    new Dictionary<string, FanState>(fans, StringComparer.OrdinalIgnoreCase));

            Devices = devices == null
                ? (IReadOnlyDictionary<string, DeviceHealth>)EmptyDevices
                : new ReadOnlyDictionary<string, DeviceHealth>(
                    new Dictionary<string, DeviceHealth>(devices, StringComparer.OrdinalIgnoreCase));

            Plc = plc ?? PlcDigitalState.NoData();
            Auxiliary = auxiliary ?? AuxiliaryReadings.Empty;
            Control = control ?? ControlStatus.Initial;
            Alarms = alarms ?? AlarmSummary.None;
        }

        /// <summary>모든 값이 미수집인 초기 스냅샷을 만든다.</summary>
        /// <param name="timestampUtc">스냅샷 시각(UTC).</param>
        /// <returns>빈 스냅샷.</returns>
        public static SystemSnapshot CreateEmpty(DateTime timestampUtc)
        {
            return new SystemSnapshot(timestampUtc, null, null, null, null, null, null, null);
        }

        /// <summary>지정한 ID 의 차압센서 판독값을 찾는다.</summary>
        /// <param name="sensorId">센서 ID.</param>
        /// <returns>판독값. 없으면 null.</returns>
        public PressureReading FindPressure(string sensorId)
        {
            PressureReading reading;
            if (!string.IsNullOrEmpty(sensorId) && Pressures.TryGetValue(sensorId, out reading))
            {
                return reading;
            }

            return null;
        }

        /// <summary>지정한 ID 의 밸브 상태를 찾는다.</summary>
        /// <param name="valveId">밸브 ID.</param>
        /// <returns>밸브 상태. 없으면 null.</returns>
        public ValveState FindValve(string valveId)
        {
            ValveState state;
            if (!string.IsNullOrEmpty(valveId) && Valves.TryGetValue(valveId, out state))
            {
                return state;
            }

            return null;
        }

        /// <summary>지정한 ID 의 디바이스 건강 상태를 찾는다.</summary>
        /// <param name="deviceId">디바이스 ID.</param>
        /// <returns>건강 상태. 없으면 null.</returns>
        public DeviceHealth FindDevice(string deviceId)
        {
            DeviceHealth health;
            if (!string.IsNullOrEmpty(deviceId) && Devices.TryGetValue(deviceId, out health))
            {
                return health;
            }

            return null;
        }

        /// <summary>지정한 ID 의 팬 상태를 찾는다.</summary>
        /// <param name="fanId">팬 ID.</param>
        /// <returns>팬 상태. 없으면 null.</returns>
        public FanState FindFan(string fanId)
        {
            FanState state;
            if (!string.IsNullOrEmpty(fanId) && Fans.TryGetValue(fanId, out state))
            {
                return state;
            }

            return null;
        }
    }
}
