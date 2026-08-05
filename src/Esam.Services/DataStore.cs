using System;
using System.Threading;
using Esam.Communication.Polling;
using Esam.Domain;
using Esam.Domain.Models;

namespace Esam.Services
{
    /// <summary>
    /// 시스템 상태의 단일 진실 공급원. 불변 스냅샷을 통째로 교체하는 방식으로 발행한다.
    /// </summary>
    /// <remarks>
    /// <para>DESIGN.md 3.2 스레딩 모델의 중심이다.</para>
    /// <list type="number">
    ///   <item><description>포트 워커 스레드가 <see cref="Apply"/> 로 수집값을 넣는다.
    ///     조립은 락 안에서 하지만, 락 구간은 딕셔너리 갱신뿐이라 매우 짧다.</description></item>
    ///   <item><description>완성된 스냅샷은 <see cref="Volatile.Write{T}"/> 로 교체한다.
    ///     교체가 원자적이므로 <see cref="Current"/> 를 읽는 쪽은 <b>락이 전혀 필요 없다.</b></description></item>
    ///   <item><description>UI 와 제어 엔진은 각자의 주기로 <see cref="Current"/> 를 <b>끌어간다(pull)</b>.
    ///     통신 스레드가 UI 스레드를 붙잡는 일이 없어 화면이 멈추지 않는다.</description></item>
    /// </list>
    /// <para>부분 갱신이 없으므로 "센서 1은 새 값, 센서 2는 이전 값"인 tearing 이
    /// 원천적으로 발생하지 않는다. 이것이 불변 스냅샷을 쓰는 이유다.</para>
    /// </remarks>
    public sealed class DataStore
    {
        private readonly SnapshotBuilder _builder;
        private readonly IClock _clock;
        private readonly object _gate = new object();

        private SystemSnapshot _current;
        private long _revision;
        private long _appliedCycles;

        /// <summary>데이터 저장소를 생성한다.</summary>
        /// <param name="builder">스냅샷 조립기.</param>
        /// <param name="clock">시각 제공자. null 이면 시스템 시계를 쓴다.</param>
        /// <exception cref="ArgumentNullException">조립기가 null 일 때.</exception>
        public DataStore(SnapshotBuilder builder, IClock clock)
        {
            if (builder == null)
            {
                throw new ArgumentNullException("builder");
            }

            _builder = builder;
            _clock = clock ?? SystemClock.Instance;
            _current = SystemSnapshot.CreateEmpty(_clock.UtcNow);
        }

        /// <summary>
        /// 최신 스냅샷. 어느 스레드에서든 락 없이 읽을 수 있다.
        /// </summary>
        public SystemSnapshot Current
        {
            get { return Volatile.Read(ref _current); }
        }

        /// <summary>스냅샷 교체 횟수. UI 가 변경 여부를 값 비교로 판정할 때 쓴다.</summary>
        public long Revision
        {
            get { return Interlocked.Read(ref _revision); }
        }

        /// <summary>반영된 폴링 사이클 수. 진단용.</summary>
        public long AppliedCycles
        {
            get { return Interlocked.Read(ref _appliedCycles); }
        }

        /// <summary>새 스냅샷이 발행되면 발생한다.</summary>
        /// <remarks>
        /// 구독자는 <b>가볍고 예외를 던지지 않아야</b> 한다.
        /// 이 이벤트는 포트 워커 스레드에서 발생하므로, 무거운 작업을 하면 폴링이 늦어진다.
        /// UI 는 이 이벤트를 구독하지 말고 <see cref="Current"/> 를 주기적으로 끌어가야 한다.
        /// </remarks>
        public event EventHandler<SnapshotPublishedEventArgs> SnapshotPublished;

        /// <summary>
        /// 폴링 결과를 반영하고 새 스냅샷을 발행한다.
        /// </summary>
        /// <param name="args">폴링 완료 결과.</param>
        /// <param name="control">제어 상태 요약. null 이면 직전 값을 유지한다.</param>
        /// <param name="alarms">알람 요약. null 이면 직전 값을 유지한다.</param>
        /// <returns>발행된 스냅샷.</returns>
        public SystemSnapshot Apply(
            PollCompletedEventArgs args, ControlStatus control, AlarmSummary alarms)
        {
            SystemSnapshot published;

            lock (_gate)
            {
                _builder.Apply(args);

                SystemSnapshot previous = _current;

                published = _builder.Build(
                    control ?? previous.Control,
                    alarms ?? previous.Alarms,
                    _clock.UtcNow);

                Volatile.Write(ref _current, published);
            }

            Interlocked.Increment(ref _revision);
            Interlocked.Increment(ref _appliedCycles);

            RaisePublished(published);
            return published;
        }

        /// <summary>
        /// 수집값 없이 제어·알람 요약만 갱신해 재발행한다.
        /// 제어 엔진이 상태를 바꿨을 때 화면에 즉시 반영하기 위해 사용한다.
        /// </summary>
        /// <param name="control">제어 상태 요약.</param>
        /// <param name="alarms">알람 요약.</param>
        /// <returns>발행된 스냅샷.</returns>
        public SystemSnapshot Republish(ControlStatus control, AlarmSummary alarms)
        {
            SystemSnapshot published;

            lock (_gate)
            {
                SystemSnapshot previous = _current;

                published = _builder.Build(
                    control ?? previous.Control,
                    alarms ?? previous.Alarms,
                    _clock.UtcNow);

                Volatile.Write(ref _current, published);
            }

            Interlocked.Increment(ref _revision);

            RaisePublished(published);
            return published;
        }

        /// <summary>
        /// 포트 워커의 <c>PollCompleted</c> 에 연결한다.
        /// 여러 포트를 각각 연결해도 안전하다.
        /// </summary>
        /// <param name="worker">포트 워커.</param>
        /// <param name="controlProvider">현재 제어 상태를 제공하는 함수. null 허용.</param>
        /// <param name="alarmProvider">현재 알람 요약을 제공하는 함수. null 허용.</param>
        public void AttachTo(
            ModbusPortWorker worker,
            Func<ControlStatus> controlProvider,
            Func<AlarmSummary> alarmProvider)
        {
            if (worker == null)
            {
                throw new ArgumentNullException("worker");
            }

            worker.PollCompleted += (sender, e) => Apply(
                e,
                controlProvider == null ? null : controlProvider(),
                alarmProvider == null ? null : alarmProvider());
        }

        /// <summary>발행 이벤트를 일으킨다.</summary>
        /// <param name="snapshot">발행된 스냅샷.</param>
        private void RaisePublished(SystemSnapshot snapshot)
        {
            EventHandler<SnapshotPublishedEventArgs> handler = SnapshotPublished;

            if (handler == null)
            {
                return;
            }

            try
            {
                handler(this, new SnapshotPublishedEventArgs(snapshot, Revision));
            }
            catch (Exception)
            {
                // 구독자의 예외가 폴링 루프를 멈추게 해서는 안 된다.
                // 통신이 끊기는 것보다 로깅 한 건을 놓치는 편이 낫다.
            }
        }
    }

    /// <summary>스냅샷 발행 정보.</summary>
    public sealed class SnapshotPublishedEventArgs : EventArgs
    {
        /// <summary>발행된 스냅샷.</summary>
        public SystemSnapshot Snapshot { get; private set; }

        /// <summary>발행 회차.</summary>
        public long Revision { get; private set; }

        /// <summary>발행 정보를 생성한다.</summary>
        /// <param name="snapshot">스냅샷.</param>
        /// <param name="revision">발행 회차.</param>
        public SnapshotPublishedEventArgs(SystemSnapshot snapshot, long revision)
        {
            Snapshot = snapshot;
            Revision = revision;
        }
    }
}
