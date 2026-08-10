using System;
using System.Globalization;

namespace Esam.Services
{
    /// <summary>구성 경고의 심각도.</summary>
    public enum ConfigWarningSeverity
    {
        /// <summary>
        /// 일부 계측이 빠졌지만 제어와 안전 기능은 온전하다.
        /// </summary>
        Advisory = 0,

        /// <summary>
        /// <b>안전 기능이 동작하지 않는 상태다.</b> 자동 운전 진입을 막는다.
        /// </summary>
        Blocking = 1
    }

    /// <summary>
    /// 런타임 조립 단계에서 발견한 구성 문제.
    /// </summary>
    /// <remarks>
    /// <para>종전에는 경고가 <c>List&lt;string&gt;</c> 였다. 그래서 "안전 입력이 하나도 없다" 와
    /// "MFC 주소가 미확정이다" 가 같은 무게로 섞여, 목록을 봐도 무엇이 중요한지 알 수 없었다.
    /// 게다가 <c>Describe()</c> 는 건수만 출력하고 HMI 는 이 계층을 참조조차 하지 않아
    /// <b>어떤 경로로도 작업자에게 도달하지 않았다.</b></para>
    /// <para>심각도를 두 단계로 나눈 이유는 그 이상이 필요 없기 때문이다.
    /// 판단해야 할 것은 "이 상태로 자동 운전에 들어가도 되는가" 하나다.</para>
    /// <para><see cref="Remedy"/> 를 따로 두는 이유는, 경고 문구만으로는
    /// 무엇을 해야 할지 알 수 없는 경우가 많기 때문이다.
    /// 조치 방법을 함께 적어 두면 현장에서 바로 처리할 수 있다.</para>
    /// </remarks>
    public sealed class ConfigWarning
    {
        /// <summary>경고 코드. 화면 필터와 문서 참조에 쓴다.</summary>
        public string Code { get; private set; }

        /// <summary>심각도.</summary>
        public ConfigWarningSeverity Severity { get; private set; }

        /// <summary>무엇이 문제인지.</summary>
        public string Message { get; private set; }

        /// <summary>무엇을 해야 하는지. 없으면 null.</summary>
        public string Remedy { get; private set; }

        /// <summary>자동 운전 진입을 막는 경고인지 여부.</summary>
        public bool IsBlocking
        {
            get { return Severity == ConfigWarningSeverity.Blocking; }
        }

        /// <summary>구성 경고를 생성한다.</summary>
        /// <param name="code">경고 코드.</param>
        /// <param name="severity">심각도.</param>
        /// <param name="message">문제 설명.</param>
        /// <param name="remedy">조치 방법. 없으면 null.</param>
        /// <exception cref="ArgumentNullException">메시지가 null 일 때.</exception>
        public ConfigWarning(
            string code, ConfigWarningSeverity severity, string message, string remedy)
        {
            if (message == null)
            {
                throw new ArgumentNullException("message");
            }

            Code = code;
            Severity = severity;
            Message = message;
            Remedy = remedy;
        }

        /// <summary>안전 기능이 동작하지 않는 경고를 만든다.</summary>
        /// <param name="code">경고 코드.</param>
        /// <param name="message">문제 설명.</param>
        /// <param name="remedy">조치 방법.</param>
        /// <returns>생성된 경고.</returns>
        public static ConfigWarning Blocking(string code, string message, string remedy)
        {
            return new ConfigWarning(code, ConfigWarningSeverity.Blocking, message, remedy);
        }

        /// <summary>참고 수준의 경고를 만든다.</summary>
        /// <param name="code">경고 코드.</param>
        /// <param name="message">문제 설명.</param>
        /// <param name="remedy">조치 방법. 없으면 null.</param>
        /// <returns>생성된 경고.</returns>
        public static ConfigWarning Advisory(string code, string message, string remedy)
        {
            return new ConfigWarning(code, ConfigWarningSeverity.Advisory, message, remedy);
        }

        /// <summary>사람이 읽을 수 있는 한 줄로 만든다.</summary>
        /// <returns>경고 문자열.</returns>
        public override string ToString()
        {
            string head = string.Format(
                CultureInfo.InvariantCulture,
                "[{0}] {1}: {2}",
                Severity == ConfigWarningSeverity.Blocking ? "차단" : "참고",
                Code,
                Message);

            return string.IsNullOrEmpty(Remedy) ? head : head + " → " + Remedy;
        }
    }
}
