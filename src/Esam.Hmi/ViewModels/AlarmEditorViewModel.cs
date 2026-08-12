using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows.Input;
using Esam.Communication.Configuration;
using Esam.Domain.Alarms;
using Esam.Domain.Configuration;
using Esam.Hmi.Infrastructure;
using Esam.Services;

namespace Esam.Hmi.ViewModels
{
    /// <summary>
    /// 알람 규칙(<c>alarms.json</c>)의 임계값·확정 시간·활성 여부를 편집한다.
    /// </summary>
    /// <remarks>
    /// <para>그전까지 알람 74종에는 <b>편집 수단이 없었다.</b> 현장에서 알람이 과민하면
    /// 파일을 편집기로 직접 열어야 했고, 그 경로는 저장 검증을 거치지 않는다.
    /// 쉼표 하나를 잘못 찍으면 다음 기동에서 장비가 뜨지 않는다.</para>
    /// <para><b>구조는 건드리지 않는다.</b> <c>code</c>·<c>source</c>·<c>condition</c> 은
    /// 읽기 전용이다. 화면에서 바꿀 수 있게 하면 경로 해석 검증을 통과하지 못하는
    /// 조합이 나오고, 그것은 알람 편집이 아니라 알람 재정의다.</para>
    /// <para><b>압력 26룰의 임계값 칸은 잠근다.</b> 그 값은 <c>recipe.json</c> 이 관리한다.
    /// 여기서 고칠 수 있게 하면 같은 숫자가 두 곳에 살고,
    /// 어느 쪽이 적용되는지 화면만 보고는 알 수 없다.</para>
    /// <para>저장은 원문의 <b>값 토큰만</b> 바꿔 쓴다(<see cref="AlarmDocumentEditor"/>).
    /// 재직렬화하면 비활성 규칙의 사유를 적은 주석 200여 줄이 사라진다.</para>
    /// </remarks>
    public sealed class AlarmEditorViewModel : ObservableObject
    {
        private readonly HmiHost _host;
        private string _statusText;
        private bool _hasError;
        private string _searchText;
        private bool _disabledOnly;
        private bool _criticalOnly;
        private bool _criticalDisableConfirmed;

        /// <summary>편집기를 생성한다.</summary>
        /// <param name="host">런타임 호스트. null 이면 디자인타임으로 동작한다.</param>
        public AlarmEditorViewModel(HmiHost host)
        {
            _host = host;

            All = new List<AlarmRuleRowViewModel>();
            Rows = new ObservableCollection<AlarmRuleRowViewModel>();
            Errors = new ObservableCollection<string>();
            Notices = new ObservableCollection<string>();

            SaveCommand = new RelayCommand(OnSave, CanWrite);
            ReloadCommand = new RelayCommand(OnReload);
            ClearFilterCommand = new RelayCommand(OnClearFilter);

            if (_host != null && _host.WriteAccess != null)
            {
                _host.WriteAccess.WriteAccessChanged += OnWriteAccessChanged;
            }

            Load();
        }

        /// <summary>필터를 거치지 않은 전체 행.</summary>
        public IList<AlarmRuleRowViewModel> All { get; private set; }

        /// <summary>화면에 보이는 행.</summary>
        public ObservableCollection<AlarmRuleRowViewModel> Rows { get; private set; }

        /// <summary>검증 실패 사유.</summary>
        public ObservableCollection<string> Errors { get; private set; }

        /// <summary>저장 후 알릴 사항(승계·소실 등).</summary>
        public ObservableCollection<string> Notices { get; private set; }

        /// <summary>저장 명령.</summary>
        public ICommand SaveCommand { get; private set; }

        /// <summary>파일에서 다시 읽는 명령.</summary>
        public ICommand ReloadCommand { get; private set; }

        /// <summary>필터 해제 명령.</summary>
        public ICommand ClearFilterCommand { get; private set; }

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

        /// <summary>코드·이름 검색어.</summary>
        public string SearchText
        {
            get { return _searchText; }
            set
            {
                if (Set(ref _searchText, value))
                {
                    ApplyFilter();
                }
            }
        }

        /// <summary>비활성 규칙만 보기.</summary>
        public bool DisabledOnly
        {
            get { return _disabledOnly; }
            set
            {
                if (Set(ref _disabledOnly, value))
                {
                    ApplyFilter();
                }
            }
        }

        /// <summary>치명 규칙만 보기.</summary>
        public bool CriticalOnly
        {
            get { return _criticalOnly; }
            set
            {
                if (Set(ref _criticalOnly, value))
                {
                    ApplyFilter();
                }
            }
        }

        /// <summary>보이는 행 수 안내.</summary>
        public string FilterSummary
        {
            get
            {
                return string.Format(
                    CultureInfo.InvariantCulture, "{0} / {1} 건", Rows.Count, All.Count);
            }
        }

        /// <summary>쓰기 작업이 잠겨 있는지 여부.</summary>
        public bool IsLocked
        {
            get { return _host == null || _host.WriteAccess == null || !_host.WriteAccess.IsWriteAllowed; }
        }

        /// <summary>쓰기 잠금 안내 문구.</summary>
        public string LockNotice
        {
            get
            {
                return _host == null || _host.WriteAccess == null
                    ? "런타임이 없어 편집 결과를 적용할 수 없습니다."
                    : _host.WriteAccess.DescribeDenial();
            }
        }

        /// <summary>알람 설정 파일 경로.</summary>
        public string AlarmPath
        {
            get
            {
                return _host == null
                    ? "config/alarms.json"
                    : Path.Combine(_host.ConfigFolder ?? "config", "alarms.json");
            }
        }

        /// <summary>
        /// 치명 알람을 끄려 한다는 경고. 해당 변경이 없으면 null.
        /// </summary>
        /// <remarks>
        /// 치명 알람은 자동 운전을 중단시키는 종류다. 현장에서 과민하다는 이유로
        /// 끄는 일이 실제로 생기고, 그 조작은 <b>기록도 흔적도 없이</b> 끝난다.
        /// 그래서 한 번 더 묻는다. 막지는 않는다 — 막으면 파일을 직접 열게 되고,
        /// 그 경로에는 검증도 확인 절차도 없다.
        /// </remarks>
        public string CriticalDisableWarning
        {
            get
            {
                IList<string> codes = CriticalBeingDisabled();

                if (codes.Count == 0)
                {
                    return null;
                }

                return string.Format(
                    CultureInfo.InvariantCulture,
                    "치명(Critical) 알람 {0}건을 끄려 합니다: {1}. "
                    + "이 알람은 자동 운전을 중단시키는 종류입니다.",
                    codes.Count,
                    string.Join(", ", codes));
            }
        }

        /// <summary>치명 알람 비활성화를 확인했는지 여부.</summary>
        public bool CriticalDisableConfirmed
        {
            get { return _criticalDisableConfirmed; }
            set { Set(ref _criticalDisableConfirmed, value); }
        }

        /// <summary>현재 런타임의 알람 규칙을 화면에 채운다.</summary>
        public void Load()
        {
            All.Clear();
            Rows.Clear();
            Errors.Clear();
            Notices.Clear();

            CriticalDisableConfirmed = false;

            AlarmLoadResult result = AlarmConfigLoader.LoadFromFile(AlarmPath, CurrentRecipe());

            if (!result.IsSuccess)
            {
                foreach (string error in result.Errors)
                {
                    Errors.Add(error);
                }

                StatusText = "알람 설정을 읽지 못했습니다.";
                HasError = true;
                RaiseFilterState();
                return;
            }

            foreach (AlarmRule rule in result.Rules)
            {
                All.Add(new AlarmRuleRowViewModel(rule, OnRowChanged));
            }

            ApplyFilter();

            StatusText = string.Format(
                CultureInfo.InvariantCulture, "알람 {0}건을 읽었습니다.", All.Count);

            HasError = false;

            Raise("IsLocked");
            Raise("LockNotice");
        }

        /// <summary>필터를 적용해 보이는 행을 다시 만든다.</summary>
        /// <remarks>
        /// 74행을 다시 채운다. 사람의 조작에만 반응하므로 매 틱 갱신과 다르다.
        /// </remarks>
        private void ApplyFilter()
        {
            Rows.Clear();

            string needle = _searchText == null ? null : _searchText.Trim();

            foreach (AlarmRuleRowViewModel row in All)
            {
                if (_disabledOnly && row.Enabled)
                {
                    continue;
                }

                if (_criticalOnly && !row.IsCritical)
                {
                    continue;
                }

                if (!string.IsNullOrEmpty(needle) && !row.Matches(needle))
                {
                    continue;
                }

                Rows.Add(row);
            }

            RaiseFilterState();
        }

        /// <summary>필터 해제.</summary>
        /// <param name="parameter">사용하지 않는다.</param>
        private void OnClearFilter(object parameter)
        {
            _searchText = null;
            _disabledOnly = false;
            _criticalOnly = false;

            Raise("SearchText");
            Raise("DisabledOnly");
            Raise("CriticalOnly");

            ApplyFilter();
        }

        /// <summary>행이 바뀌면 경고 문구를 다시 계산한다.</summary>
        private void OnRowChanged()
        {
            Raise("CriticalDisableWarning");
        }

        /// <summary>필터 관련 표시를 갱신한다.</summary>
        private void RaiseFilterState()
        {
            Raise("FilterSummary");
            Raise("CriticalDisableWarning");
        }

        /// <summary>현재 적용 중인 레시피를 가져온다.</summary>
        /// <returns>레시피. 없으면 null.</returns>
        private RecipeDefinition CurrentRecipe()
        {
            EsamRuntime runtime = _host == null ? null : _host.Runtime;

            return runtime == null || runtime.Control == null ? null : runtime.Control.Recipe;
        }

        /// <summary>끄려는 치명 알람 코드를 모은다.</summary>
        /// <returns>코드 목록.</returns>
        private IList<string> CriticalBeingDisabled()
        {
            List<string> codes = new List<string>();

            foreach (AlarmRuleRowViewModel row in All)
            {
                if (row.IsCritical && row.WasEnabled && !row.Enabled)
                {
                    codes.Add(row.Code);
                }
            }

            return codes;
        }

        /// <summary>쓰기가 허용되는지 판정한다.</summary>
        /// <param name="parameter">사용하지 않는다.</param>
        /// <returns>허용되면 true.</returns>
        private bool CanWrite(object parameter)
        {
            return !IsLocked && All.Count > 0;
        }

        /// <summary>쓰기 권한이 바뀌면 버튼 상태를 갱신한다.</summary>
        /// <param name="sender">이벤트 발신자.</param>
        /// <param name="e">이벤트 인자.</param>
        private void OnWriteAccessChanged(object sender, EventArgs e)
        {
            Raise("IsLocked");
            Raise("LockNotice");

            CommandManager.InvalidateRequerySuggested();
        }

        /// <summary>파일에서 다시 읽는다.</summary>
        /// <param name="parameter">사용하지 않는다.</param>
        private void OnReload(object parameter)
        {
            Load();
            StatusText = "파일에서 다시 읽었습니다.";
        }

        /// <summary>검증 후 저장하고 런타임에 적용한다.</summary>
        /// <param name="parameter">사용하지 않는다.</param>
        private void OnSave(object parameter)
        {
            Errors.Clear();
            Notices.Clear();
            HasError = false;

            IList<string> criticals = CriticalBeingDisabled();

            if (criticals.Count > 0 && !CriticalDisableConfirmed)
            {
                Errors.Add(CriticalDisableWarning);
                Errors.Add("아래 확인란을 체크한 뒤 다시 저장하십시오.");

                StatusText = "치명 알람 비활성화는 확인이 필요합니다.";
                HasError = true;
                return;
            }

            List<AlarmRule> edited = new List<AlarmRule>();

            foreach (AlarmRuleRowViewModel row in All)
            {
                string parseError;
                AlarmRule rule = row.ToRule(out parseError);

                if (rule == null)
                {
                    Errors.Add(parseError);
                    continue;
                }

                edited.Add(rule);
            }

            if (Errors.Count > 0)
            {
                StatusText = "입력값을 확인하십시오.";
                HasError = true;
                return;
            }

            // 저장 시점에 파일을 다시 읽는다. 화면을 열어 둔 사이 누가 주석을
            // 고쳤다면 그 변경 위에 값을 얹어야 한다. 화면이 들고 있던 옛 원문에
            // 쓰면 그 편집을 지운다.
            string original;

            if (!TryReadFile(out original))
            {
                return;
            }

            string updated;
            string editError;

            if (!AlarmDocumentEditor.TryApply(original, edited, out updated, out editError))
            {
                Errors.Add(editError);
                StatusText = "저장하지 않았습니다.";
                HasError = true;
                return;
            }

            // ★ 저장 전에 로드와 같은 경로로 검증한다.
            // 화면에서 따로 검사하면 규칙이 두 곳에 생기고, 한쪽만 바뀌면
            // 화면은 통과시키는데 다음 기동에서 로드가 실패해 장비가 뜨지 않는다.
            AlarmLoadResult verified = AlarmConfigLoader.LoadFromJson(updated, CurrentRecipe());

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
                Notices.Add("경고: " + warning);
            }

            if (!TryWriteFile(updated))
            {
                return;
            }

            ApplyToRuntime(verified.Rules);
        }

        /// <summary>원문을 읽는다.</summary>
        /// <param name="text">원문(출력).</param>
        /// <returns>성공하면 true.</returns>
        private bool TryReadFile(out string text)
        {
            text = null;

            try
            {
                text = File.ReadAllText(AlarmPath);
                return true;
            }
            catch (IOException ex)
            {
                Errors.Add(ex.Message);
            }
            catch (UnauthorizedAccessException ex)
            {
                Errors.Add(ex.Message);
            }

            StatusText = "파일을 읽지 못했습니다.";
            HasError = true;
            return false;
        }

        /// <summary>원문을 쓴다.</summary>
        /// <param name="text">쓸 내용.</param>
        /// <returns>성공하면 true.</returns>
        private bool TryWriteFile(string text)
        {
            try
            {
                File.WriteAllText(AlarmPath, text);
                return true;
            }
            catch (IOException ex)
            {
                Errors.Add(ex.Message);
                StatusText = "파일을 쓰지 못했습니다.";
            }
            catch (UnauthorizedAccessException ex)
            {
                Errors.Add(ex.Message);
                StatusText = "파일 접근이 거부되었습니다.";
            }

            HasError = true;
            return false;
        }

        /// <summary>런타임에 규칙을 적용하고 결과를 알린다.</summary>
        /// <param name="rules">검증을 통과한 규칙.</param>
        private void ApplyToRuntime(IList<AlarmRule> rules)
        {
            EsamRuntime runtime = _host == null ? null : _host.Runtime;

            if (runtime == null || runtime.Alarms == null)
            {
                StatusText = "저장했습니다. 런타임이 없어 다음 기동에 반영됩니다.";
                MarkSaved();
                return;
            }

            AlarmRuleSwapResult swap = runtime.Alarms.ReplaceRules(rules);

            foreach (string code in swap.DroppedActive)
            {
                // 떠 있던 알람이 규칙과 함께 사라진 것은 조건이 해소된 것과 다르다.
                Notices.Add("발생 중이던 알람 " + code + " 의 규칙이 없어졌습니다.");
            }

            if (swap.HasStructuralChange)
            {
                Notices.Add(string.Format(
                    CultureInfo.InvariantCulture,
                    "규칙 구성이 바뀌었습니다. 추가 {0}건, 삭제 {1}건.",
                    swap.Added.Count, swap.Removed.Count));
            }

            StatusText = string.Format(
                CultureInfo.InvariantCulture,
                "저장했습니다. {0}건에 즉시 반영되었습니다.", swap.Carried);

            MarkSaved();
        }

        /// <summary>저장 성공 후 편집 기준선을 갱신한다.</summary>
        /// <remarks>
        /// 기준선을 갱신하지 않으면 다음 저장에서 <b>이미 저장한 치명 알람 해제를
        /// 다시 확인하라고 묻는다.</b> 두 번째부터는 확인이 형식이 되고,
        /// 형식이 된 확인은 아무도 읽지 않는다.
        /// </remarks>
        private void MarkSaved()
        {
            foreach (AlarmRuleRowViewModel row in All)
            {
                row.MarkSaved();
            }

            CriticalDisableConfirmed = false;
            Raise("CriticalDisableWarning");
        }
    }
}
