using System;
using System.Collections.Generic;
using Esam.Communication.Configuration;
using Esam.Communication.Polling;
using Esam.Domain.Models;

namespace Esam.Services
{
    /// <summary>
    /// 포트 워커가 발행한 수집값을 <see cref="SystemSnapshot"/> 으로 조립한다.
    /// </summary>
    /// <remarks>
    /// <para><b>누적이 필수다.</b> 폴링 사이클마다 발행되는 결과에는 그 사이클에 읽은 티어만
    /// 담겨 있다. Fast 티어(차압·밸브위치)는 매번 오지만 Medium/Slow(온도·풍속)는
    /// 몇 초에 한 번만 온다. 사이클 결과만으로 스냅샷을 만들면 온도값이 매 사이클
    /// 사라졌다 나타난다. 따라서 마지막 값을 계속 보관하고 새 값이 오면 갱신한다.</para>
    /// <para><b>통신 실패 시 값을 유지하지 않는다.</b> 실패한 디바이스의 포인트는
    /// 직전 값을 남기되 <see cref="Quality.Bad"/> 로 표시한다.
    /// 제어 로직은 Good 이 아닌 값으로 액추에이터를 움직이지 않으므로,
    /// 통신이 끊긴 센서의 낡은 값으로 밸브가 움직이는 사고를 막는다.</para>
    /// <para><b>Stale 판정.</b> 갱신 없이 허용 시간을 넘긴 값은 Stale 로 격하한다.
    /// 워커가 살아 있는데 특정 그룹만 응답이 없는 경우를 잡아낸다.</para>
    /// <para>이 클래스는 스레드 안전하지 않다. <see cref="DataStore"/> 가 락으로 감싼다.</para>
    /// </remarks>
    public sealed class SnapshotBuilder
    {
        private readonly DeviceMap _map;

        /// <summary>경로("디바이스ID.키") → 최신 표본.</summary>
        private readonly Dictionary<string, PointSample> _points =
            new Dictionary<string, PointSample>(StringComparer.OrdinalIgnoreCase);

        /// <summary>디바이스 ID → 그 디바이스가 제공하는 경로 목록. 실패 시 일괄 격하에 사용한다.</summary>
        private readonly Dictionary<string, List<string>> _pathsByDevice =
            new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        /// <summary>조립기를 생성한다.</summary>
        /// <param name="map">
        /// 통신 구성. 디바이스마다 어떤 드라이버인지 판별해 어떤 타입의 상태를 만들지 결정한다.
        /// </param>
        /// <exception cref="ArgumentNullException">구성이 null 일 때.</exception>
        public SnapshotBuilder(DeviceMap map)
        {
            if (map == null)
            {
                throw new ArgumentNullException("map");
            }

            _map = map;
            StaleThresholdMs = 15000.0;
        }

        /// <summary>
        /// 값을 갱신 없이 유지할 수 있는 최대 시간 [ms]. 초과하면 Stale 로 격하한다.
        /// Slow 티어(5초)를 넉넉히 넘기는 값이어야 오탐이 없다.
        /// </summary>
        public double StaleThresholdMs { get; set; }

        /// <summary>누적된 측정점 수. 진단용.</summary>
        public int PointCount
        {
            get { return _points.Count; }
        }

        /// <summary>폴링 결과를 누적 상태에 반영한다.</summary>
        /// <param name="args">폴링 완료 결과.</param>
        public void Apply(PollCompletedEventArgs args)
        {
            if (args == null)
            {
                return;
            }

            foreach (GroupReadResult result in args.Results)
            {
                if (result.IsSuccess)
                {
                    foreach (PointSample sample in result.Samples)
                    {
                        Store(sample);
                    }

                    continue;
                }

                // 실패한 그룹은 표본이 없다. 이 디바이스가 이전에 제공했던 포인트를
                // Bad 로 격하해, 제어가 낡은 값을 Good 으로 착각하지 않게 한다.
                DegradeDevice(result.DeviceId);
            }
        }

        /// <summary>표본 하나를 누적 상태에 저장한다.</summary>
        /// <param name="sample">측정점 표본.</param>
        private void Store(PointSample sample)
        {
            if (sample == null || string.IsNullOrEmpty(sample.Key))
            {
                return;
            }

            _points[sample.Path] = sample;

            List<string> paths;
            if (!_pathsByDevice.TryGetValue(sample.DeviceId, out paths))
            {
                paths = new List<string>();
                _pathsByDevice[sample.DeviceId] = paths;
            }

            if (!paths.Contains(sample.Path))
            {
                paths.Add(sample.Path);
            }
        }

        /// <summary>지정 디바이스의 모든 포인트를 <see cref="Quality.Bad"/> 로 격하한다.</summary>
        /// <param name="deviceId">디바이스 ID.</param>
        private void DegradeDevice(string deviceId)
        {
            List<string> paths;
            if (string.IsNullOrEmpty(deviceId) || !_pathsByDevice.TryGetValue(deviceId, out paths))
            {
                return;
            }

            foreach (string path in paths)
            {
                PointSample previous;
                if (!_points.TryGetValue(path, out previous) || previous.Quality == Quality.Bad)
                {
                    continue;
                }

                // 값은 참고용으로 남기되 품질만 Bad 로 바꾼다.
                // 값을 0 으로 지우면 트렌드에 가짜 급락이 기록된다.
                _points[path] = new PointSample(
                    previous.DeviceId, previous.Key, previous.Value, previous.RawValue,
                    Quality.Bad, previous.Unit, previous.TimestampUtc);
            }
        }

        /// <summary>현재 누적 상태로 스냅샷을 만든다.</summary>
        /// <param name="control">제어 상태 요약.</param>
        /// <param name="alarms">알람 요약.</param>
        /// <param name="nowUtc">스냅샷 시각(UTC).</param>
        /// <returns>조립된 스냅샷.</returns>
        public SystemSnapshot Build(ControlStatus control, AlarmSummary alarms, DateTime nowUtc)
        {
            Dictionary<string, PressureReading> pressures =
                new Dictionary<string, PressureReading>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, ValveState> valves =
                new Dictionary<string, ValveState>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, FanState> fans =
                new Dictionary<string, FanState>(StringComparer.OrdinalIgnoreCase);

            PlcDigitalState plc = null;
            AuxiliaryAccumulator aux = new AuxiliaryAccumulator();

            if (_map.Devices != null)
            {
                foreach (DeviceInstanceDefinition device in _map.Devices)
                {
                    if (device == null || !device.Enabled || string.IsNullOrEmpty(device.Id))
                    {
                        continue;
                    }

                    DeviceTypeDefinition type = _map.FindType(device.Type);
                    string driver = type == null ? null : type.Driver;

                    switch (driver)
                    {
                        case PointKeys.DriverPressureSensor:
                            pressures[device.Id] = BuildPressure(device, nowUtc);
                            break;

                        case PointKeys.DriverThrottleValve:
                            valves[device.Id] = BuildValve(device, nowUtc);
                            break;

                        case PointKeys.DriverModbusFan:
                            fans[device.Id] = BuildFan(device, nowUtc);
                            break;

                        case PointKeys.DriverPlc:
                            plc = BuildPlc(device, nowUtc, aux);
                            break;

                        case PointKeys.DriverTempHumidity:
                            aux.ApplyTempHumidity(this, device.Id, nowUtc);
                            break;

                        case PointKeys.DriverAirVelocity:
                            aux.ApplyVelocity(this, device.Id, nowUtc);
                            break;

                        case PointKeys.DriverParticle:
                            aux.ApplyParticle(this, device.Id, nowUtc);
                            break;

                        case PointKeys.DriverMfc:
                            aux.ApplyMfc(this, device.Id, nowUtc);
                            break;

                        case PointKeys.DriverFfu:
                            aux.ApplyFfu(this, device.Id, nowUtc);
                            break;

                        default:
                            // 알 수 없는 드라이버는 무시한다. 설정 검증에서 이미 걸러졌어야 한다.
                            break;
                    }
                }
            }

            return new SystemSnapshot(
                nowUtc, pressures, valves, fans,
                plc ?? PlcDigitalState.NoData(),
                aux.ToReadings(nowUtc),
                control, alarms);
        }

        /// <summary>차압센서 판독값을 만든다.</summary>
        private PressureReading BuildPressure(DeviceInstanceDefinition device, DateTime nowUtc)
        {
            PointSample sample = Find(device.Id, PointKeys.PressurePa, nowUtc);

            if (sample == null)
            {
                return PressureReading.NoData(device.Id);
            }

            return new PressureReading(
                device.Id, sample.Value, sample.RawValue, 0,
                device.Offset, sample.Quality, sample.TimestampUtc);
        }

        /// <summary>스로틀밸브 상태를 만든다.</summary>
        private ValveState BuildValve(DeviceInstanceDefinition device, DateTime nowUtc)
        {
            PointSample position = Find(device.Id, PointKeys.PositionPulse, nowUtc);

            if (position == null)
            {
                return ValveState.NoData(device.Id);
            }

            DeviceTypeDefinition type = _map.FindType(device.Type);
            DeviceConversion conversion = type == null ? null : type.Conversion;

            int fullOpen = conversion == null || conversion.PulsePerFullOpen <= 0
                ? 5000
                : conversion.PulsePerFullOpen;

            double fullDegree = conversion == null || conversion.FullOpenDegree <= 0.0
                ? 90.0
                : conversion.FullOpenDegree;

            int pulse = (int)Math.Round(position.Value, MidpointRounding.AwayFromZero);

            PointSample motion = Find(device.Id, PointKeys.MotionStatus, nowUtc);
            PointSample alarm = Find(device.Id, PointKeys.AlarmCode, nowUtc);
            PointSample home = Find(device.Id, PointKeys.HomeDone, nowUtc);

            ushort alarmCode = alarm == null ? (ushort)0 : ToUInt16(alarm.Value);

            // 모션 상태 비트 정의가 미확정이므로(Open Issue #5) 0 = 정지, 그 외 = 이동중으로 본다.
            ValveMotionStatus motionStatus;

            if (motion == null)
            {
                motionStatus = ValveMotionStatus.Unknown;
            }
            else if (alarmCode != 0)
            {
                motionStatus = ValveMotionStatus.Fault;
            }
            else
            {
                motionStatus = motion.Value == 0.0 ? ValveMotionStatus.Idle : ValveMotionStatus.Moving;
            }

            return new ValveState(
                device.Id,
                pulse,
                pulse,
                pulse / (double)fullOpen * 100.0,
                pulse / (double)fullOpen * fullDegree,
                motionStatus,
                alarmCode,
                home != null && home.AsBoolean,
                position.Quality,
                position.TimestampUtc);
        }

        /// <summary>송풍팬 상태를 만든다.</summary>
        private FanState BuildFan(DeviceInstanceDefinition device, DateTime nowUtc)
        {
            PointSample rpm = Find(device.Id, PointKeys.Rpm, nowUtc);

            if (rpm == null)
            {
                return FanState.NoData(device.Id);
            }

            PointSample status = Find(device.Id, PointKeys.RunStatus, nowUtc);
            PointSample alarm = Find(device.Id, PointKeys.AlarmCode, nowUtc);

            ushort alarmCode = alarm == null ? (ushort)0 : ToUInt16(alarm.Value);

            FanRunStatus runStatus;

            if (alarmCode != 0)
            {
                runStatus = FanRunStatus.Fault;
            }
            else if (status == null)
            {
                runStatus = FanRunStatus.Unknown;
            }
            else if (status.Value <= 0.0)
            {
                runStatus = FanRunStatus.Stopped;
            }
            else
            {
                runStatus = status.Value >= 2.0 ? FanRunStatus.Running : FanRunStatus.Ramping;
            }

            // 목표값 자리에는 드라이버에서 되읽은 설정값을 넣는다.
            // 예전에는 측정값을 그대로 복사했는데, 그러면 화면과 진단이
            // "지령과 실측이 항상 일치"하는 것처럼 보여 지령 거부를 알아챌 수 없다.
            // 되읽기 포인트가 없는 구성에서는 부득이 측정값으로 대체한다.
            PointSample setpoint = Find(device.Id, PointKeys.RpmSetpoint, nowUtc);

            double targetRpm = setpoint != null && setpoint.Quality == Quality.Good
                ? setpoint.Value
                : rpm.Value;

            return new FanState(
                device.Id, rpm.Value, targetRpm, runStatus, alarmCode,
                rpm.Quality, rpm.TimestampUtc);
        }

        /// <summary>PLC 디지털 입력 상태를 만들고, 온도값은 보조 계측에 넘긴다.</summary>
        private PlcDigitalState BuildPlc(
            DeviceInstanceDefinition device, DateTime nowUtc, AuxiliaryAccumulator aux)
        {
            bool[] fanStops = new bool[5];
            bool anyKnown = false;
            DateTime latest = DateTime.MinValue;
            Quality quality = Quality.NoData;

            for (int i = 0; i < 5; i++)
            {
                PointSample sample = Find(
                    device.Id,
                    PointKeys.DiFanStopPrefix + i.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    nowUtc);

                if (sample != null)
                {
                    fanStops[i] = sample.AsBoolean;
                    anyKnown = true;
                    quality = sample.Quality;

                    if (sample.TimestampUtc > latest)
                    {
                        latest = sample.TimestampUtc;
                    }
                }

                // 팬 온도는 PLC 가 읽지만 표시상으로는 보조 계측이다.
                PointSample temp = Find(
                    device.Id,
                    PointKeys.TempFanPrefix + i.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    nowUtc);

                if (temp != null && temp.Quality == Quality.Good)
                {
                    aux.SetFanTemperature(i, temp.Value);
                }
            }

            // 컨트롤박스 냉각팬은 상·하 2대다. 어느 한쪽이라도 정지하면 알람으로 본다.
            PointSample ctrlBoxFanTop = Find(device.Id, PointKeys.DiControlBoxFanTop, nowUtc);
            PointSample ctrlBoxFanBottom = Find(device.Id, PointKeys.DiControlBoxFanBottom, nowUtc);
            PointSample ctrlBoxFan = ctrlBoxFanTop ?? ctrlBoxFanBottom;

            bool ctrlBoxFanAlarm =
                (ctrlBoxFanTop != null && ctrlBoxFanTop.AsBoolean)
                || (ctrlBoxFanBottom != null && ctrlBoxFanBottom.AsBoolean);

            PointSample emo = Find(device.Id, PointKeys.DiEmo, nowUtc);

            // 도어·메인 차단기는 배선된 입력이 없어 항상 null 이다.
            // 키 조회는 남겨 두어 배선이 추가되면 코드 변경 없이 동작하게 한다.
            PointSample door = Find(device.Id, PointKeys.DiDoor, nowUtc);
            PointSample breaker = Find(device.Id, PointKeys.DiMainBreaker, nowUtc);
            PointSample panel = Find(device.Id, PointKeys.TempPanel, nowUtc);

            if (panel != null && panel.Quality == Quality.Good)
            {
                aux.ControlBoxTemperature = panel.Value;
            }

            // 도어·차단기는 배선이 없어 판정에서 제외한다.
            // 이 둘을 조건에 넣으면 정상 구성에서도 영원히 NoData 가 된다.
            if (!anyKnown && emo == null)
            {
                return PlcDigitalState.NoData();
            }

            // 안전 입력은 하나라도 품질이 나쁘면 전체를 Bad 로 본다.
            // 인터록 IL-04 가 "안전 입력을 신뢰할 수 없음"으로 판정해 전체 정지시켜야 한다.
            Quality safetyQuality = WorstQuality(quality, emo, breaker, door, ctrlBoxFan);

            return new PlcDigitalState(
                fanStops,
                ctrlBoxFanAlarm,
                emo != null && emo.AsBoolean,
                door != null && door.AsBoolean,
                breaker != null && breaker.AsBoolean,
                safetyQuality,
                latest == DateTime.MinValue ? nowUtc : latest);
        }

        /// <summary>여러 표본 중 가장 나쁜 품질을 고른다.</summary>
        private static Quality WorstQuality(Quality seed, params PointSample[] samples)
        {
            Quality worst = seed;

            foreach (PointSample sample in samples)
            {
                if (sample == null)
                {
                    continue;
                }

                // Quality 열거형은 값이 클수록 나쁘게 정의되어 있다(Good=1 … Bad=4).
                if (sample.Quality > worst)
                {
                    worst = sample.Quality;
                }
            }

            return worst;
        }

        /// <summary>
        /// 누적 상태에서 표본을 찾는다. 갱신이 오래 끊긴 값은 Stale 로 격하해 반환한다.
        /// </summary>
        /// <param name="deviceId">디바이스 ID.</param>
        /// <param name="key">측정점 키.</param>
        /// <param name="nowUtc">현재 시각(UTC).</param>
        /// <returns>표본. 없으면 null.</returns>
        internal PointSample Find(string deviceId, string key, DateTime nowUtc)
        {
            PointSample sample;

            if (!_points.TryGetValue(string.Concat(deviceId, ".", key), out sample))
            {
                return null;
            }

            if (sample.Quality != Quality.Good || StaleThresholdMs <= 0.0)
            {
                return sample;
            }

            double ageMs = (nowUtc - sample.TimestampUtc).TotalMilliseconds;

            if (ageMs <= StaleThresholdMs)
            {
                return sample;
            }

            // 워커는 살아 있는데 이 그룹만 응답이 없는 경우다.
            // 값을 그대로 쓰면 제어가 과거 상태를 현재로 착각한다.
            return new PointSample(
                sample.DeviceId, sample.Key, sample.Value, sample.RawValue,
                Quality.Stale, sample.Unit, sample.TimestampUtc);
        }

        /// <summary>double 값을 부호 없는 16비트로 변환한다.</summary>
        private static ushort ToUInt16(double value)
        {
            double rounded = Math.Round(value, MidpointRounding.AwayFromZero);

            if (rounded <= 0.0)
            {
                return 0;
            }

            return rounded >= ushort.MaxValue ? ushort.MaxValue : (ushort)rounded;
        }

        /// <summary>
        /// 보조 계측값을 모으는 누적기. 여러 디바이스의 값이 하나의
        /// <see cref="AuxiliaryReadings"/> 로 합쳐지므로 중간 버퍼가 필요하다.
        /// </summary>
        private sealed class AuxiliaryAccumulator
        {
            private readonly double?[] _velocities = new double?[3];
            private readonly double?[] _fanTemperatures = new double?[5];
            private readonly double?[] _mfcFlows = new double?[2];
            private readonly double?[] _mfcSetpoints = new double?[2];
            private int _velocityIndex;
            private int _mfcIndex;

            public double? EfemTemperature { get; set; }

            public double? EfemHumidity { get; set; }

            public double? Particle { get; set; }

            public double? ControlBoxTemperature { get; set; }

            public double? FfuRpm { get; set; }

            public void SetFanTemperature(int index, double value)
            {
                if (index >= 0 && index < _fanTemperatures.Length)
                {
                    _fanTemperatures[index] = value;
                }
            }

            public void ApplyTempHumidity(SnapshotBuilder owner, string deviceId, DateTime nowUtc)
            {
                PointSample temp = owner.Find(deviceId, PointKeys.Temperature, nowUtc);
                PointSample humidity = owner.Find(deviceId, PointKeys.Humidity, nowUtc);

                if (temp != null && temp.Quality == Quality.Good)
                {
                    EfemTemperature = temp.Value;
                }

                if (humidity != null && humidity.Quality == Quality.Good)
                {
                    EfemHumidity = humidity.Value;
                }
            }

            public void ApplyVelocity(SnapshotBuilder owner, string deviceId, DateTime nowUtc)
            {
                PointSample sample = owner.Find(deviceId, PointKeys.Velocity, nowUtc);

                if (_velocityIndex < _velocities.Length)
                {
                    if (sample != null && sample.Quality == Quality.Good)
                    {
                        _velocities[_velocityIndex] = sample.Value;
                    }

                    _velocityIndex++;
                }
            }

            public void ApplyParticle(SnapshotBuilder owner, string deviceId, DateTime nowUtc)
            {
                PointSample sample = owner.Find(deviceId, PointKeys.Particle, nowUtc);

                if (sample != null && sample.Quality == Quality.Good)
                {
                    Particle = sample.Value;
                }
            }

            public void ApplyMfc(SnapshotBuilder owner, string deviceId, DateTime nowUtc)
            {
                PointSample flow = owner.Find(deviceId, PointKeys.Flow, nowUtc);
                PointSample setpoint = owner.Find(deviceId, PointKeys.FlowSetpoint, nowUtc);

                if (_mfcIndex < _mfcFlows.Length)
                {
                    if (flow != null && flow.Quality == Quality.Good)
                    {
                        _mfcFlows[_mfcIndex] = flow.Value;
                    }

                    if (setpoint != null && setpoint.Quality == Quality.Good)
                    {
                        _mfcSetpoints[_mfcIndex] = setpoint.Value;
                    }

                    _mfcIndex++;
                }
            }

            public void ApplyFfu(SnapshotBuilder owner, string deviceId, DateTime nowUtc)
            {
                PointSample sample = owner.Find(deviceId, PointKeys.FfuRpm, nowUtc);

                if (sample != null && sample.Quality == Quality.Good)
                {
                    FfuRpm = sample.Value;
                }
            }

            public AuxiliaryReadings ToReadings(DateTime nowUtc)
            {
                return new AuxiliaryReadings(
                    _velocities,
                    EfemTemperature,
                    EfemHumidity,
                    Particle,
                    ControlBoxTemperature,
                    _fanTemperatures,
                    FfuRpm,
                    _mfcFlows,
                    _mfcSetpoints,
                    Quality.Good,
                    nowUtc);
            }
        }
    }
}
