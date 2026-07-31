using System;
using System.Diagnostics;
using System.Globalization;
using System.IO.Ports;
using System.Threading;
using Esam.Communication.Abstractions;
using Esam.Communication.Diagnostics;

namespace Esam.Communication.Modbus
{
    /// <summary>
    /// 실제 RS-485 시리얼 포트를 사용하는 Modbus RTU 전송 계층.
    /// </summary>
    /// <remarks>
    /// <para><b>반이중 직렬화</b>: 모든 트랜잭션이 <see cref="SemaphoreSlim"/> 하나를 통과하므로
    /// 동시에 두 프레임이 버스에 실리는 일이 없다. RS-485 에서 이것을 놓치면
    /// 간헐적 CRC 오류로 나타나 원인 추적이 매우 어려워진다.</para>
    /// <para><b>동기 블로킹 설계</b>: 포트당 전용 스레드 1개가 순차 폴링하는 구조이므로
    /// async/await 보다 동기 블로킹이 단순하고 타이밍 제어가 정확하다.
    /// 상위 계층(ModbusPortWorker)이 이 객체를 Task 안에서 호출한다.</para>
    /// <para><b>무음구간 보장</b>: 직전 프레임 종료 후 t3.5 가 지나기 전에는 다음 요청을 보내지 않는다.
    /// 이를 지키지 않으면 슬레이브가 두 프레임을 하나로 이어붙여 해석한다.</para>
    /// </remarks>
    public sealed class SerialPortModbusTransport : IModbusTransport
    {
        /// <summary>RTU 프레임 최대 크기 [byte].</summary>
        private const int MaxFrameLength = 256;

        private readonly SerialPortSettings _settings;
        private readonly SemaphoreSlim _busLock = new SemaphoreSlim(1, 1);
        private readonly byte[] _receiveBuffer = new byte[MaxFrameLength];
        private readonly Stopwatch _sinceLastFrame = new Stopwatch();

        private SerialPort _port;
        private bool _disposed;

        /// <inheritdoc />
        public string PortId
        {
            get { return _settings.PortId; }
        }

        /// <inheritdoc />
        public bool IsOpen
        {
            get
            {
                SerialPort port = _port;
                return port != null && port.IsOpen;
            }
        }

        /// <summary>이 포트의 통신 품질 통계.</summary>
        public PortStatistics Statistics { get; private set; }

        /// <summary>전송 계층을 생성한다. 생성 시점에는 포트를 열지 않는다.</summary>
        /// <param name="settings">포트 설정.</param>
        /// <exception cref="ArgumentNullException">설정이 null 일 때.</exception>
        /// <exception cref="ArgumentException">설정이 유효하지 않을 때.</exception>
        public SerialPortModbusTransport(SerialPortSettings settings)
        {
            if (settings == null)
            {
                throw new ArgumentNullException("settings");
            }

            string error;
            if (!settings.Validate(out error))
            {
                throw new ArgumentException(error, "settings");
            }

            _settings = settings;
            Statistics = new PortStatistics(settings.PortId);
        }

        /// <inheritdoc />
        public void Open()
        {
            ThrowIfDisposed();

            if (IsOpen)
            {
                return;
            }

            SerialPort port = null;

            try
            {
                port = new SerialPort(
                    _settings.PortName,
                    _settings.BaudRate,
                    _settings.Parity,
                    _settings.DataBits,
                    _settings.StopBits);

                port.ReadTimeout = _settings.ResponseTimeoutMs;
                port.WriteTimeout = _settings.ResponseTimeoutMs;
                port.Handshake = Handshake.None;

                // 자동 방향 전환 트랜시버는 DTR/RTS 를 건드리지 않는 편이 안전하다.
                port.DtrEnable = false;
                port.RtsEnable = _settings.ToggleRtsForTransmit;

                port.Open();
                _port = port;
                _sinceLastFrame.Restart();
            }
            catch (Exception ex)
            {
                // Open 실패 시 이미 생성된 SerialPort 를 반드시 해제한다.
                // 그러지 않으면 포트가 OS 에 점유된 채로 남아 재시도조차 실패한다.
                if (port != null)
                {
                    port.Dispose();
                }

                throw new ModbusTransportException(
                    string.Format(CultureInfo.InvariantCulture,
                        "포트 {0}({1}) 을(를) 열 수 없습니다: {2}",
                        _settings.PortId, _settings.PortName, ex.Message),
                    ex);
            }
        }

        /// <inheritdoc />
        public void Close()
        {
            SerialPort port = _port;
            _port = null;

            if (port == null)
            {
                return;
            }

            try
            {
                if (port.IsOpen)
                {
                    port.Close();
                }
            }
            catch (Exception)
            {
                // 종료 경로에서는 예외를 무시한다. 포트를 닫지 못해도 프로그램은 계속 종료되어야 한다.
            }
            finally
            {
                port.Dispose();
            }
        }

        /// <inheritdoc />
        public ModbusResponse Execute(ModbusRequest request, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();

            if (request == null)
            {
                throw new ArgumentNullException("request");
            }

            Stopwatch totalWatch = Stopwatch.StartNew();

            if (!IsOpen)
            {
                ModbusResponse notOpen = ModbusResponse.Failure(
                    request, ModbusFailureKind.PortError,
                    string.Format(CultureInfo.InvariantCulture, "포트 {0} 가 열려 있지 않습니다.", PortId),
                    totalWatch.Elapsed.TotalMilliseconds, 0);

                Statistics.Record(notOpen);
                return notOpen;
            }

            // 버스 점유. 대기 중 취소되면 즉시 반환한다.
            try
            {
                _busLock.Wait(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // 다른 모든 종료 경로와 동일하게 통계에 기록해야 트랜잭션 수가 어긋나지 않는다.
                ModbusResponse canceled = ModbusResponse.Failure(
                    request, ModbusFailureKind.Canceled, "취소 요청으로 중단되었습니다.",
                    totalWatch.Elapsed.TotalMilliseconds, 0);

                Statistics.Record(canceled);
                return canceled;
            }

            try
            {
                ModbusResponse response = ExecuteLocked(request, cancellationToken, totalWatch);
                Statistics.Record(response);
                return response;
            }
            finally
            {
                _busLock.Release();
            }
        }

        /// <summary>버스를 점유한 상태에서 재시도를 포함한 트랜잭션을 수행한다.</summary>
        /// <param name="request">요청.</param>
        /// <param name="cancellationToken">취소 토큰.</param>
        /// <param name="totalWatch">전체 소요 시간 측정용 스톱워치.</param>
        /// <returns>트랜잭션 결과.</returns>
        private ModbusResponse ExecuteLocked(
            ModbusRequest request, CancellationToken cancellationToken, Stopwatch totalWatch)
        {
            byte[] frame = ModbusRtuCodec.BuildRequest(request);
            int expectedLength = ModbusRtuCodec.ExpectedResponseLength(request);

            FrameParseResult lastResult = null;
            int attempt = 0;

            while (attempt <= _settings.RetryCount)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    // 이번 시도는 아직 수행하지 않았으므로 실제 재시도 횟수는 attempt - 1 이다.
                    return ModbusResponse.Failure(
                        request, ModbusFailureKind.Canceled, "취소 요청으로 중단되었습니다.",
                        totalWatch.Elapsed.TotalMilliseconds, attempt == 0 ? 0 : attempt - 1);
                }

                WaitForInterFrameSilence();

                try
                {
                    int received = SendAndReceive(frame, expectedLength);
                    lastResult = ModbusRtuCodec.ParseResponse(request, _receiveBuffer, received);

                    if (lastResult.IsSuccess)
                    {
                        return ModbusResponse.Success(
                            request, lastResult.Registers,
                            totalWatch.Elapsed.TotalMilliseconds, attempt);
                    }

                    // 슬레이브 예외 응답은 통신이 정상이라는 뜻이므로 재시도하지 않는다.
                    // 같은 요청을 다시 보내도 같은 예외가 돌아올 뿐이다.
                    if (lastResult.FailureKind == ModbusFailureKind.ExceptionResponse)
                    {
                        return ModbusResponse.Exception(
                            request, lastResult.ExceptionCode,
                            totalWatch.Elapsed.TotalMilliseconds, attempt);
                    }
                }
                catch (TimeoutException)
                {
                    lastResult = FrameParseResult.Fail(
                        ModbusFailureKind.Timeout,
                        string.Format(CultureInfo.InvariantCulture,
                            "응답 시간 초과({0} ms).", _settings.ResponseTimeoutMs));
                }
                catch (InvalidOperationException ex)
                {
                    // 포트가 닫혔거나 USB 변환기가 분리된 경우. 재시도해도 의미가 없다.
                    return ModbusResponse.Failure(
                        request, ModbusFailureKind.PortError, ex.Message,
                        totalWatch.Elapsed.TotalMilliseconds, attempt);
                }
                catch (System.IO.IOException ex)
                {
                    return ModbusResponse.Failure(
                        request, ModbusFailureKind.PortError, ex.Message,
                        totalWatch.Elapsed.TotalMilliseconds, attempt);
                }

                attempt++;
            }

            ModbusFailureKind kind = lastResult != null ? lastResult.FailureKind : ModbusFailureKind.Timeout;
            string detail = lastResult != null ? lastResult.Detail : "알 수 없는 통신 실패";

            return ModbusResponse.Failure(
                request, kind, detail, totalWatch.Elapsed.TotalMilliseconds, attempt - 1);
        }

        /// <summary>
        /// 요청 프레임을 송신하고 응답을 수신한다.
        /// </summary>
        /// <param name="frame">송신할 요청 프레임.</param>
        /// <param name="expectedLength">기대하는 정상 응답 길이 [byte].</param>
        /// <returns>수신한 바이트 수.</returns>
        /// <exception cref="TimeoutException">응답 시간이 초과되었을 때.</exception>
        private int SendAndReceive(byte[] frame, int expectedLength)
        {
            SerialPort port = _port;
            if (port == null || !port.IsOpen)
            {
                throw new InvalidOperationException(
                    string.Format(CultureInfo.InvariantCulture, "포트 {0} 가 닫혔습니다.", PortId));
            }

            // 이전 트랜잭션의 잔여 바이트나 노이즈가 남아 있으면 프레임 해석이 어긋난다.
            port.DiscardInBuffer();
            port.DiscardOutBuffer();

            if (_settings.ToggleRtsForTransmit)
            {
                port.RtsEnable = true;
            }

            try
            {
                port.Write(frame, 0, frame.Length);
            }
            finally
            {
                if (_settings.ToggleRtsForTransmit)
                {
                    // 송신 버퍼가 비워질 때까지 기다려야 마지막 바이트가 잘리지 않는다.
                    SpinWaitForTransmitComplete(port, frame.Length);
                    port.RtsEnable = false;
                }
            }

            // 정상 응답 길이만큼 먼저 채운다.
            // 예외 응답은 5바이트로 더 짧으므로, 함수코드(frame[1])를 확보한 시점(2바이트)에
            // 예외 여부를 판별해 목표 길이를 줄인다. 그러지 않으면 오지 않을 바이트를 기다려
            // 매번 타임아웃이 발생한다.
            int received = 0;
            int target = expectedLength;

            while (received < target)
            {
                int read = port.Read(_receiveBuffer, received, target - received);
                if (read <= 0)
                {
                    throw new TimeoutException("응답 스트림이 종료되었습니다.");
                }

                received += read;

                if (received >= 2 && target == expectedLength)
                {
                    bool isException = (_receiveBuffer[1] & ModbusRtuCodec.ExceptionFlag) != 0;
                    if (isException)
                    {
                        target = ModbusRtuCodec.ExceptionResponseLength;
                        if (received >= target)
                        {
                            // 한 번의 Read 로 예외 프레임보다 많이 읽혔을 수 있다(회선 노이즈 등).
                            // 초과분을 그대로 넘기면 CRC 계산 구간이 어긋나 CrcError 로 오판하므로 잘라낸다.
                            received = target;
                            break;
                        }
                    }
                }
            }

            _sinceLastFrame.Restart();
            return received;
        }

        /// <summary>
        /// 직전 프레임 종료 후 규격 무음구간이 경과할 때까지 대기한다.
        /// </summary>
        private void WaitForInterFrameSilence()
        {
            double requiredMs = _settings.EffectiveInterFrameDelayMs;

            if (!_sinceLastFrame.IsRunning)
            {
                _sinceLastFrame.Restart();
                return;
            }

            double elapsedMs = _sinceLastFrame.Elapsed.TotalMilliseconds;
            double remainingMs = requiredMs - elapsedMs;

            if (remainingMs <= 0.0)
            {
                return;
            }

            // Thread.Sleep 의 분해능은 약 15ms 여서 1~2ms 대기에는 쓸 수 없다.
            // 짧은 구간은 SpinWait 로, 긴 구간은 Sleep 으로 처리해 CPU 낭비와 정확도를 절충한다.
            if (remainingMs > 5.0)
            {
                Thread.Sleep((int)(remainingMs - 2.0));
            }

            SpinWait spin = new SpinWait();
            while (_sinceLastFrame.Elapsed.TotalMilliseconds < requiredMs)
            {
                spin.SpinOnce();
            }
        }

        /// <summary>RTS 토글 모드에서 송신 완료를 대기한다.</summary>
        /// <param name="port">시리얼 포트.</param>
        /// <param name="byteCount">송신한 바이트 수.</param>
        private void SpinWaitForTransmitComplete(SerialPort port, int byteCount)
        {
            double expectedMs = ModbusTiming.TransmissionTimeMs(byteCount, _settings.BaudRate);
            Stopwatch watch = Stopwatch.StartNew();
            SpinWait spin = new SpinWait();

            while (watch.Elapsed.TotalMilliseconds < expectedMs)
            {
                if (port.BytesToWrite == 0 && watch.Elapsed.TotalMilliseconds >= expectedMs * 0.9)
                {
                    break;
                }

                spin.SpinOnce();
            }
        }

        /// <summary>객체가 해제되었는지 확인한다.</summary>
        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException("SerialPortModbusTransport");
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            Close();

            // _busLock 은 의도적으로 Dispose 하지 않는다.
            // 진행 중인 트랜잭션의 finally 블록이 Release 를 호출하는 순간 해제되어 있으면
            // ObjectDisposedException 이 발생한다. SemaphoreSlim 은 WaitHandle 을 만들지 않는 한
            // 비관리 자원을 보유하지 않으므로 해제하지 않아도 누수가 없다.
        }
    }
}
