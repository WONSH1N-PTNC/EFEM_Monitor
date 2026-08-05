using System.Collections.Generic;
using System.Globalization;
using Esam.Communication.Abstractions;

namespace Esam.Communication.Configuration
{
    /// <summary>
    /// 한 번의 Modbus 트랜잭션으로 읽는 연속 레지스터 묶음. device-map.json 의 <c>readGroups[]</c> 에 대응한다.
    /// </summary>
    /// <remarks>
    /// 측정점을 그룹으로 묶는 이유는 트랜잭션 수를 줄이는 것이다.
    /// COMM_MAP.md 1.3 에서 송풍팬 레지스터를 연속 배치해 달라고 요청한 근거가 여기에 있다.
    /// 그룹 1개 = 트랜잭션 1건이므로, 그룹을 합칠수록 폴링 사이클이 짧아진다.
    /// </remarks>
    public sealed class ReadGroupDefinition
    {
        /// <summary>그룹 이름(예: "pressure", "position", "digital"). 로그·진단 표시용.</summary>
        public string Name { get; set; }

        /// <summary>이 그룹을 읽을 폴링 티어.</summary>
        public PollingTier Tier { get; set; }

        /// <summary>사용할 함수 코드(3 = Holding, 4 = Input).</summary>
        public int FunctionCode { get; set; }

        /// <summary>시작 주소 문자열("0x602B", "0", "TBD" 등).</summary>
        public string StartAddress { get; set; }

        /// <summary>읽을 레지스터 개수.</summary>
        public int Count { get; set; }

        /// <summary>이 그룹에서 추출할 측정점 목록.</summary>
        public IList<PointDefinition> Points { get; set; }

        /// <summary>기본값으로 초기화한다(Fast 티어, FC03).</summary>
        public ReadGroupDefinition()
        {
            Tier = PollingTier.Fast;
            FunctionCode = 3;
            Count = 1;
            Points = new List<PointDefinition>();
        }

        /// <summary>주소가 미확정(TBD)인지 여부. true 이면 이 그룹은 폴링 대상에서 제외된다.</summary>
        public bool IsAddressUnspecified
        {
            get { return RegisterAddress.IsUnspecified(StartAddress); }
        }

        /// <summary>함수 코드를 열거형으로 변환한다.</summary>
        /// <param name="functionCode">변환된 함수 코드.</param>
        /// <returns>지원하는 읽기 함수 코드이면 true.</returns>
        public bool TryGetFunctionCode(out ModbusFunctionCode functionCode)
        {
            switch (FunctionCode)
            {
                case 3:
                    functionCode = ModbusFunctionCode.ReadHoldingRegisters;
                    return true;

                case 4:
                    functionCode = ModbusFunctionCode.ReadInputRegisters;
                    return true;

                default:
                    functionCode = ModbusFunctionCode.ReadHoldingRegisters;
                    return false;
            }
        }

        /// <summary>정의의 유효성을 검증한다.</summary>
        /// <param name="context">오류 메시지에 포함할 위치 설명.</param>
        /// <param name="errors">검증 실패 사유를 추가할 목록.</param>
        /// <returns>유효하면 true.</returns>
        public bool Validate(string context, IList<string> errors)
        {
            int before = errors.Count;
            string groupContext = string.Format(
                CultureInfo.InvariantCulture, "{0}.{1}", context, Name ?? "(무명)");

            if (string.IsNullOrEmpty(Name))
            {
                errors.Add(string.Format(
                    CultureInfo.InvariantCulture, "{0}: 읽기 그룹 name 은 필수입니다.", context));
            }

            ModbusFunctionCode ignored;
            if (!TryGetFunctionCode(out ignored))
            {
                errors.Add(string.Format(
                    CultureInfo.InvariantCulture,
                    "{0}: 읽기 함수 코드는 3 또는 4 여야 합니다(현재 {1}).", groupContext, FunctionCode));
            }

            if (Count < 1 || Count > 125)
            {
                errors.Add(string.Format(
                    CultureInfo.InvariantCulture,
                    "{0}: count 는 1~125 범위여야 합니다(현재 {1}).", groupContext, Count));
            }

            if (Points == null || Points.Count == 0)
            {
                errors.Add(string.Format(
                    CultureInfo.InvariantCulture, "{0}: 측정점(points)이 비어 있습니다.", groupContext));
                return false;
            }

            HashSet<string> seenKeys = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);

            foreach (PointDefinition point in Points)
            {
                if (point == null)
                {
                    errors.Add(string.Format(
                        CultureInfo.InvariantCulture, "{0}: 측정점 목록에 null 항목이 있습니다.", groupContext));
                    continue;
                }

                string pointContext = string.Format(
                    CultureInfo.InvariantCulture, "{0}.{1}", groupContext, point.Key ?? "(무명)");

                point.Validate(pointContext, errors);

                if (!string.IsNullOrEmpty(point.Key) && !seenKeys.Add(point.Key))
                {
                    errors.Add(string.Format(
                        CultureInfo.InvariantCulture,
                        "{0}: 측정점 key '{1}' 가 중복되었습니다.", groupContext, point.Key));
                }

                // 오프셋이 읽기 범위를 벗어나면 런타임에 IndexOutOfRange 가 발생한다.
                // 설정 로드 시점에 잡아야 한다.
                int required = point.Offset + point.RegisterCount;
                if (required > Count)
                {
                    errors.Add(string.Format(
                        CultureInfo.InvariantCulture,
                        "{0}: offset {1}({2}) 이 읽기 범위(count {3})를 벗어납니다. count 를 {4} 이상으로 늘리십시오.",
                        pointContext, point.Offset, point.Type, Count, required));
                }
            }

            return errors.Count == before;
        }
    }
}
