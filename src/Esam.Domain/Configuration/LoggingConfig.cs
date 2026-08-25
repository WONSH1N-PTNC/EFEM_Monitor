using System;
using System.Collections.Generic;
using System.Globalization;

namespace Esam.Domain.Configuration
{
    /// <summary>
    /// 데이터 기록 설정. <c>control.json</c> 의 <c>logging</c> 절에 대응한다.
    /// </summary>
    /// <remarks>
    /// <para><b>왜 제어 설정 안에 두는가.</b> 기록은 제어가 아니다. 그래도 여기 둔 이유는
    /// 검증 경로를 하나로 두기 위해서다(설계 원칙 2). <see cref="ControlConfig.Validate"/>
    /// 를 거치면 로더와 설정 화면이 같은 규칙을 쓴다. 별도 파일·별도 로더로 두면
    /// 화면에서 저장할 때만 검증이 빠지는 길이 생긴다.</para>
    /// <para><b>기록 실패가 운전을 막지 않는다.</b> 여기의 값이 잘못되어도 자동 운전은
    /// 성립해야 한다. 그래서 검증 실패는 <see cref="ControlConfig.Validate"/> 의 오류로
    /// 올라가지만, 실행 중 기록 실패는 구성 경고(Advisory)로만 드러낸다.</para>
    /// </remarks>
    public sealed class LoggingConfig
    {
        /// <summary>기록을 수행할지 여부(운용 스위치).</summary>
        /// <remarks>
        /// 조립 스위치(<c>RuntimeOptions.EnableDataLogging</c>)와 역할이 다르다.
        /// 이쪽은 "현장에서 기록을 켤 것인가", 저쪽은 "이 프로세스가 기록을 담당하는가" 다.
        /// 테스트와 시뮬레이터는 후자가 false 이므로 이 값과 무관하게 기록하지 않는다.
        /// </remarks>
        public bool Enabled { get; set; }

        /// <summary>DB 파일을 둘 폴더. 상대경로면 프로그램 작업 폴더 기준이다.</summary>
        /// <remarks>
        /// <c>config</c> 폴더를 상대경로로 잡는 것과 같은 규칙이다. 규칙이 다르면
        /// 설정 파일은 찾는데 로그는 엉뚱한 곳에 쌓이는 상태가 된다.
        /// </remarks>
        public string Folder { get; set; }

        /// <summary>배치를 확정하는 최대 대기 시간 [ms].</summary>
        /// <remarks>
        /// 이 시간이 곧 <b>기록 지연의 상한</b>이다. 길게 두면 강제 종료 시 잃는 구간이 늘고,
        /// 짧게 두면 트랜잭션이 잦아져 디스크가 바빠진다.
        /// </remarks>
        public int BatchMs { get; set; }

        /// <summary>한 트랜잭션에 넣는 최대 행 수.</summary>
        public int BatchRows { get; set; }

        /// <summary>보존 일수(오늘 포함).</summary>
        public int RetentionDays { get; set; }

        /// <summary>적재 대기 큐의 최대 행 수.</summary>
        /// <remarks>
        /// <para>무한 큐를 두지 않는 이유는, 디스크가 멈추면 메모리가 대신 차오르다가
        /// 프로그램이 죽기 때문이다. 죽는 것은 제어가 멈추는 것이다.</para>
        /// <para>2000 행은 200ms 주기로 약 400초분이다. 디스크가 6분 넘게 멈추면
        /// 그때부터 구멍이 생기고, 몇 행을 버렸는지는 센다.</para>
        /// </remarks>
        public int QueueCapacity { get; set; }

        /// <summary>배포 기본값으로 초기화한다.</summary>
        public LoggingConfig()
        {
            Enabled = true;
            Folder = "log";
            BatchMs = 500;
            BatchRows = 100;
            RetentionDays = 90;
            QueueCapacity = 2000;
        }

        /// <summary>설정을 검증하고 실패 사유를 덧붙인다.</summary>
        /// <param name="errors">실패 사유를 덧붙일 목록.</param>
        /// <exception cref="ArgumentNullException">목록이 null 일 때.</exception>
        /// <remarks>
        /// 이 계층의 다른 설정 클래스는 <c>Validate(out string)</c> 로 첫 실패만 돌려준다.
        /// 여기서는 전부 모아 돌려준다. 검사 항목이 다섯 개라, 하나씩 알려주면
        /// 파일을 고치는 사람이 그만큼 왕복해야 한다.
        /// </remarks>
        public void Validate(IList<string> errors)
        {
            if (errors == null)
            {
                throw new ArgumentNullException("errors");
            }

            if (string.IsNullOrEmpty(Folder) || Folder.Trim().Length == 0)
            {
                errors.Add("logging.folder 가 비어 있습니다.");
            }

            if (BatchMs < 50)
            {
                errors.Add(Format("logging.batchMs 는 50 이상이어야 합니다(현재 {0}).", BatchMs));
            }

            if (BatchRows < 1)
            {
                errors.Add(Format("logging.batchRows 는 1 이상이어야 합니다(현재 {0}).", BatchRows));
            }

            if (RetentionDays < 1)
            {
                errors.Add(Format("logging.retentionDays 는 1 이상이어야 합니다(현재 {0}).", RetentionDays));
            }

            if (QueueCapacity < BatchRows)
            {
                // 큐가 배치보다 작으면 한 배치를 모으는 동안에도 넘친다.
                errors.Add(Format(
                    "logging.queueCapacity({0}) 가 batchRows({1}) 보다 작습니다. 한 배치를 모으는 동안 계속 버려집니다.",
                    QueueCapacity, BatchRows));
            }
        }

        /// <summary>불변 문화권으로 문자열을 만든다.</summary>
        /// <param name="format">서식.</param>
        /// <param name="args">인자.</param>
        /// <returns>서식이 적용된 문자열.</returns>
        private static string Format(string format, params object[] args)
        {
            return string.Format(CultureInfo.InvariantCulture, format, args);
        }
    }
}
