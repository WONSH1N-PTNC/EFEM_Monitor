using System;
using System.Globalization;

namespace Esam.Domain.Configuration
{
    /// <summary>
    /// 체인 1조의 구성 정의. chains.json 의 <c>chains[]</c> 항목에 대응한다.
    /// 설비 확장 시 이 정의만 추가하면 되고 코드 수정은 필요 없다.
    /// </summary>
    public sealed class ChainDefinition
    {
        /// <summary>체인 번호(1~5).</summary>
        public int Id { get; set; }

        /// <summary>체인 표시명(예: "Chain 2-1").</summary>
        public string Name { get; set; }

        /// <summary>이 체인의 센서 2 ID(예: "S2-1").</summary>
        public string Sensor2Id { get; set; }

        /// <summary>이 체인의 센서 3 ID(예: "S3-1"). 인터록 IL-01 판정에 사용한다.</summary>
        public string Sensor3Id { get; set; }

        /// <summary>
        /// Sensor 1 모드에서 이 체인이 참조할 센서 1 ID.
        /// <c>sensor1Reference</c> 가 <c>PerChain</c> 일 때만 사용한다(DESIGN.md Open Issue #16).
        /// </summary>
        public string Sensor1Id { get; set; }

        /// <summary>이 체인의 스로틀밸브 ID(예: "V-1").</summary>
        public string ValveId { get; set; }

        /// <summary>이 체인의 송풍팬 ID(예: "F-1").</summary>
        public string FanId { get; set; }

        /// <summary>이 체인을 제어 대상으로 사용할지 여부. false 이면 모니터링만 한다.</summary>
        public bool Enabled { get; set; }

        /// <summary>기본값으로 초기화한다.</summary>
        public ChainDefinition()
        {
            Enabled = true;
        }

        /// <summary>지정한 센서 모드에서 이 체인이 참조할 센서 ID 를 반환한다.</summary>
        /// <param name="mode">센서 모드.</param>
        /// <param name="sensor1Reference">Sensor1 모드의 참조 방식(고정 ID 또는 "PerChain"/"Average").</param>
        /// <returns>참조할 센서 ID. Average 방식이면 null(호출측이 평균을 계산해야 함).</returns>
        public string ResolveSensorId(Control.SensorMode mode, string sensor1Reference)
        {
            switch (mode)
            {
                case Control.SensorMode.Sensor2:
                    return Sensor2Id;

                case Control.SensorMode.Sensor3:
                    return Sensor3Id;

                case Control.SensorMode.Sensor1:
                    // Sensor1 은 문서상 "EFEM 내부에 1개"이나 실제로는 S1-1/1-2/1-3 이 존재한다.
                    // 어느 것을 참조할지는 설정으로 결정한다(Open Issue #16).
                    if (string.Equals(sensor1Reference, "PerChain", StringComparison.OrdinalIgnoreCase))
                    {
                        return Sensor1Id;
                    }

                    if (string.Equals(sensor1Reference, "Average", StringComparison.OrdinalIgnoreCase))
                    {
                        return null;
                    }

                    return sensor1Reference;

                default:
                    throw new ArgumentOutOfRangeException("mode", mode, "알 수 없는 센서 모드입니다.");
            }
        }

        /// <summary>정의의 유효성을 검증한다.</summary>
        /// <param name="error">검증 실패 사유. 성공 시 null.</param>
        /// <returns>유효하면 true.</returns>
        public bool Validate(out string error)
        {
            if (Id <= 0)
            {
                error = "체인 번호(Id)는 1 이상이어야 합니다.";
                return false;
            }

            if (string.IsNullOrEmpty(ValveId) || string.IsNullOrEmpty(FanId))
            {
                error = string.Format(CultureInfo.InvariantCulture, "체인 {0}: ValveId 와 FanId 는 필수입니다.", Id);
                return false;
            }

            if (string.IsNullOrEmpty(Sensor2Id) || string.IsNullOrEmpty(Sensor3Id))
            {
                error = string.Format(CultureInfo.InvariantCulture, "체인 {0}: Sensor2Id 와 Sensor3Id 는 필수입니다.", Id);
                return false;
            }

            error = null;
            return true;
        }
    }
}
