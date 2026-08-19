using System;
using System.Collections.Generic;
using Esam.Domain.Control;
using Esam.Domain.Models;
using Esam.Services;
using Xunit;

namespace Esam.Tests
{
    /// <summary>
    /// 수동 조작의 관문 검증(S7.5-c).
    /// </summary>
    /// <remarks>
    /// <para>지금까지의 쓰기는 전부 설정 파일이었다. 여기는 <b>밸브와 팬을 직접
    /// 움직인다.</b> 관문이 하나라도 새면 사람이 자동 운전 중에, 또는 인터록이
    /// 걸린 상태에서 액추에이터를 건드릴 수 있다.</para>
    /// <para>그래서 "허용되는가" 보다 <b>"거부되는가"</b> 를 중심으로 짰다.</para>
    /// </remarks>
    public sealed class ManualControlTests : IDisposable
    {
        private readonly List<EsamRuntime> _runtimes = new List<EsamRuntime>();

        /// <inheritdoc />
        public void Dispose()
        {
            foreach (EsamRuntime runtime in _runtimes)
            {
                try
                {
                    // 워커를 띄우지 않았으므로 파킹을 기다릴 것이 없다.
                    runtime.Stop(0);
                    runtime.Dispose();
                }
                catch (Exception)
                {
                    // 정리 실패가 테스트 결과를 바꾸면 안 된다.
                }
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // 거부되어야 하는 경우
        // ─────────────────────────────────────────────────────────────────────

        [Fact]
        public void 원점_복귀_전에는_조작할_수_없다()
        {
            // 밸브 위치를 신뢰할 수 없는 상태에서 개도율을 지정하는 것은 의미가 없다.
            EsamRuntime runtime = Create();

            Assert.NotNull(runtime.DescribeManualDenial());

            string reason;

            Assert.False(runtime.TryCommandValvePercent("V-1", 50.0, out reason));
            Assert.NotNull(reason);
        }

        [Fact]
        public void 거부_사유가_비어_있지_않다()
        {
            // 이유 없이 눌리지 않는 버튼은 프로그램이 멈춘 것처럼 보이고,
            // 현장에서는 그때부터 화면을 믿지 않는다.
            EsamRuntime runtime = Create();

            string reason = runtime.DescribeManualDenial();

            Assert.False(string.IsNullOrWhiteSpace(reason));
        }

        [Fact]
        public void 개도율_범위를_벗어나면_거부한다()
        {
            EsamRuntime runtime = Ready();

            string low;
            string high;

            Assert.False(runtime.TryCommandValvePercent("V-1", -1.0, out low));
            Assert.False(runtime.TryCommandValvePercent("V-1", 101.0, out high));

            Assert.Contains("0~100", low);
            Assert.Contains("0~100", high);
        }

        [Fact]
        public void 회전수_범위를_벗어나면_거부한다()
        {
            EsamRuntime runtime = Ready();

            string reason;

            Assert.False(runtime.TryCommandFanRpm("F-1", 99999.0, out reason));
            Assert.NotNull(reason);
        }

        [Fact]
        public void 대상을_지정하지_않으면_거부한다()
        {
            EsamRuntime runtime = Ready();

            string valve;
            string fan;

            Assert.False(runtime.TryCommandValvePercent(null, 50.0, out valve));
            Assert.False(runtime.TryCommandFanRpm(string.Empty, 1000.0, out fan));

            Assert.NotNull(valve);
            Assert.NotNull(fan);
        }

        [Fact]
        public void 자동_운전_중에는_조작할_수_없다()
        {
            // 제어 루프가 다음 주기에 같은 액추에이터에 다른 값을 쓴다.
            // 사람이 넣은 값은 눈 깜짝할 사이 사라지고, 조작이 먹은 것처럼
            // 보이다가 원인 없이 되돌아간다.
            EsamRuntime runtime = Ready();

            // 상태머신을 직접 전이시킨다. RequestAuto 는 구성 경고와 팬 사양까지
            // 보므로 여기서 쓰면 검증 대상이 자동 진입 조건으로 바뀐다.
            // 여기서 보려는 것은 자동 운전 중의 수동 관문이다.
            Assert.True(runtime.Engine.StateMachine.Fire(SystemTrigger.AutoRequested));
            Assert.True(runtime.Engine.StateMachine.IsAutoEnabled);

            string reason;

            Assert.False(runtime.TryCommandValvePercent("V-1", 50.0, out reason));
            Assert.Contains("자동 운전", reason);
        }

        [Fact]
        public void 정지하면_다시_조작할_수_있다()
        {
            EsamRuntime runtime = Ready();

            runtime.Engine.StateMachine.Fire(SystemTrigger.AutoRequested);
            runtime.Engine.StopAuto();

            Assert.Null(runtime.DescribeManualDenial());
        }

        // ─────────────────────────────────────────────────────────────────────
        // 허용되는 경우
        // ─────────────────────────────────────────────────────────────────────

        [Fact]
        public void 준비_단계에서는_밸브를_지령할_수_있다()
        {
            EsamRuntime runtime = Ready();

            string reason;

            Assert.True(runtime.TryCommandValvePercent("V-1", 50.0, out reason), reason);
            Assert.Null(reason);
        }

        [Fact]
        public void 최소_회전수_미만은_정지로_바꾼다()
        {
            // 드라이버가 받지 못하는 값을 그대로 보내면 지령이 조용히 무시되고
            // 화면에는 보낸 것으로 남는다.
            EsamRuntime runtime = Ready();

            string reason;

            Assert.True(runtime.TryCommandFanRpm("F-1", 10.0, out reason), reason);
        }

        [Fact]
        public void 원점_복귀를_개별로_지령할_수_있다()
        {
            EsamRuntime runtime = Ready();

            string reason;

            Assert.True(runtime.TryHomeValve("V-1", out reason), reason);
        }

        [Fact]
        public void 파킹은_전_통로에_지령을_보낸다()
        {
            // 밸브를 열어 둔 채 화면을 떠나는 경로를 막는다.
            EsamRuntime runtime = Ready();

            int parked = runtime.ParkManual("테스트");

            Assert.Equal(runtime.Control.Chains.Count * 2, parked);
        }

        // ─────────────────────────────────────────────────────────────────────
        // 영점
        // ─────────────────────────────────────────────────────────────────────

        [Fact]
        public void 영점을_적용하면_런타임에_반영된다()
        {
            EsamRuntime runtime = Create();

            Dictionary<string, double> offsets =
                new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

            offsets["S1-1"] = 0.35;

            IList<string> unknown;
            int applied = runtime.ApplyZeroOffsets(offsets, out unknown);

            Assert.Equal(1, applied);
            Assert.Empty(unknown);
            Assert.Equal(0.35, FindOffset(runtime, "S1-1"), 3);
        }

        [Fact]
        public void 어느_포트에도_없는_센서는_따로_돌려준다()
        {
            // 조용히 넘어가면 영점을 잡았는데 값이 그대로인 상태가 되고,
            // 그 원인을 화면에서 찾을 수 없다.
            EsamRuntime runtime = Create();

            Dictionary<string, double> offsets =
                new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

            offsets["S9-9"] = 1.0;

            IList<string> unknown;
            int applied = runtime.ApplyZeroOffsets(offsets, out unknown);

            Assert.Equal(0, applied);
            Assert.Contains("S9-9", unknown);
        }

        [Fact]
        public void 빈_목록은_아무_일도_하지_않는다()
        {
            EsamRuntime runtime = Create();

            IList<string> unknown;

            Assert.Equal(0, runtime.ApplyZeroOffsets(null, out unknown));
            Assert.Empty(unknown);
        }

        // ─────────────────────────────────────────────────────────────────────
        // 도우미
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>시뮬레이션 런타임을 조립한다(기동하지 않는다).</summary>
        /// <returns>런타임.</returns>
        private EsamRuntime Create()
        {
            ConfigLoadResultHolder holder = LoadConfig();

            RuntimeOptions options = new RuntimeOptions();
            options.Transport = TransportMode.Simulation;
            options.AlarmRulesPath = "config/alarms.json";
            options.RecipePath = "config/recipe.json";

            EsamRuntime runtime = EsamRuntime.Create(holder.Map, holder.Control, options, null);
            _runtimes.Add(runtime);

            return runtime;
        }

        /// <summary>원점 복귀가 끝난 상태의 런타임을 만든다.</summary>
        /// <returns>런타임.</returns>
        /// <remarks>
        /// 워커를 띄우지 않고 상태머신만 전이시킨다. 실제 원점 복귀는 밸브 응답이
        /// 필요하고, 여기서 검증하려는 것은 <b>관문</b>이지 원점 복귀가 아니다.
        /// </remarks>
        private EsamRuntime Ready()
        {
            EsamRuntime runtime = Create();

            runtime.Engine.StateMachine.Fire(SystemTrigger.Start);
            runtime.Engine.StateMachine.Fire(SystemTrigger.InitCompleted);
            runtime.Engine.StateMachine.Fire(SystemTrigger.HomingCompleted);

            Assert.Equal(SystemPhase.Ready, runtime.Engine.StateMachine.Phase);

            return runtime;
        }

        /// <summary>런타임 구성에서 오프셋을 읽는다.</summary>
        /// <param name="runtime">런타임.</param>
        /// <param name="deviceId">디바이스 ID.</param>
        /// <returns>오프셋.</returns>
        private static double FindOffset(EsamRuntime runtime, string deviceId)
        {
            foreach (Esam.Communication.Configuration.DeviceInstanceDefinition device
                     in runtime.Map.Devices)
            {
                if (string.Equals(device.Id, deviceId, StringComparison.OrdinalIgnoreCase))
                {
                    return device.Offset;
                }
            }

            throw new InvalidOperationException("디바이스가 없습니다: " + deviceId);
        }

        /// <summary>배포 설정을 읽는다.</summary>
        /// <returns>구성 묶음.</returns>
        private static ConfigLoadResultHolder LoadConfig()
        {
            Esam.Communication.Configuration.ConfigLoadResult map =
                Esam.Communication.Configuration.CommunicationConfigLoader.LoadFromFile(
                    "config/device-map.json");

            Assert.True(map.IsSuccess, "통신 구성 오류:\n" + string.Join("\n", map.Errors));

            Esam.Communication.Configuration.ControlLoadResult control =
                Esam.Communication.Configuration.ControlConfigLoader.LoadFromFile("config/control.json");

            Assert.True(control.IsSuccess, "제어 설정 오류:\n" + string.Join("\n", control.Errors));

            ConfigLoadResultHolder holder = new ConfigLoadResultHolder();
            holder.Map = map.Map;
            holder.Control = control.Config;

            return holder;
        }

        /// <summary>구성 묶음.</summary>
        private sealed class ConfigLoadResultHolder
        {
            /// <summary>통신 구성.</summary>
            public Esam.Communication.Configuration.DeviceMap Map { get; set; }

            /// <summary>제어 설정.</summary>
            public Esam.Domain.Configuration.ControlConfig Control { get; set; }
        }
    }
}
