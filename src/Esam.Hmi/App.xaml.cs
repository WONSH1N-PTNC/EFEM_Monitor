using System.Windows;
using Esam.Hmi.Infrastructure;
using Esam.Services;

namespace Esam.Hmi
{
    /// <summary>ESAM HMI 애플리케이션 진입점.</summary>
    /// <remarks>
    /// <para>조립 루트다. 런타임을 세우고 창을 띄우며, 종료 시 순서대로 내린다.</para>
    /// <para><b>종료 처리가 중요하다.</b> 그냥 프로세스를 끝내면 밸브는 열린 채,
    /// 팬은 도는 채로 프로그램만 사라지고 인터록 평가도 함께 멈춘다.
    /// 아무도 보지 않는 상태에서 액추에이터가 계속 동작한다.
    /// <see cref="EsamRuntime.Stop"/> 이 파킹 지령을 보낸 뒤 워커를 정지시킨다.</para>
    /// </remarks>
    public partial class App : Application
    {
        /// <summary>현재 애플리케이션의 런타임 호스트.</summary>
        public HmiHost Host { get; private set; }

        /// <inheritdoc />
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            Host = new HmiHost();

            // 기본은 시뮬레이션이다. 하드웨어 없이 화면 전역을 확인할 수 있고,
            // 현장 설치 시 설정 화면에서 Serial 로 바꾼다.
            Host.Start("config", TransportMode.Simulation);
        }

        /// <inheritdoc />
        protected override void OnExit(ExitEventArgs e)
        {
            if (Host != null)
            {
                EsamRuntime runtime = Host.Runtime;

                if (runtime != null)
                {
                    // 액추에이터를 안전 위치로 보낸 뒤 폴링을 멈춘다.
                    // 순서를 바꾸면 파킹 지령이 큐에만 남고 전송되지 않는다.
                    runtime.Stop();
                }

                Host.Dispose();
                Host = null;
            }

            base.OnExit(e);
        }
    }
}
