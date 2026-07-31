using System;
using System.Threading;
using Esam.Communication.Abstractions;

namespace Esam.Communication.Diagnostics
{
    /// <summary>
    /// 포트 1개의 통신 품질 통계. Maintenance 화면의 통신 진단 탭에 표시한다.
    /// </summary>
    /// <remarks>
    /// <para>DESIGN.md 7.3 항목 5의 근거 데이터다. 특히 <see cref="LastCycleMs"/> 와
    /// <see cref="AverageResponseMs"/> 는 100ms 폴링 목표 달성 여부(2.2 B)를 현장에서
    /// 실측 검증하는 유일한 수단이므로 반드시 화면에 노출해야 한다.</para>
    /// <para>폴링 스레드가 쓰고 UI 스레드가 읽으므로 <see cref="Interlocked"/> 로 갱신한다.
    /// 통계는 정확성보다 저비용이 중요해 락을 쓰지 않는다.</para>
    /// </remarks>
    public sealed class PortStatistics
    {
        private long _totalTransactions;
        private long _successCount;
        private long _timeoutCount;
        private long _crcErrorCount;
        private long _malformedCount;
        private long _exceptionCount;
        private long _retryCount;
        private long _consecutiveFailures;
        private long _maxConsecutiveFailures;

        // double 은 Interlocked 로 직접 다루기 어려우므로 비트 패턴을 long 으로 보관한다.
        private long _totalResponseMsBits;
        private long _lastResponseMsBits;
        private long _maxResponseMsBits;
        private long _lastCycleMsBits;

        /// <summary>통계 대상 포트 ID.</summary>
        public string PortId { get; private set; }

        /// <summary>총 트랜잭션 수.</summary>
        public long TotalTransactions
        {
            get { return Interlocked.Read(ref _totalTransactions); }
        }

        /// <summary>성공한 트랜잭션 수.</summary>
        public long SuccessCount
        {
            get { return Interlocked.Read(ref _successCount); }
        }

        /// <summary>응답 시간 초과 횟수.</summary>
        public long TimeoutCount
        {
            get { return Interlocked.Read(ref _timeoutCount); }
        }

        /// <summary>CRC 오류 횟수. 증가 추세면 배선·종단저항·접지를 점검해야 한다.</summary>
        public long CrcErrorCount
        {
            get { return Interlocked.Read(ref _crcErrorCount); }
        }

        /// <summary>프레임 구조 오류 횟수.</summary>
        public long MalformedCount
        {
            get { return Interlocked.Read(ref _malformedCount); }
        }

        /// <summary>슬레이브 예외 응답 횟수.</summary>
        public long ExceptionCount
        {
            get { return Interlocked.Read(ref _exceptionCount); }
        }

        /// <summary>누적 재시도 횟수.</summary>
        public long RetryCount
        {
            get { return Interlocked.Read(ref _retryCount); }
        }

        /// <summary>현재 연속 실패 횟수. 인터록 IL-04 판정에 사용한다.</summary>
        public long ConsecutiveFailures
        {
            get { return Interlocked.Read(ref _consecutiveFailures); }
        }

        /// <summary>관측된 최대 연속 실패 횟수.</summary>
        public long MaxConsecutiveFailures
        {
            get { return Interlocked.Read(ref _maxConsecutiveFailures); }
        }

        /// <summary>트랜잭션 성공률 [%]. 트랜잭션이 없으면 100 을 반환한다.</summary>
        public double SuccessRatePercent
        {
            get
            {
                long total = TotalTransactions;
                return total == 0 ? 100.0 : SuccessCount * 100.0 / total;
            }
        }

        /// <summary>평균 응답 시간 [ms]. 성공 트랜잭션만 집계한다.</summary>
        public double AverageResponseMs
        {
            get
            {
                long success = SuccessCount;
                return success == 0 ? 0.0 : ReadDouble(ref _totalResponseMsBits) / success;
            }
        }

        /// <summary>직전 응답 시간 [ms].</summary>
        public double LastResponseMs
        {
            get { return ReadDouble(ref _lastResponseMsBits); }
        }

        /// <summary>관측된 최대 응답 시간 [ms].</summary>
        public double MaxResponseMs
        {
            get { return ReadDouble(ref _maxResponseMsBits); }
        }

        /// <summary>직전 폴링 사이클 1회 소요 시간 [ms]. 폴링 주기 목표 검증용.</summary>
        public double LastCycleMs
        {
            get { return ReadDouble(ref _lastCycleMsBits); }
        }

        /// <summary>통계 객체를 생성한다.</summary>
        /// <param name="portId">포트 ID.</param>
        public PortStatistics(string portId)
        {
            PortId = portId;
        }

        /// <summary>트랜잭션 결과를 통계에 반영한다.</summary>
        /// <param name="response">트랜잭션 결과.</param>
        public void Record(ModbusResponse response)
        {
            if (response == null)
            {
                return;
            }

            Interlocked.Increment(ref _totalTransactions);
            Interlocked.Add(ref _retryCount, response.RetryCount);

            if (response.IsSuccess)
            {
                Interlocked.Increment(ref _successCount);
                Interlocked.Exchange(ref _consecutiveFailures, 0);

                AddDouble(ref _totalResponseMsBits, response.ElapsedMs);
                Interlocked.Exchange(ref _lastResponseMsBits, BitConverter.DoubleToInt64Bits(response.ElapsedMs));
                MaxDouble(ref _maxResponseMsBits, response.ElapsedMs);
                return;
            }

            long consecutive = Interlocked.Increment(ref _consecutiveFailures);
            MaxLong(ref _maxConsecutiveFailures, consecutive);

            switch (response.FailureKind)
            {
                case ModbusFailureKind.Timeout:
                    Interlocked.Increment(ref _timeoutCount);
                    break;

                case ModbusFailureKind.CrcError:
                    Interlocked.Increment(ref _crcErrorCount);
                    break;

                case ModbusFailureKind.MalformedFrame:
                case ModbusFailureKind.UnexpectedEcho:
                    Interlocked.Increment(ref _malformedCount);
                    break;

                case ModbusFailureKind.ExceptionResponse:
                    Interlocked.Increment(ref _exceptionCount);
                    break;

                default:
                    break;
            }
        }

        /// <summary>폴링 사이클 1회의 소요 시간을 기록한다.</summary>
        /// <param name="cycleMs">사이클 소요 시간 [ms].</param>
        public void RecordCycle(double cycleMs)
        {
            Interlocked.Exchange(ref _lastCycleMsBits, BitConverter.DoubleToInt64Bits(cycleMs));
        }

        /// <summary>모든 통계를 초기화한다. 진단 화면의 초기화 버튼에 대응한다.</summary>
        public void Reset()
        {
            Interlocked.Exchange(ref _totalTransactions, 0);
            Interlocked.Exchange(ref _successCount, 0);
            Interlocked.Exchange(ref _timeoutCount, 0);
            Interlocked.Exchange(ref _crcErrorCount, 0);
            Interlocked.Exchange(ref _malformedCount, 0);
            Interlocked.Exchange(ref _exceptionCount, 0);
            Interlocked.Exchange(ref _retryCount, 0);
            Interlocked.Exchange(ref _consecutiveFailures, 0);
            Interlocked.Exchange(ref _maxConsecutiveFailures, 0);
            Interlocked.Exchange(ref _totalResponseMsBits, 0);
            Interlocked.Exchange(ref _lastResponseMsBits, 0);
            Interlocked.Exchange(ref _maxResponseMsBits, 0);
            Interlocked.Exchange(ref _lastCycleMsBits, 0);
        }

        /// <summary>비트 패턴으로 보관된 double 을 읽는다.</summary>
        /// <param name="bits">비트 패턴 필드.</param>
        /// <returns>double 값.</returns>
        private static double ReadDouble(ref long bits)
        {
            return BitConverter.Int64BitsToDouble(Interlocked.Read(ref bits));
        }

        /// <summary>비트 패턴으로 보관된 double 에 값을 누적한다(CAS 루프).</summary>
        /// <param name="bits">비트 패턴 필드.</param>
        /// <param name="value">더할 값.</param>
        private static void AddDouble(ref long bits, double value)
        {
            long current;
            long updated;

            do
            {
                current = Interlocked.Read(ref bits);
                updated = BitConverter.DoubleToInt64Bits(
                    BitConverter.Int64BitsToDouble(current) + value);
            }
            while (Interlocked.CompareExchange(ref bits, updated, current) != current);
        }

        /// <summary>비트 패턴으로 보관된 double 을 최댓값으로 갱신한다(CAS 루프).</summary>
        /// <param name="bits">비트 패턴 필드.</param>
        /// <param name="value">후보 값.</param>
        private static void MaxDouble(ref long bits, double value)
        {
            long current;

            do
            {
                current = Interlocked.Read(ref bits);
                if (BitConverter.Int64BitsToDouble(current) >= value)
                {
                    return;
                }
            }
            while (Interlocked.CompareExchange(
                       ref bits, BitConverter.DoubleToInt64Bits(value), current) != current);
        }

        /// <summary>long 필드를 최댓값으로 갱신한다(CAS 루프).</summary>
        /// <param name="target">대상 필드.</param>
        /// <param name="value">후보 값.</param>
        private static void MaxLong(ref long target, long value)
        {
            long current;

            do
            {
                current = Interlocked.Read(ref target);
                if (current >= value)
                {
                    return;
                }
            }
            while (Interlocked.CompareExchange(ref target, value, current) != current);
        }
    }
}
