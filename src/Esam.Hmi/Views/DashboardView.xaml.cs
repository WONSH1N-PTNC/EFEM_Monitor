using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Esam.Hmi.ViewModels;

namespace Esam.Hmi.Views
{
    /// <summary>Dashboard 화면.</summary>
    public partial class DashboardView : UserControl
    {
        private DashboardViewModel _viewModel;

        /// <summary>화면을 생성한다.</summary>
        public DashboardView()
        {
            InitializeComponent();

            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        /// <summary>
        /// 화면이 로드되면 실시간 갱신을 시작한다.
        /// </summary>
        /// <remarks>
        /// <para><b>디자인 모드 가드가 핵심이다.</b> Visual Studio 디자이너는 컨트롤을
        /// 생성하는 데 그치지 않고 시각 트리를 실제로 로드하므로 <c>Loaded</c> 도 발생한다.
        /// <c>d:DataContext</c> 가 실제 DataContext 로 적용되기 때문에 가드가 없으면
        /// VS 프로세스 안에서 100ms 타이머가 돌아 디자이너가 무거워지고 화면이 깜박인다.</para>
        /// <para>생성자가 아니라 <c>Loaded</c> 에서 시작하는 것만으로는 막을 수 없다.</para>
        /// </remarks>
        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (DesignerProperties.GetIsInDesignMode(this))
            {
                return;
            }

            _viewModel = DataContext as DashboardViewModel;

            if (_viewModel != null)
            {
                _viewModel.Start();
            }
        }

        /// <summary>화면이 내려가면 타이머를 정지한다.</summary>
        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            if (_viewModel != null)
            {
                _viewModel.Stop();
                _viewModel = null;
            }
        }
    }
}
