using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using Esam.Domain.Alarms;
using Esam.Hmi.Infrastructure;
using Esam.Hmi.ViewModels;
using Esam.Services;
using Xunit;

namespace Esam.Tests
{
    /// <summary>
    /// 알람 설정 화면의 판단 검증.
    /// </summary>
    /// <remarks>
    /// <para>이 화면은 <b>설비의 통보 기능을 끌 수 있는 자리</b>다. 임계값을 잘못 넣으면
    /// 알람이 과민해지거나 영원히 울리지 않고, 둘 다 화면에는 정상으로 보인다.</para>
    /// <para>파일을 실제로 쓰는 경로가 있으므로 배포 설정을 임시 폴더에 복사해 쓴다.
    /// 배포본을 건드리면 다음 테스트가 그 결과 위에서 돈다.</para>
    /// </remarks>
    public sealed class AlarmEditorViewModelTests
    {
        private const string DeployedConfigFolder = "config";

        // ─────────────────────────────────────────────────────────────────────
        // 읽기·필터
        // ─────────────────────────────────────────────────────────────────────

        [Fact]
        public void 배포_알람_74건을_읽는다()
        {
            using (TempConfig config = new TempConfig())
            using (HmiHost host = CreateHost(config))
            {
                AlarmEditorViewModel vm = new AlarmEditorViewModel(host);

                Assert.Equal(74, vm.All.Count);
                Assert.Equal(74, vm.Rows.Count);
                Assert.False(vm.HasError);
            }
        }

        [Fact]
        public void 검색어가_코드와_이름을_모두_훑는다()
        {
            using (TempConfig config = new TempConfig())
            using (HmiHost host = CreateHost(config))
            {
                AlarmEditorViewModel vm = new AlarmEditorViewModel(host);

                vm.SearchText = "AL-02";

                Assert.Single(vm.Rows);
                Assert.Equal("AL-02", vm.Rows[0].Code);

                vm.SearchText = "Throttle";

                Assert.True(vm.Rows.Count > 1);
            }
        }

        [Fact]
        public void 비활성만_보기가_동작한다()
        {
            using (TempConfig config = new TempConfig())
            using (HmiHost host = CreateHost(config))
            {
                AlarmEditorViewModel vm = new AlarmEditorViewModel(host);

                vm.DisabledOnly = true;

                Assert.NotEmpty(vm.Rows);

                foreach (AlarmRuleRowViewModel row in vm.Rows)
                {
                    Assert.False(row.Enabled);
                }
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // 임계값이 두 곳에 생기지 않는가
        // ─────────────────────────────────────────────────────────────────────

        [Fact]
        public void 압력_규칙의_임계값_칸은_잠긴다()
        {
            // 임계값은 recipe.json 이 관리한다. 여기서 고칠 수 있게 하면
            // 같은 숫자가 두 곳에 살고, 화면만 보고는 어느 쪽이 적용되는지 알 수 없다.
            using (TempConfig config = new TempConfig())
            using (HmiHost host = CreateHost(config))
            {
                AlarmEditorViewModel vm = new AlarmEditorViewModel(host);

                int locked = 0;

                foreach (AlarmRuleRowViewModel row in vm.All)
                {
                    if (row.Condition == AlarmConditionType.AboveHighLimit.ToString()
                        || row.Condition == AlarmConditionType.BelowLowLimit.ToString())
                    {
                        Assert.False(row.IsThresholdEditable);
                        Assert.Equal("recipe.json", row.ThresholdNotice);
                        locked++;
                    }
                }

                Assert.Equal(26, locked);
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // 저장
        // ─────────────────────────────────────────────────────────────────────

        [Fact]
        public void 쓰기가_잠겨_있으면_저장할_수_없다()
        {
            // 기본값은 거부여야 한다. 허용으로 두면 관문이 한 번도 동작하지 않은 채 출하된다.
            using (TempConfig config = new TempConfig())
            using (HmiHost host = CreateHost(config))
            {
                AlarmEditorViewModel vm = new AlarmEditorViewModel(host);

                Assert.True(vm.IsLocked);
                Assert.False(vm.SaveCommand.CanExecute(null));
            }
        }

        [Fact]
        public void 저장하면_주석이_그대로_남는다()
        {
            using (TempConfig config = new TempConfig())
            using (HmiHost host = CreateHost(config))
            {
                string before = File.ReadAllText(config.PathOf("alarms.json"));

                AlarmEditorViewModel vm = Unlocked(host);
                Row(vm, "AL-02").DebounceMs = "750";

                vm.SaveCommand.Execute(null);

                string after = File.ReadAllText(config.PathOf("alarms.json"));

                Assert.False(vm.HasError, Join(vm.Errors));
                Assert.Equal(CountComments(before), CountComments(after));
                Assert.Contains("비활성 규칙은 소스가 없거나", after);
            }
        }

        [Fact]
        public void 저장한_값이_런타임에_즉시_반영된다()
        {
            using (TempConfig config = new TempConfig())
            using (HmiHost host = CreateHost(config))
            {
                AlarmEditorViewModel vm = Unlocked(host);
                Row(vm, "AL-02").DebounceMs = "750";

                vm.SaveCommand.Execute(null);

                Assert.False(vm.HasError, Join(vm.Errors));

                AlarmState state = host.Runtime.Alarms.FindState("AL-02");

                Assert.NotNull(state);
                Assert.Equal(750.0, state.Rule.DebounceMs, 3);
            }
        }

        [Fact]
        public void 숫자가_아닌_입력은_어느_알람인지_밝히고_거부한다()
        {
            using (TempConfig config = new TempConfig())
            using (HmiHost host = CreateHost(config))
            {
                string before = File.ReadAllText(config.PathOf("alarms.json"));

                AlarmEditorViewModel vm = Unlocked(host);
                Row(vm, "AL-02").DebounceMs = "빠르게";

                vm.SaveCommand.Execute(null);

                Assert.True(vm.HasError);
                Assert.Contains(vm.Errors, e => e.Contains("AL-02"));
                Assert.Equal(before, File.ReadAllText(config.PathOf("alarms.json")));
            }
        }

        [Fact]
        public void 음수_확정_시간은_거부한다()
        {
            using (TempConfig config = new TempConfig())
            using (HmiHost host = CreateHost(config))
            {
                AlarmEditorViewModel vm = Unlocked(host);
                Row(vm, "AL-02").DebounceMs = "-100";

                vm.SaveCommand.Execute(null);

                Assert.True(vm.HasError);
            }
        }

        [Fact]
        public void 현재_문화권이_바뀌어도_소수점_해석이_같다()
        {
            // 쉼표를 소수 구분자로 쓰는 지역 설정에서 6.5 가 65 로 읽히면
            // 임계값이 10배가 된 채 아무 오류 없이 운전에 들어간다.
            using (TempConfig config = new TempConfig())
            using (HmiHost host = CreateHost(config))
            {
                CultureInfo saved = Thread.CurrentThread.CurrentCulture;

                try
                {
                    Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");

                    AlarmEditorViewModel vm = Unlocked(host);
                    AlarmRuleRowViewModel row = FindEditableThreshold(vm);

                    row.Threshold = "6.5";
                    vm.SaveCommand.Execute(null);

                    Assert.False(vm.HasError, Join(vm.Errors));

                    AlarmState state = host.Runtime.Alarms.FindState(row.Code);

                    Assert.Equal(6.5, state.Rule.Threshold, 3);
                }
                finally
                {
                    Thread.CurrentThread.CurrentCulture = saved;
                }
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // 치명 알람 비활성화 확인
        // ─────────────────────────────────────────────────────────────────────

        [Fact]
        public void 치명_알람을_끄려_하면_확인을_요구한다()
        {
            using (TempConfig config = new TempConfig())
            using (HmiHost host = CreateHost(config))
            {
                string before = File.ReadAllText(config.PathOf("alarms.json"));

                AlarmEditorViewModel vm = Unlocked(host);
                AlarmRuleRowViewModel critical = FindEnabledCritical(vm);

                critical.Enabled = false;

                Assert.NotNull(vm.CriticalDisableWarning);
                Assert.Contains(critical.Code, vm.CriticalDisableWarning);

                vm.SaveCommand.Execute(null);

                Assert.True(vm.HasError);
                Assert.Equal(before, File.ReadAllText(config.PathOf("alarms.json")));
            }
        }

        [Fact]
        public void 확인하면_치명_알람을_끌_수_있다()
        {
            // 막지는 않는다. 막으면 파일을 직접 열게 되고, 그 경로에는 검증도 확인도 없다.
            using (TempConfig config = new TempConfig())
            using (HmiHost host = CreateHost(config))
            {
                AlarmEditorViewModel vm = Unlocked(host);
                AlarmRuleRowViewModel critical = FindEnabledCritical(vm);

                critical.Enabled = false;
                vm.CriticalDisableConfirmed = true;

                vm.SaveCommand.Execute(null);

                Assert.False(vm.HasError, Join(vm.Errors));

                AlarmState state = host.Runtime.Alarms.FindState(critical.Code);

                Assert.False(state.Rule.Enabled);
            }
        }

        [Fact]
        public void 치명이_아닌_알람은_확인을_요구하지_않는다()
        {
            using (TempConfig config = new TempConfig())
            using (HmiHost host = CreateHost(config))
            {
                AlarmEditorViewModel vm = Unlocked(host);
                AlarmRuleRowViewModel row = FindEnabledNonCritical(vm);

                row.Enabled = false;

                Assert.Null(vm.CriticalDisableWarning);

                vm.SaveCommand.Execute(null);

                Assert.False(vm.HasError, Join(vm.Errors));
            }
        }

        [Fact]
        public void 저장한_뒤에는_같은_해제를_다시_묻지_않는다()
        {
            // 두 번째부터 확인이 형식이 되면 아무도 읽지 않는다.
            using (TempConfig config = new TempConfig())
            using (HmiHost host = CreateHost(config))
            {
                AlarmEditorViewModel vm = Unlocked(host);
                AlarmRuleRowViewModel critical = FindEnabledCritical(vm);

                critical.Enabled = false;
                vm.CriticalDisableConfirmed = true;
                vm.SaveCommand.Execute(null);

                Assert.False(vm.HasError, Join(vm.Errors));
                Assert.Null(vm.CriticalDisableWarning);
                Assert.False(vm.CriticalDisableConfirmed);
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // 도우미
        // ─────────────────────────────────────────────────────────────────────

        private static AlarmEditorViewModel Unlocked(HmiHost host)
        {
            host.WriteAccessControl.SetAllowed(true);
            return new AlarmEditorViewModel(host);
        }

        private static AlarmRuleRowViewModel Row(AlarmEditorViewModel vm, string code)
        {
            foreach (AlarmRuleRowViewModel row in vm.All)
            {
                if (string.Equals(row.Code, code, StringComparison.OrdinalIgnoreCase))
                {
                    return row;
                }
            }

            throw new InvalidOperationException("행이 없습니다: " + code);
        }

        private static AlarmRuleRowViewModel FindEnabledCritical(AlarmEditorViewModel vm)
        {
            foreach (AlarmRuleRowViewModel row in vm.All)
            {
                if (row.IsCritical && row.Enabled)
                {
                    return row;
                }
            }

            throw new InvalidOperationException("활성 치명 규칙이 없습니다.");
        }

        private static AlarmRuleRowViewModel FindEnabledNonCritical(AlarmEditorViewModel vm)
        {
            foreach (AlarmRuleRowViewModel row in vm.All)
            {
                if (!row.IsCritical && row.Enabled)
                {
                    return row;
                }
            }

            throw new InvalidOperationException("활성 비치명 규칙이 없습니다.");
        }

        private static AlarmRuleRowViewModel FindEditableThreshold(AlarmEditorViewModel vm)
        {
            foreach (AlarmRuleRowViewModel row in vm.All)
            {
                if (row.IsThresholdEditable)
                {
                    return row;
                }
            }

            throw new InvalidOperationException("임계값을 고칠 수 있는 규칙이 없습니다.");
        }

        private static string Join(IEnumerable<string> messages)
        {
            return string.Join(" / ", messages);
        }

        private static int CountComments(string text)
        {
            int count = 0;
            int index = 0;

            while (true)
            {
                index = text.IndexOf("//", index, StringComparison.Ordinal);

                if (index < 0)
                {
                    return count;
                }

                count++;
                index += 2;
            }
        }

        private static HmiHost CreateHost(TempConfig config)
        {
            HmiHost host = new HmiHost();

            if (!host.Start(config.Folder, TransportMode.Simulation))
            {
                string error = host.StartupError;
                host.Dispose();
                Assert.Fail("시뮬레이션 조립이 실패했습니다: " + error);
            }

            return host;
        }

        /// <summary>배포 설정을 복사한 임시 폴더.</summary>
        private sealed class TempConfig : IDisposable
        {
            /// <summary>임시 폴더를 만들고 설정 파일을 복사한다.</summary>
            public TempConfig()
            {
                Folder = Path.Combine(
                    Path.GetTempPath(), "esam-alarm-" + Guid.NewGuid().ToString("N"));

                Directory.CreateDirectory(Folder);

                string[] names = { "device-map.json", "alarms.json", "recipe.json" };

                foreach (string name in names)
                {
                    File.Copy(
                        Path.Combine(DeployedConfigFolder, name),
                        Path.Combine(Folder, name));
                }
            }

            /// <summary>임시 폴더 경로.</summary>
            public string Folder { get; private set; }

            /// <summary>파일 경로를 만든다.</summary>
            /// <param name="name">파일 이름.</param>
            /// <returns>전체 경로.</returns>
            public string PathOf(string name)
            {
                return Path.Combine(Folder, name);
            }

            /// <inheritdoc />
            public void Dispose()
            {
                try
                {
                    Directory.Delete(Folder, true);
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }
    }
}
