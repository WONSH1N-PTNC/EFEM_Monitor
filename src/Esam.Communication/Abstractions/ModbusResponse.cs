using System;
using System.Globalization;

namespace Esam.Communication.Abstractions
{
    /// <summary>
    /// Modbus 트랜잭션 결과 1건.
    /// </summary>
    /// <remarks>
    /// 통신 실패를 예외로 던지지 않고 결과 객체로 반환한다.
    /// 폴링 루프는 100~200ms 주기로 수십 회 트랜잭션을 수행하므로,
    /// 타임아웃마다 예외를 던지면 비용과 로그 노이즈가 크고 루프 제어도 복잡해진다.
    /// </remarks>
    public sealed class ModbusResponse
    {
        private static readonly ushort[] NoRegisters = new ushort[0];

        /// <summary>대응하는 요청.</summary>
        public ModbusRequest Request { get; private set; }

        /// <summary>트랜잭션 성공 여부.</summary>
        public bool IsSuccess { get; private set; }

        /// <summary>실패 원인. 성공이면 <see cref="ModbusFailureKind.None"/>.</summary>
        public ModbusFailureKind FailureKind { get; private set; }

        /// <summary>슬레이브가 반환한 예외 코드. 예외 응답이 아니면 <see cref="ModbusExceptionCode.None"/>.</summary>
        public ModbusExceptionCode ExceptionCode { get; private set; }

        /// <summary>읽은 레지스터 값. 쓰기 요청이나 실패 시 빈 배열.</summary>
        public ushort[] Registers { get; private set; }

        /// <summary>요청 송신부터 응답 수신 완료까지 걸린 시간 [ms].</summary>
        public double ElapsedMs { get; private set; }

        /// <summary>재시도 횟수(0 = 첫 시도에 성공).</summary>
        public int RetryCount { get; private set; }

        /// <summary>실패 상세 설명. 성공이면 null.</summary>
        public string FailureDetail { get; private set; }

        private ModbusResponse(
            ModbusRequest request,
            bool isSuccess,
            ModbusFailureKind failureKind,
            ModbusExceptionCode exceptionCode,
            ushort[] registers,
            double elapsedMs,
            int retryCount,
            string failureDetail)
        {
            Request = request;
            IsSuccess = isSuccess;
            FailureKind = failureKind;
            ExceptionCode = exceptionCode;
            Registers = registers ?? NoRegisters;
            ElapsedMs = elapsedMs;
            RetryCount = retryCount;
            FailureDetail = failureDetail;
        }

        /// <summary>성공 결과를 만든다.</summary>
        /// <param name="request">대응 요청.</param>
        /// <param name="registers">읽은 레지스터 값(쓰기면 null).</param>
        /// <param name="elapsedMs">소요 시간 [ms].</param>
        /// <param name="retryCount">재시도 횟수.</param>
        /// <returns>성공 결과.</returns>
        public static ModbusResponse Success(
            ModbusRequest request, ushort[] registers, double elapsedMs, int retryCount)
        {
            return new ModbusResponse(
                request, true, ModbusFailureKind.None, ModbusExceptionCode.None,
                registers, elapsedMs, retryCount, null);
        }

        /// <summary>실패 결과를 만든다.</summary>
        /// <param name="request">대응 요청.</param>
        /// <param name="kind">실패 원인.</param>
        /// <param name="detail">실패 상세 설명.</param>
        /// <param name="elapsedMs">소요 시간 [ms].</param>
        /// <param name="retryCount">재시도 횟수.</param>
        /// <returns>실패 결과.</returns>
        public static ModbusResponse Failure(
            ModbusRequest request,
            ModbusFailureKind kind,
            string detail,
            double elapsedMs,
            int retryCount)
        {
            return new ModbusResponse(
                request, false, kind, ModbusExceptionCode.None,
                null, elapsedMs, retryCount, detail);
        }

        /// <summary>슬레이브 예외 응답 결과를 만든다.</summary>
        /// <param name="request">대응 요청.</param>
        /// <param name="exceptionCode">슬레이브가 반환한 예외 코드.</param>
        /// <param name="elapsedMs">소요 시간 [ms].</param>
        /// <param name="retryCount">재시도 횟수.</param>
        /// <returns>예외 응답 결과.</returns>
        public static ModbusResponse Exception(
            ModbusRequest request, ModbusExceptionCode exceptionCode, double elapsedMs, int retryCount)
        {
            string detail = string.Format(
                CultureInfo.InvariantCulture,
                "슬레이브 예외 응답: {0} (0x{1:X2})", exceptionCode, (byte)exceptionCode);

            return new ModbusResponse(
                request, false, ModbusFailureKind.ExceptionResponse, exceptionCode,
                null, elapsedMs, retryCount, detail);
        }

        /// <summary>지정 위치의 레지스터를 부호 없는 16비트 정수로 읽는다.</summary>
        /// <param name="offset">레지스터 오프셋.</param>
        /// <returns>레지스터 값.</returns>
        /// <exception cref="IndexOutOfRangeException">오프셋이 범위를 벗어날 때.</exception>
        public ushort GetUInt16(int offset)
        {
            return Registers[offset];
        }

        /// <summary>지정 위치의 레지스터를 부호 있는 16비트 정수로 읽는다.</summary>
        /// <param name="offset">레지스터 오프셋.</param>
        /// <returns>부호 있는 값. 차압센서의 음압값 판독에 사용한다.</returns>
        public short GetInt16(int offset)
        {
            return unchecked((short)Registers[offset]);
        }

        /// <summary>연속된 2개 레지스터를 32비트 정수로 결합한다.</summary>
        /// <param name="offset">시작 레지스터 오프셋.</param>
        /// <param name="highWordFirst">true 면 상위 워드가 먼저 오는 배열(Big-endian word order).</param>
        /// <returns>결합된 부호 있는 32비트 값.</returns>
        public int GetInt32(int offset, bool highWordFirst)
        {
            ushort first = Registers[offset];
            ushort second = Registers[offset + 1];

            // 장치에 따라 워드 순서가 다르므로(DESIGN.md Open Issue #5) 설정으로 선택할 수 있게 한다.
            uint combined = highWordFirst
                ? (uint)((first << 16) | second)
                : (uint)((second << 16) | first);

            return unchecked((int)combined);
        }

        /// <inheritdoc />
        public override string ToString()
        {
            if (IsSuccess)
            {
                return string.Format(
                    CultureInfo.InvariantCulture,
                    "OK {0} — {1} regs, {2:F1} ms, retry {3}",
                    Request, Registers.Length, ElapsedMs, RetryCount);
            }

            return string.Format(
                CultureInfo.InvariantCulture,
                "FAIL {0} — {1}: {2} ({3:F1} ms, retry {4})",
                Request, FailureKind, FailureDetail, ElapsedMs, RetryCount);
        }
    }
}
