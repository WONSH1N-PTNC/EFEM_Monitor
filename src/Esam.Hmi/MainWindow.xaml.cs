using System.Windows;
using Esam.Hmi.ViewModels;

namespace Esam.Hmi
{
    /// <summary>ESAM HMI 메인 윈도우.</summary>
    public partial class MainWindow : Window
    {
        /// <summary>윈도우를 생성한다.</summary>
        public MainWindow()
        {
            InitializeComponent();

            // 현재는 화면이 하나뿐이므로 여기서 직접 연결한다.
            // Maintenance / Config / I-O / Data Log 가 추가되면
            // ShellViewModel 과 네비게이션 서비스로 분리한다(DESIGN.md 9 참조).
            Dashboard.DataContext = new DashboardViewModel();
        }
    }
}
