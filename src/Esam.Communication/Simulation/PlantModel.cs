using System;
using System.Collections.Generic;
using System.Globalization;
using Esam.Domain.Configuration;
using Esam.Domain.Control;

namespace Esam.Communication.Simulation
{
    /// <summary>
    /// EFEM 기류·차압 계통의 가상 플랜트 모델.
    /// </summary>
    /// <remarks>
    /// <para>구성: 체인 n조 각각이 스로틀밸브 1개 + 송풍팬 1개 + 센서2/센서3 1개씩을 갖고,
    /// 센서 1은 EFEM 내부 공통이라 모든 체인의 평균 액추에이터 상태에 반응한다.</para>
    /// <para>모델링 요소 3가지</para>
    /// <list type="number">
    ///   <item><description><b>액추에이터 구동 지연</b> — 밸브는 pulse/초, 팬은 RPM/초로
    ///     속도 제한되어 목표값까지 시간이 걸린다. 지령을 내자마자 반영되는 모델은
    ///     Dwell 파라미터 검증에 쓸 수 없다.</description></item>
    ///   <item><description><b>압력 1차 지연</b> — 챔버 부피로 인한 시정수.</description></item>
    ///   <item><description><b>측정 노이즈</b> — 수 Pa 제어에서 필터 설계의 근거.</description></item>
    /// </list>
    /// <para>스레드 안전하지 않다. 시뮬레이션 스레드 1개에서만 조작한다.</para>
    /// </remarks>
    public sealed class PlantModel
    {
        /// <summary>체인 1조의 시뮬레이션 상태.</summary>
        private sealed class ChainState
        {
            public int Id;
            public string ValveId;
            public string FanId;
            public string Sensor2Id;
            public string Sensor3Id;

            public double ValvePulse;
            public double ValveTargetPulse;
            public bool ValveHomeDone;

            public double FanRpm;
            public double FanTargetRpm;

            public FirstOrderLag Sensor2Lag;
            public FirstOrderLag Sensor3Lag;
        }

        private readonly PlantOptions _options;
        private readonly GaussianNoise _noise;
        private readonly List<ChainState> _chains = new List<ChainState>();
        private readonly Dictionary<string, ChainState> _byValveId;
        private readonly Dictionary<string, ChainState> _byFanId;
        private readonly Dictionary<string, ChainState> _bySensor2Id;
        private readonly Dictionary<string, ChainState> _bySensor3Id;
        private readonly FirstOrderLag _sensor1Lag;

        // 나머지 조회 딕셔너리와 동일하게 대소문자를 무시해야 한다.
        // List.Contains 를 쓰면 S1 만 대소문자를 구분해 조용히 조회 실패하는 비대칭이 생긴다.
        private readonly HashSet<string> _sensor1Ids;

        /// <summary>누적 시뮬레이션 시간 [초].</summary>
        public double ElapsedSec { get; private set; }

        /// <summary>체인 개수.</summary>
        public int ChainCount
        {
            get { return _chains.Count; }
        }

        /// <summary>적용 중인 물리 파라미터.</summary>
        public PlantOptions Options
        {
            get { return _options; }
        }

        /// <summary>가상 플랜트를 생성한다.</summary>
        /// <param name="chains">체인 정의(chains.json 과 동일한 ID 체계를 사용한다).</param>
        /// <param name="sensor1Ids">센서 1 ID 목록(예: S1-1, S1-2, S1-3).</param>
        /// <param name="options">물리 파라미터. null 이면 기본값.</param>
        /// <param name="seed">노이즈 난수 시드.</param>
        /// <exception cref="ArgumentException">체인 정의가 비어 있을 때.</exception>
        public PlantModel(
            IList<ChainDefinition> chains,
            IList<string> sensor1Ids,
            PlantOptions options,
            int seed)
        {
            if (chains == null || chains.Count == 0)
            {
                throw new ArgumentException("체인 정의가 비어 있습니다.", "chains");
            }

            _options = options ?? new PlantOptions();
            _noise = new GaussianNoise(seed);

            _byValveId = new Dictionary<string, ChainState>(StringComparer.OrdinalIgnoreCase);
            _byFanId = new Dictionary<string, ChainState>(StringComparer.OrdinalIgnoreCase);
            _bySensor2Id = new Dictionary<string, ChainState>(StringComparer.OrdinalIgnoreCase);
            _bySensor3Id = new Dictionary<string, ChainState>(StringComparer.OrdinalIgnoreCase);

            foreach (ChainDefinition definition in chains)
            {
                if (definition == null)
                {
                    continue;
                }

                ChainState state = new ChainState
                {
                    Id = definition.Id,
                    ValveId = definition.ValveId,
                    FanId = definition.FanId,
                    Sensor2Id = definition.Sensor2Id,
                    Sensor3Id = definition.Sensor3Id,
                    ValvePulse = 0.0,
                    ValveTargetPulse = 0.0,

                    // 전원 ON 직후를 모사한다. 원점 복귀 전에는 제어가 진입할 수 없어야 하고,
                    // 그것을 시뮬레이션으로 검증할 수 있어야 한다.
                    ValveHomeDone = false,

                    FanRpm = 0.0,
                    FanTargetRpm = 0.0,
                    Sensor2Lag = new FirstOrderLag(_options.Sensor2TauSec, _options.Sensor2BasePa),
                    Sensor3Lag = new FirstOrderLag(_options.Sensor3TauSec, _options.Sensor3BasePa)
                };

                _chains.Add(state);

                if (!string.IsNullOrEmpty(state.ValveId))
                {
                    _byValveId[state.ValveId] = state;
                }

                if (!string.IsNullOrEmpty(state.FanId))
                {
                    _byFanId[state.FanId] = state;
                }

                if (!string.IsNullOrEmpty(state.Sensor2Id))
                {
                    _bySensor2Id[state.Sensor2Id] = state;
                }

                if (!string.IsNullOrEmpty(state.Sensor3Id))
                {
                    _bySensor3Id[state.Sensor3Id] = state;
                }
            }

            _sensor1Lag = new FirstOrderLag(_options.Sensor1TauSec, _options.Sensor1BasePa);
            _sensor1Ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (sensor1Ids != null)
            {
                foreach (string id in sensor1Ids)
                {
                    if (!string.IsNullOrEmpty(id))
                    {
                        _sensor1Ids.Add(id);
                    }
                }
            }
        }

        /// <summary>시뮬레이션을 지정 시간만큼 진행시킨다.</summary>
        /// <param name="dtSec">진행 시간 [초].</param>
        public void Advance(double dtSec)
        {
            if (dtSec <= 0.0)
            {
                return;
            }

            ElapsedSec += dtSec;

            double valveSum = 0.0;
            double fanSum = 0.0;

            foreach (ChainState chain in _chains)
            {
                AdvanceActuators(chain, dtSec);

                double valveRatio = ValveRatio(chain);
                double fanRatio = FanRatio(chain);

                valveSum += valveRatio;
                fanSum += fanRatio;

                double sensor2Target = _options.Sensor2BasePa
                                       - (_options.Sensor2ValveGain * valveRatio)
                                       - (_options.Sensor2FanGain * fanRatio);

                double sensor3Target = _options.Sensor3BasePa
                                       - (_options.Sensor3ValveGain * valveRatio)
                                       - (_options.Sensor3FanGain * fanRatio);

                chain.Sensor2Lag.Advance(sensor2Target, dtSec);
                chain.Sensor3Lag.Advance(sensor3Target, dtSec);
            }

            // 센서 1은 EFEM 내부 공통이므로 전 체인의 평균 배기량에 반응한다.
            int count = _chains.Count;
            double valveMean = count == 0 ? 0.0 : valveSum / count;
            double fanMean = count == 0 ? 0.0 : fanSum / count;

            double sensor1Target = _options.Sensor1BasePa
                                   - (_options.Sensor1ValveGain * valveMean)
                                   - (_options.Sensor1FanGain * fanMean);

            _sensor1Lag.Advance(sensor1Target, dtSec);
        }

        /// <summary>도메인 계층이 생성한 액추에이터 지령을 플랜트에 적용한다.</summary>
        /// <param name="command">액추에이터 지령.</param>
        /// <returns>지령 대상을 찾아 적용했으면 true.</returns>
        public bool ApplyCommand(ActuatorCommand command)
        {
            if (command == null)
            {
                return false;
            }

            ChainState chain;

            switch (command.Kind)
            {
                case ActuatorCommandKind.SetValvePosition:
                    if (!_byValveId.TryGetValue(command.DeviceId, out chain))
                    {
                        return false;
                    }

                    chain.ValveTargetPulse = Clamp(command.Value, 0.0, _options.ValveFullOpenPulse);
                    return true;

                case ActuatorCommandKind.CloseValve:
                    if (!_byValveId.TryGetValue(command.DeviceId, out chain))
                    {
                        return false;
                    }

                    chain.ValveTargetPulse = 0.0;
                    return true;

                case ActuatorCommandKind.QuickStopValve:
                    if (!_byValveId.TryGetValue(command.DeviceId, out chain))
                    {
                        return false;
                    }

                    // 즉시 정지는 현재 위치를 목표로 고정하는 것과 같다.
                    chain.ValveTargetPulse = chain.ValvePulse;
                    return true;

                case ActuatorCommandKind.HomeValve:
                    if (!_byValveId.TryGetValue(command.DeviceId, out chain))
                    {
                        return false;
                    }

                    chain.ValveTargetPulse = 0.0;
                    chain.ValveHomeDone = true;
                    return true;

                case ActuatorCommandKind.SetFanRpm:
                    if (!_byFanId.TryGetValue(command.DeviceId, out chain))
                    {
                        return false;
                    }

                    chain.FanTargetRpm = Clamp(command.Value, 0.0, _options.FanMaxRpm);
                    return true;

                case ActuatorCommandKind.StopFan:
                    if (!_byFanId.TryGetValue(command.DeviceId, out chain))
                    {
                        return false;
                    }

                    chain.FanTargetRpm = 0.0;
                    return true;

                case ActuatorCommandKind.StartFan:
                    if (!_byFanId.TryGetValue(command.DeviceId, out chain))
                    {
                        return false;
                    }

                    if (chain.FanTargetRpm <= 0.0)
                    {
                        chain.FanTargetRpm = _options.FanMaxRpm * 0.3;
                    }

                    return true;

                default:
                    return false;
            }
        }

        /// <summary>여러 지령을 한 번에 적용한다.</summary>
        /// <param name="commands">지령 목록.</param>
        /// <returns>적용된 지령 수.</returns>
        public int ApplyCommands(IEnumerable<ActuatorCommand> commands)
        {
            if (commands == null)
            {
                return 0;
            }

            int applied = 0;
            foreach (ActuatorCommand command in commands)
            {
                if (ApplyCommand(command))
                {
                    applied++;
                }
            }

            return applied;
        }

        /// <summary>모든 밸브의 원점 복귀를 완료 처리한다. 시뮬레이션 초기화 편의 메서드.</summary>
        public void CompleteAllHoming()
        {
            foreach (ChainState chain in _chains)
            {
                chain.ValveHomeDone = true;
            }
        }

        /// <summary>센서 ID 로 압력값을 조회한다(노이즈 포함).</summary>
        /// <param name="sensorId">센서 ID(S1-*, S2-*, S3-* 중 하나).</param>
        /// <param name="pressurePa">조회된 압력 [Pa].</param>
        /// <returns>해당 ID 의 센서가 존재하면 true.</returns>
        public bool TryGetPressure(string sensorId, out double pressurePa)
        {
            pressurePa = 0.0;

            if (string.IsNullOrEmpty(sensorId))
            {
                return false;
            }

            ChainState chain;

            if (_bySensor2Id.TryGetValue(sensorId, out chain))
            {
                pressurePa = chain.Sensor2Lag.Value + _noise.Next(_options.Sensor2NoiseSigmaPa);
                return true;
            }

            if (_bySensor3Id.TryGetValue(sensorId, out chain))
            {
                pressurePa = chain.Sensor3Lag.Value + _noise.Next(_options.Sensor3NoiseSigmaPa);
                return true;
            }

            if (_sensor1Ids.Contains(sensorId))
            {
                pressurePa = _sensor1Lag.Value + _noise.Next(_options.Sensor1NoiseSigmaPa);
                return true;
            }

            return false;
        }

        /// <summary>센서 ID 의 노이즈 없는 참값을 조회한다. 검증·디버깅용.</summary>
        /// <param name="sensorId">센서 ID.</param>
        /// <param name="pressurePa">조회된 참값 [Pa].</param>
        /// <returns>해당 ID 의 센서가 존재하면 true.</returns>
        public bool TryGetTruePressure(string sensorId, out double pressurePa)
        {
            pressurePa = 0.0;

            ChainState chain;

            if (_bySensor2Id.TryGetValue(sensorId ?? string.Empty, out chain))
            {
                pressurePa = chain.Sensor2Lag.Value;
                return true;
            }

            if (_bySensor3Id.TryGetValue(sensorId ?? string.Empty, out chain))
            {
                pressurePa = chain.Sensor3Lag.Value;
                return true;
            }

            if (sensorId != null && _sensor1Ids.Contains(sensorId))
            {
                pressurePa = _sensor1Lag.Value;
                return true;
            }

            return false;
        }

        /// <summary>밸브 ID 로 현재 위치를 조회한다.</summary>
        /// <param name="valveId">밸브 ID.</param>
        /// <param name="pulse">현재 위치 [pulse].</param>
        /// <param name="targetPulse">목표 위치 [pulse].</param>
        /// <param name="homeDone">원점 복귀 완료 여부.</param>
        /// <returns>해당 밸브가 존재하면 true.</returns>
        public bool TryGetValve(string valveId, out int pulse, out int targetPulse, out bool homeDone)
        {
            pulse = 0;
            targetPulse = 0;
            homeDone = false;

            ChainState chain;
            if (!_byValveId.TryGetValue(valveId ?? string.Empty, out chain))
            {
                return false;
            }

            pulse = (int)Math.Round(chain.ValvePulse, MidpointRounding.AwayFromZero);
            targetPulse = (int)Math.Round(chain.ValveTargetPulse, MidpointRounding.AwayFromZero);
            homeDone = chain.ValveHomeDone;
            return true;
        }

        /// <summary>팬 ID 로 현재 회전수를 조회한다.</summary>
        /// <param name="fanId">팬 ID.</param>
        /// <param name="rpm">현재 회전수 [RPM].</param>
        /// <param name="targetRpm">목표 회전수 [RPM].</param>
        /// <returns>해당 팬이 존재하면 true.</returns>
        public bool TryGetFan(string fanId, out double rpm, out double targetRpm)
        {
            rpm = 0.0;
            targetRpm = 0.0;

            ChainState chain;
            if (!_byFanId.TryGetValue(fanId ?? string.Empty, out chain))
            {
                return false;
            }

            rpm = chain.FanRpm;
            targetRpm = chain.FanTargetRpm;
            return true;
        }

        /// <summary>플랜트 상태를 한 줄로 요약한다. 시뮬레이션 로그용.</summary>
        /// <returns>요약 문자열.</returns>
        public string Describe()
        {
            System.Text.StringBuilder builder = new System.Text.StringBuilder();
            builder.Append(string.Format(
                CultureInfo.InvariantCulture, "t={0:F1}s S1={1:F2}Pa", ElapsedSec, _sensor1Lag.Value));

            foreach (ChainState chain in _chains)
            {
                builder.Append(string.Format(
                    CultureInfo.InvariantCulture,
                    " | #{0} V={1:F0}p F={2:F0}rpm S2={3:F1} S3={4:F1}",
                    chain.Id, chain.ValvePulse, chain.FanRpm,
                    chain.Sensor2Lag.Value, chain.Sensor3Lag.Value));
            }

            return builder.ToString();
        }

        /// <summary>액추에이터를 속도 제한 하에 목표값으로 진행시킨다.</summary>
        /// <param name="chain">체인 상태.</param>
        /// <param name="dtSec">진행 시간 [초].</param>
        private void AdvanceActuators(ChainState chain, double dtSec)
        {
            double maxValveStep = _options.ValveSlewPulsePerSec * dtSec;
            chain.ValvePulse = MoveToward(chain.ValvePulse, chain.ValveTargetPulse, maxValveStep);

            double maxFanStep = _options.FanRampRpmPerSec * dtSec;
            chain.FanRpm = MoveToward(chain.FanRpm, chain.FanTargetRpm, maxFanStep);
        }

        /// <summary>밸브 개도 비율(0~1)을 계산한다.</summary>
        /// <param name="chain">체인 상태.</param>
        /// <returns>개도 비율.</returns>
        private double ValveRatio(ChainState chain)
        {
            if (_options.ValveFullOpenPulse <= 0)
            {
                return 0.0;
            }

            return Clamp(chain.ValvePulse / _options.ValveFullOpenPulse, 0.0, 1.0);
        }

        /// <summary>팬 회전수 비율(0~1)을 계산한다.</summary>
        /// <param name="chain">체인 상태.</param>
        /// <returns>회전수 비율.</returns>
        private double FanRatio(ChainState chain)
        {
            if (_options.FanMaxRpm <= 0.0)
            {
                return 0.0;
            }

            return Clamp(chain.FanRpm / _options.FanMaxRpm, 0.0, 1.0);
        }

        /// <summary>현재값을 목표값으로 최대 step 만큼 이동시킨다.</summary>
        /// <param name="current">현재값.</param>
        /// <param name="target">목표값.</param>
        /// <param name="maxStep">1회 최대 이동량.</param>
        /// <returns>이동 후 값.</returns>
        private static double MoveToward(double current, double target, double maxStep)
        {
            double delta = target - current;

            if (Math.Abs(delta) <= maxStep)
            {
                return target;
            }

            return current + (delta > 0.0 ? maxStep : -maxStep);
        }

        /// <summary>값을 지정 범위로 제한한다.</summary>
        /// <param name="value">입력값.</param>
        /// <param name="min">하한.</param>
        /// <param name="max">상한.</param>
        /// <returns>제한된 값.</returns>
        private static double Clamp(double value, double min, double max)
        {
            if (double.IsNaN(value))
            {
                return min;
            }

            if (value < min)
            {
                return min;
            }

            return value > max ? max : value;
        }
    }
}
