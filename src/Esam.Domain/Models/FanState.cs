using System;

namespace Esam.Domain.Models
{
    /// <summary>송풍팬 운전 상태.</summary>
    public enum FanRunStatus
    {
        /// <summary>판정 불가(통신 실패 또는 레지스터 정의 미확정).</summary>
        Unknown = 0,

        /// <summary>정지.</summary>
        Stopped = 1,

        /// <summary>가감속 중(목표 RPM 미도달).</summary>
        Ramping = 2,

        /// <summary>정속 운전(목표 RPM 도달).</summary>
        Running = 3,

        /// <summary>알람 발생.</summary>
        Fault = 4
    }

    /// <summary>
    /// 송풍팬 1대의 상태 스냅샷. 불변 객체이다.
    /// 2026-07-31 설계 변경으로 CAN 이 아닌 RS-485 Modbus RTU 직결 장치이다.
    /// </summary>
    public sealed class FanState
    {
        /// <summary>팬 식별자(예: "F-1").</summary>
        public string Id { get; private set; }

        /// <summary>현재 회전수 [RPM].</summary>
        public double Rpm { get; private set; }

        /// <summary>지령된 목표 회전수 [RPM].</summary>
        public double TargetRpm { get; private set; }

        /// <summary>운전 상태.</summary>
        public FanRunStatus RunStatus { get; private set; }

        /// <summary>알람 코드. 0 이면 정상.</summary>
        public ushort AlarmCode { get; private set; }

        /// <summary>통신 품질.</summary>
        public Quality Quality { get; private set; }

        /// <summary>마지막 성공 갱신 시각(UTC).</summary>
        public DateTime LastUpdateUtc { get; private set; }

        /// <summary>회전 중인지 여부.</summary>
        public bool IsRunning
        {
            get { return RunStatus == FanRunStatus.Running || RunStatus == FanRunStatus.Ramping; }
        }

        /// <summary>알람이 발생한 상태인지 여부.</summary>
        public bool HasAlarm
        {
            get { return AlarmCode != 0 || RunStatus == FanRunStatus.Fault; }
        }

        /// <summary>제어 지령을 받을 수 있는 상태인지 여부.</summary>
        public bool IsControllable
        {
            get { return Quality == Quality.Good && !HasAlarm; }
        }

        /// <summary>송풍팬 상태를 생성한다.</summary>
        /// <param name="id">팬 식별자.</param>
        /// <param name="rpm">현재 회전수 [RPM].</param>
        /// <param name="targetRpm">목표 회전수 [RPM].</param>
        /// <param name="runStatus">운전 상태.</param>
        /// <param name="alarmCode">알람 코드.</param>
        /// <param name="quality">통신 품질.</param>
        /// <param name="lastUpdateUtc">마지막 성공 갱신 시각(UTC).</param>
        public FanState(
            string id,
            double rpm,
            double targetRpm,
            FanRunStatus runStatus,
            ushort alarmCode,
            Quality quality,
            DateTime lastUpdateUtc)
        {
            if (string.IsNullOrEmpty(id))
            {
                throw new ArgumentException("팬 식별자는 비어 있을 수 없습니다.", "id");
            }

            Id = id;
            Rpm = rpm;
            TargetRpm = targetRpm;
            RunStatus = runStatus;
            AlarmCode = alarmCode;
            Quality = quality;
            LastUpdateUtc = lastUpdateUtc;
        }

        /// <summary>아직 데이터를 수신하지 못한 초기 상태의 팬 상태를 만든다.</summary>
        /// <param name="id">팬 식별자.</param>
        /// <returns><see cref="Models.Quality.NoData"/> 상태의 팬 상태.</returns>
        public static FanState NoData(string id)
        {
            return new FanState(id, 0.0, 0.0, FanRunStatus.Unknown, 0, Quality.NoData, DateTime.MinValue);
        }
    }
}
