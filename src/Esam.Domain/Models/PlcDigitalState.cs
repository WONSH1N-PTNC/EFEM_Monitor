using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Esam.Domain.Models
{
    /// <summary>
    /// LS XBM-DR16S PLC 의 디지털 입력 상태(통신자료 「PLC Signal」 시트 D10.0 ~ D10.8).
    /// D10 은 1워드이므로 통신 계층에서 1회 read 후 비트 마스킹하여 채운다.
    /// </summary>
    /// <remarks>
    /// 각 비트의 Active High/Low 극성은 아직 미확정이다(DESIGN.md Open Issue #18).
    /// 극성 보정은 통신 계층(device-map.json 의 activeHigh)에서 처리하므로,
    /// 이 클래스의 속성은 모두 "true = 해당 이상 상태 발생"으로 정규화된 의미를 갖는다.
    /// </remarks>
    public sealed class PlcDigitalState
    {
        private static readonly bool[] EmptyFanAlarms = new bool[5];

        /// <summary>송풍팬 1~5 정지 알람(D10.0 ~ D10.4). true = 정지 알람 발생.</summary>
        public IReadOnlyList<bool> FanStopAlarms { get; private set; }

        /// <summary>제어박스 냉각팬 정지 알람. 상·하 어느 한쪽이라도 정지하면 true.</summary>
        public bool ControlBoxFanAlarm { get; private set; }

        /// <summary>제어박스 상부 냉각팬 정지 여부.</summary>
        /// <remarks>
        /// <c>Alarm LIST</c> 는 상부와 하부를 별개 알람(AL-38·AL-39)으로 요구하고
        /// PLC 도 비트를 둘로 읽는다. 합쳐서만 노출하면 두 알람이 항상 함께 울려
        /// <b>어느 팬이 멈췄는지 알 수 없다.</b> 제어함을 열어 봐야 한다.
        /// </remarks>
        public bool ControlBoxFanTopAlarm { get; private set; }

        /// <summary>제어박스 하부 냉각팬 정지 여부.</summary>
        public bool ControlBoxFanBottomAlarm { get; private set; }

        /// <summary>비상정지(EMO) 작동 여부(D10.6). true = EMO 눌림 → 즉시 SafeStop.</summary>
        public bool EmoActive { get; private set; }

        /// <summary>도어 열림 여부(D10.7). true = 열림.</summary>
        public bool DoorOpen { get; private set; }

        /// <summary>메인 차단기 OFF 여부(D10.8). true = 차단됨 → 즉시 SafeStop.</summary>
        public bool MainBreakerOff { get; private set; }

        /// <summary>통신 품질.</summary>
        public Quality Quality { get; private set; }

        /// <summary>마지막 성공 갱신 시각(UTC).</summary>
        public DateTime LastUpdateUtc { get; private set; }

        /// <summary>
        /// 즉시 전체 정지가 필요한 안전 조건이 하나라도 성립하는지 여부.
        /// 인터록 IL-02(EMO) / IL-03(메인 차단기) 판정에 사용한다.
        /// </summary>
        public bool RequiresSafeStop
        {
            get { return EmoActive || MainBreakerOff; }
        }

        /// <summary>PLC 디지털 입력 상태를 생성한다.</summary>
        /// <param name="fanStopAlarms">송풍팬 1~5 정지 알람. 길이 5 여야 한다.</param>
        /// <param name="controlBoxFanAlarm">제어박스 냉각팬 알람.</param>
        /// <param name="emoActive">EMO 작동 여부.</param>
        /// <param name="doorOpen">도어 열림 여부.</param>
        /// <param name="mainBreakerOff">메인 차단기 OFF 여부.</param>
        /// <param name="quality">통신 품질.</param>
        /// <param name="lastUpdateUtc">마지막 성공 갱신 시각(UTC).</param>
        /// <param name="controlBoxFanTopAlarm">제어박스 상부 냉각팬 정지 여부.</param>
        /// <param name="controlBoxFanBottomAlarm">제어박스 하부 냉각팬 정지 여부.</param>
        public PlcDigitalState(
            IList<bool> fanStopAlarms,
            bool controlBoxFanAlarm,
            bool emoActive,
            bool doorOpen,
            bool mainBreakerOff,
            Quality quality,
            DateTime lastUpdateUtc,
            bool controlBoxFanTopAlarm = false,
            bool controlBoxFanBottomAlarm = false)
        {
            bool[] copied = new bool[5];
            if (fanStopAlarms != null)
            {
                int count = Math.Min(5, fanStopAlarms.Count);
                for (int i = 0; i < count; i++)
                {
                    copied[i] = fanStopAlarms[i];
                }
            }

            // 외부에서 원본 배열을 수정해도 스냅샷이 변하지 않도록 복사본을 감싼다.
            FanStopAlarms = new ReadOnlyCollection<bool>(copied);
            ControlBoxFanTopAlarm = controlBoxFanTopAlarm;
            ControlBoxFanBottomAlarm = controlBoxFanBottomAlarm;

            // 개별 비트가 서면 합계도 서야 한다. 둘이 어긋나면
            // "제어함 팬 이상 없음" 인데 개별 알람은 울리는 상태가 된다.
            ControlBoxFanAlarm =
                controlBoxFanAlarm || controlBoxFanTopAlarm || controlBoxFanBottomAlarm;
            EmoActive = emoActive;
            DoorOpen = doorOpen;
            MainBreakerOff = mainBreakerOff;
            Quality = quality;
            LastUpdateUtc = lastUpdateUtc;
        }

        /// <summary>아직 데이터를 수신하지 못한 초기 상태를 만든다.</summary>
        /// <returns><see cref="Models.Quality.NoData"/> 상태의 PLC 입력 상태.</returns>
        public static PlcDigitalState NoData()
        {
            return new PlcDigitalState(
                EmptyFanAlarms, false, false, false, false, Quality.NoData, DateTime.MinValue);
        }
    }
}
