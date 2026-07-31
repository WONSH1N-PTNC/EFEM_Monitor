using System;
using System.Threading;

namespace Esam.Communication.Abstractions
{
    /// <summary>
    /// Modbus 전송 계층 추상화. 포트 1개(= 독립 RS-485 버스 1개)에 대응한다.
    /// </summary>
    /// <remarks>
    /// <para><b>구현체는 반드시 트랜잭션을 직렬화해야 한다.</b>
    /// RS-485 는 반이중이므로 두 트랜잭션이 겹치면 프레임이 깨진다.</para>
    /// <para>실제 하드웨어용 <c>SerialPortModbusTransport</c> 와
    /// 시뮬레이션용 <c>SimulatedModbusTransport</c> 가 이 인터페이스를 함께 구현하므로,
    /// 상위 계층 코드는 하드웨어 유무를 구분하지 않는다.
    /// 이것이 하드웨어 입고 전에 제어 로직과 HMI 를 완성할 수 있는 근거다.</para>
    /// </remarks>
    public interface IModbusTransport : IDisposable
    {
        /// <summary>포트 식별자(예: "BUS_A"). 로그와 진단 화면에 사용한다.</summary>
        string PortId { get; }

        /// <summary>포트가 열려 통신 가능한 상태인지 여부.</summary>
        bool IsOpen { get; }

        /// <summary>포트를 연다. 이미 열려 있으면 아무 동작도 하지 않는다.</summary>
        /// <exception cref="ModbusTransportException">포트를 열 수 없을 때.</exception>
        void Open();

        /// <summary>포트를 닫는다.</summary>
        void Close();

        /// <summary>
        /// 트랜잭션 1건을 수행한다. 응답 수신 또는 최종 실패까지 블로킹된다.
        /// </summary>
        /// <param name="request">요청.</param>
        /// <param name="cancellationToken">취소 토큰.</param>
        /// <returns>트랜잭션 결과. 통신 실패도 예외 없이 결과로 반환된다.</returns>
        ModbusResponse Execute(ModbusRequest request, CancellationToken cancellationToken);
    }

    /// <summary>
    /// 전송 계층의 복구 불가 오류(포트 열기 실패, 하드웨어 부재 등).
    /// 트랜잭션 단위 실패는 예외가 아니라 <see cref="ModbusResponse"/> 로 표현한다.
    /// </summary>
    [Serializable]
    public class ModbusTransportException : Exception
    {
        /// <summary>기본 생성자.</summary>
        public ModbusTransportException()
        {
        }

        /// <summary>메시지를 지정해 생성한다.</summary>
        /// <param name="message">오류 메시지.</param>
        public ModbusTransportException(string message)
            : base(message)
        {
        }

        /// <summary>메시지와 내부 예외를 지정해 생성한다.</summary>
        /// <param name="message">오류 메시지.</param>
        /// <param name="innerException">내부 예외.</param>
        public ModbusTransportException(string message, Exception innerException)
            : base(message, innerException)
        {
        }

        /// <summary>직렬화 생성자.</summary>
        /// <param name="info">직렬화 정보.</param>
        /// <param name="context">직렬화 컨텍스트.</param>
        protected ModbusTransportException(
            System.Runtime.Serialization.SerializationInfo info,
            System.Runtime.Serialization.StreamingContext context)
            : base(info, context)
        {
        }
    }
}
