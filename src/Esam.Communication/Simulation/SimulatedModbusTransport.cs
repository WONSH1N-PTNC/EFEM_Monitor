using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using Esam.Communication.Abstractions;
using Esam.Communication.Diagnostics;
using Esam.Communication.Modbus;

namespace Esam.Communication.Simulation
{
    /// <summary>시뮬레이션 전송 계층의 동작 옵션(장애 주입 포함).</summary>
    public sealed class SimulationTransportOptions
    {
        /// <summary>슬레이브 응답 지연 [ms]. 실제 장치의 처리 지연을 모사한다.</summary>
        public double SlaveResponseDelayMs { get; set; }

        /// <summary>실제 통신 시간만큼 스레드를 대기시킬지 여부. false 면 즉시 반환한다(테스트 고속화).</summary>
        public bool SimulateRealTimeDelay { get; set; }

        /// <summary>트랜잭션 타임아웃 발생 확률(0.0~1.0). 통신 장애 시나리오 테스트용.</summary>
        public double TimeoutProbability { get; set; }

        /// <summary>CRC 오류 발생 확률(0.0~1.0).</summary>
        public double CrcErrorProbability { get; set; }

        /// <summary>장애 주입 난수 시드.</summary>
        public int FaultSeed { get; set; }

        /// <summary>
        /// true 면 <c>Execute</c> 호출 사이의 실제 경과 시간만큼 플랜트를 진행시킨다.
        /// 단위테스트는 false 로 두고 <c>PlantModel.Advance</c> 를 직접 호출해 결정성을 확보한다.
        /// </summary>
        public bool AutoAdvancePlant { get; set; }

        /// <summary>통신 속도 [bps]. 트랜잭션 소요 시간 산출에만 쓰인다.</summary>
        public int BaudRate { get; set; }

        /// <summary>기본값으로 초기화한다(장애 없음, 실시간 지연 없음).</summary>
        public SimulationTransportOptions()
        {
            SlaveResponseDelayMs = 8.0;
            SimulateRealTimeDelay = false;
            TimeoutProbability = 0.0;
            CrcErrorProbability = 0.0;
            FaultSeed = 20260731;
            AutoAdvancePlant = false;
            BaudRate = 19200;
        }
    }

    /// <summary>
    /// 가상 플랜트를 Modbus 슬레이브 집합으로 노출하는 전송 계층.
    /// </summary>
    /// <remarks>
    /// <para><see cref="SerialPortModbusTransport"/> 와 동일한 <see cref="IModbusTransport"/> 를
    /// 구현하므로, 상위 계층은 시뮬레이션인지 실장비인지 구분하지 못한다.
    /// 이것이 하드웨어 입고와 레지스터 명세 확정을 기다리지 않고
    /// 제어 로직·HMI·로깅을 완성할 수 있는 근거다(DESIGN.md 10단계 S2).</para>
    /// <para>통신 실패 확률을 주입할 수 있으므로, 실장비에서 재현하기 어려운
    /// 타임아웃·CRC 오류 상황에서의 인터록(IL-04) 동작도 검증할 수 있다.</para>
    /// </remarks>
    public sealed class SimulatedModbusTransport : IModbusTransport
    {
        private readonly Dictionary<byte, ISimulatedSlave> _slaves = new Dictionary<byte, ISimulatedSlave>();
        private readonly SimulationTransportOptions _options;
        private readonly PlantModel _plant;
        private readonly Random _faultRandom;
        private readonly object _gate = new object();
        private readonly Stopwatch _sinceLastExecute = new Stopwatch();

        private bool _isOpen;
        private bool _disposed;

        /// <inheritdoc />
        public string PortId { get; private set; }

        /// <inheritdoc />
        public bool IsOpen
        {
            get { return _isOpen; }
        }

        /// <summary>이 포트의 통신 품질 통계.</summary>
        public PortStatistics Statistics { get; private set; }

        /// <summary>연결된 가상 플랜트.</summary>
        public PlantModel Plant
        {
            get { return _plant; }
        }

        /// <summary>시뮬레이션 전송 계층을 생성한다.</summary>
        /// <param name="portId">포트 논리 ID.</param>
        /// <param name="plant">가상 플랜트.</param>
        /// <param name="options">동작 옵션. null 이면 기본값.</param>
        /// <exception cref="ArgumentNullException">플랜트가 null 일 때.</exception>
        public SimulatedModbusTransport(
            string portId, PlantModel plant, SimulationTransportOptions options)
        {
            if (plant == null)
            {
                throw new ArgumentNullException("plant");
            }

            PortId = portId;
            _plant = plant;
            _options = options ?? new SimulationTransportOptions();
            _faultRandom = new Random(_options.FaultSeed);
            Statistics = new PortStatistics(portId);
        }

        /// <summary>슬레이브를 등록한다.</summary>
        /// <param name="slave">시뮬레이션 슬레이브.</param>
        /// <returns>메서드 체이닝을 위해 자신을 반환한다.</returns>
        /// <exception cref="ArgumentException">슬레이브 주소가 이미 등록되어 있을 때.</exception>
        public SimulatedModbusTransport AddSlave(ISimulatedSlave slave)
        {
            if (slave == null)
            {
                throw new ArgumentNullException("slave");
            }

            lock (_gate)
            {
                if (_slaves.ContainsKey(slave.SlaveId))
                {
                    // 실제 버스에서 ID 중복은 응답 충돌을 일으키는 치명적 배선 오류다.
                    // 시뮬레이션에서는 즉시 예외로 드러내 설정 단계에서 잡는다.
                    throw new ArgumentException(
                        string.Format(CultureInfo.InvariantCulture,
                            "포트 {0} 에 슬레이브 ID {1} 가 이미 등록되어 있습니다.", PortId, slave.SlaveId),
                        "slave");
                }

                _slaves[slave.SlaveId] = slave;
            }

            return this;
        }

        /// <summary>등록된 슬레이브를 조회한다.</summary>
        /// <param name="slaveId">슬레이브 주소.</param>
        /// <returns>슬레이브. 없으면 null.</returns>
        public ISimulatedSlave FindSlave(byte slaveId)
        {
            lock (_gate)
            {
                ISimulatedSlave slave;
                return _slaves.TryGetValue(slaveId, out slave) ? slave : null;
            }
        }

        /// <summary>
        /// 특정 슬레이브를 버스에서 일시적으로 제거한다. 통신 상실 시나리오 테스트용.
        /// </summary>
        /// <param name="slaveId">제거할 슬레이브 주소.</param>
        /// <returns>제거되었으면 true.</returns>
        public bool DetachSlave(byte slaveId)
        {
            lock (_gate)
            {
                return _slaves.Remove(slaveId);
            }
        }

        /// <inheritdoc />
        public void Open()
        {
            ThrowIfDisposed();
            _isOpen = true;
            _sinceLastExecute.Restart();
        }

        /// <inheritdoc />
        public void Close()
        {
            _isOpen = false;
            _sinceLastExecute.Reset();
        }

        /// <inheritdoc />
        public ModbusResponse Execute(ModbusRequest request, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();

            if (request == null)
            {
                throw new ArgumentNullException("request");
            }

            Stopwatch watch = Stopwatch.StartNew();

            if (!_isOpen)
            {
                return RecordAndReturn(ModbusResponse.Failure(
                    request, ModbusFailureKind.PortError,
                    string.Format(CultureInfo.InvariantCulture, "포트 {0} 가 열려 있지 않습니다.", PortId),
                    watch.Elapsed.TotalMilliseconds, 0));
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return RecordAndReturn(ModbusResponse.Failure(
                    request, ModbusFailureKind.Canceled, "취소 요청으로 중단되었습니다.",
                    watch.Elapsed.TotalMilliseconds, 0));
            }

            double transactionMs = EstimateTransactionMs(request);

            if (_options.SimulateRealTimeDelay && transactionMs > 0.0)
            {
                Thread.Sleep((int)Math.Ceiling(transactionMs));
            }

            lock (_gate)
            {
                // 플랜트 진행은 반드시 락 안에서 수행한다. PlantModel 은 스레드 안전하지 않으며
                // 아래 슬레이브 핸들러가 같은 상태를 읽고 쓰기 때문이다.
                AdvancePlantIfNeeded();

                // ── 장애 주입 ────────────────────────────────────────────────────
                if (NextFault(_options.TimeoutProbability))
                {
                    return RecordAndReturn(ModbusResponse.Failure(
                        request, ModbusFailureKind.Timeout, "주입된 타임아웃",
                        transactionMs, 0));
                }

                if (NextFault(_options.CrcErrorProbability))
                {
                    return RecordAndReturn(ModbusResponse.Failure(
                        request, ModbusFailureKind.CrcError, "주입된 CRC 오류",
                        transactionMs, 0));
                }

                ISimulatedSlave slave;
                if (!_slaves.TryGetValue(request.SlaveId, out slave))
                {
                    // 존재하지 않는 슬레이브는 실제 버스와 동일하게 무응답(타임아웃)이 된다.
                    return RecordAndReturn(ModbusResponse.Failure(
                        request, ModbusFailureKind.Timeout,
                        string.Format(CultureInfo.InvariantCulture,
                            "슬레이브 {0} 무응답(미등록).", request.SlaveId),
                        transactionMs, 0));
                }

                ModbusExceptionCode exceptionCode;

                if (request.IsWrite)
                {
                    if (!slave.TryWrite(request.StartAddress, request.Values, out exceptionCode))
                    {
                        return RecordAndReturn(ModbusResponse.Exception(
                            request, exceptionCode, transactionMs, 0));
                    }

                    return RecordAndReturn(ModbusResponse.Success(request, null, transactionMs, 0));
                }

                ushort[] values;
                if (!slave.TryRead(request.StartAddress, request.RegisterCount, out values, out exceptionCode))
                {
                    return RecordAndReturn(ModbusResponse.Exception(
                        request, exceptionCode, transactionMs, 0));
                }

                return RecordAndReturn(ModbusResponse.Success(request, values, transactionMs, 0));
            }
        }

        /// <summary>
        /// 실제 프레임 크기를 기준으로 트랜잭션 소요 시간을 산출한다.
        /// 실장비와 같은 기준으로 계산하므로, 시뮬레이션에서도 폴링 주기 예산을 검증할 수 있다.
        /// </summary>
        /// <param name="request">요청.</param>
        /// <returns>예상 소요 시간 [ms].</returns>
        private double EstimateTransactionMs(ModbusRequest request)
        {
            int requestBytes = ModbusRtuCodec.BuildRequest(request).Length;
            int responseBytes = ModbusRtuCodec.ExpectedResponseLength(request);

            return ModbusTiming.EstimateTransactionMs(
                requestBytes, responseBytes, _options.BaudRate, _options.SlaveResponseDelayMs);
        }

        /// <summary>자동 진행 옵션이 켜져 있으면 실제 경과 시간만큼 플랜트를 진행시킨다.</summary>
        private void AdvancePlantIfNeeded()
        {
            if (!_options.AutoAdvancePlant)
            {
                return;
            }

            if (!_sinceLastExecute.IsRunning)
            {
                _sinceLastExecute.Restart();
                return;
            }

            double dtSec = _sinceLastExecute.Elapsed.TotalSeconds;
            _sinceLastExecute.Restart();

            if (dtSec > 0.0)
            {
                _plant.Advance(dtSec);
            }
        }

        /// <summary>확률에 따라 장애를 발생시킬지 판정한다.</summary>
        /// <param name="probability">발생 확률(0.0~1.0).</param>
        /// <returns>장애를 발생시켜야 하면 true.</returns>
        private bool NextFault(double probability)
        {
            if (probability <= 0.0)
            {
                return false;
            }

            return _faultRandom.NextDouble() < probability;
        }

        /// <summary>결과를 통계에 기록하고 반환한다.</summary>
        /// <param name="response">트랜잭션 결과.</param>
        /// <returns>같은 결과.</returns>
        private ModbusResponse RecordAndReturn(ModbusResponse response)
        {
            Statistics.Record(response);
            return response;
        }

        /// <summary>객체가 해제되었는지 확인한다.</summary>
        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException("SimulatedModbusTransport");
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
        }
    }
}
