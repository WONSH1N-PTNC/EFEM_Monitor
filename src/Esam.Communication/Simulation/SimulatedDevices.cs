using Esam.Domain.Control;

namespace Esam.Communication.Simulation
{
    /// <summary>
    /// 차압센서(WTDM-550) 시뮬레이션 슬레이브.
    /// </summary>
    /// <remarks>
    /// <b>주소와 스케일은 잠정값이다</b>(DESIGN.md Open Issue: 매뉴얼 4번 미확보).
    /// 매뉴얼 확보 후 <see cref="PressureRegister"/> 와 <see cref="PaPerLsb"/> 만 수정하면 된다.
    /// </remarks>
    public sealed class SimulatedPressureSensor : SimulatedSlaveBase
    {
        /// <summary>압력값 레지스터 주소(잠정).</summary>
        public const ushort PressureRegister = 0x4001;

        /// <summary>장치 상태 레지스터 주소(잠정).</summary>
        public const ushort StatusRegister = 0x4002;

        /// <summary>1 LSB 당 압력 [Pa] (잠정: 0.1 Pa/LSB).</summary>
        public const double PaPerLsb = 0.1;

        private readonly PlantModel _plant;
        private readonly string _sensorId;

        /// <summary>차압센서 슬레이브를 생성한다.</summary>
        /// <param name="slaveId">슬레이브 주소.</param>
        /// <param name="plant">가상 플랜트.</param>
        /// <param name="sensorId">이 슬레이브가 대응하는 센서 ID.</param>
        public SimulatedPressureSensor(byte slaveId, PlantModel plant, string sensorId)
            : base(slaveId)
        {
            _plant = plant;
            _sensorId = sensorId;

            MapRead(PressureRegister, ReadPressure);
            MapRead(StatusRegister, ReadStatus);
        }

        /// <summary>압력 레지스터를 읽는다.</summary>
        /// <returns>부호 있는 16비트로 인코딩된 압력값.</returns>
        private ushort ReadPressure()
        {
            double pa;
            if (!_plant.TryGetPressure(_sensorId, out pa))
            {
                return 0;
            }

            return ToSignedRegister(pa, PaPerLsb);
        }

        /// <summary>상태 레지스터를 읽는다.</summary>
        /// <returns>0 = 정상.</returns>
        private ushort ReadStatus()
        {
            return 0;
        }
    }

    /// <summary>
    /// 스로틀밸브 시뮬레이션 슬레이브.
    /// 통신자료 「쓰로틀 밸브」 시트의 <b>실제 확정 주소</b>를 그대로 구현한다.
    /// </summary>
    /// <remarks>
    /// 0x1003(Motion status)과 0x2203(Alarm)의 <b>비트 정의는 미확정</b>이므로
    /// 0 = 정지, 1 = 이동중, 2 = 원점복귀중 으로 잠정 정의했다(Open Issue #5).
    /// </remarks>
    public sealed class SimulatedThrottleValve : SimulatedSlaveBase
    {
        /// <summary>명령 레지스터. 0x20 = Homing, 0x10 = PR0 Move, 0x40 = Quick Stop.</summary>
        public const ushort CommandRegister = 0x6002;

        /// <summary>PR0 위치 설정 레지스터 [pulse].</summary>
        public const ushort PositionSetRegister = 0x6202;

        /// <summary>PR0 속도 설정 레지스터 [RPM] (1~5).</summary>
        public const ushort VelocitySetRegister = 0x6203;

        /// <summary>알람 리셋 레지스터. 0x1111 을 쓴다.</summary>
        public const ushort AlarmResetRegister = 0x1801;

        /// <summary>현재 위치 레지스터 [pulse].</summary>
        public const ushort CurrentPositionRegister = 0x602B;

        /// <summary>알람 코드 레지스터.</summary>
        public const ushort AlarmRegister = 0x2203;

        /// <summary>모션 상태 레지스터.</summary>
        public const ushort MotionStatusRegister = 0x1003;

        /// <summary>원점 복귀 완료 레지스터.</summary>
        public const ushort HomeRegister = 0x0147;

        /// <summary>Homing 명령 값.</summary>
        public const ushort CommandHoming = 0x0020;

        /// <summary>PR0 Move 명령 값.</summary>
        public const ushort CommandPrMove = 0x0010;

        /// <summary>Quick Stop 명령 값.</summary>
        public const ushort CommandQuickStop = 0x0040;

        /// <summary>알람 리셋 값.</summary>
        public const ushort AlarmResetValue = 0x1111;

        private readonly PlantModel _plant;
        private readonly string _valveId;

        private ushort _pendingPositionPulse;
        private ushort _velocityRpm = 3;
        private ushort _alarmCode;

        /// <summary>스로틀밸브 슬레이브를 생성한다.</summary>
        /// <param name="slaveId">슬레이브 주소.</param>
        /// <param name="plant">가상 플랜트.</param>
        /// <param name="valveId">이 슬레이브가 대응하는 밸브 ID.</param>
        public SimulatedThrottleValve(byte slaveId, PlantModel plant, string valveId)
            : base(slaveId)
        {
            _plant = plant;
            _valveId = valveId;

            MapRead(CurrentPositionRegister, ReadCurrentPosition);
            MapRead(AlarmRegister, ReadAlarm);
            MapRead(MotionStatusRegister, ReadMotionStatus);
            MapRead(HomeRegister, ReadHome);
            MapRead(PositionSetRegister, ReadPositionSet);
            MapRead(VelocitySetRegister, ReadVelocitySet);

            MapWrite(CommandRegister, WriteCommand);
            MapWrite(PositionSetRegister, WritePositionSet);
            MapWrite(VelocitySetRegister, WriteVelocitySet);
            MapWrite(AlarmResetRegister, WriteAlarmReset);
        }

        /// <summary>강제로 알람을 발생시킨다. 인터록·알람 시나리오 테스트용.</summary>
        /// <param name="code">알람 코드. 0 이면 해제.</param>
        public void InjectAlarm(ushort code)
        {
            _alarmCode = code;
        }

        private ushort ReadCurrentPosition()
        {
            int pulse;
            int target;
            bool home;
            _plant.TryGetValve(_valveId, out pulse, out target, out home);
            return ToUnsignedRegister(pulse);
        }

        private ushort ReadAlarm()
        {
            return _alarmCode;
        }

        private ushort ReadMotionStatus()
        {
            int pulse;
            int target;
            bool home;
            if (!_plant.TryGetValve(_valveId, out pulse, out target, out home))
            {
                return 0;
            }

            // 잠정 정의: 목표에 도달했으면 정지(0), 아니면 이동중(1).
            return (ushort)(pulse == target ? 0 : 1);
        }

        private ushort ReadHome()
        {
            int pulse;
            int target;
            bool home;
            _plant.TryGetValve(_valveId, out pulse, out target, out home);
            return (ushort)(home ? 1 : 0);
        }

        private ushort ReadPositionSet()
        {
            return _pendingPositionPulse;
        }

        private ushort ReadVelocitySet()
        {
            return _velocityRpm;
        }

        /// <summary>명령 레지스터 쓰기를 처리한다.</summary>
        /// <param name="value">명령 값.</param>
        private void WriteCommand(ushort value)
        {
            switch (value)
            {
                case CommandHoming:
                    _plant.ApplyCommand(ActuatorCommand.HomeValve(_valveId, "시뮬레이션 Homing"));
                    break;

                case CommandPrMove:
                    // 실제 드라이브와 동일하게, 0x6202 에 미리 써 둔 값으로만 이동한다.
                    _plant.ApplyCommand(ActuatorCommand.SetValvePosition(
                        _valveId, _pendingPositionPulse, CommandPriority.Automatic, "시뮬레이션 PR0 Move"));
                    break;

                case CommandQuickStop:
                    _plant.ApplyCommand(new ActuatorCommand(
                        ActuatorTarget.Valve, _valveId, ActuatorCommandKind.QuickStopValve,
                        0.0, CommandPriority.Interlock, "시뮬레이션 Quick Stop"));
                    break;

                default:
                    // 정의되지 않은 명령은 무시한다(실제 드라이브도 대체로 무시한다).
                    break;
            }
        }

        private void WritePositionSet(ushort value)
        {
            _pendingPositionPulse = value;
        }

        private void WriteVelocitySet(ushort value)
        {
            _velocityRpm = value;
        }

        private void WriteAlarmReset(ushort value)
        {
            if (value == AlarmResetValue)
            {
                _alarmCode = 0;
            }
        }
    }

    /// <summary>
    /// 송풍팬 시뮬레이션 슬레이브(RS-485 Modbus 직결, 2026-07-31 설계 변경 반영).
    /// </summary>
    /// <remarks>
    /// <b>주소 전체가 잠정값이다</b>(DESIGN.md Open Issue #9).
    /// COMM_MAP.md 1.3 의 설계 요청대로 현재값·상태를 연속 주소에 배치해
    /// fast tier 트랜잭션 1회로 읽을 수 있는 형태를 가정했다.
    /// </remarks>
    public sealed class SimulatedBlowerFan : SimulatedSlaveBase
    {
        /// <summary>현재 회전수 레지스터(잠정).</summary>
        public const ushort CurrentRpmRegister = 0x4041;

        /// <summary>운전 상태 레지스터(잠정). 0 = 정지, 1 = 가감속, 2 = 정속.</summary>
        public const ushort RunStatusRegister = 0x4037;

        /// <summary>알람 코드 레지스터(잠정).</summary>
        public const ushort AlarmRegister = 0x4042;

        /// <summary>목표 회전수 설정 레지스터(잠정).</summary>
        public const ushort RpmSetRegister = 0x4006;

        /// <summary>기동/정지 명령 레지스터(잠정). 1 = 기동, 0 = 정지.</summary>
        public const ushort RunCommandRegister = 0x4034;

        /// <summary>알람 리셋 레지스터(잠정).</summary>
        public const ushort AlarmResetRegister = 0x4043;

        /// <summary>도달 판정 허용오차 [RPM].</summary>
        private const double RpmTolerance = 10.0;

        private readonly PlantModel _plant;
        private readonly string _fanId;
        private ushort _alarmCode;

        /// <summary>송풍팬 슬레이브를 생성한다.</summary>
        /// <param name="slaveId">슬레이브 주소.</param>
        /// <param name="plant">가상 플랜트.</param>
        /// <param name="fanId">이 슬레이브가 대응하는 팬 ID.</param>
        public SimulatedBlowerFan(byte slaveId, PlantModel plant, string fanId)
            : base(slaveId)
        {
            _plant = plant;
            _fanId = fanId;

            MapRead(CurrentRpmRegister, ReadCurrentRpm);
            MapRead(RunStatusRegister, ReadRunStatus);
            MapRead(AlarmRegister, ReadAlarm);
            MapRead(RpmSetRegister, ReadRpmSet);

            MapWrite(RpmSetRegister, WriteRpmSet);
            MapWrite(RunCommandRegister, WriteRunCommand);
            MapWrite(AlarmResetRegister, WriteAlarmReset);
        }

        /// <summary>강제로 알람을 발생시킨다.</summary>
        /// <param name="code">알람 코드. 0 이면 해제.</param>
        public void InjectAlarm(ushort code)
        {
            _alarmCode = code;
        }

        private ushort ReadCurrentRpm()
        {
            double rpm;
            double target;
            _plant.TryGetFan(_fanId, out rpm, out target);
            return ToUnsignedRegister(rpm);
        }

        private ushort ReadRunStatus()
        {
            double rpm;
            double target;
            if (!_plant.TryGetFan(_fanId, out rpm, out target))
            {
                return 0;
            }

            if (target <= 0.0 && rpm <= RpmTolerance)
            {
                return 0;
            }

            return (ushort)(System.Math.Abs(rpm - target) <= RpmTolerance ? 2 : 1);
        }

        private ushort ReadAlarm()
        {
            return _alarmCode;
        }

        private ushort ReadRpmSet()
        {
            double rpm;
            double target;
            _plant.TryGetFan(_fanId, out rpm, out target);
            return ToUnsignedRegister(target);
        }

        private void WriteRpmSet(ushort value)
        {
            _plant.ApplyCommand(ActuatorCommand.SetFanRpm(
                _fanId, value, CommandPriority.Automatic, "시뮬레이션 RPM 설정"));
        }

        private void WriteRunCommand(ushort value)
        {
            if (value == 0)
            {
                _plant.ApplyCommand(ActuatorCommand.StopFan(
                    _fanId, CommandPriority.Automatic, "시뮬레이션 정지"));
                return;
            }

            _plant.ApplyCommand(new ActuatorCommand(
                ActuatorTarget.Fan, _fanId, ActuatorCommandKind.StartFan,
                0.0, CommandPriority.Automatic, "시뮬레이션 기동"));
        }

        private void WriteAlarmReset(ushort value)
        {
            if (value != 0)
            {
                _alarmCode = 0;
            }
        }
    }

    /// <summary>
    /// PLC 디지털 입력·온도 슬레이브 시뮬레이션.
    /// </summary>
    /// <remarks>
    /// <para><b>이것이 없으면 시뮬레이션에서 장비가 기동하지 못한다.</b>
    /// <c>device-map.json</c> 에 PLC 가 있으면 <c>SafetyInputsConfigured</c> 가 참이 되고,
    /// 그 상태에서 PLC 가 무응답이면 IL-04(안전 입력 신뢰 불가)가 발동해
    /// 전체 정지에 들어간다. 즉 안전 판정은 옳게 동작하는데 시뮬레이션에
    /// 상대가 없어서 영구히 SafeStop 에 머문다.</para>
    /// <para>더 중요한 것은 <b>안전 입력 경로를 시뮬레이션에서 한 번도 검증하지
    /// 못한다</b>는 점이다. IL-02(EMO)·IL-04 는 이 슬레이브가 있어야 시험할 수 있다.</para>
    /// <para>레지스터 배치는 <c>device-map.json</c> 의 <c>LsXbmPlc</c> 정의를 따른다.
    /// 디지털 입력 8점이 0x000A 한 워드에 비트마스크로 들어오고,
    /// BLDC 온도 5점은 0x0064~, 센서 단선 5점은 0x006E~ 다.</para>
    /// </remarks>
    public sealed class SimulatedPlc : SimulatedSlaveBase
    {
        /// <summary>디지털 입력 워드 주소.</summary>
        public const ushort DigitalRegister = 0x000A;

        /// <summary>BLDC 온도 시작 주소(5워드).</summary>
        public const ushort FanTemperatureRegister = 0x0064;

        /// <summary>온도센서 단선 시작 주소(5워드).</summary>
        public const ushort TemperatureFaultRegister = 0x006E;

        /// <summary>온도 1 LSB 당 섭씨(잠정 1 ℃/LSB).</summary>
        public const double CelsiusPerLsb = 1.0;

        private readonly double[] _fanTemperatures = new double[5];
        private readonly bool[] _fanStop = new bool[5];
        private readonly bool[] _temperatureFault = new bool[5];

        private bool _emo;
        private bool _controlBoxFanTop;
        private bool _controlBoxFanBottom;

        /// <summary>PLC 슬레이브를 생성한다.</summary>
        /// <param name="slaveId">슬레이브 주소.</param>
        public SimulatedPlc(byte slaveId)
            : base(slaveId)
        {
            // 정상 운전 상태로 시작한다. 모든 입력이 서 있으면 기동 즉시 정지한다.
            for (int i = 0; i < 5; i++)
            {
                _fanTemperatures[i] = 36.0 + i;
            }

            MapRead(DigitalRegister, ReadDigital);

            for (int i = 0; i < 5; i++)
            {
                int index = i;

                MapRead((ushort)(FanTemperatureRegister + i),
                    () => ToSignedRegister(_fanTemperatures[index], CelsiusPerLsb));

                MapRead((ushort)(TemperatureFaultRegister + i),
                    () => (ushort)(_temperatureFault[index] ? 1 : 0));
            }

            // temperatures 그룹은 0x0064 부터 15워드를 한 번에 읽는다.
            // 사이 공백(0x0069~0x006D)도 응답해야 트랜잭션이 성립한다.
            for (ushort a = FanTemperatureRegister + 5; a < TemperatureFaultRegister; a++)
            {
                MapRead(a, () => 0);
            }
        }

        /// <summary>비상정지 입력을 설정한다.</summary>
        /// <param name="active">작동 상태이면 true.</param>
        public void SetEmo(bool active)
        {
            _emo = active;
        }

        /// <summary>송풍팬 정지 입력을 설정한다.</summary>
        /// <param name="index">팬 번호(0~4).</param>
        /// <param name="stopped">정지 상태이면 true.</param>
        public void SetFanStop(int index, bool stopped)
        {
            if (index >= 0 && index < _fanStop.Length)
            {
                _fanStop[index] = stopped;
            }
        }

        /// <summary>제어함 냉각팬 입력을 설정한다.</summary>
        /// <param name="top">상부 정지 여부.</param>
        /// <param name="bottom">하부 정지 여부.</param>
        public void SetControlBoxFan(bool top, bool bottom)
        {
            _controlBoxFanTop = top;
            _controlBoxFanBottom = bottom;
        }

        /// <summary>BLDC 온도를 설정한다.</summary>
        /// <param name="index">팬 번호(0~4).</param>
        /// <param name="celsius">온도 [℃].</param>
        public void SetFanTemperature(int index, double celsius)
        {
            if (index >= 0 && index < _fanTemperatures.Length)
            {
                _fanTemperatures[index] = celsius;
            }
        }

        /// <summary>디지털 입력 워드를 만든다.</summary>
        /// <returns>비트마스크.</returns>
        private ushort ReadDigital()
        {
            int word = 0;

            if (_emo)
            {
                word |= 1 << 0;
            }

            for (int i = 0; i < 5; i++)
            {
                if (_fanStop[i])
                {
                    word |= 1 << (i + 1);
                }
            }

            if (_controlBoxFanTop)
            {
                word |= 1 << 6;
            }

            if (_controlBoxFanBottom)
            {
                word |= 1 << 7;
            }

            return (ushort)word;
        }
    }
}
