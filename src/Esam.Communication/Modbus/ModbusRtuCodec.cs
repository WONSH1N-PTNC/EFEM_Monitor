using System;
using System.Globalization;
using Esam.Communication.Abstractions;

namespace Esam.Communication.Modbus
{
    /// <summary>응답 프레임 파싱 결과.</summary>
    public sealed class FrameParseResult
    {
        /// <summary>파싱 성공 여부.</summary>
        public bool IsSuccess { get; private set; }

        /// <summary>실패 원인.</summary>
        public ModbusFailureKind FailureKind { get; private set; }

        /// <summary>슬레이브 예외 코드.</summary>
        public ModbusExceptionCode ExceptionCode { get; private set; }

        /// <summary>파싱된 레지스터 값.</summary>
        public ushort[] Registers { get; private set; }

        /// <summary>실패 상세 설명.</summary>
        public string Detail { get; private set; }

        private FrameParseResult(
            bool isSuccess,
            ModbusFailureKind failureKind,
            ModbusExceptionCode exceptionCode,
            ushort[] registers,
            string detail)
        {
            IsSuccess = isSuccess;
            FailureKind = failureKind;
            ExceptionCode = exceptionCode;
            Registers = registers;
            Detail = detail;
        }

        /// <summary>성공 결과를 만든다.</summary>
        /// <param name="registers">파싱된 레지스터(쓰기 응답이면 빈 배열).</param>
        /// <returns>성공 결과.</returns>
        public static FrameParseResult Ok(ushort[] registers)
        {
            return new FrameParseResult(
                true, ModbusFailureKind.None, ModbusExceptionCode.None,
                registers ?? new ushort[0], null);
        }

        /// <summary>실패 결과를 만든다.</summary>
        /// <param name="kind">실패 원인.</param>
        /// <param name="detail">상세 설명.</param>
        /// <returns>실패 결과.</returns>
        public static FrameParseResult Fail(ModbusFailureKind kind, string detail)
        {
            return new FrameParseResult(false, kind, ModbusExceptionCode.None, null, detail);
        }

        /// <summary>슬레이브 예외 응답 결과를 만든다.</summary>
        /// <param name="code">예외 코드.</param>
        /// <returns>예외 결과.</returns>
        public static FrameParseResult Exception(ModbusExceptionCode code)
        {
            return new FrameParseResult(
                false, ModbusFailureKind.ExceptionResponse, code, null,
                string.Format(CultureInfo.InvariantCulture,
                    "슬레이브 예외 0x{0:X2} ({1})", (byte)code, code));
        }
    }

    /// <summary>
    /// Modbus RTU 프레임 인코더/디코더. 전송 매체와 무관한 순수 함수 집합이므로
    /// 하드웨어 없이 전수 테스트할 수 있다.
    /// </summary>
    public static class ModbusRtuCodec
    {
        /// <summary>예외 응답을 나타내는 함수코드 비트마스크.</summary>
        public const byte ExceptionFlag = 0x80;

        /// <summary>요청 프레임을 조립한다.</summary>
        /// <param name="request">요청.</param>
        /// <returns>CRC 를 포함한 완성된 RTU 프레임.</returns>
        /// <exception cref="ArgumentNullException">요청이 null 일 때.</exception>
        public static byte[] BuildRequest(ModbusRequest request)
        {
            if (request == null)
            {
                throw new ArgumentNullException("request");
            }

            switch (request.FunctionCode)
            {
                case ModbusFunctionCode.ReadHoldingRegisters:
                case ModbusFunctionCode.ReadInputRegisters:
                    return BuildReadRequest(request);

                case ModbusFunctionCode.WriteSingleRegister:
                    return BuildWriteSingleRequest(request);

                case ModbusFunctionCode.WriteMultipleRegisters:
                    return BuildWriteMultipleRequest(request);

                default:
                    throw new NotSupportedException(string.Format(
                        CultureInfo.InvariantCulture,
                        "지원하지 않는 함수 코드입니다: 0x{0:X2}", (byte)request.FunctionCode));
            }
        }

        /// <summary>
        /// 요청에 대한 정상 응답 프레임의 예상 크기를 계산한다.
        /// RTU 는 길이 필드가 없으므로, 수신 측이 몇 바이트를 기다려야 하는지 미리 알아야 한다.
        /// </summary>
        /// <param name="request">요청.</param>
        /// <returns>정상 응답 프레임 크기 [byte] (CRC 포함).</returns>
        public static int ExpectedResponseLength(ModbusRequest request)
        {
            if (request == null)
            {
                throw new ArgumentNullException("request");
            }

            switch (request.FunctionCode)
            {
                case ModbusFunctionCode.ReadHoldingRegisters:
                case ModbusFunctionCode.ReadInputRegisters:
                    // slave(1) + fc(1) + byteCount(1) + data(2N) + crc(2)
                    return 5 + (request.RegisterCount * 2);

                case ModbusFunctionCode.WriteSingleRegister:
                    // 요청을 그대로 에코: slave(1) + fc(1) + addr(2) + value(2) + crc(2)
                    return 8;

                case ModbusFunctionCode.WriteMultipleRegisters:
                    // slave(1) + fc(1) + addr(2) + count(2) + crc(2)
                    return 8;

                default:
                    throw new NotSupportedException("지원하지 않는 함수 코드입니다.");
            }
        }

        /// <summary>예외 응답 프레임의 크기. slave(1) + fc|0x80(1) + code(1) + crc(2).</summary>
        public const int ExceptionResponseLength = 5;

        /// <summary>
        /// 수신 프레임을 검증하고 파싱한다.
        /// </summary>
        /// <param name="request">대응 요청(에코 검증용).</param>
        /// <param name="frame">수신 버퍼.</param>
        /// <param name="length">수신한 유효 바이트 수.</param>
        /// <returns>파싱 결과.</returns>
        public static FrameParseResult ParseResponse(ModbusRequest request, byte[] frame, int length)
        {
            if (request == null)
            {
                throw new ArgumentNullException("request");
            }

            if (frame == null || length < 3)
            {
                return FrameParseResult.Fail(
                    ModbusFailureKind.MalformedFrame,
                    string.Format(CultureInfo.InvariantCulture, "응답이 너무 짧습니다({0} byte).", length));
            }

            // CRC 는 가장 먼저 확인한다. 깨진 프레임의 내용을 해석하는 것은 위험하다.
            if (!ModbusCrc.Verify(frame, length))
            {
                return FrameParseResult.Fail(
                    ModbusFailureKind.CrcError,
                    string.Format(CultureInfo.InvariantCulture, "CRC 불일치({0} byte 수신).", length));
            }

            if (frame[0] != request.SlaveId)
            {
                return FrameParseResult.Fail(
                    ModbusFailureKind.UnexpectedEcho,
                    string.Format(CultureInfo.InvariantCulture,
                        "슬레이브 ID 불일치: 요청 {0}, 응답 {1}", request.SlaveId, frame[0]));
            }

            byte responseFunction = frame[1];

            // 예외 응답: 함수코드에 0x80 이 더해져 돌아온다.
            if ((responseFunction & ExceptionFlag) != 0)
            {
                if (length < ExceptionResponseLength)
                {
                    return FrameParseResult.Fail(
                        ModbusFailureKind.MalformedFrame, "예외 응답 길이가 부족합니다.");
                }

                return FrameParseResult.Exception((ModbusExceptionCode)frame[2]);
            }

            if (responseFunction != (byte)request.FunctionCode)
            {
                return FrameParseResult.Fail(
                    ModbusFailureKind.UnexpectedEcho,
                    string.Format(CultureInfo.InvariantCulture,
                        "함수 코드 불일치: 요청 0x{0:X2}, 응답 0x{1:X2}",
                        (byte)request.FunctionCode, responseFunction));
            }

            if (request.IsWrite)
            {
                return ParseWriteResponse(request, frame, length);
            }

            return ParseReadResponse(request, frame, length);
        }

        /// <summary>읽기 요청 프레임을 조립한다.</summary>
        /// <param name="request">요청.</param>
        /// <returns>RTU 프레임.</returns>
        private static byte[] BuildReadRequest(ModbusRequest request)
        {
            byte[] frame = new byte[8];

            frame[0] = request.SlaveId;
            frame[1] = (byte)request.FunctionCode;
            frame[2] = (byte)(request.StartAddress >> 8);
            frame[3] = (byte)(request.StartAddress & 0xFF);
            frame[4] = (byte)(request.RegisterCount >> 8);
            frame[5] = (byte)(request.RegisterCount & 0xFF);

            ModbusCrc.Append(frame, 6);
            return frame;
        }

        /// <summary>단일 쓰기 요청 프레임을 조립한다.</summary>
        /// <param name="request">요청.</param>
        /// <returns>RTU 프레임.</returns>
        private static byte[] BuildWriteSingleRequest(ModbusRequest request)
        {
            ushort value = request.Values[0];
            byte[] frame = new byte[8];

            frame[0] = request.SlaveId;
            frame[1] = (byte)request.FunctionCode;
            frame[2] = (byte)(request.StartAddress >> 8);
            frame[3] = (byte)(request.StartAddress & 0xFF);
            frame[4] = (byte)(value >> 8);
            frame[5] = (byte)(value & 0xFF);

            ModbusCrc.Append(frame, 6);
            return frame;
        }

        /// <summary>다중 쓰기 요청 프레임을 조립한다.</summary>
        /// <param name="request">요청.</param>
        /// <returns>RTU 프레임.</returns>
        private static byte[] BuildWriteMultipleRequest(ModbusRequest request)
        {
            int count = request.Values.Length;
            int byteCount = count * 2;

            // slave(1) + fc(1) + addr(2) + count(2) + byteCount(1) + data(2N) + crc(2)
            byte[] frame = new byte[9 + byteCount];

            frame[0] = request.SlaveId;
            frame[1] = (byte)request.FunctionCode;
            frame[2] = (byte)(request.StartAddress >> 8);
            frame[3] = (byte)(request.StartAddress & 0xFF);
            frame[4] = (byte)(count >> 8);
            frame[5] = (byte)(count & 0xFF);
            frame[6] = (byte)byteCount;

            for (int i = 0; i < count; i++)
            {
                frame[7 + (i * 2)] = (byte)(request.Values[i] >> 8);
                frame[8 + (i * 2)] = (byte)(request.Values[i] & 0xFF);
            }

            ModbusCrc.Append(frame, 7 + byteCount);
            return frame;
        }

        /// <summary>읽기 응답을 파싱한다.</summary>
        /// <param name="request">요청.</param>
        /// <param name="frame">수신 프레임.</param>
        /// <param name="length">유효 길이.</param>
        /// <returns>파싱 결과.</returns>
        private static FrameParseResult ParseReadResponse(
            ModbusRequest request, byte[] frame, int length)
        {
            int declaredByteCount = frame[2];
            int expectedByteCount = request.RegisterCount * 2;

            if (declaredByteCount != expectedByteCount)
            {
                return FrameParseResult.Fail(
                    ModbusFailureKind.MalformedFrame,
                    string.Format(CultureInfo.InvariantCulture,
                        "바이트 수 불일치: 기대 {0}, 응답 {1}", expectedByteCount, declaredByteCount));
            }

            if (length < 5 + declaredByteCount)
            {
                return FrameParseResult.Fail(
                    ModbusFailureKind.MalformedFrame,
                    string.Format(CultureInfo.InvariantCulture,
                        "데이터가 부족합니다: 필요 {0}, 수신 {1}", 5 + declaredByteCount, length));
            }

            ushort[] registers = new ushort[request.RegisterCount];
            for (int i = 0; i < registers.Length; i++)
            {
                // Modbus 는 레지스터 내부를 Big-endian(상위 바이트 먼저)으로 전송한다.
                registers[i] = (ushort)((frame[3 + (i * 2)] << 8) | frame[4 + (i * 2)]);
            }

            return FrameParseResult.Ok(registers);
        }

        /// <summary>쓰기 응답의 에코 내용을 검증한다.</summary>
        /// <param name="request">요청.</param>
        /// <param name="frame">수신 프레임.</param>
        /// <param name="length">유효 길이.</param>
        /// <returns>파싱 결과.</returns>
        private static FrameParseResult ParseWriteResponse(
            ModbusRequest request, byte[] frame, int length)
        {
            if (length < 8)
            {
                return FrameParseResult.Fail(
                    ModbusFailureKind.MalformedFrame,
                    string.Format(CultureInfo.InvariantCulture, "쓰기 응답 길이 부족({0} byte).", length));
            }

            ushort echoAddress = (ushort)((frame[2] << 8) | frame[3]);
            if (echoAddress != request.StartAddress)
            {
                return FrameParseResult.Fail(
                    ModbusFailureKind.UnexpectedEcho,
                    string.Format(CultureInfo.InvariantCulture,
                        "주소 에코 불일치: 요청 0x{0:X4}, 응답 0x{1:X4}",
                        request.StartAddress, echoAddress));
            }

            ushort echoTail = (ushort)((frame[4] << 8) | frame[5]);

            // FC06 은 쓴 값을, FC16 은 쓴 개수를 에코한다.
            ushort expectedTail = request.FunctionCode == ModbusFunctionCode.WriteSingleRegister
                ? request.Values[0]
                : request.RegisterCount;

            if (echoTail != expectedTail)
            {
                return FrameParseResult.Fail(
                    ModbusFailureKind.UnexpectedEcho,
                    string.Format(CultureInfo.InvariantCulture,
                        "에코 값 불일치: 기대 {0}, 응답 {1}", expectedTail, echoTail));
            }

            return FrameParseResult.Ok(null);
        }
    }
}
