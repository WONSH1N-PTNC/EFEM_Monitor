using System.Collections.Generic;
using System.Globalization;
using Esam.Communication.Modbus;

namespace Esam.Communication.Configuration
{
    /// <summary>폴링 티어별 주기 설정.</summary>
    public sealed class PollingTierPeriods
    {
        /// <summary>Fast 티어 주기 [ms]. 차압센서·밸브 위치·PLC 안전 입력.</summary>
        public int FastMs { get; set; }

        /// <summary>Medium 티어 주기 [ms]. 온도·장치 알람.</summary>
        public int MediumMs { get; set; }

        /// <summary>Slow 티어 주기 [ms]. 온습도·풍속·파티클.</summary>
        public int SlowMs { get; set; }

        /// <summary>기본값으로 초기화한다(200 / 1000 / 5000 ms).</summary>
        public PollingTierPeriods()
        {
            FastMs = 200;
            MediumMs = 1000;
            SlowMs = 5000;
        }

        /// <summary>지정 티어의 주기를 반환한다.</summary>
        /// <param name="tier">폴링 티어.</param>
        /// <returns>주기 [ms].</returns>
        public int GetPeriodMs(PollingTier tier)
        {
            switch (tier)
            {
                case PollingTier.Medium:
                    return MediumMs;

                case PollingTier.Slow:
                    return SlowMs;

                default:
                    return FastMs;
            }
        }

        /// <summary>설정의 유효성을 검증한다.</summary>
        /// <param name="context">오류 메시지에 포함할 위치 설명.</param>
        /// <param name="errors">검증 실패 사유를 추가할 목록.</param>
        /// <returns>유효하면 true.</returns>
        public bool Validate(string context, IList<string> errors)
        {
            int before = errors.Count;

            if (FastMs <= 0 || MediumMs <= 0 || SlowMs <= 0)
            {
                errors.Add(string.Format(
                    CultureInfo.InvariantCulture, "{0}: 폴링 주기는 모두 0보다 커야 합니다.", context));
            }

            if (MediumMs < FastMs || SlowMs < MediumMs)
            {
                // 티어를 나눈 목적이 버스 부하 감소이므로 Fast ≤ Medium ≤ Slow 가 성립해야 한다.
                errors.Add(string.Format(
                    CultureInfo.InvariantCulture,
                    "{0}: 폴링 주기는 fast({1}) ≤ medium({2}) ≤ slow({3}) 순서여야 합니다.",
                    context, FastMs, MediumMs, SlowMs));
            }

            return errors.Count == before;
        }
    }

    /// <summary>
    /// 포트 1개의 전체 설정. ports.json 의 <c>ports[]</c> 항목에 대응한다.
    /// </summary>
    public sealed class PortDefinition
    {
        /// <summary>시리얼 및 Modbus 트랜잭션 파라미터.</summary>
        public SerialPortSettings Serial { get; set; }

        /// <summary>폴링 티어별 주기.</summary>
        public PollingTierPeriods Polling { get; set; }

        /// <summary>포트 논리 ID. <see cref="Serial"/> 의 PortId 를 그대로 노출한다.</summary>
        public string PortId
        {
            get { return Serial == null ? null : Serial.PortId; }
        }

        /// <summary>기본값으로 초기화한다.</summary>
        public PortDefinition()
        {
            Serial = new SerialPortSettings();
            Polling = new PollingTierPeriods();
        }

        /// <summary>설정의 유효성을 검증한다.</summary>
        /// <param name="errors">검증 실패 사유를 추가할 목록.</param>
        /// <returns>유효하면 true.</returns>
        public bool Validate(IList<string> errors)
        {
            int before = errors.Count;
            string context = string.Format(
                CultureInfo.InvariantCulture, "port '{0}'", PortId ?? "(무명)");

            if (Serial == null)
            {
                errors.Add("포트의 serial 설정이 null 입니다.");
                return false;
            }

            string serialError;
            if (!Serial.Validate(out serialError))
            {
                errors.Add(string.Format(CultureInfo.InvariantCulture, "{0}: {1}", context, serialError));
            }

            if (Polling == null)
            {
                errors.Add(string.Format(CultureInfo.InvariantCulture, "{0}: polling 설정이 null 입니다.", context));
            }
            else
            {
                Polling.Validate(context, errors);
            }

            return errors.Count == before;
        }
    }
}
