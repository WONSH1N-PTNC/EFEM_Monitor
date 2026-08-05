using System;
using System.Collections.Generic;
using System.Globalization;
using Esam.Communication.Abstractions;
using Esam.Communication.Configuration;
using Esam.Domain.Control;
using Esam.Domain.Units;

namespace Esam.Communication.Polling
{
    /// <summary>
    /// 도메인 지령을 Modbus 요청 시퀀스로 변환한다.
    /// </summary>
    public interface ICommandTranslator
    {
        /// <summary>지령을 요청 시퀀스로 변환한다.</summary>
        /// <param name="command">도메인 지령.</param>
        /// <param name="device">대상 디바이스 런타임.</param>
        /// <param name="requests">생성된 요청 시퀀스(순서대로 실행해야 한다).</param>
        /// <param name="reason">변환 실패 사유. 성공 시 null.</param>
        /// <returns>변환에 성공하면 true.</returns>
        bool TryTranslate(
            ActuatorCommand command,
            DeviceRuntime device,
            out IList<ModbusRequest> requests,
            out string reason);
    }

    /// <summary>
    /// device-map.json 의 <c>commands</c> 선언을 이용한 지령 변환기.
    /// </summary>
    /// <remarks>
    /// <para>핵심은 <b>밸브의 2단계 시퀀스</b>다. 통신자료 규정상 스로틀밸브는
    /// <c>0x6202</c> 에 목표 위치를 먼저 쓰고, 그 다음 <c>0x6002 ← 0x10</c>(PR0 Move)을
    /// 써야 실제로 움직인다. 한 지령이 두 트랜잭션으로 펼쳐지는 것이므로
    /// 이 순서가 깨지면 밸브가 엉뚱한 위치로 가거나 아예 움직이지 않는다.</para>
    /// <para>명령 이름과 매직 넘버를 설정에 두었기 때문에, 장치 개정으로 값이 바뀌어도
    /// 이 클래스는 수정할 필요가 없다.</para>
    /// </remarks>
    public sealed class DeclarativeCommandTranslator : ICommandTranslator
    {
        /// <summary>밸브 목표 위치 설정 명령 이름.</summary>
        public const string CommandSetPosition = "setPosition";

        /// <summary>밸브 PR0 이동 실행 명령 이름.</summary>
        public const string CommandPrMove = "prMove";

        /// <summary>밸브 원점 복귀 명령 이름.</summary>
        public const string CommandHoming = "homing";

        /// <summary>밸브 즉시 정지 명령 이름.</summary>
        public const string CommandQuickStop = "quickStop";

        /// <summary>팬 목표 회전수 설정 명령 이름.</summary>
        public const string CommandSetRpm = "setRpm";

        /// <summary>팬 기동 명령 이름.</summary>
        public const string CommandStart = "start";

        /// <summary>팬 정지 명령 이름.</summary>
        public const string CommandStop = "stop";

        /// <inheritdoc />
        public bool TryTranslate(
            ActuatorCommand command,
            DeviceRuntime device,
            out IList<ModbusRequest> requests,
            out string reason)
        {
            requests = null;
            reason = null;

            if (command == null)
            {
                reason = "지령이 null 입니다.";
                return false;
            }

            if (device == null)
            {
                reason = string.Format(
                    CultureInfo.InvariantCulture, "지령 대상 '{0}' 를 찾을 수 없습니다.", command.DeviceId);
                return false;
            }

            switch (command.Kind)
            {
                case ActuatorCommandKind.SetValvePosition:
                    return TryBuildValveMove(device, command.Value, out requests, out reason);

                case ActuatorCommandKind.CloseValve:
                    // 인터록 동작. 목표 위치 0(완전 닫힘) 으로 이동시킨다.
                    return TryBuildValveMove(device, 0.0, out requests, out reason);

                case ActuatorCommandKind.QuickStopValve:
                    return TryBuildSingle(device, CommandQuickStop, 0.0, out requests, out reason);

                case ActuatorCommandKind.HomeValve:
                    return TryBuildSingle(device, CommandHoming, 0.0, out requests, out reason);

                case ActuatorCommandKind.SetFanRpm:
                    return TryBuildFanRpm(device, command.Value, out requests, out reason);

                case ActuatorCommandKind.StopFan:
                    return TryBuildFanStop(device, out requests, out reason);

                case ActuatorCommandKind.StartFan:
                    return TryBuildSingle(device, CommandStart, 0.0, out requests, out reason);

                default:
                    reason = string.Format(
                        CultureInfo.InvariantCulture, "지원하지 않는 지령 종류입니다: {0}", command.Kind);
                    return false;
            }
        }

        /// <summary>
        /// 밸브 이동 시퀀스를 만든다. 위치 설정 → PR0 Move 순서를 반드시 지킨다.
        /// </summary>
        /// <param name="device">밸브 디바이스.</param>
        /// <param name="targetPulse">목표 위치 [pulse].</param>
        /// <param name="requests">생성된 요청 시퀀스.</param>
        /// <param name="reason">실패 사유.</param>
        /// <returns>성공하면 true.</returns>
        private static bool TryBuildValveMove(
            DeviceRuntime device, double targetPulse, out IList<ModbusRequest> requests, out string reason)
        {
            requests = null;
            reason = null;

            CommandDefinition setPosition = device.Type.FindCommand(CommandSetPosition);
            CommandDefinition prMove = device.Type.FindCommand(CommandPrMove);

            if (setPosition == null || prMove == null)
            {
                reason = string.Format(
                    CultureInfo.InvariantCulture,
                    "device '{0}': 밸브 이동에는 '{1}' 와 '{2}' 명령 정의가 모두 필요합니다.",
                    device.DeviceId, CommandSetPosition, CommandPrMove);
                return false;
            }

            // 설정된 pulse 한계로 제한한다. 상위 계층이 이미 클램프하지만
            // 여기서도 막아 두어야 수동 조작이나 설정 실수로 범위를 넘기지 않는다.
            int maxPulse = device.Type.Conversion == null ? 5000 : device.Type.Conversion.PulsePerFullOpen;
            double clamped = targetPulse;

            if (clamped < 0.0)
            {
                clamped = 0.0;
            }
            else if (clamped > maxPulse)
            {
                clamped = maxPulse;
            }

            ModbusRequest positionRequest;
            if (!setPosition.TryBuildRequest(device.SlaveId, clamped, out positionRequest))
            {
                reason = string.Format(
                    CultureInfo.InvariantCulture,
                    "device '{0}': '{1}' 명령의 주소/값이 미확정입니다.", device.DeviceId, CommandSetPosition);
                return false;
            }

            ModbusRequest moveRequest;
            if (!prMove.TryBuildRequest(device.SlaveId, 0.0, out moveRequest))
            {
                reason = string.Format(
                    CultureInfo.InvariantCulture,
                    "device '{0}': '{1}' 명령의 주소/값이 미확정입니다.", device.DeviceId, CommandPrMove);
                return false;
            }

            requests = new List<ModbusRequest> { positionRequest, moveRequest };
            return true;
        }

        /// <summary>팬 회전수 설정 요청을 만든다.</summary>
        /// <param name="device">팬 디바이스.</param>
        /// <param name="targetRpm">목표 회전수 [RPM].</param>
        /// <param name="requests">생성된 요청 시퀀스.</param>
        /// <param name="reason">실패 사유.</param>
        /// <returns>성공하면 true.</returns>
        private static bool TryBuildFanRpm(
            DeviceRuntime device, double targetRpm, out IList<ModbusRequest> requests, out string reason)
        {
            requests = null;
            reason = null;

            DeviceConversion conversion = device.Type.Conversion;

            if (conversion != null && conversion.MaxRpm > 0.0 && targetRpm > conversion.MaxRpm)
            {
                targetRpm = conversion.MaxRpm;
            }

            if (targetRpm < 0.0)
            {
                targetRpm = 0.0;
            }

            return TryBuildSingle(device, CommandSetRpm, targetRpm, out requests, out reason);
        }

        /// <summary>
        /// 팬 정지 요청을 만든다. 전용 stop 명령이 없으면 회전수 0 설정으로 대체한다.
        /// </summary>
        /// <param name="device">팬 디바이스.</param>
        /// <param name="requests">생성된 요청 시퀀스.</param>
        /// <param name="reason">실패 사유.</param>
        /// <returns>성공하면 true.</returns>
        private static bool TryBuildFanStop(
            DeviceRuntime device, out IList<ModbusRequest> requests, out string reason)
        {
            if (device.Type.FindCommand(CommandStop) != null)
            {
                return TryBuildSingle(device, CommandStop, 0.0, out requests, out reason);
            }

            // 안전 정지 경로이므로 전용 명령이 없다는 이유로 실패시키지 않는다.
            return TryBuildSingle(device, CommandSetRpm, 0.0, out requests, out reason);
        }

        /// <summary>단일 명령 요청을 만든다.</summary>
        /// <param name="device">대상 디바이스.</param>
        /// <param name="commandName">명령 이름.</param>
        /// <param name="argument">명령 인자.</param>
        /// <param name="requests">생성된 요청 시퀀스.</param>
        /// <param name="reason">실패 사유.</param>
        /// <returns>성공하면 true.</returns>
        private static bool TryBuildSingle(
            DeviceRuntime device,
            string commandName,
            double argument,
            out IList<ModbusRequest> requests,
            out string reason)
        {
            requests = null;
            reason = null;

            CommandDefinition definition = device.Type.FindCommand(commandName);
            if (definition == null)
            {
                reason = string.Format(
                    CultureInfo.InvariantCulture,
                    "device '{0}': 명령 '{1}' 이 정의되어 있지 않습니다.", device.DeviceId, commandName);
                return false;
            }

            ModbusRequest request;
            if (!definition.TryBuildRequest(device.SlaveId, argument, out request))
            {
                reason = string.Format(
                    CultureInfo.InvariantCulture,
                    "device '{0}': 명령 '{1}' 의 주소/값이 미확정입니다.", device.DeviceId, commandName);
                return false;
            }

            requests = new List<ModbusRequest> { request };
            return true;
        }

        /// <summary>
        /// 개도율(%)을 pulse 로 변환한다. 수동 조작 화면이 % 단위를 쓰기 때문에 필요하다.
        /// </summary>
        /// <param name="device">밸브 디바이스.</param>
        /// <param name="percent">개도율 [%].</param>
        /// <returns>변환된 pulse 값.</returns>
        public static int PercentToPulse(DeviceRuntime device, double percent)
        {
            DeviceConversion conversion = device == null ? null : device.Type.Conversion;

            ValvePulseConverter converter = conversion == null
                ? ValvePulseConverter.Default
                : new ValvePulseConverter(conversion.PulsePerFullOpen, conversion.FullOpenDegree);

            return converter.PercentToPulse(percent);
        }
    }
}
