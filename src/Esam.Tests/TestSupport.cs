using System;
using System.Collections.Generic;
using Esam.Domain;
using Esam.Domain.Configuration;
using Esam.Domain.Control;
using Esam.Domain.Models;

namespace Esam.Tests
{
    /// <summary>테스트에서 시간을 임의로 조작할 수 있는 가상 시계.</summary>
    internal sealed class FakeClock : IClock
    {
        public FakeClock(DateTime startUtc)
        {
            UtcNow = startUtc;
        }

        public DateTime UtcNow { get; private set; }

        /// <summary>지정한 밀리초만큼 시간을 진행시킨다.</summary>
        public void AdvanceMs(double milliseconds)
        {
            UtcNow = UtcNow.AddMilliseconds(milliseconds);
        }
    }

    /// <summary>테스트 데이터 생성 도우미.</summary>
    internal static class Build
    {
        public static readonly DateTime T0 = new DateTime(2026, 7, 31, 0, 0, 0, DateTimeKind.Utc);

        public static PressureReading Pressure(string id, double pa, Quality quality = Quality.Good)
        {
            return new PressureReading(id, pa, pa, 0, 0.0, quality, T0);
        }

        public static ValveState Valve(
            string id,
            int pulse,
            Quality quality = Quality.Good,
            bool homeDone = true,
            ushort alarmCode = 0,
            int? targetPulse = null)
        {
            return new ValveState(
                id,
                pulse,
                targetPulse ?? pulse,
                pulse / 5000.0 * 100.0,
                pulse / 5000.0 * 90.0,
                ValveMotionStatus.Idle,
                alarmCode,
                homeDone,
                quality,
                T0);
        }

        public static FanState Fan(
            string id,
            double rpm,
            double targetRpm = 0.0,
            Quality quality = Quality.Good,
            ushort alarmCode = 0,
            FanRunStatus status = FanRunStatus.Running)
        {
            return new FanState(id, rpm, targetRpm, status, alarmCode, quality, T0);
        }

        public static PlcDigitalState Plc(
            bool emo = false,
            bool door = false,
            bool breakerOff = false,
            Quality quality = Quality.Good)
        {
            return new PlcDigitalState(new bool[5], false, emo, door, breakerOff, quality, T0);
        }

        /// <summary>체인 5조를 갖춘 기본 제어 설정을 만든다.</summary>
        public static ControlConfig Config(SensorMode mode = SensorMode.Sensor2)
        {
            ControlConfig config = new ControlConfig();
            config.ActiveMode = mode;

            // 안전 입력 PLC 가 배선된 상태를 가정한다.
            // 이 값이 false 면 IL-04(안전 입력 신뢰 불가)가 판정되지 않는다.
            config.SafetyInputsConfigured = true;

            // 팬 사양 미확보 기본값(MaxRpm=0)으로는 증속 테스트가 불가하므로 값을 채운다.
            config.Fan.MaxRpm = 3000.0;
            config.Fan.MinRpm = 0.0;
            config.Fan.StepRpm = 100.0;
            config.Fan.DwellMs = 0;
            config.Valve.DwellMs = 0;

            List<ChainDefinition> chains = new List<ChainDefinition>();
            for (int i = 1; i <= 5; i++)
            {
                chains.Add(new ChainDefinition
                {
                    Id = i,
                    Name = "Chain 2-" + i,
                    Sensor1Id = "S1-1",
                    Sensor2Id = "S2-" + i,
                    Sensor3Id = "S3-" + i,
                    ValveId = "V-" + i,
                    FanId = "F-" + i,
                    Enabled = true
                });
            }

            config.Chains = chains;
            return config;
        }

        /// <summary>단일 체인 제어 컨텍스트를 만든다.</summary>
        public static ChainControlContext Context(
            ChainRuntime runtime,
            double pv,
            ValveState valve,
            FanState fan,
            ControlConfig config,
            SensorMode mode,
            DateTime nowUtc,
            Quality pvQuality = Quality.Good)
        {
            return new ChainControlContext(
                runtime, pv, pvQuality, valve, fan,
                config.GetMode(mode), config.Valve, config.Fan, nowUtc);
        }

        /// <summary>지정한 센서/밸브/팬만 담긴 스냅샷을 만든다.</summary>
        public static SystemSnapshot Snapshot(
            IDictionary<string, PressureReading> pressures = null,
            IDictionary<string, ValveState> valves = null,
            IDictionary<string, FanState> fans = null,
            PlcDigitalState plc = null,
            AuxiliaryReadings aux = null)
        {
            return new SystemSnapshot(T0, pressures, valves, fans, plc ?? Plc(), aux, null, null);
        }
    }
}
