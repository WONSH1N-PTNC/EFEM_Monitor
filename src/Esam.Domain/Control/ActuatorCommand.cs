using System;
using System.Globalization;

namespace Esam.Domain.Control
{
    /// <summary>
    /// 액추에이터 지령 1건. 도메인 계층은 이 값 객체만 생성하고,
    /// 실제 Modbus 프레임 변환과 송신은 통신 계층이 담당한다.
    /// </summary>
    /// <remarks>
    /// 도메인이 하드웨어를 직접 알지 못하게 하는 경계이다.
    /// 덕분에 제어 알고리즘 테스트는 "어떤 지령이 생성되었는가"만 검증하면 된다.
    /// </remarks>
    public sealed class ActuatorCommand
    {
        /// <summary>지령 대상 종류.</summary>
        public ActuatorTarget Target { get; private set; }

        /// <summary>지령 대상 디바이스 ID(예: "V-1", "F-3").</summary>
        public string DeviceId { get; private set; }

        /// <summary>지령 종류.</summary>
        public ActuatorCommandKind Kind { get; private set; }

        /// <summary>지령 값. 밸브는 [pulse], 팬은 [RPM]. 값이 필요 없는 지령은 0.</summary>
        public double Value { get; private set; }

        /// <summary>처리 우선순위. 포트 워커의 우선순위 큐 정렬에 사용한다.</summary>
        public CommandPriority Priority { get; private set; }

        /// <summary>지령 발생 사유. 감사 로그와 디버깅에 사용한다.</summary>
        public string Reason { get; private set; }

        /// <summary>액추에이터 지령을 생성한다.</summary>
        /// <param name="target">지령 대상 종류.</param>
        /// <param name="deviceId">대상 디바이스 ID.</param>
        /// <param name="kind">지령 종류.</param>
        /// <param name="value">지령 값.</param>
        /// <param name="priority">처리 우선순위.</param>
        /// <param name="reason">지령 발생 사유.</param>
        /// <exception cref="ArgumentException">디바이스 ID 가 비어 있을 때.</exception>
        public ActuatorCommand(
            ActuatorTarget target,
            string deviceId,
            ActuatorCommandKind kind,
            double value,
            CommandPriority priority,
            string reason)
        {
            if (string.IsNullOrEmpty(deviceId))
            {
                throw new ArgumentException("지령 대상 디바이스 ID 는 비어 있을 수 없습니다.", "deviceId");
            }

            Target = target;
            DeviceId = deviceId;
            Kind = kind;
            Value = value;
            Priority = priority;
            Reason = reason;
        }

        /// <summary>밸브 목표 위치 지령을 만든다.</summary>
        /// <param name="valveId">밸브 ID.</param>
        /// <param name="targetPulse">목표 위치 [pulse].</param>
        /// <param name="priority">처리 우선순위.</param>
        /// <param name="reason">지령 사유.</param>
        /// <returns>생성된 지령.</returns>
        public static ActuatorCommand SetValvePosition(
            string valveId, int targetPulse, CommandPriority priority, string reason)
        {
            return new ActuatorCommand(
                ActuatorTarget.Valve, valveId, ActuatorCommandKind.SetValvePosition,
                targetPulse, priority, reason);
        }

        /// <summary>밸브 완전 닫힘 지령을 만든다. 인터록 동작에 사용한다.</summary>
        /// <param name="valveId">밸브 ID.</param>
        /// <param name="priority">처리 우선순위.</param>
        /// <param name="reason">지령 사유.</param>
        /// <returns>생성된 지령.</returns>
        public static ActuatorCommand CloseValve(string valveId, CommandPriority priority, string reason)
        {
            return new ActuatorCommand(
                ActuatorTarget.Valve, valveId, ActuatorCommandKind.CloseValve, 0.0, priority, reason);
        }

        /// <summary>밸브 원점 복귀 지령을 만든다.</summary>
        /// <param name="valveId">밸브 ID.</param>
        /// <param name="reason">지령 사유.</param>
        /// <returns>생성된 지령.</returns>
        public static ActuatorCommand HomeValve(string valveId, string reason)
        {
            return new ActuatorCommand(
                ActuatorTarget.Valve, valveId, ActuatorCommandKind.HomeValve, 0.0,
                CommandPriority.Manual, reason);
        }

        /// <summary>팬 목표 회전수 지령을 만든다.</summary>
        /// <param name="fanId">팬 ID.</param>
        /// <param name="targetRpm">목표 회전수 [RPM].</param>
        /// <param name="priority">처리 우선순위.</param>
        /// <param name="reason">지령 사유.</param>
        /// <returns>생성된 지령.</returns>
        public static ActuatorCommand SetFanRpm(
            string fanId, double targetRpm, CommandPriority priority, string reason)
        {
            return new ActuatorCommand(
                ActuatorTarget.Fan, fanId, ActuatorCommandKind.SetFanRpm, targetRpm, priority, reason);
        }

        /// <summary>팬 정지 지령을 만든다.</summary>
        /// <param name="fanId">팬 ID.</param>
        /// <param name="priority">처리 우선순위.</param>
        /// <param name="reason">지령 사유.</param>
        /// <returns>생성된 지령.</returns>
        public static ActuatorCommand StopFan(string fanId, CommandPriority priority, string reason)
        {
            return new ActuatorCommand(
                ActuatorTarget.Fan, fanId, ActuatorCommandKind.StopFan, 0.0, priority, reason);
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "[{0}] {1} {2} = {3} ({4})",
                Priority, DeviceId, Kind, Value, Reason);
        }
    }
}
