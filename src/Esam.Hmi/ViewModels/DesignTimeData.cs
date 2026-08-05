namespace Esam.Hmi.ViewModels
{
    /// <summary>
    /// Visual Studio 디자이너용 데이터 소스.
    /// </summary>
    /// <remarks>
    /// XAML 의 <c>d:DataContext</c> 에 연결해, 하드웨어나 실행 없이도
    /// 디자이너에서 실제와 같은 화면을 확인할 수 있게 한다.
    /// <para>레이아웃 검토를 실행 없이 할 수 있어야 화면 작업 속도가 크게 달라진다.
    /// 값이 비어 있는 껍데기만 보이는 디자이너는 사실상 쓸 수 없다.</para>
    /// <para>실시간 타이머는 시작하지 않는다. 디자이너 프로세스에서 타이머가 돌면
    /// Visual Studio 가 무거워지고, 정지 화면으로 배치를 검토하는 편이 오히려 정확하다.</para>
    /// </remarks>
    public static class DesignTimeData
    {
        private static DashboardViewModel _dashboard;

        /// <summary>디자이너에 표시할 대시보드 상태.</summary>
        public static DashboardViewModel Dashboard
        {
            get
            {
                if (_dashboard == null)
                {
                    _dashboard = new DashboardViewModel();
                }

                return _dashboard;
            }
        }
    }
}
