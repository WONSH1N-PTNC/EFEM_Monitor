# ESAM HMI/Monitoring System — 프로그램 설계서 (Draft v0.1)

> EFEM Smart Airflow & Pressure Management System
> 대상: Phase 4(HMI/모니터링) 주업무, Phase 3(제어로직) / Phase 5(현장튜닝) 연계
> 근거 문서: `docs/EFEM_Plan.md`, `docs/DSE_통신 자료_260710.xlsx`, `docs/ESAM 운용방법 설명자료_260309 V1.4.6.pdf`
> 작성일: 2026-07-31 / 상태: **검토 요청 (미승인)**

---

## 0. 확정 사항 (2026-07-31 협의)

| 항목 | 결정 |
|---|---|
| Runtime | .NET Framework 4.7.2 / C# 7.3 |
| UI | **WPF + MVVM** (ScottPlot.WPF 트렌드 차트) |
| 송풍팬 | **RS-485 Modbus RTU 직결** (CAN 폐지). **전용 포트 BUS_C** 배치 — 2026-07-31 변경 |
| 데이터 로그 | **SQLite(WAL) 적재 + CSV Export** |
| 산출물 범위 | 설계문서 + 프로젝트 골격(구조/인터페이스 제안). 코드 작성은 승인 후 |

---

## 1. 시스템 범위

### 1.1 In Scope
- 13ch 차압센서, 5 스로틀밸브, 5 송풍팬 실시간 모니터링 및 자동 압력제어
- 부속 센서: 온습도(EFEM), 풍속 3ch, 파티클, 컨트롤박스 온도, 쿨링팬
- FFU / MFC 2ch (확장, 사양 미정)
- Sensor 1/2/3 Mode 자동제어 (ESAM PDF p.10~12 순서도)
- 알람 31종 + 인터록 1종
- Data Log 적재 / 조회 / CSV 반출
- 사용자 권한(Operator / Maintenance / Engineer) 및 로그인
- 외부 모니터링(FDC) 연동 인터페이스 계층 (프로토콜 미정 → 어댑터로 격리)

### 1.2 Out of Scope
- PLC(XBM-DR16S) 래더 로직
- 밸브/팬 임베디드 펌웨어
- 중앙 원격제어 서버 (단, HMI에 원격 API 훅만 예비)

---

## 2. 하드웨어 / 통신 토폴로지

### 2.1 채널 구성

```
                        ┌──────────────── PC (ESAM HMI) ────────────────┐
                        │                                                │
   COM-A  RS-485 19200 8N1 ──┬─ 차압센서 1-1..3-5   (Slave 1~13)  WTDM-550
                             ├─ 온습도센서          (Slave 15)     THD-R-T
                             ├─ 풍속센서 1~3        (Slave 20~22)  BECK985
                             └─ PLC XBM-DR16S      (Slave 25)     D100~D105, D10.x

   COM-B  RS-485 38400    ──┬─ 스로틀밸브 2-1..2-5  (Slave 1~5)   자사 제작
                             └─ MFC 1~2            (Slave 20,21)  ※사양 미정

   COM-C  RS-485 38400(TBD) ── 송풍팬 2-1..2-5      (Slave 1~5)   자사 제작
                                                    ※Modbus 직결 (CAN 폐지)
```

> **변경 이력 (2026-07-31)**: 송풍팬을 CAN → **RS-485 Modbus RTU 직결**로 변경. CAN 게이트웨이 및 CAN ID(0x721~0x725) 관련 설계 항목 전량 폐기.
> 이에 따라 **시스템 전체가 단일 프로토콜(Modbus RTU)** 로 통일되어 다음 이점이 생깁니다.
> - `ICanChannel` / 게이트웨이 매핑 계층 삭제 → 인프라 계층 단순화
> - 모든 디바이스가 동일한 `ModbusPortWorker` + `IDeviceDriver` 파이프라인 사용 → 진단·재시도·타임아웃 정책 일원화
> - 게이트웨이 중계로 인한 추가 지연 및 단일 장애점(SPOF) 제거
> - 시뮬레이터(`SimulatedTransport`)가 전 디바이스를 동일 방식으로 커버

### 2.2 ⚠ 설계상 반드시 확인해야 할 사항

**(A) 슬레이브 ID 충돌**
- 차압센서(A) ID 1~13 ↔ 스로틀밸브(B) ID 1~5 ↔ **송풍팬(C) ID 1~5**
- 풍속센서(A) ID 20~22 ↔ MFC(B) ID 20~21

→ **물리 포트가 반드시 분리되어야 함.** 동일 버스에 올릴 경우 주소 재할당 필수.
설계는 "포트 = 독립 버스" 전제로 진행하며, 포트당 슬레이브 ID 유일성을 Config 로드시 검증(Fail-fast).

**(B) 100ms 폴링 주기 실현 가능성 — 재검토 필요**

RS-485 19200bps, 8N1(10 bit/byte) → **0.52 ms/byte**

| 항목 | 값 |
|---|---|
| Request (Read Holding Registers) | 8 byte = 4.2 ms |
| Response (2 register) | 9 byte = 4.7 ms |
| 슬레이브 응답 지연 (WTDM-550 등 전형값) | 5 ~ 20 ms |
| Frame 간 t3.5 silent interval | ≈ 2 ms |
| **트랜잭션 1건 합계** | **약 16 ~ 31 ms** |

- COM-A 전체 18대 / 19 트랜잭션(PLC는 온도워드 + 디지털워드 2건) 순차 폴링 → **약 300 ~ 590 ms/cycle**
- 차압센서 13대만 폴링해도 → **약 210 ~ 400 ms/cycle**

즉 **단일 버스에서 13ch 100ms 갱신은 물리적으로 불가능**합니다. 대응안:

| 안 | 내용 | 평가 |
|---|---|---|
| **안 1 (권장)** | 차압센서를 **3~4개 RS-485 포트로 분할** (포트당 4~5대) → 포트당 4대 시 **63~123 ms**, 포트 병렬 폴링 | 하드웨어 추가(USB-485 허브 or 다포트 카드) 필요. 목표 달성 확실 |
| 안 2 | Baud를 115200으로 상향 (센서 지원 여부 확인) | 전송시간은 줄지만 **슬레이브 응답지연이 지배적** → 13ch **107~302 ms**. 응답지연이 실측 5ms 수준이면 단독으로도 달성 가능하나 보장 불가 |
| 안 3 | **폴링 티어링** — 차압센서 Fast tier(전 사이클), 온습도/풍속/PLC Slow tier(1~5s) | 필수 적용. 단독으로는 100ms 미달 |
| 안 4 | 제어주기 목표를 **250~300ms**로 현실화 | 수 Pa 차압계는 시정수가 크므로 제어 성능상 수용 가능성 높음 |

→ **협의 필요 항목 #1**: 목표 갱신주기를 100ms로 유지할 것인지(→ 안 1 하드웨어 반영), 250~300ms로 현실화할 것인지. 설계는 **폴링 주기를 Config로 티어별 분리**하여 두 경우 모두 수용하도록 합니다.

**(C) 포트별 폴링 예산 (Fast tier 기준)**

| 포트 | Baud | Fast tier 트랜잭션 | 예상 사이클 |
|---|---|---|---|
| BUS_A | 19200 | 차압센서 13 + PLC 디지털 1 = **14** | **220 ~ 430 ms** |
| BUS_B | 38400 | 밸브 5 × (위치 + Motion status) = **10** | **103 ~ 253 ms** |
| BUS_C | 38400 | 팬 5 × (RPM + 상태) = **10** | **103 ~ 253 ms** |

세 포트는 **병렬 폴링**되므로 전체 사이클은 최댓값인 BUS_A가 결정합니다 → 여전히 **BUS_A 분할이 100ms 달성의 관건**(안 1).

**BUS_C 최적화 여지**: 팬 5대의 RPM·상태 레지스터가 **연속 주소로 배치**되어 있고 팬이 멀티드롭 응답을 지원한다면 트랜잭션 수를 줄일 수 있습니다. 자사 제작품이므로 **레지스터 배치를 설계 단계에서 조정 요청 가능**합니다. → **협의 필요 항목 #9**

**(D) 송풍팬 Modbus 레지스터 명세 미확보**
RPM 설정/현재값/ON·OFF/알람 레지스터 명세가 아직 없습니다. → `IFanDriver` 인터페이스로 격리하고, 명세 확보 전까지 시뮬레이터 드라이버로 개발 진행 (밸브와 동일 전략).

---

## 3. 소프트웨어 아키텍처

### 3.1 레이어

```
┌─────────────────────────────────────────────────────────────┐
│ Presentation (WPF / MVVM)                                    │
│  Views: Operate, Maintenance, Config, IO, DataLog, Status    │
│  ViewModels ── DispatcherTimer(100ms) pull ─┐                │
└──────────────────────────────────────────────┼───────────────┘
                                               │ Snapshot (immutable)
┌──────────────────────────────────────────────▼───────────────┐
│ Application / Services                                        │
│  DataStore(스냅샷)  ControlEngine  AlarmEngine                │
│  InterlockGuard     DataLogger     RecipeService  AuthService │
└──────────────────────────────────────────────┬───────────────┘
                                               │
┌──────────────────────────────────────────────▼───────────────┐
│ Domain (순수 로직, 하드웨어 무의존 → 단위테스트 대상)          │
│  DeviceModel  SensorModeStateMachine  ControlPolicy           │
│  AlarmRule    InterlockRule           EngineeringUnitConverter│
└──────────────────────────────────────────────┬───────────────┘
                                               │
┌──────────────────────────────────────────────▼───────────────┐
│ Infrastructure                                                │
│  ModbusPortWorker(포트당 1)  IPressureSensorDriver            │
│  IThrottleValveDriver  IFanDriver  IPlcDriver                 │
│  SqliteLogRepository  ConfigLoader  NLog                      │
└───────────────────────────────────────────────────────────────┘
```

### 3.2 스레딩 모델 (핵심)

```
[ModbusPortWorker: COM-A]  Task + CancellationToken, 단일 스레드
     │  Fast tier 매 사이클 / Slow tier 카운터 기반
     │  ※ RS-485 반이중 → 포트당 트랜잭션 직렬화 (SemaphoreSlim(1,1))
     ├─ InterlockGuard.Evaluate(raw)   ← 인터록은 폴링 스레드에서 즉시 실행
     └─> DataStore.Publish(snapshot)   ← Volatile.Write / Interlocked.Exchange

[ModbusPortWorker: COM-B, COM-C]  동일 구조, 포트별 독립 병렬

[ControlEngine]  독립 Timer (Config 주기, 기본 200ms)
     └─ DataStore.Current 읽어 상태머신 1 step → 명령을 포트 워커 큐에 Enqueue

[DataLogger]     BlockingCollection 소비, 500ms 배치 트랜잭션 insert

[UI]  DispatcherTimer 100ms → DataStore.Current pull → ViewModel 프로퍼티 갱신
```

**원칙**
1. 통신 스레드는 UI를 절대 직접 건드리지 않는다 (Dispatcher.Invoke 금지). UI가 스냅샷을 **끌어간다(pull)**.
2. 스냅샷은 불변(immutable). 부분 갱신으로 인한 tearing 방지.
3. **인터록은 폴링 스레드 내에서 판정·실행.** 서비스/UI 경유 시 지연이 누적되어 안전기능으로 성립하지 않음.
4. 명령 Write는 포트 워커의 우선순위 큐를 통해서만 수행 (인터록 > 수동 > 자동).

### 3.3 데이터 모델 (스냅샷)

```
SystemSnapshot
 ├ DateTime TimestampUtc
 ├ PressureReading[13]   { Id, Pa, Raw, IsValid, Quality, LastUpdateUtc }
 ├ ValveState[5]         { Id, PositionPulse, PositionDeg, Percent, MotionStatus, AlarmCode, HomeDone }
 ├ FanState[5]           { Id, Rpm, SetRpm, IsOn, AlarmCode }
 ├ AirVelocity[3], Temperature{Efem,ControlBox,Fan[5],Panel}, Humidity, Particle
 ├ FfuState, MfcState[2]
 ├ PlcDigital            { FanStopAlarm[5], CtrlBoxFanAlarm, Emo, Door, MainBreaker }
 ├ ControlState          { Mode(Sensor1/2/3), Phase(Idle..AutoControl), ChainStatus[5] }
 └ AlarmSummary          { ActiveCount, HighestSeverity, ActiveIds[] }
```

---

## 4. 제어 로직 설계

### 4.1 전체 상태머신

```
        ┌──────┐
        │ Idle │
        └──┬───┘  Start
           ▼
      ┌─────────┐   통신 확인 실패
      │  Init   │──────────────────► Fault
      └────┬────┘
           ▼  (전원 ON 후 HOME 필수 — 밸브 명세 요구사항)
   ┌───────────────┐  Timeout/Alarm
   │ ValveHoming   │─────────────────► Fault
   └───────┬───────┘
           ▼
      ┌─────────┐
      │  Ready  │◄──────────┐
      └────┬────┘           │ Stop
   Auto    ▼                │
   ┌───────────────┐        │
   │  AutoControl  │────────┘
   │ (Sensor1/2/3) │
   └───┬───────┬───┘
       │       │ S3 High Limit
  Alarm│       ▼
       │  ┌──────────┐   밸브 Close + 팬 OFF (즉시)
       │  │ Interlock│
       │  └────┬─────┘
       ▼       ▼
   ┌─────────────┐  Reset(권한 필요)
   │    Fault    │──────────────► Ready
   └─────────────┘

   EMO / Door Open / Main Breaker OFF → 어느 상태에서든 즉시 SafeStop
```

### 4.2 Sensor Mode 밴드 제어 (PDF p.10~12 순서도 정규화)

**Sensor 1/2/3 Mode는 동일 알고리즘, 파라미터(Setpoint, ±Band)만 다름** → 하나의 `BandControlPolicy`로 구현하고 파라미터를 주입.

| Mode | Setpoint | Band | 예시 범위 | Time |
|---|---|---|---|---|
| Sensor 1 | 6 Pa | ± 2 Pa | 5 < x < 7 | 1 min |
| Sensor 2 | -10 Pa | ± 30 Pa | -40 < x < 20 | 2 min |
| Sensor 3 | -200 Pa | ± 100 Pa | -300 < x < -100 | 5 min |

> ⚠ Sensor 1은 `± 2 Pa`인데 예시가 `5 < x < 7`(=±1 Pa)입니다. 문서 불일치 → **협의 필요 항목 #2**.
> `Time` 컬럼(1/2/5 min)의 의미도 명시가 필요합니다. 설계는 **"밴드 이탈이 Time 이상 지속되면 알람 확정(디바운스)"**로 해석했습니다. → **협의 필요 항목 #3**

의사코드 (체인 n = 1..5, 각 체인 = 센서 1 + 밸브 1 + 팬 1):

```
Step(chain, pv, sp, band):
    lo = sp - band ; hi = sp + band

    if lo < pv < hi:                       # 정상대역
        HOLD valve ; HOLD fan
        deviationTimer.Reset()
        return Normal

    if pv < lo:                            # 압력 부족(과배기)
        if valve.Deg <= 0 (fully closed):
            deviationTimer.Tick()
            if deviationTimer >= Time: return Error(LowLimit)
        else:
            valve.DecreasePosition(step)   # 위치 감소
            fan.Off()
        return Deviating

    if pv > hi:                            # 압력 과다
        if valve.Deg < 90:
            valve.IncreasePosition(step)   # 위치 증가 (1순위)
        else:
            if fan.Rpm < fan.MaxRpm:
                fan.IncreaseSpeed(step)    # 팬 증속 (2순위)
            else:
                deviationTimer.Tick()
                if deviationTimer >= Time: return Error(HighLimit)
        return Deviating
```

**제어 특성**
- 액추에이터 우선순위: **밸브 위치 > 팬 속도**. 팬은 밸브 포화(90°) 후에만 개입.
- Step 크기(pulse/RPM), 최소 이동간격(dwell time), 히스테리시스는 모두 Config 파라미터. 현장 튜닝(Phase 5) 대상.
- 순서도 자체는 **PID가 아닌 스텝형 밴드 제어**입니다. `EFEM_Plan.md`의 PID는 `IControlPolicy` 하위에 `PidControlPolicy`로 **선택 가능 옵션**으로 예비하되, 1차 릴리스는 순서도 그대로 구현합니다. → **협의 필요 항목 #4**

### 4.3 스로틀밸브 드라이버 (xlsx 「쓰로틀 밸브」 시트 기준)

**Write**

| Address | Value | 동작 | 비고 |
|---|---|---|---|
| 0x6002 | 0x20 | Homing | **전원 ON시 필수** |
| 0x6002 | 0x10 | PR0 Move | 0x6202 위치값으로 이동 |
| 0x6002 | 0x40 | Quick Stop | 인터록/E-Stop 시 사용 |
| 0x6202 | Pulse | PR0 위치 설정 | **90° = 5000 pulse** |
| 0x6203 | 1~5 | PR0 속도 (RPM) | |
| 0x1801 | 0x1111 | Alarm Reset | |

**Read**: `0x602B` 현재위치(pulse), `0x2203` Alarm, `0x1003` Motion Status, `0x0147` HOME ON/OFF

**단위 변환**
```
pulse  = round(percent / 100.0 * 5000)      # 0% = 0 pulse(0°), 100% = 5000 pulse(90°)
degree = pulse / 5000.0 * 90.0
```
> ⚠ 0x6002 값 0x20/0x10/0x40이 **비트 플래그인지 명령 코드인지**, 0x1003 / 0x2203의 비트 정의, Modbus 함수코드(FC03/FC04, FC06/FC16) 명세가 없습니다. → **협의 필요 항목 #5**

**Move 완료 판정**: `0x1003` Motion Status 확인 + `0x602B` 목표 도달(±허용 pulse) 확인, 타임아웃 시 Alarm ⑩ `Throttle valve error`.

---

## 5. 알람 / 인터록 설계

### 5.1 알람 정의 (총 31종, PDF p.9)

| Group | ID 범위 | 내용 |
|---|---|---|
| Process | A01~A17 | MFC1/2 flow error, FDC comm error, Cooling fan error, Temp(CtrlBox/EFEM) High, Humidity High/Low, Particle High, Throttle valve error, 송풍팬 error, FFU error, FFU High/Low, 풍속1~3 Low |
| Pressure | P00~P14 | Analog card error(차압센서 통신), 차압센서 1-1~3-5 High/Low limit (14종) |

각 알람 정의는 **데이터(선언)**로 관리 — 코드 하드코딩 금지:
```
AlarmRule { Id, Code, Name, Severity(Info/Warn/Alarm/Critical),
            Source(DevicePath), Condition(GT/LT/Band/CommFail/BitSet),
            Threshold, DebounceMs, RequiresInterlock, ResetPolicy(Auto/Manual),
            MessageKo, MessageEn }
```
→ 알람 추가/임계값 변경이 재컴파일 없이 Config로 가능해야 함 (`EFEM_Plan.md` 확장성 요구).

### 5.2 인터록

| ID | 조건 | 동작 | 구현 위치 |
|---|---|---|---|
| IL-01 | Sensor 3 (3-1~3-5) **High limit 도달** | 해당 체인 **Throttle valve Close(0 pulse) + 송풍팬 OFF** | 폴링 스레드 즉시 실행 |
| IL-02 | EMO ON (PLC D10.6) | 전 체인 SafeStop (전 밸브 Close, 전 팬 OFF) | 폴링 스레드 즉시 실행 |
| IL-03 | Main Breaker OFF (D10.8) | SafeStop + 통신 재연결 대기 | 폴링 스레드 |
| IL-04 | 통신 상실 (연속 N회 타임아웃) | AutoControl 중단 → Fault, 액추에이터 Fail-Safe 위치 | 포트 워커 |

> IL-01은 PDF에 "해당 체인" / "전체"인지 명시가 없습니다. 설계는 **체인 단위**로 가정. → **협의 필요 항목 #6**
> Door Open (D10.7)의 인터록 여부도 미정. → **협의 필요 항목 #7**

**인터록 응답 목표**: 조건 검출 → 명령 송신 완료 **< 1 폴링 사이클 + 1 트랜잭션** (약 250ms 이내). 인터록 명령은 포트 큐 최우선 순위로 삽입하며 진행 중 트랜잭션 종료 직후 실행.

#### 5.2.1 IL-01 임계값은 운전 대역과 분리한다 (2026-08-05 확정)

S4 통합 검증에서 **전원 투입 직후 IL-01이 발동해 장비가 영구히 기동 불가**가 되는 결함을 찾았습니다. 원인은 산수입니다.

```
IL-01 임계값 = Sensor 3 대역 상한 = 설정값(-200) + 대역(100) = -100 Pa
밸브 닫힘·팬 정지 상태의 배기 덕트 압력       ≈  -50 Pa
  → -50 > -100 → 조건 성립
  → ResetPolicy = Manual → 래치 → RequestAuto() 영구 차단
```

시뮬레이터 특성이 아니라 **인터록이 자신의 발동 조건이 본래 참인 상태에서 무장되어 있던** 구조 문제입니다. 두 값 모두 ESAM 문서 기본값이고, 배기 밸브를 닫으면 덕트 압력이 대기압 쪽으로 완화되는 것은 물리적 사실입니다.

부수 문제도 있었습니다. 임계값이 운전 대역에서 파생되므로 **작업자가 Config 화면에서 Sensor 3 설정값을 바꾸면 안전 임계값이 함께 이동**했습니다. 안전 기능이 운전 파라미터에 종속되어서는 안 됩니다.

**확정: `InterlockRule.ThresholdPa` 를 도입하고 IL-01 기본값을 `0 Pa` 로 명시합니다.**

| 항목 | 값 | 근거 |
|---|---|---|
| 발동 임계값 | **0 Pa** (대기압) | 튜닝값이 아니라 물리적 경계. 배기 덕트가 음압을 잃는 순간이 오염 확산 조건이며, 그것이 IL-01의 존재 이유 |
| 해제 임계값 | -20 Pa | 0 − `ClearHysteresisPa`(20) |
| 정지 상태 -50 Pa | 발동 안 함 | 음압 유지 중이므로 위험 없음 |
| -100 ~ 0 Pa 구간 | **알람 담당** | 배기 열화는 통보 대상. Sensor 3 이탈 확정 시간이 300초로 잡힌 것도 같은 취지 |

검토 과정에서 대안으로 "자동 운전 중에만 무장 + 정상 대역 진입 후 무장" 방식을 시도했으나 **폐기**했습니다. 이유:

- 무장 기준(해제 임계값 이하)이 밴드 제어가 아무것도 하지 않는 구간 안에 있어, 체인이 -105 Pa에서 안정되면 **영구히 무장되지 않음**
- 대역을 ±10 Pa로 좁히면 무장점이 설정값보다 아래가 되어 **원리적으로 무장 불가**
- `StopAuto()`가 밸브·팬을 정지시키지 않으므로, Auto를 끄면 **팬이 도는 상태로 감시가 사라짐**
- 상태 변수 2개(`_armed`, `_lastPhase`)가 추가되어 3개 폴링 스레드의 경합 면이 늘어남

즉 "기동 못 함"이라는 안전측 결함을 "무방비로 운전됨"이라는 위험측 결함으로 바꾸는 거래였습니다. HW팀의 배기 계통 사양 확정 시 임계값을 재검토합니다. → **협의 필요 항목 #21**

#### 5.2.2 IL-04는 Good 이 아닌 모든 품질에서 발동한다 (2026-08-05 확정)

기존 구현은 `Plc.Quality == Quality.Bad` 만 검사했습니다. 그런데 **한 번도 응답하지 않은 PLC는 영구히 `NoData`** 로 남습니다(`Bad` 는 한 번 성공한 뒤 실패했을 때만 부여). 결과적으로:

- `EmoActive` / `MainBreakerOff` / `DoorOpen` 이 모두 `false` 로 읽혀 **IL-02·IL-03·IL-05가 침묵**
- 그 상태를 잡으라고 만든 IL-04 자신도 발동하지 않음

PLC 미배선인 현재 상태가 정확히 여기에 해당했습니다. `!= Quality.Good` 으로 수정하고, `NoData`·`Stale`·`Uncertain` 도 함께 잡습니다.

단, PLC가 **아직 구성에 없는** 단계에서 항상 발동하면 아무것도 검증할 수 없습니다. `ControlConfig.SafetyInputsConfigured` 를 도입해 조립 루트가 device-map 에서 PLC 드라이버 존재 여부로 채우고, 없으면 IL-04 판정을 끄되 **"안전 입력이 하나도 없다"는 사실을 구성 경고로 올립니다.** 조용히 비활성화하는 것이 가장 위험합니다.

#### 5.2.3 인터록 지령은 같은 사이클의 자동 지령에 되돌려지지 않는다 (2026-08-05 수정)

`CommandQueue` 가 하위 우선순위 지령을 정리할 때 **지령 종류(`Kind`)까지 비교**하고 있었습니다. 인터록은 `CloseValve`, 자동 제어는 `SetValvePosition` 이라 종류가 달라 자동 지령이 큐에 남았고, 워커는 우선순위 순으로 인터록 → 자동을 연달아 실행했습니다.

```
1) 자동 제어가 SetValvePosition(V-3, 3200) 을 큐에 넣음
2) 폴링 스레드가 IL-01 발동 → CloseValve(V-3) + StopFan(F-3) 투입
3) Kind 가 다르므로 자동 지령이 제거되지 않음
4) 워커: 밸브 0 pulse → 팬 OFF → 밸브 3200 pulse (57.6°)
   → 인터록이 닫은 밸브를 같은 사이클에 다시 연다. 안전 기능의 실효가 0.
```

**수정**: 하위 우선순위 정리는 **장치 단위**(`Target` + `DeviceId`)로 비교합니다. 더 높은 권한의 새 지령이 내려온 장치에 낡은 지령을 실행할 이유는 없습니다. 같은 우선순위 내 병합은 종류까지 비교하는 기존 동작을 유지합니다(`StartFan` 후 `SetFanRpm` 같은 연속 지령을 지우지 않기 위함).

추가 방어 2겹:
- `InterlockGuard.Dispatch` 가 지령 투입 전에 `ClearAutomaticCommands()` 호출 — 인터록 실행 *뒤에* 제어 엔진이 넣은 지령은 큐 정리를 거치지 않으므로
- `ControlEngine.ExecuteStep` 이 지령 투입 **직전에** 단계를 재확인 — 판정 중 인터록이 발동한 경우를 막기 위해

#### 5.2.4 판정기는 스레드 안전해야 한다 (2026-08-05 수정)

당초 설계는 "인터록 판정은 폴링 스레드 1개에서만 호출"을 전제했으나, **실제 배선은 포트마다 워커 스레드가 하나**(3개)이고 각 워커가 폴링 완료 시점에 판정을 호출합니다. `InterlockEvaluator._latched` 와 `AlarmEvaluator._states` 를 3개 스레드가 동시에 변형하고 있었습니다.

구체적 손상:

| 증상 | 결과 |
|---|---|
| `HashSet` 확장 중 동시 삽입으로 래치 항목 소실 | 발동이 더 이상 보고되지 않음 → 상태머신이 인터록 해제로 판단 → **위험이 남은 채 Ready 복귀**, UI는 정상 표시 |
| UI 스레드의 `Reset` 이 열거 중인 집합 수정 | `InvalidOperationException` → 폴링 스레드에서 삼켜짐 |
| `AlarmState.Update` 의 check-then-act 중복 | 하나의 물리 사건에 이력 중복 적재, 디바운스 시작 시각 덮어쓰기로 300초 디바운스 무한 연장 |

**수정**: 락을 **도메인 판정기 내부**에 둡니다(`InterlockEvaluator._gate`, `AlarmEvaluator._gate`). 상위 계층에 두면 UI 스레드의 `Reset`/`AcknowledgeAll` 경로가 우회합니다. 락 경합 비용은 마이크로초 단위이므로 250ms 예산에 영향이 없습니다. 안전 기능에서 락을 아끼는 것은 잘못된 최적화입니다.

---

## 6. 데이터 로깅 설계 (SQLite + CSV)

### 6.1 SQLite 스키마 (초안)

```sql
PRAGMA journal_mode = WAL;      -- 쓰기 중 조회 가능
PRAGMA synchronous  = NORMAL;   -- 처리량/내구성 절충

-- 시계열 (Wide 테이블: 100~200ms 주기, 조회 성능 우선)
CREATE TABLE trend (
  ts_utc      INTEGER NOT NULL,        -- Unix ms
  s11 REAL, s12 REAL, s13 REAL,
  s21 REAL, s22 REAL, s23 REAL, s24 REAL, s25 REAL,
  s31 REAL, s32 REAL, s33 REAL, s34 REAL, s35 REAL,
  v1_pct REAL, v2_pct REAL, v3_pct REAL, v4_pct REAL, v5_pct REAL,
  f1_rpm REAL, f2_rpm REAL, f3_rpm REAL, f4_rpm REAL, f5_rpm REAL,
  ffu_rpm REAL, mfc1 REAL, mfc2 REAL,
  av1 REAL, av2 REAL, av3 REAL,
  temp_efem REAL, humi_efem REAL, particle REAL, temp_ctrlbox REAL,
  ctrl_mode INTEGER, ctrl_phase INTEGER, alarm_bits BLOB
);
CREATE INDEX ix_trend_ts ON trend(ts_utc);

CREATE TABLE alarm_history (
  id INTEGER PRIMARY KEY, code TEXT NOT NULL, severity INTEGER,
  raised_utc INTEGER NOT NULL, cleared_utc INTEGER,
  ack_utc INTEGER, ack_user TEXT, value REAL, message TEXT
);

CREATE TABLE audit_log (           -- 설정 변경 추적 (반도체 고객 요구 대비)
  id INTEGER PRIMARY KEY, ts_utc INTEGER, user TEXT, category TEXT,
  item TEXT, old_value TEXT, new_value TEXT
);

CREATE TABLE recipe (id INTEGER PRIMARY KEY, name TEXT UNIQUE,
  json TEXT, updated_utc INTEGER, updated_user TEXT);
```

### 6.2 적재 전략
- **일별 DB 파일 롤링** (`log/esam_YYYYMMDD.db`) — 단일 파일 무한 증가 방지
- `BlockingCollection<SystemSnapshot>` → 500ms 또는 100건 단위 **단일 트랜잭션 배치 insert**
- 용량 추산: 40 컬럼 × 8B ≈ 320B/row → **200ms 주기 = 약 138 MB/일**, **100ms 주기 = 약 276 MB/일**
  - → **압축 저장 옵션**: 변화 없는 구간 데드밴드 필터(값 변화 < ε 이면 skip) 적용 시 60~80% 절감
  - 리텐션 기본 90일, Config로 조정
- CSV Export: 기간 + 항목 선택 → 스트리밍 방식 (전량 메모리 로드 금지), Phase 5 응답성 분석용 원본 해상도 유지

### 6.3 Data Log Viewer (PDF p.8 "협의 필요")
제안: 상단 기간/항목 선택 → ScottPlot 다축 트렌드 + 하단 알람 이벤트 마커 오버레이 + 그리드 뷰 토글 + CSV 반출 버튼. → **협의 필요 항목 #8**

---

## 7. 화면 설계 (ESAM PDF p.4~8 기반)

### 7.1 공통 셸

```
┌────────────────────────────────────────────────────────────────────────┐
│ DSE TECH │ User(Login) │ Equipment Status │ Host Comm │ Alarm │ Date/Time│
├──────────┼─────────────────────────────────────────────────────────────┤
│ Operate  │                                                             │
│ Maint.   │                                                             │
│ Config   │                  ContentControl (활성 View)                  │
│ I/O      │                                                             │
│ Data Log │                                                             │
│ Quit     │                                                             │
└──────────┴─────────────────────────────────────────────────────────────┘
```

### 7.2 화면별 명세

| # | 화면 | 내용 | 권한 |
|---|---|---|---|
| 1 | **Operate (View)** | FFU RPM, 센서1-1/1-2/1-3, MFC, 센서2-1~2-5 / 스로틀밸브 / 송풍팬 / 센서3-1~3-5 **현재값 5열 그리드**, 우측 Auto mode·Sensor Mode(1/2/3)·풍속1~3·Particle·Temp/Humidity | Operator (읽기) |
| 2 | **Operate (Set)** | 위 그리드에 밸브/팬 **설정값 + [SET] 버튼** 추가, Sensor 1/2/3 Mode 선택, MFC 설정값 | Maintenance |
| 3 | **Config** | S1-1~1-3 / S2-1~2-5 / S3-1~3-5의 **Set · ±범위 · Time** 테이블, FFU/Particle/Temp/Humidity High·Low Limit, 풍속1~3 Low Limit | Engineer |
| 4 | **I/O (Status)** | ①FFU ②송풍팬 ③스로틀밸브 ④차압센서 ⑤FDC ⑥쿨링팬 ⑦Temp(CtrlBox) ⑧Temp(EFEM) ⑨Humidity ⑩Particle ⑪MFC ⑫풍속 — **연결/정상동작 상태 램프**, PLC 디지털 입력 D10.0~D10.8 | Maintenance |
| 5 | **Data Log** | 트렌드 차트 + 알람 이력 + CSV Export | Operator |
| 6 | **Alarm Popup** | 활성 알람 리스트, Ack, Reset(권한), 이력 조회 | 상황별 |
| 7 | **Maintenance** | 아래 7.3 참조 | Engineer |

### 7.3 Maintenance — Phase 5 현장 튜닝 전용 기능 (선제 구축)

`EFEM_Plan.md` 요구사항 반영. **현장 시운전 시간을 좌우하는 가장 중요한 화면**입니다.

1. **센서 영점 교정 탭** — 대기압 상태에서 1클릭으로 13ch Offset 일괄 취득/저장 (샘플 N회 평균, 이전 값 이력 보관, 롤백 가능)
2. **수동 제어(Manual Jog / Override)** — Auto 루프 정지 후 밸브 개도율(%)·팬 RPM 직접 입력. 인터록은 **해제 불가**(안전)
3. **제어 파라미터 실시간 조정** — Step 크기, Dwell, 히스테리시스, (옵션)P·I·D Gain을 **재시작 없이 적용**
4. **고속 데이터 로거** — 원본 해상도 CSV 즉시 반출 (구간 지정 + Ring buffer Trigger 캡처)
5. **통신 진단** — 포트별 트랜잭션 성공률/평균 응답시간/타임아웃 카운트, 실제 사이클 타임 표시 (2.2절 (B) 검증용)
6. **밸브 Homing / Alarm Reset** 개별 실행

### 7.4 UI 성능 원칙
- 센서 5열 그리드는 `ChainPanel` **UserControl로 모듈화** (5회 반복, ItemsControl 바인딩)
- 값 표시는 `TextBlock` + `StringFormat` 바인딩, `INotifyPropertyChanged` **변화 시에만** 발신
- 트렌드 차트는 **ScottPlot + DataLogger 플롯 + 링버퍼**, 렌더는 최대 10 FPS로 제한 (`Refresh()` 호출 throttle)
- `ObservableCollection` 대량 갱신 금지 → 고정 크기 컬렉션 + 인플레이스 갱신

---

## 8. Config 설계 (재컴파일 없는 확장)

`EFEM_Plan.md`의 "설비 확장(FFU/MFC) 시 재컴파일 불필요" 요구를 만족하기 위해 **전체를 선언적 JSON**으로 분리.

```
config/
 ├ ports.json          # 시리얼 포트, Baud, Parity, Timeout, 폴링 티어 주기
 ├ device-map.json     # 디바이스 인스턴스 + 슬레이브ID + 레지스터 맵 + 스케일링
 ├ chains.json         # 체인 구성 (센서 ↔ 밸브 ↔ 팬 매핑) — 확장 시 여기만 수정
 ├ control.json        # Setpoint / Band / Time / Step / Dwell / Policy 종류
 ├ alarms.json         # AlarmRule 선언 목록
 ├ interlocks.json     # InterlockRule 선언 목록
 ├ ui-layout.json      # 화면 그리드 열 수, 표시 항목
 └ users.json          # 권한 역할 정의 (비밀번호는 해시, 별도 저장)
```

상세 스키마와 xlsx 통신자료 정규화 결과는 → **`docs/COMM_MAP.md`** 참조.

**로드 시 검증(Fail-fast)**: 포트별 슬레이브 ID 유일성, 체인 참조 무결성, 레지스터 범위 중복, Setpoint가 High/Low Limit 내에 있는지, 스키마 버전 호환성.

---

## 9. 프로젝트 골격 제안

```
EFEM_Monitor.sln
└ src/
  ├ Esam.Domain/                        (net472, 의존성 없음 — 단위테스트 핵심)
  │   ├ Models/            SystemSnapshot, PressureReading, ValveState, FanState ...
  │   ├ Control/           IControlPolicy, BandControlPolicy, PidControlPolicy,
  │   │                    SensorModeStateMachine, SystemStateMachine, ChainContext
  │   ├ Alarms/            AlarmRule, AlarmEvaluator, InterlockRule
  │   └ Units/             PressureConverter, ValvePulseConverter, ScaleFactor
  │
  ├ Esam.Communication/                 (net472)
  │   ├ Abstractions/      IModbusTransport, IDeviceDriver, ITransactionQueue
  │   ├ Modbus/            ModbusRtuTransport, ModbusPortWorker, CrcCalculator
  │   ├ Drivers/           Wtdm550SensorDriver, ThrottleValveDriver,
  │   │                    ModbusFanDriver, LsXbmPlcDriver, ThdRtDriver,
  │   │                    Beck985Driver, MfcDriver
  │   ├ Simulation/        SimulatedTransport, PlantModel  ← 하드웨어 없이 개발/테스트
  │   └ Diagnostics/       PortStatistics
  │
  ├ Esam.Services/                      (net472)
  │   ├ DataStore.cs               스냅샷 발행/구독
  │   ├ ControlEngine.cs           제어 타이머 루프
  │   ├ AlarmEngine.cs             알람 판정/이력
  │   ├ InterlockGuard.cs          인터록 즉시 실행
  │   ├ DataLogger.cs              배치 적재
  │   ├ ConfigService.cs           로드/검증/핫리로드
  │   ├ RecipeService.cs, AuthService.cs, AuditService.cs
  │   └ FdcAdapter/                외부 모니터링 연동 (프로토콜 미정, 어댑터 격리)
  │
  ├ Esam.Persistence/                   (net472)
  │   ├ SqliteLogRepository.cs, DbMigrator.cs, CsvExporter.cs
  │
  ├ Esam.Hmi/                           (net472, WPF)
  │   ├ App.xaml, Shell/               ShellView, ShellViewModel, Navigation
  │   ├ Views/  ViewModels/            Operate, Maintenance, Config, IO, DataLog, Alarm
  │   ├ Controls/                      ChainPanel, ValueTile, StatusLamp, TrendChartView
  │   ├ Converters/, Themes/
  │   └ Infrastructure/                RelayCommand, ObservableObject, DispatcherPump
  │
  └ Esam.Tests/                         (net472, NUnit/xUnit)
      ├ Domain/     상태머신·밴드제어·단위변환·알람판정
      ├ Comm/       CRC, 프레임 파싱, 타임아웃/재시도
      └ Integration/ Simulation 기반 시나리오 (인터록 응답, 밴드 수렴)
```

### 9.1 의존성 방향
```
Hmi → Services → Domain
        ↓            ↑
   Persistence   Communication
```
- **Domain은 어떤 프로젝트도 참조하지 않음** → 하드웨어 없이 제어 로직 전체를 단위테스트 가능
- Communication은 Domain 모델만 참조 (Services 역참조 금지)
- 구체 드라이버는 DI 컨테이너(경량 — `SimpleInjector` 또는 수동 조립)로 조립

### 9.2 외부 라이브러리 (모두 net472 호환 확인 필요)

| 용도 | 후보 | 비고 |
|---|---|---|
| Modbus RTU | **NModbus4** 또는 자체 구현 | 자체 구현 시 t3.5 타이밍·재시도 제어가 정밀함. **RS-485 반이중 타이밍 제어가 중요하므로 자체 구현 권장 검토** |
| 트렌드 차트 | **ScottPlot 4.1.x (WPF)** | 5.x는 net6+ 요구 → **4.1.x 계열 사용 필수** |
| DB | **System.Data.SQLite** | |
| 로깅 | NLog | |
| MVVM | 경량 자체 구현 (ObservableObject/RelayCommand) | 프레임워크 도입 없이 충분 |
| JSON | Newtonsoft.Json 13.x | |
| 테스트 | xUnit 또는 NUnit + FluentAssertions | |

---

## 10. 개발 단계 계획

| 단계 | 산출물 | 검증 방법 |
|---|---|---|
| **S0** | 설계 확정 (본 문서 + COMM_MAP 리뷰) | 협의 항목 #1~#20 클로징 |
| **S1** ✅ | 솔루션 골격 + Domain 모델/상태머신/밴드제어/알람·인터록 + 단위테스트 46건 | 2026-07-31 완료 (정적 검토 통과, VS 빌드 확인 필요) |
| **S2** ✅ | Modbus RTU 전송계층(CRC·프레임·타이밍·시리얼) + 가상 플랜트(1차 지연+노이즈) + 시뮬레이션 전송계층 | 2026-07-31 완료. 테스트 163건 |
| **S3** ✅ | 선언적 디바이스 드라이버 + ModbusPortWorker + JSON 설정 로더 | 2026-07-31 완료. 테스트 263건. **시뮬레이션 사이클타임 실측: 13ch 단일 버스 242ms** → 100ms 불가 확인 |
| **S3.5** | 실 센서 1대 루프백으로 슬레이브 응답지연 실측 → 위 242ms 값 보정 | 하드웨어 입고 후 |
| **S4** ✅ | `Esam.Services` — SnapshotBuilder / DataStore / InterlockGuard / ControlEngine / AlarmService / EsamRuntime 조립 루트 + 종단 통합테스트 | 2026-08-05 완료. **시뮬레이션 위에서 폴링→스냅샷→제어→인터록 전 경로 동작.** 통합 검증에서 안전 결함 4건 수정(5.2.1~5.2.4), 미해결 11건은 11.1 |
| **S4.5** ✅ | HMI Dashboard 프로토타입 (ArcGauge / TrendChart / 내장 시뮬레이터) | 2026-08-05 완료. 실 DataStore 연결은 S7 |
| **S5** | **11.1의 D1~D4 우선 처리** — SafeStop 라우팅, 원점 복귀 시퀀스, 상태머신 락 | 상태 전이표 전수 테스트 + 동시성 테스트 |
| **S6** | 밸브·팬 드라이버 실장비 검증 | 실 밸브 1대 위치 정확도, 인터록 응답시간 실측 |
| **S7** | HMI 화면 (Operate → Config → I/O → Maintenance → DataLog) + DataStore 실연결 | 8시간 연속 UI 응답성/메모리 누수 |
| **S8** | 데이터 로깅 + CSV Export | 24시간 연속 적재, DB 크기/조회 성능 |
| **S9** | 알람 규칙 집합(31종) + 권한/감사로그 | 알람 31종 전수 유발 테스트 |
| **S10** | 현장 튜닝 (Phase 5) | Maintenance 툴로 파라미터 수렴 |

**S2(시뮬레이션)를 먼저 구축**한 것이 핵심이었습니다. 하드웨어 입고·타부서 명세 확정을 기다리지 않고 제어 로직과 HMI를 병행 개발할 수 있었고, S4 통합 검증에서 **실장비 없이 안전 결함 4건을 찾아냈습니다.** 그중 IL-01 기동 불가와 인터록 지령 상쇄는 실장비에서 발견했다면 원인 규명에 훨씬 큰 비용이 들었을 항목입니다.

> **S5를 D1~D4 처리로 잡은 이유**: 현재 상태에서 인터록은 부분적으로만 동작합니다. EMO가 SafeStop에 도달하지 않고(D1), Homing 중 인터록 트리거가 소실되며(D2), 원점 복귀가 검증되지 않아 밸브 위치 자체를 신뢰할 수 없습니다(D3). 이 위에 화면이나 로깅을 올려도 검증의 근거가 서지 않습니다.

---

## 11. 협의 필요 항목 (Open Issues)

| # | 항목 | 영향 | 담당 |
|---|---|---|---|
| 1 | **폴링 주기 목표 100ms 유지 여부** (2.2 B) — 유지 시 RS-485 포트 3~4분할 하드웨어 필요 | **아키텍처·BOM** | HW팀 협의 |
| 2 | Sensor 1 Band 불일치 (`± 2 Pa` vs `5<x<7`) | 제어 정확도 | CTO |
| 3 | Config `Time` 컬럼 의미 (알람 디바운스? 제어 유지시간? 안정화 대기?) | 알람/제어 | CTO |
| 4 | 1차 릴리스 제어 방식: 순서도 밴드제어 확정 vs PID 병행 | 제어 구현 | CTO |
| 5 | 밸브 레지스터 상세 (FC코드, 0x6002 비트 정의, 0x1003/0x2203 비트맵, 엔디안) | 밸브 드라이버 | 밸브 설계자 |
| 6 | 인터록 IL-01 범위: 해당 체인만 vs 전체 정지 | **안전** | CTO |
| 7 | Door Open(D10.7) / EMO(D10.6) 인터록 정책 및 복귀 절차 | **안전** | CTO |
| 8 | Data Log Viewer 방식 (PDF p.8 미정) — 7.3 제안 검토 | HMI | CTO |
| 9 | **송풍팬 Modbus 레지스터 명세** (RPM 설정/현재값, ON·OFF, 알람, 상태). 자사 제작품이므로 **연속 주소 배치 + 밸브와 동일 주소체계**로 설계 요청 시 드라이버 재사용·트랜잭션 절감 가능 | 팬 드라이버 | 팬 설계자 |
| 10 | MFC 사양/레지스터 (xlsx "미정"), 파티클센서 프로토콜, FFU 통신 방식, FDC 프로토콜 | 확장 기능 | 구매/HW팀 |
| 11 | 온습도/파티클/컨트롤박스온도가 BOM에서 모두 `PSU650-AR` 동일 품번 — 1대 통합형인지 3대 별도인지 (통신 시트에는 온습도 ID15만 존재) | 통신 구성 | HW팀 |
| 12 | 컨트롤박스 FAN이 BOM에는 RS-485, PLC Signal에는 D10.5 알람 — 제어 대상인지 감시 대상인지 | I/O 설계 | HW팀 |
| 13 | 풍속센서 품번 2종 혼재 (AVS701 E+E / 985VM Beck) — 통신 시트는 BECK985 | 드라이버 | 구매 |
| 14 | 로그 리텐션 정책 및 데드밴드 필터 적용 여부 (일 138MB) | 저장용량 | CTO |
| 15 | CH2 표기의 의미 — 현재 장비가 2채널(CH1/CH2) 구성 중 CH2만 명세인지, 향후 CH1 추가 시 센서/밸브/팬 2배 확장인지 | **확장성 설계** | CTO |
| 16 | **Sensor 1 Mode에서 5개 체인이 참조할 센서** — PDF는 "센서 1은 1개만 설치"라 하지만 실제 S1-1/1-2/1-3 3개 존재. (a) S1-1 단독 (b) 3개 평균 (c) 체인별 매핑 | **제어 로직** | CTO |
| 17 | 차압센서 레인지 배정 (±750 Pa vs ±2.0 kPa) — 위치별 어느 품번인지. Sensor 3 Mode는 -300 Pa까지 사용 | 스케일링/알람 | HW팀 |
| 18 | PLC D영역 ↔ Modbus 주소 변환 규칙 (LS XBM), D10 비트 극성(Active High/Low), 온도 워드 스케일 | PLC 드라이버 | PLC 담당 |
| 19 | **송풍팬 BUS_C 통신 파라미터 확정** — Baud(38400 가정), Parity, Slave ID(1~5 가정) | 통신 | HW팀 |
| 20 | 송풍팬 최대 RPM / 최소 동작 RPM 사양 — 미확보 시 자동제어 진입 불가 | 제어 | 팬 설계자 |
| 21 | **IL-01 발동 임계값 확정** — 현재 0 Pa(대기압) 로 잠정 설정(5.2.1). 배기 계통 설계상 허용 최소 음압이 있다면 그 값으로 대체 | **안전** | HW팀 |

### 11.1 S4 검토에서 발견된 미해결 결함 (2026-08-05)

S4 통합 검증 중 정적 리뷰에서 찾은 항목 중 **아직 고치지 않은 것들**입니다. 세 건(지령 상쇄·스레드 경합·PLC 품질 판정)은 5.2.3·5.2.4·5.2.2에서 처리했고, 아래는 S5 과제로 남깁니다.

| # | 결함 | 실패 시나리오 | 심각도 |
|---|---|---|---|
| D1 | **EMO가 SafeStop에 도달하지 않는다.** `SystemTrigger.SafeStopRaised` 가 프로덕션 코드에서 한 번도 발생하지 않고, IL-02/IL-03은 `InterlockRaised` 로만 전이된다 | EMO가 순간 해제되면 `Interlocked → InterlockCleared → Ready` 로 복귀. 설계는 `SafeStop → Fault → Init → ValveHoming`(재원점) 을 의도했다 | **높음** |
| D2 | **`InterlockRaised` 가 일부 단계에서 무시되고, 그 엣지가 소실된다.** `Resolve` 는 `Ready`/`AutoControl` 에서만 처리하는데 `InterlockGuard` 는 false→true 엣지에서만 `Tripped` 를 발생시킨다 | Homing 중 EMO를 누르면 정지 지령은 나가지만 트리거가 버려지고 `_wasTripped=true` 로 남아 재시도되지 않는다. `ValveHoming` 에 머물며 UI는 인터록을 표시하지 않는다 | **높음** |
| D3 | **`Start()` 가 원점 복귀를 확인하지 않고 완료 선언한다.** `HomeValve` 는 프로덕션 경로에서 전송되지 않는다 | `homeDone` 이 false면 밴드 제어가 전 스텝 Skipped → 화면은 AutoControl인데 아무것도 제어되지 않는다. true면 기계적 원점이 미확정이라 `CloseValve` 의 0 pulse도 실제 닫힘이 아니다 | **높음** |
| D4 | **`SystemStateMachine` 이 스레드 안전하지 않다.** 폴링 3스레드 + 제어 스레드 + UI가 `Fire` 를 호출한다 | Auto 버튼과 인터록 발동이 겹치면 `Ready→AutoControl` 과 `Ready→Interlocked` 가 경합해 **인터록 래치를 안은 채 AutoControl** 에 들어간다 | **높음** |
| D5 | **`StopAuto()` 가 액추에이터를 정지시키지 않는다.** 큐만 비우고 트리거만 발생시킨다 | Auto를 끄면 팬이 계속 돌고 밸브가 열린 상태로 남는다 | 중간 |
| D6 | **인터록 지령의 실행 성공을 검증하지 않는다.** `worker.CommandFailed` 구독자가 없다 | `CloseValve` 는 `setPosition` → `prMove` 2단 시퀀스인데 2번째가 타임아웃하면 밸브는 전혀 움직이지 않는다. 그런데 `Tripped` 는 이미 "처리됨"을 알렸다 | 중간 |
| D7 | **인터록이 3중 평가·9중 투입된다.** 포트 3개가 각각 판정하고 결과를 전 워커에 뿌린다 | 래치 1건에 사이클당 18회 enqueue, 담당 워커가 중복 3회 실행(6 트랜잭션). 인터록 활성 중 폴링 사이클이 늘어나 2차 위험 검출이 늦어진다 | 중간 |
| D8 | **스냅샷 staleness 15초를 인터록이 검사하지 않는다.** `Quality != Good` 만 본다 | 최대 15초 낡은 값으로 판정한다. 250ms 목표의 60배 | 중간 |
| D9 | **알람 규칙 집합이 존재하지 않는다.** `RuntimeOptions` 는 `AlarmRules` 를 채우지 않고 `config/alarms.json` 도 없다 | DESIGN 5.1의 31종 알람이 어떤 구성에서도 동작하지 않는다 | 중간 |
| D10 | **구성 경고가 화면에 도달하지 않는다.** `Describe()` 는 건수만 출력하고 HMI는 `Esam.Services` 를 참조하지 않는다 | "안전 입력 없음", "IL-01 임계값 미지정" 같은 경고를 작업자가 볼 수 없다 | 중간 |
| D11 | **`OnPollCompleted` 의 예외가 흔적 없이 삼켜진다.** 워커의 `catch (Exception) { }` 로 흘러간다 | 예외가 결정적이면 인터록 평가가 무한히 중단되는데 로그·알람·카운터가 전부 없다 | 중간 |

---

## 12. 다음 단계

승인 시 진행 순서:
1. `docs/COMM_MAP.md` 검토 → Config JSON 스키마 확정
2. 솔루션 골격 및 `Esam.Domain` 모델/상태머신 코드 작성 (S1)
3. Simulation 드라이버 구축 (S2)

**본 문서는 검토용 초안이며, 승인 전까지 코드 작성은 진행하지 않습니다.**
