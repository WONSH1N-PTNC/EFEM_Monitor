using System;
using Esam.Domain.Configuration;
using Esam.Domain.Models;

namespace Esam.Domain.Control
{
    /// <summary>
    /// 1회 제어 스텝에 필요한 모든 입력을 모은 컨텍스트.
    /// 제어 정책은 이 객체만 보고 판단하므로 스냅샷 탐색 로직과 알고리즘이 분리된다.
    /// </summary>
    public sealed class ChainControlContext
    {
        /// <summary>체인의 유지 상태(가변).</summary>
        public ChainRuntime Runtime { get; private set; }

        /// <summary>제어 기준 측정값 [Pa]. Sensor1 Average 모드에서는 평균값이 들어온다.</summary>
        public double ProcessValuePa { get; private set; }

        /// <summary>측정값의 신뢰도.</summary>
        public Quality ProcessQuality { get; private set; }

        /// <summary>현재 밸브 상태.</summary>
        public ValveState Valve { get; private set; }

        /// <summary>현재 팬 상태.</summary>
        public FanState Fan { get; private set; }

        /// <summary>적용 중인 모드 설정(Setpoint/Band/Time).</summary>
        public ModeSetting Mode { get; private set; }

        /// <summary>밸브 구동 파라미터.</summary>
        public ValveActuatorConfig ValveConfig { get; private set; }

        /// <summary>팬 구동 파라미터.</summary>
        public FanActuatorConfig FanConfig { get; private set; }

        /// <summary>현재 시각(UTC).</summary>
        public DateTime NowUtc { get; private set; }

        /// <summary>제어를 수행할 수 있는 최소 조건이 충족되었는지 여부.</summary>
        public bool IsReadyForControl
        {
            get
            {
                return ProcessQuality == Quality.Good
                       && Valve != null && Valve.IsControllable
                       && Fan != null && Fan.IsControllable;
            }
        }

        /// <summary>제어 컨텍스트를 생성한다.</summary>
        /// <param name="runtime">체인 유지 상태.</param>
        /// <param name="processValuePa">제어 기준 측정값 [Pa].</param>
        /// <param name="processQuality">측정값 신뢰도.</param>
        /// <param name="valve">밸브 상태.</param>
        /// <param name="fan">팬 상태.</param>
        /// <param name="mode">모드 설정.</param>
        /// <param name="valveConfig">밸브 구동 파라미터.</param>
        /// <param name="fanConfig">팬 구동 파라미터.</param>
        /// <param name="nowUtc">현재 시각(UTC).</param>
        /// <exception cref="ArgumentNullException">필수 인자가 null 일 때.</exception>
        public ChainControlContext(
            ChainRuntime runtime,
            double processValuePa,
            Quality processQuality,
            ValveState valve,
            FanState fan,
            ModeSetting mode,
            ValveActuatorConfig valveConfig,
            FanActuatorConfig fanConfig,
            DateTime nowUtc)
        {
            if (runtime == null)
            {
                throw new ArgumentNullException("runtime");
            }

            if (mode == null)
            {
                throw new ArgumentNullException("mode");
            }

            if (valveConfig == null)
            {
                throw new ArgumentNullException("valveConfig");
            }

            if (fanConfig == null)
            {
                throw new ArgumentNullException("fanConfig");
            }

            Runtime = runtime;
            ProcessValuePa = processValuePa;
            ProcessQuality = processQuality;
            Valve = valve;
            Fan = fan;
            Mode = mode;
            ValveConfig = valveConfig;
            FanConfig = fanConfig;
            NowUtc = nowUtc;
        }
    }
}
