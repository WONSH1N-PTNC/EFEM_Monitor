using System;
using System.IO.Ports;

namespace Esam.Communication.Modbus
{
    /// <summary>
    /// 시리얼 포트 및 Modbus 트랜잭션 파라미터. ports.json 의 항목 1건에 대응한다.
    /// </summary>
    public sealed class SerialPortSettings
    {
        /// <summary>포트 논리 ID(예: "BUS_A"). 로그·진단 표시에 사용한다.</summary>
        public string PortId { get; set; }

        /// <summary>OS 포트 이름(예: "COM3").</summary>
        public string PortName { get; set; }

        /// <summary>통신 속도 [bps]. 통신자료 기준 BUS_A=19200, BUS_B/C=38400.</summary>
        public int BaudRate { get; set; }

        /// <summary>패리티. 통신자료 기준 None.</summary>
        public Parity Parity { get; set; }

        /// <summary>데이터 비트. 통신자료 기준 8.</summary>
        public int DataBits { get; set; }

        /// <summary>스톱 비트. 통신자료 기준 1.</summary>
        public StopBits StopBits { get; set; }

        /// <summary>응답 대기 시간 [ms]. 초과 시 타임아웃으로 처리한다.</summary>
        public int ResponseTimeoutMs { get; set; }

        /// <summary>실패 시 재시도 횟수. 0 이면 재시도하지 않는다.</summary>
        public int RetryCount { get; set; }

        /// <summary>
        /// 프레임 간 무음구간에 추가할 여유 시간 [ms].
        /// USB-RS485 변환기는 지연이 커서 규격 t3.5 만으로는 부족한 경우가 있다.
        /// </summary>
        public double ExtraInterFrameDelayMs { get; set; }

        /// <summary>
        /// true 면 송신 시 RTS 를 직접 토글한다.
        /// 반이중 트랜시버가 자동 방향 전환(Auto-direction)을 지원하면 false 로 둔다.
        /// </summary>
        public bool ToggleRtsForTransmit { get; set; }

        /// <summary>통신자료 기준 기본값(19200, 8-N-1)으로 초기화한다.</summary>
        public SerialPortSettings()
        {
            BaudRate = 19200;
            Parity = Parity.None;
            DataBits = 8;
            StopBits = StopBits.One;
            ResponseTimeoutMs = 200;
            RetryCount = 2;
            ExtraInterFrameDelayMs = 1.0;
            ToggleRtsForTransmit = false;
        }

        /// <summary>규격 t3.5 와 여유 시간을 합한 실제 적용 무음구간 [ms].</summary>
        public double EffectiveInterFrameDelayMs
        {
            get { return ModbusTiming.InterFrameDelayMs(BaudRate) + Math.Max(0.0, ExtraInterFrameDelayMs); }
        }

        /// <summary>설정의 유효성을 검증한다.</summary>
        /// <param name="error">검증 실패 사유. 성공 시 null.</param>
        /// <returns>유효하면 true.</returns>
        public bool Validate(out string error)
        {
            if (string.IsNullOrEmpty(PortId))
            {
                error = "PortId 는 필수입니다.";
                return false;
            }

            if (string.IsNullOrEmpty(PortName))
            {
                error = "PortName(예: COM3)은 필수입니다.";
                return false;
            }

            if (BaudRate <= 0)
            {
                error = "BaudRate 는 0보다 커야 합니다.";
                return false;
            }

            if (DataBits < 5 || DataBits > 8)
            {
                error = "DataBits 는 5~8 범위여야 합니다.";
                return false;
            }

            if (ResponseTimeoutMs <= 0)
            {
                error = "ResponseTimeoutMs 는 0보다 커야 합니다.";
                return false;
            }

            if (RetryCount < 0)
            {
                error = "RetryCount 는 음수일 수 없습니다.";
                return false;
            }

            error = null;
            return true;
        }
    }
}
