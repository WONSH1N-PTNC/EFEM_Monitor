using System.Collections.Generic;
using System.Globalization;

namespace Esam.Communication.Configuration
{
    /// <summary>
    /// 실제 설치된 디바이스 1대. device-map.json 의 <c>devices[]</c> 항목에 대응한다.
    /// </summary>
    public sealed class DeviceInstanceDefinition
    {
        /// <summary>디바이스 ID(예: "S1-1", "V-3", "F-5", "PLC-1"). 체인 정의와 알람이 이 ID 를 참조한다.</summary>
        public string Id { get; set; }

        /// <summary>디바이스 종류 이름(deviceTypes 의 키).</summary>
        public string Type { get; set; }

        /// <summary>소속 포트 ID(예: "BUS_A").</summary>
        public string Port { get; set; }

        /// <summary>슬레이브 주소(1~247).</summary>
        public byte SlaveId { get; set; }

        /// <summary>이 디바이스를 폴링할지 여부. false 이면 통신하지 않는다.</summary>
        public bool Enabled { get; set; }

        /// <summary>측정 하한 [공학단위]. 센서 레인지 검증에 사용한다.</summary>
        public double? RangeMin { get; set; }

        /// <summary>측정 상한 [공학단위].</summary>
        public double? RangeMax { get; set; }

        /// <summary>영점 오프셋. 최종값 = 측정값 - 이 값. Maintenance 화면의 영점 교정으로 갱신한다.</summary>
        public double Offset { get; set; }

        /// <summary>이동평균 창 크기. 1 이면 필터를 적용하지 않는다.</summary>
        public int FilterWindowSize { get; set; }

        /// <summary>표시명(선택).</summary>
        public string Name { get; set; }

        /// <summary>기본값으로 초기화한다.</summary>
        public DeviceInstanceDefinition()
        {
            Enabled = true;
            Offset = 0.0;
            FilterWindowSize = 1;
        }

        /// <summary>정의의 유효성을 검증한다.</summary>
        /// <param name="errors">검증 실패 사유를 추가할 목록.</param>
        /// <returns>유효하면 true.</returns>
        public bool Validate(IList<string> errors)
        {
            int before = errors.Count;
            string context = string.Format(
                CultureInfo.InvariantCulture, "device '{0}'", Id ?? "(무명)");

            if (string.IsNullOrEmpty(Id))
            {
                errors.Add("디바이스 id 는 필수입니다.");
            }

            if (string.IsNullOrEmpty(Type))
            {
                errors.Add(string.Format(CultureInfo.InvariantCulture, "{0}: type 은 필수입니다.", context));
            }

            if (string.IsNullOrEmpty(Port))
            {
                errors.Add(string.Format(CultureInfo.InvariantCulture, "{0}: port 는 필수입니다.", context));
            }

            if (SlaveId == 0 || SlaveId > 247)
            {
                errors.Add(string.Format(
                    CultureInfo.InvariantCulture,
                    "{0}: slaveId 는 1~247 범위여야 합니다(현재 {1}).", context, SlaveId));
            }

            if (FilterWindowSize < 1)
            {
                errors.Add(string.Format(
                    CultureInfo.InvariantCulture,
                    "{0}: filterWindowSize 는 1 이상이어야 합니다(현재 {1}).", context, FilterWindowSize));
            }

            if (RangeMin.HasValue && RangeMax.HasValue && RangeMin.Value >= RangeMax.Value)
            {
                errors.Add(string.Format(
                    CultureInfo.InvariantCulture,
                    "{0}: range.min({1}) 이 range.max({2}) 보다 크거나 같습니다.",
                    context, RangeMin.Value, RangeMax.Value));
            }

            return errors.Count == before;
        }

        /// <summary>측정값이 센서 레인지 안에 있는지 판정한다.</summary>
        /// <param name="value">측정값.</param>
        /// <returns>레인지가 지정되지 않았거나 범위 안이면 true.</returns>
        public bool IsWithinRange(double value)
        {
            if (RangeMin.HasValue && value < RangeMin.Value)
            {
                return false;
            }

            if (RangeMax.HasValue && value > RangeMax.Value)
            {
                return false;
            }

            return true;
        }
    }
}
