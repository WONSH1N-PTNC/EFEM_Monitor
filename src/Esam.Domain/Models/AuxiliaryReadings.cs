using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Esam.Domain.Models
{
    /// <summary>
    /// 차압센서·밸브·팬 이외의 보조 계측값 모음(온습도, 풍속, 파티클, 온도, FFU, MFC).
    /// </summary>
    /// <remarks>
    /// 파티클센서·FFU·MFC 는 아직 통신 사양이 확정되지 않았다(DESIGN.md Open Issue #10).
    /// 미확정 항목은 <c>null</c> 로 두며, UI 는 null 을 "--" 로 표시하고 알람은 비활성 처리한다.
    /// 값의 유무를 <see cref="double"/> 의 0 이나 NaN 으로 표현하지 않는 이유는,
    /// 0 이 유효한 측정값(예: 풍속 0 m/s)일 수 있기 때문이다.
    /// </remarks>
    public sealed class AuxiliaryReadings
    {
        /// <summary>모든 값이 미수집인 기본 인스턴스.</summary>
        public static readonly AuxiliaryReadings Empty = new AuxiliaryReadings(
            null, null, null, null, null, null, null, null, null, Quality.NoData, DateTime.MinValue);

        /// <summary>풍속 1~3 [m/s]. 미수집 채널은 null.</summary>
        public IReadOnlyList<double?> AirVelocities { get; private set; }

        /// <summary>EFEM 내부 온도 [℃].</summary>
        public double? TemperatureEfem { get; private set; }

        /// <summary>EFEM 내부 습도 [%RH].</summary>
        public double? HumidityEfem { get; private set; }

        /// <summary>EFEM 내부 파티클 농도. 단위는 센서 사양 확정 후 결정한다.</summary>
        public double? Particle { get; private set; }

        /// <summary>컨트롤박스(판넬) 온도 [℃]. PLC D105 에서 취득.</summary>
        public double? TemperatureControlBox { get; private set; }

        /// <summary>송풍팬 1~5 온도 [℃]. PLC D100~D104 에서 취득. 미수집 채널은 null.</summary>
        public IReadOnlyList<double?> FanTemperatures { get; private set; }

        /// <summary>FFU 회전수 [RPM]. 통신 방식 미정.</summary>
        public double? FfuRpm { get; private set; }

        /// <summary>MFC 1~2 유량. 단위·사양 미정. 미수집 채널은 null.</summary>
        public IReadOnlyList<double?> MfcFlows { get; private set; }

        /// <summary>MFC 1~2 설정 유량. 미수집 채널은 null.</summary>
        public IReadOnlyList<double?> MfcSetpoints { get; private set; }

        /// <summary>보조 계측 전반의 통신 품질(대표값).</summary>
        public Quality Quality { get; private set; }

        /// <summary>마지막 성공 갱신 시각(UTC).</summary>
        public DateTime LastUpdateUtc { get; private set; }

        /// <summary>보조 계측값 모음을 생성한다.</summary>
        /// <param name="airVelocities">풍속 1~3 [m/s].</param>
        /// <param name="temperatureEfem">EFEM 온도 [℃].</param>
        /// <param name="humidityEfem">EFEM 습도 [%RH].</param>
        /// <param name="particle">파티클 농도.</param>
        /// <param name="temperatureControlBox">컨트롤박스 온도 [℃].</param>
        /// <param name="fanTemperatures">송풍팬 1~5 온도 [℃].</param>
        /// <param name="ffuRpm">FFU 회전수 [RPM].</param>
        /// <param name="mfcFlows">MFC 1~2 현재 유량.</param>
        /// <param name="mfcSetpoints">MFC 1~2 설정 유량.</param>
        /// <param name="quality">통신 품질.</param>
        /// <param name="lastUpdateUtc">마지막 성공 갱신 시각(UTC).</param>
        public AuxiliaryReadings(
            IList<double?> airVelocities,
            double? temperatureEfem,
            double? humidityEfem,
            double? particle,
            double? temperatureControlBox,
            IList<double?> fanTemperatures,
            double? ffuRpm,
            IList<double?> mfcFlows,
            IList<double?> mfcSetpoints,
            Quality quality,
            DateTime lastUpdateUtc)
        {
            AirVelocities = CopyFixed(airVelocities, 3);
            TemperatureEfem = temperatureEfem;
            HumidityEfem = humidityEfem;
            Particle = particle;
            TemperatureControlBox = temperatureControlBox;
            FanTemperatures = CopyFixed(fanTemperatures, 5);
            FfuRpm = ffuRpm;
            MfcFlows = CopyFixed(mfcFlows, 2);
            MfcSetpoints = CopyFixed(mfcSetpoints, 2);
            Quality = quality;
            LastUpdateUtc = lastUpdateUtc;
        }

        /// <summary>
        /// 입력 리스트를 고정 길이 읽기전용 컬렉션으로 복사한다.
        /// 외부 배열 변경으로 스냅샷 불변성이 깨지는 것을 막고, 인덱스 범위를 보장한다.
        /// </summary>
        /// <param name="source">원본 리스트(null 허용).</param>
        /// <param name="length">고정 길이.</param>
        /// <returns>길이가 <paramref name="length"/> 인 읽기전용 컬렉션.</returns>
        private static IReadOnlyList<double?> CopyFixed(IList<double?> source, int length)
        {
            double?[] buffer = new double?[length];
            if (source != null)
            {
                int count = Math.Min(length, source.Count);
                for (int i = 0; i < count; i++)
                {
                    buffer[i] = source[i];
                }
            }

            return new ReadOnlyCollection<double?>(buffer);
        }
    }
}
