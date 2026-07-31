using System;

namespace Esam.Communication.Modbus
{
    /// <summary>
    /// CRC-16/MODBUS 계산기. 다항식 0xA001(reflected 0x8005), 초기값 0xFFFF, 출력 반전 없음.
    /// </summary>
    /// <remarks>
    /// <para>256엔트리 룩업 테이블을 사용한다. 폴링 루프가 초당 수십~수백 프레임을 처리하므로
    /// 비트 단위 루프보다 8배 빠른 바이트 단위 처리가 유리하다.</para>
    /// <para><b>주의:</b> Modbus RTU 는 CRC 를 <b>하위 바이트 먼저</b> 전송한다.
    /// 프레임 조립 시 순서를 뒤집으면 모든 통신이 실패하므로
    /// <see cref="Append"/> / <see cref="Verify"/> 를 사용해 직접 배치하지 않는 것이 안전하다.</para>
    /// </remarks>
    public static class ModbusCrc
    {
        private static readonly ushort[] Table = BuildTable();

        /// <summary>바이트 배열의 지정 구간에 대한 CRC 를 계산한다.</summary>
        /// <param name="buffer">대상 버퍼.</param>
        /// <param name="offset">시작 오프셋.</param>
        /// <param name="length">계산할 바이트 수.</param>
        /// <returns>CRC-16/MODBUS 값.</returns>
        /// <exception cref="ArgumentNullException">버퍼가 null 일 때.</exception>
        /// <exception cref="ArgumentOutOfRangeException">구간이 버퍼 범위를 벗어날 때.</exception>
        public static ushort Compute(byte[] buffer, int offset, int length)
        {
            if (buffer == null)
            {
                throw new ArgumentNullException("buffer");
            }

            if (offset < 0 || length < 0 || offset + length > buffer.Length)
            {
                throw new ArgumentOutOfRangeException(
                    "length", length, "CRC 계산 구간이 버퍼 범위를 벗어났습니다.");
            }

            ushort crc = 0xFFFF;
            int end = offset + length;

            for (int i = offset; i < end; i++)
            {
                byte index = (byte)(crc ^ buffer[i]);
                crc = (ushort)((crc >> 8) ^ Table[index]);
            }

            return crc;
        }

        /// <summary>바이트 배열 전체에 대한 CRC 를 계산한다.</summary>
        /// <param name="buffer">대상 버퍼.</param>
        /// <returns>CRC-16/MODBUS 값.</returns>
        public static ushort Compute(byte[] buffer)
        {
            if (buffer == null)
            {
                throw new ArgumentNullException("buffer");
            }

            return Compute(buffer, 0, buffer.Length);
        }

        /// <summary>
        /// 프레임 뒤 2바이트에 CRC 를 규격 순서(하위 바이트 먼저)로 기록한다.
        /// </summary>
        /// <param name="frame">프레임 버퍼. 길이가 <paramref name="payloadLength"/> + 2 이상이어야 한다.</param>
        /// <param name="payloadLength">CRC 대상 페이로드 길이(CRC 2바이트 제외).</param>
        public static void Append(byte[] frame, int payloadLength)
        {
            if (frame == null)
            {
                throw new ArgumentNullException("frame");
            }

            if (payloadLength < 0 || payloadLength + 2 > frame.Length)
            {
                throw new ArgumentOutOfRangeException(
                    "payloadLength", payloadLength, "CRC 를 기록할 공간이 부족합니다.");
            }

            ushort crc = Compute(frame, 0, payloadLength);

            frame[payloadLength] = (byte)(crc & 0xFF);          // 하위 바이트 먼저
            frame[payloadLength + 1] = (byte)((crc >> 8) & 0xFF); // 상위 바이트
        }

        /// <summary>
        /// 수신 프레임의 CRC 가 올바른지 검증한다.
        /// </summary>
        /// <param name="frame">CRC 2바이트를 포함한 전체 프레임.</param>
        /// <param name="length">유효 프레임 길이(CRC 포함).</param>
        /// <returns>CRC 가 일치하면 true.</returns>
        public static bool Verify(byte[] frame, int length)
        {
            if (frame == null || length < 3 || length > frame.Length)
            {
                return false;
            }

            ushort expected = Compute(frame, 0, length - 2);
            ushort actual = (ushort)(frame[length - 2] | (frame[length - 1] << 8));

            return expected == actual;
        }

        /// <summary>룩업 테이블을 생성한다.</summary>
        /// <returns>256엔트리 CRC 테이블.</returns>
        private static ushort[] BuildTable()
        {
            ushort[] table = new ushort[256];

            for (int i = 0; i < 256; i++)
            {
                ushort value = (ushort)i;

                for (int bit = 0; bit < 8; bit++)
                {
                    if ((value & 0x0001) != 0)
                    {
                        value = (ushort)((value >> 1) ^ 0xA001);
                    }
                    else
                    {
                        value = (ushort)(value >> 1);
                    }
                }

                table[i] = value;
            }

            return table;
        }
    }
}
