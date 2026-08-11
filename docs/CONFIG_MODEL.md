# 설정 파일 모델 — device-map / recipe(ECID) / alarms / gem-map

> 작성일: 2026-08-10
> 근거: `ESAM_IO List_260806.xlsx` (IO · Driver · Alarm LIST · ECID · SVID 시트), `ESAM_UI_설명서.pptx`
> 상태: **스키마 확정 / 구현 대기**

---

## 1. 파일마다 역할이 하나씩

| 파일 | 역할 | 바꾸는 사람 | 바꾸는 빈도 |
|---|---|---|---|
| `device-map.json` | **하드웨어 사양** | 커미셔닝 엔지니어 | 설치 시 1회 |
| `control.json` | 제어 알고리즘 파라미터 | Engineer | 튜닝 시 |
| `recipe.json` | **운전 파라미터 (ECID 마스터)** | Engineer / GEM 상위 | 레시피 변경 시 |
| `alarms.json` | **알람 설정** | Engineer | 드물게 |
| `gem-map.json` | ECID/SVID 번호 매핑 | 상위 연동 담당 | 연동 시 1회 |
| `interlocks.json` | 인터록 규칙 | **안전 담당자** | 거의 없음 |

**바꾸는 빈도가 다르면 파일을 합치지 않는다**가 분리 기준입니다. 설치 시 한 번 정하는 값과 레시피마다 바뀌는 값이 같은 파일에 있으면, 레시피를 바꿀 때마다 하드웨어 설정을 건드릴 위험이 생깁니다.

---

## 2. 의존은 한 방향으로만

```
device-map.json          하드웨어 물리 한계 (rangeMin/Max, maxPulse, minRpm/maxRpm)
       ↑ deviceId
recipe.json  (ECID)      운전 설정값 (setpointPa, highLimitPa, lowLimitPa)
       ↑ deviceId + limitKind
alarms.json              알람 정의 (code, name, severity, condition)  ← 숫자 없음

gem-map.json  ─────→ 위 셋을 경로로 참조 (역방향 없음)
control.json  ─────→ 모드별 Time, 액추에이터 파라미터 (센서와 무관)
```

### 규칙 1 — 숫자 임계값은 `recipe.json` 에만 산다

`alarms.json` 은 **어느 센서의 어느 한계를 볼 것인가**만 지정합니다. 값을 복사해 두면 Config 화면에서 설정을 바꿨을 때 알람만 옛 값으로 남습니다. 그 상태는 화면과 알람이 서로 다른 진실을 말하는 것이라, 현장에서 원인을 찾기 매우 어렵습니다.

### 규칙 2 — 하드웨어 물리 한계는 `device-map.json` 에만 산다

`recipe.json` 값은 그 범위 안에서만 유효합니다. 센서 레인지(±2000 Pa)를 넘는 설정값을 넣으면 도달 불가능한 목표를 영원히 추종합니다.

### 규칙 3 — GEM 번호는 `gem-map.json` 에만 산다

`recipe.json` 에 ECID 번호를 섞지 않는 이유가 둘입니다. GEM 을 쓰지 않는 설치에서도 `recipe.json` 은 필요하고, 상위가 ECID 번호를 재배정해도 레시피는 건드릴 일이 없습니다. SVID(현재값)는 레시피와 무관하니 어차피 별도 매핑이 필요한데, 같이 두면 GEM 관련이 한곳에 모입니다.

---

## 3. `recipe.json` — ECID 마스터

`ECID` 시트 39항목 = 압력센서 13대 × (설정값 + 상한 + 하한).

```jsonc
{
  "schemaVersion": "1.0",
  "name": "기본 레시피",

  // 센서 13대 각각의 설정값·상한·하한.
  // 상한과 하한을 독립적으로 주므로 비대칭 대역이 가능하다.
  // 배기는 상한 여유와 하한 여유가 다를 수 있어 이쪽이 물리적으로 맞다.
  "sensors": [
    { "deviceId": "S1-1", "setpointPa":    6.0, "highLimitPa":    8.0, "lowLimitPa":    4.0 },
    { "deviceId": "S1-2", "setpointPa":    6.0, "highLimitPa":    8.0, "lowLimitPa":    4.0 },
    { "deviceId": "S1-3", "setpointPa":    6.0, "highLimitPa":    8.0, "lowLimitPa":    4.0 },

    { "deviceId": "S2-1", "setpointPa":  -10.0, "highLimitPa":   20.0, "lowLimitPa":  -40.0 },
    { "deviceId": "S2-2", "setpointPa":  -10.0, "highLimitPa":   20.0, "lowLimitPa":  -40.0 },
    { "deviceId": "S2-3", "setpointPa":  -10.0, "highLimitPa":   20.0, "lowLimitPa":  -40.0 },
    { "deviceId": "S2-4", "setpointPa":  -10.0, "highLimitPa":   20.0, "lowLimitPa":  -40.0 },
    { "deviceId": "S2-5", "setpointPa":  -10.0, "highLimitPa":   20.0, "lowLimitPa":  -40.0 },

    { "deviceId": "S3-1", "setpointPa": -200.0, "highLimitPa": -100.0, "lowLimitPa": -300.0 },
    { "deviceId": "S3-2", "setpointPa": -200.0, "highLimitPa": -100.0, "lowLimitPa": -300.0 },
    { "deviceId": "S3-3", "setpointPa": -200.0, "highLimitPa": -100.0, "lowLimitPa": -300.0 },
    { "deviceId": "S3-4", "setpointPa": -200.0, "highLimitPa": -100.0, "lowLimitPa": -300.0 },
    { "deviceId": "S3-5", "setpointPa": -200.0, "highLimitPa": -100.0, "lowLimitPa": -300.0 }
  ]
}
```

**모드별 공통값에서 센서별 개별값으로 바뀝니다.** ESAM 운용 PDF 의 Config 화면도 원래 센서별 테이블이었으므로, 우리가 단순화한 것을 되돌리는 것입니다. 배기 저항이 통로마다 다르면 통로별로 다른 설정값이 필요한 게 물리적으로 자연스럽습니다.

---

## 4. `control.json` — 모드별 Time 은 여기 남는다

`Time`(대역 이탈 확정 시간)은 **모드별 공통**으로 확정했습니다(2026-08-10). ECID 대상이 아니므로 `recipe.json` 이 아니라 제어 설정에 둡니다.

```jsonc
{
  "activeMode": "Sensor2",     // 어느 센서 그룹을 제어 기준으로 쓸지

  // 이탈 확정 시간. 센서별이 아니라 모드별 공통이다.
  "modeTimes": {
    "Sensor1":  60,
    "Sensor2": 120,
    "Sensor3": 300
  },

  "controlPeriodMs": 200,
  "filterWindowSize": 5,
  "valve": { "stepPulse": 100, "dwellMs": 1000, "moveTimeoutMs": 10000, "homingTimeoutMs": 30000 },
  "fan":   { "stepRpm": 100, "minRpm": 200, "maxRpm": 4000, "dwellMs": 1000, "offBelowRpm": 200 }
}
```

### `ModeSetting` 은 런타임 조합 결과가 된다

지금 `ModeSetting { SetpointPa, BandPa, TimeSec }` 은 파일에서 직접 역직렬화됩니다. 앞으로는 **두 파일을 합쳐 만드는 값 객체**가 됩니다.

```
recipe.sensors[sensorId]  →  SetpointPa, HighLimitPa, LowLimitPa
control.modeTimes[mode]   →  TimeMs
                             ↓
        RecipeService.GetSetting(sensorId, mode) → ModeSetting
```

`ChainControlContext` 가 이미 `ModeSetting` 을 인자로 받으므로 **시그니처 변경이 없습니다.** 체인별로 다른 값을 넘기면 됩니다.

`BandPa` 는 사라집니다. 지금은 `LowLimit = Setpoint - Band`, `HighLimit = Setpoint + Band` 로 대칭 강제인데, recipe 가 High/Low 를 독립적으로 주므로 비대칭이 가능해집니다. 필요하면 표시용 계산 속성으로만 남깁니다.

---

## 5. `alarms.json` — 숫자를 갖지 않는다

`Alarm LIST` 시트가 `High Limit` / `Low Limit` 를 분리했으니 조건 타입을 둘로 나눕니다.

```jsonc
{
  "code": "P40",
  "name": "EFEM Center 압력 상한 초과",
  "severity": "Alarm",
  "source": "device:S1-1.pressurePa",
  "condition": "AboveHighLimit",     // recipe 의 highLimitPa 를 끌어온다
  "debounceMs": 60000,
  "resetPolicy": "Auto"
}
```

`threshold` 가 없습니다. `AboveHighLimit` / `BelowLowLimit` 가 `source` 의 디바이스 ID 로 `recipe.json` 을 조회합니다.

이러면 압력 알람 26종(13대 × 2)이 임계값 중복 없이 나오고, `Alarm LIST` 66종 구조와 개수가 맞습니다.

### ECID 대상이 아닌 계측은 직접 `threshold` 를 쓴다

`ECID` 시트는 **압력센서만** 다룹니다. 온습도·풍속·파티클·BLDC 온도는 ECID 항목이 없으므로 상위가 관리하지 않습니다. 이들은 `alarms.json` 에 직접 `threshold` 를 씁니다.

| 대상 | 임계값 출처 | 조건 |
|---|---|---|
| 압력센서 13대 | `recipe.json` | `AboveHighLimit` / `BelowLowLimit` |
| 온습도·풍속·파티클·BLDC 온도 | `alarms.json` 의 `threshold` | `GreaterThan` / `LessThan` |
| 통신·비트 입력 | 임계값 없음 | `CommFail` / `CommFailOrAlarmCode` / `BitSet` |

> **로더가 섞임을 경고합니다.** `recipe.json` 에 있는 디바이스인데 `alarms.json` 에서 직접 `threshold` 를 쓰면 값이 두 곳에 생깁니다. 오류로 막지는 않되(예외적 필요가 있을 수 있음) 경고로 드러냅니다.

---

## 6. `gem-map.json` — 번호와 경로의 매핑

```jsonc
{
  "schemaVersion": "1.0",

  // 장비 상수. 상위가 읽고 쓴다. recipe.json 의 항목을 가리킨다.
  "ecid": [
    { "id": 1, "name": "EFEM Center Pressure Sensor",            "path": "recipe:S1-1.setpointPa"  },
    { "id": 2, "name": "EFEM Center Pressure Sensor High Limit", "path": "recipe:S1-1.highLimitPa" },
    { "id": 3, "name": "EFEM Center Pressure Sensor Low Limit",  "path": "recipe:S1-1.lowLimitPa"  }
    // … 39항목
  ],

  // 상태 변수. 상위가 읽는다. 스냅샷 경로를 그대로 쓴다.
  "svid": [
    { "id":  1, "name": "EFEM Center Pressure Sensor", "path": "device:S1-1.pressurePa"   },
    { "id": 14, "name": "EFEM Temperature",            "path": "aux:temperatureEfem"      },
    { "id": 19, "name": "EFEM Center Throttle Valve Position", "path": "device:V-1.positionPercent" },
    { "id": 39, "name": "Emergency Stop",              "path": "plc:di.emo"               }
    // … 43항목
  ]
}
```

`device:` · `aux:` · `plc:` 는 `SnapshotValueResolver` 가 이미 지원하는 경로 규약입니다. **SVID 는 새 해석기가 필요 없습니다.** `recipe:` 스키마만 추가하면 ECID 읽기·쓰기가 됩니다.

---

## 7. 로드 시 검증 (Fail-fast)

파일이 넷으로 갈리면 **참조가 끊어지는 사고**가 새 위험입니다. 로더에서 막습니다.

| # | 검사 | 막지 않으면 | 처리 |
|---|---|---|---|
| 1 | `recipe` 의 deviceId 가 `device-map` 에 있는가 | 존재하지 않는 센서의 설정값이 조용히 무시됨 | **오류** |
| 2 | `recipe` 값이 `device-map` 의 `rangeMin`~`rangeMax` 안인가 | 센서 레인지를 넘는 목표를 영원히 추종 | **오류** |
| 3 | `lowLimitPa < setpointPa < highLimitPa` 인가 | 상하한이 뒤집혀 알람이 영구 발생 또는 영구 침묵 | **오류** |
| 4 | `alarms` 의 `AboveHighLimit`/`BelowLowLimit` 대상이 `recipe` 에 있는가 | **알람이 등록됐는데 영원히 울리지 않음** | **오류** |
| 5 | `recipe` 대상인데 `alarms` 가 직접 `threshold` 를 쓰는가 | 값이 두 곳에 생겨 어느 쪽이 적용되는지 알 수 없음 | 경고 |
| 6 | `gem-map` 의 ECID/SVID 번호가 중복되는가 | 상위가 엉뚱한 값을 받음 | **오류** |
| 7 | `gem-map` 의 경로가 실제로 해석되는가 | 상위 보고값이 영구히 0 | **오류** |
| 8 | ECID 가 `recipe` 항목을 빠짐없이 덮는가 | 상위에서 바꿀 수 없는 파라미터가 생김 | 경고 |
| 9 | 제어 기준 센서(`activeMode`)가 `recipe` 에 있는가 | 자동 운전 진입 후 전 체인 Skipped | **오류** |

**4번이 가장 위험합니다.** 조건이 성립하지 않으면 조용히 false 만 반환하므로, 알람 목록에는 보이는데 절대 울리지 않는 상태가 됩니다. `OutOfBand + referenceMode` 누락에서 이미 겪은 패턴입니다.

---

## 8. 구현 순서

```
1. recipe.json 스키마 + 로더 + 검증 1~3      ✅ 2026-08-10 (커밋 54b34a5)
2. ControlConfig 를 센서별 설정값으로 전환    ✅ 2026-08-10
3. alarms.json 66종 재작성 + 검증 4~5      ✅ 2026-08-11
4. gem-map.json + recipe: 경로 해석 + 검증 6~8  S7.5 GEM 화면과 함께
```

**1~3 단계로 만든 `recipe.json` 은 S7 에서 화면 편집에 도달했습니다.** 그전까지는 파일을 편집기로
직접 여는 수밖에 없었습니다. 저장 경로는 `ToJson → LoadFromJson → 파일 쓰기 → 런타임 통째 교체` 로,
**로드와 같은 검증을 거칩니다.** 화면에서 따로 검사하면 규칙이 두 곳에 생기고, 한쪽만 바뀌었을 때
화면은 통과시키는데 다음 기동에서 로드가 실패해 장비가 뜨지 않습니다.

`alarms.json` 은 아직 편집 화면이 없습니다. 임계값과 확정 시간을 고치려면 파일을 직접 열어야 합니다.

### 1·2 단계 구현 노트

**`ModeSetting` 은 파일에서 역직렬화되지 않습니다.** `SensorSetting.ToModeSetting(timeSec)` 이 recipe 의 센서별 값과 control 의 모드별 시간을 합쳐 만듭니다. `ChainControlContext` 가 이미 `ModeSetting` 을 받으므로 시그니처 변경이 없었습니다.

**비대칭 대역을 비파괴적으로 추가했습니다.** 4인자 생성자 `(setpoint, low, high, time)` 를 더하고 기존 3인자 `(setpoint, band, time)` 는 그대로 뒀습니다. `BandPa` 는 표시용으로 남아 비대칭일 때 넓은 쪽 편차를 반환합니다.

**`ControlConfig.GetSetting(sensorId, mode)` 가 세 갈래로 나뉩니다.**

| 상황 | 반환 | 이유 |
|---|---|---|
| 레시피 없음 | 모드별 공통값 | 레시피 도입 전 거동. 제어는 성립하므로 막지 않는다 |
| 레시피에 센서 있음 | 센서별 값 + 모드별 시간 | 정상 경로 |
| 레시피에 센서 없음 | **`null`** | 공통값으로 메우면 그 체인만 조용히 다른 기준으로 제어된다 |

세 번째는 `RequestAuto()` 에서 미리 막습니다. 그대로 자동 운전에 들어가면 **일부 통로만 제어되면서 화면은 정상으로 보입니다.**

**레시피 부재는 차단 경고가 아닙니다.** 모드별 공통값으로 제어가 성립하기 때문입니다. 다만 `RCP-01` 참고 경고로 드러냅니다 — 조용히 넘어가면 통로별로 값을 넣었다고 믿은 채 공통값으로 운전하게 됩니다. 반면 **파일이 있는데 검증에 실패한 경우는 `RCP-03` 차단 경고**입니다. 레시피를 쓰겠다고 선언했는데 참조가 끊어진 상태이므로 구성 오류입니다.

**`DriverNames` 를 Communication 으로 옮겼습니다.** 로더가 `driver` 이름을 알아야 하는데 `PointKeys` 는 Services 에 있어 참조할 수 없었습니다. 문자열을 다시 적으면 같은 계약이 두 곳에 생겨 한쪽만 바뀌었을 때 컴파일러가 잡아주지 못합니다. 설정 파일을 읽는 계층이 Communication 이므로 계약도 그쪽에 둡니다.

1·2 가 3 의 전제입니다. 2 를 건너뛰고 3 부터 하면 `AboveHighLimit` 이 참조할 곳이 없습니다.

4 는 GEM 화면(S7)과 묶습니다. 매핑만 만들고 쓰는 곳이 없으면 검증할 방법이 없습니다.

---

## 9. 남은 확인 항목

| 항목 | 내용 |
|---|---|
| PLC 미러링 차압값 | `0x82`/`0x84`/`0x86` (D130/132/134), 오프셋 `0x8C`/`0x8E`/`0x90` (D140/142/144). **주소 간격이 2워드**라 32비트일 가능성이 있습니다. Int16 으로 읽으면 값이 완전히 틀어집니다. 워드 수 확인 필요 |
| 중복 계측 | 위 3개는 DP-01~03(Sensor 1)과 같은 물리 센서입니다. 이미 슬레이브 1~3 에서 직접 읽고 있어, PLC 경유를 추가하면 같은 값을 두 번 읽고 CH1 Fast 예산이 늘어납니다. **오프셋만 읽는 편이 낫습니다** |
| `Driver` 시트 `0x4007` | `0x4006` 으로 수정 요청이 반영되지 않았습니다 |
| `SVID` 시트 24~28 명칭 | PSM 은 **Bottom**(Blower Pressure Sensor)으로 확정. 시트의 "Front" 표기가 오류이므로 수정 요청 필요 |

---

## 9. 3단계 구현 노트 (2026-08-11)

### 코드 체계

`Alarm LIST` 시트의 No. 를 그대로 코드로 씁니다.

| 범위 | 출처 | 상위(GEM) 보고 |
|---|---|---|
| `AL-01` ~ `AL-66` | 고객 사양 (Alarm LIST 시트) | 예 |
| `DG-01` ~ | 사내 진단 알람 | 아니오 |

시트 번호가 곧 코드이므로 변환표가 필요 없고, ALID 매핑도 같은 번호를 씁니다.

**`DG` 범위를 나눈 이유**는 66종이 "고장이 났다" 를 알리는 데 비해 진단 알람은 **"고장 나기 전"** 을 알리기 때문입니다. 밸브가 다 열렸는데도 목표에 못 미치는 상태(`DG-02`)를 알아야 압력 이탈(`AL-46`~`AL-55`)로 번지기 전에 손을 쓸 수 있습니다.

### 임계값이 규칙에 없다

압력 26종(`AL-40`~`AL-65`)에는 `threshold` 필드가 없습니다. `AboveHighLimit` / `BelowLowLimit` 가 `source` 의 디바이스 ID 로 `recipe.json` 을 조회합니다.

레시피가 없거나 해당 센서가 없으면 **판정하지 않습니다.** 폴백 임계값을 쓰면 작업자가 설정한 값과 다른 기준으로 알람이 울립니다. 조용히 틀리는 쪽이 더 위험합니다. 참조가 끊어진 구성은 로드 단계에서 오류로 막습니다.

### 검증 4·5 와 경로 검증

| 검사 | 처리 | 이유 |
|---|---|---|
| 4. 대상이 레시피에 있는가 | **오류** | 없으면 알람이 등록됐는데 영원히 울리지 않는다 |
| 5. 레시피 대상인데 threshold 직접 사용 | 경고 | 값이 두 곳에 생긴다 |
| 신설. 경로를 해석할 수 있는가 | **오류** | 오타 하나로 안전 통보가 사라진다 |

경로 검증을 추가한 이유가 중요합니다. `TryGetNumeric` 은 **"값이 없다"(NoData)와 "경로를 모른다"(오타)를 같은 false 로 반환**합니다. 실제로 이번 작업에서 `plc:temp.fan.0` · `plc:di.fanStop.0` 처럼 존재하지 않는 경로를 열 곳 넘게 썼고, 형식 검증이 없었다면 그 알람 10종이 조용히 죽은 채 배포되었을 것입니다.

`SnapshotValueResolver.IsSupportedPath` 가 스키마와 멤버 이름을 함께 봅니다. 스키마만 보면 `plc:di.emoo` 같은 것이 통과합니다.

### `independentThreshold`

검증 5 에는 정당한 예외가 있습니다. `DG-04` 는 배기 음압이 −100 Pa 를 넘어서면 경고하는데, 이 값은 운전 설정과 무관한 고정 기준입니다. 인터록(0 Pa) 도달 전에 알리는 것이 목적이므로 레시피 하한을 따라 움직이면 안 됩니다.

플래그로 **의도를 데이터에 적어 둡니다.** 항상 뜨는 경고는 읽히지 않고, 그러면 진짜 중복도 함께 묻힙니다.

### 비활성 7종

| 코드 | 사유 |
|---|---|
| `AL-01` | 상위(FDC) 통신 — SECS/GEM 모듈 미구현 (S7) |
| `AL-03·06·09·12·15` | 밸브 홈센서 — 알람코드 `0x2203` 비트 정의 미확정 |
| `AL-66` | 제어 PC 온도 — 취득 경로 없음 |

**소스가 없거나 사양이 미확정인 것뿐입니다.** 사유를 `messageKo` 에 남겨 "동작하는 줄 알았는데 아니었다" 를 막습니다.

### 함께 드러난 모델 공백

`Alarm LIST` 는 제어함 냉각팬 상부·하부를 별개 알람(`AL-38`·`AL-39`)으로 요구하고 PLC 도 비트를 둘로 읽는데, `PlcDigitalState` 가 하나로 합쳐서만 노출하고 있었습니다. 합친 값으로 두 알람을 만들면 항상 함께 울려 **어느 팬이 멈췄는지 알 수 없습니다.** 제어함을 열어 봐야 합니다. 개별 비트를 노출하도록 고쳤습니다.
