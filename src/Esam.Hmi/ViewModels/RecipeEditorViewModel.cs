using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows.Input;
using Esam.Communication.Configuration;
using Esam.Domain.Configuration;
using Esam.Hmi.Infrastructure;
using Esam.Services;

namespace Esam.Hmi.ViewModels
{
    /// <summary>
    /// 센서별 운전 설정값(<c>recipe.json</c>)을 편집한다.
    /// </summary>
    /// <remarks>
    /// <para>C1~C3 에서 만든 ECID 마스터를 <b>처음으로 사람이 고칠 수 있게 되는 지점</b>이다.
    /// 그전까지는 파일을 편집기로 직접 여는 수밖에 없었다.</para>
    /// <para><b>저장은 로드와 같은 검증을 거친다.</b> 화면에서 따로 검사하면 규칙이
    /// 두 곳에 생기고, 한쪽만 바뀌었을 때 화면은 통과시키는데 다음 기동에서
    /// 로드가 실패하는 상태가 된다. 그래서 JSON 으로 직렬화한 뒤
    /// <see cref="RecipeConfigLoader"/> 로 되읽어 검증한다.</para>
    /// <para><b>적용은 통째로 교체한다.</b> 항목을 제자리에서 고치면 제어 스레드가
    /// 반쯤 갱신된 목록을 읽는다. 통로 하나는 새 값, 다른 하나는 옛 값으로
    /// 제어되는 순간이 생긴다.</para>
    /// </remarks>
    public sealed class RecipeEditorViewModel : ObservableObject
    {
        private readonly HmiHost _host;
        private string _statusText;
        private bool _hasError;

        /// <summary>편집기를 생성한다.</summary>
        /// <param name="host">런타임 호스트. null 이면 디자인타임으로 동작한다.</param>
        public RecipeEditorViewModel(HmiHost host)
        {
            _host = host;

            Sensors = new ObservableCollection<SensorSettingRowViewModel>();
            Errors = new ObservableCollection<string>();

            SaveCommand = new RelayCommand(OnSave, CanWrite);
            ReloadCommand = new RelayCommand(OnReload);

            if (_host != null && _host.WriteAccess != null)
            {
                _host.WriteAccess.WriteAccessChanged += OnWriteAccessChanged;
            }

            Load();
        }

        /// <summary>센서별 설정 행.</summary>
        public ObservableCollection<SensorSettingRowViewModel> Sensors { get; private set; }

        /// <summary>검증 실패 사유 목록.</summary>
        public ObservableCollection<string> Errors { get; private set; }

        /// <summary>저장 명령.</summary>
        public ICommand SaveCommand { get; private set; }

        /// <summary>파일에서 다시 읽는 명령.</summary>
        public ICommand ReloadCommand { get; private set; }

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

        /// <summary>쓰기 작업이 잠겨 있는지 여부.</summary>
        public bool IsLocked
        {
            get { return _host == null || _host.WriteAccess == null || !_host.WriteAccess.IsWriteAllowed; }
        }

        /// <summary>쓰기 잠금 안내 문구. 잠겨 있지 않으면 null.</summary>
        public string LockNotice
        {
            get
            {
                return _host == null || _host.WriteAccess == null
                    ? "런타임이 없어 편집 결과를 적용할 수 없습니다."
                    : _host.WriteAccess.DescribeDenial();
            }
        }

        /// <summary>레시피 파일 경로.</summary>
        public string RecipePath
        {
            get
            {
                return _host == null
                    ? "config/recipe.json"
                    : Path.Combine(_host.ConfigFolder ?? "config", "recipe.json");
            }
        }

        /// <summary>현재 런타임의 레시피를 화면에 채운다.</summary>
        public void Load()
        {
            Sensors.Clear();
            Errors.Clear();

            RecipeDefinition recipe = CurrentRecipe();

            if (recipe == null)
            {
                StatusText = "레시피가 없습니다. 모드별 공통값으로 운전 중입니다.";
                HasError = true;
                return;
            }

            foreach (SensorSetting sensor in recipe.Sensors)
            {
                Sensors.Add(new SensorSettingRowViewModel(sensor));
            }

            StatusText = string.Format(
                CultureInfo.InvariantCulture, "센서 {0}대를 읽었습니다.", Sensors.Count);

            HasError = false;

            Raise("IsLocked");
            Raise("LockNotice");
        }

        /// <summary>현재 적용 중인 레시피를 가져온다. 없으면 null.</summary>
        /// <returns>레시피.</returns>
        private RecipeDefinition CurrentRecipe()
        {
            EsamRuntime runtime = _host == null ? null : _host.Runtime;

            return runtime == null || runtime.Control == null ? null : runtime.Control.Recipe;
        }

        /// <summary>쓰기가 허용되는지 판정한다.</summary>
        /// <param name="parameter">사용하지 않는다.</param>
        /// <returns>허용되면 true.</returns>
        private bool CanWrite(object parameter)
        {
            return !IsLocked && Sensors.Count > 0;
        }

        /// <summary>쓰기 권한이 바뀌면 버튼 상태를 갱신한다.</summary>
        /// <param name="sender">이벤트 발신자.</param>
        /// <param name="e">이벤트 인자.</param>
        private void OnWriteAccessChanged(object sender, EventArgs e)
        {
            Raise("IsLocked");
            Raise("LockNotice");

            System.Windows.Input.CommandManager.InvalidateRequerySuggested();
        }

        /// <summary>파일에서 다시 읽는다.</summary>
        /// <param name="parameter">사용하지 않는다.</param>
        /// <remarks>
        /// 편집 중이던 값을 버린다. 잘못 만진 값을 되돌릴 수단이 없으면
        /// 작업자가 화면을 아예 쓰지 않게 된다.
        /// </remarks>
        private void OnReload(object parameter)
        {
            EsamRuntime runtime = _host == null ? null : _host.Runtime;

            if (runtime == null)
            {
                Load();
                return;
            }

            RecipeLoadResult result = RecipeConfigLoader.LoadFromFile(RecipePath, runtime.Map);

            if (!result.IsSuccess)
            {
                Errors.Clear();

                foreach (string error in result.Errors)
                {
                    Errors.Add(error);
                }

                StatusText = "파일을 읽지 못했습니다.";
                HasError = true;
                return;
            }

            runtime.Control.Recipe = result.Recipe;
            Load();

            StatusText = "파일에서 다시 읽었습니다.";
        }

        /// <summary>검증 후 저장하고 런타임에 적용한다.</summary>
        /// <param name="parameter">사용하지 않는다.</param>
        private void OnSave(object parameter)
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

            RecipeDefinition edited = new RecipeDefinition();
            edited.Name = "운전 레시피";

            foreach (SensorSettingRowViewModel row in Sensors)
            {
                string parseError;
                SensorSetting sensor = row.ToSetting(out parseError);

                if (sensor == null)
                {
                    Errors.Add(parseError);
                    continue;
                }

                edited.Sensors.Add(sensor);
            }

            if (Errors.Count > 0)
            {
                StatusText = "입력값을 확인하십시오.";
                HasError = true;
                return;
            }

            // ★ 저장 전에 로드와 같은 경로로 검증한다.
            //
            // 화면에서 따로 검사하면 규칙이 두 곳에 생긴다. 한쪽만 바뀌면
            // 화면은 통과시키는데 다음 기동에서 로드가 실패해 장비가 뜨지 않는다.
            string json = RecipeConfigLoader.ToJson(edited);
            RecipeLoadResult verified = RecipeConfigLoader.LoadFromJson(json, runtime.Map);

            if (!verified.IsSuccess)
            {
                foreach (string error in verified.Errors)
                {
                    Errors.Add(error);
                }

                StatusText = "검증에 실패해 저장하지 않았습니다.";
                HasError = true;
                return;
            }

            foreach (string warning in verified.Warnings)
            {
                Errors.Add("경고: " + warning);
            }

            try
            {
                File.WriteAllText(RecipePath, json);
            }
            catch (IOException ex)
            {
                Errors.Add(ex.Message);
                StatusText = "파일을 쓰지 못했습니다.";
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

            // 통째로 교체한다. 항목을 제자리에서 고치면 제어 스레드가
            // 반쯤 갱신된 목록을 읽어, 통로마다 다른 시점의 설정으로 제어된다.
            runtime.Control.Recipe = verified.Recipe;

            StatusText = runtime.Engine.StateMachine.IsAutoEnabled
                ? "저장했습니다. 운전 중이므로 즉시 반영됩니다."
                : "저장했습니다.";
        }
    }

    /// <summary>센서 1대의 편집 행.</summary>
    /// <remarks>
    /// 값을 문자열로 들고 있는다. double 로 바인딩하면 입력 도중의
    /// "-" 나 "1." 같은 중간 상태에서 바인딩이 실패해 입력이 되돌아간다.
    /// 파싱은 저장 시점에 한 번만 한다.
    /// </remarks>
    public sealed class SensorSettingRowViewModel : ObservableObject
    {
        private string _setpoint;
        private string _lowLimit;
        private string _highLimit;

        /// <summary>설정값으로 행을 만든다.</summary>
        /// <param name="sensor">센서 설정.</param>
        public SensorSettingRowViewModel(SensorSetting sensor)
        {
            if (sensor == null)
            {
                throw new ArgumentNullException("sensor");
            }

            DeviceId = sensor.DeviceId;
            Group = DescribeGroup(sensor.DeviceId);

            _setpoint = Format(sensor.SetpointPa);
            _lowLimit = Format(sensor.LowLimitPa);
            _highLimit = Format(sensor.HighLimitPa);
        }

        /// <summary>디바이스 ID.</summary>
        public string DeviceId { get; private set; }

        /// <summary>센서 그룹 표기.</summary>
        public string Group { get; private set; }

        /// <summary>목표 압력 [Pa].</summary>
        public string Setpoint
        {
            get { return _setpoint; }
            set { Set(ref _setpoint, value); }
        }

        /// <summary>대역 하한 [Pa].</summary>
        public string LowLimit
        {
            get { return _lowLimit; }
            set { Set(ref _lowLimit, value); }
        }

        /// <summary>대역 상한 [Pa].</summary>
        public string HighLimit
        {
            get { return _highLimit; }
            set { Set(ref _highLimit, value); }
        }

        /// <summary>입력값을 설정으로 바꾼다.</summary>
        /// <param name="error">변환 실패 사유(출력). 성공 시 null.</param>
        /// <returns>설정. 실패하면 null.</returns>
        public SensorSetting ToSetting(out string error)
        {
            double setpoint;
            double low;
            double high;

            if (!TryParse(_setpoint, out setpoint))
            {
                error = DeviceId + ": 설정값이 숫자가 아닙니다.";
                return null;
            }

            if (!TryParse(_lowLimit, out low))
            {
                error = DeviceId + ": 하한이 숫자가 아닙니다.";
                return null;
            }

            if (!TryParse(_highLimit, out high))
            {
                error = DeviceId + ": 상한이 숫자가 아닙니다.";
                return null;
            }

            error = null;
            return new SensorSetting(DeviceId, setpoint, low, high);
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

        /// <summary>표시용 문자열로 만든다.</summary>
        /// <param name="value">값.</param>
        /// <returns>문자열.</returns>
        private static string Format(double value)
        {
            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }

        /// <summary>센서 ID 에서 그룹 표기를 만든다.</summary>
        /// <param name="deviceId">디바이스 ID.</param>
        /// <returns>그룹 표기.</returns>
        private static string DescribeGroup(string deviceId)
        {
            if (string.IsNullOrEmpty(deviceId))
            {
                return string.Empty;
            }

            if (deviceId.StartsWith("S1", StringComparison.OrdinalIgnoreCase))
            {
                return "Sensor 1 · EFEM 내부";
            }

            if (deviceId.StartsWith("S2", StringComparison.OrdinalIgnoreCase))
            {
                return "Sensor 2 · 배기 전면";
            }

            if (deviceId.StartsWith("S3", StringComparison.OrdinalIgnoreCase))
            {
                return "Sensor 3 · 배기 하부";
            }

            return string.Empty;
        }
    }
}
