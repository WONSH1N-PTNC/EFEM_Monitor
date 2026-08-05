using System.Collections.Generic;
using System.Globalization;
using Esam.Communication.Abstractions;

namespace Esam.Communication.Configuration
{
    /// <summary>
    /// 쓰기 명령 1건의 정의. device-map.json 의 <c>commands</c> 항목에 대응한다.
    /// </summary>
    /// <remarks>
    /// <para>값 표기 규칙</para>
    /// <list type="bullet">
    ///   <item><description><c>"$arg"</c> — 호출측이 넘긴 값을 그대로 쓴다(위치 pulse, RPM 등).</description></item>
    ///   <item><description><c>"0x1111"</c> / <c>"4369"</c> — 고정값을 쓴다(알람 리셋 등).</description></item>
    ///   <item><description><c>"TBD"</c> — 명세 미확정. 이 명령은 실행 불가로 처리한다.</description></item>
    /// </list>
    /// <para>고정값 명령을 코드에 박지 않는 이유는, 밸브의 <c>0x6002 ← 0x20</c>(Homing) 같은
    /// 매직 넘버가 장치 개정 시 바뀔 수 있기 때문이다.</para>
    /// </remarks>
    public sealed class CommandDefinition
    {
        /// <summary>호출측 인자를 그대로 쓰라는 표기.</summary>
        public const string ArgumentToken = "$arg";

        /// <summary>사용할 함수 코드(6 = 단일 쓰기, 16 = 다중 쓰기).</summary>
        public int FunctionCode { get; set; }

        /// <summary>대상 주소 문자열.</summary>
        public string Address { get; set; }

        /// <summary>쓸 값 문자열. <c>"$arg"</c>, 고정값, 또는 <c>"TBD"</c>.</summary>
        public string Value { get; set; }

        /// <summary>기본값으로 초기화한다(FC06).</summary>
        public CommandDefinition()
        {
            FunctionCode = 6;
        }

        /// <summary>이 명령이 호출측 인자를 사용하는지 여부.</summary>
        public bool UsesArgument
        {
            get
            {
                return Value != null
                       && Value.Trim().Equals(ArgumentToken, System.StringComparison.OrdinalIgnoreCase);
            }
        }

        /// <summary>주소 또는 값이 미확정이어서 실행할 수 없는지 여부.</summary>
        public bool IsUnspecified
        {
            get
            {
                if (RegisterAddress.IsUnspecified(Address))
                {
                    return true;
                }

                if (UsesArgument)
                {
                    return false;
                }

                ushort ignored;
                return !RegisterAddress.TryParse(Value, out ignored);
            }
        }

        /// <summary>함수 코드를 열거형으로 변환한다.</summary>
        /// <param name="functionCode">변환된 함수 코드.</param>
        /// <returns>지원하는 쓰기 함수 코드이면 true.</returns>
        public bool TryGetFunctionCode(out ModbusFunctionCode functionCode)
        {
            switch (FunctionCode)
            {
                case 6:
                    functionCode = ModbusFunctionCode.WriteSingleRegister;
                    return true;

                case 16:
                    functionCode = ModbusFunctionCode.WriteMultipleRegisters;
                    return true;

                default:
                    functionCode = ModbusFunctionCode.WriteSingleRegister;
                    return false;
            }
        }

        /// <summary>
        /// 이 명령을 실제 Modbus 요청으로 변환한다.
        /// </summary>
        /// <param name="slaveId">대상 슬레이브 주소.</param>
        /// <param name="argument">
        /// <c>$arg</c> 를 대체할 값. 범위를 벗어나면 0~65535 로 제한한다.
        /// </param>
        /// <param name="request">생성된 요청.</param>
        /// <returns>변환에 성공하면 true. 미확정 명령이면 false.</returns>
        public bool TryBuildRequest(byte slaveId, double argument, out ModbusRequest request)
        {
            request = null;

            ushort address;
            if (!RegisterAddress.TryParse(Address, out address))
            {
                return false;
            }

            ModbusFunctionCode function;
            if (!TryGetFunctionCode(out function))
            {
                return false;
            }

            ushort value;

            if (UsesArgument)
            {
                double rounded = System.Math.Round(argument, System.MidpointRounding.AwayFromZero);

                if (rounded < 0.0)
                {
                    rounded = 0.0;
                }
                else if (rounded > ushort.MaxValue)
                {
                    rounded = ushort.MaxValue;
                }

                value = (ushort)rounded;
            }
            else if (!RegisterAddress.TryParse(Value, out value))
            {
                return false;
            }

            request = function == ModbusFunctionCode.WriteSingleRegister
                ? ModbusRequest.WriteSingle(slaveId, address, value)
                : ModbusRequest.WriteMultiple(slaveId, address, new[] { value });

            return true;
        }

        /// <summary>정의의 유효성을 검증한다.</summary>
        /// <param name="context">오류 메시지에 포함할 위치 설명.</param>
        /// <param name="errors">검증 실패 사유를 추가할 목록.</param>
        /// <returns>유효하면 true.</returns>
        public bool Validate(string context, IList<string> errors)
        {
            int before = errors.Count;

            ModbusFunctionCode ignored;
            if (!TryGetFunctionCode(out ignored))
            {
                errors.Add(string.Format(
                    CultureInfo.InvariantCulture,
                    "{0}: 쓰기 함수 코드는 6 또는 16 이어야 합니다(현재 {1}).", context, FunctionCode));
            }

            if (string.IsNullOrEmpty(Value))
            {
                errors.Add(string.Format(
                    CultureInfo.InvariantCulture, "{0}: 명령 value 는 필수입니다.", context));
            }

            return errors.Count == before;
        }
    }
}
