using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using Esam.Hmi.Infrastructure;
using Esam.Services;

namespace Esam.Hmi.ViewModels
{
    /// <summary>
    /// 구성 경고와 런타임 장애를 화면 상단에 드러내고, 작업자의 확인·해제를 받는다.
    /// </summary>
    /// <remarks>
    /// <para><b>이 화면이 없으면 그동안 만든 안전 장치가 무의미하다.</b></para>
    /// <list type="bullet">
    ///   <item><description><c>ConfigWarning</c>(D10) — 안전 입력이 없다·레시피가 없다 같은
    ///     구성 결함을 모아 두었지만 표시할 곳이 없었다</description></item>
    ///   <item><description><c>ResetRuntimeFault</c>(D14) — 안전 경로 장애로 올린 SafeStop 은
    ///     작업자가 해제해야 하는데 누를 버튼이 없었다</description></item>
    ///   <item><description><c>HmiHost.StartupError</c> — 설정 파일이 잘못되면 조립에 실패하는데
    ///     사유를 볼 방법이 없었다</description></item>
    /// </list>
    /// <para>세 가지 모두 "기록은 남지만 아무도 보지 않는" 상태였다.</para>
    /// </remarks>
    public sealed class SystemBannerViewModel : ObservableObject
    {
        private readonly HmiHost _host;
        private string _lastSignature;
        private bool _isExpanded = true;

        /// <summary>배너를 생성한다.</summary>
        /// <param name="host">런타임 호스트. null 이면 디자인타임으로 동작한다.</param>
        public SystemBannerViewModel(HmiHost host)
        {
            _host = host;

            Warnings = new ObservableCollection<ConfigWarningRowViewModel>();

            AcknowledgeCommand = new RelayCommand(OnAcknowledge, CanAcknowledge);
            ResetFaultCommand = new RelayCommand(OnResetFault, CanResetFault);
            RestartCommand = new RelayCommand(OnRestart, CanExecuteRestart);
            ToggleCommand = new RelayCommand(OnToggle);

            Refresh();
        }

        /// <summary>구성 경고 목록.</summary>
        public ObservableCollection<ConfigWarningRowViewModel> Warnings { get; private set; }

        /// <summary>수동 조작 활성 여부의 백킹 필드.</summary>
        private bool _isManualActive;

        /// <summary>배너를 표시해야 하는지 여부.</summary>
        public bool IsVisible
        {
            get { return HasStartupError || Warnings.Count > 0 || IsManualActive || CanRestart; }
        }

        /// <summary>
        /// 수동 조작이 살아 있는지 여부. 정비 화면이 알린다.
        /// </summary>
        /// <remarks>
        /// <para>밸브를 수동으로 열어 두고 다른 화면으로 넘어가면 그 사실이
        /// 어디에도 보이지 않는다. 화면 전환 시 자동으로 파킹하지만,
        /// <b>파킹 지령을 보낸 것과 액추에이터가 실제로 닫힌 것은 다르다.</b>
        /// 통신이 끊겨 있으면 지령은 큐에 남는다.</para>
        /// <para>그래서 정비 화면 안에 있는 동안에도 배너에 남긴다.
        /// 색을 아끼는 배너의 원칙에 맞춰 경고 색을 쓴다 — 고장은 아니지만
        /// 자동 운전과 다른 상태라는 뜻이다.</para>
        /// </remarks>
        public bool IsManualActive
        {
            get { return _isManualActive; }
            set
            {
                if (Set(ref _isManualActive, value))
                {
                    Raise("IsVisible");
                    Raise("ManualNotice");
                }
            }
        }

        /// <summary>수동 조작 안내 문구. 없으면 null.</summary>
        public string ManualNotice
        {
            get
            {
                return _isManualActive
                    ? "수동 조작이 살아 있습니다. 자동 운전으로 돌아가기 전에 정리하십시오."
                    : null;
            }
        }

        /// <summary>
        /// 상세 목록을 펼친 상태인지 여부.
        /// </summary>
        /// <remarks>
        /// <para>확인(Acknowledge)하면 접는다. <b>사라지지는 않는다.</b></para>
        /// <para>완전히 감추면 "안전 입력이 배선되지 않은 채 운전 중" 이라는 사실이
        /// 화면에서 없어진다. 확인은 "인지했다" 는 기록이지 "해결했다" 가 아니다.
        /// 접힌 한 줄은 남겨 두고, 누르면 다시 펼쳐 무엇이 걸려 있는지 볼 수 있게 한다.</para>
        /// <para>기동 실패는 접지 않는다. 운전 자체가 불가능한 상태이므로
        /// 화면을 정리해 줄 이유가 없다.</para>
        /// </remarks>
        public bool IsExpanded
        {
            get { return _isExpanded || HasStartupError || CanRestart; }
        }

        /// <summary>접기/펼치기 토글 명령.</summary>
        public ICommand ToggleCommand { get; private set; }

        /// <summary>접힌 상태에서 보여줄 한 줄 요약.</summary>
        public string CollapsedSummary { get; private set; }

        /// <summary>기동 실패 사유. 없으면 null.</summary>
        public string StartupError
        {
            get { return _host == null ? null : _host.StartupError; }
        }

        /// <summary>기동에 실패했는지 여부.</summary>
        public bool HasStartupError
        {
            get { return !string.IsNullOrEmpty(StartupError); }
        }

        /// <summary>자동 운전을 막는 경고가 있는지 여부.</summary>
        public bool HasBlocking { get; private set; }

        /// <summary>차단 경고가 확인되었는지 여부.</summary>
        public bool IsAcknowledged { get; private set; }

        /// <summary>배너 제목.</summary>
        public string Title { get; private set; }

        /// <summary>배너 본문.</summary>
        public string Detail { get; private set; }

        /// <summary>차단 경고 확인 명령.</summary>
        public ICommand AcknowledgeCommand { get; private set; }

        /// <summary>안전 경로 장애 해제 명령.</summary>
        public ICommand ResetFaultCommand { get; private set; }

        /// <summary>장애 단계에서 기동 시퀀스를 다시 시작하는 명령.</summary>
        public ICommand RestartCommand { get; private set; }

        /// <summary>재시작 버튼을 보여야 하는지 여부.</summary>
        /// <remarks>
        /// <b>Fault 는 사람이 손대지 않으면 영원히 유지된다(D23).</b>
        /// 배너의 "장애 해제" 는 SafeStop 을 Fault 로 내려보내는 명령이라
        /// 도착지에서는 사라진다. 여기서 나올 길을 따로 둔다.
        /// </remarks>
        public bool CanRestart { get; private set; }

        /// <summary>런타임 장애로 정지 중인지 여부.</summary>
        public bool HasRuntimeFault { get; private set; }

        /// <summary>
        /// 현재 상태를 다시 읽는다. 대시보드 갱신 주기에 맞춰 호출한다.
        /// </summary>
        /// <remarks>
        /// 내용이 바뀌지 않았으면 컬렉션을 건드리지 않는다. 매 주기마다
        /// <c>Clear</c> 후 재구성하면 목록을 읽는 중에 선택이 풀리고 화면이 깜빡인다.
        /// </remarks>
        public void Refresh()
        {
            EsamRuntime runtime = _host == null ? null : _host.Runtime;

            IList<ConfigWarning> current = runtime == null
                ? new List<ConfigWarning>()
                : runtime.Warnings;

            string signature = BuildSignature(current);

            if (!string.Equals(signature, _lastSignature, StringComparison.Ordinal))
            {
                _lastSignature = signature;

                Warnings.Clear();

                foreach (ConfigWarning warning in current)
                {
                    Warnings.Add(new ConfigWarningRowViewModel(warning));
                }
            }

            HasBlocking = runtime != null && runtime.HasBlockingWarnings;
            IsAcknowledged = runtime != null && runtime.WarningsAcknowledged;

            // 안전 경로 장애로 올린 SafeStop 인지 판정한다.
            // ResetRuntimeFault 가 해제할 것이 있으면 true 를 반환하므로
            // 그 조건을 그대로 쓸 수는 없다(호출하면 실제로 해제된다).
            HasRuntimeFault = runtime != null
                              && runtime.Engine != null
                              && runtime.Engine.StateMachine.Phase == Esam.Domain.Control.SystemPhase.SafeStop;

            // Fault 는 사람이 손대지 않으면 영원히 유지된다.
            // 쓰기 잠금은 여기에 걸지 않는다 — 잠긴 상태로 Fault 에 들어가면
            // 잠금을 풀 화면(Settings)도 조작할 수 없어 복구 자체가 막힌다.
            CanRestart = runtime != null
                         && runtime.Engine != null
                         && runtime.Engine.StateMachine.Phase == Esam.Domain.Control.SystemPhase.Fault;

            UpdateText();
            UpdateCollapsedSummary();

            Raise("IsVisible");
            Raise("StartupError");
            Raise("HasStartupError");
            Raise("HasBlocking");
            Raise("IsAcknowledged");
            Raise("HasRuntimeFault");
            Raise("CanRestart");
            Raise("Title");
            Raise("Detail");
            Raise("IsExpanded");
            Raise("CollapsedSummary");
        }

        /// <summary>접기/펼치기를 토글한다.</summary>
        /// <param name="parameter">사용하지 않는다.</param>
        private void OnToggle(object parameter)
        {
            _isExpanded = !_isExpanded;

            Raise("IsExpanded");
        }

        /// <summary>제목과 본문을 현재 상태에 맞춰 만든다.</summary>
        private void UpdateText()
        {
            if (HasStartupError)
            {
                Title = "기동 실패";
                Detail = StartupError;
                return;
            }

            if (Warnings.Count == 0)
            {
                Title = null;
                Detail = null;
                return;
            }

            int blocking = 0;

            foreach (ConfigWarningRowViewModel row in Warnings)
            {
                if (row.IsBlocking)
                {
                    blocking++;
                }
            }

            if (blocking > 0)
            {
                Title = string.Format(
                    CultureInfo.InvariantCulture,
                    "안전 기능 경고 {0}건{1}",
                    blocking,
                    IsAcknowledged ? " (확인됨)" : string.Empty);

                Detail = IsAcknowledged
                    ? "확인 처리된 상태로 운전 중입니다. 원인은 해소되지 않았습니다."
                    : "확인하기 전에는 자동 운전에 진입할 수 없습니다.";

                return;
            }

            Title = string.Format(
                CultureInfo.InvariantCulture, "구성 참고 {0}건", Warnings.Count);

            Detail = "운전에는 영향이 없으나 확인이 필요한 항목입니다.";
        }

        /// <summary>접힌 상태의 한 줄 요약을 만든다.</summary>
        private void UpdateCollapsedSummary()
        {
            if (!IsVisible)
            {
                CollapsedSummary = null;
                return;
            }

            int blocking = 0;

            foreach (ConfigWarningRowViewModel row in Warnings)
            {
                if (row.IsBlocking)
                {
                    blocking++;
                }
            }

            CollapsedSummary = blocking > 0
                ? string.Format(
                    CultureInfo.InvariantCulture,
                    "안전 기능 경고 {0}건 확인됨 · 구성 참고 {1}건",
                    blocking, Warnings.Count - blocking)
                : string.Format(
                    CultureInfo.InvariantCulture, "구성 참고 {0}건", Warnings.Count);
        }

        /// <summary>목록이 바뀌었는지 판정할 서명을 만든다.</summary>
        /// <param name="warnings">경고 목록.</param>
        /// <returns>서명 문자열.</returns>
        private static string BuildSignature(IList<ConfigWarning> warnings)
        {
            if (warnings == null || warnings.Count == 0)
            {
                return string.Empty;
            }

            string[] parts = new string[warnings.Count];

            for (int i = 0; i < warnings.Count; i++)
            {
                parts[i] = warnings[i].Code + "|" + warnings[i].Message;
            }

            return string.Join("\n", parts);
        }

        /// <summary>차단 경고를 확인 처리할 수 있는지 판정한다.</summary>
        /// <param name="parameter">사용하지 않는다.</param>
        /// <returns>가능하면 true.</returns>
        private bool CanAcknowledge(object parameter)
        {
            return HasBlocking && !IsAcknowledged;
        }

        /// <summary>차단 경고를 확인 처리한다.</summary>
        /// <param name="parameter">사용하지 않는다.</param>
        /// <remarks>
        /// 경고를 없애는 것이 아니라 <b>사람이 인지했음을 기록</b>하는 것이다.
        /// 목록은 그대로 남아 화면에 계속 표시된다.
        /// </remarks>
        private void OnAcknowledge(object parameter)
        {
            EsamRuntime runtime = _host == null ? null : _host.Runtime;

            if (runtime != null)
            {
                runtime.AcknowledgeWarnings();
            }

            // 확인하면 접는다. 화면을 잠식하지 않으면서 사실은 남긴다.
            _isExpanded = false;

            Refresh();
        }

        /// <summary>장애 해제가 가능한지 판정한다.</summary>
        /// <param name="parameter">사용하지 않는다.</param>
        /// <returns>가능하면 true.</returns>
        private bool CanResetFault(object parameter)
        {
            return HasRuntimeFault;
        }

        /// <summary>재시작을 실행할 수 있는지 판정한다.</summary>
        /// <param name="parameter">사용하지 않는다.</param>
        /// <returns>가능하면 true.</returns>
        private bool CanExecuteRestart(object parameter)
        {
            return CanRestart;
        }

        /// <summary>기동 시퀀스를 다시 시작한다.</summary>
        /// <param name="parameter">사용하지 않는다.</param>
        /// <remarks>
        /// Ready 로 바로 가지 않는다. Init 부터 다시 시작해 원점 복귀를 거친다.
        /// 장애 뒤에는 밸브의 기계적 원점을 신뢰할 수 없다.
        /// </remarks>
        private void OnRestart(object parameter)
        {
            EsamRuntime runtime = _host == null ? null : _host.Runtime;

            if (runtime != null)
            {
                runtime.RequestRestart();
            }

            Refresh();
        }

        /// <summary>안전 경로 장애로 올린 SafeStop 을 해제한다.</summary>
        /// <param name="parameter">사용하지 않는다.</param>
        /// <remarks>
        /// 해제해도 Ready 가 아니라 Fault 로 간다. 안전 경로가 동작하지 못했던 뒤에는
        /// 밸브의 기계적 원점을 신뢰할 수 없으므로 초기화와 원점 복귀를 다시 거쳐야 한다.
        /// </remarks>
        private void OnResetFault(object parameter)
        {
            EsamRuntime runtime = _host == null ? null : _host.Runtime;

            if (runtime != null)
            {
                runtime.ResetRuntimeFault();
            }

            Refresh();
        }
    }

    /// <summary>구성 경고 1건의 표시 상태.</summary>
    public sealed class ConfigWarningRowViewModel
    {
        /// <summary>경고를 표시용으로 감싼다.</summary>
        /// <param name="warning">구성 경고.</param>
        public ConfigWarningRowViewModel(ConfigWarning warning)
        {
            if (warning == null)
            {
                throw new ArgumentNullException("warning");
            }

            Code = warning.Code;
            Message = warning.Message;
            Remedy = warning.Remedy;
            IsBlocking = warning.IsBlocking;
            SeverityText = warning.IsBlocking ? "차단" : "참고";
        }

        /// <summary>경고 코드.</summary>
        public string Code { get; private set; }

        /// <summary>경고 본문.</summary>
        public string Message { get; private set; }

        /// <summary>조치 안내. 없으면 null.</summary>
        public string Remedy { get; private set; }

        /// <summary>자동 운전을 막는 경고인지 여부.</summary>
        public bool IsBlocking { get; private set; }

        /// <summary>심각도 표기.</summary>
        public string SeverityText { get; private set; }

        /// <summary>조치 안내가 있는지 여부.</summary>
        public bool HasRemedy
        {
            get { return !string.IsNullOrEmpty(Remedy); }
        }
    }
}
