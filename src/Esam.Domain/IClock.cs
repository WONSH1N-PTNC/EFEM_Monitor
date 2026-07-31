using System;

namespace Esam.Domain
{
    /// <summary>
    /// 시각 제공자. 도메인 계층은 <see cref="DateTime.UtcNow"/> 를 직접 호출하지 않고
    /// 반드시 이 인터페이스를 통해 시각을 얻는다.
    /// 단위테스트에서 가상 시간을 주입해 디바운스·Dwell·타임아웃 로직을 결정적으로 검증하기 위함이다.
    /// </summary>
    public interface IClock
    {
        /// <summary>현재 UTC 시각.</summary>
        DateTime UtcNow { get; }
    }

    /// <summary>
    /// 실제 시스템 시각을 사용하는 <see cref="IClock"/> 구현. 운영 환경에서 사용한다.
    /// </summary>
    public sealed class SystemClock : IClock
    {
        /// <summary>공용 단일 인스턴스. 상태가 없으므로 스레드 안전하다.</summary>
        public static readonly SystemClock Instance = new SystemClock();

        private SystemClock()
        {
        }

        /// <inheritdoc />
        public DateTime UtcNow
        {
            get { return DateTime.UtcNow; }
        }
    }
}
