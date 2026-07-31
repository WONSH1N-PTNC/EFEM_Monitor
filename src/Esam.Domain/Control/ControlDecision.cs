using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Esam.Domain.Control
{
    /// <summary>
    /// 1회 제어 스텝의 산출물. 판정 결과와 생성된 액추에이터 지령을 함께 담는다.
    /// </summary>
    public sealed class ControlDecision
    {
        private static readonly ReadOnlyCollection<ActuatorCommand> NoCommands =
            new ReadOnlyCollection<ActuatorCommand>(new ActuatorCommand[0]);

        /// <summary>판정 결과.</summary>
        public ControlResult Result { get; private set; }

        /// <summary>생성된 액추에이터 지령 목록. 없으면 빈 컬렉션.</summary>
        public IReadOnlyList<ActuatorCommand> Commands { get; private set; }

        /// <summary>판정 근거 설명. 로그와 HMI 진단 표시에 사용한다.</summary>
        public string Explanation { get; private set; }

        /// <summary>제어 판정 결과를 생성한다.</summary>
        /// <param name="result">판정 결과.</param>
        /// <param name="commands">액추에이터 지령 목록(null 허용).</param>
        /// <param name="explanation">판정 근거 설명.</param>
        public ControlDecision(ControlResult result, IList<ActuatorCommand> commands, string explanation)
        {
            Result = result;
            Commands = commands == null || commands.Count == 0
                ? (IReadOnlyList<ActuatorCommand>)NoCommands
                : new ReadOnlyCollection<ActuatorCommand>(new List<ActuatorCommand>(commands));
            Explanation = explanation;
        }

        /// <summary>지령 없이 결과만 담은 판정을 만든다.</summary>
        /// <param name="result">판정 결과.</param>
        /// <param name="explanation">판정 근거 설명.</param>
        /// <returns>생성된 판정.</returns>
        public static ControlDecision WithoutCommand(ControlResult result, string explanation)
        {
            return new ControlDecision(result, null, explanation);
        }

        /// <summary>지령 1건을 담은 판정을 만든다.</summary>
        /// <param name="result">판정 결과.</param>
        /// <param name="command">액추에이터 지령.</param>
        /// <param name="explanation">판정 근거 설명.</param>
        /// <returns>생성된 판정.</returns>
        public static ControlDecision WithCommand(
            ControlResult result, ActuatorCommand command, string explanation)
        {
            return new ControlDecision(result, new List<ActuatorCommand> { command }, explanation);
        }
    }
}
