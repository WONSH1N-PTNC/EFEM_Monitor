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
| 송풍팬 | **RS-485 Modbus RTU 직결** (CAN 폐지). ~~전용 포트 BUS_C~~ → **CH2 를 스로틀밸브와 공유, 슬레이브 6~10** (2026-08-10 IO List 확정) |
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

### 2.1 채널 구성 (2026-08-10 확정 — `IO List_260801.xlsx`)

```
                    ┌──────────────── PC (ESAM HMI) ────────────────┐
                    │                                                │
 CH1  RS-485 19200 8N1 ──┬─ 차압센서 DP-01~13    (Slave 1~13)   0x4001
                         ├─ 온습도 THD-01        (Slave 15)     0x0000~0x0001
                         ├─ 풍속 WS-01~03        (Slave 20~22)  0x0002, 0x0005
                         ├─ PLC XBM-DR16S        (Slave 25)     0x000A / 0x0064~0x0072
                         └─ 압력센서 P-01~05     (Slave 26)     0x03E9~0x03FD (FC04, 5채널 1대)

 CH2  RS-485 38400 8N1 ──┬─ 스로틀밸브 U1~U5     (Slave 1~5)    0x602B / 0x6002 / 0x6202
                         └─ 송풍팬 U6~U10        (Slave 6~10)   JKBLD300V2, 0x4041 / 0x4006
```

**2026-07-31 자 3포트(BUS_A/B/C) 구성은 폐기되었습니다.** 실제 배선은 2채널이며 송풍팬이 밸브와 CH2 를 공유합니다. 슬레이브 ID 충돌은 팬을 6~10 으로 배치해 해소되어 있습니다.

체인은 **EC, EL, ER, SL, SR** 5조이며 센서 명명이 정확히 대응합니다.

| 논리 ID | Tag | 위치 |
|---|---|---|
| S1-1 ~ S1-3 | DP-01~03 | EC, SL, SR — **EL·ER 에는 설치되지 않음** |
| S2-1 ~ S2-5 | DP-04~08 | EEX C/L/R/SL/SR **Front** |
| S3-1 ~ S3-5 | DP-09~13 | EEX C/L/R/SL/SR **Back** |

레지스터 상세는 `COMM_MAP.md` §0.3 을 참조하십시오.

### 2.2 ⚠ 설계상 반드시 확인해야 할 사항

**(A) 슬레이브 ID 충돌 — 해소됨 (2026-08-10)**

초안에서 우려한 충돌(차압 1~13 ↔ 밸브 1~5 ↔ 팬 1~5)은 실제 배선에서 **팬을 6~10 으로 배치**해 해결되어 있었습니다. CH2 에서 밸브 1~5 와 팬 6~10 이 공존합니다.

남은 잠재 충돌은 **풍속 20~22 ↔ MFC 20~21** 뿐이며, MFC 는 아직 구성에 없습니다(사양 미정). 추가 시 재배정이 필요합니다.

포트당 슬레이브 ID 유일성 검증은 그대로 유지합니다(Config 로드 시 Fail-fast). 같은 버스에 두 장치가 같은 주소를 가지면 동시 응답으로 프레임이 깨지기 때문입니다.

**(B) 100ms 폴링 주기 — 2026-08-10 실측 계산으로 결론**

슬레이브 응답 지연 5 ms, 11 bit/char 가정. 코드의 `ModbusTiming.EstimateTransactionMs` 와 동일 모델입니다.

| 채널 | 티어 | 구성 | tx | 소요 |
|---|---|---|---|---|
| CH1 | Fast | 차압 13 + PLC DI 1 | 14 | **218.4 ms** |
| CH1 | Medium | PSM 압력 5채널(21워드 1회) | 1 | 38.5 ms |
| CH1 | Slow | 온습도 + 풍속 3 + PLC 온도 | 5 | 105.5 ms |
| CH2 | Fast | 밸브 위치 5 + 블로워 5 | 10 | **113.3 ms** |
| CH2 | Slow | 밸브 원점 5 | 5 | 55.2 ms |

```
제어 루프가 보는 실효 갱신 주기 = max(CH1 Fast, CH2 Fast) = 218 ms
Slow 가 겹치는 사이클의 최악값                              = 362 ms
```

**Fast 티어만으로도 목표의 2.2배입니다.** 트랜잭션당 최소 시간이 물리적으로 정해지므로 소프트웨어 최적화로는 좁힐 수 없습니다. 선택지:

| 방안 | CH1 Fast | 대가 |
|---|---|---|
| 현행 (19200, 14 tx) | 218 ms | — |
| CH1 을 38400 으로 상향 | 155 ms | 차압센서의 38400 지원 확인 필요 |
| 차압센서를 2포트로 분할 | 125 ms | 포트 1개 추가 (BOM 변경) |
| **위 둘을 병행** | **약 90 ms** | 100 ms 달성 |

이미 적용된 완화책:
- **티어 분리** — 원점·모션상태·알람을 Slow 로 내려 Fast 를 제어·안전 경로만 남김
- **트랜잭션 병합** — PSM 5채널을 21워드 1회로(3배 단축), PLC 온도+단선을 15워드 1회로, 풍속 에러+값을 4워드 1회로

**→ 2026-08-10 결정: 하드웨어 추가 없이 218 ms 로 현실화합니다.**

차압계는 시정수가 커서(플랜트 모델 기준 센서 2 가 1.5초, 센서 3 이 1.0초) 218 ms 갱신으로도 제어 성능에 여유가 있습니다. 100 ms 를 위해 포트를 늘리면 BOM·배선·유지보수가 늘어나는데 그만한 제어 이득이 없습니다.

이 결정에 따라 확정되는 값입니다.

| 항목 | 값 | 근거 |
|---|---|---|
| CH1 Fast 주기 | 250 ms | 실측 계산 218 ms + 여유 |
| CH2 Fast 주기 | 150 ms | 실측 계산 113 ms + 여유 |
| 제어 주기 | 200 ms | 갱신 주기보다 짧아도 무해 — Dwell 이 실제 조작을 게이트한다 |
| Dwell(밸브·팬) | 1000 ms | **폴링 주기보다 반드시 커야 한다.** 작으면 스냅샷이 직전 조작을 반영하기 전에 다음 조작이 나가 중복 지령이 쌓인다 |

> **제약**: `DwellMs ≥ Fast 폴링 주기`. 현재 1000 ≥ 250 으로 4배 여유가 있습니다. 현장 튜닝(Phase 5)에서 Dwell 을 줄일 때 이 하한을 넘지 않도록 검증에 넣어야 합니다.

**(C) 슬레이브 응답 지연은 아직 가정값입니다**

위 계산의 5 ms 는 추정입니다. 전송 시간은 보레이트로 확정되지만 **응답 지연은 장치마다 다르고 전체의 30% 이상을 차지**합니다(CH1 Fast 218 ms 중 70 ms). 실 센서 1대 루프백 실측(S3.5)으로 보정해야 위 숫자가 확정됩니다.

**(D) 송풍팬 드라이버 — JKBLD300V2 (2026-08-10 확보, 폐루프 확정)**

- 속도 지령은 **폐루프 `0x4006`(200~4000 rpm)** 입니다. 개루프 `0x4007` 도 검토했으나 매뉴얼 안에서 스케일이 모순(§7.2 표는 100~1000=10~100%, §7.7 예제는 50=50%)이고 실장비 확인이 어려워 폐루프로 확정했습니다.
- **`0x4041`(현재 속도)은 모드와 무관합니다.** 읽기 전용 실측값이라 개루프든 폐루프든 항상 rpm 을 반환합니다. 모드가 갈리는 것은 쓰기 레지스터뿐입니다. 따라서 `IO List` 의 **IO 시트**(폴링 목록, `0x4041` 만 기재)는 수정할 필요가 없고, **Driver 시트**의 `0x4007` 한 줄만 `0x4006` 으로 바꾸면 됩니다.
- 폐루프의 실질적 이득은 여기서 나옵니다. 지령(`0x4006`, rpm)과 실측(`0x4041`, rpm)의 **단위가 같아 편차를 계산**할 수 있어 "지령 대비 미달" 진단이 성립합니다. 개루프면 지령은 %, 실측은 rpm 이라 비교 자체가 불가능합니다.
- 대신 폐루프는 `0x4001`(극쌍수) 설정에 의존합니다. 속도 산출식 `N = (F/P) × 60/3` 이 극수에 걸려 있어, 값이 틀리면 `0x4041` 이 배수로 어긋나고 **드라이버가 잘못된 실측값을 기준으로 조절**하게 됩니다. 커미셔닝 항목으로 분리했습니다(`COMMISSIONING.md` §1.3).
- 기동 시 `0x4037 = 1`(명령 소스=통신) → `0x4005 = 1`(폐루프) 순서가 **필수**입니다. 누락하면 드라이버가 IO·가변저항 입력을 보므로 우리 지령이 전부 무시됩니다.
- 정지는 `0x4034 = 0` 입니다. 최소 설정값이 200 rpm 이라 속도 0 지령으로는 멈출 수 없습니다.
- 보레이트 변경(`0x4044 = 2`)은 **앱이 할 수 없는 커미셔닝 절차**입니다. 출하 상태가 115200 이라 38400 으로 여는 우리 프로그램은 애초에 통신이 되지 않습니다. 상세는 `COMM_MAP.md` §0.3.

**(E) 팬 제어의 적분 상태는 지령값이어야 합니다 (2026-08-10 수정)**

`SnapshotBuilder` 가 `FanState.TargetRpm` 자리에 **측정값을 복사**하고 있었고, 밴드 제어가 그 값을 적분 상태로 썼습니다.

```csharp
double currentTarget = context.Fan.TargetRpm;              // ← 실제로는 측정 RPM
double nextRpm  = currentTarget + StepRpm;                 // 다음 지령
bool fanAtMax   = currentTarget >= MaxRpm - RpmTolerance;  // 포화 판정
```

두 가지가 깨집니다.

1. **램프업 지연** — 팬이 목표까지 오르기 전에 다음 스텝이 뒤처진 값에서 계산되어 증속이 느려집니다.
2. **포화 미감지** — 덕트 부하가 크면 팬이 `MaxRpm` 에 물리적으로 도달하지 못합니다. 그러면 `fanAtMax` 가 영영 성립하지 않아 제어기는 증속 여력이 남았다고 믿고 계속 올립니다. **밸브가 이미 포화된 뒤 팬이 마지막 대응 수단인 구조**에서, 제어 권한을 다 쓴 상태가 보고되지 않는 것은 "대응 수단이 없는데 화면은 정상"인 위험한 불일치입니다.

수정: `ChainRuntime.LastFanCommandRpm` 에 **제어기가 낸 지령**을 유지하고 그것을 적분 상태로 씁니다. 자동 운전 진입 시에는 드라이버에서 되읽은 설정값(`0x4006`)으로 `SeedFanCommand` 를 호출해 맞춥니다. 수동 운전으로 이미 팬이 돌고 있어도 최소값부터 다시 증속하지 않게 하기 위함입니다.

되읽은 설정값은 `FanState.TargetRpm` 에 실어 **진단 경로**로만 씁니다. 지령과 설정값이 다르면 드라이버가 거부·클램프한 것이므로 화면에서 드러나야 합니다. 제어 경로에 넣으면 통신 지연만큼 적분이 뒤처집니다.

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
[ModbusPortWorker: CH1]  Task + CancellationToken, 단일 스레드
     │  Fast tier 매 사이클 / Slow tier 카운터 기반
     │  ※ RS-485 반이중 → 포트당 트랜잭션 직렬화 (SemaphoreSlim(1,1))
     ├─ InterlockGuard.Evaluate(raw)   ← 인터록은 폴링 스레드에서 즉시 실행
     └─> DataStore.Publish(snapshot)   ← Volatile.Write / Interlocked.Exchange

[ModbusPortWorker: CH2]  동일 구조, 포트별 독립 병렬

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
| ~~1~~ | **Operate (View)** | FFU RPM, 센서1-1/1-2/1-3, MFC, 센서2-1~2-5 / 스로틀밸브 / 송풍팬 / 센서3-1~3-5 **현재값 5열 그리드**, 우측 Auto mode·Sensor Mode(1/2/3)·풍속1~3·Particle·Temp/Humidity | Operator (읽기) **→ 2026-08-10 답변.** CH1 Fast 218 ms 실측 계산(COMM_MAP §0.2). 19200 유지 시 100 ms 불가. 38400 상향 + 2포트 분할 병행해야 90 ms 달성 |
| 2 | **Operate (Set)** | 위 그리드에 밸브/팬 **설정값 + [SET] 버튼** 추가, Sensor 1/2/3 Mode 선택, MFC 설정값 | Maintenance |
| 3 | **Config** | S1-1~1-3 / S2-1~2-5 / S3-1~3-5의 **Set · ±범위 · Time** 테이블, FFU/Particle/Temp/Humidity High·Low Limit, 풍속1~3 Low Limit | Engineer |
| 4 | **I/O (Status)** | ①FFU ②송풍팬 ③스로틀밸브 ④차압센서 ⑤FDC ⑥쿨링팬 ⑦Temp(CtrlBox) ⑧Temp(EFEM) ⑨Humidity ⑩Particle ⑪MFC ⑫풍속 — **연결/정상동작 상태 램프**, PLC 디지털 입력 D10.0~D10.8 | Maintenance |
| ~~5~~ | **Data Log** | 트렌드 차트 + 알람 이력 + CSV Export | Operator **→ 2026-08-10 확정.** IO List Driver 시트로 전량 확보 |
| 6 | **Alarm Popup** | 활성 알람 리스트, Ack, Reset(권한), 이력 조회 | 상황별 |
| ~~7~~ | **Maintenance** | 아래 7.3 참조 | Engineer **→ 2026-08-10 확정.** DI 8점에 도어·차단기 접점 없음. IL-03·IL-05 비활성. SPARE DI 2점 배선 시 활성화 |

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

> **설정 파일 모델은 `CONFIG_MODEL.md` 로 분리했습니다 (2026-08-10).**
> `ESAM_IO List_260806.xlsx` 에 `ECID`(39) · `SVID`(43) · `Alarm LIST`(66) 시트가 추가되어
> 파일 간 역할과 의존 방향을 확정해야 했습니다. 요약하면 세 가지입니다.
>
> - `device-map.json` 하드웨어 사양 / `recipe.json` 운전 파라미터(ECID) / `alarms.json` 알람 설정
> - **숫자 임계값은 `recipe.json` 에만 산다.** 알람은 참조만 한다
> - 설정값이 **모드별 공통에서 센서별 개별로** 바뀐다. `Time` 만 모드별 공통으로 남는다


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
| **S5** ✅ | **11.1 결함 11건 전량 해소** — SafeStop 라우팅, 인터록 트리거 소실, 원점 복귀 시퀀스, 상태머신 락, 종료 파킹, 알람 규칙 집합, 구성 경고 강제, 안전 경로 실패 감지, 데이터 신선도, 지령 라우팅 | 2026-08-10 완료. 회귀 테스트 40여 건 추가 |
| **C1·C2** ✅ | 설정 모델 정리 — `recipe.json`(ECID 마스터) 신설, 제어 설정값을 모드별 공통에서 **센서별 개별**로 전환 | 2026-08-10 완료. CONFIG_MODEL.md 1·2단계 |
| **S5.5** ✅ | **빌드·테스트 복구와 결함 3건 추가 해소** — 지령 실패 되먹임(D13), 경고 목록 경합(D12), 장애 SafeStop 자동 해제(D14) | 2026-08-11 완료. **테스트 385건 전량 통과** |
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
| ~~1~~ | **폴링 주기 목표 100ms 유지 여부** (2.2 B) | **아키텍처·BOM** | HW팀 **→ 2026-08-10 결정. 하드웨어 추가 없이 218 ms 로 현실화.** 차압계 시정수(1.0~1.5초) 대비 충분하며, 포트 증설의 이득이 비용에 못 미침 |
| 2 | Sensor 1 Band 불일치 (`± 2 Pa` vs `5<x<7`) | 제어 정확도 | CTO |
| 4 | 1차 릴리스 제어 방식: 순서도 밴드제어 확정 vs PID 병행 | 제어 구현 | CTO |
| ~~5~~ | 밸브 레지스터 상세 (FC코드, 0x6002 비트 정의, 0x1003/0x2203 비트맵, 엔디안) | 밸브 드라이버 | 밸브 설계자 **→ 2026-08-10 확정.** IO List Driver 시트로 전량 확보 |
| 6 | 인터록 IL-01 범위: 해당 체인만 vs 전체 정지 | **안전** | CTO |
| ~~7~~ | Door Open(D10.7) / EMO(D10.6) 인터록 정책 및 복귀 절차 | **안전** | CTO **→ 2026-08-10 확정.** DI 8점에 도어·차단기 접점 없음. IL-03·IL-05 비활성. SPARE DI 2점 배선 시 활성화 |
| 8 | Data Log Viewer 방식 (PDF p.8 미정) — 7.3 제안 검토 | HMI | CTO |
| ~~9~~ | **송풍팬 Modbus 레지스터 명세** (RPM 설정/현재값, ON·OFF, 알람, 상태). 자사 제작품이므로 **연속 주소 배치 + 밸브와 동일 주소체계**로 설계 요청 시 드라이버 재사용·트랜잭션 절감 가능 | 팬 드라이버 | 팬 설계자 **→ 2026-08-10 확정.** JKBLD300V2 매뉴얼 확보. 단 0x4007 vs 0x4006 표기 충돌은 IO List 수정 필요 |
| 10 | MFC 사양/레지스터 (xlsx "미정"), 파티클센서 프로토콜, FFU 통신 방식, FDC 프로토콜 | 확장 기능 | 구매/HW팀 |
| 11 | 온습도/파티클/컨트롤박스온도가 BOM에서 모두 `PSU650-AR` 동일 품번 — 1대 통합형인지 3대 별도인지 (통신 시트에는 온습도 ID15만 존재) | 통신 구성 | HW팀 |
| 12 | 컨트롤박스 FAN이 BOM에는 RS-485, PLC Signal에는 D10.5 알람 — 제어 대상인지 감시 대상인지 | I/O 설계 | HW팀 |
| 13 | 풍속센서 품번 2종 혼재 (AVS701 E+E / 985VM Beck) — 통신 시트는 BECK985 | 드라이버 | 구매 |
| 14 | 로그 리텐션 정책 및 데드밴드 필터 적용 여부 (일 138MB) | 저장용량 | CTO |
| 15 | CH2 표기의 의미 — 현재 장비가 2채널(CH1/CH2) 구성 중 CH2만 명세인지, 향후 CH1 추가 시 센서/밸브/팬 2배 확장인지 | **확장성 설계** | CTO |
| ~~16~~ | **Sensor 1 Mode에서 5개 체인이 참조할 센서** — PDF는 "센서 1은 1개만 설치"라 하지만 실제 S1-1/1-2/1-3 3개 존재. (a) S1-1 단독 (b) 3개 평균 (c) 체인별 매핑 | **제어 로직** | CTO **→ 2026-08-10 근거 확보.** DP-01~03 이 EC·SL·SR 3곳에만 설치. EL·ER 체인은 Sensor 1 없음 |
| ~~17~~ | 차압센서 레인지 배정 (±750 Pa vs ±2.0 kPa) — 위치별 어느 품번인지. Sensor 3 Mode는 -300 Pa까지 사용 | 스케일링/알람 | HW팀 **→ 2026-08-10 확정.** 차압 13대 전량 동일 사양, 0x4001. 별도 Autonics PSM 압력센서 5채널 추가 확인 |
| ~~18~~ | PLC D영역 ↔ Modbus 주소 변환 규칙 (LS XBM), D10 비트 극성(Active High/Low), 온도 워드 스케일 | PLC 드라이버 | PLC 담당 **→ 2026-08-10 확정.** 0x000A 비트마스크 8점, 온도 0x0064~0x0068, 단선 0x006E~0x0072 |
| ~~19~~ | **송풍팬 BUS_C 통신 파라미터 확정** — Baud(38400 가정), Parity, Slave ID(1~5 가정) | 통신 | HW팀 **→ 2026-08-10 확정.** CH2 38400 8-N-1, 블로워 ID 6~10. 드라이버 기본값 115200 이라 0x4044=2 설정 필요 |
| ~~20~~ | 송풍팬 최대 RPM / 최소 동작 RPM 사양 — 미확보 시 자동제어 진입 불가 | 제어 | 팬 설계자 **→ 2026-08-10 확정.** 폐루프 0x4006 유효범위 200~4000 rpm. 20000 은 드라이버 전기사양이며 제어값 아님 |
| ~~3~~ | Config `Time` 컬럼 의미 (알람 디바운스? 제어 유지시간? 안정화 대기?) | 알람/제어 | CTO **→ 2026-08-10 확정.** 대역 이탈 확정 시간이며 **모드별 공통**. 센서별 개별값이 아니므로 ECID 대상이 아니고 control.json 에 남는다 |
| 22 | **PLC 미러링 차압값의 워드 수** — `0x82`/`0x84`/`0x86` 및 오프셋 `0x8C`/`0x8E`/`0x90` 이 2워드 간격이라 32비트일 가능성. Int16 으로 읽으면 값이 완전히 틀어진다 | 스케일링 | PLC 담당 |
| 23 | **`SVID` 시트 24~28 명칭** — PSM 은 Bottom(Blower Pressure Sensor)으로 확정. 시트의 "Front" 표기 수정 필요 | 상위 연동 | HW팀 |
| 21 | **IL-01 발동 임계값 확정** — 현재 0 Pa(대기압) 로 잠정 설정(5.2.1). 배기 계통 설계상 허용 최소 음압이 있다면 그 값으로 대체 | **안전** | HW팀 |

#### D1~D4 해소 내역 (2026-08-10, S5)

**D4 — 상태머신 락.** `Fire` 의 판정과 대입을 하나의 원자 단위로 묶었습니다. 나눠 두면 두 스레드가 같은 현재 단계를 읽고 서로 다른 다음 단계를 써서, 나중에 쓴 쪽이 이깁니다. 작업자가 자동 버튼을 누르는 순간 폴링 스레드가 인터록을 발동시키면 **인터록 래치를 안은 채 `AutoControl`** 에 들어갔습니다. `PhaseChanged` 는 락 밖에서 발생시켜, 구독자가 `Fire` 를 다시 호출해도 재진입이나 교착이 생기지 않게 했습니다.

**D2 — 인터록 트리거 소실.** 두 곳을 고쳤습니다.

- `Resolve` 가 `InterlockRaised` 를 **모든 비-SafeStop 단계**에서 수용합니다. 종전에는 `Ready`·`AutoControl` 에서만 처리해, 원점 복귀 중 EMO 를 누르면 전이가 무시됐습니다.
- 상태 반영을 **엣지에서 상태 기반으로** 바꿨습니다(`EsamRuntime.ReconcileInterlock`). 종전에는 `Tripped` 이벤트(false→true 엣지)로 한 번만 시도하고, 실패하면 `_wasTripped` 가 true 로 남아 다시 시도하지 않았습니다. 이제 매 폴링마다 현재 상태를 대조하므로 전이가 한 번 실패해도 다음 사이클에 복구됩니다. **안전 기능에 "한 번 놓치면 끝"인 구조를 두어서는 안 됩니다.**

**D1 — SafeStop 라우팅.** `RequiresSystemStop`(EMO·차단기·안전입력 상실)이면 `SafeStopRaised`, 체인 범위면 `InterlockRaised` 로 갈라 보냅니다. `Interlocked` 는 해제 시 `Ready` 로 바로 복귀하지만 `SafeStop` 은 `Fault → Init → 원점 복귀` 를 거칩니다. 물리 안전장치가 동작한 뒤에는 밸브 위치를 다시 확인하고 시작해야 합니다.

**D3 — 원점 복귀 시퀀스.** 제어 엔진의 주기 루프가 기동 단계도 진행시킵니다.

```
Init         모든 밸브가 Quality.Good 으로 읽히면 InitCompleted
             HomingTimeoutMs 초과 시 FaultRaised
ValveHoming  미완료 밸브에 HomeValve 를 1회씩 지령
             homeDone 이 전부 참이면 HomingCompleted
             HomingTimeoutMs 초과 시 FaultRaised
```

지령을 매 스텝 반복하지 않는 이유는, 복귀 중인 드라이브가 같은 지령을 다시 받으면 동작을 재시작해 영영 끝나지 않기 때문입니다.

`Stop()` 이 `Stop` 트리거를 내도록 했습니다. 단계가 `AutoControl` 로 남으면 재시작 시 `Start` 트리거가 무시되어 **초기화와 원점 복귀를 건너뛴 채 자동 운전 상태에서 재개**됩니다.

시뮬레이션 기본값도 바꿨습니다. `RuntimeOptions.PreHomeValves` 기본 false 로 두어 **전원 투입 직후 상태를 그대로 재현**합니다. true 로 두면 원점 복귀 경로를 한 번도 지나지 않아, 시퀀스가 깨져 있어도 시뮬레이션에서 드러나지 않습니다. 통합 테스트의 `AdvanceToReady` 도 트리거를 직접 발생시키던 것에서 **실제 시퀀스를 돌리는 방식**으로 바꿨습니다.

---

#### D5·D9·D10 해소 내역 (2026-08-10)

**D9 — 알람 규칙 집합.** `config/alarms.json` 34종을 만들고 `RuntimeOptions.AlarmRulesPath`(기본 `config/alarms.json`)로 배선했습니다. 종전에는 `AlarmRules` 를 채우는 코드가 어디에도 없어 **DESIGN 5.1 의 알람이 어떤 구성에서도 동작하지 않았습니다.** 인터록 IL-01 의 주석이 "초기 배기 불량은 대역 이탈 알람이 잡는다" 고 적어 두어, 존재하지 않는 기능에 안전 판단을 기대고 있었습니다.

설계 원칙 두 가지를 지켰습니다.

- **임계값을 중복해 적지 않습니다.** 대역 이탈은 `OutOfBand` + `referenceMode` 로 Config 의 값을 참조합니다. JSON 에 숫자를 복사해 두면 Config 화면에서 바꿨을 때 알람만 옛 값으로 남습니다.
- **인터록보다 먼저 울립니다.** `P09`(배기 음압 저하, -100 Pa)를 새로 넣었습니다. IL-01 은 0 Pa 에서 Manual 래치로 걸려 장비가 멈춥니다. 그 전에 알려야 대응할 여지가 생깁니다. 경고 임계값이 인터록 임계값보다 낮은지 테스트로 강제합니다.

비활성 5건(A05·A12·A13·A18·A19)은 전부 명세 미확보 장치입니다. **확보된 장치의 알람이 비활성이면 실패**하는 테스트를 두어 감시 공백이 슬쩍 생기지 못하게 했습니다. csproj 에서 배포 파일 자체를 테스트 출력으로 복사하므로, 샘플만 통과하고 배포본이 깨지는 상황이 생기지 않습니다.

**D10 — 구성 경고.** `List<string>` 을 `ConfigWarning { Code, Severity, Message, Remedy }` 로 바꿨습니다. 종전에는 "안전 입력이 하나도 없다" 와 "MFC 주소가 미확정이다" 가 같은 무게로 섞여 있었고, `Describe()` 는 건수만 출력했으며 HMI 는 이 계층을 참조조차 하지 않아 **어떤 경로로도 작업자에게 도달하지 않았습니다.**

심각도는 두 단계입니다. 판단해야 할 것이 "이 상태로 자동 운전에 들어가도 되는가" 하나이기 때문입니다.

| 등급 | 의미 | 해당 |
|---|---|---|
| `Blocking` | 안전 기능이 동작하지 않음 | `SAFE-01` 안전 입력 없음, `SAFE-02` 인터록 미확정·비활성, `ALM-01` 알람 로드 실패 |
| `Advisory` | 일부 계측이 빠짐 | `CFG-ADDR` 주소 미확정, `ALM-02` 개별 알람 비활성, `SIM-01` 시뮬레이션 슬레이브 없음 |

**`Blocking` 경고가 확인되지 않으면 `RequestAuto()` 가 거부합니다.** 화면 연결(S7) 전에도 효력이 생기도록 진입 지점에 걸었습니다. `AcknowledgeWarnings()` 는 경고를 없애지 않고 **사람이 인지했음을 기록**할 뿐이라 목록은 그대로 남습니다.

시운전 단계에서 매번 확인을 눌러야 하는 것이 번거로울 수 있으나, 그것이 목적입니다. 안전 입력 없이 운전 중이라는 사실을 한 번은 인지해야 합니다.

**D5 — 종료 시 액추에이터 파킹.** 리뷰가 지적한 전제는 S5 에서 이미 바뀌었습니다. 인터록 무장 게이트를 걷어내고 임계값을 0 Pa 절대값으로 바꿨으므로 **자동 운전을 꺼도 인터록이 모든 단계에서 판정합니다.** 그래서 `StopAuto()` 는 액추에이터를 그대로 둡니다. 웨이퍼 처리 중에 송풍팬을 세우면 오히려 봉쇄가 무너집니다. "자동 제어를 끈다" 와 "기류를 멈춘다" 는 다른 요구입니다.

남은 진짜 문제는 **종료 경로**였습니다. `Stop()`/`Dispose()` 는 워커를 멈춰 인터록 평가가 함께 끝나는데, 밸브는 열려 있고 팬은 계속 돕니다. 아무도 보지 않는 상태로 남습니다.

```
1. 제어 엔진 정지          새 자동 지령 차단
2. ParkActuators()         전 체인 밸브 Close + 팬 OFF (Interlock 우선순위)
3. WaitForPark(timeoutMs)  실행 확인까지 대기
4. Fire(Stop) → 워커 정지
```

순서가 중요합니다. 폴링을 먼저 멈추면 파킹 지령이 큐에만 남고 전송되지 않습니다. 확인이 안 되면 경고를 남기고 진행합니다.

파킹은 **비활성 체인도 포함**합니다. 안전 정지에 예외를 두지 않습니다. 정전이나 강제 종료에서는 이 경로가 실행되지 않는 한계가 있으나, 대부분의 종료가 정상 종료이므로 값어치가 있습니다.

---

#### D6·D11 해소 내역 (2026-08-10) — 실패가 조용히 사라지지 않게

두 결함은 형태가 같습니다. **안전 기능이 동작하지 못했는데 아무도 알지 못했습니다.**

**D11 — 판정 예외가 흔적 없이 사라졌습니다.** `OnPollCompleted` 에 try/catch 가 없어, 예외가 포트 워커의 `catch (Exception) { }` 로 흘러갔습니다. 워커는 살아남지만 그 사이클의 인터록·알람 평가는 수행되지 않습니다. 예외가 결정적이면 **인터록이 영구히 꺼진 채** 로그도 알람도 카운터도 없이 운전이 계속됩니다.

예외를 다시 던지지 않는 이유는 폴링 스레드를 죽이면 통신 전체가 멎기 때문입니다. 대신 세고, 연속되면 SafeStop 으로 보냅니다. 안전 판정이 수행되지 않는 상태를 조용히 넘기는 것보다 장비를 세우는 편이 낫습니다.

**D6 — 안전 지령의 도달을 확인하지 않았습니다.** `CommandFailed` 구독자가 하나도 없었습니다. `CloseValve` 는 위치 설정 → PR0 이동 2단 시퀀스라, 두 번째가 타임아웃하면 **밸브가 전혀 움직이지 않습니다.** 그런데 `Tripped` 이벤트는 이미 "인터록이 처리됐다" 고 알린 뒤입니다.

두 겹으로 확인합니다.

- **지령 실패 집계** — 담당 워커에서 인터록 지령이 실패하면 디바이스별로 셉니다. 인터록 지령은 전 워커에 뿌리므로 담당하지 않는 워커의 실패는 정상이며, 그것까지 세면 정상 동작이 장애로 집계됩니다. 담당 여부는 워커의 사유 문자열이 아니라 device-map 으로 판정합니다.
- **실효 확인** — 발동 후 `MoveTimeoutMs` 가 지나도 밸브가 안전 위치가 아니면 보고합니다. 지령을 다시 보내는 것으로는 부족합니다. 같은 경로로 다시 실패할 뿐입니다.

**임계값 3회**는 250 ms 폴링에서 약 750 ms 입니다. 일시적 노이즈는 넘기고 지속적 장애는 1초 안에 잡는 절충점입니다. 한 번의 타임아웃으로 장비를 세우는 것은 과합니다.

**에스컬레이션은 `Interlocked` 가 아니라 `SafeStop`** 입니다. 여기 도달했다는 것은 "안전 조건이 성립한 상태" 가 아니라 **"안전 기능이 동작하지 못하는 상태"** 이기 때문입니다. 전자는 원인이 해소되면 Ready 로 복귀하지만, 후자는 원인을 확인하고 원점 복귀부터 다시 시작해야 합니다.

`RuntimeDiagnostics` 는 스레드 안전합니다. 포트 워커 3스레드가 동시에 기록합니다.

---

#### D7·D8 해소 내역 (2026-08-10) — S5 완료

**D8 — 인터록이 15초 낡은 값으로 판정할 수 있었습니다.** `SnapshotBuilder` 의 Stale 임계값은 Slow 티어(5초 주기)까지 덮어야 해서 15초로 잡혀 있습니다. 그래서 **Fast 티어 센서가 14초 갱신되지 않아도 품질은 여전히 Good** 입니다. 250 ms 응답을 목표로 하는 안전 기능이 그 값을 쓰면 두 방향으로 틀립니다. 이미 해소된 조건으로 발동하거나, 실제로 상승한 압력을 14초 동안 놓칩니다.

`InterlockRule.MaxDataAgeMs`(기본 1000 ms = Fast 폴링의 4배)를 두어 인터록이 자체 기준으로 검사합니다.

여기서 한 가지를 더 분리했습니다. **"발동하지 않음" 과 "판정하지 못함" 은 다릅니다.** 둘 다 `Trips` 가 비어 있지만, 후자는 인터록이 눈을 감은 상태입니다. `InterlockEvaluation.UnjudgeableChainIds` 로 사실만 보고하고, 정책 판단(몇 사이클까지 봐줄 것인가)은 조립 루트가 합니다.

운전 중 판정 불가가 8사이클(약 2초) 이어지면 SafeStop 으로 보냅니다. 센서 3 을 읽지 못하면 배기 상실을 감지할 수단이 없기 때문입니다. 정지 중에는 세지 않습니다 — 측정값이 없는 것이 정상이고 액추에이터도 움직이지 않습니다.

**D7 — 인터록이 활성일 때 버스가 가장 바빴습니다.** 지령을 전 워커에 뿌리고 담당하지 않는 워커가 무시하게 했습니다. "안전 경로에서 라우팅 판단을 하다 실수하는 것보다 확실하다" 는 이유였는데 대가가 컸습니다.

2포트 구성에서 래치 1건이면 사이클당 2회 평가 × 2 워커 × 2 지령 = 8회 enqueue 이고, 절반은 담당하지 않는 워커에서 실패합니다. 실패는 `CommandFailed` 로 흘러 D6 의 진단 카운터를 오염시키고, 담당 워커는 같은 지령을 중복 실행합니다. **안전 기능이 활성인 동안 폴링 사이클이 늘어나 2차 위험 검출이 늦어집니다.** 정확히 반대로 가는 것입니다.

두 가지를 고쳤습니다.

- **담당 워커로만 라우팅** — device-map 에서 `디바이스 ID → 워커` 경로표를 조립 시점에 만듭니다. 라우팅 실수 우려는 구성 검증으로 해소됩니다. 매 사이클 추측하는 것보다 한 번 확인한 표를 쓰는 편이 확실합니다. 경로표에 없는 장치는 안전하게 전 워커로 보냅니다.
- **재투입 억제** — `ReassertIntervalMs`(기본 2000 ms) 간격으로만 다시 보냅니다. 한 번만 보내고 마는 것도 안 됩니다. 지령이 유실되면 복구할 방법이 없어집니다. `Reset` 시에는 이력을 비워 재발동 시 즉시 나가게 합니다.

부수 효과로 D6 의 담당 판정이 단순해졌습니다. 담당하지 않는 워커의 실패가 애초에 발생하지 않습니다.

---

### 11.2 ESAM_UI_설명서.pptx 로 확인된 범위 확장 (2026-08-10)

수령한 UI 설명서 11화면 중 현재 설계(§7)에 없는 항목입니다. 일정·범위 재산정이 필요합니다.

| 화면 | 신규 요구 | 비고 |
|---|---|---|
| SCREEN 10 | **SECS/GEM (HSMS) 상위 통신** — 연결 상태, SVID 보고, S5F1 알람 송수신 | §1.2 Out of Scope 에 없던 항목. 라이브러리 선정과 인증 시험이 별도 과제 |
| SCREEN 07 | 사용자 계정 관리 (사번 / Password / Level) | §7 에 권한 개념은 있으나 계정 CRUD 화면은 없었음 |
| SCREEN 08 | **알람 코드 런타임 등록·편집** | 설계는 `alarms.json` 정적 로드 전제. 화면에서 정의를 바꾸려면 저장·검증 경로가 추가로 필요 |
| SCREEN 02 | Recipe Mode **S1~S4 + ENV 탭** | 설계는 Sensor 1/2/3 3모드. **S4 와 ENV 가 무엇인지 확인 필요** |
| SCREEN 05 | Log Viewer 다중 탭 (System / Sequence / Zone별 / Comm / CommCh1·2 / Gem) | 별도 창. Zone(Left/Center/Right) 개념이 설계에 없음 |
| SCREEN 06 | 기류 순환 통로 유닛 1~5 개별 활성/비활성 | `chains.json` 의 `enabled` 로 대응 가능 |
| SCREEN 01 | recipe.json 경로 설정, 게이지 눈금 상한 설정 | 경미 |

특히 **Recipe 의 S4·ENV 탭**은 제어 로직에 직접 영향을 줍니다. 센서 모드가 4개라면 `SensorMode` 열거형과 밴드 제어 분기를 확장해야 합니다. → 신규 협의 항목

---

### 11.0 S5.5 — 빌드 복구 과정에서 드러난 결함 3건 (2026-08-11 완료)

S5·C1·C2 를 진행하는 동안 `Esam.Domain` 이 **CS1573 하나로 컴파일에 실패**하고 있었습니다. 메타데이터 오류만 표시되어 원인이 보이지 않았고, 그 사이 `Esam.Services` 는 D7 이후 한 번도 컴파일되지 않았습니다. 즉 **안전망이 통째로 빠진 상태로 8개 작업이 누적**되었습니다.

빌드를 되살리자 실패 26건이 나왔고, 원인을 추적하는 과정에서 결함 3건이 드러났습니다.

| # | 결함 | 실패 시나리오 | 심각도 |
|---|---|---|---|
| **D12** | `Warnings` 가 내부 리스트를 그대로 반환 | 워커 스레드가 경고를 추가하는 중 화면이 열거하면 `InvalidOperationException`. **경고를 보여주려던 화면이 죽는다** | 높음 |
| **D13** | 지령 실패 → 장애 보고 → 파킹 지령 → 지령 실패 되먹임 | 큐를 경유하므로 스택 가드로 막히지 않는다. **폴링 사이클이 끝나지 않아 통신·인터록 판정·화면 갱신이 영구히 멈춘다** | **치명** |
| **D14** | 인터록이 올리지 않은 SafeStop 을 인터록이 해제 | 장애로 SafeStop 에 갔다가 다음 사이클에 스스로 Fault 로 내려가고, 판정 불가 카운터까지 0 으로 돌아가 다시 올리지 못한다. **장애를 알리고 즉시 잊는다** | **치명** |

D13 의 구조적 대책은 `ProcessCommands` 의 지령 처리 상한입니다. 되먹임 경로를 하나 막는 것보다 **사이클이 반드시 끝나는 것을 보장**하는 편이 근본적입니다. 누가 무엇을 큐에 넣든 상관없어집니다.

D14 는 "누가 올린 정지인지" 를 기억하지 않은 것이 원인입니다. 인터록이 올린 정지는 인터록 해소로 풀리지만, 장애로 올린 정지는 작업자가 `ResetRuntimeFault` 로 해제해야 하고 그때도 Ready 가 아니라 Fault 로 갑니다.

함께 드러난 것 중 **문서와 구현이 정반대였던 사례**가 있습니다. `EsamRuntime.Stop` 은 "Idle 복귀" 라고 적고 "단계가 남으면 초기화와 원점 복귀를 건너뛴 채 재개된다" 고 위험까지 설명해 두었는데, 상태머신은 `AutoControl + Stop → Ready` 였습니다. 그 전이를 단정하는 테스트가 없어 아무도 막지 못했습니다.

> **교훈은 빌드 실패를 방치한 것입니다.** 경고를 오류로 승격시켜 둔 것은 옳았지만, 메타데이터 오류 7건을 "환경 문제" 로 넘기지 않고 첫날 추적했다면 D13·D14 를 8개 작업 전에 잡았을 것입니다.

---

### 11.1 S4 검토에서 발견된 결함 11건 — 전량 해소 (2026-08-05 발견 / 2026-08-10 완료)

S4 통합 검증 중 정적 리뷰에서 찾은 항목입니다. 세 건(지령 상쇄·스레드 경합·PLC 품질 판정)은 발견 즉시 5.2.3·5.2.4·5.2.2에서 처리했고, 나머지 11건은 S5에서 전량 해소했습니다.

> **하드웨어 없이 찾은 결함입니다.** S2에서 가상 플랜트를 먼저 구축한 덕분에, 실장비 입고 전에 인터록이 기동을 막는 문제·안전 지령이 상쇄되는 문제·EMO 비트 오매핑을 모두 시뮬레이션 위에서 잡았습니다. 실장비에서 발견했다면 원인 규명에 훨씬 큰 비용이 들었을 항목들입니다.

| # | 결함 | 실패 시나리오 | 심각도 |
|---|---|---|---|
| ~~D1~~ ✅ | **EMO가 SafeStop에 도달하지 않는다.** `SystemTrigger.SafeStopRaised` 가 프로덕션 코드에서 한 번도 발생하지 않고, IL-02/IL-03은 `InterlockRaised` 로만 전이된다 | EMO가 순간 해제되면 `Interlocked → InterlockCleared → Ready` 로 복귀. 설계는 `SafeStop → Fault → Init → ValveHoming`(재원점) 을 의도했다 | **높음** |
| ~~D2~~ ✅ | **`InterlockRaised` 가 일부 단계에서 무시되고, 그 엣지가 소실된다.** `Resolve` 는 `Ready`/`AutoControl` 에서만 처리하는데 `InterlockGuard` 는 false→true 엣지에서만 `Tripped` 를 발생시킨다 | Homing 중 EMO를 누르면 정지 지령은 나가지만 트리거가 버려지고 `_wasTripped=true` 로 남아 재시도되지 않는다. `ValveHoming` 에 머물며 UI는 인터록을 표시하지 않는다 | **높음** |
| ~~D3~~ ✅ | **`Start()` 가 원점 복귀를 확인하지 않고 완료 선언한다.** `HomeValve` 는 프로덕션 경로에서 전송되지 않는다 | `homeDone` 이 false면 밴드 제어가 전 스텝 Skipped → 화면은 AutoControl인데 아무것도 제어되지 않는다. true면 기계적 원점이 미확정이라 `CloseValve` 의 0 pulse도 실제 닫힘이 아니다 | **높음** |
| ~~D4~~ ✅ | **`SystemStateMachine` 이 스레드 안전하지 않다.** 폴링 3스레드 + 제어 스레드 + UI가 `Fire` 를 호출한다 | Auto 버튼과 인터록 발동이 겹치면 `Ready→AutoControl` 과 `Ready→Interlocked` 가 경합해 **인터록 래치를 안은 채 AutoControl** 에 들어간다 | **높음** |
| ~~D5~~ ✅ | **`StopAuto()` 가 액추에이터를 정지시키지 않는다.** 큐만 비우고 트리거만 발생시킨다 | Auto를 끄면 팬이 계속 돌고 밸브가 열린 상태로 남는다 | 중간 |
| ~~D6~~ ✅ | **인터록 지령의 실행 성공을 검증하지 않는다.** `worker.CommandFailed` 구독자가 없다 | `CloseValve` 는 `setPosition` → `prMove` 2단 시퀀스인데 2번째가 타임아웃하면 밸브는 전혀 움직이지 않는다. 그런데 `Tripped` 는 이미 "처리됨"을 알렸다 | 중간 |
| ~~D7~~ ✅ | **인터록이 3중 평가·9중 투입된다.** 포트 3개가 각각 판정하고 결과를 전 워커에 뿌린다 | 래치 1건에 사이클당 18회 enqueue, 담당 워커가 중복 3회 실행(6 트랜잭션). 인터록 활성 중 폴링 사이클이 늘어나 2차 위험 검출이 늦어진다 | 중간 |
| ~~D8~~ ✅ | **스냅샷 staleness 15초를 인터록이 검사하지 않는다.** `Quality != Good` 만 본다 | 최대 15초 낡은 값으로 판정한다. 250ms 목표의 60배 | 중간 |
| ~~D9~~ ✅ | **알람 규칙 집합이 존재하지 않는다.** `RuntimeOptions` 는 `AlarmRules` 를 채우지 않고 `config/alarms.json` 도 없다 | DESIGN 5.1의 31종 알람이 어떤 구성에서도 동작하지 않는다 | 중간 |
| ~~D10~~ ✅ | **구성 경고가 화면에 도달하지 않는다.** `Describe()` 는 건수만 출력하고 HMI는 `Esam.Services` 를 참조하지 않는다 | "안전 입력 없음", "IL-01 임계값 미지정" 같은 경고를 작업자가 볼 수 없다 | 중간 |
| ~~D11~~ ✅ | **`OnPollCompleted` 의 예외가 흔적 없이 삼켜진다.** 워커의 `catch (Exception) { }` 로 흘러간다 | 예외가 결정적이면 인터록 평가가 무한히 중단되는데 로그·알람·카운터가 전부 없다 | 중간 |

---

## 12. 다음 단계

승인 시 진행 순서:
1. `docs/COMM_MAP.md` 검토 → Config JSON 스키마 확정
2. 솔루션 골격 및 `Esam.Domain` 모델/상태머신 코드 작성 (S1)
3. Simulation 드라이버 구축 (S2)

**본 문서는 검토용 초안이며, 승인 전까지 코드 작성은 진행하지 않습니다.**
