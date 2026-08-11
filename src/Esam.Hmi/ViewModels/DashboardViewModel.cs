using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Esam.Domain.Alarms;
using Esam.Domain.Configuration;
using Esam.Domain.Control;
using Esam.Domain.Models;
using Esam.Hmi.Controls;
using Esam.Hmi.Infrastructure;
using Esam.Services;

namespace Esam.Hmi.ViewModels
{
    /// <summary>제어 기준 센서 모드. 도메인의 SensorMode 와 대응한다.</summary>
    public enum DashboardMode
    {
        /// <summary>센서 1 기준(EFEM 내부 차압). 6 Pa ± 2.</summary>
        Sensor1 = 1,

        /// <summary>센서 2 기준(체인 차압). -10 Pa ± 30.</summary>
        Sensor2 = 2,

        /// <summary>센서 3 기준(배기 차압). -200 Pa ± 100.</summary>
        Sensor3 = 3
    }

    /// <summary>
    /// Dashboard 화면 전체의 표시 상태.
    /// </summary>
    /// <remarks>
    /// <para>현재는 내장 시뮬레이터가 값을 만든다. 하드웨어와 S4 DataStore 가 준비되면
    /// <see cref="Step"/> 을 스냅샷 구독으로 교체하면 되고, 화면 코드는 손대지 않는다.
    /// 시뮬레이터를 내장한 이유는 디자이너와 실행 화면이 하드웨어 없이도
    /// 실제와 같은 움직임을 보여야 레이아웃·색상 검토가 가능하기 때문이다.</para>
    /// <para><b>게이지 축 범위</b>는 목표값 ± 대역의 1.5배로 잡는다.
    /// 이렇게 하면 정상 대역이 게이지의 가운데 2/3 를 차지해,
    /// 바늘이 대역을 벗어나는 순간이 시각적으로 분명해진다.</para>
    /// <para><b>트렌드 축 범위</b>는 목표값 ± 대역의 1.9배다. 대역보다 넉넉히 잡아
    /// 이탈 폭이 얼마나 큰지도 함께 보이게 한 것이다.</para>
    /// </remarks>
    public sealed class DashboardViewModel : ObservableObject
    {
        /// <summary>화면 갱신 주기 [ms]. 사람이 인지하는 한계인 10 FPS 로 제한한다.</summary>
        private const int RefreshIntervalMs = 100;

        /// <summary>트렌드에 보관하는 표본 수. 100ms × 120 = 12초 구간.</summary>
        private const int HistoryLength = 120;

        /// <summary>게이지 축 범위 배수(목표값 ± 대역 × 이 값).</summary>
        private const double GaugeSpanFactor = 1.5;

        /// <summary>트렌드 축 범위 배수.</summary>
        private const double TrendSpanFactor = 1.9;

        /// <summary>체인별 수렴 목표값 [센서1, 센서2, 센서3]. 시뮬레이터용.</summary>
        private static readonly double[][] Targets =
        {
            new[] { 6.2, -11.2, -198.0 },
            new[] { 5.6, -9.4, -203.0 },
            new[] { 6.5, -12.8, -207.0 },
            new[] { 5.9, -7.9, -186.0 },
            new[] { 6.1, -10.6, -201.0 }
        };

        /// <summary>
        /// 값을 끌어올 런타임. null 이면 디자인타임(정적 표본)으로 동작한다.
        /// </summary>
        /// <remarks>
        /// <para><b>구독하지 않고 당긴다.</b> <c>DataStore.SnapshotPublished</c> 는
        /// 포트 워커 스레드에서 발생한다. 거기서 바인딩 속성을 건드리면 WPF 가 예외를
        /// 던지므로, 타이머가 <c>DataStore.Current</c> 를 읽는 방식으로 둔다.</para>
        /// <para>스냅샷을 불변으로 만들고 통째로 교체하도록 설계한 이유가 이것이다.
        /// 당겨오기만 하면 스레드 마샬링 자체가 필요 없다.</para>
        /// </remarks>
        private readonly EsamRuntime _runtime;

        private readonly List<double[]> _history1 = new List<double[]>();
        private readonly List<double[]> _history2 = new List<double[]>();
        private readonly List<double[]> _history3 = new List<double[]>();
        private readonly double[] _valve = new double[5];
        private readonly double[] _fan = new double[5];
        private readonly double[] _fanTemp = new double[5];

        private DispatcherTimer _timer;
        private DashboardMode _mode = DashboardMode.Sensor2;
        private bool _showAlarms = true;
        private string _clock;
        private string _bannerTitle;
        private string _bannerSubtitle;
        private Brush _bannerBrush;
        private Brush _bannerBorderBrush;
        private string _trendCaption;
        private double _trendMinimum;
        private double _trendMaximum;
        private double _trendBandLow;
        private double _trendBandHigh;
        private double _trendSetpoint;
        private int _trendDecimals;
        private long _trendRevision;

        /// <summary>디자인타임용으로 생성한다. 값이 움직이지 않는다.</summary>
        public DashboardViewModel()
            : this(null)
        {
        }

        /// <summary>대시보드 상태를 생성한다.</summary>
        /// <param name="runtime">값을 끌어올 런타임. null 이면 디자인타임 표본을 쓴다.</param>
        public DashboardViewModel(EsamRuntime runtime)
        {
            _runtime = runtime;

            Chains = new ObservableCollection<ChainCardViewModel>();
            Sensor1Gauges = new ObservableCollection<GaugeViewModel>();
            ChamberReadouts = new ObservableCollection<ReadoutViewModel>();
            AuxiliaryReadouts = new ObservableCollection<ReadoutViewModel>();
            PortDiagnostics = new ObservableCollection<ReadoutViewModel>();
            ActiveAlarms = new ObservableCollection<AlarmRowViewModel>();
            TickerAlarms = new ObservableCollection<AlarmRowViewModel>();
            TrendSeriesList = new ObservableCollection<TrendSeries>();
            TrendMarkers = new ObservableCollection<TrendMarker>();

            SelectModeCommand = new RelayCommand(OnSelectMode);
            AckAllCommand = new RelayCommand(OnAckAll, o => UnacknowledgedCount > 0);

            BuildStructure();
            SeedHistory();
            BuildAlarms();
            ApplyMode();
            Refresh();
        }

        #region 정적 구성

        /// <summary>체인 카드 5개.</summary>
        public ObservableCollection<ChainCardViewModel> Chains { get; private set; }

        /// <summary>센서 1 게이지 3개(S1-1 ~ S1-3).</summary>
        public ObservableCollection<GaugeViewModel> Sensor1Gauges { get; private set; }

        /// <summary>챔버 정보(MFC, 온도).</summary>
        public ObservableCollection<ReadoutViewModel> ChamberReadouts { get; private set; }

        /// <summary>우측 패널 보조 상태(풍속, 온습도, MFC).</summary>
        public ObservableCollection<ReadoutViewModel> AuxiliaryReadouts { get; private set; }

        /// <summary>
        /// 포트별 폴링 사이클 실측값.
        /// DESIGN.md 2.2(B) 의 100ms 목표 달성 여부를 현장에서 판정하는 유일한 수단이므로
        /// 반드시 상시 노출한다.
        /// </summary>
        public ObservableCollection<ReadoutViewModel> PortDiagnostics { get; private set; }

        /// <summary>우측 패널 활성 알람 목록.</summary>
        public ObservableCollection<AlarmRowViewModel> ActiveAlarms { get; private set; }

        /// <summary>상단 알람 티커(최대 3건).</summary>
        public ObservableCollection<AlarmRowViewModel> TickerAlarms { get; private set; }

        /// <summary>트렌드 채널 5개.</summary>
        public ObservableCollection<TrendSeries> TrendSeriesList { get; private set; }

        /// <summary>트렌드 알람 이벤트 마커.</summary>
        public ObservableCollection<TrendMarker> TrendMarkers { get; private set; }

        /// <summary>장비명.</summary>
        public string EquipmentName
        {
            get { return "EFEM CHAMBER"; }
        }

        /// <summary>장비 부제.</summary>
        public string EquipmentSubtitle
        {
            get { return "CH2 · 양압 영역"; }
        }

        /// <summary>FFU 상태 표시.</summary>
        public string FfuStatus
        {
            get { return "FFU 1,240 RPM"; }
        }

        /// <summary>제품명(좌상단).</summary>
        public string BrandName
        {
            get { return "DSE TECH"; }
        }

        /// <summary>버전 표기.</summary>
        public string BrandVersion
        {
            get { return "ESAM HMI v1.4.6"; }
        }

        /// <summary>장비 운전 상태 표기.</summary>
        /// <remarks>
        /// 상태머신의 단계를 그대로 보여준다. 고정 문자열을 두면 인터록이 걸려
        /// 정지한 장비가 화면에는 계속 <c>AUTO RUN</c> 으로 보인다.
        /// </remarks>
        public string EquipmentState
        {
            get
            {
                if (_runtime == null)
                {
                    return "DESIGN";
                }

                switch (_runtime.Engine.StateMachine.Phase)
                {
                    case SystemPhase.Idle: return "IDLE";
                    case SystemPhase.Init: return "INIT";
                    case SystemPhase.ValveHoming: return "HOMING";
                    case SystemPhase.Ready: return "READY";
                    case SystemPhase.AutoControl: return "AUTO RUN";
                    case SystemPhase.Interlocked: return "INTERLOCK";
                    case SystemPhase.SafeStop: return "SAFE STOP";
                    case SystemPhase.Fault: return "FAULT";
                    default: return "UNKNOWN";
                }
            }
        }

        /// <summary>외부 모니터링(FDC) 연결 상태 표기.</summary>
        /// <remarks>
        /// SECS/GEM 모듈은 아직 없다(S7 범위 밖). 연결된 척 <c>ONLINE</c> 을 표시하면
        /// 상위 보고가 되고 있다고 오판하므로 미구현임을 그대로 드러낸다.
        /// </remarks>
        public string HostState
        {
            get { return "GEM N/A"; }
        }

        #endregion

        #region 동적 상태

        /// <summary>적용 중인 제어 기준 모드.</summary>
        public DashboardMode Mode
        {
            get { return _mode; }
            set
            {
                if (Set(ref _mode, value))
                {
                    ApplyMode();
                    Refresh();
                    Raise("IsSensor1");
                    Raise("IsSensor2");
                    Raise("IsSensor3");
                }
            }
        }

        /// <summary>센서 1 모드 선택 여부(라디오 버튼 바인딩).</summary>
        public bool IsSensor1
        {
            get { return _mode == DashboardMode.Sensor1; }
        }

        /// <summary>센서 2 모드 선택 여부.</summary>
        public bool IsSensor2
        {
            get { return _mode == DashboardMode.Sensor2; }
        }

        /// <summary>센서 3 모드 선택 여부.</summary>
        public bool IsSensor3
        {
            get { return _mode == DashboardMode.Sensor3; }
        }

        /// <summary>알람 표시 여부. 검토용 토글이며 실제 운전에서는 항상 true 다.</summary>
        public bool ShowAlarms
        {
            get { return _showAlarms; }
            set
            {
                if (Set(ref _showAlarms, value))
                {
                    BuildAlarms();
                    Refresh();
                }
            }
        }

        /// <summary>현재 시각 표시.</summary>
        public string Clock
        {
            get { return _clock; }
            private set { Set(ref _clock, value); }
        }

        /// <summary>배너 제목(ALARM 3 / NORMAL).</summary>
        public string BannerTitle
        {
            get { return _bannerTitle; }
            private set { Set(ref _bannerTitle, value); }
        }

        /// <summary>배너 부제(UNACK 2 / NO ALARM).</summary>
        public string BannerSubtitle
        {
            get { return _bannerSubtitle; }
            private set { Set(ref _bannerSubtitle, value); }
        }

        /// <summary>배너 강조색.</summary>
        public Brush BannerBrush
        {
            get { return _bannerBrush; }
            private set { Set(ref _bannerBrush, value); }
        }

        /// <summary>배너 테두리색.</summary>
        public Brush BannerBorderBrush
        {
            get { return _bannerBorderBrush; }
            private set { Set(ref _bannerBorderBrush, value); }
        }

        /// <summary>활성 알람 수.</summary>
        public int AlarmCount
        {
            get { return ActiveAlarms.Count; }
        }

        /// <summary>미확인 알람 수.</summary>
        public int UnacknowledgedCount
        {
            get
            {
                int count = 0;

                foreach (AlarmRowViewModel alarm in ActiveAlarms)
                {
                    if (!alarm.IsAcknowledged)
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        /// <summary>상단 알람 카운터 표시.</summary>
        public string AlarmCounterText
        {
            get
            {
                return AlarmCount == 0
                    ? "NO ALARM"
                    : "ALARM " + AlarmCount.ToString(CultureInfo.InvariantCulture);
            }
        }

        /// <summary>상단 알람 카운터 색상.</summary>
        public Brush AlarmCounterBrush
        {
            get { return AlarmCount == 0 ? HmiPalette.Ok : HmiPalette.Bad; }
        }

        /// <summary>트렌드 캡션(모드 라벨 + 샘플링 정보).</summary>
        public string TrendCaption
        {
            get { return _trendCaption; }
            private set { Set(ref _trendCaption, value); }
        }

        /// <summary>트렌드 Y 축 하한.</summary>
        public double TrendMinimum
        {
            get { return _trendMinimum; }
            private set { Set(ref _trendMinimum, value); }
        }

        /// <summary>트렌드 Y 축 상한.</summary>
        public double TrendMaximum
        {
            get { return _trendMaximum; }
            private set { Set(ref _trendMaximum, value); }
        }

        /// <summary>트렌드 정상 대역 하한.</summary>
        public double TrendBandLow
        {
            get { return _trendBandLow; }
            private set { Set(ref _trendBandLow, value); }
        }

        /// <summary>트렌드 정상 대역 상한.</summary>
        public double TrendBandHigh
        {
            get { return _trendBandHigh; }
            private set { Set(ref _trendBandHigh, value); }
        }

        /// <summary>트렌드 목표값.</summary>
        public double TrendSetpoint
        {
            get { return _trendSetpoint; }
            private set { Set(ref _trendSetpoint, value); }
        }

        /// <summary>트렌드 Y 축 라벨 소수점 자리수.</summary>
        public int TrendDecimals
        {
            get { return _trendDecimals; }
            private set { Set(ref _trendDecimals, value); }
        }

        /// <summary>
        /// 트렌드 재렌더링 트리거. 갱신마다 1 증가한다.
        /// </summary>
        /// <remarks>
        /// 트렌드 데이터는 같은 <c>List</c> 인스턴스의 내용만 바뀌므로,
        /// 참조 타입 의존 속성인 <c>TrendChart.Series</c> 는 변경을 감지하지 못한다.
        /// 값 타입인 이 속성이 바뀔 때 <c>AffectsRender</c> 가 발동해 차트가 다시 그려진다.
        /// </remarks>
        public long TrendRevision
        {
            get { return _trendRevision; }
            private set { Set(ref _trendRevision, value); }
        }

        /// <summary>제어 모드 전환 커맨드. 파라미터는 <see cref="DashboardMode"/> 이름 문자열.</summary>
        public ICommand SelectModeCommand { get; private set; }

        /// <summary>전체 알람 확인 커맨드.</summary>
        public ICommand AckAllCommand { get; private set; }

        #endregion

        /// <summary>
        /// 실시간 갱신을 시작한다. 화면이 로드된 뒤 호출한다.
        /// </summary>
        /// <remarks>
        /// 실제 시스템에서는 이 타이머가 DataStore 스냅샷을 끌어오는 주기가 된다.
        /// UI 스레드에서 값을 밀어넣지 않고 <b>끌어오는(pull)</b> 구조여야
        /// 통신 스레드가 UI 를 붙잡지 않는다(DESIGN.md 3.2).
        /// </remarks>
        public void Start()
        {
            if (_timer != null)
            {
                return;
            }

            _timer = new DispatcherTimer(DispatcherPriority.Render);

            // 화면 갱신은 사람이 인지할 수 있는 한계인 10 FPS 로 제한한다.
            // 제어 루프(200ms)보다 빠르게 그릴 이유가 없고, 과도한 갱신은 UI 스레드만 낭비한다.
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

        /// <summary>타이머 콜백.</summary>
        private void OnTick(object sender, EventArgs e)
        {
            Sample();
            BuildAlarms();
            ApplyMode();
            Refresh();

            Raise("EquipmentState");
            Raise("HostState");
        }

        /// <summary>체인 카드·게이지·트렌드 채널의 구조를 만든다.</summary>
        private void BuildStructure()
        {
            for (int i = 0; i < 5; i++)
            {
                Chains.Add(new ChainCardViewModel(i + 1));

                TrendSeries series = new TrendSeries();
                series.Label = "2-" + (i + 1).ToString(CultureInfo.InvariantCulture);
                series.Stroke = HmiPalette.ChainLines[i];
                series.Values = new List<double>();
                TrendSeriesList.Add(series);
            }

            for (int i = 0; i < 3; i++)
            {
                GaugeViewModel gauge = new GaugeViewModel();
                gauge.Title = "센서 1-" + (i + 1).ToString(CultureInfo.InvariantCulture);
                gauge.UnitText = "Pa";
                gauge.ShowBand = true;
                gauge.DetailSecondary = "차압센서 WTDM-550 · SLAVE "
                    + (i + 1).ToString(CultureInfo.InvariantCulture);
                Sensor1Gauges.Add(gauge);
            }

            ChamberReadouts.Add(new ReadoutViewModel("MFC 1", "12.4", "slm"));
            ChamberReadouts.Add(new ReadoutViewModel("MFC 2", "- -", "slm"));
            ChamberReadouts.Add(new ReadoutViewModel("Temp (EFEM)", "30.4", "°C", true));

            AuxiliaryReadouts.Add(new ReadoutViewModel("풍속 1", "0.42", "m/s"));
            AuxiliaryReadouts.Add(new ReadoutViewModel("풍속 2", "0.44", "m/s"));
            AuxiliaryReadouts.Add(new ReadoutViewModel("풍속 3", "0.45", "m/s"));
            AuxiliaryReadouts.Add(new ReadoutViewModel("Temp (EFEM)", "30.4", "°C", true));
            AuxiliaryReadouts.Add(new ReadoutViewModel("Humidity", "41.2", "%RH"));
            AuxiliaryReadouts.Add(new ReadoutViewModel("Temp (C/BOX)", "31.8", "°C"));
            AuxiliaryReadouts.Add(new ReadoutViewModel("MFC 1", "12.4", "slm"));

            // S3 시뮬레이션 실측값. BUS_A 가 100ms 목표를 넘는 것이 한눈에 보여야 한다.
            PortDiagnostics.Add(new ReadoutViewModel("BUS_A", "218", "ms", true));
            PortDiagnostics.Add(new ReadoutViewModel("BUS_B", "124", "ms"));
            PortDiagnostics.Add(new ReadoutViewModel("BUS_C", "119", "ms"));
        }

        /// <summary>시뮬레이터 이력을 초기화한다.</summary>
        private void SeedHistory()
        {
            for (int i = 0; i < 5; i++)
            {
                double[] h1 = new double[HistoryLength];
                double[] h2 = new double[HistoryLength];
                double[] h3 = new double[HistoryLength];

                double a = Targets[i][0];
                double b = Targets[i][1];
                double c = Targets[i][2];

                for (int k = 0; k < HistoryLength; k++)
                {
                    a += ((Targets[i][0] - a) * 0.12) + (Math.Sin((k * 0.19) + i) * 0.22);
                    b += ((Targets[i][1] - b) * 0.10) + (Math.Sin((k * 0.13) + i) * 1.9);
                    c += ((Targets[i][2] - c) * 0.12) + (Math.Sin((k * 0.11) + i) * 5.5);

                    h1[k] = a;
                    h2[k] = b;
                    h3[k] = c;
                }

                _history1.Add(h1);
                _history2.Add(h2);
                _history3.Add(h3);

                _valve[i] = 38.0 + (i * 7.5);
                _fan[i] = 1800.0 + (i * 95.0);
                _fanTemp[i] = 36.0 + (i * 1.1);
            }
        }

        /// <summary>
        /// 런타임에서 현재 값을 한 표본 끌어와 이력에 넣는다.
        /// </summary>
        /// <remarks>
        /// 런타임이 없으면 아무것도 하지 않는다. 디자인타임에는 값이 멈춰 있는 것이
        /// 맞고, 가짜 값을 움직이면 <b>화면이 살아 있는데 통신은 죽은 상태</b>를
        /// 구분할 수 없게 된다. 실장비에서 가장 위험한 오판이다.
        /// </remarks>
        private void Sample()
        {
            if (_runtime == null)
            {
                return;
            }

            SystemSnapshot snapshot = _runtime.Store.Current;

            for (int i = 0; i < 5; i++)
            {
                ChainDefinition chain = ChainAt(i);

                Push(_history1[i], ReadPressure(snapshot, Sensor1IdFor(i)));
                Push(_history2[i], ReadPressure(snapshot, chain == null ? null : chain.Sensor2Id));
                Push(_history3[i], ReadPressure(snapshot, chain == null ? null : chain.Sensor3Id));

                ValveState valve = chain == null ? null : snapshot.FindValve(chain.ValveId);
                FanState fan = chain == null ? null : snapshot.FindFan(chain.FanId);

                if (valve != null && valve.Quality == Quality.Good)
                {
                    _valve[i] = valve.PositionPercent;
                }

                if (fan != null && fan.Quality == Quality.Good)
                {
                    _fan[i] = fan.Rpm;
                }

                double? temp = snapshot.Auxiliary == null
                    ? null
                    : (i < snapshot.Auxiliary.FanTemperatures.Count
                        ? snapshot.Auxiliary.FanTemperatures[i]
                        : null);

                if (temp.HasValue)
                {
                    _fanTemp[i] = temp.Value;
                }
            }
        }

        /// <summary>지정 센서의 압력을 읽는다. 읽을 수 없으면 null.</summary>
        /// <param name="snapshot">스냅샷.</param>
        /// <param name="sensorId">센서 디바이스 ID.</param>
        /// <returns>압력 [Pa]. 품질이 나쁘면 null.</returns>
        /// <remarks>
        /// <b>품질이 나쁜 값을 그래프에 그리지 않는다.</b> 통신이 끊긴 센서의 마지막 값을
        /// 계속 이어 그리면 선이 평평하게 유지되어 정상으로 보인다.
        /// 값을 넣지 않으면 이력이 멈추고, 그 멈춤 자체가 신호가 된다.
        /// </remarks>
        private static double? ReadPressure(SystemSnapshot snapshot, string sensorId)
        {
            if (snapshot == null || string.IsNullOrEmpty(sensorId))
            {
                return null;
            }

            PressureReading reading = snapshot.FindPressure(sensorId);

            if (reading == null || reading.Quality != Quality.Good)
            {
                return null;
            }

            return reading.Pa;
        }

        /// <summary>이력 배열을 한 칸 밀고 새 표본을 넣는다.</summary>
        /// <param name="history">이력 배열.</param>
        /// <param name="value">새 표본. null 이면 직전 값을 유지한다.</param>
        private static void Push(double[] history, double? value)
        {
            // 링버퍼 대신 시프트를 쓰는 이유: 120칸 × 5체인이면 시프트 비용이 무시할 수준이고,
            // 트렌드 컨트롤이 인덱스 순서를 그대로 시간순으로 해석할 수 있어 코드가 단순해진다.
            double last = history[history.Length - 1];
            Array.Copy(history, 1, history, 0, history.Length - 1);
            history[history.Length - 1] = value ?? last;
        }

        /// <summary>인덱스에 해당하는 체인 정의를 반환한다. 없으면 null.</summary>
        /// <param name="index">체인 인덱스(0~4).</param>
        /// <returns>체인 정의.</returns>
        private ChainDefinition ChainAt(int index)
        {
            if (_runtime == null || _runtime.Control.Chains == null
                || index < 0 || index >= _runtime.Control.Chains.Count)
            {
                return null;
            }

            return _runtime.Control.Chains[index];
        }

        /// <summary>센서 1 은 3곳에만 설치되므로 인덱스를 그대로 쓸 수 없다.</summary>
        /// <param name="index">체인 인덱스(0~4).</param>
        /// <returns>센서 1 디바이스 ID.</returns>
        private static string Sensor1IdFor(int index)
        {
            // S1-1(EC) · S1-2(SL) · S1-3(SR). 통로 4·5 는 대응 센서가 없어
            // 가장 가까운 것을 재사용한다. 실제 판정은 ControlConfig.Sensor1Reference 가 정한다.
            return "S1-" + ((index % 3) + 1).ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>모드에 따른 목표값·대역·축 범위를 적용한다.</summary>
        private void ApplyMode()
        {
            double setpoint;
            double band;
            int decimals;
            string label;
            string sensorLabel;

            switch (_mode)
            {
                case DashboardMode.Sensor1:
                    decimals = 1;
                    label = "Sensor 1 (EFEM 내부 차압)";
                    sensorLabel = "센서 1 (PV)";
                    break;

                case DashboardMode.Sensor3:
                    decimals = 0;
                    label = "Sensor 3 (배기 차압)";
                    sensorLabel = "센서 3 (PV)";
                    break;

                default:
                    decimals = 1;
                    label = "Sensor 2 (체인 차압)";
                    sensorLabel = "센서 2 (PV)";
                    break;
            }

            // ★ 목표값과 대역은 설정에서 가져온다.
            //
            // 종전에는 여기 숫자를 적어 두었다. 그러면 작업자가 Config 화면에서 설정을
            // 바꿔도 게이지 눈금과 대역 표시는 옛 값으로 남는다. 실제 제어는 새 값으로
            // 도는데 화면은 아니라서, 대역 안에 있는 값이 대역 밖으로 보인다.
            ResolveTargets(out setpoint, out band);

            Setpoint = setpoint;
            Band = band;
            Decimals = decimals;
            SensorLabel = sensorLabel;

            TrendSetpoint = setpoint;
            TrendBandLow = setpoint - band;
            TrendBandHigh = setpoint + band;
            TrendMinimum = setpoint - (band * TrendSpanFactor);
            TrendMaximum = setpoint + (band * TrendSpanFactor);
            TrendDecimals = decimals;

            // 캡션의 숫자는 현장 엔지니어가 사양으로 읽는다. 실제 설정값에서 계산해
            // 코드와 화면이 어긋나지 않게 한다.
            TrendCaption = string.Format(
                CultureInfo.InvariantCulture,
                "{0} · 최근 {1} s · {2} ms sampling",
                label,
                HistoryLength * RefreshIntervalMs / 1000,
                RefreshIntervalMs);
        }

        /// <summary>
        /// 현재 모드의 목표값과 대역 폭을 설정에서 가져온다.
        /// </summary>
        /// <param name="setpoint">목표값 [Pa](출력).</param>
        /// <param name="band">대역 폭 [Pa](출력).</param>
        /// <remarks>
        /// <para>레시피는 센서별 값을 갖는다. 화면의 축은 하나이므로 대표값이 필요하고,
        /// 현재 모드의 <b>첫 번째 센서</b>를 대표로 쓴다. 통로마다 설정이 다르면
        /// 축이 통로별로 달라져야 하는데, 그러면 5개 카드를 나란히 비교할 수 없다.</para>
        /// <para>대역은 상하한 중 넓은 쪽을 쓴다. 좁은 쪽을 쓰면 반대쪽 이탈이
        /// 축 밖으로 나가 보이지 않는다.</para>
        /// <para>런타임이 없으면 디자인타임 표본값을 쓴다.</para>
        /// </remarks>
        private void ResolveTargets(out double setpoint, out double band)
        {
            SensorMode mode = ToSensorMode(_mode);

            if (_runtime == null)
            {
                // 디자인타임 표본. 화면 배치를 확인할 수 있을 정도의 값이면 된다.
                setpoint = mode == SensorMode.Sensor1 ? 6.0 : (mode == SensorMode.Sensor3 ? -200.0 : -10.0);
                band = mode == SensorMode.Sensor1 ? 2.0 : (mode == SensorMode.Sensor3 ? 100.0 : 30.0);
                return;
            }

            ModeSetting setting = null;

            try
            {
                setting = _runtime.Control.GetSetting(RepresentativeSensorId(mode), mode);
            }
            catch (InvalidOperationException)
            {
                // 해당 모드의 공통 설정이 없는 구성. 표시를 위해 계속 진행한다.
                // 자동 운전 진입은 ControlEngine 이 별도로 막는다.
            }

            if (setting == null)
            {
                setpoint = 0.0;
                band = 100.0;
                return;
            }

            setpoint = setting.SetpointPa;

            double upper = setting.HighLimitPa - setting.SetpointPa;
            double lower = setting.SetpointPa - setting.LowLimitPa;

            band = upper > lower ? upper : lower;

            if (band <= 0.0)
            {
                // 대역이 0 이면 눈금 계산이 0 으로 나눠진다.
                band = 1.0;
            }
        }

        /// <summary>화면 모드를 도메인 센서 모드로 바꾼다.</summary>
        /// <param name="mode">화면 모드.</param>
        /// <returns>센서 모드.</returns>
        private static SensorMode ToSensorMode(DashboardMode mode)
        {
            switch (mode)
            {
                case DashboardMode.Sensor1: return SensorMode.Sensor1;
                case DashboardMode.Sensor3: return SensorMode.Sensor3;
                default: return SensorMode.Sensor2;
            }
        }

        /// <summary>모드의 대표 센서 ID 를 반환한다.</summary>
        /// <param name="mode">센서 모드.</param>
        /// <returns>디바이스 ID.</returns>
        private static string RepresentativeSensorId(SensorMode mode)
        {
            switch (mode)
            {
                case SensorMode.Sensor1: return "S1-1";
                case SensorMode.Sensor3: return "S3-1";
                default: return "S2-1";
            }
        }

        /// <summary>적용 중인 목표값.</summary>
        private double Setpoint { get; set; }

        /// <summary>적용 중인 대역 폭.</summary>
        private double Band { get; set; }

        /// <summary>표시 소수점 자리수.</summary>
        private int Decimals { get; set; }

        /// <summary>센서 게이지 제목.</summary>
        public string SensorLabel { get; private set; }

        /// <summary>모드에 해당하는 이력을 반환한다.</summary>
        /// <param name="chainIndex">체인 인덱스(0~4).</param>
        /// <returns>이력 배열.</returns>
        private double[] HistoryFor(int chainIndex)
        {
            switch (_mode)
            {
                case DashboardMode.Sensor1:
                    return _history1[chainIndex];

                case DashboardMode.Sensor3:
                    return _history3[chainIndex];

                default:
                    return _history2[chainIndex];
            }
        }

        /// <summary>모든 표시값을 현재 상태로 갱신한다.</summary>
        private void Refresh()
        {
            double gaugeLow = Setpoint - (Band * GaugeSpanFactor);
            double gaugeHigh = Setpoint + (Band * GaugeSpanFactor);
            double gaugeSpan = gaugeHigh - gaugeLow;

            double bandStart = (Setpoint - Band - gaugeLow) / gaugeSpan;
            double bandEnd = (Setpoint + Band - gaugeLow) / gaugeSpan;

            for (int i = 0; i < Chains.Count; i++)
            {
                ChainCardViewModel card = Chains[i];
                double[] history = HistoryFor(i);
                double pv = history[history.Length - 1];
                double deviation = pv - Setpoint;

                bool inBand = Math.Abs(deviation) <= Band;
                bool nearBand = Math.Abs(deviation) <= Band * 1.25;

                // 확정 알람이 걸린 체인은 계측값이 대역 안이어도 정상으로 표시하지 않는다.
                // 하단에 빨간 알람 바가 켜진 카드가 상단에 NORMAL 을 달고 있으면
                // 운전자가 상황을 오판한다.
                Brush statusBrush;
                string statusText;

                if (card.HasCriticalAlarm)
                {
                    statusBrush = HmiPalette.Bad;
                    statusText = "ALARM";
                }
                else if (inBand)
                {
                    statusBrush = HmiPalette.Ok;
                    statusText = "NORMAL";
                }
                else if (nearBand)
                {
                    statusBrush = HmiPalette.Warn;
                    statusText = "DEVIATING";
                }
                else
                {
                    statusBrush = HmiPalette.Bad;
                    statusText = "OUT OF BAND";
                }

                card.StatusText = statusText;
                card.StatusBrush = statusBrush;
                card.AccentBrush = statusBrush;

                // ── 센서 게이지 ─────────────────────────────────────────────────
                GaugeViewModel sensor = card.Sensor;
                sensor.Title = SensorLabel;
                sensor.UnitText = "Pa";
                sensor.ShowBand = true;
                sensor.BandStart = bandStart;
                sensor.BandEnd = bandEnd;
                sensor.Ratio = (pv - gaugeLow) / gaugeSpan;
                sensor.ValueText = Format(pv, Decimals);

                // 아크는 계측값 기준(대역 이탈 여부)만 반영한다.
                // 알람 때문에 아크까지 빨개지면 "지금 압력이 대역 안인가"를 읽을 수 없다.
                sensor.ArcBrush = inBand ? HmiPalette.Ok : (nearBand ? HmiPalette.Warn : HmiPalette.Bad);
                sensor.ValueBrush = HmiPalette.TextPrimary;
                sensor.DetailPrefix = "DEV";

                // 표시 자리수로 반올림한 뒤 부호를 판단한다.
                // 그러지 않으면 -0.04 가 "-0.0" 으로 나와 음의 편차처럼 보인다.
                double shownDeviation = Math.Round(deviation, Decimals);
                sensor.DetailValue = (shownDeviation >= 0.0 ? "+" : string.Empty)
                    + Format(shownDeviation, Decimals);
                sensor.DetailValueBrush = inBand ? HmiPalette.OkSoft : HmiPalette.BadSoft;
                sensor.DetailSecondary = string.Format(
                    CultureInfo.InvariantCulture,
                    "SP {0} Pa ± {1} Pa", Format(Setpoint, 0), Format(Band, 0));

                // ── 밸브 게이지 ─────────────────────────────────────────────────
                GaugeViewModel valve = card.Valve;
                valve.Title = string.Format(
                    CultureInfo.InvariantCulture, "V-{0} 스로틀밸브", i + 1);
                valve.UnitText = "% OPEN";
                valve.ShowBand = false;
                valve.Ratio = _valve[i] / 100.0;
                valve.ValueText = Format(_valve[i], 1);
                valve.ArcBrush = HmiPalette.Accent;
                valve.ValueBrush = HmiPalette.TextPrimary;
                valve.DetailPrefix = string.Empty;
                valve.DetailValue = string.Format(
                    CultureInfo.InvariantCulture, "{0}° / 90°", Format(_valve[i] / 100.0 * 90.0, 1));
                valve.DetailValueBrush = HmiPalette.TextPrimary;
                valve.DetailSecondary = string.Format(
                    CultureInfo.InvariantCulture,
                    "{0} pulse", Format(Math.Round(_valve[i] / 100.0 * 5000.0), 0));

                // ── 팬 게이지 ───────────────────────────────────────────────────
                GaugeViewModel fan = card.Fan;
                fan.Title = string.Format(CultureInfo.InvariantCulture, "F-{0} 송풍팬", i + 1);
                fan.UnitText = "RPM";
                fan.ShowBand = false;
                fan.Ratio = _fan[i] / 3000.0;
                fan.ValueText = Format(_fan[i], 0);
                fan.ArcBrush = HmiPalette.Ok;
                fan.ValueBrush = HmiPalette.TextPrimary;
                fan.DetailPrefix = string.Empty;
                fan.DetailValue = string.Format(
                    CultureInfo.InvariantCulture,
                    "센서 3 · {0} Pa", Format(_history3[i][HistoryLength - 1], 0));
                fan.DetailValueBrush = HmiPalette.TextSecondary;
                fan.DetailSecondary = string.Format(
                    CultureInfo.InvariantCulture, "DRIVER {0} °C", Format(_fanTemp[i], 1));

                // ── 트렌드 채널 ─────────────────────────────────────────────────
                List<double> values = (List<double>)TrendSeriesList[i].Values;
                values.Clear();
                values.AddRange(history);
            }

            RefreshSensor1Gauges();

            Clock = DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture)
                + "  " + DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

            // 트렌드는 컬렉션 내용만 바뀌므로 이 값을 올려야 실제로 다시 그려진다.
            TrendRevision = _trendRevision + 1;

            Raise("AlarmCounterText");
            Raise("AlarmCounterBrush");
        }

        /// <summary>센서 1 게이지 3개를 갱신한다. 이 게이지는 항상 센서 1 기준을 보여준다.</summary>
        private void RefreshSensor1Gauges()
        {
            // 센서 1 은 EFEM 내부 압력이라 제어 모드와 무관하게 항상 6 Pa ± 2 기준으로 표시한다.
            // 모드를 바꿔도 챔버 상태 판단 기준이 흔들리면 안 되기 때문이다.
            const double sensor1Setpoint = 6.0;
            const double sensor1Band = 2.0;

            double low = sensor1Setpoint - (sensor1Band * GaugeSpanFactor);
            double span = sensor1Band * GaugeSpanFactor * 2.0;
            double start = (sensor1Setpoint - sensor1Band - low) / span;
            double end = (sensor1Setpoint + sensor1Band - low) / span;

            for (int i = 0; i < Sensor1Gauges.Count; i++)
            {
                double pv = _history1[i][HistoryLength - 1];
                bool inBand = Math.Abs(pv - sensor1Setpoint) <= sensor1Band;

                GaugeViewModel gauge = Sensor1Gauges[i];
                gauge.Ratio = (pv - low) / span;
                gauge.BandStart = start;
                gauge.BandEnd = end;
                gauge.ValueText = Format(pv, 1);
                gauge.ArcBrush = inBand ? HmiPalette.Ok : HmiPalette.Bad;
                gauge.DetailPrefix = inBand ? "NORMAL" : "OUT";
                gauge.DetailValue = "SP +6 Pa ±2";
                gauge.DetailValueBrush = inBand ? HmiPalette.OkSoft : HmiPalette.BadSoft;
            }
        }

        /// <summary>알람 목록을 구성한다.</summary>
        private void BuildAlarms()
        {
            if (_runtime == null || _runtime.Alarms == null)
            {
                BuildDesignTimeAlarms();
                return;
            }

            ActiveAlarms.Clear();
            TickerAlarms.Clear();

            foreach (ChainCardViewModel card in Chains)
            {
                card.SetNoAlarm();
            }

            int unacknowledged = 0;
            AlarmSeverity worst = AlarmSeverity.Warning;
            bool anyActive = false;

            // 활성 알람만 나열한다. 이력은 History 화면(S8)의 몫이다.
            foreach (AlarmState state in EnumerateActive())
            {
                anyActive = true;

                if (!state.IsAcknowledged)
                {
                    unacknowledged++;
                }

                if (state.Rule.Severity > worst)
                {
                    worst = state.Rule.Severity;
                }

                bool critical = state.Rule.Severity >= AlarmSeverity.Alarm;

                AlarmRowViewModel row = new AlarmRowViewModel(
                    state.Rule.Code,
                    string.IsNullOrEmpty(state.Rule.MessageKo) ? state.Rule.Name : state.Rule.MessageKo,
                    state.Rule.Severity.ToString().ToUpperInvariant(),
                    DescribeSource(state.Rule.Source),
                    state.RaisedUtc.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture),
                    critical);

                row.IsAcknowledged = state.IsAcknowledged;
                ActiveAlarms.Add(row);

                // 알람을 해당 체인 카드에 붙인다.
                // 카드 하단에 표시되지 않으면 어느 통로 문제인지 목록을 뒤져야 한다.
                int chainIndex = ChainIndexOf(state.Rule.Source);

                if (chainIndex >= 0 && chainIndex < Chains.Count)
                {
                    Chains[chainIndex].SetAlarm(state.Rule.Code, row.Name, critical);
                }

                if (TickerAlarms.Count < 3)
                {
                    TickerAlarms.Add(row);
                }
            }

            BannerTitle = anyActive
                ? worst.ToString().ToUpperInvariant() + " " + ActiveAlarms.Count.ToString(CultureInfo.InvariantCulture)
                : "NORMAL";

            BannerSubtitle = anyActive
                ? "UNACK " + unacknowledged.ToString(CultureInfo.InvariantCulture)
                : "NO ALARM";

            bool bad = anyActive && worst >= AlarmSeverity.Alarm;

            BannerBrush = !anyActive ? HmiPalette.Ok : (bad ? HmiPalette.Bad : HmiPalette.Warn);
            BannerBorderBrush = !anyActive
                ? HmiPalette.BorderNormal
                : (bad ? HmiPalette.BorderBad : HmiPalette.BorderNormal);

            Raise("AlarmCount");
            Raise("AlarmCounterText");
            Raise("AlarmCounterBrush");
            Raise("UnacknowledgedCount");
        }

        /// <summary>활성 알람 상태를 열거한다.</summary>
        /// <returns>활성 알람 목록.</returns>
        private IEnumerable<AlarmState> EnumerateActive()
        {
            List<AlarmState> found = new List<AlarmState>();
            AlarmSummary summary = _runtime.Alarms.Summary;

            if (summary == null)
            {
                return found;
            }

            foreach (string code in summary.ActiveCodes)
            {
                AlarmState state = _runtime.Alarms.FindState(code);

                if (state != null && state.IsActive)
                {
                    found.Add(state);
                }
            }

            return found;
        }

        /// <summary>알람 대상 경로에서 표시용 출처 문자열을 만든다.</summary>
        /// <param name="source">값 경로.</param>
        /// <returns>표시 문자열.</returns>
        private static string DescribeSource(string source)
        {
            if (string.IsNullOrEmpty(source))
            {
                return "SYSTEM";
            }

            string deviceId;

            if (SnapshotValueResolver.TryGetDeviceId(source, out deviceId))
            {
                return deviceId.ToUpperInvariant();
            }

            int colon = source.IndexOf(':');
            return (colon > 0 ? source.Substring(0, colon) : source).ToUpperInvariant();
        }

        /// <summary>알람 대상이 속한 체인 인덱스를 찾는다. 없으면 -1.</summary>
        /// <param name="source">값 경로.</param>
        /// <returns>체인 인덱스(0~4) 또는 -1.</returns>
        private int ChainIndexOf(string source)
        {
            string deviceId;

            if (_runtime == null || !SnapshotValueResolver.TryGetDeviceId(source, out deviceId))
            {
                return -1;
            }

            IList<ChainDefinition> chains = _runtime.Control.Chains;

            if (chains == null)
            {
                return -1;
            }

            for (int i = 0; i < chains.Count; i++)
            {
                ChainDefinition chain = chains[i];

                if (Eq(chain.Sensor2Id, deviceId) || Eq(chain.Sensor3Id, deviceId)
                    || Eq(chain.ValveId, deviceId) || Eq(chain.FanId, deviceId))
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>대소문자를 무시하고 비교한다.</summary>
        /// <param name="a">왼쪽.</param>
        /// <param name="b">오른쪽.</param>
        /// <returns>같으면 true.</returns>
        private static bool Eq(string a, string b)
        {
            return !string.IsNullOrEmpty(a) && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 디자인타임 표본 알람을 만든다. 화면 배치 확인 전용이다.
        /// </summary>
        /// <remarks>
        /// 런타임이 붙으면 절대 호출되지 않는다. 실행 중에 가짜 알람이 보이면
        /// 작업자가 실재하지 않는 사건을 추적하게 된다.
        /// </remarks>
        private void BuildDesignTimeAlarms()
        {
            ActiveAlarms.Clear();
            TickerAlarms.Clear();

            foreach (ChainCardViewModel card in Chains)
            {
                card.SetNoAlarm();
            }

            TrendMarkers.Clear();

            if (_showAlarms)
            {
                ActiveAlarms.Add(new AlarmRowViewModel(
                    "AL-46", "배기 중앙 전면 압력 상한 초과",
                    "ALARM", "S2-1", "10:42:17", true));

                ActiveAlarms.Add(new AlarmRowViewModel(
                    "AL-37", "비상정지 작동",
                    "CRITICAL", "PLC", "10:41:55", true));

                ActiveAlarms.Add(new AlarmRowViewModel(
                    "DG-04", "배기 음압 저하 — 인터록 도달 전 경고",
                    "WARNING", "S3-1", "10:38:02", false));

                ActiveAlarms[2].IsAcknowledged = true;

                foreach (AlarmRowViewModel alarm in ActiveAlarms)
                {
                    TickerAlarms.Add(alarm);
                }

                Chains[0].SetAlarm("AL-46", "배기 중앙 전면 압력 상한 초과", true);
                Chains[3].SetAlarm("DG-04", "배기 음압 저하", false);
            }

            BannerTitle = _showAlarms ? "ALARM 3" : "NORMAL";
            BannerSubtitle = _showAlarms ? "UNACK 2" : "NO ALARM";
            BannerBrush = _showAlarms ? HmiPalette.Bad : HmiPalette.Ok;
            BannerBorderBrush = _showAlarms ? HmiPalette.BorderBad : HmiPalette.BorderNormal;

            Raise("AlarmCount");
            Raise("AlarmCounterText");
            Raise("AlarmCounterBrush");
            Raise("UnacknowledgedCount");
        }

        /// <summary>제어 모드 전환 커맨드 처리.</summary>
        private void OnSelectMode(object parameter)
        {
            string name = parameter as string;

            if (string.IsNullOrEmpty(name))
            {
                return;
            }

            if (name.EndsWith("1", StringComparison.Ordinal))
            {
                Mode = DashboardMode.Sensor1;
            }
            else if (name.EndsWith("3", StringComparison.Ordinal))
            {
                Mode = DashboardMode.Sensor3;
            }
            else
            {
                Mode = DashboardMode.Sensor2;
            }
        }

        /// <summary>전체 알람 확인 처리.</summary>
        private void OnAckAll(object parameter)
        {
            // ★ 화면 목록만 바꾸면 다음 갱신에 되돌아온다.
            // 확인 상태는 AlarmService 가 들고 있으므로 그쪽에 반영해야 한다.
            if (_runtime != null && _runtime.Alarms != null)
            {
                _runtime.Alarms.AcknowledgeAll();
            }

            foreach (AlarmRowViewModel alarm in ActiveAlarms)
            {
                alarm.IsAcknowledged = true;
            }

            BannerSubtitle = "UNACK 0";
            Raise("UnacknowledgedCount");
        }

        /// <summary>천 단위 구분과 소수점 자리수를 적용해 수치를 포맷한다.</summary>
        /// <param name="value">값.</param>
        /// <param name="decimals">소수점 자리수.</param>
        /// <returns>포맷된 문자열.</returns>
        private static string Format(double value, int decimals)
        {
            string pattern = decimals <= 0
                ? "#,0"
                : "#,0." + new string('0', decimals);

            return value.ToString(pattern, CultureInfo.InvariantCulture);
        }

        /// <summary>값을 지정 범위로 제한한다.</summary>
        private static double Clamp(double value, double min, double max)
        {
            if (value < min)
            {
                return min;
            }

            return value > max ? max : value;
        }
    }
}
