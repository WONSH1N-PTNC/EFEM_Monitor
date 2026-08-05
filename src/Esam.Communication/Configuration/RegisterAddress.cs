using System;
using System.Globalization;

namespace Esam.Communication.Configuration
{
    /// <summary>
    /// 설정 파일의 레지스터 주소 문자열을 파싱한다.
    /// </summary>
    /// <remarks>
    /// <para>통신자료의 주소는 밸브처럼 <c>"0x602B"</c> 형태인 것도 있고,
    /// 센서처럼 십진수인 것도 있다. 또 아직 명세가 확보되지 않은 항목은
    /// <c>"TBD"</c> 로 표기해 두어야 한다(DESIGN.md Open Issue #5, #9).</para>
    /// <para>세 형태를 모두 받아들이고, TBD 는 "주소 미확정"으로 명확히 구분해
    /// 설정 검증 단계에서 걸러낼 수 있게 한다. 미확정 주소를 0 으로 해석해
    /// 엉뚱한 레지스터를 읽는 것이 가장 위험하기 때문이다.</para>
    /// </remarks>
    public static class RegisterAddress
    {
        /// <summary>주소가 아직 확정되지 않았음을 나타내는 표기.</summary>
        public const string UnspecifiedToken = "TBD";

        /// <summary>주소 문자열이 미확정 표기인지 판정한다.</summary>
        /// <param name="text">주소 문자열.</param>
        /// <returns>비어 있거나 TBD 로 시작하면 true.</returns>
        public static bool IsUnspecified(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return true;
            }

            string trimmed = text.Trim();

            // 실제 설정에는 "TBD(D100)" 처럼 참고 정보를 덧붙인 경우가 있으므로 접두 비교한다.
            return trimmed.StartsWith(UnspecifiedToken, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>주소 문자열을 파싱한다.</summary>
        /// <param name="text">주소 문자열("0x602B", "24619", "TBD" 등).</param>
        /// <param name="address">파싱된 주소.</param>
        /// <returns>파싱에 성공하면 true. 미확정 표기이거나 형식 오류이면 false.</returns>
        public static bool TryParse(string text, out ushort address)
        {
            address = 0;

            if (IsUnspecified(text))
            {
                return false;
            }

            string trimmed = text.Trim();

            if (trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                return ushort.TryParse(
                    trimmed.Substring(2),
                    NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture,
                    out address);
            }

            return ushort.TryParse(
                trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out address);
        }

        /// <summary>주소 문자열을 파싱한다. 실패 시 예외를 던진다.</summary>
        /// <param name="text">주소 문자열.</param>
        /// <param name="context">오류 메시지에 포함할 위치 설명(예: "WTDM550.pressure.startAddress").</param>
        /// <returns>파싱된 주소.</returns>
        /// <exception cref="FormatException">파싱에 실패했을 때.</exception>
        public static ushort Parse(string text, string context)
        {
            ushort address;
            if (TryParse(text, out address))
            {
                return address;
            }

            throw new FormatException(string.Format(
                CultureInfo.InvariantCulture,
                "{0}: 레지스터 주소 '{1}' 을(를) 해석할 수 없습니다. " +
                "16진수(0x602B), 십진수(24619) 또는 미확정(TBD) 이어야 합니다.",
                context, text));
        }

        /// <summary>주소를 설정 파일 표기(16진수)로 변환한다.</summary>
        /// <param name="address">주소.</param>
        /// <returns>"0xXXXX" 형식 문자열.</returns>
        public static string ToHex(ushort address)
        {
            return string.Format(CultureInfo.InvariantCulture, "0x{0:X4}", address);
        }
    }
}
