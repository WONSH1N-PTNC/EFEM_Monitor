using System.Collections.Generic;
using System.Globalization;

namespace Esam.Communication.Configuration
{
    /// <summary>
    /// 읽기 그룹 안의 개별 측정점 정의. device-map.json 의 <c>points[]</c> 항목 1건에 대응한다.
    /// </summary>
    public sealed class PointDefinition
    {
        /// <summary>측정점 키(예: "pressurePa", "di.emo", "temp.fan1").</summary>
        public string Key { get; set; }

        /// <summary>읽기 그룹 시작 주소 기준 레지스터 오프셋.</summary>
        public int Offset { get; set; }

        /// <summary>데이터 타입.</summary>
        public PointDataType Type { get; set; }

        /// <summary>32비트 타입의 워드 순서.</summary>
        public WordOrder WordOrder { get; set; }

        /// <summary><see cref="PointDataType.Bool"/> 일 때 읽을 비트 번호(0~15).</summary>
        public int Bit { get; set; }

        /// <summary>
        /// <see cref="PointDataType.Bool"/> 일 때의 신호 극성.
        /// false 이면 비트가 0 일 때 true 로 해석한다(Active Low).
        /// </summary>
        public bool ActiveHigh { get; set; }

        /// <summary>원시값에 곱할 배율(예: 0.1 Pa/LSB 이면 0.1).</summary>
        public double Scale { get; set; }

        /// <summary>배율 적용 후 더할 값.</summary>
        public double Bias { get; set; }

        /// <summary>단위 표기(표시용). 예: "Pa", "RPM", "C".</summary>
        public string Unit { get; set; }

        /// <summary>
        /// 이 측정점에 영점 오프셋·이동평균 필터·센서 레인지 검증을 적용할지 여부.
        /// </summary>
        /// <remarks>
        /// <b>기본값은 false 다.</b> 디바이스의 오프셋과 필터 설정은 주 계측값(예: 압력)에만
        /// 적용되어야 한다. 상태·알람 코드에 영점 오프셋을 적용하면
        /// 영점 교정으로 오프셋 20 을 설정한 순간 <c>deviceStatus</c> 가 -20 으로 보이는
        /// 식으로 값이 오염된다. 이동평균도 상태 코드에는 의미가 없다.
        /// </remarks>
        public bool ApplyCalibration { get; set; }

        /// <summary>이 측정점이 32비트 타입인지 여부(레지스터 2개를 소비한다).</summary>
        public bool Is32Bit
        {
            get { return Type == PointDataType.Int32 || Type == PointDataType.UInt32; }
        }

        /// <summary>이 측정점이 소비하는 레지스터 개수.</summary>
        public int RegisterCount
        {
            get { return Is32Bit ? 2 : 1; }
        }

        /// <summary>기본값으로 초기화한다(UInt16, 배율 1, 오프셋 0, Active High).</summary>
        public PointDefinition()
        {
            Type = PointDataType.UInt16;
            WordOrder = WordOrder.HighWordFirst;
            Scale = 1.0;
            Bias = 0.0;
            ActiveHigh = true;
            Bit = 0;
        }

        /// <summary>정의의 유효성을 검증한다.</summary>
        /// <param name="context">오류 메시지에 포함할 위치 설명.</param>
        /// <param name="errors">검증 실패 사유를 추가할 목록.</param>
        /// <returns>유효하면 true.</returns>
        public bool Validate(string context, IList<string> errors)
        {
            int before = errors.Count;

            if (string.IsNullOrEmpty(Key))
            {
                errors.Add(string.Format(
                    CultureInfo.InvariantCulture, "{0}: 측정점 key 는 필수입니다.", context));
            }

            if (Offset < 0)
            {
                errors.Add(string.Format(
                    CultureInfo.InvariantCulture, "{0}: offset 은 음수일 수 없습니다.", context));
            }

            if (Type == PointDataType.Bool && (Bit < 0 || Bit > 15))
            {
                errors.Add(string.Format(
                    CultureInfo.InvariantCulture,
                    "{0}: Bool 타입의 bit 는 0~15 범위여야 합니다(현재 {1}).", context, Bit));
            }

            if (Scale == 0.0)
            {
                // 배율 0 은 모든 측정값을 0 으로 만들어 버린다. 설정 실수일 가능성이 매우 높다.
                errors.Add(string.Format(
                    CultureInfo.InvariantCulture, "{0}: scale 이 0 입니다(측정값이 항상 0이 됩니다).", context));
            }

            return errors.Count == before;
        }
    }
}
