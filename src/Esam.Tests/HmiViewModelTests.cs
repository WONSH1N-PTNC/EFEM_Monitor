using System;
using System.Globalization;
using System.IO;
using System.Threading;
using Esam.Communication.Configuration;
using Esam.Domain.Configuration;
using Esam.Hmi.Infrastructure;
using Esam.Hmi.ViewModels;
using Esam.Services;
using Xunit;

namespace Esam.Tests
{
    /// <summary>
    /// HMI ViewModel 의 판단 로직을 검증한다.
    /// </summary>
    /// <remarks>
    /// <para><b>화면을 띄우지 않는다.</b> Window·UserControl·XAML 은 대상이 아니다.
    /// ViewModel 만 직접 만들어 호출한다.</para>
    /// <para>여기서 다루는 것은 표시 로직이 아니라 <b>설비 거동을 바꾸는 판단</b>이다.
    /// 입력 문자열 파싱, 쓰기 잠금 관문, 저장 전 검증 경유 세 가지다.
    /// 셋 다 틀렸을 때 프로그램은 정상으로 보이면서 잘못된 설정값으로 운전에 들어간다.
    /// 그래서 화면 코드라도 테스트가 필요하다.</para>
    /// <para><b>런타임은 조립하되 기동하지 않는다.</b> <c>Start()</c> 를 부르지 않으면
    /// 워커 스레드가 없고, 그러면 <c>Stop()</c> 이 파킹을 기다리지 않아 즉시 끝난다.
    /// 설정 적용 경로(재조립)까지 실시간 대기 없이 검증할 수 있다.</para>
    /// </remarks>
    public sealed class HmiViewModelTests
    {
        /// <summary>배포 설정 파일이 있는 폴더(테스트 출력에 복사된다).</summary>
        private const string DeployedConfigFolder = "config";

        #region 도우미

        /// <summary>
        /// 배포 설정을 복사한 임시 폴더. 테스트가 파일을 고쳐도 배포본이 다치지 않는다.
        /// </summary>
        private sealed class TempConfig : IDisposable
        {
            /// <summary>임시 폴더를 만들고 설정 파일을 복사한다.</summary>
            public TempConfig()
            {
                Folder = Path.Combine(
                    Path.GetTempPath(), "esam-hmi-" + Guid.NewGuid().ToString("N"));

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
                    // 임시 폴더 정리 실패가 테스트 결과를 바꾸면 안 된다.
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }

        /// <summary>임시 설정 폴더로 런타임을 조립한다(기동하지 않는다).</summary>
        /// <param name="config">임시 설정 폴더.</param>
        /// <returns>조립된 호스트.</returns>
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

        /// <summary>배포 통신 구성을 읽는다.</summary>
        /// <returns>디바이스 맵.</returns>
        private static DeviceMap LoadDeployedMap()
        {
            ConfigLoadResult result = CommunicationConfigLoader.LoadFromFile(
                Path.Combine(DeployedConfigFolder, "device-map.json"));

            if (!result.IsSuccess)
            {
                Assert.Fail("배포 통신 구성을 읽지 못했습니다.");
            }

            return result.Map;
        }

        /// <summary>첫 번째 센서 행을 만든다.</summary>
        /// <returns>편집 행.</returns>
        private static SensorSettingRowViewModel CreateRow()
        {
            return new SensorSettingRowViewModel(new SensorSetting("S2-1", -10.0, -40.0, 20.0));
        }

        #endregion

        #region 문자열 파싱 — 입력 도중 상태

        /// <summary>
        /// 값을 문자열로 들고 있어야 입력 도중 상태가 보존된다.
        /// </summary>
        /// <remarks>
        /// double 로 바인딩하면 "1." 같은 중간 상태에서 변환이 실패해
        /// 바인딩이 옛 값을 되돌린다. 작업자에게는 <b>키가 씹히는 화면</b>으로 보인다.
        /// </remarks>
        [Fact]
        public void 입력_도중_상태가_되돌려지지_않는다()
        {
            SensorSettingRowViewModel row = CreateRow();

            row.Setpoint = "-";
            Assert.Equal("-", row.Setpoint);

            row.Setpoint = "-1.";
            Assert.Equal("-1.", row.Setpoint);

            row.Setpoint = "-1.5";
            Assert.Equal("-1.5", row.Setpoint);
        }

        /// <summary>음수와 소수를 모두 받는다.</summary>
        /// <remarks>
        /// Sensor 2·3 의 설정값은 음압이라 음수가 정상값이다.
        /// 음수를 거부하면 배기 계통을 아예 설정할 수 없다.
        /// </remarks>
        [Fact]
        public void 음수와_소수를_모두_받는다()
        {
            SensorSettingRowViewModel row = CreateRow();

            row.Setpoint = "-12.5";
            row.LowLimit = "-40";
            row.HighLimit = "0.25";

            string error;
            SensorSetting setting = row.ToSetting(out error);

            if (setting == null)
            {
                Assert.Fail("음수·소수 입력이 거부되었습니다: " + error);
            }

            Assert.Null(error);
            Assert.Equal(-12.5, setting.SetpointPa, 6);
            Assert.Equal(-40.0, setting.LowLimitPa, 6);
            Assert.Equal(0.25, setting.HighLimitPa, 6);
        }

        /// <summary>빈칸은 거부하고 어느 칸인지 사유에 남긴다.</summary>
        /// <remarks>
        /// "입력값을 확인하십시오" 만 띄우면 13개 센서 × 3칸 중 어디가 문제인지
        /// 작업자가 찾을 수 없다.
        /// </remarks>
        [Fact]
        public void 빈칸은_거부하고_어느_칸인지_사유에_남긴다()
        {
            SensorSettingRowViewModel row = CreateRow();
            row.Setpoint = string.Empty;

            string error;

            Assert.Null(row.ToSetting(out error));
            Assert.Contains("S2-1", error);
            Assert.Contains("설정값", error);
        }

        /// <summary>숫자가 아닌 입력을 칸별로 구분해 거부한다.</summary>
        [Fact]
        public void 숫자가_아닌_입력을_칸별로_구분해_거부한다()
        {
            SensorSettingRowViewModel row = CreateRow();
            row.LowLimit = "abc";

            string error;

            Assert.Null(row.ToSetting(out error));
            Assert.Contains("하한", error);

            row.LowLimit = "-40";
            row.HighLimit = "20 Pa";

            Assert.Null(row.ToSetting(out error));
            Assert.Contains("상한", error);
        }

        /// <summary>앞뒤 공백은 허용한다.</summary>
        /// <remarks>붙여넣기하면 공백이 따라오는 일이 흔하다.</remarks>
        [Fact]
        public void 앞뒤_공백은_허용한다()
        {
            SensorSettingRowViewModel row = CreateRow();
            row.Setpoint = "  -6  ";

            string error;
            SensorSetting setting = row.ToSetting(out error);

            if (setting == null)
            {
                Assert.Fail("공백이 붙은 입력이 거부되었습니다: " + error);
            }

            Assert.Equal(-6.0, setting.SetpointPa, 6);
        }

        /// <summary>
        /// 현재 문화권이 바뀌어도 소수점 해석이 같다.
        /// </summary>
        /// <remarks>
        /// <para><b>이것이 문화권을 고정한 이유다.</b> 현장 PC 의 지역 설정은
        /// 우리가 통제할 수 없다. 쉼표를 소수 구분자로 쓰는 지역 설정에서
        /// 현재 문화권으로 파싱하면 "6.5" 가 65 로 읽힌다.</para>
        /// <para>설정값이 10배로 들어가도 프로그램은 아무 오류를 내지 않는다.
        /// 압력 목표가 10배가 된 채 운전에 들어간다.</para>
        /// </remarks>
        [Fact]
        public void 현재_문화권이_바뀌어도_소수점_해석이_같다()
        {
            CultureInfo original = Thread.CurrentThread.CurrentCulture;

            try
            {
                // 쉼표를 소수 구분자로 쓰는 문화권.
                Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");

                SensorSettingRowViewModel row = CreateRow();
                row.Setpoint = "6.5";

                string error;
                SensorSetting setting = row.ToSetting(out error);

                if (setting == null)
                {
                    Assert.Fail("문화권이 바뀌자 소수 입력이 거부되었습니다: " + error);
                }

                Assert.Equal(6.5, setting.SetpointPa, 6);

                // 반대로 그 지역 표기(쉼표)는 받지 않는다. 조용히 다른 값이 되는 편보다
                // 거부하고 사유를 보여 주는 편이 낫다.
                row.Setpoint = "6,5";
                Assert.Null(row.ToSetting(out error));
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = original;
            }
        }

        #endregion

        #region 포트 설정 행

        /// <summary>포트 이름이 비면 거부한다.</summary>
        [Fact]
        public void 포트_이름이_비면_거부한다()
        {
            DeviceMap map = LoadDeployedMap();
            PortSettingRowViewModel row = new PortSettingRowViewModel(map.Ports[0]);

            row.PortName = "   ";

            string error;

            Assert.False(row.Validate(out error));
            Assert.Contains(row.PortId, error);
        }

        /// <summary>통신 속도가 숫자가 아니면 거부한다.</summary>
        [Fact]
        public void 통신_속도가_숫자가_아니면_거부한다()
        {
            DeviceMap map = LoadDeployedMap();
            PortSettingRowViewModel row = new PortSettingRowViewModel(map.Ports[0]);

            row.BaudRate = "115.2k";

            string error;

            Assert.False(row.Validate(out error));
            Assert.Contains("통신 속도", error);
        }

        /// <summary>통신 속도가 0 이하면 거부한다.</summary>
        [Fact]
        public void 통신_속도가_0_이하면_거부한다()
        {
            DeviceMap map = LoadDeployedMap();
            PortSettingRowViewModel row = new PortSettingRowViewModel(map.Ports[0]);

            row.BaudRate = "0";

            string error;

            Assert.False(row.Validate(out error));
        }

        /// <summary>
        /// 포트 이름의 앞뒤 공백을 잘라 반영한다.
        /// </summary>
        /// <remarks>
        /// "COM3 " 은 열리지 않는다. 그런데 화면에서는 "COM3" 과 구분되지 않으므로
        /// 작업자가 몇 번을 다시 봐도 원인을 찾지 못한다.
        /// </remarks>
        [Fact]
        public void 포트_이름의_앞뒤_공백을_잘라_반영한다()
        {
            DeviceMap map = LoadDeployedMap();
            PortSettingRowViewModel row = new PortSettingRowViewModel(map.Ports[0]);

            row.PortName = "  COM7  ";
            row.BaudRate = "38400";

            string error;

            Assert.True(row.Validate(out error));

            row.ApplyTo(map.Ports[0]);

            Assert.Equal("COM7", map.Ports[0].Serial.PortName);
            Assert.Equal(38400, map.Ports[0].Serial.BaudRate);
        }

        #endregion

        #region 쓰기 잠금 관문

        /// <summary>
        /// 기본값은 쓰기 거부다.
        /// </summary>
        /// <remarks>
        /// 기본을 허용으로 두면 관문이 있으나 마나 한 상태가 되고,
        /// 그 상태로 화면을 다 만들면 관문이 한 번도 동작하지 않은 채 출하된다.
        /// </remarks>
        [Fact]
        public void 쓰기는_기본적으로_거부된다()
        {
            ManualWriteAccessProvider access = new ManualWriteAccessProvider();

            Assert.False(access.IsWriteAllowed);
            Assert.NotNull(access.DescribeDenial());
        }

        /// <summary>허용하면 거부 사유가 사라진다.</summary>
        [Fact]
        public void 허용하면_거부_사유가_사라진다()
        {
            ManualWriteAccessProvider access = new ManualWriteAccessProvider();
            access.SetAllowed(true);

            Assert.True(access.IsWriteAllowed);
            Assert.Null(access.DescribeDenial());
        }

        /// <summary>
        /// 같은 값으로 설정하면 통지가 발생하지 않는다.
        /// </summary>
        /// <remarks>
        /// 상태가 바뀌지 않았는데 통지하면 커맨드 재조회가 불필요하게 돈다.
        /// </remarks>
        [Fact]
        public void 같은_값으로_설정하면_통지하지_않는다()
        {
            ManualWriteAccessProvider access = new ManualWriteAccessProvider();
            int count = 0;

            access.WriteAccessChanged += (s, e) => count++;

            access.SetAllowed(true);
            access.SetAllowed(true);
            access.SetAllowed(false);

            Assert.Equal(2, count);
        }

        #endregion

        #region 호스트 조립

        /// <summary>
        /// 설정 폴더가 없으면 예외가 아니라 사유를 남긴다.
        /// </summary>
        /// <remarks>
        /// 여기서 예외를 던지면 창이 뜨지 못한다. 화면이 없으면 작업자는
        /// 원인을 볼 수도, 설정을 고칠 수도 없다. D17 과 같은 종류의 결함이다.
        /// </remarks>
        [Fact]
        public void 설정_폴더가_없으면_예외가_아니라_사유를_남긴다()
        {
            using (HmiHost host = new HmiHost())
            {
                bool ok = host.Start("__no_such_config_folder__", TransportMode.Simulation);

                Assert.False(ok);
                Assert.Null(host.Runtime);
                Assert.False(string.IsNullOrEmpty(host.StartupError));
            }
        }

        /// <summary>배포 설정으로 시뮬레이션 조립이 성공한다.</summary>
        [Fact]
        public void 배포_설정으로_시뮬레이션_조립이_성공한다()
        {
            using (TempConfig config = new TempConfig())
            using (HmiHost host = CreateHost(config))
            {
                Assert.NotNull(host.Runtime);
                Assert.Null(host.StartupError);
                Assert.Equal(config.Folder, host.ConfigFolder);

                // 제어 설정에 통로 5조가 세워져야 한다.
                Assert.Equal(5, host.Runtime.Control.Chains.Count);

                // 레시피가 붙어야 설정값 화면이 뜬다.
                Assert.NotNull(host.Runtime.Control.Recipe);
            }
        }

        /// <summary>
        /// 재조립하면 옛 런타임을 버린다.
        /// </summary>
        /// <remarks>
        /// 옛 런타임이 남으면 같은 COM 포트를 두 번 열려다 실패한다.
        /// 시뮬레이션에서는 드러나지 않고 실장비에서만 터지는 종류다.
        /// </remarks>
        [Fact]
        public void 재조립하면_옛_런타임을_버린다()
        {
            using (TempConfig config = new TempConfig())
            using (HmiHost host = CreateHost(config))
            {
                EsamRuntime first = host.Runtime;

                Assert.True(host.Start(config.Folder, TransportMode.Simulation));
                Assert.NotNull(host.Runtime);
                Assert.NotSame(first, host.Runtime);
            }
        }

        #endregion

        #region 레시피 편집 화면

        /// <summary>잠긴 상태에서는 저장할 수 없다.</summary>
        [Fact]
        public void 잠긴_상태에서는_레시피를_저장할_수_없다()
        {
            using (TempConfig config = new TempConfig())
            using (HmiHost host = CreateHost(config))
            {
                RecipeEditorViewModel editor = new RecipeEditorViewModel(host);

                Assert.True(editor.IsLocked);
                Assert.NotNull(editor.LockNotice);
                Assert.False(editor.SaveCommand.CanExecute(null));

                host.WriteAccessControl.SetAllowed(true);

                Assert.False(editor.IsLocked);
                Assert.Null(editor.LockNotice);
                Assert.True(editor.SaveCommand.CanExecute(null));
            }
        }

        /// <summary>현재 런타임의 레시피가 화면에 채워진다.</summary>
        [Fact]
        public void 레시피_편집기가_센서_13대를_읽는다()
        {
            using (TempConfig config = new TempConfig())
            using (HmiHost host = CreateHost(config))
            {
                RecipeEditorViewModel editor = new RecipeEditorViewModel(host);

                Assert.Equal(13, editor.Sensors.Count);
                Assert.False(editor.HasError);
                Assert.Equal(config.PathOf("recipe.json"), editor.RecipePath);
            }
        }

        /// <summary>숫자가 아닌 입력은 파일을 바꾸지 않는다.</summary>
        [Fact]
        public void 숫자가_아닌_입력은_레시피_파일을_바꾸지_않는다()
        {
            using (TempConfig config = new TempConfig())
            using (HmiHost host = CreateHost(config))
            {
                host.WriteAccessControl.SetAllowed(true);

                RecipeEditorViewModel editor = new RecipeEditorViewModel(host);
                string before = File.ReadAllText(config.PathOf("recipe.json"));

                editor.Sensors[0].Setpoint = "여섯";
                editor.SaveCommand.Execute(null);

                Assert.True(editor.HasError);
                Assert.NotEmpty(editor.Errors);
                Assert.Equal(before, File.ReadAllText(config.PathOf("recipe.json")));
            }
        }

        /// <summary>
        /// 계측 레인지를 벗어난 값은 저장하지 않는다.
        /// </summary>
        /// <remarks>
        /// <b>화면이 아니라 로더가 잡아야 한다.</b> 화면에서 따로 검사하면 규칙이
        /// 두 곳에 생기고, 한쪽만 바뀌었을 때 화면은 통과시키는데 다음 기동에서
        /// 로드가 실패해 장비가 뜨지 않는다.
        /// </remarks>
        [Fact]
        public void 레인지를_벗어난_값은_저장하지_않는다()
        {
            using (TempConfig config = new TempConfig())
            using (HmiHost host = CreateHost(config))
            {
                host.WriteAccessControl.SetAllowed(true);

                RecipeEditorViewModel editor = new RecipeEditorViewModel(host);
                string before = File.ReadAllText(config.PathOf("recipe.json"));

                // 차압센서 레인지는 ±2000 Pa 다.
                editor.Sensors[0].HighLimit = "99999";
                editor.SaveCommand.Execute(null);

                Assert.True(editor.HasError);
                Assert.NotEmpty(editor.Errors);
                Assert.Equal(before, File.ReadAllText(config.PathOf("recipe.json")));
            }
        }

        /// <summary>
        /// 상하한이 뒤집히면 저장하지 않는다.
        /// </summary>
        /// <remarks>
        /// 뒤집힌 대역은 알람이 영구 발생하거나 영구 침묵한다.
        /// 후자가 더 위험하다. 아무 일도 일어나지 않으므로 아무도 눈치채지 못한다.
        /// </remarks>
        [Fact]
        public void 상하한이_뒤집히면_저장하지_않는다()
        {
            using (TempConfig config = new TempConfig())
            using (HmiHost host = CreateHost(config))
            {
                host.WriteAccessControl.SetAllowed(true);

                RecipeEditorViewModel editor = new RecipeEditorViewModel(host);
                string before = File.ReadAllText(config.PathOf("recipe.json"));

                editor.Sensors[0].LowLimit = "50";
                editor.Sensors[0].HighLimit = "-50";
                editor.SaveCommand.Execute(null);

                Assert.True(editor.HasError);
                Assert.NotEmpty(editor.Errors);
                Assert.Equal(before, File.ReadAllText(config.PathOf("recipe.json")));
            }
        }

        /// <summary>정상값은 파일과 런타임에 모두 반영된다.</summary>
        /// <remarks>
        /// 파일에만 쓰면 다음 기동까지 반영되지 않고, 런타임에만 넣으면
        /// 재기동에서 값이 되돌아간다. 어느 쪽이든 작업자는 화면을 믿지 못하게 된다.
        /// </remarks>
        [Fact]
        public void 정상값은_레시피_파일과_런타임에_모두_반영된다()
        {
            using (TempConfig config = new TempConfig())
            using (HmiHost host = CreateHost(config))
            {
                host.WriteAccessControl.SetAllowed(true);

                RecipeEditorViewModel editor = new RecipeEditorViewModel(host);
                string deviceId = editor.Sensors[0].DeviceId;

                editor.Sensors[0].Setpoint = "7.5";
                editor.SaveCommand.Execute(null);

                if (editor.HasError)
                {
                    Assert.Fail("정상값 저장이 실패했습니다: " + string.Join(" / ", editor.Errors));
                }

                // 1) 런타임에 즉시 반영된다.
                SensorSetting applied = host.Runtime.Control.Recipe.Find(deviceId);

                Assert.NotNull(applied);
                Assert.Equal(7.5, applied.SetpointPa, 6);

                // 2) 파일에도 남아 다음 기동에서 되돌아가지 않는다.
                RecipeLoadResult reloaded = RecipeConfigLoader.LoadFromFile(
                    config.PathOf("recipe.json"), host.Runtime.Map);

                if (!reloaded.IsSuccess)
                {
                    Assert.Fail("저장한 파일을 다시 읽지 못했습니다: "
                                + string.Join(" / ", reloaded.Errors));
                }

                Assert.Equal(7.5, reloaded.Recipe.Find(deviceId).SetpointPa, 6);
            }
        }

        /// <summary>다시 읽기는 편집 중이던 값을 버린다.</summary>
        [Fact]
        public void 다시_읽기는_편집_중이던_값을_버린다()
        {
            using (TempConfig config = new TempConfig())
            using (HmiHost host = CreateHost(config))
            {
                RecipeEditorViewModel editor = new RecipeEditorViewModel(host);
                string original = editor.Sensors[0].Setpoint;

                editor.Sensors[0].Setpoint = "123";
                editor.ReloadCommand.Execute(null);

                Assert.Equal(original, editor.Sensors[0].Setpoint);
                Assert.False(editor.HasError);
            }
        }

        #endregion

        #region 설정 화면

        /// <summary>잠긴 상태에서는 적용을 막고 사유를 남긴다.</summary>
        /// <remarks>
        /// 이유 없이 눌리지 않는 버튼은 작업자가 프로그램이 멈췄다고 판단하게 만든다.
        /// </remarks>
        [Fact]
        public void 잠긴_상태에서는_설정_적용을_막고_사유를_남긴다()
        {
            using (TempConfig config = new TempConfig())
            using (HmiHost host = CreateHost(config))
            {
                SettingsViewModel settings = new SettingsViewModel(host, null);

                Assert.True(settings.IsLocked);
                Assert.False(settings.IsRunning);
                Assert.NotNull(settings.BlockReason);
                Assert.False(settings.ApplyCommand.CanExecute(null));

                host.WriteAccessControl.SetAllowed(true);

                Assert.False(settings.IsLocked);
                Assert.Null(settings.BlockReason);
                Assert.True(settings.ApplyCommand.CanExecute(null));
            }
        }

        /// <summary>현재 구성이 화면에 채워진다.</summary>
        [Fact]
        public void 설정_화면이_현재_구성을_읽는다()
        {
            using (TempConfig config = new TempConfig())
            using (HmiHost host = CreateHost(config))
            {
                SettingsViewModel settings = new SettingsViewModel(host, null);

                Assert.Equal(host.Runtime.Map.Ports.Count, settings.Ports.Count);
                Assert.Equal(5, settings.Chains.Count);

                // 시뮬레이션으로 조립했으므로 체크가 켜져 있어야 한다.
                // 옵션을 따로 기억해 두면 재조립 실패 시 화면과 실제가 어긋난다.
                Assert.True(settings.UseSimulation);
            }
        }

        /// <summary>눈금 배율이 범위를 벗어나면 적용하지 않는다.</summary>
        [Fact]
        public void 눈금_배율이_범위를_벗어나면_적용하지_않는다()
        {
            using (TempConfig config = new TempConfig())
            using (HmiHost host = CreateHost(config))
            {
                host.WriteAccessControl.SetAllowed(true);

                SettingsViewModel settings = new SettingsViewModel(host, null);
                string before = File.ReadAllText(config.PathOf("device-map.json"));

                settings.GaugeSpanText = "0.2";
                settings.ApplyCommand.Execute(null);

                Assert.True(settings.HasError);
                Assert.NotEmpty(settings.Errors);
                Assert.Equal(before, File.ReadAllText(config.PathOf("device-map.json")));
            }
        }

        /// <summary>
        /// 통로를 전부 끄면 적용하지 않는다.
        /// </summary>
        /// <remarks>
        /// 제어 대상이 없으면 자동 운전에 들어가도 아무 일이 일어나지 않는다.
        /// 그 이유가 화면 어디에도 없으면 통신 고장으로 오진하게 된다.
        /// </remarks>
        [Fact]
        public void 통로를_전부_끄면_적용하지_않는다()
        {
            using (TempConfig config = new TempConfig())
            using (HmiHost host = CreateHost(config))
            {
                host.WriteAccessControl.SetAllowed(true);

                SettingsViewModel settings = new SettingsViewModel(host, null);

                foreach (ChainToggleViewModel chain in settings.Chains)
                {
                    chain.IsEnabled = false;
                }

                settings.ApplyCommand.Execute(null);

                Assert.True(settings.HasError);
                Assert.NotEmpty(settings.Errors);
            }
        }

        /// <summary>포트 이름이 비면 적용하지 않는다.</summary>
        [Fact]
        public void 포트_이름이_비면_설정을_적용하지_않는다()
        {
            using (TempConfig config = new TempConfig())
            using (HmiHost host = CreateHost(config))
            {
                host.WriteAccessControl.SetAllowed(true);

                SettingsViewModel settings = new SettingsViewModel(host, null);
                string before = File.ReadAllText(config.PathOf("device-map.json"));

                settings.Ports[0].PortName = string.Empty;
                settings.ApplyCommand.Execute(null);

                Assert.True(settings.HasError);
                Assert.Equal(before, File.ReadAllText(config.PathOf("device-map.json")));
            }
        }

        /// <summary>
        /// 두 포트에 같은 이름을 주면 검증이 막고 화면 값을 되돌린다.
        /// </summary>
        /// <remarks>
        /// <para>같은 COM 포트를 두 논리 포트가 열면 반이중 직렬화가 깨진다.</para>
        /// <para><b>살아 있는 맵이 더럽혀지지 않는 것이 핵심이다(D18).</b>
        /// 검증을 살아 있는 맵에 직접 반영한 뒤에 돌리면, 실패했을 때
        /// 파일은 옛 값인데 메모리는 새 값인 상태가 남는다.</para>
        /// <para>동시에 <b>작업자가 입력한 값은 지우지 않는다.</b> 무엇을 고치려
        /// 했는지 사라지면 사유를 읽어도 어디를 손댈지 알 수 없다.</para>
        /// </remarks>
        [Fact]
        public void 두_포트에_같은_이름을_주면_적용하지_않고_맵을_더럽히지_않는다()
        {
            using (TempConfig config = new TempConfig())
            using (HmiHost host = CreateHost(config))
            {
                if (host.Runtime.Map.Ports.Count < 2)
                {
                    Assert.Fail("이 테스트는 논리 포트가 2개 이상인 구성을 전제한다.");
                }

                host.WriteAccessControl.SetAllowed(true);

                SettingsViewModel settings = new SettingsViewModel(host, null);
                string before = File.ReadAllText(config.PathOf("device-map.json"));
                string kept = host.Runtime.Map.Ports[1].Serial.PortName;

                string typed = settings.Ports[0].PortName;
                settings.Ports[1].PortName = typed;
                settings.ApplyCommand.Execute(null);

                Assert.True(settings.HasError);
                Assert.NotEmpty(settings.Errors);
                Assert.Equal(before, File.ReadAllText(config.PathOf("device-map.json")));

                // 살아 있는 맵은 손대지 않는다. 사본에만 반영해 검증했기 때문이다.
                Assert.Equal(kept, host.Runtime.Map.Ports[1].Serial.PortName);

                // 작업자가 입력한 값은 남는다. 그 자리에서 바로잡을 수 있어야 한다.
                Assert.Equal(typed, settings.Ports[1].PortName);
            }
        }

        /// <summary>
        /// 적용하면 포트 이름이 파일과 새 런타임에 함께 반영된다.
        /// </summary>
        /// <remarks>
        /// 통신 설정은 재조립을 거쳐야 반영된다. 파일에만 쓰고 끝내면
        /// 화면은 새 값을 보여 주는데 실제 폴링은 옛 포트로 계속 돈다.
        /// </remarks>
        [Fact]
        public void 적용하면_포트_이름이_파일과_새_런타임에_반영된다()
        {
            using (TempConfig config = new TempConfig())
            using (HmiHost host = CreateHost(config))
            {
                host.WriteAccessControl.SetAllowed(true);

                int rebuilt = 0;
                SettingsViewModel settings = new SettingsViewModel(host, () => rebuilt++);

                EsamRuntime before = host.Runtime;

                settings.Ports[0].PortName = "COM77";
                settings.Ports[0].BaudRate = "38400";
                settings.ApplyCommand.Execute(null);

                if (settings.HasError)
                {
                    Assert.Fail("설정 적용이 실패했습니다: " + string.Join(" / ", settings.Errors));
                }

                // 1) 재조립 콜백이 정확히 한 번 돈다. 화면 재배선이 여기 달려 있다.
                Assert.Equal(1, rebuilt);

                // 2) 새 런타임으로 교체된다.
                Assert.NotNull(host.Runtime);
                Assert.NotSame(before, host.Runtime);
                Assert.Equal("COM77", host.Runtime.Map.Ports[0].Serial.PortName);

                // 3) 파일에도 남는다.
                ConfigLoadResult saved = CommunicationConfigLoader.LoadFromFile(
                    config.PathOf("device-map.json"));

                if (!saved.IsSuccess)
                {
                    Assert.Fail("저장한 통신 구성을 다시 읽지 못했습니다.");
                }

                Assert.Equal("COM77", saved.Map.Ports[0].Serial.PortName);
                Assert.Equal(38400, saved.Map.Ports[0].Serial.BaudRate);
            }
        }

        /// <summary>
        /// 통로 활성화는 재조립된 런타임의 제어 설정에 다시 실린다.
        /// </summary>
        /// <remarks>
        /// 통로 활성화는 <c>control</c> 쪽 값이라 파일이 없다.
        /// 재조립하면 기본값(전부 활성)으로 돌아가므로 다시 실어 줘야 한다.
        /// </remarks>
        [Fact]
        public void 통로_활성화는_재조립_후에도_유지된다()
        {
            using (TempConfig config = new TempConfig())
            using (HmiHost host = CreateHost(config))
            {
                host.WriteAccessControl.SetAllowed(true);

                SettingsViewModel settings = new SettingsViewModel(host, null);

                // 통로 5 만 끈다. 하나는 남겨 둬야 검증을 통과한다.
                settings.Chains[4].IsEnabled = false;
                settings.ApplyCommand.Execute(null);

                if (settings.HasError)
                {
                    Assert.Fail("설정 적용이 실패했습니다: " + string.Join(" / ", settings.Errors));
                }

                ChainDefinition chain5 = null;

                foreach (ChainDefinition chain in host.Runtime.Control.Chains)
                {
                    if (chain.Id == 5)
                    {
                        chain5 = chain;
                        break;
                    }
                }

                Assert.NotNull(chain5);
                Assert.False(chain5.Enabled);
            }
        }

        #endregion

        #region 구성 경고 배너

        /// <summary>시뮬레이션 조립에서는 구성 경고가 배너에 뜬다.</summary>
        [Fact]
        public void 시뮬레이션_조립의_구성_경고가_배너에_뜬다()
        {
            using (TempConfig config = new TempConfig())
            using (HmiHost host = CreateHost(config))
            {
                SystemBannerViewModel banner = new SystemBannerViewModel(host);

                Assert.False(banner.HasStartupError);
                Assert.NotEmpty(banner.Warnings);
                Assert.True(banner.IsVisible);
                Assert.True(banner.IsExpanded);
            }
        }

        /// <summary>
        /// 확인하면 접히지만 사라지지 않는다.
        /// </summary>
        /// <remarks>
        /// <para>완전히 감추면 "안전 입력이 배선되지 않은 채 운전 중" 이라는 사실이
        /// 화면에서 없어진다. 확인은 <b>인지했다는 기록</b>이지 해결이 아니다.</para>
        /// </remarks>
        [Fact]
        public void 경고를_확인하면_접히지만_사라지지_않는다()
        {
            using (TempConfig config = new TempConfig())
            using (HmiHost host = CreateHost(config))
            {
                SystemBannerViewModel banner = new SystemBannerViewModel(host);

                if (!banner.HasBlocking)
                {
                    // 현재 HMI 조립에는 interlocks 설정 파일이 없어 SAFE-02 가 뜬다.
                    // 그것이 없어졌다면 이 테스트의 전제를 다시 세워야 한다.
                    Assert.Fail("차단 경고가 없어 확인 동작을 검증할 수 없습니다.");
                }

                int count = banner.Warnings.Count;

                Assert.True(banner.AcknowledgeCommand.CanExecute(null));
                banner.AcknowledgeCommand.Execute(null);

                Assert.True(banner.IsAcknowledged);
                Assert.False(banner.IsExpanded);

                // 목록은 남는다.
                Assert.Equal(count, banner.Warnings.Count);
                Assert.True(banner.IsVisible);
                Assert.False(string.IsNullOrEmpty(banner.CollapsedSummary));

                // 두 번 확인할 수는 없다.
                Assert.False(banner.AcknowledgeCommand.CanExecute(null));
            }
        }

        /// <summary>기동에 실패하면 배너를 접을 수 없다.</summary>
        /// <remarks>운전 자체가 불가능한 상태이므로 한 줄로 줄이지 않는다.</remarks>
        [Fact]
        public void 기동_실패는_접을_수_없다()
        {
            using (HmiHost host = new HmiHost())
            {
                host.Start("__no_such_config_folder__", TransportMode.Simulation);

                SystemBannerViewModel banner = new SystemBannerViewModel(host);

                Assert.True(banner.HasStartupError);
                Assert.True(banner.IsVisible);
                Assert.True(banner.IsExpanded);

                banner.ToggleCommand.Execute(null);

                Assert.True(banner.IsExpanded);
            }
        }

        #endregion

        #region 셸

        /// <summary>화면을 전환하면 한 화면만 보인다.</summary>
        [Fact]
        public void 화면을_전환하면_한_화면만_보인다()
        {
            using (TempConfig config = new TempConfig())
            using (HmiHost host = CreateHost(config))
            {
                ShellViewModel shell = new ShellViewModel(host);

                Assert.True(shell.IsOperate);
                Assert.False(shell.IsConfigRecipe);
                Assert.False(shell.IsConfigSystem);

                shell.SelectScreenCommand.Execute("ConfigRecipe");

                Assert.False(shell.IsOperate);
                Assert.True(shell.IsConfigRecipe);
                Assert.False(shell.IsConfigSystem);

                shell.SelectScreenCommand.Execute("ConfigSystem");

                Assert.False(shell.IsConfigRecipe);
                Assert.True(shell.IsConfigSystem);

                shell.SelectScreenCommand.Execute("Operate");

                Assert.True(shell.IsOperate);
                Assert.False(shell.IsConfigSystem);
            }
        }

        /// <summary>
        /// 쓰기 잠금 버튼 문구는 누르면 될 상태를 적는다.
        /// </summary>
        /// <remarks>
        /// 현재 상태를 적으면 "지금 잠겨 있다" 인지 "누르면 잠긴다" 인지 헷갈린다.
        /// </remarks>
        [Fact]
        public void 쓰기_잠금_버튼_문구는_누르면_될_상태를_적는다()
        {
            using (TempConfig config = new TempConfig())
            using (HmiHost host = CreateHost(config))
            {
                ShellViewModel shell = new ShellViewModel(host);

                Assert.False(shell.IsWriteAllowed);
                Assert.Equal("정비 모드 진입", shell.WriteAccessText);

                shell.ToggleWriteAccessCommand.Execute(null);

                Assert.True(shell.IsWriteAllowed);
                Assert.Equal("쓰기 잠그기", shell.WriteAccessText);
            }
        }

        #endregion
    }
}
