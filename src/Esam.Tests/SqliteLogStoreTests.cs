using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Globalization;
using System.IO;
using Esam.Domain.Alarms;
using Esam.Persistence;
using Xunit;

namespace Esam.Tests
{
    /// <summary>
    /// SQLite 적재 저장소 검증.
    /// </summary>
    /// <remarks>
    /// 실제 파일을 만들어 검증한다. 메모리 DB 로 대체하면 이 계층에서 가장 잘 틀리는 것
    /// — 일별 파일 전환, WAL 파일, 리텐션 삭제 — 이 전부 검증 대상 밖으로 빠진다.
    /// </remarks>
    public sealed class SqliteLogStoreTests
    {
        /// <summary>테스트마다 별도 임시 폴더를 쓴다. xUnit 이 병렬로 돌려도 섞이지 않는다.</summary>
        private sealed class TempFolder : IDisposable
        {
            public string Root { get; private set; }

            public TempFolder()
            {
                Root = Path.Combine(
                    Path.GetTempPath(), "esam-log-" + Guid.NewGuid().ToString("N"));

                Directory.CreateDirectory(Root);
            }

            public void Dispose()
            {
                try
                {
                    Directory.Delete(Root, true);
                }
                catch (IOException)
                {
                    // 임시 폴더 정리 실패가 테스트 결과를 뒤집을 이유는 없다.
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }

        /// <summary>지정한 현지 시각의 트렌드 행 하나를 만든다.</summary>
        private static TrendRow RowAtLocal(int year, int month, int day, int hour, int minute, int second, int ms)
        {
            DateTime local = new DateTime(year, month, day, hour, minute, second, ms, DateTimeKind.Local);

            TrendRow row = new TrendRow();
            row.TimestampMs = TrendRow.ToUnixMs(local.ToUniversalTime());

            return row;
        }

        /// <summary>DB 파일에 스칼라 질의를 던진다.</summary>
        private static object Scalar(string dbPath, string sql)
        {
            SQLiteConnectionStringBuilder builder = new SQLiteConnectionStringBuilder();
            builder.DataSource = dbPath;
            builder.Version = 3;

            using (SQLiteConnection connection = new SQLiteConnection(builder.ToString()))
            {
                connection.Open();

                using (SQLiteCommand command = connection.CreateCommand())
                {
                    command.CommandText = sql;
                    return command.ExecuteScalar();
                }
            }
        }

        [Fact]
        public void 적재한_값이_그대로_읽힌다()
        {
            using (TempFolder folder = new TempFolder())
            using (SqliteLogStore store = new SqliteLogStore(folder.Root, 90))
            {
                TrendRow row = RowAtLocal(2026, 8, 19, 10, 30, 0, 0);
                row.Pressures[0] = -12.5;      // S1-1
                row.Pressures[12] = 3.25;      // S3-5
                row.ValvePercents[4] = 47.5;   // V-5
                row.FanRpms[0] = 1234.0;       // F-1
                row.FfuRpm = 900.0;
                row.MfcFlows[1] = 2.5;
                row.AirVelocities[2] = 0.42;
                row.TemperatureEfem = 23.4;
                row.HumidityEfem = 45.0;
                row.TemperatureControlBox = 31.2;
                row.ControlMode = 1;
                row.ControlPhase = 4;
                row.ActiveAlarmCodes = "AL-02,AL-13";

                int written = store.WriteTrend(new List<TrendRow> { row });
                Assert.Equal(1, written);

                string path = store.CurrentFilePath;
                Assert.NotNull(path);
                store.Dispose();

                Assert.Equal(1L, Convert.ToInt64(Scalar(path, "SELECT COUNT(*) FROM trend;"), CultureInfo.InvariantCulture));
                Assert.Equal(-12.5, Convert.ToDouble(Scalar(path, "SELECT s11 FROM trend;"), CultureInfo.InvariantCulture));
                Assert.Equal(3.25, Convert.ToDouble(Scalar(path, "SELECT s35 FROM trend;"), CultureInfo.InvariantCulture));
                Assert.Equal(47.5, Convert.ToDouble(Scalar(path, "SELECT v5_pct FROM trend;"), CultureInfo.InvariantCulture));
                Assert.Equal(1234.0, Convert.ToDouble(Scalar(path, "SELECT f1_rpm FROM trend;"), CultureInfo.InvariantCulture));
                Assert.Equal(2.5, Convert.ToDouble(Scalar(path, "SELECT mfc2 FROM trend;"), CultureInfo.InvariantCulture));
                Assert.Equal(0.42, Convert.ToDouble(Scalar(path, "SELECT av3 FROM trend;"), CultureInfo.InvariantCulture));
                Assert.Equal(4L, Convert.ToInt64(Scalar(path, "SELECT ctrl_phase FROM trend;"), CultureInfo.InvariantCulture));
                Assert.Equal("AL-02,AL-13", Scalar(path, "SELECT alarm_codes FROM trend;"));
            }
        }

        [Fact]
        public void 값이_없는_항목은_NULL_로_남는다()
        {
            // 0 으로 채우면 "측정하지 않은 것" 과 "0 Pa" 를 구분할 수 없다.
            using (TempFolder folder = new TempFolder())
            using (SqliteLogStore store = new SqliteLogStore(folder.Root, 90))
            {
                TrendRow row = RowAtLocal(2026, 8, 19, 10, 30, 0, 0);
                row.Pressures[0] = 0.0;

                store.WriteTrend(new List<TrendRow> { row });

                string path = store.CurrentFilePath;
                store.Dispose();

                Assert.Equal(0.0, Convert.ToDouble(Scalar(path, "SELECT s11 FROM trend;"), CultureInfo.InvariantCulture));
                Assert.Equal(1L, Convert.ToInt64(Scalar(path, "SELECT COUNT(*) FROM trend WHERE s12 IS NULL;"), CultureInfo.InvariantCulture));
                Assert.Equal(1L, Convert.ToInt64(Scalar(path, "SELECT COUNT(*) FROM trend WHERE alarm_codes IS NULL;"), CultureInfo.InvariantCulture));
            }
        }

        [Fact]
        public void 자정을_걸친_배치는_날짜별_파일로_나뉜다()
        {
            // 배치 시작 시각으로 몰아넣으면 00:00 직후 데이터가 전날 파일에 들어간다.
            using (TempFolder folder = new TempFolder())
            using (SqliteLogStore store = new SqliteLogStore(folder.Root, 90))
            {
                List<TrendRow> rows = new List<TrendRow>
                {
                    RowAtLocal(2026, 8, 19, 23, 59, 59, 500),
                    RowAtLocal(2026, 8, 19, 23, 59, 59, 800),
                    RowAtLocal(2026, 8, 20, 0, 0, 0, 100)
                };

                Assert.Equal(3, store.WriteTrend(rows));
                store.Dispose();

                string first = Path.Combine(folder.Root, "esam_20260819.db");
                string second = Path.Combine(folder.Root, "esam_20260820.db");

                Assert.True(File.Exists(first), "19일 파일이 없습니다.");
                Assert.True(File.Exists(second), "20일 파일이 없습니다.");
                Assert.Equal(2L, Convert.ToInt64(Scalar(first, "SELECT COUNT(*) FROM trend;"), CultureInfo.InvariantCulture));
                Assert.Equal(1L, Convert.ToInt64(Scalar(second, "SELECT COUNT(*) FROM trend;"), CultureInfo.InvariantCulture));
            }
        }

        [Fact]
        public void 메타에_스키마_버전과_UTC_구간이_남는다()
        {
            // 시간대가 다른 곳에서 파일을 열었을 때 하루가 어긋나지 않도록 구간을 적어 둔다.
            using (TempFolder folder = new TempFolder())
            using (SqliteLogStore store = new SqliteLogStore(folder.Root, 90))
            {
                store.WriteTrend(new List<TrendRow> { RowAtLocal(2026, 8, 19, 12, 0, 0, 0) });

                string path = store.CurrentFilePath;
                store.Dispose();

                Assert.Equal(
                    SqliteLogStore.SchemaVersion.ToString(CultureInfo.InvariantCulture),
                    Scalar(path, "SELECT [value] FROM meta WHERE [key] = 'schema_version';"));

                Assert.Equal("20260819", Scalar(path, "SELECT [value] FROM meta WHERE [key] = 'local_date';"));

                long from = Convert.ToInt64(
                    Scalar(path, "SELECT [value] FROM meta WHERE [key] = 'utc_from_ms';"), CultureInfo.InvariantCulture);
                long to = Convert.ToInt64(
                    Scalar(path, "SELECT [value] FROM meta WHERE [key] = 'utc_to_ms';"), CultureInfo.InvariantCulture);

                Assert.Equal(
                    TrendRow.ToUnixMs(new DateTime(2026, 8, 19, 0, 0, 0, DateTimeKind.Local).ToUniversalTime()),
                    from);

                Assert.True(to - from >= 23L * 3600000L, "하루 구간이 아닙니다: " + (to - from).ToString(CultureInfo.InvariantCulture));
            }
        }

        [Fact]
        public void WAL_모드로_열린다()
        {
            // WAL 이 아니면 조회 화면을 여는 순간 적재가 막힌다.
            using (TempFolder folder = new TempFolder())
            using (SqliteLogStore store = new SqliteLogStore(folder.Root, 90))
            {
                store.WriteTrend(new List<TrendRow> { RowAtLocal(2026, 8, 19, 12, 0, 0, 0) });

                string path = store.CurrentFilePath;
                store.Dispose();

                string mode = Convert.ToString(Scalar(path, "PRAGMA journal_mode;"), CultureInfo.InvariantCulture);
                Assert.Equal("wal", mode.ToLowerInvariant());
            }
        }

        [Fact]
        public void 알람과_설정변경_이력이_적재된다()
        {
            using (TempFolder folder = new TempFolder())
            using (SqliteLogStore store = new SqliteLogStore(folder.Root, 90))
            {
                DateTime raisedUtc = new DateTime(2026, 8, 19, 12, 0, 0, DateTimeKind.Local).ToUniversalTime();

                store.WriteAlarms(new List<AlarmLogEntry>
                {
                    new AlarmLogEntry("AL-02", AlarmSeverity.Alarm, raisedUtc, -12.5, "차압 하한 미달")
                });

                store.WriteAudit(new List<AuditLogEntry>
                {
                    new AuditLogEntry(raisedUtc, null, "recipe", "S1-1.setpointPa", "-5.0", "-6.5")
                });

                string path = store.CurrentFilePath;
                store.Dispose();

                Assert.Equal("AL-02", Scalar(path, "SELECT code FROM alarm_history;"));
                Assert.Equal((long)AlarmSeverity.Alarm, Convert.ToInt64(Scalar(path, "SELECT severity FROM alarm_history;"), CultureInfo.InvariantCulture));
                Assert.Equal(1L, Convert.ToInt64(Scalar(path, "SELECT COUNT(*) FROM alarm_history WHERE cleared_utc IS NULL;"), CultureInfo.InvariantCulture));
                Assert.Equal("S1-1.setpointPa", Scalar(path, "SELECT item FROM audit_log;"));
                Assert.Equal("-6.5", Scalar(path, "SELECT new_value FROM audit_log;"));
            }
        }

        [Fact]
        public void 보존_기간이_지난_파일만_지운다()
        {
            using (TempFolder folder = new TempFolder())
            using (SqliteLogStore store = new SqliteLogStore(folder.Root, 7))
            {
                DateTime todayLocal = new DateTime(2026, 8, 19, 12, 0, 0, DateTimeKind.Local);

                for (int back = 0; back < 12; back++)
                {
                    string name = todayLocal.AddDays(-back).ToString("yyyyMMdd", CultureInfo.InvariantCulture);
                    File.WriteAllText(Path.Combine(folder.Root, "esam_" + name + ".db"), string.Empty);
                }

                PurgeResult result = store.Purge(todayLocal.ToUniversalTime());

                Assert.Empty(result.Failed);
                Assert.Equal(5, result.Deleted.Count);          // 12일치 중 7일치만 남는다
                Assert.Equal(7, Directory.GetFiles(folder.Root, "esam_*.db").Length);

                // 오늘 포함 7일치이므로 가장 오래 남는 것은 6일 전이다.
                string oldestKept = "esam_" + todayLocal.AddDays(-6).ToString("yyyyMMdd", CultureInfo.InvariantCulture) + ".db";
                Assert.True(File.Exists(Path.Combine(folder.Root, oldestKept)), oldestKept + " 이 지워졌습니다.");
            }
        }

        [Fact]
        public void 이름_규칙에_맞지_않는_파일은_건드리지_않는다()
        {
            // 같은 폴더에 둔 백업본을 지우는 것은 리텐션의 일이 아니다.
            using (TempFolder folder = new TempFolder())
            using (SqliteLogStore store = new SqliteLogStore(folder.Root, 1))
            {
                File.WriteAllText(Path.Combine(folder.Root, "esam_20200101.db"), string.Empty);
                File.WriteAllText(Path.Combine(folder.Root, "esam_backup.db"), string.Empty);
                File.WriteAllText(Path.Combine(folder.Root, "esam_2020.db"), string.Empty);

                PurgeResult result = store.Purge(new DateTime(2026, 8, 19, 12, 0, 0, DateTimeKind.Local).ToUniversalTime());

                Assert.Single(result.Deleted);
                Assert.True(File.Exists(Path.Combine(folder.Root, "esam_backup.db")), "백업본이 지워졌습니다.");
                Assert.True(File.Exists(Path.Combine(folder.Root, "esam_2020.db")), "규칙 밖 파일이 지워졌습니다.");
            }
        }

        [Fact]
        public void 지금_쓰고_있는_파일은_지우지_않는다()
        {
            using (TempFolder folder = new TempFolder())
            using (SqliteLogStore store = new SqliteLogStore(folder.Root, 1))
            {
                store.WriteTrend(new List<TrendRow> { RowAtLocal(2026, 8, 19, 12, 0, 0, 0) });

                // 한참 뒤 시각으로 정리해도, 열려 있는 파일은 대상이 아니다.
                PurgeResult result = store.Purge(new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Local).ToUniversalTime());

                Assert.Empty(result.Deleted);
                Assert.True(File.Exists(Path.Combine(folder.Root, "esam_20260819.db")), "쓰는 중인 파일이 지워졌습니다.");
            }
        }

        [Fact]
        public void 빈_목록은_파일을_만들지_않는다()
        {
            using (TempFolder folder = new TempFolder())
            using (SqliteLogStore store = new SqliteLogStore(folder.Root, 90))
            {
                Assert.Equal(0, store.WriteTrend(new List<TrendRow>()));
                Assert.Null(store.CurrentFilePath);
                Assert.Empty(Directory.GetFiles(folder.Root));
            }
        }

        [Fact]
        public void 정리된_저장소를_다시_쓰면_예외()
        {
            using (TempFolder folder = new TempFolder())
            {
                SqliteLogStore store = new SqliteLogStore(folder.Root, 90);
                store.Dispose();

                Assert.Throws<ObjectDisposedException>(
                    () => store.WriteTrend(new List<TrendRow> { RowAtLocal(2026, 8, 19, 12, 0, 0, 0) }));
            }
        }

        [Fact]
        public void 잘못된_생성_인자는_거부한다()
        {
            using (TempFolder folder = new TempFolder())
            {
                Assert.Throws<ArgumentException>(() => new SqliteLogStore(" ", 90));
                Assert.Throws<ArgumentOutOfRangeException>(() => new SqliteLogStore(folder.Root, 0));
            }
        }

        [Fact]
        public void 큰_배치도_한_트랜잭션으로_들어간다()
        {
            // 500건은 DESIGN 6.2 의 배치 상한(100건)보다 크게 잡아 트랜잭션 경로를 확인한다.
            using (TempFolder folder = new TempFolder())
            using (SqliteLogStore store = new SqliteLogStore(folder.Root, 90))
            {
                List<TrendRow> rows = new List<TrendRow>();
                DateTime start = new DateTime(2026, 8, 19, 9, 0, 0, DateTimeKind.Local).ToUniversalTime();

                for (int i = 0; i < 500; i++)
                {
                    TrendRow row = new TrendRow();
                    row.TimestampMs = TrendRow.ToUnixMs(start.AddMilliseconds(218.0 * i));
                    row.Pressures[0] = i * 0.1;
                    rows.Add(row);
                }

                Assert.Equal(500, store.WriteTrend(rows));

                string path = store.CurrentFilePath;
                store.Dispose();

                Assert.Equal(500L, Convert.ToInt64(Scalar(path, "SELECT COUNT(*) FROM trend;"), CultureInfo.InvariantCulture));
                Assert.Equal(500L, Convert.ToInt64(Scalar(path, "SELECT COUNT(DISTINCT ts_utc) FROM trend;"), CultureInfo.InvariantCulture));
            }
        }
    }
}
