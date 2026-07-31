namespace Esam.Domain.Control
{
    /// <summary>
    /// 압력 제어 기준 센서 모드. ESAM 운용방법 설명자료 p.10~12 의 3개 순서도에 대응한다.
    /// 세 모드는 알고리즘이 동일하고 Setpoint/Band/Time 파라미터만 다르다.
    /// </summary>
    public enum SensorMode
    {
        /// <summary>센서 1 기준(EFEM 내부 압력, 기본 6 Pa ± 2 Pa).</summary>
        Sensor1 = 1,

        /// <summary>센서 2 기준(밸브·팬 직전, 기본 -10 Pa ± 30 Pa).</summary>
        Sensor2 = 2,

        /// <summary>센서 3 기준(배기 후단, 기본 -200 Pa ± 100 Pa).</summary>
        Sensor3 = 3
    }

    /// <summary>
    /// 시스템 전체 운전 단계. DESIGN.md 4.1 상태머신에 대응한다.
    /// </summary>
    public enum SystemPhase
    {
        /// <summary>정지 상태. 통신만 수행하며 액추에이터 지령을 내지 않는다.</summary>
        Idle = 0,

        /// <summary>초기화 중(설정 로드, 통신 확인).</summary>
        Init = 1,

        /// <summary>밸브 원점 복귀 중. 전원 ON 후 반드시 거쳐야 한다.</summary>
        ValveHoming = 2,

        /// <summary>자동 운전 준비 완료. 수동 조작 가능.</summary>
        Ready = 3,

        /// <summary>자동 압력 제어 수행 중.</summary>
        AutoControl = 4,

        /// <summary>인터록 발동 상태. 액추에이터가 안전 위치로 고정된다.</summary>
        Interlocked = 5,

        /// <summary>장애 상태. 권한 있는 사용자의 Reset 이 필요하다.</summary>
        Fault = 6,

        /// <summary>비상정지(EMO/차단기). 최우선 상태이며 물리 조건 해제 후에만 복귀 가능하다.</summary>
        SafeStop = 7
    }

    /// <summary>상태머신 전이를 유발하는 사건.</summary>
    public enum SystemTrigger
    {
        /// <summary>사용자가 시작을 요청.</summary>
        Start = 0,

        /// <summary>초기화 성공.</summary>
        InitCompleted = 1,

        /// <summary>밸브 원점 복귀 완료.</summary>
        HomingCompleted = 2,

        /// <summary>자동 제어 시작 요청.</summary>
        AutoRequested = 3,

        /// <summary>자동 제어 중지 요청.</summary>
        AutoStopRequested = 4,

        /// <summary>인터록 조건 성립.</summary>
        InterlockRaised = 5,

        /// <summary>인터록 조건 해제.</summary>
        InterlockCleared = 6,

        /// <summary>치명 장애 발생.</summary>
        FaultRaised = 7,

        /// <summary>사용자 Reset(권한 확인 완료).</summary>
        ResetRequested = 8,

        /// <summary>비상정지 조건 성립(EMO/메인 차단기).</summary>
        SafeStopRaised = 9,

        /// <summary>비상정지 조건 해제.</summary>
        SafeStopCleared = 10,

        /// <summary>정지 요청.</summary>
        Stop = 11
    }

    /// <summary>1회 제어 스텝의 판정 결과.</summary>
    public enum ControlResult
    {
        /// <summary>제어를 건너뜀(품질 불량, Dwell 대기, 비제어 상태 등).</summary>
        Skipped = 0,

        /// <summary>정상 대역 내. 액추에이터를 유지한다.</summary>
        InBand = 1,

        /// <summary>하한 이탈. 밸브 위치 감소 + 팬 OFF 로 대응 중.</summary>
        DeviatingLow = 2,

        /// <summary>상한 이탈. 밸브 위치 증가(또는 팬 증속)로 대응 중.</summary>
        DeviatingHigh = 3,

        /// <summary>하한 이탈 + 밸브 완전 닫힘 → 더 이상 대응 불가(에러).</summary>
        ErrorLow = 4,

        /// <summary>상한 이탈 + 밸브 완전 열림 + 팬 최대 → 더 이상 대응 불가(에러).</summary>
        ErrorHigh = 5
    }

    /// <summary>액추에이터 지령의 대상.</summary>
    public enum ActuatorTarget
    {
        /// <summary>스로틀밸브.</summary>
        Valve = 0,

        /// <summary>송풍팬.</summary>
        Fan = 1
    }

    /// <summary>액추에이터 지령의 종류.</summary>
    public enum ActuatorCommandKind
    {
        /// <summary>밸브 목표 위치를 절대값[pulse]으로 지정한다.</summary>
        SetValvePosition = 0,

        /// <summary>밸브를 완전히 닫는다(0 pulse). 인터록 동작.</summary>
        CloseValve = 1,

        /// <summary>밸브를 즉시 정지시킨다(Quick Stop).</summary>
        QuickStopValve = 2,

        /// <summary>밸브 원점 복귀를 실행한다.</summary>
        HomeValve = 3,

        /// <summary>팬 목표 회전수를 절대값[RPM]으로 지정한다.</summary>
        SetFanRpm = 4,

        /// <summary>팬을 정지시킨다.</summary>
        StopFan = 5,

        /// <summary>팬을 기동시킨다.</summary>
        StartFan = 6
    }

    /// <summary>지령 우선순위. 포트 워커의 우선순위 큐 정렬 기준이 된다.</summary>
    public enum CommandPriority
    {
        /// <summary>자동 제어 루프가 생성한 일반 지령.</summary>
        Automatic = 0,

        /// <summary>사용자의 수동 조작(Maintenance 화면).</summary>
        Manual = 1,

        /// <summary>인터록·비상정지 지령. 항상 최우선으로 처리한다.</summary>
        Interlock = 2
    }
}
