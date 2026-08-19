using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows.Input;
using System.Windows.Threading;
using Esam.Communication.Configuration;
using Esam.Domain.Configuration;
using Esam.Domain.Models;
using Esam.Hmi.Infrastructure;
using Esam.Services;

namespace Esam.Hmi.ViewModels
{
    /// <summary>
    /// Maintenance 화면 — 영점 교정 · 수동 조작 · 원점 복귀.
    /// </summary>
    /// <remarks>
    /// <para><b>지금까지의 쓰기는 전부 설정 파일이었다.</b> 여기는 밸브와 팬을
    /// 직접 움직인다. 성질이 다르므로 관문도 다르다.</para>
    /// <para>세 겹으로 막는다. 쓰기 잠금(레시피·알람 화면과 동일), 단계 판정
    /// (자동 운전 중·원점 복귀 전에는 불가), 인터록 판정(발동 중에는 불가).
    /// 판정은 <see cref="EsamRuntime.DescribeManualDenial"/> 한 곳에 있다.</para>
    /// <para><b>화면을 떠나면 파킹한다.</b> 밸브를 60 % 로 열어 두고 다른 화면으로
    /// 넘어가면 그 상태가 그대로 남는데, 다른 화면에는 그 사실이 보이지 않는다.
    /// 수동 조작이 살아 있는 동안에는 배너에도 표시한다.</para>
    /// </remarks>
    public sealed class MaintenanceViewModel : ObservableObject
    {
        /// <summary>화면 갱신 주기 [ms].</summary>
        private const int RefreshIntervalMs = 250;

        /// <summary>영점 취득에 쓸 표본 수.</summary>
        /// <remarks>
        /// 폴링 주기 218 ms 기준 20회면 약 4.4초다. 차압계 시정수(1.0~1.5초)의
        /// 3배 이상이라 과도 상태가 평균에 남지 않는다.
        /// </remarks>
        private const int ZeroSampleCount = 20;

        private readonly HmiHost _host;
        private readonly Dictionary<string, List<double>> _samples =
            new Dictionary<string, List<double>>(StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<string, double> _previousOffsets =
            new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

        private DispatcherTimer _timer;
        private bool _isSampling;
        private int _samplesTaken;
        private string _statusText;
        private bool _hasError;
        private bool _manualActive;
        private string _selectedValveId;
        private string _selectedFanId;
        private string _valvePercent = "0";
        private string _fanRpm = "0";

        /// <summary>디자인타임용으로 생성한다.</summary>
        public MaintenanceViewModel()
            : this(null)
        {
        }

        /// <summary>정비 화면을 생성한다.</summary>
        /// <param name="host">런타임 호스트. null 이면 디자인타임으로 동작한다.</param>
        public MaintenanceViewModel(HmiHost host)
        {
            _host = host;

            Sensors = new ObservableCollection<ZeroCalibrationRowViewModel>();
            Valves = new ObservableCollection<string>();
            Fans = new ObservableCollection<string>();
            Errors = new ObservableCollection<string>();

            StartSamplingCommand = new RelayCommand(OnStartSampling, CanCalibrate);
            CancelSamplingCommand = new RelayCommand(OnCancelSampling, o => _isSampling);
            SaveOffsetsCommand = new RelayCommand(OnSaveOffsets, CanSaveOffsets);
            RevertOffsetsCommand = new RelayCommand(OnRevertOffsets, o => _previousOffsets.Count > 0);

            SetValveCommand = new RelayCommand(OnSetValve, CanOperate);
            SetFanCommand = new RelayCommand(OnSetFan, CanOperate);
            HomeValveCommand = new RelayCommand(OnHomeValve, CanOperate);
            ParkCommand = new RelayCommand(OnPark, o => _manualActive);

            if (_host != null && _host.WriteAccess != null)
            {
                _host.WriteAccess.WriteAccessChanged += OnWriteAccessChanged;
            }

            Rebuild();
        }

        /// <summary>센서별 영점 교정 행.</summary>
        public ObservableCollection<ZeroCalibrationRowViewModel> Sensors { get; private set; }

        /// <summary>조작 가능한 밸브 ID.</summary>
        public ObservableCollection<string> Valves { get; private set; }

        /// <summary>조작 가능한 팬 ID.</summary>
        public ObservableCollection<string> Fans { get; private set; }

        /// <summary>실패 사유 목록.</summary>
        public ObservableCollection<string> Errors { get; private set; }

        /// <summary>영점 표본 수집 시작 명령.</summary>
        public ICommand StartSamplingCommand { get; private set; }

        /// <summary>영점 표본 수집 취소 명령.</summary>
        public ICommand CancelSamplingCommand { get; private set; }

        /// <summary>영점 저장 명령.</summary>
        public ICommand SaveOffsetsCommand { get; private set; }

        /// <summary>영점 되돌리기 명령.</summary>
        public ICommand RevertOffsetsCommand { get; private set; }

        /// <summary>밸브 개도 지령 명령.</summary>
        public ICommand SetValveCommand { get; private set; }

        /// <summary>팬 회전수 지령 명령.</summary>
        public ICommand SetFanCommand { get; private set; }

        /// <summary>밸브 원점 복귀 명령.</summary>
        public ICommand HomeValveCommand { get; private set; }

        /// <summary>수동 조작 정리 명령.</summary>
        public ICommand ParkCommand { get; private set; }

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

        /// <summary>표본을 모으는 중인지 여부.</summary>
        public bool IsSampling
        {
            get { return _isSampling; }
            private set { Set(ref _isSampling, value); }
        }

        /// <summary>수집 진행 문구.</summary>
        public string SamplingProgress
        {
            get
            {
                return _isSampling
                    ? string.Format(
                        CultureInfo.InvariantCulture, "{0} / {1} 회", _samplesTaken, ZeroSampleCount)
                    : null;
            }
        }

        /// <summary>
        /// 수동 조작이 살아 있는지 여부. 배너가 이 값을 읽는다.
        /// </summary>
        public bool IsManualActive
        {
            get { return _manualActive; }
            private set
            {
                if (Set(ref _manualActive, value))
                {
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        /// <summary>선택된 밸브 ID.</summary>
        public string SelectedValveId
        {
            get { return _selectedValveId; }
            set { Set(ref _selectedValveId, value); }
        }

        /// <summary>선택된 팬 ID.</summary>
        public string SelectedFanId
        {
            get { return _selectedFanId; }
            set { Set(ref _selectedFanId, value); }
        }

        /// <summary>밸브 개도 입력 [%].</summary>
        /// <remarks>
        /// 문자열로 들고 있는다. <c>double</c> 바인딩은 입력 도중의 중간 상태에서
        /// 입력을 되돌린다. 파싱은 지령 시점에 한 번만 한다.
        /// </remarks>
        public string ValvePercent
        {
            get { return _valvePercent; }
            set { Set(ref _valvePercent, value); }
        }

        /// <summary>팬 회전수 입력 [RPM].</summary>
        public string FanRpm
        {
            get { return _fanRpm; }
            set { Set(ref _fanRpm, value); }
        }

        /// <summary>쓰기 작업이 잠겨 있는지 여부.</summary>
        public bool IsLocked
        {
            get { return _host == null || _host.WriteAccess == null || !_host.WriteAccess.IsWriteAllowed; }
        }

        /// <summary>조작을 막는 사유. 없으면 null.</summary>
        /// <remarks>
        /// 이유 없이 눌리지 않는 버튼은 프로그램이 멈춘 것처럼 보인다.
        /// 잠금이 먼저이고, 그다음이 런타임 판정이다.
        /// </remarks>
        public string BlockReason
        {
            get
            {
                if (IsLocked)
                {
                    return _host == null || _host.WriteAccess == null
                        ? "런타임이 없어 조작할 수 없습니다."
                        : _host.WriteAccess.DescribeDenial();
                }

                EsamRuntime runtime = Runtime;

                return runtime == null ? "런타임이 없습니다." : runtime.DescribeManualDenial();
            }
        }

        /// <summary>실시간 갱신을 시작한다.</summary>
        public void Start()
        {
            if (_timer != null)
            {
                return;
            }

            _timer = new DispatcherTimer(DispatcherPriority.Background);
            _timer.Interval = TimeSpan.FromMilliseconds(RefreshIntervalMs);
            _timer.Tick += OnTick;
            _timer.Start();
        }

        /// <summary>실시간 갱신을 중지한다.</summary>
        public void Stop()
        {
            if (_timer == null)
            {
                return;
            }

            _timer.Stop();
            _timer.Tick -= OnTick;
            _timer = null;
        }

        /// <summary>
        /// 화면을 떠날 때 호출한다. 수동으로 움직인 것을 되돌린다.
        /// </summary>
        /// <remarks>
        /// 되돌리지 않으면 밸브가 수동 위치로 남고, 다른 화면에는 그 사실이 없다.
        /// 표본 수집도 함께 끊는다 — 화면을 떠난 뒤 조용히 진행되다가
        /// 돌아왔을 때 끝나 있으면 무엇을 근거로 잡은 영점인지 알 수 없다.
        /// </remarks>
        public void Leave()
        {
            CancelSampling();

            if (!_manualActive)
            {
                return;
            }

            EsamRuntime runtime = Runtime;

            if (runtime != null)
            {
                int parked = runtime.ParkManual("정비 화면 이탈");

                StatusText = string.Format(
                    CultureInfo.InvariantCulture,
                    "화면을 떠나 수동 조작을 정리했습니다({0}건).", parked);
            }

            IsManualActive = false;
        }

        /// <summary>구성이 바뀌면 목록을 다시 만든다.</summary>
        public void Rebuild()
        {
            Sensors.Clear();
            Valves.Clear();
            Fans.Clear();
            Errors.Clear();

            EsamRuntime runtime = Runtime;

            if (runtime == null || runtime.Map == null)
            {
                StatusText = "런타임이 없어 정비 기능을 쓸 수 없습니다.";
                HasError = true;
                return;
            }

            foreach (DeviceInstanceDefinition device in runtime.Map.Devices)
            {
                if (device == null || string.IsNullOrEmpty(device.Id) || !device.Enabled)
                {
                    continue;
                }

                DeviceTypeDefinition type = runtime.Map.FindType(device.Type);
                string driver = type == null ? null : type.Driver;

                if (string.Equals(driver, PointKeys.DriverPressureSensor, StringComparison.Ordinal))
                {
                    Sensors.Add(new ZeroCalibrationRowViewModel(device.Id, device.Offset));
                }
                else if (string.Equals(driver, PointKeys.DriverThrottleValve, StringComparison.Ordinal))
                {
                    Valves.Add(device.Id);
                }
                else if (string.Equals(driver, PointKeys.DriverModbusFan, StringComparison.Ordinal))
                {
                    Fans.Add(device.Id);
                }
            }

            if (Valves.Count > 0 && SelectedValveId == null)
            {
                SelectedValveId = Valves[0];
            }

            if (Fans.Count > 0 && SelectedFanId == null)
            {
                SelectedFanId = Fans[0];
            }

            StatusText = string.Format(
                CultureInfo.InvariantCulture,
                "센서 {0}대 · 밸브 {1}대 · 팬 {2}대", Sensors.Count, Valves.Count, Fans.Count);

            HasError = false;
            RaiseGate();
        }

        /// <summary>현재 런타임.</summary>
        private EsamRuntime Runtime
        {
            get { return _host == null ? null : _host.Runtime; }
        }

        /// <summary>타이머 콜백.</summary>
        /// <param name="sender">이벤트 발신자.</param>
        /// <param name="e">이벤트 인자.</param>
        private void OnTick(object sender, EventArgs e)
        {
            EsamRuntime runtime = Runtime;

            if (runtime == null)
            {
                return;
            }

            SystemSnapshot snapshot = runtime.Store.Current;

            foreach (ZeroCalibrationRowViewModel row in Sensors)
            {
                row.Update(snapshot.FindPressure(row.DeviceId));
            }

            if (_isSampling)
            {
                CollectSample(snapshot);
            }

            RaiseGate();
        }

        /// <summary>표본 1회를 모은다.</summary>
        /// <param name="snapshot">현재 스냅샷.</param>
        /// <remarks>
        /// <b>품질이 나쁜 값은 세지 않는다.</b> 통신이 끊긴 센서의 낡은 값으로
        /// 영점을 잡으면 그 오차가 이후 모든 측정에 실린다.
        /// </remarks>
        private void CollectSample(SystemSnapshot snapshot)
        {
            bool complete = true;

            foreach (ZeroCalibrationRowViewModel row in Sensors)
            {
                PressureReading reading = snapshot.FindPressure(row.DeviceId);

                if (reading == null || reading.Quality != Quality.Good)
                {
                    complete = false;
                    continue;
                }

                List<double> values;

                if (!_samples.TryGetValue(row.DeviceId, out values))
                {
                    values = new List<double>();
                    _samples[row.DeviceId] = values;
                }

                if (values.Count < ZeroSampleCount)
                {
                    // 영점은 보정 전 값의 평균이다. 보정 후 값을 쓰면
                    // 이미 적용된 오프셋이 두 번 반영된다.
                    values.Add(reading.RawPa);
                }

                if (values.Count < ZeroSampleCount)
                {
                    complete = false;
                }
            }

            _samplesTaken++;
            Raise("SamplingProgress");

            if (complete)
            {
                FinishSampling();
                return;
            }

            // 표본이 채워지지 않는 센서가 있어도 무한정 기다리지 않는다.
            // 통신이 끊긴 센서 하나 때문에 화면이 영원히 수집 중으로 남으면
            // 사람이 그 사실을 알 수 없다.
            if (_samplesTaken >= ZeroSampleCount * 3)
            {
                FinishSampling();
            }
        }

        /// <summary>수집을 끝내고 제안값을 계산한다.</summary>
        private void FinishSampling()
        {
            IsSampling = false;
            Raise("SamplingProgress");

            int ready = 0;
            List<string> incomplete = new List<string>();

            foreach (ZeroCalibrationRowViewModel row in Sensors)
            {
                List<double> values;

                if (!_samples.TryGetValue(row.DeviceId, out values) || values.Count == 0)
                {
                    incomplete.Add(row.DeviceId);
                    row.ClearProposal();
                    continue;
                }

                double sum = 0.0;

                foreach (double value in values)
                {
                    sum += value;
                }

                row.SetProposal(sum / values.Count, values.Count);
                ready++;
            }

            if (incomplete.Count > 0)
            {
                Errors.Add("표본을 얻지 못한 센서: " + string.Join(", ", incomplete)
                           + " (통신 품질을 확인하십시오)");
            }

            StatusText = string.Format(
                CultureInfo.InvariantCulture,
                "센서 {0}대의 영점 제안값을 계산했습니다. 확인 후 저장하십시오.", ready);

            HasError = incomplete.Count > 0;
            CommandManager.InvalidateRequerySuggested();
        }

        /// <summary>표본 수집을 시작한다.</summary>
        /// <param name="parameter">사용하지 않는다.</param>
        private void OnStartSampling(object parameter)
        {
            Errors.Clear();
            HasError = false;

            _samples.Clear();
            _samplesTaken = 0;

            foreach (ZeroCalibrationRowViewModel row in Sensors)
            {
                row.ClearProposal();
            }

            IsSampling = true;
            Raise("SamplingProgress");

            StatusText = "대기압 상태인지 확인하십시오. 표본을 모으는 중입니다.";
        }

        /// <summary>표본 수집을 취소한다.</summary>
        /// <param name="parameter">사용하지 않는다.</param>
        private void OnCancelSampling(object parameter)
        {
            CancelSampling();
            StatusText = "영점 취득을 취소했습니다.";
        }

        /// <summary>수집 상태를 정리한다.</summary>
        private void CancelSampling()
        {
            if (!_isSampling)
            {
                return;
            }

            IsSampling = false;
            _samples.Clear();
            _samplesTaken = 0;

            foreach (ZeroCalibrationRowViewModel row in Sensors)
            {
                row.ClearProposal();
            }

            Raise("SamplingProgress");
        }

        /// <summary>제안된 영점을 적용하고 파일에 남긴다.</summary>
        /// <param name="parameter">사용하지 않는다.</param>
        private void OnSaveOffsets(object parameter)
        {
            Errors.Clear();
            HasError = false;

            EsamRuntime runtime = Runtime;

            if (runtime == null)
            {
                StatusText = "런타임이 없어 적용할 수 없습니다.";
                HasError = true;
                return;
            }

            Dictionary<string, double> offsets =
                new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

            _previousOffsets.Clear();

            foreach (ZeroCalibrationRowViewModel row in Sensors)
            {
                if (!row.HasProposal)
                {
                    continue;
                }

                // 되돌릴 수 있어야 한다. 대기압이 아닌 상태에서 잡은 영점은
                // 모든 측정에 오차를 싣는데, 그 사실은 한참 뒤에 드러난다.
                _previousOffsets[row.DeviceId] = row.CurrentOffset;
                offsets[row.DeviceId] = row.ProposedOffset;
            }

            if (offsets.Count == 0)
            {
                StatusText = "적용할 제안값이 없습니다. 먼저 영점을 취득하십시오.";
                HasError = true;
                return;
            }

            if (!ApplyAndSave(runtime, offsets))
            {
                _previousOffsets.Clear();
                return;
            }

            foreach (ZeroCalibrationRowViewModel row in Sensors)
            {
                row.Commit();
            }

            StatusText = string.Format(
                CultureInfo.InvariantCulture, "영점 {0}대를 적용하고 저장했습니다.", offsets.Count);

            CommandManager.InvalidateRequerySuggested();
        }

        /// <summary>직전 영점으로 되돌린다.</summary>
        /// <param name="parameter">사용하지 않는다.</param>
        private void OnRevertOffsets(object parameter)
        {
            Errors.Clear();
            HasError = false;

            EsamRuntime runtime = Runtime;

            if (runtime == null || _previousOffsets.Count == 0)
            {
                return;
            }

            Dictionary<string, double> restore =
                new Dictionary<string, double>(_previousOffsets, StringComparer.OrdinalIgnoreCase);

            if (!ApplyAndSave(runtime, restore))
            {
                return;
            }

            _previousOffsets.Clear();

            foreach (ZeroCalibrationRowViewModel row in Sensors)
            {
                double restored;

                if (restore.TryGetValue(row.DeviceId, out restored))
                {
                    row.Restore(restored);
                }
            }

            StatusText = "직전 영점으로 되돌렸습니다.";
            CommandManager.InvalidateRequerySuggested();
        }

        /// <summary>영점을 런타임과 파일에 반영한다.</summary>
        /// <param name="runtime">런타임.</param>
        /// <param name="offsets">디바이스 ID → 오프셋.</param>
        /// <returns>성공하면 true.</returns>
        private bool ApplyAndSave(EsamRuntime runtime, IDictionary<string, double> offsets)
        {
            IList<string> unknown;
            runtime.ApplyZeroOffsets(offsets, out unknown);

            if (unknown.Count > 0)
            {
                // 조용히 넘어가면 영점을 잡았는데 값이 그대로인 상태가 된다.
                Errors.Add("어느 포트에도 없는 센서: " + string.Join(", ", unknown));
                StatusText = "구성이 어긋났습니다. 저장하지 않았습니다.";
                HasError = true;
                return false;
            }

            string path = Path.Combine(
                _host.ConfigFolder ?? "config", "device-map.json");

            string original;

            try
            {
                original = File.ReadAllText(path);
            }
            catch (IOException ex)
            {
                Errors.Add(ex.Message);
                StatusText = "통신 구성 파일을 읽지 못했습니다.";
                HasError = true;
                return false;
            }
            catch (UnauthorizedAccessException ex)
            {
                Errors.Add(ex.Message);
                StatusText = "파일 접근이 거부되었습니다.";
                HasError = true;
                return false;
            }

            string updated;
            string editError;

            if (!DeviceMapDocumentEditor.TryApply(original, runtime.Map, out updated, out editError))
            {
                Errors.Add(editError);
                StatusText = "저장하지 않았습니다.";
                HasError = true;
                return false;
            }

            ConfigLoadResult verified = CommunicationConfigLoader.LoadFromJson(updated);

            if (!verified.IsSuccess)
            {
                foreach (string error in verified.Errors)
                {
                    Errors.Add(error);
                }

                StatusText = "검증에 실패해 저장하지 않았습니다.";
                HasError = true;
                return false;
            }

            try
            {
                File.WriteAllText(path, updated);
            }
            catch (IOException ex)
            {
                Errors.Add(ex.Message);
                StatusText = "파일을 쓰지 못했습니다.";
                HasError = true;
                return false;
            }
            catch (UnauthorizedAccessException ex)
            {
                Errors.Add(ex.Message);
                StatusText = "파일 접근이 거부되었습니다.";
                HasError = true;
                return false;
            }

            return true;
        }

        /// <summary>밸브 개도를 지령한다.</summary>
        /// <param name="parameter">사용하지 않는다.</param>
        private void OnSetValve(object parameter)
        {
            Errors.Clear();
            HasError = false;

            double percent;

            if (!TryParse(_valvePercent, out percent))
            {
                Errors.Add("개도율이 숫자가 아닙니다.");
                StatusText = "입력값을 확인하십시오.";
                HasError = true;
                return;
            }

            string reason;

            if (!Runtime.TryCommandValvePercent(SelectedValveId, percent, out reason))
            {
                Errors.Add(reason);
                StatusText = "지령하지 않았습니다.";
                HasError = true;
                return;
            }

            IsManualActive = true;

            StatusText = string.Format(
                CultureInfo.InvariantCulture,
                "{0} 을 {1:F1} % 로 지령했습니다.", SelectedValveId, percent);
        }

        /// <summary>팬 회전수를 지령한다.</summary>
        /// <param name="parameter">사용하지 않는다.</param>
        private void OnSetFan(object parameter)
        {
            Errors.Clear();
            HasError = false;

            double rpm;

            if (!TryParse(_fanRpm, out rpm))
            {
                Errors.Add("회전수가 숫자가 아닙니다.");
                StatusText = "입력값을 확인하십시오.";
                HasError = true;
                return;
            }

            string reason;

            if (!Runtime.TryCommandFanRpm(SelectedFanId, rpm, out reason))
            {
                Errors.Add(reason);
                StatusText = "지령하지 않았습니다.";
                HasError = true;
                return;
            }

            IsManualActive = true;

            StatusText = string.Format(
                CultureInfo.InvariantCulture,
                "{0} 을 {1:F0} RPM 으로 지령했습니다.", SelectedFanId, rpm);
        }

        /// <summary>밸브 원점 복귀를 지령한다.</summary>
        /// <param name="parameter">사용하지 않는다.</param>
        private void OnHomeValve(object parameter)
        {
            Errors.Clear();
            HasError = false;

            string reason;

            if (!Runtime.TryHomeValve(SelectedValveId, out reason))
            {
                Errors.Add(reason);
                StatusText = "지령하지 않았습니다.";
                HasError = true;
                return;
            }

            IsManualActive = true;
            StatusText = SelectedValveId + " 원점 복귀를 지령했습니다.";
        }

        /// <summary>수동 조작을 정리한다.</summary>
        /// <param name="parameter">사용하지 않는다.</param>
        private void OnPark(object parameter)
        {
            Errors.Clear();
            HasError = false;

            EsamRuntime runtime = Runtime;

            if (runtime == null)
            {
                return;
            }

            int parked = runtime.ParkManual("수동 조작 정리");

            IsManualActive = false;

            StatusText = string.Format(
                CultureInfo.InvariantCulture, "수동 조작을 정리했습니다({0}건).", parked);
        }

        /// <summary>영점 교정을 시작할 수 있는지 판정한다.</summary>
        /// <param name="parameter">사용하지 않는다.</param>
        /// <returns>가능하면 true.</returns>
        private bool CanCalibrate(object parameter)
        {
            return !IsLocked && !_isSampling && Sensors.Count > 0;
        }

        /// <summary>영점을 저장할 수 있는지 판정한다.</summary>
        /// <param name="parameter">사용하지 않는다.</param>
        /// <returns>가능하면 true.</returns>
        private bool CanSaveOffsets(object parameter)
        {
            if (IsLocked || _isSampling)
            {
                return false;
            }

            foreach (ZeroCalibrationRowViewModel row in Sensors)
            {
                if (row.HasProposal)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>액추에이터를 조작할 수 있는지 판정한다.</summary>
        /// <param name="parameter">사용하지 않는다.</param>
        /// <returns>가능하면 true.</returns>
        private bool CanOperate(object parameter)
        {
            return BlockReason == null;
        }

        /// <summary>쓰기 권한이 바뀌면 버튼 상태를 갱신한다.</summary>
        /// <param name="sender">이벤트 발신자.</param>
        /// <param name="e">이벤트 인자.</param>
        private void OnWriteAccessChanged(object sender, EventArgs e)
        {
            RaiseGate();
            CommandManager.InvalidateRequerySuggested();
        }

        /// <summary>관문 표시를 갱신한다.</summary>
        private void RaiseGate()
        {
            Raise("IsLocked");
            Raise("BlockReason");
        }

        /// <summary>불변 문화권으로 파싱한다.</summary>
        /// <param name="text">입력 문자열.</param>
        /// <param name="value">파싱 결과.</param>
        /// <returns>성공하면 true.</returns>
        private static bool TryParse(string text, out double value)
        {
            return double.TryParse(
                text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }
    }
}
