using System;
using System.Collections.Generic;
using Esam.Domain;
using Esam.Domain.Alarms;
using Esam.Domain.Configuration;
using Esam.Domain.Models;

namespace Esam.Services
{
    /// <summary>
    /// 알람 평가 및 이력 관리.
    /// </summary>
    /// <remarks>
    /// <para>인터록과 달리 알람은 <b>통보 기능</b>이다. 제어 주기(기본 200ms)로 평가하면 충분하고,
    /// 폴링 스레드를 붙잡을 이유가 없다. 그래서 인터록은 폴링 스레드에서,
    /// 알람은 별도 호출로 분리했다.</para>
    /// <para>이력은 메모리에 최근 N건만 보관한다. 영구 보존은 S8 SQLite 로거의 몫이다.
    /// 24시간 연속 운전에서 알람이 수천 건 쌓여도 메모리가 늘지 않아야 한다.</para>
    /// <para>이 클래스는 스레드 안전하다. 제어 엔진이 평가하고 UI 가 이력을 읽는다.</para>
    /// </remarks>
    public sealed class AlarmService
    {
        /// <summary>메모리에 보관할 최대 이력 건수.</summary>
        private const int MaxHistory = 500;

        private readonly AlarmEvaluator _evaluator;
        private readonly ControlConfig _config;
        private readonly IClock _clock;
        private readonly object _gate = new object();
        private readonly List<AlarmHistoryEntry> _history = new List<AlarmHistoryEntry>();

        private AlarmSummary _summary = AlarmSummary.None;

        /// <summary>알람 서비스를 생성한다.</summary>
        /// <param name="rules">알람 규칙 목록.</param>
        /// <param name="config">제어 설정(OutOfBand 판정에 필요).</param>
        /// <param name="clock">시각 제공자.</param>
        /// <exception cref="ArgumentNullException">규칙이 null 일 때.</exception>
        public AlarmService(IEnumerable<AlarmRule> rules, ControlConfig config, IClock clock)
        {
            if (rules == null)
            {
                throw new ArgumentNullException("rules");
            }

            _evaluator = new AlarmEvaluator(rules);
            _config = config;
            _clock = clock ?? SystemClock.Instance;
        }

        /// <summary>등록된 규칙 수.</summary>
        public int RuleCount
        {
            get { return _evaluator.RuleCount; }
        }

        /// <summary>현재 알람 요약. DataStore 가 스냅샷에 넣는다.</summary>
        public AlarmSummary Summary
        {
            get { lock (_gate) { return _summary; } }
        }

        /// <summary>알람이 새로 발생하면 발생한다.</summary>
        public event EventHandler<AlarmRaisedEventArgs> AlarmRaised;

        /// <summary>스냅샷을 평가하고 요약을 갱신한다.</summary>
        /// <param name="snapshot">현재 스냅샷.</param>
        /// <returns>이번 평가에서 새로 발생한 알람 목록.</returns>
        public IList<AlarmState> Evaluate(SystemSnapshot snapshot)
        {
            DateTime nowUtc = _clock.UtcNow;
            IList<AlarmState> raised = _evaluator.Evaluate(snapshot, _config, nowUtc);

            AlarmSummary summary = _evaluator.BuildSummary();

            lock (_gate)
            {
                _summary = summary;

                foreach (AlarmState state in raised)
                {
                    _history.Add(new AlarmHistoryEntry(
                        state.Rule.Code,
                        state.Rule.Name,
                        state.Rule.Severity,
                        state.TriggerValue,
                        state.Detail,
                        state.RaisedUtc));
                }

                // 오래된 이력을 잘라내 메모리가 무한히 늘지 않게 한다.
                if (_history.Count > MaxHistory)
                {
                    _history.RemoveRange(0, _history.Count - MaxHistory);
                }
            }

            foreach (AlarmState state in raised)
            {
                RaiseAlarm(state, nowUtc);
            }

            return raised;
        }

        /// <summary>최근 이력을 최신 순으로 반환한다.</summary>
        /// <param name="maxCount">최대 건수. 0 이하면 전체.</param>
        /// <returns>이력 목록.</returns>
        public IList<AlarmHistoryEntry> GetHistory(int maxCount)
        {
            lock (_gate)
            {
                List<AlarmHistoryEntry> result = new List<AlarmHistoryEntry>(_history);
                result.Reverse();

                if (maxCount > 0 && result.Count > maxCount)
                {
                    result.RemoveRange(maxCount, result.Count - maxCount);
                }

                return result;
            }
        }

        /// <summary>지정 알람 상태를 조회한다.</summary>
        /// <param name="code">알람 코드.</param>
        /// <returns>알람 상태. 없으면 null.</returns>
        public AlarmState FindState(string code)
        {
            return _evaluator.FindState(code);
        }

        /// <summary>모든 활성 알람을 확인(Ack) 처리한다.</summary>
        public void AcknowledgeAll()
        {
            _evaluator.AcknowledgeAll();

            lock (_gate)
            {
                _summary = _evaluator.BuildSummary();
            }
        }

        /// <summary>Manual 정책 알람을 해제한다.</summary>
        /// <param name="code">해제할 알람 코드. null 이면 전체.</param>
        public void Reset(string code)
        {
            _evaluator.Reset(code);

            lock (_gate)
            {
                _summary = _evaluator.BuildSummary();
            }
        }

        /// <summary>알람 발생 이벤트를 일으킨다.</summary>
        private void RaiseAlarm(AlarmState state, DateTime nowUtc)
        {
            EventHandler<AlarmRaisedEventArgs> handler = AlarmRaised;

            if (handler == null)
            {
                return;
            }

            try
            {
                handler(this, new AlarmRaisedEventArgs(state, nowUtc));
            }
            catch (Exception)
            {
                // 구독자 예외가 제어 루프를 멈추게 해서는 안 된다.
            }
        }
    }

    /// <summary>알람 이력 1건.</summary>
    public sealed class AlarmHistoryEntry
    {
        /// <summary>알람 코드.</summary>
        public string Code { get; private set; }

        /// <summary>알람 이름.</summary>
        public string Name { get; private set; }

        /// <summary>심각도.</summary>
        public AlarmSeverity Severity { get; private set; }

        /// <summary>발생 시점의 측정값.</summary>
        public double Value { get; private set; }

        /// <summary>발생 사유 설명.</summary>
        public string Detail { get; private set; }

        /// <summary>발생 시각(UTC).</summary>
        public DateTime RaisedUtc { get; private set; }

        /// <summary>이력 항목을 생성한다.</summary>
        /// <param name="code">알람 코드.</param>
        /// <param name="name">알람 이름.</param>
        /// <param name="severity">심각도.</param>
        /// <param name="value">측정값.</param>
        /// <param name="detail">사유 설명.</param>
        /// <param name="raisedUtc">발생 시각(UTC).</param>
        public AlarmHistoryEntry(
            string code, string name, AlarmSeverity severity,
            double value, string detail, DateTime raisedUtc)
        {
            Code = code;
            Name = name;
            Severity = severity;
            Value = value;
            Detail = detail;
            RaisedUtc = raisedUtc;
        }
    }

    /// <summary>알람 발생 정보.</summary>
    public sealed class AlarmRaisedEventArgs : EventArgs
    {
        /// <summary>발생한 알람 상태.</summary>
        public AlarmState State { get; private set; }

        /// <summary>발생 시각(UTC).</summary>
        public DateTime OccurredUtc { get; private set; }

        /// <summary>알람 발생 정보를 생성한다.</summary>
        /// <param name="state">알람 상태.</param>
        /// <param name="occurredUtc">발생 시각(UTC).</param>
        public AlarmRaisedEventArgs(AlarmState state, DateTime occurredUtc)
        {
            State = state;
            OccurredUtc = occurredUtc;
        }
    }
}
