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
        InterlockNotEffective = 2,

        /// <summary>측정값을 신뢰할 수 없어 인터록이 판정 자체를 하지 못하고 있다.</summary>
        InterlockBlind = 3
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

        /// <summary>이미 알린 장애의 래치 키 집합. 해소될 때까지 다시 알리지 않는다.</summary>
        private readonly HashSet<string> _escalated =
            new HashSet<string>(StringComparer.Ordinal);

        private int _consecutiveEvaluationFailures;
        private int _consecutiveBlindCycles;
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

            BlindCycleThreshold = 8;
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

        /// <summary>
        /// 인터록이 판정하지 못한 상태를 허용할 연속 사이클 수. 기본 8.
        /// </summary>
        /// <remarks>
        /// 250 ms 폴링에서 약 2초다. 지령 실패(3회)보다 관대한 이유는,
        /// 기동 직후나 센서 재연결 중에는 일시적으로 판정할 수 없는 것이 정상이기 때문이다.
        /// </remarks>
        public int BlindCycleThreshold { get; set; }

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
                _escalated.Remove("eval");
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

            RaiseFault(RuntimeFaultKind.EvaluationFailed, "eval", detail, count, exception, nowUtc);
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

            RaiseFault(RuntimeFaultKind.InterlockCommandFailed, "cmd|" + key, detail, count, null, nowUtc);
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

                // 이 디바이스가 다시 실패하면 새 장애로 알린다.
                _escalated.Remove("cmd|" + deviceId);

                // 지령이 통했다는 것은 액추에이터를 움직일 수단이 살아 있다는 뜻이다.
                // 실효 실패를 다시 판정할 수 있는 상태가 되었으므로 래치를 푼다.
                _escalated.Remove("effect");
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

            RaiseFault(RuntimeFaultKind.InterlockNotEffective, "effect", detail, 1, null, nowUtc);
        }

        /// <summary>
        /// 인터록이 측정값을 신뢰할 수 없어 판정하지 못하고 있음을 기록한다.
        /// </summary>
        /// <param name="detail">설명.</param>
        /// <param name="nowUtc">발생 시각(UTC).</param>
        /// <returns>연속 횟수가 임계를 넘었으면 true.</returns>
        /// <remarks>
        /// <b>"발동하지 않음" 과 "판정하지 못함" 은 다르다.</b> 후자는 인터록이 눈을 감은 상태다.
        /// 센서 3 을 읽지 못하면 배기 상실을 감지할 수단이 없으므로,
        /// 안전 기능이 동작하지 못하는 상태로 취급한다.
        /// </remarks>
        public bool RecordInterlockBlind(string detail, DateTime nowUtc)
        {
            int count;

            lock (_gate)
            {
                _consecutiveBlindCycles++;
                count = _consecutiveBlindCycles;
                _lastDetail = detail;
            }

            if (count < BlindCycleThreshold)
            {
                return false;
            }

            RaiseFault(RuntimeFaultKind.InterlockBlind, "blind", detail, count, null, nowUtc);
            return true;
        }

        /// <summary>인터록이 정상적으로 판정했음을 기록한다.</summary>
        public void RecordInterlockJudged()
        {
            lock (_gate)
            {
                _consecutiveBlindCycles = 0;
                _escalated.Remove("blind");
            }
        }

        /// <summary>현재 연속된 판정 불가 사이클 수.</summary>
        public int ConsecutiveBlindCycles
        {
            get { lock (_gate) { return _consecutiveBlindCycles; } }
        }

        /// <summary>모든 카운터를 초기화한다. 장애 해제 후 호출한다.</summary>
        public void Reset()
        {
            lock (_gate)
            {
                _consecutiveEvaluationFailures = 0;
                _consecutiveBlindCycles = 0;
                _commandFailures.Clear();
                _escalated.Clear();
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

        /// <summary>
        /// 장애 이벤트를 일으킨다. 같은 장애가 해소되기 전에는 한 번만 낸다.
        /// </summary>
        /// <param name="kind">장애 종류.</param>
        /// <param name="latchKey">해소 판정 단위. 같은 키로는 다시 내지 않는다.</param>
        /// <param name="detail">설명.</param>
        /// <param name="count">연속 횟수.</param>
        /// <param name="exception">원인 예외.</param>
        /// <param name="nowUtc">발생 시각(UTC).</param>
        /// <remarks>
        /// <para><b>래치가 없으면 되먹임이 성립한다.</b> 구독자는 이 이벤트를 받아
        /// 파킹 지령을 투입한다. 그 지령이 또 실패하면 여기로 돌아오고,
        /// 매번 이벤트를 내면 지령이 실행보다 빠르게 쌓여 폴링 사이클이 끝나지 않는다.
        /// 통신·인터록 판정·화면 갱신이 전부 멈춘다.</para>
        /// <para>세는 것과 알리는 것을 분리했다. 누적 카운터는 계속 올라가므로
        /// 실패가 지속되는 사실은 기록에 남는다. 다만 <b>상태 전이에서만</b> 알린다.
        /// 장애가 해소되면(성공 기록 또는 <see cref="Reset"/>) 래치가 풀려
        /// 다음 발생을 다시 알린다.</para>
        /// <para>인터록 <b>판정</b>은 반대로 수준 기반이다(D2). 판정을 놓치면
        /// 위험이 남지만, 장애 <b>보고</b>를 반복하는 것은 정보를 늘리지 않고
        /// 시스템을 멈춘다. 성질이 다르므로 다르게 다룬다.</para>
        /// </remarks>
        private void RaiseFault(
            RuntimeFaultKind kind,
            string latchKey,
            string detail,
            int count,
            Exception exception,
            DateTime nowUtc)
        {
            lock (_gate)
            {
                if (!_escalated.Add(latchKey))
                {
                    return;
                }
            }

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
