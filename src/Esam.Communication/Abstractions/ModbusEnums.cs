namespace Esam.Communication.Abstractions
{
    /// <summary>
    /// Modbus 함수 코드. 본 시스템에서 사용하는 4종만 정의한다.
    /// </summary>
    public enum ModbusFunctionCode : byte
    {
        /// <summary>Read Holding Registers (0x03). 읽기/쓰기 가능 레지스터 조회.</summary>
        ReadHoldingRegisters = 0x03,

        /// <summary>Read Input Registers (0x04). 읽기 전용 레지스터 조회.</summary>
        ReadInputRegisters = 0x04,

        /// <summary>Write Single Register (0x06).</summary>
        WriteSingleRegister = 0x06,

        /// <summary>Write Multiple Registers (0x10).</summary>
        WriteMultipleRegisters = 0x10
    }

    /// <summary>
    /// Modbus 예외 코드. 슬레이브가 함수코드에 0x80 을 더해 응답할 때 함께 오는 값이다.
    /// </summary>
    public enum ModbusExceptionCode : byte
    {
        /// <summary>예외 없음.</summary>
        None = 0x00,

        /// <summary>지원하지 않는 함수 코드.</summary>
        IllegalFunction = 0x01,

        /// <summary>존재하지 않는 레지스터 주소.</summary>
        IllegalDataAddress = 0x02,

        /// <summary>허용 범위를 벗어난 데이터 값.</summary>
        IllegalDataValue = 0x03,

        /// <summary>슬레이브 장치 내부 오류.</summary>
        SlaveDeviceFailure = 0x04,

        /// <summary>요청 접수됨(장시간 처리 중).</summary>
        Acknowledge = 0x05,

        /// <summary>슬레이브가 다른 요청 처리 중이라 바쁨.</summary>
        SlaveDeviceBusy = 0x06,

        /// <summary>메모리 패리티 오류.</summary>
        MemoryParityError = 0x08,

        /// <summary>게이트웨이 경로 사용 불가.</summary>
        GatewayPathUnavailable = 0x0A,

        /// <summary>게이트웨이 대상 장치 응답 없음.</summary>
        GatewayTargetFailedToRespond = 0x0B
    }

    /// <summary>
    /// 트랜잭션 실패 원인. 통신 진단 화면과 알람(P00, A10, A11) 판정에 사용한다.
    /// </summary>
    public enum ModbusFailureKind
    {
        /// <summary>실패하지 않음.</summary>
        None = 0,

        /// <summary>응답 시간 초과(슬레이브 무응답).</summary>
        Timeout = 1,

        /// <summary>CRC 불일치(전기적 노이즈, 종단저항 문제 등).</summary>
        CrcError = 2,

        /// <summary>응답 프레임 구조가 규격과 다름.</summary>
        MalformedFrame = 3,

        /// <summary>슬레이브가 예외 응답을 반환.</summary>
        ExceptionResponse = 4,

        /// <summary>응답의 슬레이브 ID 또는 함수코드가 요청과 불일치.</summary>
        UnexpectedEcho = 5,

        /// <summary>포트가 열려 있지 않거나 하드웨어 오류.</summary>
        PortError = 6,

        /// <summary>취소 요청으로 중단됨.</summary>
        Canceled = 7
    }
}
