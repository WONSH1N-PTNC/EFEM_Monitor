using System;

namespace Esam.Communication.Modbus
{
    /// <summary>
    /// Modbus RTU 타이밍 계산. 프레임 간 무음구간(t3.5)과 전송 시간을 산출한다.
    /// </summary>
    /// <remarks>
    /// <para>RTU 는 프레임 경계를 <b>침묵 시간</b>으로 구분한다. 규격상 프레임 사이에는
    /// 최소 3.5 문자시간의 무음구간이 있어야 하며, 프레임 내부에서는 1.5 문자시간 이상
    /// 끊기면 안 된다. 이를 지키지 않으면 슬레이브가 프레임을 이어붙여 해석해 통신이 깨진다.</para>
    /// <para>1 문자는 start 1 + data 8 + parity 1 + stop 1 = <b>11비트</b>로 계산한다.
    /// 패리티를 쓰지 않는 8-N-1 은 실제 10비트지만, 규격은 11비트를 기준으로 하므로
    /// 보수적으로 11비트를 적용해 여유를 둔다.</para>
    /// <para>19200bps 초과에서는 규격이 고정값(t3.5 = 1.75ms, t1.5 = 0.75ms)을 권고한다.</para>
    /// </remarks>
    public static class ModbusTiming
    {
        /// <summary>1 문자당 비트 수(규격 기준).</summary>
        public const int BitsPerCharacter = 11;

        /// <summary>고정 타이밍을 적용하기 시작하는 통신 속도 [bps].</summary>
        public const int FixedTimingBaudThreshold = 19200;

        /// <summary>19200bps 초과 시 적용할 t3.5 고정값 [ms].</summary>
        public const double FixedT35Ms = 1.75;

        /// <summary>19200bps 초과 시 적용할 t1.5 고정값 [ms].</summary>
        public const double FixedT15Ms = 0.75;

        /// <summary>1 문자 전송 시간을 계산한다.</summary>
        /// <param name="baudRate">통신 속도 [bps].</param>
        /// <returns>문자 시간 [ms].</returns>
        public static double CharacterTimeMs(int baudRate)
        {
            if (baudRate <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    "baudRate", baudRate, "통신 속도는 0보다 커야 합니다.");
            }

            return BitsPerCharacter * 1000.0 / baudRate;
        }

        /// <summary>프레임 간 최소 무음구간(t3.5)을 계산한다.</summary>
        /// <param name="baudRate">통신 속도 [bps].</param>
        /// <returns>무음구간 [ms].</returns>
        public static double InterFrameDelayMs(int baudRate)
        {
            if (baudRate > FixedTimingBaudThreshold)
            {
                return FixedT35Ms;
            }

            return 3.5 * CharacterTimeMs(baudRate);
        }

        /// <summary>프레임 내 최대 허용 문자 간격(t1.5)을 계산한다.</summary>
        /// <param name="baudRate">통신 속도 [bps].</param>
        /// <returns>허용 간격 [ms].</returns>
        public static double IntraFrameGapMs(int baudRate)
        {
            if (baudRate > FixedTimingBaudThreshold)
            {
                return FixedT15Ms;
            }

            return 1.5 * CharacterTimeMs(baudRate);
        }

        /// <summary>지정 바이트 수의 순수 전송 시간을 계산한다.</summary>
        /// <param name="byteCount">바이트 수.</param>
        /// <param name="baudRate">통신 속도 [bps].</param>
        /// <returns>전송 시간 [ms].</returns>
        public static double TransmissionTimeMs(int byteCount, int baudRate)
        {
            return byteCount * CharacterTimeMs(baudRate);
        }

        /// <summary>
        /// 트랜잭션 1건의 예상 소요 시간을 계산한다.
        /// 폴링 주기 설계(DESIGN.md 2.2 B)와 통신 진단 화면의 기준값 표시에 사용한다.
        /// </summary>
        /// <param name="requestBytes">요청 프레임 크기 [byte].</param>
        /// <param name="responseBytes">응답 프레임 크기 [byte].</param>
        /// <param name="baudRate">통신 속도 [bps].</param>
        /// <param name="slaveResponseDelayMs">슬레이브 내부 처리 지연 [ms].</param>
        /// <returns>예상 트랜잭션 시간 [ms].</returns>
        public static double EstimateTransactionMs(
            int requestBytes, int responseBytes, int baudRate, double slaveResponseDelayMs)
        {
            return TransmissionTimeMs(requestBytes, baudRate)
                   + slaveResponseDelayMs
                   + TransmissionTimeMs(responseBytes, baudRate)
                   + InterFrameDelayMs(baudRate);
        }
    }
}
