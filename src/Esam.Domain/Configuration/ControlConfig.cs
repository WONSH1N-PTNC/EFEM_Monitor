using System;
using System.Globalization;
using System.Collections.Generic;
using Esam.Domain.Control;

namespace Esam.Domain.Configuration
{
    /// <summary>제어 알고리즘 종류.</summary>
    public enum ControlPolicyKind
    {
        /// <summary>ESAM 순서도 그대로의 스텝형 밴드 제어. 1차 릴리스 기본값.</summary>
        Band = 0,

        /// <summary>PID 제어. 향후 옵션(DESIGN.md Open Issue #4).</summary>
        Pid = 1
    }

    /// <summary>
    /// 제어 전반의 설정. control.json 전체에 대응한다.
    /// </summary>
    public sealed class ControlConfig
    {
        /// <summary>적용 중인 센서 모드.</summary>
        public SensorMode ActiveMode { get; set; }

        /// <summary>사용할 제어 알고리즘.</summary>
        public ControlPolicyKind Policy { get; set; }

        /// <summary>제어 루프 주기 [ms]. 통신 폴링 주기와 독립적으로 동작한다.</summary>
        public int ControlPeriodMs { get; set; }

        /// <summary>
        /// Sensor1 모드에서 참조할 센서 지정. "S1-1" 같은 고정 ID, 또는 "Average", "PerChain".
        /// (DESIGN.md Open Issue #16)
        /// </summary>
        public string Sensor1Reference { get; set; }

        /// <summary>센서 모드별 제어 파라미터.</summary>
        public IDictionary<SensorMode, ModeSetting> Modes { get; set; }

        /// <summary>스로틀밸브 구동 파라미터.</summary>
        public ValveActuatorConfig Valve { get; set; }

        /// <summary>송풍팬 구동 파라미터.</summary>
        public FanActuatorConfig Fan { get; set; }

        /// <summary>체인 구성 정의.</summary>
        public IList<ChainDefinition> Chains { get; set; }

        /// <summary>
        /// 측정값에 적용할 이동평균 창 크기. 1 이면 필터를 적용하지 않는다.
        /// 수 Pa 단위 제어이므로 노이즈 억제가 중요하다.
        /// </summary>
        public int FilterWindowSize { get; set; }

        /// <summary>원시값과 필터값을 함께 로깅할지 여부. Phase 5 튜닝용.</summary>
        public bool LogFilteredAndRaw { get; set; }

        /// <summary>
        /// 안전 입력(EMO·메인 차단기·도어)을 읽을 PLC 가 구성되어 있는지 여부.
        /// </summary>
        /// <remarks>
        /// <para>IL-04(안전 입력 신뢰 불가)의 판정 여부를 결정한다.
        /// PLC 가 구성되어 있는데 응답하지 않으면 EMO 판정 자체를 신뢰할 수 없으므로 전체 정지한다.
        /// 반대로 PLC 가 아직 구성에 없으면 IL-04 가 항상 발동해 어떤 검증도 불가능해진다.</para>
        /// <para><b>이 값이 false 라는 것은 안전 입력이 하나도 없다는 뜻이다.</b>
        /// 실장비 운전에서는 있을 수 없는 상태이므로, 런타임 조립 단계에서 반드시
        /// 구성 경고로 보고해 작업자가 모르고 넘어가지 않게 해야 한다.</para>
        /// <para>이 값은 통신 구성(device-map)에서 파생되므로 조립 루트가 채운다.
        /// Domain 이 Communication 을 참조하지 않기 위한 배치다.</para>
        /// </remarks>
        public bool SafetyInputsConfigured { get; set; }

        /// <summary>ESAM 문서 기준 기본값으로 초기화한다.</summary>
        public ControlConfig()
        {
            ActiveMode = SensorMode.Sensor2;
            Policy = ControlPolicyKind.Band;
            ControlPeriodMs = 200;
            Sensor1Reference = "S1-1";
            FilterWindowSize = 5;
            LogFilteredAndRaw = true;

            // ESAM 운용방법 설명자료 p.6 Config 화면의 기본값
            Modes = new Dictionary<SensorMode, ModeSetting>
            {
                { SensorMode.Sensor1, new ModeSetting(6.0, 2.0, 60.0) },
                { SensorMode.Sensor2, new ModeSetting(-10.0, 30.0, 120.0) },
                { SensorMode.Sensor3, new ModeSetting(-200.0, 100.0, 300.0) }
            };

            Valve = new ValveActuatorConfig();
            Fan = new FanActuatorConfig();
            Chains = new List<ChainDefinition>();
        }

        /// <summary>
        /// 운전 파라미터. <c>recipe.json</c> 에서 로드해 조립 루트가 주입한다. null 이면 모드별 공통값을 쓴다.
        /// </summary>
        /// <remarks>
        /// <para>이 필드는 <c>control.json</c> 에서 역직렬화되지 않는다. 두 파일은 역할이 다르고
        /// 바꾸는 빈도도 다르므로 따로 로드해 여기서 합친다.</para>
        /// <para><b>null 이면 종전처럼 모드별 공통값으로 동작한다.</b> 레시피 도입 전 구성과
        /// 기존 테스트가 그대로 통과하도록 남긴 이행 경로다. 조립 루트가 이 사실을 구성 경고로 올린다.
        /// 조용히 넘어가면 센서별 설정을 넣었다고 착각한 채 공통값으로 운전하게 된다.</para>
        /// </remarks>
        public RecipeDefinition Recipe { get; set; }

        /// <summary>
        /// 지정 센서의 제어 파라미터를 가져온다. 레시피의 센서별 값과 모드별 시간을 합친다.
        /// </summary>
        /// <param name="sensorId">제어 기준 센서의 디바이스 ID.</param>
        /// <param name="mode">센서 모드. 이탈 확정 시간을 여기서 가져온다.</param>
        /// <returns>제어 파라미터. 레시피가 있는데 해당 센서가 없으면 null.</returns>
        /// <remarks>
        /// <para>세 갈래로 나뉜다.</para>
        /// <list type="number">
        ///   <item><description><b>레시피 없음</b> → 모드별 공통값. 레시피 도입 전 동작이다</description></item>
        ///   <item><description><b>레시피에 센서 있음</b> → 센서별 값 + 모드별 시간</description></item>
        ///   <item><description><b>레시피에 센서 없음</b> → <c>null</c></description></item>
        /// </list>
        /// <para>세 번째에서 공통값으로 대체하지 않는 이유가 있다. 레시피를 도입한 구성에서
        /// 센서 하나가 빠졌다면 그것은 설정 오류다. 공통값으로 메우면 <b>그 체인만 조용히
        /// 다른 기준으로 제어</b>되고, 화면에는 레시피 값이 적용된 것처럼 보인다.
        /// 로드 검증이 먼저 걸러야 하고, 그래도 새면 여기서 드러나야 한다.</para>
        /// </remarks>
        public ModeSetting GetSetting(string sensorId, SensorMode mode)
        {
            ModeSetting common = GetMode(mode);

            if (Recipe == null)
            {
                return common;
            }

            SensorSetting sensor = Recipe.Find(sensorId);

            return sensor == null ? null : sensor.ToModeSetting(common.TimeSec);
        }

        /// <summary>지정한 센서 모드의 파라미터를 가져온다.</summary>
        /// <param name="mode">센서 모드.</param>
        /// <returns>해당 모드의 파라미터.</returns>
        /// <exception cref="InvalidOperationException">해당 모드의 설정이 없을 때.</exception>
        /// <remarks>
        /// 모드별 공통값이다. 레시피 도입 후 이 값에서 실제로 쓰이는 것은
        /// <b>이탈 확정 시간(Time)뿐</b>이며, 설정값·상하한은 <see cref="GetSetting"/> 이
        /// 레시피에서 가져온다. 레시피가 없는 구성에서는 전체가 폴백으로 쓰인다.
        /// </remarks>
        public ModeSetting GetMode(SensorMode mode)
        {
            ModeSetting setting;
            if (Modes != null && Modes.TryGetValue(mode, out setting) && setting != null)
            {
                return setting;
            }

            throw new InvalidOperationException(
                string.Format(CultureInfo.InvariantCulture, "센서 모드 {0} 의 제어 설정이 정의되어 있지 않습니다.", mode));
        }

        /// <summary>
        /// 설정 전체의 유효성을 검증한다(COMM_MAP.md 4.7 검증 규칙).
        /// 실패 항목이 하나라도 있으면 자동 제어 진입을 차단해야 한다.
        /// </summary>
        /// <param name="errors">검증 실패 사유 목록. 유효하면 빈 목록.</param>
        /// <returns>유효하면 true.</returns>
        public bool Validate(out IList<string> errors)
        {
            List<string> found = new List<string>();

            if (ControlPeriodMs <= 0)
            {
                found.Add("ControlPeriodMs 는 0보다 커야 합니다.");
            }

            if (FilterWindowSize < 1)
            {
                found.Add("FilterWindowSize 는 1 이상이어야 합니다.");
            }

            if (Modes == null || Modes.Count == 0)
            {
                found.Add("센서 모드 설정(Modes)이 비어 있습니다.");
            }
            else
            {
                foreach (KeyValuePair<SensorMode, ModeSetting> pair in Modes)
                {
                    string modeError;
                    if (pair.Value == null)
                    {
                        found.Add(string.Format(CultureInfo.InvariantCulture, "센서 모드 {0} 의 설정이 null 입니다.", pair.Key));
                    }
                    else if (!pair.Value.Validate(out modeError))
                    {
                        found.Add(string.Format(CultureInfo.InvariantCulture, "센서 모드 {0}: {1}", pair.Key, modeError));
                    }
                }
            }

            string actuatorError;
            if (Valve == null)
            {
                found.Add("밸브 설정(Valve)이 null 입니다.");
            }
            else if (!Valve.Validate(out actuatorError))
            {
                found.Add(actuatorError);
            }

            if (Fan == null)
            {
                found.Add("팬 설정(Fan)이 null 입니다.");
            }
            else if (!Fan.Validate(out actuatorError))
            {
                found.Add(actuatorError);
            }

            if (Chains == null || Chains.Count == 0)
            {
                found.Add("체인 정의(Chains)가 비어 있습니다.");
            }
            else
            {
                HashSet<int> seenIds = new HashSet<int>();
                foreach (ChainDefinition chain in Chains)
                {
                    string chainError;
                    if (chain == null)
                    {
                        found.Add("체인 정의에 null 항목이 있습니다.");
                        continue;
                    }

                    if (!chain.Validate(out chainError))
                    {
                        found.Add(chainError);
                    }

                    if (!seenIds.Add(chain.Id))
                    {
                        found.Add(string.Format(CultureInfo.InvariantCulture, "체인 번호 {0} 가 중복되었습니다.", chain.Id));
                    }
                }
            }

            if (string.IsNullOrEmpty(Sensor1Reference))
            {
                found.Add("Sensor1Reference 가 지정되지 않았습니다.");
            }

            errors = found;
            return found.Count == 0;
        }
    }
}
