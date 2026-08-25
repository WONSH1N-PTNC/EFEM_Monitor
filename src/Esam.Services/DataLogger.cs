using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using Esam.Domain;
using Esam.Domain.Configuration;
using Esam.Domain.Models;
using Esam.Persistence;

namespace Esam.Services
{
    /// <summary>
    /// 스냅샷과 알람 발생을 일별 SQLite 파일에 적재한다.
    /// </summary>
    /// <remarks>
    /// <para><b>왜 전용 스레드인가.</b> <see cref="DataStore.SnapshotPublished"/> 는
    /// <b>포트 워커 스레드</b>에서 발생한다. 그 스레드에서 디스크를 만지면 폴링 주기가
    /// 디스크 사정에 묶인다. 기록이 제어를 멈추게 하는 것은 이 설계에서 가장 나쁜 결과다.
    /// 그래서 이벤트 처리는 큐에 넣는 것까지만 하고, 쓰기는 전용 스레드가 한다.</para>
    /// <para><b>왜 유한 큐인가.</b> 무한 큐를 두면 디스크가 멈췄을 때 메모리가 대신
    /// 차오르다가 프로그램이 죽는다. 죽는 것은 제어가 멈추는 것이다. 넘치면
    /// <b>새 것을 버리고 버린 수를 센다</b>. 버린 것을 세지 않으면 트렌드에 구멍이
    /// 난 사실을 아무도 모르고, 나중에 그 구간을 "아무 일도 없던 시간" 으로 읽는다.</para>
    /// <para><b>왜 스냅샷을 큐에 넣는가.</b> 트렌드 행으로 바꾸는 일(사전 조회 23회)을
    /// 워커 스레드에서 하지 않기 위해서다. <see cref="SystemSnapshot"/> 은 불변이므로
    /// 다른 스레드로 넘겨도 안전하다.</para>
    /// <para><b>실패는 운전을 막지 않는다.</b> 연속 실패가 이어지면 기록을 중단하고
    /// <b>Advisory</b> 구성 경고를 올린다. 기록은 통보 기능이므로 차단 경고로 올려
    /// 자동 운전을 막아서는 안 된다. 대신 조용히 실패하지도 않는다.</para>
    /// </remarks>
    public sealed class DataLogger : IDisposable
    {
        /// <summary>이 횟수만큼 연속 실패하면 기록을 중단한다.</summary>
        /// <remarks>
        /// 무한 재시도는 배치마다 예외를 던지며 CPU 를 태우고, 같은 경고로 화면을 덮는다.
        /// 한두 번은 일시적일 수 있으므로 즉시 포기하지도 않는다.
        /// </remarks>
        private const int MaxConsecutiveFailures = 5;

        /// <summary>리텐션 정리 주기 [시간].</summary>
        private const double PurgeIntervalHours = 24.0;

        private readonly LoggingConfig _config;
        private readonly SqliteLogStore _store;
        private readonly Action<ConfigWarning> _report;
        private readonly IClock _clock;

        private readonly BlockingCollection<SystemSnapshot> _snapshots;
        private readonly BlockingCollection<AlarmLogEntry> _alarms;

        private DataStore _attachedStore;
        private AlarmService _attachedAlarms;

        private Thread _thread;
        private long _droppedSnapshots;
        private long _droppedAlarms;
        private long _writtenRows;
        private int _consecutiveFailures;
        private volatile bool _halted;
        private volatile string _lastError;
        private bool _dropReported;
        private DateTime _lastPurgeUtc = DateTime.MinValue;
        private bool _disposed;

        /// <summary>로거를 만든다. 저장 폴더가 없으면 만든다.</summary>
        /// <param name="config">기록 설정.</param>
        /// <param name="report">구성 경고 보고 경로. null 이면 보고하지 않는다.</param>
        /// <param name="clock">시각 제공자. null 이면 시스템 시각.</param>
        /// <exception cref="ArgumentNullException">설정이 null 일 때.</exception>
        public DataLogger(LoggingConfig config, Action<ConfigWarning> report, IClock clock)
        {
            if (config == null)
            {
                throw new ArgumentNullException("config");
            }

            _config = config;
            _report = report;
            _clock = clock ?? SystemClock.Instance;

            _store = new SqliteLogStore(config.Folder, config.RetentionDays);

            _snapshots = new BlockingCollection<SystemSnapshot>(
                new ConcurrentQueue<SystemSnapshot>(), Math.Max(1, config.QueueCapacity));

            // 알람은 드물다. 큐가 넘칠 상황은 기록이 이미 멈춘 상황이다.
            _alarms = new BlockingCollection<AlarmLogEntry>(
                new ConcurrentQueue<AlarmLogEntry>(), 1000);
        }

        /// <summary>지금 쓰고 있는 DB 파일 경로. 아직 쓰지 않았으면 null.</summary>
        public string CurrentFilePath
        {
            get { return _store.CurrentFilePath; }
        }

        /// <summary>적재한 트렌드 행 수.</summary>
        public long WrittenRows
        {
            get { return Interlocked.Read(ref _writtenRows); }
        }

        /// <summary>큐가 넘쳐 버린 스냅샷 수.</summary>
        public long DroppedSnapshots
        {
            get { return Interlocked.Read(ref _droppedSnapshots); }
        }

        /// <summary>큐가 넘쳐 버린 알람 수.</summary>
        public long DroppedAlarms
        {
            get { return Interlocked.Read(ref _droppedAlarms); }
        }

        /// <summary>기록이 중단되었는지 여부.</summary>
        public bool IsHalted
        {
            get { return _halted; }
        }

        /// <summary>마지막 실패 사유. 실패가 없으면 null.</summary>
        public string LastError
        {
            get { return _lastError; }
        }

        /// <summary>스냅샷·알람 발생을 구독한다.</summary>
        /// <param name="store">스냅샷 발행자.</param>
        /// <param name="alarms">알람 서비스. null 이면 알람 이력을 남기지 않는다.</param>
        /// <exception cref="ArgumentNullException">저장소가 null 일 때.</exception>
        public void AttachTo(DataStore store, AlarmService alarms)
        {
            if (store == null)
            {
                throw new ArgumentNullException("store");
            }

            Detach();

            _attachedStore = store;
            _attachedAlarms = alarms;

            store.SnapshotPublished += OnSnapshotPublished;

            if (alarms != null)
            {
                alarms.AlarmRaised += OnAlarmRaised;
            }
        }

        /// <summary>구독을 해제한다.</summary>
        public void Detach()
        {
            if (_attachedStore != null)
            {
                _attachedStore.SnapshotPublished -= OnSnapshotPublished;
                _attachedStore = null;
            }

            if (_attachedAlarms != null)
            {
                _attachedAlarms.AlarmRaised -= OnAlarmRaised;
                _attachedAlarms = null;
            }
        }

        /// <summary>적재 스레드를 시작한다. 이미 돌고 있으면 아무것도 하지 않는다.</summary>
        /// <exception cref="ObjectDisposedException">이미 정리되었을 때.</exception>
        public void Start()
        {
            ThrowIfDisposed();

            if (_thread != null)
            {
                return;
            }

            _thread = new Thread(Run);
            _thread.Name = "EsamDataLogger";

            // 배경 스레드로 둔다. 정상 종료는 Stop 이 큐를 비우고 끝낸다.
            // 강제 종료에서 스레드가 프로세스를 붙잡고 있으면 안 된다.
            _thread.IsBackground = true;
            _thread.Start();
        }

        /// <summary>남은 큐를 비우고 적재 스레드를 멈춘다.</summary>
        /// <param name="timeoutMs">스레드 종료 대기 시간 [ms]. 0 이하면 기다리지 않는다.</param>
        /// <remarks>
        /// <para>구독을 먼저 끊는다. 그러지 않으면 큐를 비우는 동안 새 스냅샷이 들어와
        /// 종료가 끝나지 않는다.</para>
        /// <para><b>폴링을 멈춘 뒤에 호출해야 한다.</b> 먼저 호출하면 마지막 몇 초가
        /// 기록되지 않는다. 그 몇 초는 종료 직전 구간이라 사고 분석에서 가장 중요하다.</para>
        /// </remarks>
        public void Stop(int timeoutMs)
        {
            Detach();

            if (!_snapshots.IsAddingCompleted)
            {
                _snapshots.CompleteAdding();
            }

            if (!_alarms.IsAddingCompleted)
            {
                _alarms.CompleteAdding();
            }

            Thread thread = _thread;

            if (thread != null && timeoutMs > 0)
            {
                thread.Join(timeoutMs);
            }

            _thread = null;
        }

        /// <summary>구독을 끊고 스레드와 파일을 정리한다.</summary>
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            Stop(2000);

            _snapshots.Dispose();
            _alarms.Dispose();
            _store.Dispose();
        }

        /// <summary>스냅샷을 적재 대기 큐에 넣는다.</summary>
        /// <param name="snapshot">스냅샷. null 이면 아무것도 하지 않는다.</param>
        /// <returns>큐에 들어갔으면 true. 넘쳐서 버렸으면 false.</returns>
        /// <remarks>
        /// ★ <b>이 메서드는 절대 막히지 않는다.</b> 포트 워커 스레드에서 호출되므로,
        /// 여기서 대기하면 폴링 주기가 디스크 사정에 묶인다. <c>TryAdd</c> 의 대기
        /// 시간을 0 으로 두는 것이 이 클래스에서 가장 중요한 한 줄이다.
        /// </remarks>
        public bool Enqueue(SystemSnapshot snapshot)
        {
            if (snapshot == null)
            {
                return false;
            }

            if (_halted || _snapshots.IsAddingCompleted)
            {
                return false;
            }

            try
            {
                if (_snapshots.TryAdd(snapshot, 0))
                {
                    return true;
                }
            }
            catch (ObjectDisposedException)
            {
                // ★ 순서가 중요하다. ObjectDisposedException 은
                // InvalidOperationException 의 하위형이므로 먼저 잡아야 한다.
                // 뒤에 두면 도달할 수 없는 절이 되어 컴파일되지 않는다(CS0160).
                return false;
            }
            catch (InvalidOperationException)
            {
                // 종료 중 CompleteAdding 과 겹쳤다. 큐가 넘쳐서 버린 것이 아니므로
                // 세지 않는다. 세면 정상 종료마다 "기록을 버렸다" 고 알리게 된다.
                return false;
            }

            Interlocked.Increment(ref _droppedSnapshots);
            return false;
        }

        /// <summary>스냅샷 발행을 받아 큐에 넣는다. 워커 스레드에서 호출된다.</summary>
        /// <param name="sender">발행자.</param>
        /// <param name="e">스냅샷 인자.</param>
        private void OnSnapshotPublished(object sender, SnapshotPublishedEventArgs e)
        {
            if (e != null)
            {
                Enqueue(e.Snapshot);
            }
        }

        /// <summary>알람 발생을 큐에 넣는다. 제어 스레드에서 호출된다.</summary>
        /// <param name="sender">발행자.</param>
        /// <param name="e">알람 인자.</param>
        private void OnAlarmRaised(object sender, AlarmRaisedEventArgs e)
        {
            if (_halted || e == null || e.State == null || e.State.Rule == null
                || _alarms.IsAddingCompleted)
            {
                return;
            }

            AlarmLogEntry entry = new AlarmLogEntry(
                e.State.Rule.Code,
                e.State.Rule.Severity,
                e.State.RaisedUtc,
                e.State.TriggerValue,
                e.State.Detail);

            try
            {
                if (!_alarms.TryAdd(entry, 0))
                {
                    Interlocked.Increment(ref _droppedAlarms);
                }
            }
            catch (ObjectDisposedException)
            {
                // 하위형이 먼저다(CS0160). 정리 중이므로 셀 이유가 없다.
            }
            catch (InvalidOperationException)
            {
                // 종료 중 CompleteAdding 과 겹쳤다. 넘침이 아니므로 세지 않는다.
            }
        }

        /// <summary>적재 스레드 본체.</summary>
        private void Run()
        {
            List<TrendRow> batch = new List<TrendRow>(Math.Max(1, _config.BatchRows));

            while (!_snapshots.IsCompleted)
            {
                FillBatch(batch);

                if (batch.Count > 0)
                {
                    WriteTrend(batch);
                    batch.Clear();
                }

                WriteAlarms();
                PurgeIfDue();
                ReportDropsOnce();
            }

            // 남은 알람은 스냅샷 큐가 먼저 끝나도 비워야 한다.
            WriteAlarms();
        }

        /// <summary>배치 하나를 모은다. 행 수 상한이나 시간 상한에서 끝난다.</summary>
        /// <param name="batch">채울 배치.</param>
        /// <remarks>
        /// 시간 측정에 <see cref="Stopwatch"/> 를 쓴다. 시각을 뺄셈하면 운전 중 시스템
        /// 시각이 바뀔 때(NTP 보정·수동 변경) 마감이 과거나 먼 미래가 되어 배치가
        /// 즉시 끊기거나 영원히 안 끊긴다.
        /// </remarks>
        private void FillBatch(List<TrendRow> batch)
        {
            Stopwatch elapsed = Stopwatch.StartNew();
            int budgetMs = Math.Max(50, _config.BatchMs);
            int limit = Math.Max(1, _config.BatchRows);

            while (batch.Count < limit)
            {
                int remain = budgetMs - (int)elapsed.ElapsedMilliseconds;

                if (remain <= 0)
                {
                    return;
                }

                SystemSnapshot snapshot;

                try
                {
                    if (!_snapshots.TryTake(out snapshot, remain))
                    {
                        // 시간 만료이거나 큐가 끝났다. 둘 다 모은 것을 쓸 시점이다.
                        return;
                    }
                }
                catch (ObjectDisposedException)
                {
                    return;
                }

                try
                {
                    batch.Add(TrendRow.FromSnapshot(snapshot));
                }
                catch (ArgumentException)
                {
                    // 변환할 수 없는 스냅샷 하나로 배치 전체를 버리지 않는다.
                    Interlocked.Increment(ref _droppedSnapshots);
                }
            }
        }

        /// <summary>배치를 파일에 쓴다.</summary>
        /// <param name="batch">쓸 배치.</param>
        private void WriteTrend(List<TrendRow> batch)
        {
            if (_halted)
            {
                return;
            }

            try
            {
                int written = _store.WriteTrend(batch);

                Interlocked.Add(ref _writtenRows, written);
                _consecutiveFailures = 0;
            }
            catch (Exception error)
            {
                // ★ 예외 종류를 좁히지 않는다.
                //
                // 여기서 새어 나간 예외는 배경 스레드를 죽이고, 그러면 기록이 조용히
                // 멈춘다. 디스크 만료·권한·잠금·드라이버까지 종류가 다양하고,
                // 목록을 다 적었다고 믿는 것보다 전부 잡고 사유를 남기는 쪽이 낫다.
                OnFailure("트렌드", error);
            }
        }

        /// <summary>큐에 쌓인 알람 이력을 쓴다.</summary>
        private void WriteAlarms()
        {
            if (_halted)
            {
                return;
            }

            List<AlarmLogEntry> pending = new List<AlarmLogEntry>();
            AlarmLogEntry entry;

            try
            {
                while (_alarms.TryTake(out entry, 0))
                {
                    pending.Add(entry);
                }
            }
            catch (ObjectDisposedException)
            {
                return;
            }

            if (pending.Count == 0)
            {
                return;
            }

            try
            {
                _store.WriteAlarms(pending);
                _consecutiveFailures = 0;
            }
            catch (Exception error)
            {
                OnFailure("알람 이력", error);
            }
        }

        /// <summary>정리 주기가 되었으면 오래된 파일을 지운다.</summary>
        private void PurgeIfDue()
        {
            DateTime nowUtc = _clock.UtcNow;

            if (_lastPurgeUtc != DateTime.MinValue
                && (nowUtc - _lastPurgeUtc).TotalHours < PurgeIntervalHours)
            {
                return;
            }

            _lastPurgeUtc = nowUtc;

            try
            {
                PurgeResult result = _store.Purge(nowUtc);

                if (result.Failed.Count > 0)
                {
                    // 지우지 못한 것을 알린다. 조용히 넘기면 디스크가 차는 것을
                    // 아무도 모르고, 리텐션이 동작한다고 믿게 된다.
                    Report(ConfigWarning.Advisory(
                        "LOG-03",
                        string.Format(
                            CultureInfo.InvariantCulture,
                            "오래된 로그 파일 {0}개를 지우지 못했습니다: {1}",
                            result.Failed.Count, result.Failed[0]),
                        "해당 파일을 열어 둔 프로그램이 있는지 확인하십시오."));
                }
            }
            catch (Exception error)
            {
                OnFailure("리텐션 정리", error);
            }
        }

        /// <summary>버린 것이 생겼으면 한 번만 알린다.</summary>
        /// <remarks>
        /// 매번 알리면 같은 경고로 화면이 덮인다. 누적 수는
        /// <see cref="DroppedSnapshots"/> 로 언제든 읽을 수 있다.
        /// </remarks>
        private void ReportDropsOnce()
        {
            if (_dropReported)
            {
                return;
            }

            long dropped = DroppedSnapshots + DroppedAlarms;

            if (dropped == 0)
            {
                return;
            }

            _dropReported = true;

            Report(ConfigWarning.Advisory(
                "LOG-02",
                string.Format(
                    CultureInfo.InvariantCulture,
                    "적재가 밀려 기록 {0}건을 버렸습니다. 트렌드에 구멍이 생깁니다.",
                    dropped),
                "디스크 성능을 확인하고, 필요하면 control.json 의 logging.queueCapacity 를 늘리십시오."));
        }

        /// <summary>실패를 세고, 한계를 넘으면 기록을 중단한다.</summary>
        /// <param name="what">실패한 작업 이름.</param>
        /// <param name="error">예외.</param>
        private void OnFailure(string what, Exception error)
        {
            _lastError = what + " — " + error.GetType().Name + ": " + error.Message;
            _consecutiveFailures++;

            if (_consecutiveFailures < MaxConsecutiveFailures)
            {
                return;
            }

            _halted = true;

            // ★ Advisory 로 올린다. Blocking 으로 올리면 자동 운전 진입이 막힌다.
            // 기록은 통보 기능이다. 기록이 안 된다고 압력 제어를 세울 이유가 없다.
            Report(ConfigWarning.Advisory(
                "LOG-01",
                string.Format(
                    CultureInfo.InvariantCulture,
                    "데이터 기록을 중단했습니다(연속 {0}회 실패). {1}",
                    MaxConsecutiveFailures, _lastError),
                "로그 폴더의 디스크 여유와 쓰기 권한을 확인한 뒤 프로그램을 재시작하십시오."));
        }

        /// <summary>구성 경고를 보고한다. 보고 경로 예외가 적재를 멈추지 않게 감싼다.</summary>
        /// <param name="warning">경고.</param>
        private void Report(ConfigWarning warning)
        {
            Action<ConfigWarning> report = _report;

            if (report == null)
            {
                return;
            }

            try
            {
                report(warning);
            }
            catch (Exception)
            {
                // 보고하지 못한 것으로 적재를 멈추지는 않는다.
            }
        }

        /// <summary>정리된 뒤 사용되면 던진다.</summary>
        /// <exception cref="ObjectDisposedException">이미 정리되었을 때.</exception>
        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException("DataLogger");
            }
        }
    }
}
