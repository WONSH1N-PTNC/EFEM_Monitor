using System;
using System.Collections.Generic;
using Esam.Communication.Polling;
using Esam.Domain;
using Esam.Domain.Alarms;
using Esam.Domain.Configuration;
using Esam.Domain.Control;
using Esam.Domain.Models;

namespace Esam.Services
{
    /// <summary>
    /// 인터록 감시자. 폴링 스레드에서 즉시 판정하고 안전 지령을 최우선으로 투입한다.
    /// </summary>
    /// <remarks>
    /// <para><b>왜 폴링 스레드에서 실행하는가</b>(DESIGN.md 3.2 원칙 3).
    /// 인터록을 제어 엔진 타이머에 맡기면 최악의 경우
    /// "폴링 사이클(약 250ms) + 제어 주기 대기(200ms) + 큐 대기"가 누적된다.
    /// 배기 역압으로 센서 3이 상한을 넘은 상황에서 0.5초 이상 늦으면
    /// 안전 기능이라 부르기 어렵다.</para>
    /// <para>따라서 이 클래스는 <see cref="ModbusPortWorker.PollCompleted"/> 핸들러에서
    /// 동작한다. 그 이벤트는 워커 스레드에서 발생하므로, 판정 직후 같은 스레드에서
    /// 지령을 큐 최우선순위에 넣는다. 워커는 다음 읽기 전에 지령을 먼저 처리하므로
    /// 실질 지연은 <b>진행 중 트랜잭션 1건</b>으로 끝난다.</para>
    /// <para>판정에 쓰는 스냅샷은 방금 갱신된 <see cref="DataStore.Current"/> 다.
    /// 조립 비용은 딕셔너리 갱신 수준이므로 폴링 주기를 늘리지 않는다.</para>
    /// </remarks>
    public sealed class InterlockGuard
    {
        private readonly InterlockEvaluator _evaluator;
        private readonly ControlConfig _config;
        private readonly IClock _clock;
        private readonly List<ModbusPortWorker> _workers = new List<ModbusPortWorker>();
        private readonly object _gate = new object();

        private bool _wasTripped;

        /// <summary>인터록 감시자를 생성한다.</summary>
        /// <param name="evaluator">인터록 판정기. null 이면 기본 규칙으로 생성한다.</param>
        /// <param name="config">제어 설정(체인 정의와 센서 3 임계값 참조).</param>
        /// <param name="clock">시각 제공자.</param>
        /// <exception cref="ArgumentNullException">설정이 null 일 때.</exception>
        public InterlockGuard(InterlockEvaluator evaluator, ControlConfig config, IClock clock)
        {
            if (config == null)
            {
                throw new ArgumentNullException("config");
            }

            _evaluator = evaluator ?? new InterlockEvaluator(InterlockEvaluator.CreateDefaultRules());
            _config = config;
            _clock = clock ?? SystemClock.Instance;
        }

        /// <summary>현재 인터록이 발동 중인지 여부.</summary>
        public bool IsTripped
        {
            get { lock (_gate) { return _wasTripped; } }
        }

        /// <summary>전 체인 정지가 요구된 상태인지 여부.</summary>
        public bool RequiresSystemStop { get; private set; }

        /// <summary>누적 발동 횟수. 진단용.</summary>
        public long TripCount { get; private set; }

        /// <summary>마지막 판정 결과. 화면 표시용.</summary>
        public InterlockEvaluation LastEvaluation { get; private set; }

        /// <summary>인터록이 새로 발동했을 때 발생한다.</summary>
        public event EventHandler<InterlockTrippedEventArgs> Tripped;

        /// <summary>인터록이 모두 해제되었을 때 발생한다.</summary>
        public event EventHandler InterlockCleared;

        /// <summary>지령을 투입할 포트 워커를 등록한다.</summary>
        /// <param name="worker">포트 워커.</param>
        public void RegisterWorker(ModbusPortWorker worker)
        {
            if (worker == null)
            {
                return;
            }

            lock (_gate)
            {
                if (!_workers.Contains(worker))
                {
                    _workers.Add(worker);
                }
            }
        }

        /// <summary>
        /// 스냅샷을 판정하고, 발동 시 안전 지령을 즉시 투입한다.
        /// </summary>
        /// <param name="snapshot">방금 갱신된 스냅샷.</param>
        /// <returns>판정 결과.</returns>
        public InterlockEvaluation Evaluate(SystemSnapshot snapshot)
        {
            InterlockEvaluation evaluation = _evaluator.Evaluate(snapshot, _config, _clock.UtcNow);

            LastEvaluation = evaluation;
            RequiresSystemStop = evaluation.RequiresSystemStop;

            if (evaluation.HasTrip)
            {
                // 지령은 CommandPriority.Interlock 으로 생성되어 있으므로
                // 큐에서 자동으로 최우선 처리된다.
                Dispatch(evaluation.Commands);

                bool isNew;

                lock (_gate)
                {
                    isNew = !_wasTripped;
                    _wasTripped = true;

                    if (isNew)
                    {
                        TripCount++;
                    }
                }

                if (isNew)
                {
                    RaiseTripped(evaluation);
                }

                return evaluation;
            }

            bool wasCleared;

            lock (_gate)
            {
                wasCleared = _wasTripped;
                _wasTripped = false;
            }

            if (wasCleared)
            {
                RaiseCleared();
            }

            return evaluation;
        }

        /// <summary>
        /// 래치된 인터록을 해제한다. 물리 조건이 아직 성립 중이면 다음 스캔에서 다시 발동한다.
        /// </summary>
        /// <param name="ruleId">해제할 규칙 ID. null 이면 전체.</param>
        public void Reset(string ruleId)
        {
            _evaluator.Reset(ruleId);
        }

        /// <summary>적용 중인 인터록 규칙을 조회한다.</summary>
        /// <param name="ruleId">규칙 ID.</param>
        /// <returns>규칙. 없으면 null.</returns>
        /// <remarks>
        /// UI 가 실제 적용 중인 임계값을 표시하기 위해 쓴다.
        /// 작업자가 설정 화면에서 본 값과 안전 기능이 쓰는 값이 다를 수 있으므로,
        /// 화면에는 반드시 이쪽 값을 보여야 한다.
        /// </remarks>
        public InterlockRule FindRule(string ruleId)
        {
            return _evaluator.FindRule(ruleId);
        }

        /// <summary>등록된 모든 워커에 지령을 투입한다.</summary>
        /// <param name="commands">지령 목록.</param>
        /// <remarks>
        /// 어느 워커가 어느 디바이스를 담당하는지 여기서 따지지 않는다.
        /// 워커는 자기 포트에 없는 디바이스 지령을 무시하고 <c>CommandFailed</c> 로 알린다.
        /// 안전 경로에서 라우팅 판단을 하다 실수하는 것보다, 전 포트에 뿌리고
        /// 담당 워커가 집어가는 편이 확실하다.
        /// </remarks>
        private void Dispatch(IList<ActuatorCommand> commands)
        {
            if (commands == null || commands.Count == 0)
            {
                return;
            }

            List<ModbusPortWorker> targets;

            lock (_gate)
            {
                targets = new List<ModbusPortWorker>(_workers);
            }

            foreach (ModbusPortWorker worker in targets)
            {
                // 대기 중인 자동 지령을 먼저 버린다.
                // 큐가 장치 단위로 하위 우선순위를 정리하지만, 인터록 지령이 실행된 *뒤에*
                // 제어 엔진이 넣은 자동 지령은 그 정리를 거치지 않는다.
                // 남겨두면 인터록이 닫은 밸브를 다음 사이클에 다시 연다.
                worker.ClearAutomaticCommands();
                worker.EnqueueCommands(commands);
            }
        }

        /// <summary>발동 이벤트를 일으킨다.</summary>
        private void RaiseTripped(InterlockEvaluation evaluation)
        {
            EventHandler<InterlockTrippedEventArgs> handler = Tripped;

            if (handler == null)
            {
                return;
            }

            try
            {
                handler(this, new InterlockTrippedEventArgs(evaluation, _clock.UtcNow));
            }
            catch (Exception)
            {
                // 안전 지령은 이미 투입되었다. 구독자 예외로 폴링을 멈춰서는 안 된다.
            }
        }

        /// <summary>해제 이벤트를 일으킨다.</summary>
        private void RaiseCleared()
        {
            EventHandler handler = InterlockCleared;

            if (handler == null)
            {
                return;
            }

            try
            {
                handler(this, EventArgs.Empty);
            }
            catch (Exception)
            {
            }
        }
    }

    /// <summary>인터록 발동 정보.</summary>
    public sealed class InterlockTrippedEventArgs : EventArgs
    {
        /// <summary>판정 결과.</summary>
        public InterlockEvaluation Evaluation { get; private set; }

        /// <summary>발동 시각(UTC).</summary>
        public DateTime OccurredUtc { get; private set; }

        /// <summary>발동 정보를 생성한다.</summary>
        /// <param name="evaluation">판정 결과.</param>
        /// <param name="occurredUtc">발동 시각(UTC).</param>
        public InterlockTrippedEventArgs(InterlockEvaluation evaluation, DateTime occurredUtc)
        {
            Evaluation = evaluation;
            OccurredUtc = occurredUtc;
        }
    }
}
