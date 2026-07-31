using System;

namespace Esam.Domain.Models
{
    /// <summary>
    /// 스로틀밸브 구동 상태. 통신자료 「쓰로틀 밸브」 시트의 Read 레지스터
    /// (0x602B 현재위치 / 0x1003 Motion status / 0x2203 Alarm / 0x0147 HOME)에 대응한다.
    /// </summary>
    public enum ValveMotionStatus
    {
        /// <summary>판정 불가(통신 실패 또는 비트 정의 미확정).</summary>
        Unknown = 0,

        /// <summary>정지(목표 위치 도달).</summary>
        Idle = 1,

        /// <summary>이동 중.</summary>
        Moving = 2,

        /// <summary>원점 복귀 진행 중.</summary>
        Homing = 3,

        /// <summary>드라이브 알람 발생.</summary>
        Fault = 4
    }

    /// <summary>
    /// 스로틀밸브 1대의 상태 스냅샷. 불변 객체이다.
    /// </summary>
    public sealed class ValveState
    {
        /// <summary>밸브 식별자(예: "V-1").</summary>
        public string Id { get; private set; }

        /// <summary>현재 위치 [pulse]. 0 = 완전 닫힘(0도), 5000 = 완전 열림(90도).</summary>
        public int PositionPulse { get; private set; }

        /// <summary>지령된 목표 위치 [pulse].</summary>
        public int TargetPulse { get; private set; }

        /// <summary>현재 개도율 [%] (0~100). <see cref="PositionPulse"/> 환산값.</summary>
        public double PositionPercent { get; private set; }

        /// <summary>현재 개도각 [도] (0~90). <see cref="PositionPulse"/> 환산값.</summary>
        public double PositionDegree { get; private set; }

        /// <summary>모션 상태.</summary>
        public ValveMotionStatus MotionStatus { get; private set; }

        /// <summary>드라이브 알람 코드(0x2203). 0 이면 정상.</summary>
        public ushort AlarmCode { get; private set; }

        /// <summary>원점 복귀 완료 여부(0x0147). 전원 ON 후 Homing 이 완료되어야 제어를 시작할 수 있다.</summary>
        public bool IsHomeDone { get; private set; }

        /// <summary>통신 품질.</summary>
        public Quality Quality { get; private set; }

        /// <summary>마지막 성공 갱신 시각(UTC).</summary>
        public DateTime LastUpdateUtc { get; private set; }

        /// <summary>알람이 발생한 상태인지 여부.</summary>
        public bool HasAlarm
        {
            get { return AlarmCode != 0 || MotionStatus == ValveMotionStatus.Fault; }
        }

        /// <summary>제어 지령을 받을 수 있는 상태인지 여부(통신 정상 + 원점 완료 + 무알람).</summary>
        public bool IsControllable
        {
            get { return Quality == Quality.Good && IsHomeDone && !HasAlarm; }
        }

        /// <summary>스로틀밸브 상태를 생성한다.</summary>
        /// <param name="id">밸브 식별자.</param>
        /// <param name="positionPulse">현재 위치 [pulse].</param>
        /// <param name="targetPulse">목표 위치 [pulse].</param>
        /// <param name="positionPercent">현재 개도율 [%].</param>
        /// <param name="positionDegree">현재 개도각 [도].</param>
        /// <param name="motionStatus">모션 상태.</param>
        /// <param name="alarmCode">드라이브 알람 코드.</param>
        /// <param name="isHomeDone">원점 복귀 완료 여부.</param>
        /// <param name="quality">통신 품질.</param>
        /// <param name="lastUpdateUtc">마지막 성공 갱신 시각(UTC).</param>
        public ValveState(
            string id,
            int positionPulse,
            int targetPulse,
            double positionPercent,
            double positionDegree,
            ValveMotionStatus motionStatus,
            ushort alarmCode,
            bool isHomeDone,
            Quality quality,
            DateTime lastUpdateUtc)
        {
            if (string.IsNullOrEmpty(id))
            {
                throw new ArgumentException("밸브 식별자는 비어 있을 수 없습니다.", "id");
            }

            Id = id;
            PositionPulse = positionPulse;
            TargetPulse = targetPulse;
            PositionPercent = positionPercent;
            PositionDegree = positionDegree;
            MotionStatus = motionStatus;
            AlarmCode = alarmCode;
            IsHomeDone = isHomeDone;
            Quality = quality;
            LastUpdateUtc = lastUpdateUtc;
        }

        /// <summary>아직 데이터를 수신하지 못한 초기 상태의 밸브 상태를 만든다.</summary>
        /// <param name="id">밸브 식별자.</param>
        /// <returns><see cref="Models.Quality.NoData"/> 상태의 밸브 상태.</returns>
        public static ValveState NoData(string id)
        {
            return new ValveState(
                id, 0, 0, 0.0, 0.0, ValveMotionStatus.Unknown, 0, false, Quality.NoData, DateTime.MinValue);
        }
    }
}
