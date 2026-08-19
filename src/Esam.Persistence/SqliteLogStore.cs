using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace Esam.Persistence
{
    /// <summary>
    /// 일별 SQLite 파일에 트렌드·알람·설정변경 이력을 적재한다.
    /// </summary>
    /// <remarks>
    /// <para><b>일별 파일로 나누는 이유.</b> 24시간 연속 운전에서 200ms 주기로 쌓으면
    /// 하루 약 43만 행이다. 단일 파일로 두면 몇 달 뒤 파일 하나가 수십 GB 가 되고,
    /// 그 시점에는 백업도 복구도 조회도 모두 어려워진다. 하루 단위로 끊으면
    /// 오래된 것을 파일째 지울 수 있어 삭제 비용이 0 에 가깝다.</para>
    /// <para><b>날짜는 현지 시각 기준이다.</b> 값은 UTC 로 저장하지만 파일 이름은
    /// 현장 사람이 "8월 19일 로그" 를 찾는 방식과 같아야 한다. 대신 어느 파일이
    /// 어느 UTC 구간을 담는지를 <c>meta</c> 테이블에 적어 둔다. 조회 쪽이 시간대를
    /// 추측하게 두면, 시간대가 다른 곳에서 파일을 열었을 때 조용히 하루가 어긋난다.</para>
    /// <para><b>행은 자기 시각의 파일로 간다.</b> 배치가 자정을 걸치면 파일 두 개로
    /// 나뉜다. 배치 시작 시각으로 몰아넣으면 00:00 직후 데이터가 전날 파일에 들어가
    /// "어제 로그에 오늘 데이터가 있는" 상태가 된다.</para>
    /// <para><b>저장 실패를 삼키지 않는다.</b> 이 클래스는 예외를 그대로 올린다.
    /// 기록이 멈춘 것을 알리는 것은 상위(로거)의 몫이고, 저장소가 조용히 실패하면
    /// 아무도 모르는 채로 데이터가 사라진다.</para>
    /// <para>이 클래스는 잠금으로 보호되어 스레드 안전하다.</para>
    /// </remarks>
    public sealed class SqliteLogStore : IDisposable
    {
        /// <summary>스키마 버전. 열 구성이나 의미가 바뀌면 올린다.</summary>
        /// <remarks>
        /// 과거 파일을 열 때 이 값을 보고 조회 쪽이 해석 방식을 정한다.
        /// 버전을 올리지 않고 열 의미만 바꾸면 옛 파일이 조용히 잘못 읽힌다.
        /// </remarks>
        public const int SchemaVersion = 1;

        /// <summary>DB 파일 이름 접두사.</summary>
        public const string FilePrefix = "esam_";

        /// <summary>DB 파일 확장자.</summary>
        public const string FileExtension = ".db";

        /// <summary>
        /// <c>trend</c> 테이블의 열 순서. <see cref="TrendRow"/> 의 필드 순서와 1:1 이다.
        /// </summary>
        /// <remarks>
        /// 이 배열이 스키마와 DTO 사이의 계약이다. 둘 중 하나만 바꾸면 값이
        /// 옆 열에 들어가는데, 이것은 예외 없이 통과해 버리는 종류의 결함이다.
        /// 그래서 생성자에서 개수를 대조하고 어긋나면 즉시 던진다.
        /// </remarks>
        private static readonly string[] TrendColumns =
        {
            "ts_utc",
            "s11", "s12", "s13",
            "s21", "s22", "s23", "s24", "s25",
            "s31", "s32", "s33", "s34", "s35",
            "v1_pct", "v2_pct", "v3_pct", "v4_pct", "v5_pct",
            "f1_rpm", "f2_rpm", "f3_rpm", "f4_rpm", "f5_rpm",
            "ffu_rpm", "mfc1", "mfc2",
            "av1", "av2", "av3",
            "temp_efem", "humi_efem", "particle", "temp_ctrlbox",
            "ctrl_mode", "ctrl_phase", "alarm_codes"
        };

        /// <summary>일별 파일 이름 패턴. 여기에 맞지 않는 파일은 리텐션이 건드리지 않는다.</summary>
        private static readonly Regex FileNamePattern = new Regex(
            @"^esam_(\d{8})\.db$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>
        /// 스키마 정의. 하루 파일 하나에 시계열과 이력이 함께 들어간다.
        /// </summary>
        /// <remarks>
        /// <para>DESIGN 초안에 있던 <c>recipe</c> 테이블은 두지 않는다. 레시피는
        /// 시계열이 아니라 <b>현재 상태</b>여서 일별 파일에 담으면 매일 사라진다.
        /// 그리고 C5 에서 설정 파일(<c>recipe.json</c>)을 배포 산출물로 정했으므로,
        /// DB 에 사본을 두면 진실이 두 곳이 된다.</para>
        /// <para><c>alarm_bits BLOB</c> 대신 <c>alarm_codes TEXT</c> 를 쓴다.
        /// 이유는 <see cref="TrendRow.ActiveAlarmCodes"/> 주석에 적었다.</para>
        /// <para>식별자를 대괄호로 감싼 것은 <c>key</c>·<c>value</c>·<c>user</c> 가
        /// SQL 예약어와 겹치기 때문이다.</para>
        /// </remarks>
        private const string SchemaDdl = @"
CREATE TABLE IF NOT EXISTS meta (
  [key]   TEXT PRIMARY KEY,
  [value] TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS trend (
  ts_utc INTEGER NOT NULL,
  s11 REAL, s12 REAL, s13 REAL,
  s21 REAL, s22 REAL, s23 REAL, s24 REAL, s25 REAL,
  s31 REAL, s32 REAL, s33 REAL, s34 REAL, s35 REAL,
  v1_pct REAL, v2_pct REAL, v3_pct REAL, v4_pct REAL, v5_pct REAL,
  f1_rpm REAL, f2_rpm REAL, f3_rpm REAL, f4_rpm REAL, f5_rpm REAL,
  ffu_rpm REAL, mfc1 REAL, mfc2 REAL,
  av1 REAL, av2 REAL, av3 REAL,
  temp_efem REAL, humi_efem REAL, particle REAL, temp_ctrlbox REAL,
  ctrl_mode INTEGER, ctrl_phase INTEGER, alarm_codes TEXT
);
CREATE INDEX IF NOT EXISTS ix_trend_ts ON trend(ts_utc);

CREATE TABLE IF NOT EXISTS alarm_history (
  id          INTEGER PRIMARY KEY,
  code        TEXT NOT NULL,
  severity    INTEGER,
  raised_utc  INTEGER NOT NULL,
  cleared_utc INTEGER,
  ack_utc     INTEGER,
  ack_user    TEXT,
  [value]     REAL,
  message     TEXT
);
CREATE INDEX IF NOT EXISTS ix_alarm_raised ON alarm_history(raised_utc);

CREATE TABLE IF NOT EXISTS audit_log (
  id        INTEGER PRIMARY KEY,
  ts_utc    INTEGER NOT NULL,
  [user]    TEXT,
  category  TEXT,
  item      TEXT,
  old_value TEXT,
  new_value TEXT
);
CREATE INDEX IF NOT EXISTS ix_audit_ts ON audit_log(ts_utc);
";

        private readonly string _folder;
        private readonly int _retentionDays;
        private readonly object _gate = new object();

        private SQLiteConnection _connection;
        private SQLiteCommand _trendInsert;
        private SQLiteParameter[] _trendParameters;
        private string _openDayKey;
        private bool _disposed;

        /// <summary>저장소를 만든다. 폴더가 없으면 만든다.</summary>
        /// <param name="folderPath">DB 파일을 둘 폴더.</param>
        /// <param name="retentionDays">보존 일수(오늘 포함). 1 이상.</param>
        /// <exception cref="ArgumentException">폴더 경로가 비어 있을 때.</exception>
        /// <exception cref="ArgumentOutOfRangeException">보존 일수가 1 미만일 때.</exception>
        /// <exception cref="InvalidOperationException">열 구성 계약이 어긋났을 때.</exception>
        public SqliteLogStore(string folderPath, int retentionDays)
        {
            if (string.IsNullOrEmpty(folderPath) || folderPath.Trim().Length == 0)
            {
                throw new ArgumentException("저장 폴더 경로가 비어 있습니다.", "folderPath");
            }

            if (retentionDays < 1)
            {
                throw new ArgumentOutOfRangeException(
                    "retentionDays", retentionDays, "보존 일수는 1 이상이어야 합니다.");
            }

            AssertColumnContract();

            _folder = Path.GetFullPath(folderPath);
            _retentionDays = retentionDays;

            Directory.CreateDirectory(_folder);
        }

        /// <summary>DB 파일을 두는 폴더의 전체 경로.</summary>
        public string Folder
        {
            get { return _folder; }
        }

        /// <summary>보존 일수(오늘 포함).</summary>
        public int RetentionDays
        {
            get { return _retentionDays; }
        }

        /// <summary>현재 열려 있는 DB 파일 경로. 아직 아무것도 쓰지 않았으면 null.</summary>
        public string CurrentFilePath
        {
            get
            {
                lock (_gate)
                {
                    return _openDayKey == null ? null : PathForDay(_openDayKey);
                }
            }
        }

        /// <summary>트렌드 행들을 한 트랜잭션으로 적재한다.</summary>
        /// <param name="rows">적재할 행. 시각 순으로 정렬되어 있으면 파일 전환이 최소가 된다.</param>
        /// <returns>적재한 행 수.</returns>
        /// <exception cref="ArgumentNullException">행 목록이 null 일 때.</exception>
        /// <exception cref="ObjectDisposedException">이미 정리된 저장소일 때.</exception>
        /// <remarks>
        /// 한 행씩 커밋하면 fsync 가 행마다 일어나 폴링 주기를 따라가지 못한다.
        /// 배치를 한 트랜잭션으로 묶는 것이 이 클래스가 존재하는 이유의 절반이다.
        /// </remarks>
        public int WriteTrend(IList<TrendRow> rows)
        {
            if (rows == null)
            {
                throw new ArgumentNullException("rows");
            }

            if (rows.Count == 0)
            {
                return 0;
            }

            lock (_gate)
            {
                ThrowIfDisposed();

                int written = 0;
                int index = 0;

                while (index < rows.Count)
                {
                    string dayKey = DayKeyOfUnixMs(rows[index].TimestampMs);
                    int end = index;

                    // 같은 날짜가 이어지는 구간을 한 트랜잭션으로 묶는다.
                    while (end < rows.Count && DayKeyOfUnixMs(rows[end].TimestampMs) == dayKey)
                    {
                        end++;
                    }

                    written += WriteTrendRange(dayKey, rows, index, end);
                    index = end;
                }

                return written;
            }
        }

        /// <summary>알람 발생 이력을 적재한다.</summary>
        /// <param name="entries">적재할 이력.</param>
        /// <returns>적재한 건수.</returns>
        /// <exception cref="ArgumentNullException">목록이 null 일 때.</exception>
        /// <exception cref="ObjectDisposedException">이미 정리된 저장소일 때.</exception>
        public int WriteAlarms(IList<AlarmLogEntry> entries)
        {
            if (entries == null)
            {
                throw new ArgumentNullException("entries");
            }

            if (entries.Count == 0)
            {
                return 0;
            }

            lock (_gate)
            {
                ThrowIfDisposed();

                int written = 0;

                foreach (AlarmLogEntry entry in entries)
                {
                    EnsureOpen(DayKeyOfUtc(entry.RaisedUtc));

                    using (SQLiteCommand command = _connection.CreateCommand())
                    {
                        command.CommandText =
                            "INSERT INTO alarm_history" +
                            "(code, severity, raised_utc, cleared_utc, ack_utc, ack_user, [value], message) " +
                            "VALUES(@code, @severity, @raised, @cleared, @ack, @ackUser, @value, @message);";

                        command.Parameters.AddWithValue("@code", entry.Code);
                        command.Parameters.AddWithValue("@severity", (int)entry.Severity);
                        command.Parameters.AddWithValue("@raised", TrendRow.ToUnixMs(entry.RaisedUtc));
                        command.Parameters.AddWithValue("@cleared", ToDbTime(entry.ClearedUtc));
                        command.Parameters.AddWithValue("@ack", ToDbTime(entry.AckUtc));
                        command.Parameters.AddWithValue("@ackUser", ToDbText(entry.AckUser));
                        command.Parameters.AddWithValue("@value", ToDbNumber(entry.Value));
                        command.Parameters.AddWithValue("@message", ToDbText(entry.Message));

                        written += command.ExecuteNonQuery();
                    }
                }

                return written;
            }
        }

        /// <summary>설정 변경 이력을 적재한다.</summary>
        /// <param name="entries">적재할 이력.</param>
        /// <returns>적재한 건수.</returns>
        /// <exception cref="ArgumentNullException">목록이 null 일 때.</exception>
        /// <exception cref="ObjectDisposedException">이미 정리된 저장소일 때.</exception>
        public int WriteAudit(IList<AuditLogEntry> entries)
        {
            if (entries == null)
            {
                throw new ArgumentNullException("entries");
            }

            if (entries.Count == 0)
            {
                return 0;
            }

            lock (_gate)
            {
                ThrowIfDisposed();

                int written = 0;

                foreach (AuditLogEntry entry in entries)
                {
                    EnsureOpen(DayKeyOfUtc(entry.TimestampUtc));

                    using (SQLiteCommand command = _connection.CreateCommand())
                    {
                        command.CommandText =
                            "INSERT INTO audit_log(ts_utc, [user], category, item, old_value, new_value) " +
                            "VALUES(@ts, @user, @category, @item, @old, @new);";

                        command.Parameters.AddWithValue("@ts", TrendRow.ToUnixMs(entry.TimestampUtc));
                        command.Parameters.AddWithValue("@user", ToDbText(entry.User));
                        command.Parameters.AddWithValue("@category", ToDbText(entry.Category));
                        command.Parameters.AddWithValue("@item", entry.Item);
                        command.Parameters.AddWithValue("@old", ToDbText(entry.OldValue));
                        command.Parameters.AddWithValue("@new", ToDbText(entry.NewValue));

                        written += command.ExecuteNonQuery();
                    }
                }

                return written;
            }
        }

        /// <summary>보존 기간이 지난 DB 파일을 지운다.</summary>
        /// <param name="nowUtc">현재 시각(UTC).</param>
        /// <returns>지운 파일과 지우지 못한 파일.</returns>
        /// <exception cref="ObjectDisposedException">이미 정리된 저장소일 때.</exception>
        /// <remarks>
        /// <para><b>파일 수정시각이 아니라 파일 이름의 날짜로 판단한다.</b> WAL 은
        /// 체크포인트 때 본체 파일을 다시 건드리므로, 수정시각으로 보면 며칠 전 파일이
        /// 오늘 것처럼 보인다.</para>
        /// <para>이름 규칙에 맞지 않는 파일은 손대지 않는다. 사용자가 같은 폴더에
        /// 둔 백업본을 지우는 것이 리텐션의 일이 아니다.</para>
        /// </remarks>
        public PurgeResult Purge(DateTime nowUtc)
        {
            List<string> deleted = new List<string>();
            List<string> failed = new List<string>();

            lock (_gate)
            {
                ThrowIfDisposed();

                // 보존 일수에 오늘을 포함한다. 90일 보존이면 오늘 포함 90일치가 남는다.
                DateTime cutoff = nowUtc.ToLocalTime().Date.AddDays(-(_retentionDays - 1));

                string[] candidates = Directory.GetFiles(_folder, FilePrefix + "*" + FileExtension);

                foreach (string path in candidates)
                {
                    Match match = FileNamePattern.Match(Path.GetFileName(path));

                    if (!match.Success)
                    {
                        continue;
                    }

                    string dayKey = match.Groups[1].Value;
                    DateTime day;

                    if (!DateTime.TryParseExact(
                            dayKey, "yyyyMMdd", CultureInfo.InvariantCulture,
                            DateTimeStyles.None, out day))
                    {
                        continue;
                    }

                    if (day >= cutoff || dayKey == _openDayKey)
                    {
                        continue;
                    }

                    try
                    {
                        File.Delete(path);
                        DeleteIfExists(path + "-wal");
                        DeleteIfExists(path + "-shm");
                        deleted.Add(path);
                    }
                    catch (IOException error)
                    {
                        failed.Add(path + " — " + error.Message);
                    }
                    catch (UnauthorizedAccessException error)
                    {
                        failed.Add(path + " — " + error.Message);
                    }
                }
            }

            return new PurgeResult(deleted, failed);
        }

        /// <summary>열려 있는 파일을 닫는다.</summary>
        public void Dispose()
        {
            lock (_gate)
            {
                if (_disposed)
                {
                    return;
                }

                CloseCurrent();
                _disposed = true;
            }
        }

        /// <summary>같은 날짜 구간을 한 트랜잭션으로 적재한다.</summary>
        /// <param name="dayKey">대상 날짜 키(yyyyMMdd).</param>
        /// <param name="rows">행 목록.</param>
        /// <param name="start">시작 인덱스(포함).</param>
        /// <param name="end">끝 인덱스(제외).</param>
        /// <returns>적재한 행 수.</returns>
        private int WriteTrendRange(string dayKey, IList<TrendRow> rows, int start, int end)
        {
            EnsureOpen(dayKey);

            using (SQLiteTransaction transaction = _connection.BeginTransaction())
            {
                _trendInsert.Transaction = transaction;

                for (int i = start; i < end; i++)
                {
                    Bind(rows[i]);
                    _trendInsert.ExecuteNonQuery();
                }

                transaction.Commit();
            }

            _trendInsert.Transaction = null;

            return end - start;
        }

        /// <summary>한 행의 값을 준비된 파라미터에 옮긴다.</summary>
        /// <param name="row">트렌드 행.</param>
        private void Bind(TrendRow row)
        {
            int k = 0;

            _trendParameters[k++].Value = row.TimestampMs;

            for (int i = 0; i < row.Pressures.Length; i++)
            {
                _trendParameters[k++].Value = ToDbNumber(row.Pressures[i]);
            }

            for (int i = 0; i < row.ValvePercents.Length; i++)
            {
                _trendParameters[k++].Value = ToDbNumber(row.ValvePercents[i]);
            }

            for (int i = 0; i < row.FanRpms.Length; i++)
            {
                _trendParameters[k++].Value = ToDbNumber(row.FanRpms[i]);
            }

            _trendParameters[k++].Value = ToDbNumber(row.FfuRpm);

            for (int i = 0; i < row.MfcFlows.Length; i++)
            {
                _trendParameters[k++].Value = ToDbNumber(row.MfcFlows[i]);
            }

            for (int i = 0; i < row.AirVelocities.Length; i++)
            {
                _trendParameters[k++].Value = ToDbNumber(row.AirVelocities[i]);
            }

            _trendParameters[k++].Value = ToDbNumber(row.TemperatureEfem);
            _trendParameters[k++].Value = ToDbNumber(row.HumidityEfem);
            _trendParameters[k++].Value = ToDbNumber(row.Particle);
            _trendParameters[k++].Value = ToDbNumber(row.TemperatureControlBox);
            _trendParameters[k++].Value = row.ControlMode;
            _trendParameters[k++].Value = row.ControlPhase;
            _trendParameters[k++].Value = ToDbText(row.ActiveAlarmCodes);

            if (k != _trendParameters.Length)
            {
                throw new InvalidOperationException(string.Format(
                    CultureInfo.InvariantCulture,
                    "트렌드 열 {0}개에 값 {1}개를 넣었습니다. TrendRow 와 스키마가 어긋났습니다.",
                    _trendParameters.Length, k));
            }
        }

        /// <summary>해당 날짜의 DB 파일을 연다. 이미 그 파일이 열려 있으면 아무것도 하지 않는다.</summary>
        /// <param name="dayKey">날짜 키(yyyyMMdd).</param>
        private void EnsureOpen(string dayKey)
        {
            if (_connection != null && _openDayKey == dayKey)
            {
                return;
            }

            CloseCurrent();

            SQLiteConnectionStringBuilder builder = new SQLiteConnectionStringBuilder();
            builder.DataSource = PathForDay(dayKey);
            builder.Version = 3;
            builder.DateTimeKind = DateTimeKind.Utc;

            SQLiteConnection connection = new SQLiteConnection(builder.ToString());

            try
            {
                connection.Open();
                ApplyPragmas(connection);
                CreateSchema(connection, dayKey);

                _trendInsert = CreateTrendInsert(connection, out _trendParameters);
                _connection = connection;
                _openDayKey = dayKey;
            }
            catch
            {
                connection.Dispose();
                throw;
            }
        }

        /// <summary>열려 있는 파일을 체크포인트하고 닫는다.</summary>
        private void CloseCurrent()
        {
            if (_trendInsert != null)
            {
                _trendInsert.Dispose();
                _trendInsert = null;
                _trendParameters = null;
            }

            if (_connection != null)
            {
                try
                {
                    // WAL 을 본체로 합쳐 두고 닫는다. 합치지 않으면 어제 파일 옆에
                    // -wal 이 남아, 파일만 복사해 간 사람이 마지막 몇 분을 잃는다.
                    using (SQLiteCommand command = _connection.CreateCommand())
                    {
                        command.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
                        command.ExecuteNonQuery();
                    }
                }
                catch (SQLiteException)
                {
                    // 체크포인트 실패가 파일을 닫지 못할 이유는 되지 않는다.
                }

                _connection.Dispose();
                _connection = null;
            }

            _openDayKey = null;
        }

        /// <summary>트렌드 삽입 명령과 파라미터를 준비한다.</summary>
        /// <param name="connection">열린 연결.</param>
        /// <param name="parameters">준비된 파라미터 배열.</param>
        /// <returns>재사용할 삽입 명령.</returns>
        /// <remarks>
        /// 행마다 명령을 새로 만들면 SQL 파싱이 43만 번 일어난다.
        /// 파라미터 객체를 잡아 두고 값만 바꿔 넣는다.
        /// </remarks>
        private static SQLiteCommand CreateTrendInsert(
            SQLiteConnection connection, out SQLiteParameter[] parameters)
        {
            string[] names = new string[TrendColumns.Length];

            for (int i = 0; i < TrendColumns.Length; i++)
            {
                names[i] = "@p" + i.ToString(CultureInfo.InvariantCulture);
            }

            SQLiteCommand command = connection.CreateCommand();

            command.CommandText =
                "INSERT INTO trend(" + string.Join(", ", TrendColumns) + ") VALUES(" +
                string.Join(", ", names) + ");";

            parameters = new SQLiteParameter[names.Length];

            for (int i = 0; i < names.Length; i++)
            {
                // SQLiteParameterCollection.Add 는 삽입 위치(int)를 돌려준다.
                // 다른 ADO.NET 구현과 달리 파라미터 객체를 돌려주지 않으므로,
                // 값을 바꿔 넣을 참조는 직접 잡아 둔다.
                SQLiteParameter parameter = new SQLiteParameter(names[i]);

                command.Parameters.Add(parameter);
                parameters[i] = parameter;
            }

            return command;
        }

        /// <summary>WAL·동기화 설정을 적용한다.</summary>
        /// <param name="connection">열린 연결.</param>
        /// <exception cref="InvalidOperationException">WAL 모드가 켜지지 않았을 때.</exception>
        /// <remarks>
        /// WAL 이 아니면 쓰는 동안 조회가 막힌다. 조회 화면이 트렌드를 여는 순간
        /// 적재가 멈추는 구조가 되므로, 켜지지 않으면 진행하지 않고 던진다.
        /// (네트워크 드라이브에 로그 폴더를 잡으면 실제로 실패한다.)
        /// </remarks>
        private static void ApplyPragmas(SQLiteConnection connection)
        {
            using (SQLiteCommand command = connection.CreateCommand())
            {
                command.CommandText = "PRAGMA journal_mode = WAL;";
                object mode = command.ExecuteScalar();

                string text = mode == null
                    ? null
                    : Convert.ToString(mode, CultureInfo.InvariantCulture);

                if (!string.Equals(text, "wal", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        "WAL 모드를 켜지 못했습니다(응답: " + (text ?? "없음") +
                        "). 로그 폴더가 네트워크 드라이브인지 확인하십시오.");
                }

                command.CommandText = "PRAGMA synchronous = NORMAL;";
                command.ExecuteNonQuery();
            }
        }

        /// <summary>스키마를 만들고 메타 정보를 적는다.</summary>
        /// <param name="connection">열린 연결.</param>
        /// <param name="dayKey">날짜 키(yyyyMMdd).</param>
        private static void CreateSchema(SQLiteConnection connection, string dayKey)
        {
            using (SQLiteTransaction transaction = connection.BeginTransaction())
            using (SQLiteCommand command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = SchemaDdl;
                command.ExecuteNonQuery();
                transaction.Commit();
            }

            DateTime localStart = DateTime.SpecifyKind(
                DateTime.ParseExact(dayKey, "yyyyMMdd", CultureInfo.InvariantCulture),
                DateTimeKind.Local);

            WriteMeta(connection, "schema_version", SchemaVersion.ToString(CultureInfo.InvariantCulture));
            WriteMeta(connection, "local_date", dayKey);
            WriteMeta(connection, "utc_from_ms", TrendRow.ToUnixMs(localStart.ToUniversalTime())
                .ToString(CultureInfo.InvariantCulture));
            WriteMeta(connection, "utc_to_ms", TrendRow.ToUnixMs(localStart.AddDays(1).ToUniversalTime())
                .ToString(CultureInfo.InvariantCulture));
        }

        /// <summary>메타 항목을 한 번만 적는다(이미 있으면 유지).</summary>
        /// <param name="connection">열린 연결.</param>
        /// <param name="key">항목 이름.</param>
        /// <param name="value">항목 값.</param>
        private static void WriteMeta(SQLiteConnection connection, string key, string value)
        {
            using (SQLiteCommand command = connection.CreateCommand())
            {
                command.CommandText =
                    "INSERT OR IGNORE INTO meta([key], [value]) VALUES(@key, @value);";
                command.Parameters.AddWithValue("@key", key);
                command.Parameters.AddWithValue("@value", value);
                command.ExecuteNonQuery();
            }
        }

        /// <summary>날짜 키에 해당하는 DB 파일 경로를 만든다.</summary>
        /// <param name="dayKey">날짜 키(yyyyMMdd).</param>
        /// <returns>전체 경로.</returns>
        private string PathForDay(string dayKey)
        {
            return Path.Combine(_folder, FilePrefix + dayKey + FileExtension);
        }

        /// <summary>UTC 시각이 속하는 현지 날짜 키를 구한다.</summary>
        /// <param name="utc">UTC 시각.</param>
        /// <returns>yyyyMMdd 형식의 날짜 키.</returns>
        private static string DayKeyOfUtc(DateTime utc)
        {
            DateTime normalized = utc.Kind == DateTimeKind.Utc
                ? utc
                : DateTime.SpecifyKind(utc, DateTimeKind.Utc);

            return normalized.ToLocalTime().ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        }

        /// <summary>Unix 밀리초가 속하는 현지 날짜 키를 구한다.</summary>
        /// <param name="unixMs">Unix 밀리초.</param>
        /// <returns>yyyyMMdd 형식의 날짜 키.</returns>
        private static string DayKeyOfUnixMs(long unixMs)
        {
            return DayKeyOfUtc(TrendRow.FromUnixMs(unixMs));
        }

        /// <summary>값이 없으면 NULL 로 바꾼다.</summary>
        /// <param name="value">값.</param>
        /// <returns>DB 파라미터 값.</returns>
        private static object ToDbNumber(double? value)
        {
            return value.HasValue ? (object)value.Value : DBNull.Value;
        }

        /// <summary>값이 없으면 NULL 로 바꾼다.</summary>
        /// <param name="value">시각.</param>
        /// <returns>DB 파라미터 값.</returns>
        private static object ToDbTime(DateTime? value)
        {
            return value.HasValue ? (object)TrendRow.ToUnixMs(value.Value) : DBNull.Value;
        }

        /// <summary>빈 문자열을 NULL 로 바꾼다.</summary>
        /// <param name="value">문자열.</param>
        /// <returns>DB 파라미터 값.</returns>
        private static object ToDbText(string value)
        {
            return string.IsNullOrEmpty(value) ? (object)DBNull.Value : value;
        }

        /// <summary>파일이 있으면 지운다.</summary>
        /// <param name="path">파일 경로.</param>
        private static void DeleteIfExists(string path)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        /// <summary>DTO 와 스키마의 열 개수가 맞는지 확인한다.</summary>
        /// <exception cref="InvalidOperationException">개수가 어긋났을 때.</exception>
        private static void AssertColumnContract()
        {
            int expected =
                1 +
                TrendRow.SensorIds.Length +
                TrendRow.ValveIds.Length +
                TrendRow.FanIds.Length +
                1 + 2 + 3 +
                4 +
                3;

            if (TrendColumns.Length != expected)
            {
                throw new InvalidOperationException(string.Format(
                    CultureInfo.InvariantCulture,
                    "trend 열 정의가 {0}개인데 TrendRow 는 {1}개를 채웁니다.",
                    TrendColumns.Length, expected));
            }
        }

        /// <summary>정리된 뒤 사용되면 던진다.</summary>
        /// <exception cref="ObjectDisposedException">이미 정리되었을 때.</exception>
        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException("SqliteLogStore");
            }
        }
    }
}
