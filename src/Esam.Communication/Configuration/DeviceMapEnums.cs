namespace Esam.Communication.Configuration
{
    /// <summary>
    /// 폴링 티어. 갱신 빈도가 다른 항목을 분리해 버스 부하를 줄인다.
    /// </summary>
    /// <remarks>
    /// DESIGN.md 2.2 (B) 안 3의 핵심이다. 차압센서는 제어 입력이라 매 사이클 읽어야 하지만,
    /// 온습도·풍속·파티클은 초 단위로 변하므로 같은 주기로 읽으면 버스만 낭비된다.
    /// </remarks>
    public enum PollingTier
    {
        /// <summary>매 사이클 갱신. 차압센서, 밸브 위치, PLC 안전 입력.</summary>
        Fast = 0,

        /// <summary>중간 주기(기본 1초). 온도, 장치 알람 코드.</summary>
        Medium = 1,

        /// <summary>느린 주기(기본 5초). 온습도, 풍속, 파티클.</summary>
        Slow = 2
    }

    /// <summary>
    /// 레지스터 데이터 타입. device-map.json 의 <c>points[].type</c> 에 대응한다.
    /// </summary>
    public enum PointDataType
    {
        /// <summary>부호 없는 16비트. 밸브 위치, 팬 RPM 등.</summary>
        UInt16 = 0,

        /// <summary>부호 있는 16비트. <b>차압센서 음압값에 필수</b>이다.</summary>
        Int16 = 1,

        /// <summary>부호 없는 32비트(연속 2 레지스터).</summary>
        UInt32 = 2,

        /// <summary>부호 있는 32비트(연속 2 레지스터).</summary>
        Int32 = 3,

        /// <summary>단일 비트. PLC 디지털 입력(D10.0 ~ D10.8)에 사용한다.</summary>
        Bool = 4
    }

    /// <summary>
    /// 32비트 값의 워드 순서. 장치마다 다르므로 설정으로 지정해야 한다(Open Issue #5).
    /// </summary>
    public enum WordOrder
    {
        /// <summary>상위 워드가 먼저(앞 레지스터가 상위 16비트).</summary>
        HighWordFirst = 0,

        /// <summary>하위 워드가 먼저(앞 레지스터가 하위 16비트).</summary>
        LowWordFirst = 1
    }
}
