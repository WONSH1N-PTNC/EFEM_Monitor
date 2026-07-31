namespace Esam.Domain.Alarms
{
    /// <summary>알람 심각도. 값이 클수록 심각하며 UI 색상·정렬 우선순위에 사용한다.</summary>
    public enum AlarmSeverity
    {
        /// <summary>알람 없음(정상).</summary>
        None = 0,

        /// <summary>참고 정보. 운전에 영향 없음.</summary>
        Info = 1,

        /// <summary>경고. 운전은 계속하되 조치가 필요하다.</summary>
        Warning = 2,

        /// <summary>알람. 공정 품질에 영향을 줄 수 있다.</summary>
        Alarm = 3,

        /// <summary>치명. 자동 운전을 중단해야 한다.</summary>
        Critical = 4
    }

    /// <summary>
    /// 알람 판정 조건의 종류. alarms.json 의 <c>condition</c> 필드에 대응한다.
    /// 코드에 임계값을 하드코딩하지 않고 선언으로 관리하기 위한 열거형이다.
    /// </summary>
    public enum AlarmConditionType
    {
        /// <summary>값이 임계값보다 크면 알람.</summary>
        GreaterThan = 0,

        /// <summary>값이 임계값보다 작으면 알람.</summary>
        LessThan = 1,

        /// <summary>값이 지정된 센서 모드의 정상 대역을 벗어나면 알람.</summary>
        OutOfBand = 2,

        /// <summary>대상 디바이스의 통신이 연속 실패하면 알람.</summary>
        CommFail = 3,

        /// <summary>대상 비트가 1이면 알람(PLC 디지털 입력).</summary>
        BitSet = 4,

        /// <summary>통신 실패 또는 장치 알람코드가 0이 아니면 알람.</summary>
        CommFailOrAlarmCode = 5
    }

    /// <summary>알람 해제 정책.</summary>
    public enum AlarmResetPolicy
    {
        /// <summary>조건이 해소되면 자동 해제된다.</summary>
        Auto = 0,

        /// <summary>조건 해소 후에도 사용자가 Reset 해야 해제된다.</summary>
        Manual = 1
    }

    /// <summary>인터록 동작 범위.</summary>
    public enum InterlockScope
    {
        /// <summary>조건이 성립한 체인만 정지시킨다.</summary>
        Chain = 0,

        /// <summary>전 체인을 정지시킨다.</summary>
        System = 1
    }
}
