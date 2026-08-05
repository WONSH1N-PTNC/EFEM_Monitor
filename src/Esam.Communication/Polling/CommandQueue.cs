using System;
using System.Collections.Generic;
using Esam.Domain.Control;

namespace Esam.Communication.Polling
{
    /// <summary>
    /// 액추에이터 지령 우선순위 큐. 포트 워커가 매 사이클 소비한다.
    /// </summary>
    /// <remarks>
    /// <para><b>우선순위</b>: Interlock &gt; Manual &gt; Automatic (DESIGN.md 3.2 원칙 4).
    /// 인터록 지령이 자동 제어 지령 뒤에 줄을 서면 안전 기능이 성립하지 않는다.
    /// 자동 제어 루프가 200ms 마다 지령을 쌓는 상황에서도 인터록은 즉시 앞으로 나가야 한다.</para>
    /// <para><b>중복 병합</b>: 같은 디바이스에 대한 같은 종류의 미처리 지령은
    /// 나중 것으로 교체한다. 예를 들어 밸브 위치 지령이 2400 → 2300 → 2200 으로 쌓였다면
    /// 중간 단계를 거칠 필요 없이 최종값만 보내면 된다.
    /// 통신이 잠시 느려졌을 때 오래된 지령이 뒤늦게 실행되어 밸브가 역주행하는 것을 막는다.
    /// 단, 인터록 지령은 병합하지 않는다(안전 지령의 유실 방지).</para>
    /// <para>이 클래스는 스레드 안전하다. 제어 엔진 스레드가 넣고 포트 워커 스레드가 꺼낸다.</para>
    /// </remarks>
    public sealed class CommandQueue
    {
        private readonly object _gate = new object();
        private readonly List<ActuatorCommand> _interlock = new List<ActuatorCommand>();
        private readonly List<ActuatorCommand> _manual = new List<ActuatorCommand>();
        private readonly List<ActuatorCommand> _automatic = new List<ActuatorCommand>();

        /// <summary>대기 중인 지령 총 개수.</summary>
        public int Count
        {
            get
            {
                lock (_gate)
                {
                    return _interlock.Count + _manual.Count + _automatic.Count;
                }
            }
        }

        /// <summary>인터록 지령이 대기 중인지 여부.</summary>
        public bool HasInterlockCommand
        {
            get
            {
                lock (_gate)
                {
                    return _interlock.Count > 0;
                }
            }
        }

        /// <summary>지령을 큐에 넣는다.</summary>
        /// <param name="command">액추에이터 지령.</param>
        public void Enqueue(ActuatorCommand command)
        {
            if (command == null)
            {
                return;
            }

            lock (_gate)
            {
                // 인터록은 같은 우선순위끼리 병합하지 않는다. 안전 지령은 모두 실행되어야 한다.
                if (command.Priority != CommandPriority.Interlock)
                {
                    RemoveSameTarget(SelectList(command.Priority), command);
                }

                // 더 낮은 우선순위에 남아 있는 같은 대상·같은 종류의 지령은 제거한다.
                //
                // 이것이 없으면 다음 순서로 역주행이 발생한다.
                //   1) 자동 제어가 SetValvePosition(2000) 을 큐에 넣음
                //   2) 작업자가 수동으로 SetValvePosition(3000) 을 지시
                //   3) 워커는 Manual 을 먼저 실행(3000) 한 뒤 Automatic 을 실행(2000)
                //      → 작업자 조작이 낡은 자동 지령에 덮여 밸브가 되돌아간다.
                foreach (List<ActuatorCommand> lower in SelectLowerPriorityLists(command.Priority))
                {
                    RemoveSameTarget(lower, command);
                }

                SelectList(command.Priority).Add(command);
            }
        }

        /// <summary>여러 지령을 한 번에 넣는다.</summary>
        /// <param name="commands">지령 목록.</param>
        public void EnqueueRange(IEnumerable<ActuatorCommand> commands)
        {
            if (commands == null)
            {
                return;
            }

            foreach (ActuatorCommand command in commands)
            {
                Enqueue(command);
            }
        }

        /// <summary>우선순위가 가장 높은 지령을 꺼낸다.</summary>
        /// <param name="command">꺼낸 지령.</param>
        /// <returns>지령이 있었으면 true.</returns>
        public bool TryDequeue(out ActuatorCommand command)
        {
            lock (_gate)
            {
                if (TryTakeFirst(_interlock, out command))
                {
                    return true;
                }

                if (TryTakeFirst(_manual, out command))
                {
                    return true;
                }

                return TryTakeFirst(_automatic, out command);
            }
        }

        /// <summary>
        /// 대기 중인 지령을 최대 지정 개수까지 우선순위 순으로 꺼낸다.
        /// </summary>
        /// <param name="maxCount">최대 개수. 0 이하면 전부 꺼낸다.</param>
        /// <returns>꺼낸 지령 목록.</returns>
        public IList<ActuatorCommand> DequeueBatch(int maxCount)
        {
            List<ActuatorCommand> batch = new List<ActuatorCommand>();

            lock (_gate)
            {
                DrainInto(_interlock, batch, maxCount);
                DrainInto(_manual, batch, maxCount);
                DrainInto(_automatic, batch, maxCount);
            }

            return batch;
        }

        /// <summary>
        /// 자동 제어 지령만 비운다. 자동 운전을 중단할 때 사용한다.
        /// 인터록·수동 지령은 남겨 둔다.
        /// </summary>
        public void ClearAutomatic()
        {
            lock (_gate)
            {
                _automatic.Clear();
            }
        }

        /// <summary>모든 지령을 비운다.</summary>
        public void Clear()
        {
            lock (_gate)
            {
                _interlock.Clear();
                _manual.Clear();
                _automatic.Clear();
            }
        }

        /// <summary>우선순위에 해당하는 내부 목록을 선택한다.</summary>
        /// <param name="priority">우선순위.</param>
        /// <returns>내부 목록.</returns>
        private List<ActuatorCommand> SelectList(CommandPriority priority)
        {
            switch (priority)
            {
                case CommandPriority.Interlock:
                    return _interlock;

                case CommandPriority.Manual:
                    return _manual;

                default:
                    return _automatic;
            }
        }

        /// <summary>지정 우선순위보다 낮은 우선순위 목록들을 반환한다.</summary>
        /// <param name="priority">기준 우선순위.</param>
        /// <returns>더 낮은 우선순위 목록들.</returns>
        private IEnumerable<List<ActuatorCommand>> SelectLowerPriorityLists(CommandPriority priority)
        {
            if (priority == CommandPriority.Interlock)
            {
                yield return _manual;
                yield return _automatic;
            }
            else if (priority == CommandPriority.Manual)
            {
                yield return _automatic;
            }
        }

        /// <summary>같은 대상·같은 종류의 미처리 지령을 목록에서 제거한다.</summary>
        /// <param name="target">대상 목록.</param>
        /// <param name="command">기준 지령.</param>
        private static void RemoveSameTarget(List<ActuatorCommand> target, ActuatorCommand command)
        {
            for (int i = target.Count - 1; i >= 0; i--)
            {
                ActuatorCommand pending = target[i];

                if (pending.Kind == command.Kind
                    && string.Equals(pending.DeviceId, command.DeviceId,
                        StringComparison.OrdinalIgnoreCase))
                {
                    target.RemoveAt(i);
                }
            }
        }

        /// <summary>목록의 첫 항목을 꺼낸다.</summary>
        /// <param name="source">대상 목록.</param>
        /// <param name="command">꺼낸 지령.</param>
        /// <returns>항목이 있었으면 true.</returns>
        private static bool TryTakeFirst(List<ActuatorCommand> source, out ActuatorCommand command)
        {
            if (source.Count == 0)
            {
                command = null;
                return false;
            }

            command = source[0];
            source.RemoveAt(0);
            return true;
        }

        /// <summary>목록을 대상 배치로 옮긴다.</summary>
        /// <param name="source">원본 목록.</param>
        /// <param name="destination">대상 배치.</param>
        /// <param name="maxCount">최대 개수(0 이하면 무제한).</param>
        private static void DrainInto(
            List<ActuatorCommand> source, List<ActuatorCommand> destination, int maxCount)
        {
            while (source.Count > 0)
            {
                if (maxCount > 0 && destination.Count >= maxCount)
                {
                    return;
                }

                destination.Add(source[0]);
                source.RemoveAt(0);
            }
        }
    }
}
