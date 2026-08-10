using System;
using System.Collections.Generic;
using System.Globalization;
using Esam.Domain.Models;
using Esam.Domain.Units;

namespace Esam.Domain.Control
{
    /// <summary>
    /// ESAM 운용방법 설명자료 p.10~12 의 차압센서 1/2/3 모드 순서도를 그대로 구현한 밴드 제어 정책.
    /// </summary>
    /// <remarks>
    /// <para>세 모드는 알고리즘이 완전히 동일하고 Setpoint/Band/Time 파라미터만 다르므로
    /// 하나의 클래스로 구현하고 파라미터를 주입한다.</para>
    /// <para>제어 규칙(순서도 원문):</para>
    /// <list type="number">
    ///   <item><description>압력하한 &lt; PV &lt; 압력상한 → 밸브 위치 유지, 팬 유지</description></item>
    ///   <item><description>PV &lt; 압력하한 → 밸브 위치 감소 + 팬 OFF.
    ///     밸브가 이미 0도이면 error</description></item>
    ///   <item><description>PV &gt; 압력상한 → 밸브 위치 증가.
    ///     밸브가 이미 90도이면 팬 속도 증가. 팬도 최대이면 error</description></item>
    /// </list>
    /// <para>즉 액추에이터 우선순위는 <b>밸브 &gt; 팬</b> 이며, 팬은 밸브 포화 후에만 개입한다.</para>
    /// </remarks>
    public sealed class BandControlPolicy : IControlPolicy
    {
        private readonly ValvePulseConverter _converter;

        /// <inheritdoc />
        public string Name
        {
            get { return "Band"; }
        }

        /// <summary>기본 변환기(5000 pulse = 90도)로 정책을 생성한다.</summary>
        public BandControlPolicy()
            : this(ValvePulseConverter.Default)
        {
        }

        /// <summary>변환기를 지정해 정책을 생성한다.</summary>
        /// <param name="converter">밸브 pulse 변환기.</param>
        /// <exception cref="ArgumentNullException">변환기가 null 일 때.</exception>
        public BandControlPolicy(ValvePulseConverter converter)
        {
            if (converter == null)
            {
                throw new ArgumentNullException("converter");
            }

            _converter = converter;
        }

        /// <inheritdoc />
        public ControlDecision Step(ChainControlContext context)
        {
            if (context == null)
            {
                throw new ArgumentNullException("context");
            }

            // ── 0단계: 제어 가능 여부 확인 ────────────────────────────────────────
            // 측정값 품질이 나쁘거나 액추에이터가 통신 불량/알람 상태이면 지령을 내지 않는다.
            // "모르는 상태에서 움직이지 않는다"가 장비 제어의 기본 원칙이다.
            if (!context.IsReadyForControl)
            {
                context.Runtime.SetResult(ControlResult.Skipped);
                return ControlDecision.WithoutCommand(
                    ControlResult.Skipped, BuildSkipReason(context));
            }

            double pv = context.ProcessValuePa;
            double low = context.Mode.LowLimitPa;
            double high = context.Mode.HighLimitPa;

            // ── 1단계: 정상 대역 판정 ─────────────────────────────────────────────
            if (pv > low && pv < high)
            {
                context.Runtime.ClearDeviation();
                context.Runtime.SetResult(ControlResult.InBand);
                return ControlDecision.WithoutCommand(
                    ControlResult.InBand,
                    Format("정상 대역 ({0:F2} < {1:F2} < {2:F2} Pa) — 밸브/팬 유지", low, pv, high));
            }

            // 대역을 벗어났으므로 이탈 시간 누적을 시작/계속한다.
            context.Runtime.MarkDeviating(context.NowUtc);

            // ── 2단계: 하한 이탈 (압력 부족 = 과배기) ─────────────────────────────
            if (pv <= low)
            {
                return HandleBelowLowLimit(context, pv, low);
            }

            // ── 3단계: 상한 이탈 (압력 과다) ──────────────────────────────────────
            return HandleAboveHighLimit(context, pv, high);
        }

        /// <summary>
        /// 하한 이탈 처리. 순서도의 "1. 스로틀밸브 위치 감소 / 2. 송풍팬 Off" 분기이다.
        /// 밸브가 이미 완전히 닫혀 있으면 더 이상 대응할 수단이 없어 error 로 확정한다.
        /// </summary>
        /// <param name="context">제어 컨텍스트.</param>
        /// <param name="pv">측정값 [Pa].</param>
        /// <param name="low">대역 하한 [Pa].</param>
        /// <returns>제어 판정.</returns>
        private ControlDecision HandleBelowLowLimit(ChainControlContext context, double pv, double low)
        {
            ValveState valve = context.Valve;
            int minPulse = context.ValveConfig.MinPulse;

            // 밸브가 이미 최소 위치(0도)에 도달했는지 확인한다.
            bool valveFullyClosed = valve.PositionPulse <= minPulse;

            if (valveFullyClosed)
            {
                // 순서도: "압력하한 > 센서 & 스로틀밸브 0도 일때 → error"
                // 단, Time 만큼 지속되어야 확정한다(디바운스). 그 전까지는 이탈 상태로만 표시한다.
                if (context.Runtime.IsDeviationConfirmed(context.Mode))
                {
                    context.Runtime.SetResult(ControlResult.ErrorLow);
                    return ControlDecision.WithoutCommand(
                        ControlResult.ErrorLow,
                        Format("하한 이탈({0:F2} <= {1:F2} Pa) 상태에서 밸브가 이미 완전히 닫힘 — 대응 불가",
                            pv, low));
                }

                context.Runtime.SetResult(ControlResult.DeviatingLow);
                return ControlDecision.WithoutCommand(
                    ControlResult.DeviatingLow,
                    Format("하한 이탈({0:F2} <= {1:F2} Pa), 밸브 완전 닫힘 — 확정 대기 {2:F0}/{3:F0} ms",
                        pv, low, context.Runtime.DeviationElapsedMs, context.Mode.TimeMs));
            }

            List<ActuatorCommand> commands = new List<ActuatorCommand>(2);

            // 밸브 위치 감소. Dwell 이 경과하지 않았으면 이번 스텝은 건너뛴다(헌팅 방지).
            if (context.Runtime.CanActuateValve(context.NowUtc, context.ValveConfig.DwellMs))
            {
                int target = ClampPulse(
                    valve.PositionPulse - context.ValveConfig.StepPulse,
                    context.ValveConfig.MinPulse,
                    context.ValveConfig.MaxPulse);

                if (target != valve.TargetPulse)
                {
                    commands.Add(ActuatorCommand.SetValvePosition(
                        valve.Id, target, CommandPriority.Automatic,
                        Format("하한 이탈 {0:F2} Pa → 밸브 감소 {1}→{2} pulse",
                            pv, valve.PositionPulse, target)));
                }

                // 지령을 실제로 냈든(이동) 아니든(이미 목표치) Dwell 을 갱신한다.
                // 그렇지 않으면 목표 도달 상태에서 매 사이클 이 분기를 재평가하게 된다.
                context.Runtime.MarkValveActuated(context.NowUtc);
            }

            // 순서도에 따라 팬은 OFF. 이미 정지 상태이면 중복 지령을 보내지 않는다.
            // 정지 지령도 Dwell 을 적용한다. 그렇지 않으면 스냅샷이 정지를 반영하기 전까지
            // 매 제어 주기(기본 200ms)마다 동일한 정지 지령이 통신 큐에 쌓인다.
            if ((context.Fan.IsRunning || ResolveFanTarget(context) > 0.0)
                && context.Runtime.CanActuateFan(context.NowUtc, context.FanConfig.DwellMs))
            {
                commands.Add(ActuatorCommand.StopFan(
                    context.Fan.Id, CommandPriority.Automatic,
                    Format("하한 이탈 {0:F2} Pa → 송풍팬 정지", pv)));

                // 정지도 지령이므로 적분 상태를 0 으로 되돌린다.
                context.Runtime.MarkFanActuated(context.NowUtc, 0.0);
            }

            context.Runtime.SetResult(ControlResult.DeviatingLow);
            return new ControlDecision(
                ControlResult.DeviatingLow, commands,
                Format("하한 이탈({0:F2} <= {1:F2} Pa) 대응 중 — 밸브 감소 + 팬 정지", pv, low));
        }

        /// <summary>
        /// 상한 이탈 처리. 순서도의 "1. 스로틀밸브 위치 증가" → (밸브 90도 포화 시) "1. 송풍팬 속도 증가" 분기이다.
        /// 밸브와 팬이 모두 포화되면 error 로 확정한다.
        /// </summary>
        /// <param name="context">제어 컨텍스트.</param>
        /// <param name="pv">측정값 [Pa].</param>
        /// <param name="high">대역 상한 [Pa].</param>
        /// <returns>제어 판정.</returns>
        private ControlDecision HandleAboveHighLimit(ChainControlContext context, double pv, double high)
        {
            ValveState valve = context.Valve;
            int maxPulse = context.ValveConfig.MaxPulse;

            // ── 3-1. 밸브에 여유가 있으면 밸브를 먼저 연다 (1순위 액추에이터) ──────
            if (valve.PositionPulse < maxPulse)
            {
                List<ActuatorCommand> commands = new List<ActuatorCommand>(1);

                if (context.Runtime.CanActuateValve(context.NowUtc, context.ValveConfig.DwellMs))
                {
                    int target = ClampPulse(
                        valve.PositionPulse + context.ValveConfig.StepPulse,
                        context.ValveConfig.MinPulse,
                        maxPulse);

                    if (target != valve.TargetPulse)
                    {
                        commands.Add(ActuatorCommand.SetValvePosition(
                            valve.Id, target, CommandPriority.Automatic,
                            Format("상한 이탈 {0:F2} Pa → 밸브 증가 {1}→{2} pulse",
                                pv, valve.PositionPulse, target)));
                    }

                    // 하한 분기와 동일하게, 지령 유무와 무관하게 Dwell 을 갱신한다.
                    context.Runtime.MarkValveActuated(context.NowUtc);
                }

                context.Runtime.SetResult(ControlResult.DeviatingHigh);
                return new ControlDecision(
                    ControlResult.DeviatingHigh, commands,
                    Format("상한 이탈({0:F2} >= {1:F2} Pa) 대응 중 — 밸브 증가 ({2:F1}도)",
                        pv, high, _converter.PulseToDegree(valve.PositionPulse)));
            }

            // ── 3-2. 밸브가 포화(90도)되었으므로 팬을 증속한다 (2순위 액추에이터) ──
            // 팬 최대 RPM 사양이 미확보(0)이면 증속 자체가 불가능하므로 Skipped 로 처리한다.
            if (!context.FanConfig.IsUsableForAutoControl)
            {
                context.Runtime.SetResult(ControlResult.Skipped);
                return ControlDecision.WithoutCommand(
                    ControlResult.Skipped,
                    Format("상한 이탈({0:F2} Pa) + 밸브 포화이나 팬 MaxRpm 미설정 — 증속 불가 (Open Issue #20)",
                        pv));
            }

            // 적분 상태는 제어기가 마지막으로 지령한 값이다. 측정값이 아니다.
            // 측정값을 쓰면 부하 때문에 MaxRpm 에 도달하지 못할 때 포화가 영영 감지되지 않는다.
            double currentTarget = ResolveFanTarget(context);
            bool fanAtMax = currentTarget >= context.FanConfig.MaxRpm - context.FanConfig.RpmTolerance;

            if (fanAtMax)
            {
                // 순서도: "압력상한 < 센서 & 송풍팬 속도 Max → error"
                if (context.Runtime.IsDeviationConfirmed(context.Mode))
                {
                    context.Runtime.SetResult(ControlResult.ErrorHigh);
                    return ControlDecision.WithoutCommand(
                        ControlResult.ErrorHigh,
                        Format("상한 이탈({0:F2} >= {1:F2} Pa) + 밸브 90도 + 팬 최대({2:F0} RPM) — 대응 불가",
                            pv, high, context.FanConfig.MaxRpm));
                }

                context.Runtime.SetResult(ControlResult.DeviatingHigh);
                return ControlDecision.WithoutCommand(
                    ControlResult.DeviatingHigh,
                    Format("상한 이탈({0:F2} Pa), 밸브·팬 모두 포화 — 확정 대기 {1:F0}/{2:F0} ms",
                        pv, context.Runtime.DeviationElapsedMs, context.Mode.TimeMs));
            }

            List<ActuatorCommand> fanCommands = new List<ActuatorCommand>(1);

            if (context.Runtime.CanActuateFan(context.NowUtc, context.FanConfig.DwellMs))
            {
                double nextRpm = ClampRpm(
                    currentTarget + context.FanConfig.StepRpm,
                    context.FanConfig.MinRpm,
                    context.FanConfig.MaxRpm);

                fanCommands.Add(ActuatorCommand.SetFanRpm(
                    context.Fan.Id, nextRpm, CommandPriority.Automatic,
                    Format("상한 이탈 {0:F2} Pa + 밸브 포화 → 팬 증속 {1:F0}→{2:F0} RPM",
                        pv, currentTarget, nextRpm)));
                context.Runtime.MarkFanActuated(context.NowUtc, nextRpm);
            }

            context.Runtime.SetResult(ControlResult.DeviatingHigh);
            return new ControlDecision(
                ControlResult.DeviatingHigh, fanCommands,
                Format("상한 이탈({0:F2} >= {1:F2} Pa) 대응 중 — 밸브 포화, 팬 증속", pv, high));
        }

        /// <summary>
        /// 팬 제어의 현재값(적분 상태)을 구한다.
        /// </summary>
        /// <param name="context">제어 컨텍스트.</param>
        /// <returns>마지막 지령값 [RPM]. 지령 이력이 없으면 측정값으로 대체한다.</returns>
        /// <remarks>
        /// 지령 이력이 없는 경우는 자동 운전 진입 직후뿐이다. 그때만 측정값을 쓴다.
        /// 이후에는 항상 지령값을 쓴다.
        /// </remarks>
        private static double ResolveFanTarget(ChainControlContext context)
        {
            double? commanded = context.Runtime.LastFanCommandRpm;

            if (commanded.HasValue)
            {
                return commanded.Value;
            }

            return context.Fan == null ? 0.0 : context.Fan.Rpm;
        }

        /// <summary>제어를 건너뛴 사유 문자열을 만든다.</summary>
        /// <param name="context">제어 컨텍스트.</param>
        /// <returns>사유 설명.</returns>
        private static string BuildSkipReason(ChainControlContext context)
        {
            if (context.ProcessQuality != Quality.Good)
            {
                return Format("측정값 품질 불량({0}) — 제어 건너뜀", context.ProcessQuality);
            }

            if (context.Valve == null)
            {
                return "밸브 상태 없음 — 제어 건너뜀";
            }

            if (!context.Valve.IsControllable)
            {
                return Format("밸브 제어 불가(품질={0}, 원점={1}, 알람={2}) — 제어 건너뜀",
                    context.Valve.Quality, context.Valve.IsHomeDone, context.Valve.AlarmCode);
            }

            if (context.Fan == null)
            {
                return "팬 상태 없음 — 제어 건너뜀";
            }

            return Format("팬 제어 불가(품질={0}, 알람={1}) — 제어 건너뜀",
                context.Fan.Quality, context.Fan.AlarmCode);
        }

        /// <summary>pulse 값을 허용 범위로 제한한다.</summary>
        /// <param name="value">입력 pulse.</param>
        /// <param name="min">하한.</param>
        /// <param name="max">상한.</param>
        /// <returns>제한된 pulse.</returns>
        private static int ClampPulse(int value, int min, int max)
        {
            if (value < min)
            {
                return min;
            }

            return value > max ? max : value;
        }

        /// <summary>RPM 값을 허용 범위로 제한한다.</summary>
        /// <param name="value">입력 RPM.</param>
        /// <param name="min">하한.</param>
        /// <param name="max">상한.</param>
        /// <returns>제한된 RPM.</returns>
        private static double ClampRpm(double value, double min, double max)
        {
            if (value < min)
            {
                return min;
            }

            return value > max ? max : value;
        }

        /// <summary>로캘 무관 문자열 포맷 도우미.</summary>
        /// <param name="format">형식 문자열.</param>
        /// <param name="args">인자.</param>
        /// <returns>포맷된 문자열.</returns>
        private static string Format(string format, params object[] args)
        {
            return string.Format(CultureInfo.InvariantCulture, format, args);
        }
    }
}
