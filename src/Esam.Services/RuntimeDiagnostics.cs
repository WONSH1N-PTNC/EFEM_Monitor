using System;
using System.Collections.Generic;
using System.Globalization;

namespace Esam.Services
{
    /// <summary>런타임 장애의 종류.</summary>
    public enum RuntimeFaultKind
    {
        /// <summary>스냅샷 조립 또는 안전 판정 중 예외가 발생했다.</summary>
        EvaluationFailed = 0,

        /// <summary>인터록 지령이 장치에 전달되지 않았다.</summary>
        InterlockCommandFailed = 1,

        /// <summary>인터록이 발동했는데 액추에이터가 안전 위치로 가지 않았다.</summary>
        InterlockNotEffective = 2
    }

    /// <summary>런타임 장애 정보.</summary>
    public sealed class RuntimeFaultEventArgs : EventArgs
    {
        /// <summary>장애 종류.</summary>
        public RuntimeFaultKind Kind { get; private set; }

        /// <summary>사람이 읽을 수 있는 설명.</summary>
        public string Detail { get; private set; }

        /// <summary>연속 발생 횟수.</summary>
        public int ConsecutiveCount { get; private set; }

        /// <summary>원인 예외. 없으면 null.</summary>
        public Exception Exception { get; private set; }

        /// <summary>발생 시각(UTC).</summary>
        public DateTime OccurredUtc { get; private set; }

        /// <summary>장애 정보를 생성한다.</summary>
        /// <param name="kind">장애 종류.</param>
        /// <param name="detail">설명.</param>
        /// <param name="consecutiveCount">연속 발생 횟수.</param>
        /// <param name="exception">원인 예외.</param>
        /// <param name="occurredUtc">발생 시각(UTC).</param>
        public RuntimeFaultEventArgs(
            RuntimeFaultKind kind,
            string detail,
            int consecutiveCount,
            Exception exception,
            DateTime occurredUtc)
        {
            Kind = kind;
            Detail = detail;
            ConsecutiveCount = consecutiveCount;
            Exception = exception;
            OccurredUtc = occurredUtc;
        }
    }

    /// <summary>
    /// 안전 경로의 실패를 세고, 연속 실패가 임계를 넘으면 알린다.
    /// </summary>
    /// <remarks>
    /// <para>이 클래스가 존재하는 이유는 두 가지 실패가 <b>흔적 없이 사라지고 있었기</b> 때문이다.</para>
    /// <list type="number">
    ///   <item><description><b>판정 예외</b>. 폴링 완료 처리에서 예외가 나면
    ///     포트 워커의 <c>catch (Exception) { }</c> 로 흘러가 버려졌다. 워커는 살아남지만
    ///     인터록 평가는 그 사이클부터 수행되지 않는다. 예외가 결정적이면
    ///     <b>인터록이 영구히 꺼진 채</b> 로그도 알람도 카운터도 없이 운전이 계속된다.</description></item>
    ///   <item><description><b>지령 실패</b>. <c>CommandFailed</c> 구독자가 하나도 없었다.
    ///     인터록의 <c>CloseValve</c> 는 위치 설정 → PR0 이동 2단 시퀀스인데,
    ///     두 번째가 타임아웃하면 밸브는 <b>전혀 움직이지 않는다.</b>
    ///     그런데 <c>Tripped</c> 이벤트는 이미 "인터록이 처리됐다" 고 알린 뒤다.</description></item>
    /// </list>
    /// <para>안전 기능이 동작하지 못하는 상황은 <b>조용히 지나가면 안 된다.</b>
    /// 실패를 세고, 연속되면 상위에 알려 SafeStop 으로 보낸다.
    /// 한 번의 타임아웃으로 장비를 세우는 것은 과하므로 임계를 둔다.</para>
    /// <para>이 클래스는 스레드 안전하다. 포트 워커 3스레드가 동시에 기록한다.</para>
    /// </remarks>
    public sealed class RuntimeDiagnostics
    {
        private readonly object _gate = new object();
        private readonly Dictionary<string, int> _commandFailures =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        private int _consecutiveEvaluationFailures;
        private long _totalEvaluationFailures;
        private long _totalInterlockCommandFailures;
        private Exception _lastEvaluationException;
        private string _lastDetail;

        /// <summary>장애를 생성한다.</summary>
        /// <param name="evaluationFailureThreshold">판정 예외 연속 허용 횟수.</param>
        /// <param name="commandFailureThreshold">인터록 지령 실패 연속 허용 횟수.</param>
        public RuntimeDiagnostics(int evaluationFailureThreshold, int commandFailureThreshold)
        {
            EvaluationFailureThreshold = evaluationFailureThreshold > 0
                ? evaluationFailureThreshold
                : 3;

            CommandFailureThreshold = commandFailureThreshold > 0
                ? commandFailureThreshold
                : 3;
        }

        /// <summary>기본 임계값으로 생성한다.</summary>
        /// <remarks>
        /// 3회는 250ms 폴링에서 약 750ms 다. 일시적 노이즈는 넘기고
        /// 지속적 장애는 1초 안에 잡는 절충점이다.
        /// </remarks>
        public RuntimeDiagnostics()
            : this(3, 3)
        {
        }

        /// <summary>판정 예외 연속 허용 횟수.</summary>
        public int EvaluationFailureThreshold { get; private set; }

        /// <summary>인터록 지령 실패 연속 허용 횟수.</summary>
        public int CommandFailureThreshold { get; private set; }

        /// <summary>현재 연속된 판정 예외 횟수.</summary>
        public int ConsecutiveEvaluationFailures
        {
            get { lock (_gate) { return _consecutiveEvaluationFailures; } }
        }

        /// <summary>누적 판정 예외 횟수.</summary>
        public long TotalEvaluationFailures
        {
            get { lock (_gate) { return _totalEvaluationFailures; } }
        }

        /// <summary>누적 인터록 지령 실패 횟수.</summary>
        public long TotalInterlockCommandFailures
        {
            get { lock (_gate) { return _totalInterlockCommandFailures; } }
        }

        /// <summary>마지막 판정 예외. 없으면 null.</summary>
        public Exception LastEvaluationException
        {
            get { lock (_gate) { return _lastEvaluationException; } }
        }

        /// <summary>마지막 장애 설명. 없으면 null.</summary>
        public string LastDetail
        {
            get { lock (_gate) { return _lastDetail; } }
        }

        /// <summary>임계를 넘는 장애가 확인되면 발생한다.</summary>
        public event EventHandler<RuntimeFaultEventArgs> FaultDetected;

        /// <summary>판정이 정상 수행되었음을 기록한다.</summary>
        public void RecordEvaluationSuccess()
        {
            lock (_gate)
            {
                _consecutiveEvaluationFailures = 0;
            }
        }

        /// <summary>
        /// 판정 중 예외가 발생했음을 기록한다.
        /// </summary>
        /// <param name="exception">발생한 예외.</param>
        /// <param name="nowUtc">발생 시각(UTC).</param>
        /// <returns>연속 횟수가 임계를 넘었으면 true.</returns>
        public bool RecordEvaluationFailure(Exception exception, DateTime nowUtc)
        {
            int count;
            string detail;

            lock (_gate)
            {
                _consecutiveEvaluationFailures++;
                _totalEvaluationFailures++;
                _lastEvaluationException = exception;

                count = _consecutiveEvaluationFailures;
                detail = string.Format(
                    CultureInfo.InvariantCulture,
                    "안전 판정 중 예외가 발생해 이번 사이클의 인터록·알람 평가가 수행되지 않았습니다: {0}",
                    exception == null ? "(원인 미상)" : exception.Message);

                _lastDetail = detail;
            }

            if (count < EvaluationFailureThreshold)
            {
                return false;
            }

            RaiseFault(RuntimeFaultKind.EvaluationFailed, detail, count, exception, nowUtc);
            return true;
        }

        /// <summary>
        /// 인터록 지령이 실패했음을 기록한다.
        /// </summary>
        /// <param name="deviceId">대상 디바이스 ID.</param>
        /// <param name="reason">실패 사유.</param>
        /// <param name="nowUtc">발생 시각(UTC).</param>
        /// <returns>같은 디바이스에서 연속 실패가 임계를 넘었으면 true.</returns>
        /// <remarks>
        /// 디바이스별로 센다. 한 대가 계속 실패하는 것과 여러 대가 한 번씩 실패하는 것은
        /// 원인이 다르고, 전자가 훨씬 위험하다.
        /// </remarks>
        public bool RecordInterlockCommandFailure(string deviceId, string reason, DateTime nowUtc)
        {
            string key = deviceId ?? "(unknown)";
            int count;
            string detail;

            lock (_gate)
            {
                int previous;
                _commandFailures.TryGetValue(key, out previous);

                count = previous + 1;
                _commandFailures[key] = count;
                _totalInterlockCommandFailures++;

                detail = string.Format(
                    CultureInfo.InvariantCulture,
                    "인터록 지령이 {0} 에 전달되지 않았습니다({1}회 연속): {2}",
                    key, count, reason ?? "(사유 미상)");

                _lastDetail = detail;
            }

            if (count < CommandFailureThreshold)
            {
                return false;
            }

            RaiseFault(RuntimeFaultKind.InterlockCommandFailed, detail, count, null, nowUtc);
            return true;
        }

        /// <summary>지정 디바이스의 지령 실패 연속 횟수를 초기화한다.</summary>
        /// <param name="deviceId">디바이스 ID.</param>
        public void RecordCommandSuccess(string deviceId)
        {
            if (string.IsNullOrEmpty(deviceId))
            {
                return;
            }

            lock (_gate)
            {
                _commandFailures.Remove(deviceId);
            }
        }

        /// <summary>
        /// 인터록이 발동했는데 액추에이터가 안전 위치로 가지 않았음을 알린다.
        /// </summary>
        /// <param name="detail">설명.</param>
        /// <param name="nowUtc">발생 시각(UTC).</param>
        public void ReportInterlockNotEffective(string detail, DateTime nowUtc)
        {
            lock (_gate)
            {
                _lastDetail = detail;
            }

            RaiseFault(RuntimeFaultKind.InterlockNotEffective, detail, 1, null, nowUtc);
        }

        /// <summary>모든 카운터를 초기화한다. 장애 해제 후 호출한다.</summary>
        public void Reset()
        {
            lock (_gate)
            {
                _consecutiveEvaluationFailures = 0;
                _commandFailures.Clear();
                _lastEvaluationException = null;
                _lastDetail = null;
            }
        }

        /// <summary>진단 요약을 만든다.</summary>
        /// <returns>요약 문자열.</returns>
        public string Describe()
        {
            lock (_gate)
            {
                return string.Format(
                    CultureInfo.InvariantCulture,
                    "판정 예외 {0}회(연속 {1}) / 인터록 지령 실패 {2}회{3}",
                    _totalEvaluationFailures,
                    _consecutiveEvaluationFailures,
                    _totalInterlockCommandFailures,
                    _lastDetail == null ? string.Empty : " — " + _lastDetail);
            }
        }

        /// <summary>장애 이벤트를 일으킨다.</summary>
        /// <param name="kind">장애 종류.</param>
        /// <param name="detail">설명.</param>
        /// <param name="count">연속 횟수.</param>
        /// <param name="exception">원인 예외.</param>
        /// <param name="nowUtc">발생 시각(UTC).</param>
        private void RaiseFault(
            RuntimeFaultKind kind, string detail, int count, Exception exception, DateTime nowUtc)
        {
            EventHandler<RuntimeFaultEventArgs> handler = FaultDetected;

            if (handler == null)
            {
                return;
            }

            try
            {
                handler(this, new RuntimeFaultEventArgs(kind, detail, count, exception, nowUtc));
            }
            catch (Exception)
            {
                // 장애 통보 중 예외가 다시 폴링 스레드를 죽이면 안 된다.
                // 이 catch 가 삼키는 것은 구독자의 문제이지 안전 판정 자체가 아니다.
            }
        }
    }
}
