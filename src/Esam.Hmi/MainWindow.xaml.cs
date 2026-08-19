using System;
using System.Windows;
using System.Windows.Threading;
using Esam.Hmi.Infrastructure;
using Esam.Hmi.ViewModels;
using Esam.Services;

namespace Esam.Hmi
{
    /// <summary>ESAM HMI 메인 윈도우.</summary>
    public partial class MainWindow : Window
    {
        private ShellViewModel _shell;
        private DispatcherTimer _bannerTimer;

        /// <summary>윈도우를 생성한다.</summary>
        public MainWindow()
        {
            InitializeComponent();

            Loaded += OnLoaded;
            Closed += OnClosed;
        }

        /// <summary>창이 표시되면 런타임을 연결하고 갱신을 시작한다.</summary>
        /// <param name="sender">이벤트 발신자.</param>
        /// <param name="e">이벤트 인자.</param>
        /// <remarks>
        /// 생성자가 아니라 <c>Loaded</c> 에서 연결한다. 생성자 시점에는
        /// <see cref="App.Host"/> 가 아직 없을 수 있고, 그 경우 조용히 디자인타임
        /// 모드로 떨어져 <b>가짜 값이 도는 화면</b>이 된다.
        /// </remarks>
        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (_shell != null)
            {
                return;
            }

            App app = Application.Current as App;
            HmiHost host = app == null ? null : app.Host;
            EsamRuntime runtime = host == null ? null : host.Runtime;

            _shell = new ShellViewModel(host);
            DataContext = _shell;

            if (runtime != null)
            {
                // 워커 스레드를 띄워 실제 폴링을 시작한다.
                // 이 호출이 없으면 스냅샷이 갱신되지 않아 화면이 초기값에 멈춘다.
                //
                // 포트 열기 실패는 EsamRuntime.Start 가 구성 경고로 처리한다.
                // 그래도 예상 밖 예외로 창이 뜨지 못하는 일은 없어야 한다.
                // 화면이 없으면 원인을 볼 수도, 설정을 고칠 수도 없다.
                try
                {
                    runtime.Start();
                }
                catch (Exception)
                {
                    // 사유는 배너의 구성 경고로 드러난다.
                }
            }

            _shell.Dashboard.Start();

            // I/O 화면은 보이지 않을 때도 돈다. 화면을 열자마자 값이 차 있어야
            // 커미셔닝에서 "지금 표시된 것이 최신인가" 를 의심하지 않는다.
            // 주기가 250 ms 라 부담이 크지 않다.
            _shell.IoStatus.Start();

            // 정비 화면은 보이지 않을 때도 돈다. 영점 표본은 폴링 주기로 모이고,
            // 화면을 연 뒤에야 모으기 시작하면 첫 취득이 그만큼 늦어진다.
            _shell.Maintenance.Start();

            // 배너는 대시보드보다 느리게 갱신해도 된다.
            // 구성 경고는 초 단위로 바뀌는 값이 아니고, 장애 발생은 1초 안에 보이면 충분하다.
            _bannerTimer = new DispatcherTimer(DispatcherPriority.Background);
            _bannerTimer.Interval = TimeSpan.FromMilliseconds(1000);
            _bannerTimer.Tick += OnBannerTick;
            _bannerTimer.Start();
        }

        /// <summary>배너 갱신 주기.</summary>
        /// <param name="sender">이벤트 발신자.</param>
        /// <param name="e">이벤트 인자.</param>
        private void OnBannerTick(object sender, EventArgs e)
        {
            if (_shell != null)
            {
                _shell.Banner.Refresh();
            }
        }

        /// <summary>창이 닫히면 갱신을 멈춘다.</summary>
        /// <param name="sender">이벤트 발신자.</param>
        /// <param name="e">이벤트 인자.</param>
        /// <remarks>
        /// 런타임 정지는 <see cref="App.OnExit"/> 가 맡는다.
        /// 창 하나가 닫혔다고 액추에이터를 세우면, 여러 창 구조로 확장했을 때
        /// 창을 닫는 것만으로 운전이 멈추게 된다.
        /// </remarks>
        private void OnClosed(object sender, EventArgs e)
        {
            if (_bannerTimer != null)
            {
                _bannerTimer.Stop();
                _bannerTimer.Tick -= OnBannerTick;
                _bannerTimer = null;
            }

            if (_shell != null)
            {
                _shell.Dashboard.Stop();
                _shell.IoStatus.Stop();

                // ★ 창을 닫을 때도 수동 조작을 정리한다. 화면 전환만 막으면
                // 밸브를 열어 둔 채 프로그램을 끄는 경로가 남는다.
                _shell.Maintenance.Leave();
                _shell.Maintenance.Stop();

                _shell = null;
            }
        }
    }
}
