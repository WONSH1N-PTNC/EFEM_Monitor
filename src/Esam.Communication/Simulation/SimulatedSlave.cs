using System;
using System.Collections.Generic;
using Esam.Communication.Abstractions;

namespace Esam.Communication.Simulation
{
    /// <summary>
    /// 시뮬레이션 슬레이브 장치 1대. 실제 Modbus 슬레이브의 레지스터 동작을 모사한다.
    /// </summary>
    public interface ISimulatedSlave
    {
        /// <summary>슬레이브 주소.</summary>
        byte SlaveId { get; }

        /// <summary>레지스터를 읽는다.</summary>
        /// <param name="startAddress">시작 주소.</param>
        /// <param name="count">읽을 개수.</param>
        /// <param name="values">읽은 값.</param>
        /// <param name="exceptionCode">실패 시 예외 코드.</param>
        /// <returns>읽기 성공 시 true.</returns>
        bool TryRead(
            ushort startAddress, ushort count, out ushort[] values, out ModbusExceptionCode exceptionCode);

        /// <summary>레지스터에 쓴다.</summary>
        /// <param name="startAddress">시작 주소.</param>
        /// <param name="values">쓸 값.</param>
        /// <param name="exceptionCode">실패 시 예외 코드.</param>
        /// <returns>쓰기 성공 시 true.</returns>
        bool TryWrite(ushort startAddress, ushort[] values, out ModbusExceptionCode exceptionCode);
    }

    /// <summary>
    /// 주소별 처리 함수를 등록해 사용하는 시뮬레이션 슬레이브 기반 클래스.
    /// </summary>
    /// <remarks>
    /// 실제 장치 레지스터 명세가 확정되지 않은 항목(DESIGN.md Open Issue #5, #9)은
    /// 여기서 <b>잠정 주소</b>로 정의해 둔다. 명세가 확보되면 이 클래스의 주소만 바꾸면 되고,
    /// 상위 계층과 제어 로직은 손대지 않는다.
    /// </remarks>
    public abstract class SimulatedSlaveBase : ISimulatedSlave
    {
        private readonly Dictionary<ushort, Func<ushort>> _readHandlers =
            new Dictionary<ushort, Func<ushort>>();

        private readonly Dictionary<ushort, Action<ushort>> _writeHandlers =
            new Dictionary<ushort, Action<ushort>>();

        /// <inheritdoc />
        public byte SlaveId { get; private set; }

        /// <summary>시뮬레이션 슬레이브를 생성한다.</summary>
        /// <param name="slaveId">슬레이브 주소.</param>
        protected SimulatedSlaveBase(byte slaveId)
        {
            SlaveId = slaveId;
        }

        /// <summary>읽기 가능 레지스터를 등록한다.</summary>
        /// <param name="address">레지스터 주소.</param>
        /// <param name="reader">값 제공 함수.</param>
        protected void MapRead(ushort address, Func<ushort> reader)
        {
            _readHandlers[address] = reader;
        }

        /// <summary>쓰기 가능 레지스터를 등록한다.</summary>
        /// <param name="address">레지스터 주소.</param>
        /// <param name="writer">값 처리 함수.</param>
        protected void MapWrite(ushort address, Action<ushort> writer)
        {
            _writeHandlers[address] = writer;
        }

        /// <inheritdoc />
        public bool TryRead(
            ushort startAddress, ushort count, out ushort[] values, out ModbusExceptionCode exceptionCode)
        {
            values = null;
            exceptionCode = ModbusExceptionCode.None;

            ushort[] buffer = new ushort[count];

            for (int i = 0; i < count; i++)
            {
                ushort address = (ushort)(startAddress + i);

                Func<ushort> reader;
                if (!_readHandlers.TryGetValue(address, out reader))
                {
                    // 실제 슬레이브와 동일하게 정의되지 않은 주소는 예외 응답을 낸다.
                    // 이렇게 해야 device-map.json 의 주소 오타를 시뮬레이션에서 잡을 수 있다.
                    exceptionCode = ModbusExceptionCode.IllegalDataAddress;
                    return false;
                }

                buffer[i] = reader();
            }

            values = buffer;
            return true;
        }

        /// <inheritdoc />
        public bool TryWrite(ushort startAddress, ushort[] values, out ModbusExceptionCode exceptionCode)
        {
            exceptionCode = ModbusExceptionCode.None;

            if (values == null)
            {
                exceptionCode = ModbusExceptionCode.IllegalDataValue;
                return false;
            }

            for (int i = 0; i < values.Length; i++)
            {
                ushort address = (ushort)(startAddress + i);

                Action<ushort> writer;
                if (!_writeHandlers.TryGetValue(address, out writer))
                {
                    exceptionCode = ModbusExceptionCode.IllegalDataAddress;
                    return false;
                }

                writer(values[i]);
            }

            return true;
        }

        /// <summary>double 값을 스케일 적용 후 부호 있는 16비트 레지스터로 변환한다.</summary>
        /// <param name="value">물리량.</param>
        /// <param name="scale">1 LSB 당 물리량(예: 0.1 Pa/LSB 이면 0.1).</param>
        /// <returns>레지스터 값.</returns>
        protected static ushort ToSignedRegister(double value, double scale)
        {
            if (scale <= 0.0)
            {
                scale = 1.0;
            }

            double raw = Math.Round(value / scale, MidpointRounding.AwayFromZero);

            if (raw > short.MaxValue)
            {
                raw = short.MaxValue;
            }
            else if (raw < short.MinValue)
            {
                raw = short.MinValue;
            }

            return unchecked((ushort)(short)raw);
        }

        /// <summary>double 값을 부호 없는 16비트 레지스터로 변환한다.</summary>
        /// <param name="value">물리량.</param>
        /// <returns>레지스터 값.</returns>
        protected static ushort ToUnsignedRegister(double value)
        {
            double raw = Math.Round(value, MidpointRounding.AwayFromZero);

            if (raw > ushort.MaxValue)
            {
                raw = ushort.MaxValue;
            }
            else if (raw < 0.0)
            {
                raw = 0.0;
            }

            return (ushort)raw;
        }
    }
}
