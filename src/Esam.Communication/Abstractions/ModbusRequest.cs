using System;
using System.Globalization;

namespace Esam.Communication.Abstractions
{
    /// <summary>
    /// Modbus 트랜잭션 요청 1건. 불변 값 객체이다.
    /// </summary>
    public sealed class ModbusRequest
    {
        /// <summary>슬레이브 주소(1~247). 0 은 브로드캐스트이므로 본 시스템에서는 허용하지 않는다.</summary>
        public byte SlaveId { get; private set; }

        /// <summary>함수 코드.</summary>
        public ModbusFunctionCode FunctionCode { get; private set; }

        /// <summary>시작 레지스터 주소.</summary>
        public ushort StartAddress { get; private set; }

        /// <summary>읽을 레지스터 개수. 쓰기 요청에서는 <see cref="Values"/> 의 길이와 같다.</summary>
        public ushort RegisterCount { get; private set; }

        /// <summary>쓸 값. 읽기 요청이면 null.</summary>
        public ushort[] Values { get; private set; }

        /// <summary>이 요청이 쓰기 요청인지 여부.</summary>
        public bool IsWrite
        {
            get
            {
                return FunctionCode == ModbusFunctionCode.WriteSingleRegister
                       || FunctionCode == ModbusFunctionCode.WriteMultipleRegisters;
            }
        }

        private ModbusRequest(
            byte slaveId,
            ModbusFunctionCode functionCode,
            ushort startAddress,
            ushort registerCount,
            ushort[] values)
        {
            if (slaveId == 0 || slaveId > 247)
            {
                throw new ArgumentOutOfRangeException(
                    "slaveId", slaveId, "슬레이브 주소는 1~247 범위여야 합니다(브로드캐스트 미지원).");
            }

            SlaveId = slaveId;
            FunctionCode = functionCode;
            StartAddress = startAddress;
            RegisterCount = registerCount;
            Values = values;
        }

        /// <summary>Holding Register 읽기 요청(FC03)을 만든다.</summary>
        /// <param name="slaveId">슬레이브 주소.</param>
        /// <param name="startAddress">시작 주소.</param>
        /// <param name="count">읽을 레지스터 개수(1~125).</param>
        /// <returns>생성된 요청.</returns>
        public static ModbusRequest ReadHolding(byte slaveId, ushort startAddress, ushort count)
        {
            ValidateReadCount(count);
            return new ModbusRequest(
                slaveId, ModbusFunctionCode.ReadHoldingRegisters, startAddress, count, null);
        }

        /// <summary>Input Register 읽기 요청(FC04)을 만든다.</summary>
        /// <param name="slaveId">슬레이브 주소.</param>
        /// <param name="startAddress">시작 주소.</param>
        /// <param name="count">읽을 레지스터 개수(1~125).</param>
        /// <returns>생성된 요청.</returns>
        public static ModbusRequest ReadInput(byte slaveId, ushort startAddress, ushort count)
        {
            ValidateReadCount(count);
            return new ModbusRequest(
                slaveId, ModbusFunctionCode.ReadInputRegisters, startAddress, count, null);
        }

        /// <summary>단일 레지스터 쓰기 요청(FC06)을 만든다.</summary>
        /// <param name="slaveId">슬레이브 주소.</param>
        /// <param name="address">대상 주소.</param>
        /// <param name="value">쓸 값.</param>
        /// <returns>생성된 요청.</returns>
        public static ModbusRequest WriteSingle(byte slaveId, ushort address, ushort value)
        {
            return new ModbusRequest(
                slaveId, ModbusFunctionCode.WriteSingleRegister, address, 1, new[] { value });
        }

        /// <summary>다중 레지스터 쓰기 요청(FC16)을 만든다.</summary>
        /// <param name="slaveId">슬레이브 주소.</param>
        /// <param name="startAddress">시작 주소.</param>
        /// <param name="values">쓸 값 배열(1~123개).</param>
        /// <returns>생성된 요청.</returns>
        public static ModbusRequest WriteMultiple(byte slaveId, ushort startAddress, ushort[] values)
        {
            if (values == null || values.Length == 0)
            {
                throw new ArgumentException("쓸 값이 비어 있습니다.", "values");
            }

            if (values.Length > 123)
            {
                throw new ArgumentOutOfRangeException(
                    "values", values.Length, "FC16 은 한 번에 최대 123개 레지스터만 쓸 수 있습니다.");
            }

            // 호출측이 배열을 나중에 수정해도 요청이 변하지 않도록 복사한다.
            ushort[] copy = new ushort[values.Length];
            Array.Copy(values, copy, values.Length);

            return new ModbusRequest(
                slaveId, ModbusFunctionCode.WriteMultipleRegisters,
                startAddress, (ushort)copy.Length, copy);
        }

        /// <summary>읽기 개수의 규격 범위를 검증한다.</summary>
        /// <param name="count">레지스터 개수.</param>
        private static void ValidateReadCount(ushort count)
        {
            // RTU 프레임 최대 256바이트 제약에서 유도되는 규격 상한.
            if (count < 1 || count > 125)
            {
                throw new ArgumentOutOfRangeException(
                    "count", count, "읽을 레지스터 개수는 1~125 범위여야 합니다.");
            }
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "Slave {0} FC{1:X2} Addr 0x{2:X4} Count {3}",
                SlaveId, (byte)FunctionCode, StartAddress, RegisterCount);
        }
    }
}
