using System;
using Esam.Communication.Configuration;

namespace Esam.Communication.Polling
{
    /// <summary>
    /// 원시 레지스터 배열을 <see cref="PointDefinition"/> 에 따라 공학값으로 변환한다.
    /// </summary>
    /// <remarks>
    /// <para>순수 함수 집합이므로 하드웨어 없이 전수 검증할 수 있다.
    /// 통신 오류보다 <b>디코딩 오류가 더 위험하다</b>. 통신 오류는 즉시 드러나지만,
    /// 부호를 잘못 해석하면 -200 Pa 가 65336 Pa 로 보여 제어가 정반대로 동작한다.</para>
    /// <para>Modbus 는 레지스터 내부를 항상 Big-endian 으로 전송하므로 바이트 순서 처리는
    /// 프레임 파싱 단계(<c>ModbusRtuCodec</c>)에서 끝난다. 여기서 다루는 것은
    /// 32비트 값의 <b>워드 순서</b>뿐이다.</para>
    /// </remarks>
    public static class PointDecoder
    {
        /// <summary>
        /// 측정점 1건을 디코딩한다.
        /// </summary>
        /// <param name="point">측정점 정의.</param>
        /// <param name="registers">읽어온 원시 레지스터 배열.</param>
        /// <param name="value">스케일·바이어스가 적용된 값.</param>
        /// <returns>디코딩에 성공하면 true. 레지스터 범위를 벗어나면 false.</returns>
        public static bool TryDecode(PointDefinition point, ushort[] registers, out double value)
        {
            value = 0.0;

            if (point == null || registers == null)
            {
                return false;
            }

            if (point.Offset < 0 || point.Offset + point.RegisterCount > registers.Length)
            {
                // 설정 검증에서 걸러지지만, 장치가 선언보다 적은 레지스터를 응답하는 경우도 있다.
                return false;
            }

            double raw;

            switch (point.Type)
            {
                case PointDataType.UInt16:
                    raw = registers[point.Offset];
                    break;

                case PointDataType.Int16:
                    // 차압센서의 음압값은 반드시 이 경로를 타야 한다.
                    raw = unchecked((short)registers[point.Offset]);
                    break;

                case PointDataType.UInt32:
                    raw = Combine32(registers, point.Offset, point.WordOrder);
                    break;

                case PointDataType.Int32:
                    raw = unchecked((int)Combine32(registers, point.Offset, point.WordOrder));
                    break;

                case PointDataType.Bool:
                    raw = DecodeBit(registers[point.Offset], point.Bit, point.ActiveHigh) ? 1.0 : 0.0;

                    // 논리값에 배율·바이어스를 적용하면 의미가 깨진다. 그대로 반환한다.
                    value = raw;
                    return true;

                default:
                    return false;
            }

            value = (raw * point.Scale) + point.Bias;
            return true;
        }

        /// <summary>연속 2개 레지스터를 32비트 값으로 결합한다.</summary>
        /// <param name="registers">레지스터 배열.</param>
        /// <param name="offset">시작 오프셋.</param>
        /// <param name="order">워드 순서.</param>
        /// <returns>결합된 부호 없는 32비트 값.</returns>
        public static uint Combine32(ushort[] registers, int offset, WordOrder order)
        {
            ushort first = registers[offset];
            ushort second = registers[offset + 1];

            return order == WordOrder.HighWordFirst
                ? (uint)((first << 16) | second)
                : (uint)((second << 16) | first);
        }

        /// <summary>레지스터의 지정 비트를 극성에 맞춰 해석한다.</summary>
        /// <param name="register">레지스터 값.</param>
        /// <param name="bit">비트 번호(0~15).</param>
        /// <param name="activeHigh">true 면 비트 1 을 true 로 본다.</param>
        /// <returns>해석된 논리값.</returns>
        public static bool DecodeBit(ushort register, int bit, bool activeHigh)
        {
            if (bit < 0 || bit > 15)
            {
                return false;
            }

            bool isSet = ((register >> bit) & 0x0001) != 0;

            // Active Low 신호(예: 정상일 때 1, 이상일 때 0)를 설정으로 뒤집을 수 있어야 한다.
            // PLC 안전 입력의 극성이 미확정이므로(Open Issue #18) 코드가 아니라 설정으로 다뤄야 한다.
            return activeHigh ? isSet : !isSet;
        }

        /// <summary>
        /// 공학값을 레지스터 원시값으로 역변환한다. 쓰기 지령에 사용한다.
        /// </summary>
        /// <param name="point">측정점 정의.</param>
        /// <param name="value">공학값.</param>
        /// <returns>레지스터에 쓸 부호 없는 16비트 값.</returns>
        public static ushort EncodeUInt16(PointDefinition point, double value)
        {
            double scale = point == null || point.Scale == 0.0 ? 1.0 : point.Scale;
            double bias = point == null ? 0.0 : point.Bias;

            double raw = Math.Round((value - bias) / scale, MidpointRounding.AwayFromZero);

            if (raw > ushort.MaxValue)
            {
                raw = ushort.MaxValue;
            }
            else if (raw < short.MinValue)
            {
                raw = short.MinValue;
            }

            // 음수는 2의 보수로 담는다(예: -2000 → 0xF830).
            return raw < 0.0
                ? unchecked((ushort)(short)raw)
                : (ushort)raw;
        }
    }
}
