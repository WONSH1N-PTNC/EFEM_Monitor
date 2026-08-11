using System;
using System.Windows.Input;
using Esam.Hmi.Infrastructure;
using Esam.Services;

namespace Esam.Hmi.ViewModels
{
    /// <summary>표시 중인 화면.</summary>
    public enum ShellScreen
    {
        /// <summary>운전 대시보드.</summary>
        Operate = 0,

        /// <summary>설정 — 레시피 편집.</summary>
        ConfigRecipe = 1
    }

    /// <summary>
    /// 화면 전환과 공통 상태를 관리한다.
    /// </summary>
    /// <remarks>
    /// <para>화면이 하나일 때는 <c>MainWindow</c> 가 직접 배선해도 됐다.
    /// 둘이 되는 순간 "지금 무엇을 보고 있는가" 를 들고 있을 곳이 필요하다.</para>
    /// <para><b>화면 인스턴스는 유지한다.</b> 전환할 때마다 새로 만들면
    /// 트렌드 이력이 초기화되고, 편집 중이던 레시피 값이 사라진다.
    /// 가시성만 바꾸고 객체는 살려 둔다.</para>
    /// </remarks>
    public sealed class ShellViewModel : ObservableObject
    {
        private readonly ManualWriteAccessProvider _writeAccess;
        private ShellScreen _screen = ShellScreen.Operate;

        /// <summary>셸을 생성한다.</summary>
        /// <param name="host">런타임 호스트. null 이면 디자인타임으로 동작한다.</param>
        public ShellViewModel(HmiHost host)
        {
            EsamRuntime runtime = host == null ? null : host.Runtime;

            _writeAccess = host == null ? null : host.WriteAccessControl;

            Dashboard = new DashboardViewModel(runtime);
            Banner = new SystemBannerViewModel(host);
            Recipe = new RecipeEditorViewModel(host);

            SelectScreenCommand = new RelayCommand(OnSelectScreen);
            ToggleWriteAccessCommand = new RelayCommand(OnToggleWriteAccess);
        }

        /// <summary>운전 대시보드.</summary>
        public DashboardViewModel Dashboard { get; private set; }

        /// <summary>구성 경고 배너.</summary>
        public SystemBannerViewModel Banner { get; private set; }

        /// <summary>레시피 편집기.</summary>
        public RecipeEditorViewModel Recipe { get; private set; }

        /// <summary>화면 선택 명령.</summary>
        public ICommand SelectScreenCommand { get; private set; }

        /// <summary>쓰기 잠금 토글 명령.</summary>
        public ICommand ToggleWriteAccessCommand { get; private set; }

        /// <summary>운전 화면을 보고 있는지 여부.</summary>
        public bool IsOperate
        {
            get { return _screen == ShellScreen.Operate; }
        }

        /// <summary>레시피 설정 화면을 보고 있는지 여부.</summary>
        public bool IsConfigRecipe
        {
            get { return _screen == ShellScreen.ConfigRecipe; }
        }

        /// <summary>쓰기가 허용된 상태인지 여부.</summary>
        public bool IsWriteAllowed
        {
            get { return _writeAccess != null && _writeAccess.IsWriteAllowed; }
        }

        /// <summary>쓰기 잠금 버튼 문구.</summary>
        /// <remarks>
        /// 현재 상태가 아니라 <b>누르면 무엇이 되는지</b>를 적는다.
        /// 상태를 적으면 "지금 잠겨 있다" 인지 "누르면 잠긴다" 인지 헷갈린다.
        /// </remarks>
        public string WriteAccessText
        {
            get { return IsWriteAllowed ? "쓰기 잠그기" : "정비 모드 진입"; }
        }

        /// <summary>화면을 전환한다.</summary>
        /// <param name="parameter">화면 이름.</param>
        private void OnSelectScreen(object parameter)
        {
            string name = parameter as string;

            if (string.Equals(name, "ConfigRecipe", StringComparison.OrdinalIgnoreCase))
            {
                _screen = ShellScreen.ConfigRecipe;

                // 화면에 들어올 때 현재 적용값을 다시 읽는다.
                // 다른 경로로 레시피가 바뀌었을 수 있고, 옛 값을 보여 주면
                // 저장할 때 그것이 그대로 덮어쓴다.
                Recipe.Load();
            }
            else
            {
                _screen = ShellScreen.Operate;
            }

            Raise("IsOperate");
            Raise("IsConfigRecipe");
        }

        /// <summary>쓰기 잠금을 토글한다.</summary>
        /// <param name="parameter">사용하지 않는다.</param>
        /// <remarks>
        /// S9 에서 계정·권한 등급이 들어오면 이 자리를 로그인 창이 대신한다.
        /// 관문의 위치는 그대로 두고 판정 근거만 바뀐다.
        /// </remarks>
        private void OnToggleWriteAccess(object parameter)
        {
            if (_writeAccess == null)
            {
                return;
            }

            _writeAccess.SetAllowed(!_writeAccess.IsWriteAllowed);

            Raise("IsWriteAllowed");
            Raise("WriteAccessText");
        }
    }
}
