using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using Esam.Domain.Configuration;
using Esam.Domain.Models;
using Esam.Services;
using Xunit;

namespace Esam.Tests
{
    /// <summary>
    /// 데이터 기록기 검증.
    /// </summary>
    /// <remarks>
    /// 여기서 지켜야 할 것은 두 가지다. <b>기록이 제어를 멈추지 않는다</b>(큐에 넣는
    /// 경로가 절대 막히지 않는다), 그리고 <b>조용히 잃지 않는다</b>(버린 것을 센다).
    /// </remarks>
    public sealed class DataLoggerTests
    {
        /// <summary>테스트마다 별도 임시 폴더를 쓴다.</summary>
        private sealed class TempFolder : IDisposable
        {
            public string Root { get; private set; }

            public TempFolder()
            {
                Root = Path.Combine(
                    Path.GetTempPath(), "esam-logger-" + Guid.NewGuid().ToString("N"));

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
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }

        /// <summary>시험용 기록 설정을 만든다.</summary>
        private static LoggingConfig Config(string folder, int batchRows, int queueCapacity)
        {
            LoggingConfig config = new LoggingConfig();
            config.Folder = folder;
            config.BatchMs = 50;
            config.BatchRows = batchRows;
            config.QueueCapacity = queueCapacity;
            config.RetentionDays = 2;

            return config;
        }

        /// <summary>연속된 시각의 스냅샷을 만든다.</summary>
        private static SystemSnapshot SnapshotAt(int index)
        {
            return new SystemSnapshot(
                Build.T0.AddMilliseconds(218.0 * index),
                null, null, null, null, null, null, null);
        }

        /// <summary>조건이 성립할 때까지 기다린다.</summary>
        /// <remarks>
        /// <c>[Fact(Timeout=…)]</c> 은 비동기 테스트 전용이라 여기서는 쓸 수 없다.
        /// 벽시계 예산을 헬퍼 안에 둔다.
        /// </remarks>
        private static void WaitFor(Func<bool> condition, int timeoutMs, string message)
        {
            Stopwatch elapsed = Stopwatch.StartNew();

            while (elapsed.ElapsedMilliseconds < timeoutMs)
            {
                if (condition())
                {
                    return;
                }

                Thread.Sleep(10);
            }

            Assert.Fail(message);
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
        public void 큐에_넣은_스냅샷이_파일에_적재된다()
        {
            using (TempFolder folder = new TempFolder())
            {
                DataLogger logger = new DataLogger(Config(folder.Root, 5, 50), null, null);
                string path;

                try
                {
                    for (int i = 0; i < 7; i++)
                    {
                        Assert.True(logger.Enqueue(SnapshotAt(i)), "큐에 들어가지 않았습니다: " + i);
                    }

                    logger.Start();

                    WaitFor(() => logger.WrittenRows >= 7, 5000,
                        "5초 안에 7행이 적재되지 않았습니다. 적재된 행 " + logger.WrittenRows);

                    logger.Stop(3000);
                    path = logger.CurrentFilePath;
                    Assert.NotNull(path);
                }
                finally
                {
                    logger.Dispose();
                }

                Assert.Equal(7L, Convert.ToInt64(
                    Scalar(path, "SELECT COUNT(*) FROM trend;"), CultureInfo.InvariantCulture));
            }
        }

        [Fact]
        public void 큐가_넘치면_막히지_않고_버리며_센다()
        {
            // ★ 이것이 이 클래스의 존재 이유다.
            // 큐에 넣는 경로가 대기하면 폴링 워커 스레드가 멈추고, 그 순간
            // 기록이 제어를 멈추게 한다.
            using (TempFolder folder = new TempFolder())
            using (DataLogger logger = new DataLogger(Config(folder.Root, 1, 2), null, null))
            {
                Stopwatch elapsed = Stopwatch.StartNew();

                // 적재 스레드를 시작하지 않으므로 큐는 비워지지 않는다.
                Assert.True(logger.Enqueue(SnapshotAt(0)));
                Assert.True(logger.Enqueue(SnapshotAt(1)));
                Assert.False(logger.Enqueue(SnapshotAt(2)));
                Assert.False(logger.Enqueue(SnapshotAt(3)));
                Assert.False(logger.Enqueue(SnapshotAt(4)));

                Assert.Equal(3L, logger.DroppedSnapshots);
                Assert.True(elapsed.ElapsedMilliseconds < 500,
                    "큐가 찼을 때 대기했습니다: " + elapsed.ElapsedMilliseconds + " ms");
            }
        }

        [Fact]
        public void 정지하면_남은_큐를_비운다()
        {
            // 종료 직전 구간은 사고 분석에서 가장 먼저 보는 곳이다. 잃어서는 안 된다.
            using (TempFolder folder = new TempFolder())
            {
                DataLogger logger = new DataLogger(Config(folder.Root, 100, 500), null, null);
                string path;

                try
                {
                    for (int i = 0; i < 30; i++)
                    {
                        logger.Enqueue(SnapshotAt(i));
                    }

                    logger.Start();
                    logger.Stop(5000);

                    path = logger.CurrentFilePath;
                    Assert.Equal(30L, logger.WrittenRows);
                }
                finally
                {
                    logger.Dispose();
                }

                Assert.Equal(30L, Convert.ToInt64(
                    Scalar(path, "SELECT COUNT(*) FROM trend;"), CultureInfo.InvariantCulture));
            }
        }

        [Fact]
        public void 정지한_뒤에는_받지_않는다()
        {
            using (TempFolder folder = new TempFolder())
            using (DataLogger logger = new DataLogger(Config(folder.Root, 5, 50), null, null))
            {
                logger.Start();
                logger.Stop(3000);

                Assert.False(logger.Enqueue(SnapshotAt(0)));
            }
        }

        [Fact]
        public void 기록_폴더가_파일이면_생성_단계에서_실패한다()
        {
            // 조립 루트가 이 예외를 잡아 Advisory 경고로 바꾼다.
            // 여기서 던지지 않고 조용히 넘어가면 기록이 안 되는 것을 아무도 모른다.
            using (TempFolder folder = new TempFolder())
            {
                string filePath = Path.Combine(folder.Root, "log");
                File.WriteAllText(filePath, string.Empty);

                Assert.ThrowsAny<Exception>(
                    () => new DataLogger(Config(filePath, 5, 50), null, null));
            }
        }

        [Fact]
        public void 설정이_없으면_거부한다()
        {
            Assert.Throws<ArgumentNullException>(() => new DataLogger(null, null, null));
        }

        [Fact]
        public void 기록은_기본_조립에서_꺼져_있다()
        {
            // ★ 이 기본값이 바뀌면 런타임을 조립하는 테스트 전부가 작업 폴더에
            // DB 파일을 만든다. 기록을 켜는 결정은 실행 경로(HmiHost)만 한다.
            Assert.False(new RuntimeOptions().EnableDataLogging);
        }

        [Fact]
        public void 배포_기본값은_기록을_켠다()
        {
            LoggingConfig config = new LoggingConfig();

            Assert.True(config.Enabled);
            Assert.Equal("log", config.Folder);
            Assert.Equal(500, config.BatchMs);
            Assert.Equal(100, config.BatchRows);
            Assert.Equal(90, config.RetentionDays);
            Assert.Equal(2000, config.QueueCapacity);
        }

        [Fact]
        public void 큐가_배치보다_작으면_검증에서_걸린다()
        {
            // 한 배치를 모으는 동안에도 넘치므로 영구히 버리게 된다.
            LoggingConfig config = new LoggingConfig();
            config.BatchRows = 100;
            config.QueueCapacity = 50;

            List<string> errors = new List<string>();
            config.Validate(errors);

            Assert.Single(errors);
            Assert.Contains("queueCapacity", errors[0]);
        }

        [Fact]
        public void 잘못된_기록_설정은_모두_모아_알린다()
        {
            // 하나씩 알려주면 파일을 고치는 사람이 그만큼 왕복해야 한다.
            LoggingConfig config = new LoggingConfig();
            config.Folder = "  ";
            config.BatchMs = 10;
            config.BatchRows = 0;
            config.RetentionDays = 0;

            List<string> errors = new List<string>();
            config.Validate(errors);

            Assert.Equal(4, errors.Count);
        }

        [Fact]
        public void 제어_설정_검증이_기록_설정까지_본다()
        {
            // 검증 규칙은 한 곳에만 둔다. 로더와 설정 화면이 같은 경로를 거치게 한다.
            ControlConfig config = Build.Config();
            config.Logging.RetentionDays = 0;

            IList<string> errors;

            Assert.False(config.Validate(out errors));
            Assert.Contains(errors, e => e.Contains("retentionDays"));
        }

        [Fact]
        public void 기록_설정이_없으면_검증에서_걸린다()
        {
            ControlConfig config = Build.Config();
            config.Logging = null;

            IList<string> errors;

            Assert.False(config.Validate(out errors));
            Assert.Contains(errors, e => e.Contains("Logging"));
        }
    }
}
