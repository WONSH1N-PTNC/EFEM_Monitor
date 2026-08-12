using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Threading;
using Esam.Communication.Configuration;
using Esam.Communication.Diagnostics;
using Esam.Communication.Polling;
using Esam.Domain.Models;
using Esam.Hmi.Infrastructure;
using Esam.Services;

namespace Esam.Hmi.ViewModels
{
    /// <summary>상태 램프가 가리키는 대상.</summary>
    public enum IoLampSource
    {
        /// <summary>FFU.</summary>
        Ffu = 0,

        /// <summary>송풍팬.</summary>
        BlowerFan = 1,

        /// <summary>스로틀밸브.</summary>
        ThrottleValve = 2,

        /// <summary>차압센서.</summary>
        PressureSensor = 3,

        /// <summary>상위(FDC) 통신.</summary>
        Fdc = 4,

        /// <summary>제어박스 냉각팬.</summary>
        CoolingFan = 5,

        /// <summary>제어박스 온도.</summary>
        ControlBoxTemperature = 6,

        /// <summary>EFEM 내부 온도.</summary>
        EfemTemperature = 7,

        /// <summary>EFEM 내부 습도.</summary>
        Humidity = 8,

        /// <summary>파티클.</summary>
        Particle = 9,

        /// <summary>MFC.</summary>
        Mfc = 10,

        /// <summary>풍속.</summary>
        AirVelocity = 11
    }

    /// <summary>상태 램프의 색.</summary>
    /// <remarks>
    /// <b>"정상" 과 "모름" 을 같은 색으로 칠하지 않는다.</b> 구성에 없는 장치와
    /// 응답하지 않는 장치도 구분한다. 커미셔닝에서 이 구분이 없으면
    /// 배선을 확인하러 장비를 여는 일이 생긴다.
    /// </remarks>
    public enum IoLampState
    {
        /// <summary>폴링 중이고 모든 측정점이 정상.</summary>
        Healthy = 0,

        /// <summary>값이 낡았거나 일부 측정점만 정상.</summary>
        Degraded = 1,

        /// <summary>응답 없음 또는 장치 알람.</summary>
        Failed = 2,

        /// <summary>구성에는 있으나 폴링하지 않음.</summary>
        Disabled = 3,

        /// <summary>구성에 없음.</summary>
        NotConfigured = 4,

        /// <summary>기능이 아직 구현되지 않음.</summary>
        NotImplemented = 5
    }

    /// <summary>
    /// I/O Status 화면.
    /// </summary>
    /// <remarks>
    /// <para>S6(실장비 검증)의 <b>판정 수단</b>이다. 커미셔닝 미확정 항목 중
    /// 압력 스케일(0.1 Pa/LSB)과 PLC 입력 비트 극성은 계산이 아니라
    /// <b>값을 눈으로 보고</b> 판정해야 한다.</para>
    /// <para>그래서 이 화면은 가공하지 않은 것을 함께 보여 준다.
    /// 압력은 환산 전 레지스터와 환산 후 Pa 를 나란히, PLC 입력은 설정된 극성과
    /// 현재 판정값을 나란히 놓는다. 둘 중 하나만 보면 어긋난 사실을 알 수 없다.</para>
    /// <para><b>쓰기가 없다.</b> 그래서 쓰기 잠금 관문의 대상이 아니다.</para>
    /// </remarks>
    public sealed class IoStatusViewModel : ObservableObject
    {
        /// <summary>화면 갱신 주기 [ms].</summary>
        /// <remarks>
        /// 대시보드(100 ms)보다 느리다. 램프와 표는 사람이 읽고 판단하는 대상이라
        /// 빠르게 깜빡이면 오히려 읽기 어렵다.
        /// </remarks>
        private const int RefreshIntervalMs = 250;

        private readonly EsamRuntime _runtime;
        private DispatcherTimer _timer;
        private string _snapshotAge;
        private bool _isStale;

        /// <summary>디자인타임용으로 생성한다.</summary>
        public IoStatusViewModel()
            : this(null)
        {
        }

        /// <summary>I/O 상태 화면을 생성한다.</summary>
        /// <param name="runtime">값을 끌어올 런타임. null 이면 디자인타임으로 동작한다.</param>
        public IoStatusViewModel(EsamRuntime runtime)
        {
            _runtime = runtime;

            Lamps = new ObservableCollection<IoLampViewModel>();
            PlcInputs = new ObservableCollection<PlcInputRowViewModel>();
            Pressures = new ObservableCollection<PressureRawRowViewModel>();
            Ports = new ObservableCollection<PortStatusRowViewModel>();

            Rebuild(runtime == null ? null : runtime.Map);
        }

        /// <summary>상태 램프 12종.</summary>
        public ObservableCollection<IoLampViewModel> Lamps { get; private set; }

        /// <summary>PLC 디지털 입력 표.</summary>
        public ObservableCollection<PlcInputRowViewModel> PlcInputs { get; private set; }

        /// <summary>차압센서 원시값 표.</summary>
        public ObservableCollection<PressureRawRowViewModel> Pressures { get; private set; }

        /// <summary>포트별 통신 통계.</summary>
        public ObservableCollection<PortStatusRowViewModel> Ports { get; private set; }

        /// <summary>스냅샷 나이 문구.</summary>
        public string SnapshotAge
        {
            get { return _snapshotAge; }
            private set { Set(ref _snapshotAge, value); }
        }

        /// <summary>스냅샷이 갱신되지 않고 있는지 여부.</summary>
        public bool IsStale
        {
            get { return _isStale; }
            private set { Set(ref _isStale, value); }
        }

        /// <summary>실시간 갱신을 시작한다.</summary>
        public void Start()
        {
            if (_timer != null)
            {
                return;
            }

            _timer = new DispatcherTimer(DispatcherPriority.Render);
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

        /// <summary>
        /// 구성이 바뀌었을 때 표의 뼈대를 다시 만든다.
        /// </summary>
        /// <param name="map">통신 구성. null 이면 램프만 만든다.</param>
        /// <remarks>
        /// 값이 아니라 <b>구조</b>만 만든다. 값은 <see cref="Apply"/> 가 채운다.
        /// 매 갱신마다 컬렉션을 다시 채우면 표 전체가 다시 그려진다(DESIGN §7.4).
        /// </remarks>
        public void Rebuild(DeviceMap map)
        {
            Lamps.Clear();
            PlcInputs.Clear();
            Pressures.Clear();
            Ports.Clear();

            BuildLamps();

            if (map == null)
            {
                return;
            }

            BuildPlcInputs(map);
            BuildPressures(map);
        }

        /// <summary>스냅샷과 포트 통계로 표시값을 갱신한다.</summary>
        /// <param name="snapshot">현재 스냅샷.</param>
        /// <param name="statistics">포트별 통계. null 이면 통신 표를 건드리지 않는다.</param>
        /// <param name="nowUtc">현재 시각(UTC).</param>
        public void Apply(
            SystemSnapshot snapshot, IList<PortStatistics> statistics, DateTime nowUtc)
        {
            if (snapshot == null)
            {
                return;
            }

            foreach (IoLampViewModel lamp in Lamps)
            {
                lamp.Update(snapshot);
            }

            foreach (PlcInputRowViewModel row in PlcInputs)
            {
                row.Update(snapshot.Plc);
            }

            foreach (PressureRawRowViewModel row in Pressures)
            {
                row.Update(snapshot.FindPressure(row.DeviceId));
            }

            ApplyStatistics(statistics);
            ApplyAge(snapshot, nowUtc);
        }

        /// <summary>타이머 콜백.</summary>
        /// <param name="sender">이벤트 발신자.</param>
        /// <param name="e">이벤트 인자.</param>
        private void OnTick(object sender, EventArgs e)
        {
            if (_runtime == null)
            {
                return;
            }

            Apply(_runtime.Store.Current, Statistics(), DateTime.UtcNow);
        }

        /// <summary>워커에서 포트 통계를 모은다.</summary>
        /// <returns>통계 목록. 워커가 없으면 빈 목록.</returns>
        private IList<PortStatistics> Statistics()
        {
            List<PortStatistics> collected = new List<PortStatistics>();

            IList<ModbusPortWorker> workers = _runtime.Workers;

            if (workers == null)
            {
                return collected;
            }

            foreach (ModbusPortWorker worker in workers)
            {
                if (worker.Statistics != null)
                {
                    collected.Add(worker.Statistics);
                }
            }

            return collected;
        }

        /// <summary>통신 통계 표를 갱신한다.</summary>
        /// <param name="statistics">포트별 통계.</param>
        private void ApplyStatistics(IList<PortStatistics> statistics)
        {
            if (statistics == null)
            {
                return;
            }

            if (Ports.Count != statistics.Count)
            {
                Ports.Clear();

                foreach (PortStatistics item in statistics)
                {
                    Ports.Add(new PortStatusRowViewModel(item.PortId));
                }
            }

            for (int i = 0; i < statistics.Count && i < Ports.Count; i++)
            {
                Ports[i].Update(statistics[i]);
            }
        }

        /// <summary>스냅샷 나이를 표시한다.</summary>
        /// <param name="snapshot">스냅샷.</param>
        /// <param name="nowUtc">현재 시각(UTC).</param>
        /// <remarks>
        /// 값이 멈춘 화면과 값이 변하지 않는 공정은 구분되지 않는다.
        /// 나이를 적어 두면 그 둘이 갈린다.
        /// </remarks>
        private void ApplyAge(SystemSnapshot snapshot, DateTime nowUtc)
        {
            double ageMs = (nowUtc - snapshot.TimestampUtc).TotalMilliseconds;

            if (ageMs < 0.0)
            {
                ageMs = 0.0;
            }

            IsStale = ageMs > 2000.0;

            SnapshotAge = string.Format(
                CultureInfo.InvariantCulture, "스냅샷 {0:F0} ms 전", ageMs);
        }

        /// <summary>상태 램프 12종을 만든다.</summary>
        /// <remarks>
        /// 순서는 <c>DESIGN.md</c> §7.2 의 I/O(Status) 항목 ①~⑫ 를 따른다.
        /// 설명서와 화면의 순서가 다르면 대조할 때마다 눈이 헤맨다.
        /// </remarks>
        private void BuildLamps()
        {
            Lamps.Add(new IoLampViewModel("FFU", IoLampSource.Ffu));
            Lamps.Add(new IoLampViewModel("송풍팬", IoLampSource.BlowerFan));
            Lamps.Add(new IoLampViewModel("스로틀밸브", IoLampSource.ThrottleValve));
            Lamps.Add(new IoLampViewModel("차압센서", IoLampSource.PressureSensor));
            Lamps.Add(new IoLampViewModel("FDC", IoLampSource.Fdc));
            Lamps.Add(new IoLampViewModel("쿨링팬", IoLampSource.CoolingFan));
            Lamps.Add(new IoLampViewModel("Temp (C/BOX)", IoLampSource.ControlBoxTemperature));
            Lamps.Add(new IoLampViewModel("Temp (EFEM)", IoLampSource.EfemTemperature));
            Lamps.Add(new IoLampViewModel("Humidity", IoLampSource.Humidity));
            Lamps.Add(new IoLampViewModel("Particle", IoLampSource.Particle));
            Lamps.Add(new IoLampViewModel("MFC", IoLampSource.Mfc));
            Lamps.Add(new IoLampViewModel("풍속", IoLampSource.AirVelocity));
        }

        /// <summary>PLC 디지털 입력 표의 뼈대를 만든다.</summary>
        /// <param name="map">통신 구성.</param>
        /// <remarks>
        /// <b>비트 번호와 극성은 설정에서 읽는다.</b> 화면에 상수로 적으면
        /// 설정을 고쳤을 때 화면만 옛 값을 보여 주고, 극성 판정이 그 화면을 근거로 이뤄진다.
        /// </remarks>
        private void BuildPlcInputs(DeviceMap map)
        {
            foreach (KeyValuePair<string, DeviceTypeDefinition> pair in map.DeviceTypes)
            {
                if (!string.Equals(pair.Value.Driver, PointKeys.DriverPlc, StringComparison.Ordinal))
                {
                    continue;
                }

                foreach (ReadGroupDefinition group in pair.Value.ReadGroups)
                {
                    foreach (PointDefinition point in group.Points)
                    {
                        if (point.Key == null
                            || !point.Key.StartsWith("di.", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        PlcInputs.Add(new PlcInputRowViewModel(point));
                    }
                }
            }

            // 배선이 없어 설정에 선언되지 않은 입력도 행으로 남긴다.
            // 표에 없으면 "확인했더니 없더라" 와 "확인하지 않았다" 가 구분되지 않는다.
            AddUnwired(PointKeys.DiDoor, "도어 열림");
            AddUnwired(PointKeys.DiMainBreaker, "메인 차단기 OFF");
        }

        /// <summary>배선되지 않은 입력 행을 추가한다.</summary>
        /// <param name="key">측정점 키.</param>
        /// <param name="signal">신호 이름.</param>
        private void AddUnwired(string key, string signal)
        {
            foreach (PlcInputRowViewModel existing in PlcInputs)
            {
                if (string.Equals(existing.Key, key, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }

            PlcInputs.Add(PlcInputRowViewModel.Unwired(key, signal));
        }

        /// <summary>차압센서 표의 뼈대를 만든다.</summary>
        /// <param name="map">통신 구성.</param>
        private void BuildPressures(DeviceMap map)
        {
            foreach (DeviceInstanceDefinition device in map.Devices)
            {
                if (device == null || string.IsNullOrEmpty(device.Id))
                {
                    continue;
                }

                DeviceTypeDefinition type = map.FindType(device.Type);

                if (type == null
                    || !string.Equals(
                        type.Driver, PointKeys.DriverPressureSensor, StringComparison.Ordinal))
                {
                    continue;
                }

                PointDefinition point = FindPressurePoint(type);

                Pressures.Add(new PressureRawRowViewModel(
                    device,
                    point == null ? 0.0 : point.Scale,
                    point == null ? 0.0 : point.Bias));
            }
        }

        /// <summary>압력 측정점 정의를 찾는다.</summary>
        /// <param name="type">디바이스 타입 정의.</param>
        /// <returns>측정점 정의. 없으면 null.</returns>
        /// <remarks>
        /// 스케일을 화면에 상수로 적지 않는다. 설정을 고쳤을 때 화면만 옛 값을
        /// 보여 주면, 스케일 확정 작업이 그 화면을 근거로 이뤄진다.
        /// </remarks>
        private static PointDefinition FindPressurePoint(DeviceTypeDefinition type)
        {
            foreach (ReadGroupDefinition group in type.ReadGroups)
            {
                foreach (PointDefinition point in group.Points)
                {
                    if (string.Equals(point.Key, PointKeys.PressurePa, StringComparison.OrdinalIgnoreCase))
                    {
                        return point;
                    }
                }
            }

            return null;
        }
    }
}
