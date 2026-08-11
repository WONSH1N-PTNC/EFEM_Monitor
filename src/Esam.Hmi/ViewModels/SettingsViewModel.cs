using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows.Input;
using Esam.Communication.Configuration;
using Esam.Communication.Polling;
using Esam.Domain.Configuration;
using Esam.Domain.Control;
using Esam.Hmi.Infrastructure;
using Esam.Services;

namespace Esam.Hmi.ViewModels
{
    /// <summary>
    /// 통신 포트·전송 방식·통로 활성화 등 앱 전역 설정을 다룬다.
    /// </summary>
    /// <remarks>
    /// <para><b>항목마다 적용 위험도가 다르다.</b> 이 화면의 설계는 그 구분에서 나온다.</para>
    /// <list type="table">
    ///   <item><description><b>재조립</b> — 전송 방식·COM 포트·보레이트.
    ///     런타임을 통째로 다시 세워야 하므로 <b>운전 중에는 할 수 없다.</b></description></item>
    ///   <item><description><b>즉시</b> — 통로 활성화. 제어 설정을 바로 고친다.
    ///     다만 비활성화하면 그 통로의 밸브가 현재 위치에 남으므로
    ///     역시 운전 중에는 막는다.</description></item>
    ///   <item><description><b>화면 전용</b> — 게이지 눈금 배율. 언제든 바꿔도 된다.</description></item>
    /// </list>
    /// <para><b>재조립은 반드시 파킹을 거친다.</b> 그냥 런타임을 버리면
    /// 밸브는 열린 채, 팬은 도는 채로 통신만 끊긴다.
    /// 새 런타임이 붙기 전까지 아무도 보지 않는 상태가 된다.</para>
    /// </remarks>
    public sealed class SettingsViewModel : ObservableObject
    {
        private readonly HmiHost _host;
        private readonly Action _afterRebuild;

        private bool _useSimulation;
        private string _gaugeSpanText;
        private string _statusText;
        private bool _hasError;

        /// <summary>설정 화면을 생성한다.</summary>
        /// <param name="host">런타임 호스트. null 이면 디자인타임으로 동작한다.</param>
        /// <param name="afterRebuild">재조립 후 화면을 다시 붙이기 위한 콜백.</param>
        public SettingsViewModel(HmiHost host, Action afterRebuild)
        {
            _host = host;
            _afterRebuild = afterRebuild;

            Ports = new ObservableCollection<PortSettingRowViewModel>();
            Chains = new ObservableCollection<ChainToggleViewModel>();
            Errors = new ObservableCollection<string>();

            ApplyCommand = new RelayCommand(OnApply, CanApply);
            ReloadCommand = new RelayCommand(OnReload);

            if (_host != null && _host.WriteAccess != null)
            {
                _host.WriteAccess.WriteAccessChanged += OnWriteAccessChanged;
            }

            _gaugeSpanText = "1.5";
            Load();
        }

        /// <summary>포트별 설정 행.</summary>
        public ObservableCollection<PortSettingRowViewModel> Ports { get; private set; }

        /// <summary>통로 활성화 토글.</summary>
        public ObservableCollection<ChainToggleViewModel> Chains { get; private set; }

        /// <summary>검증 실패 사유.</summary>
        public ObservableCollection<string> Errors { get; private set; }

        /// <summary>적용 명령.</summary>
        public ICommand ApplyCommand { get; private set; }

        /// <summary>현재 설정을 다시 읽는 명령.</summary>
        public ICommand ReloadCommand { get; private set; }

        /// <summary>시뮬레이션 전송을 쓸지 여부.</summary>
        public bool UseSimulation
        {
            get { return _useSimulation; }
            set { Set(ref _useSimulation, value); }
        }

        /// <summary>게이지 눈금 배율(대역의 몇 배까지 표시할지).</summary>
        public string GaugeSpanText
        {
            get { return _gaugeSpanText; }
            set { Set(ref _gaugeSpanText, value); }
        }

        /// <summary>마지막 작업 결과 문구.</summary>
        public string StatusText
        {
            get { return _statusText; }
            private set { Set(ref _statusText, value); }
        }

        /// <summary>마지막 작업이 실패했는지 여부.</summary>
        public bool HasError
        {
            get { return _hasError; }
            private set { Set(ref _hasError, value); }
        }

        /// <summary>설정 폴더.</summary>
        public string ConfigFolder
        {
            get { return _host == null ? "config" : (_host.ConfigFolder ?? "config"); }
        }

        /// <summary>쓰기 작업이 잠겨 있는지 여부.</summary>
        public bool IsLocked
        {
            get
            {
                return _host == null || _host.WriteAccess == null
                       || !_host.WriteAccess.IsWriteAllowed;
            }
        }

        /// <summary>적용을 막는 사유. 없으면 null.</summary>
        /// <remarks>
        /// 버튼이 회색인 이유를 화면에 남긴다. 이유 없이 눌리지 않는 버튼은
        /// 작업자가 프로그램이 멈췄다고 판단하게 만든다.
        /// </remarks>
        public string BlockReason
        {
            get
            {
                if (IsLocked)
                {
                    return _host == null || _host.WriteAccess == null
                        ? "런타임이 없어 설정을 적용할 수 없습니다."
                        : _host.WriteAccess.DescribeDenial();
                }

                if (IsRunning)
                {
                    return "운전 중에는 통신 설정을 바꿀 수 없습니다. "
                           + "적용하려면 자동 운전을 먼저 정지하십시오.";
                }

                return null;
            }
        }

        /// <summary>자동 운전 중인지 여부.</summary>
        public bool IsRunning
        {
            get
            {
                EsamRuntime runtime = _host == null ? null : _host.Runtime;

                return runtime != null
                       && runtime.Engine != null
                       && runtime.Engine.StateMachine.Phase == SystemPhase.AutoControl;
            }
        }

        /// <summary>현재 구성을 화면에 채운다.</summary>
        public void Load()
        {
            Ports.Clear();
            Chains.Clear();
            Errors.Clear();

            EsamRuntime runtime = _host == null ? null : _host.Runtime;

            if (runtime == null)
            {
                StatusText = "런타임이 없습니다.";
                HasError = true;
                RaiseState();
                return;
            }

            _useSimulation = IsSimulation(runtime);

            foreach (PortDefinition port in runtime.Map.Ports)
            {
                Ports.Add(new PortSettingRowViewModel(port));
            }

            foreach (ChainDefinition chain in runtime.Control.Chains)
            {
                Chains.Add(new ChainToggleViewModel(chain));
            }

            StatusText = null;
            HasError = false;

            Raise("UseSimulation");
            RaiseState();
        }

        /// <summary>현재 전송 계층이 시뮬레이션인지 판정한다.</summary>
        /// <param name="runtime">런타임.</param>
        /// <returns>시뮬레이션이면 true.</returns>
        /// <remarks>
        /// 런타임이 전송 방식을 속성으로 들고 있지 않으므로 실제 객체 형으로 판정한다.
        /// 옵션을 따로 기억해 두면 재조립에 실패했을 때 화면과 실제가 어긋난다.
        /// </remarks>
        private static bool IsSimulation(EsamRuntime runtime)
        {
            foreach (ModbusPortWorker worker in runtime.Workers)
            {
                return runtime.FindTransport(worker.PortId)
                    is Esam.Communication.Simulation.SimulatedModbusTransport;
            }

            return true;
        }

        /// <summary>상태 의존 속성을 일괄 통지한다.</summary>
        private void RaiseState()
        {
            Raise("IsLocked");
            Raise("IsRunning");
            Raise("BlockReason");
            Raise("ConfigFolder");

            CommandManager.InvalidateRequerySuggested();
        }

        /// <summary>쓰기 권한이 바뀌면 상태를 갱신한다.</summary>
        /// <param name="sender">이벤트 발신자.</param>
        /// <param name="e">이벤트 인자.</param>
        private void OnWriteAccessChanged(object sender, EventArgs e)
        {
            RaiseState();
        }

        /// <summary>적용이 가능한지 판정한다.</summary>
        /// <param name="parameter">사용하지 않는다.</param>
        /// <returns>가능하면 true.</returns>
        private bool CanApply(object parameter)
        {
            return !IsLocked && !IsRunning && Ports.Count > 0;
        }

        /// <summary>현재 구성을 다시 읽는다.</summary>
        /// <param name="parameter">사용하지 않는다.</param>
        private void OnReload(object parameter)
        {
            Load();
            StatusText = "현재 구성을 다시 읽었습니다.";
        }

        /// <summary>설정을 검증하고 적용한다.</summary>
        /// <param name="parameter">사용하지 않는다.</param>
        private void OnApply(object parameter)
        {
            Errors.Clear();
            HasError = false;

            EsamRuntime runtime = _host == null ? null : _host.Runtime;

            if (runtime == null)
            {
                StatusText = "런타임이 없어 적용할 수 없습니다.";
                HasError = true;
                return;
            }

            // ── 화면 전용 값은 검증만 하고 넘어간다 ─────────────────────────────
            double gaugeSpan;

            if (!double.TryParse(
                    _gaugeSpanText, NumberStyles.Float, CultureInfo.InvariantCulture, out gaugeSpan)
                || gaugeSpan < 1.0 || gaugeSpan > 10.0)
            {
                Errors.Add("게이지 눈금 배율은 1.0 ~ 10.0 사이의 숫자여야 합니다.");
            }

            // ── 통로 활성화 ────────────────────────────────────────────────────
            bool anyChain = false;

            foreach (ChainToggleViewModel chain in Chains)
            {
                if (chain.IsEnabled)
                {
                    anyChain = true;
                }
            }

            if (!anyChain)
            {
                // 전 통로를 끄면 제어할 대상이 없어진다.
                // 자동 운전에 들어가도 아무 일도 일어나지 않고, 그 이유를 알기 어렵다.
                Errors.Add("통로를 하나도 활성화하지 않으면 제어할 대상이 없습니다.");
            }

            // ── 통신 설정 ──────────────────────────────────────────────────────
            DeviceMap map = runtime.Map;

            for (int i = 0; i < Ports.Count && i < map.Ports.Count; i++)
            {
                string error;

                if (!Ports[i].Validate(out error))
                {
                    Errors.Add(error);
                }
            }

            if (Errors.Count > 0)
            {
                StatusText = "입력값을 확인하십시오.";
                HasError = true;
                return;
            }

            // ── 통신 구성을 파일에 반영하고 검증한다 ────────────────────────────
            for (int i = 0; i < Ports.Count && i < map.Ports.Count; i++)
            {
                Ports[i].ApplyTo(map.Ports[i]);
            }

            string json = CommunicationConfigLoader.ToJson(map);
            ConfigLoadResult verified = CommunicationConfigLoader.LoadFromJson(json);

            if (!verified.IsSuccess)
            {
                foreach (string error in verified.Errors)
                {
                    Errors.Add(error);
                }

                StatusText = "통신 구성 검증에 실패해 저장하지 않았습니다.";
                HasError = true;

                // 화면 값을 되돌린다. 메모리의 map 을 이미 고쳤으므로 그대로 두면
                // 다음 폴링이 잘못된 설정으로 돌아간다.
                Load();
                return;
            }

            string path = Path.Combine(ConfigFolder, "device-map.json");

            try
            {
                File.WriteAllText(path, json);
            }
            catch (IOException ex)
            {
                Errors.Add(ex.Message);
                StatusText = "통신 구성 파일을 쓰지 못했습니다.";
                HasError = true;
                return;
            }
            catch (UnauthorizedAccessException ex)
            {
                Errors.Add(ex.Message);
                StatusText = "파일 접근이 거부되었습니다.";
                HasError = true;
                return;
            }

            // ── 재조립 ────────────────────────────────────────────────────────
            //
            // ★ 반드시 파킹을 거친다. 그냥 런타임을 버리면 밸브는 열린 채,
            // 팬은 도는 채로 통신만 끊긴다. 새 런타임이 붙기 전까지
            // 아무도 보지 않는 상태가 된다.
            runtime.Stop();

            TransportMode transport = _useSimulation
                ? TransportMode.Simulation
                : TransportMode.Serial;

            if (!_host.Start(ConfigFolder, transport))
            {
                Errors.Add(_host.StartupError ?? "사유 미상");
                StatusText = "재조립에 실패했습니다.";
                HasError = true;

                if (_afterRebuild != null)
                {
                    _afterRebuild();
                }

                return;
            }

            // 통로 활성화는 새 런타임의 제어 설정에 반영한다.
            ApplyChains(_host.Runtime);

            if (_afterRebuild != null)
            {
                _afterRebuild();
            }

            Load();

            StatusText = _useSimulation
                ? "적용했습니다. 시뮬레이션으로 재기동했습니다."
                : "적용했습니다. 실제 통신으로 재기동했습니다.";
        }

        /// <summary>통로 활성화 상태를 제어 설정에 반영한다.</summary>
        /// <param name="runtime">대상 런타임.</param>
        private void ApplyChains(EsamRuntime runtime)
        {
            if (runtime == null || runtime.Control.Chains == null)
            {
                return;
            }

            foreach (ChainToggleViewModel toggle in Chains)
            {
                foreach (ChainDefinition chain in runtime.Control.Chains)
                {
                    if (chain.Id == toggle.ChainId)
                    {
                        chain.Enabled = toggle.IsEnabled;
                        break;
                    }
                }
            }
        }
    }

    /// <summary>포트 1개의 설정 행.</summary>
    public sealed class PortSettingRowViewModel : ObservableObject
    {
        private string _portName;
        private string _baudRate;

        /// <summary>포트 정의로 행을 만든다.</summary>
        /// <param name="port">포트 정의.</param>
        public PortSettingRowViewModel(PortDefinition port)
        {
            if (port == null)
            {
                throw new ArgumentNullException("port");
            }

            PortId = port.Serial.PortId;
            _portName = port.Serial.PortName;
            _baudRate = port.Serial.BaudRate.ToString(CultureInfo.InvariantCulture);

            FastMs = port.Polling.FastMs;
        }

        /// <summary>포트 식별자(CH1 등).</summary>
        public string PortId { get; private set; }

        /// <summary>Fast 티어 주기 [ms]. 표시 전용이다.</summary>
        public int FastMs { get; private set; }

        /// <summary>시리얼 포트 이름.</summary>
        public string PortName
        {
            get { return _portName; }
            set { Set(ref _portName, value); }
        }

        /// <summary>통신 속도.</summary>
        public string BaudRate
        {
            get { return _baudRate; }
            set { Set(ref _baudRate, value); }
        }

        /// <summary>입력값을 검증한다.</summary>
        /// <param name="error">실패 사유(출력).</param>
        /// <returns>유효하면 true.</returns>
        public bool Validate(out string error)
        {
            if (string.IsNullOrWhiteSpace(_portName))
            {
                error = PortId + ": 포트 이름이 비어 있습니다.";
                return false;
            }

            int baud;

            if (!int.TryParse(_baudRate, NumberStyles.Integer, CultureInfo.InvariantCulture, out baud)
                || baud <= 0)
            {
                error = PortId + ": 통신 속도가 올바르지 않습니다.";
                return false;
            }

            error = null;
            return true;
        }

        /// <summary>입력값을 포트 정의에 반영한다.</summary>
        /// <param name="port">대상 포트 정의.</param>
        public void ApplyTo(PortDefinition port)
        {
            if (port == null)
            {
                return;
            }

            port.Serial.PortName = _portName.Trim();
            port.Serial.BaudRate = int.Parse(_baudRate, CultureInfo.InvariantCulture);
        }
    }

    /// <summary>통로 1개의 활성화 토글.</summary>
    public sealed class ChainToggleViewModel : ObservableObject
    {
        private bool _isEnabled;

        /// <summary>체인 정의로 토글을 만든다.</summary>
        /// <param name="chain">체인 정의.</param>
        public ChainToggleViewModel(ChainDefinition chain)
        {
            if (chain == null)
            {
                throw new ArgumentNullException("chain");
            }

            ChainId = chain.Id;
            Label = string.Format(
                CultureInfo.InvariantCulture,
                "통로 {0}  ({1} · {2})",
                chain.Id, chain.ValveId, chain.FanId);

            _isEnabled = chain.Enabled;
        }

        /// <summary>체인 번호.</summary>
        public int ChainId { get; private set; }

        /// <summary>표시 문구.</summary>
        public string Label { get; private set; }

        /// <summary>활성화 여부.</summary>
        public bool IsEnabled
        {
            get { return _isEnabled; }
            set { Set(ref _isEnabled, value); }
        }
    }
}
