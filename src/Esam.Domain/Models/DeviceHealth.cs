using System;

namespace Esam.Domain.Models
{
    /// <summary>
    /// 디바이스 1대의 통신 건강 상태.
    /// </summary>
    /// <remarks>
    /// <para>I/O Status 화면의 상태 램프가 이 값을 그린다. 종전에는 그릴 근거가 없었다.
    /// 차압센서·밸브·팬은 각자 <see cref="Models.Quality"/> 를 들고 있지만,
    /// 온습도·풍속·파티클·MFC·FFU 는 <see cref="AuxiliaryReadings"/> 하나에 품질이
    /// 뭉쳐 있어 <b>어느 장치가 죽었는지 구분할 수 없었다.</b> 파티클 하나가 끊겨도
    /// 다섯 램프가 함께 빨개지고, 반대로 하나만 살아 있어도 다섯이 함께 초록이 된다.</para>
    /// <para>램프를 그리려면 "값" 이 아니라 <b>그 값을 준 장치</b> 단위의 상태가 필요하다.
    /// 그래서 값 모델과 별개로 디바이스 단위 상태를 둔다.</para>
    /// <para><b>폴링하지 않는 디바이스도 목록에 남긴다.</b> 빠뜨리면 화면에서
    /// "구성에 아예 없음" 과 "구성에는 있는데 꺼 두었음" 이 똑같이 빈칸으로 보인다.
    /// 커미셔닝에서 이 둘을 혼동하면 멀쩡한 배선을 확인하러 장비를 연다.</para>
    /// </remarks>
    public sealed class DeviceHealth
    {
        /// <summary>디바이스 ID(예: "S1-1", "V-3", "PLC-1").</summary>
        public string DeviceId { get; private set; }

        /// <summary>표시명. 설정에 없으면 null.</summary>
        public string Name { get; private set; }

        /// <summary>드라이버 이름(예: "PressureSensor"). 램프 분류에 쓴다.</summary>
        /// <remarks>
        /// 문자열로 두는 이유는 Domain 이 Communication 을 참조하지 않기 때문이다.
        /// 값의 출처는 <c>device-map.json</c> 의 <c>deviceTypes[*].driver</c> 이다.
        /// </remarks>
        public string Driver { get; private set; }

        /// <summary>소속 포트 ID(예: "CH1"). 포트 단위로 죽었는지 판별할 때 쓴다.</summary>
        public string PortId { get; private set; }

        /// <summary>폴링 대상인지 여부. <c>device-map.json</c> 의 <c>enabled</c>.</summary>
        public bool IsPolled { get; private set; }

        /// <summary>이 디바이스가 제공한 측정점 전체의 품질(가장 나쁜 값).</summary>
        public Quality Quality { get; private set; }

        /// <summary>마지막 갱신 시각(UTC). 한 번도 수신하지 못했으면 <see cref="DateTime.MinValue"/>.</summary>
        public DateTime LastUpdateUtc { get; private set; }

        /// <summary>수집된 측정점 수.</summary>
        public int PointCount { get; private set; }

        /// <summary>그중 품질이 <see cref="Models.Quality.Good"/> 인 측정점 수.</summary>
        public int GoodPointCount { get; private set; }

        /// <summary>
        /// 폴링 대상이면서 모든 측정점이 정상인지 여부.
        /// </summary>
        /// <remarks>
        /// 일부만 정상인 상태를 "정상" 으로 표시하지 않는다. 밸브 위치는 오는데
        /// 알람코드 읽기만 실패하는 경우가 실제로 있고, 그때 램프가 초록이면
        /// 알람을 못 읽고 있다는 사실이 화면 어디에도 남지 않는다.
        /// </remarks>
        public bool IsHealthy
        {
            get
            {
                return IsPolled
                       && PointCount > 0
                       && GoodPointCount == PointCount
                       && Quality == Models.Quality.Good;
            }
        }

        /// <summary>디바이스 건강 상태를 생성한다.</summary>
        /// <param name="deviceId">디바이스 ID.</param>
        /// <param name="name">표시명(null 허용).</param>
        /// <param name="driver">드라이버 이름.</param>
        /// <param name="portId">소속 포트 ID.</param>
        /// <param name="isPolled">폴링 대상인지 여부.</param>
        /// <param name="quality">측정점 전체의 품질(가장 나쁜 값).</param>
        /// <param name="lastUpdateUtc">마지막 갱신 시각(UTC).</param>
        /// <param name="pointCount">수집된 측정점 수.</param>
        /// <param name="goodPointCount">정상 품질인 측정점 수.</param>
        /// <exception cref="ArgumentNullException">디바이스 ID 가 null 일 때.</exception>
        public DeviceHealth(
            string deviceId,
            string name,
            string driver,
            string portId,
            bool isPolled,
            Quality quality,
            DateTime lastUpdateUtc,
            int pointCount,
            int goodPointCount)
        {
            if (deviceId == null)
            {
                throw new ArgumentNullException("deviceId");
            }

            DeviceId = deviceId;
            Name = name;
            Driver = driver;
            PortId = portId;
            IsPolled = isPolled;
            Quality = quality;
            LastUpdateUtc = lastUpdateUtc;
            PointCount = pointCount;
            GoodPointCount = goodPointCount;
        }

        /// <summary>아직 한 번도 수신하지 못한 상태를 만든다.</summary>
        /// <param name="deviceId">디바이스 ID.</param>
        /// <param name="name">표시명(null 허용).</param>
        /// <param name="driver">드라이버 이름.</param>
        /// <param name="portId">소속 포트 ID.</param>
        /// <param name="isPolled">폴링 대상인지 여부.</param>
        /// <returns><see cref="Models.Quality.NoData"/> 상태의 건강 상태.</returns>
        public static DeviceHealth NoData(
            string deviceId, string name, string driver, string portId, bool isPolled)
        {
            return new DeviceHealth(
                deviceId, name, driver, portId, isPolled,
                Quality.NoData, DateTime.MinValue, 0, 0);
        }
    }
}
