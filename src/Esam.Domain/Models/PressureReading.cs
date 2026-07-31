using System;

namespace Esam.Domain.Models
{
    /// <summary>
    /// 차압센서(WTDM-550) 1채널의 측정 결과. 불변(immutable) 객체이다.
    /// 스냅샷 방식으로 UI/제어에 전달되므로 생성 후 변경되지 않아야 tearing 이 발생하지 않는다.
    /// </summary>
    public sealed class PressureReading
    {
        /// <summary>센서 식별자(예: "S1-1", "S2-3", "S3-5"). device-map.json 의 device.id 와 일치.</summary>
        public string Id { get; private set; }

        /// <summary>영점 오프셋과 필터가 모두 적용된 최종 압력값 [Pa]. 제어·알람 판정에 사용한다.</summary>
        public double Pa { get; private set; }

        /// <summary>필터 적용 전 원시 환산값 [Pa]. Phase 5 필터 상수 튜닝용으로 함께 로깅한다.</summary>
        public double RawPa { get; private set; }

        /// <summary>Modbus 레지스터에서 읽은 정수 원시값. 통신 진단용.</summary>
        public int RawRegister { get; private set; }

        /// <summary>적용된 영점 오프셋 [Pa]. (Pa = RawPa 필터결과 - Offset)</summary>
        public double OffsetPa { get; private set; }

        /// <summary>측정값 신뢰도.</summary>
        public Quality Quality { get; private set; }

        /// <summary>마지막 성공 갱신 시각(UTC).</summary>
        public DateTime LastUpdateUtc { get; private set; }

        /// <summary><see cref="Quality"/> 가 <see cref="Models.Quality.Good"/> 인지 여부(가독성 보조).</summary>
        public bool IsUsable
        {
            get { return Quality == Quality.Good; }
        }

        /// <summary>차압센서 측정 결과를 생성한다.</summary>
        /// <param name="id">센서 식별자.</param>
        /// <param name="pa">오프셋·필터 적용 후 압력 [Pa].</param>
        /// <param name="rawPa">필터 적용 전 압력 [Pa].</param>
        /// <param name="rawRegister">Modbus 원시 레지스터값.</param>
        /// <param name="offsetPa">적용된 영점 오프셋 [Pa].</param>
        /// <param name="quality">측정값 신뢰도.</param>
        /// <param name="lastUpdateUtc">마지막 성공 갱신 시각(UTC).</param>
        public PressureReading(
            string id,
            double pa,
            double rawPa,
            int rawRegister,
            double offsetPa,
            Quality quality,
            DateTime lastUpdateUtc)
        {
            if (string.IsNullOrEmpty(id))
            {
                throw new ArgumentException("센서 식별자는 비어 있을 수 없습니다.", "id");
            }

            Id = id;
            Pa = pa;
            RawPa = rawPa;
            RawRegister = rawRegister;
            OffsetPa = offsetPa;
            Quality = quality;
            LastUpdateUtc = lastUpdateUtc;
        }

        /// <summary>아직 데이터를 수신하지 못한 초기 상태의 판독값을 만든다.</summary>
        /// <param name="id">센서 식별자.</param>
        /// <returns><see cref="Models.Quality.NoData"/> 상태의 판독값.</returns>
        public static PressureReading NoData(string id)
        {
            return new PressureReading(id, 0.0, 0.0, 0, 0.0, Quality.NoData, DateTime.MinValue);
        }
    }
}
