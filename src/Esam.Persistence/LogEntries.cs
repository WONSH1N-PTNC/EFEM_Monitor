using System;
using System.Collections.Generic;
using Esam.Domain.Alarms;

namespace Esam.Persistence
{
    /// <summary>
    /// 알람 이력 1건. <c>alarm_history</c> 테이블의 열 구성에 대응한다.
    /// </summary>
    /// <remarks>
    /// <para><b>해제 시각을 채울 수단이 아직 없다.</b> <c>AlarmService</c> 는 발생 이벤트만
    /// 내보내고 해제 이벤트가 없다. 그래서 <see cref="ClearedUtc"/> 는 열만 만들어 두고
    /// 비워 둔다. 없는 정보를 발생 시각으로 메우면 조회 화면에서 "즉시 해제된 알람"으로
    /// 보이고, 그것이 실제 짧은 알람인지 기록이 없는 것인지 구분할 수 없게 된다.</para>
    /// <para>확인(Ack) 정보도 같은 이유로 비워 둔다. 사용자 이름은 S9 권한 기능이 생겨야
    /// 의미가 있다.</para>
    /// </remarks>
    public sealed class AlarmLogEntry
    {
        /// <summary>알람 코드(예: "AL-02").</summary>
        public string Code { get; private set; }

        /// <summary>심각도.</summary>
        public AlarmSeverity Severity { get; private set; }

        /// <summary>발생 시각(UTC).</summary>
        public DateTime RaisedUtc { get; private set; }

        /// <summary>발생 시점의 측정값. 값이 없는 종류의 알람이면 null.</summary>
        public double? Value { get; private set; }

        /// <summary>발생 사유 설명.</summary>
        public string Message { get; private set; }

        /// <summary>해제 시각(UTC). 아직 채우는 경로가 없다.</summary>
        public DateTime? ClearedUtc { get; set; }

        /// <summary>확인(Ack) 시각(UTC).</summary>
        public DateTime? AckUtc { get; set; }

        /// <summary>확인한 사용자. S9 권한 기능 이후에 채운다.</summary>
        public string AckUser { get; set; }

        /// <summary>알람 이력 1건을 만든다.</summary>
        /// <param name="code">알람 코드.</param>
        /// <param name="severity">심각도.</param>
        /// <param name="raisedUtc">발생 시각(UTC).</param>
        /// <param name="value">발생 시점의 측정값.</param>
        /// <param name="message">사유 설명.</param>
        /// <exception cref="ArgumentException">코드가 비어 있을 때.</exception>
        public AlarmLogEntry(
            string code,
            AlarmSeverity severity,
            DateTime raisedUtc,
            double? value,
            string message)
        {
            if (string.IsNullOrEmpty(code))
            {
                throw new ArgumentException("알람 코드가 비어 있습니다.", "code");
            }

            Code = code;
            Severity = severity;
            RaisedUtc = raisedUtc;
            Value = value;
            Message = message;
        }
    }

    /// <summary>
    /// 설정 변경 이력 1건. <c>audit_log</c> 테이블의 열 구성에 대응한다.
    /// </summary>
    /// <remarks>
    /// 반도체 고객이 요구하는 "누가 무엇을 언제 바꿨는가" 를 남기기 위한 것이다.
    /// 값은 표시 문자열 그대로 넣는다. 숫자로 변환해 저장하면 단위와 자릿수가
    /// 사라져서, 나중에 기록만 보고는 화면에서 무엇을 봤는지 복원할 수 없다.
    /// </remarks>
    public sealed class AuditLogEntry
    {
        /// <summary>변경 시각(UTC).</summary>
        public DateTime TimestampUtc { get; private set; }

        /// <summary>변경한 사용자. 권한 기능 이전에는 null.</summary>
        public string User { get; private set; }

        /// <summary>분류(예: "recipe", "alarm", "device-map").</summary>
        public string Category { get; private set; }

        /// <summary>항목 이름(예: "S1-1.setpointPa").</summary>
        public string Item { get; private set; }

        /// <summary>변경 전 값(표시 문자열).</summary>
        public string OldValue { get; private set; }

        /// <summary>변경 후 값(표시 문자열).</summary>
        public string NewValue { get; private set; }

        /// <summary>설정 변경 이력 1건을 만든다.</summary>
        /// <param name="timestampUtc">변경 시각(UTC).</param>
        /// <param name="user">변경한 사용자.</param>
        /// <param name="category">분류.</param>
        /// <param name="item">항목 이름.</param>
        /// <param name="oldValue">변경 전 값.</param>
        /// <param name="newValue">변경 후 값.</param>
        /// <exception cref="ArgumentException">항목 이름이 비어 있을 때.</exception>
        public AuditLogEntry(
            DateTime timestampUtc,
            string user,
            string category,
            string item,
            string oldValue,
            string newValue)
        {
            if (string.IsNullOrEmpty(item))
            {
                throw new ArgumentException("항목 이름이 비어 있습니다.", "item");
            }

            TimestampUtc = timestampUtc;
            User = user;
            Category = category;
            Item = item;
            OldValue = oldValue;
            NewValue = newValue;
        }
    }

    /// <summary>리텐션 정리 결과.</summary>
    /// <remarks>
    /// 지운 것과 지우지 못한 것을 나눠 돌려준다. 실패를 예외로 올리면 정리 한 번에
    /// 나머지 파일이 남고, 조용히 삼키면 디스크가 차는 것을 아무도 모른다.
    /// </remarks>
    public sealed class PurgeResult
    {
        /// <summary>삭제한 DB 파일 경로.</summary>
        public IList<string> Deleted { get; private set; }

        /// <summary>삭제하지 못한 DB 파일 경로와 사유.</summary>
        public IList<string> Failed { get; private set; }

        /// <summary>정리 결과를 만든다.</summary>
        /// <param name="deleted">삭제한 파일 경로.</param>
        /// <param name="failed">삭제하지 못한 파일 설명.</param>
        public PurgeResult(
            IList<string> deleted,
            IList<string> failed)
        {
            Deleted = deleted ?? new List<string>();
            Failed = failed ?? new List<string>();
        }
    }
}
