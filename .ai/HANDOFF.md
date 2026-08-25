# ESAM 개발 인수 문서 (2026-08-12 기준)

> 새 대화에서 이어서 진행하기 위한 현재 상태 요약입니다.
> **먼저 `.ai/CLAUDE.md`(작업 규칙)와 `docs/DESIGN.md`(설계서)를 읽으십시오.**
> 이 문서는 그 둘이 담지 않는 **진행 상황과 판단 근거**만 적습니다.

---

## 1. 프로젝트

**ESAM (EFEM Smart Airflow and Pressure Management System)**
반도체 EFEM 내부의 **수 Pa 단위 미세 차압 제어 및 기류 안정화** HMI.
클린룸 입자 유입 방지와 직결됩니다.

| | |
|---|---|
| 경로 | `C:\PTNC_lib\EFEM_Monitor` |
| 스택 | .NET Framework 4.7.2 / C# 7.3 / WPF + MVVM / Newtonsoft.Json 13.0.3 / xUnit 2.7 |
| 통신 | Modbus RTU (RS-485 반이중), CRC-16/MODBUS, FC03/04/06/16 |
| 하드웨어 | 차압센서 13, 스로틀밸브 5, 송풍팬 5, PLC 1 (+ FFU·MFC·풍속·온습도·파티클 확장) |
| 채널 | CH1(COM3, 19200) / CH2(COM4, 38400) — 2포트 |
| 현재 커밋 | `a113963` (C5) + S7.5-c + D23 |
| 테스트 | **564건 전량 통과** |

### 프로젝트 구조

```
src/
  Esam.Domain         상태머신·밴드제어·알람·인터록·설정 모델 (하드웨어 무의존)
  Esam.Communication  Modbus 코덱·전송·워커·설정 로더·가상 플랜트
  Esam.Services       SnapshotBuilder·DataStore·InterlockGuard·ControlEngine
                      ·AlarmService·EsamRuntime(조립 루트)·RuntimeDiagnostics
  Esam.Hmi            WPF 화면 (클래식 csproj — net472 WPF 는 SDK 스타일 불가)
  Esam.Tests          SDK 스타일 net472. 위 4개 전부 참조
config/
  device-map.json  하드웨어 (포트·타입·인스턴스·레지스터)
  recipe.json      ECID 마스터 — 숫자 압력 임계값이 사는 유일한 곳 (센서 13대)
  alarms.json      알람 구성 74룰 (AL-01~66 + DG-01~08). 압력 26룰은 threshold 없음
  control.json     제어 파라미터 (모드별 확정시간·밸브·팬·안전입력 유예) + 통로 5조. 없어도 기동
docs/
  DESIGN.md        설계서 (§10 단계표, §11 결함 이력, §12 다음 단계)
  COMM_MAP.md      레지스터 맵 (§7.1 압력 스케일 주의)
  CONFIG_MODEL.md  설정 모델 분리 근거 (§8 구현 순서)
  COMMISSIONING.md 현장 절차
```

---

## 2. 완료 단계

| 단계 | 내용 |
|---|---|
| **S1~S3** ✅ | Domain 모델 → Modbus 전송계층 + 가상 플랜트 → 선언적 드라이버 + 워커 + JSON 로더 |
| **S4·S4.5** ✅ | `Esam.Services` 조립 + 종단 통합테스트 / HMI Dashboard 프로토타입 |
| **S5** ✅ | 결함 11건(D1~D11) 전량 해소 |
| **C1·C2** ✅ | `recipe.json` 신설, 제어 설정값을 모드별 공통 → **센서별 개별** 전환 |
| **S5.5** ✅ | 빌드 복구 + 결함 3건(D12·D13·D14) |
| **C3** ✅ | 알람 66종 재작성 — 임계값을 `recipe.json` 에서 가져옴 |
| **S7** ✅ | **HMI 실연결 (Operate + Config 2화면)** + 결함 4건(D15~D18) |
| **S7.5-a** ✅ | **I/O Status 화면** + `DeviceHealth` 신설 + 결함 2건(D19·D20) |
| **S7.5-b** ✅ | **알람 설정 화면** + 주석 보존 저장 + 발생 상태 승계 교체 |
| **C5** ✅ | **`control.json` 신설** — 제어 파라미터의 배포 파일화 + 결함 1건(D22) |
| **S7.5-c** ✅ | **Maintenance 화면** — 영점 교정 · 수동 조작 · 원점 복귀 |
| **S8-a** ✅ | **`Esam.Persistence` 신설** — SQLite 스키마 · 일별 파일 롤링 · 배치 적재 · 리텐션 |
| **S8-a 후속** ✅ | **`DataLogger`** — 스냅샷·알람을 실제 흐름에서 적재. `logging` 절 신설, HMI 배선 |

**S7 세부**: a·b 난수 시뮬레이터 제거 후 `DataStore` 폴링 / c 구성 경고 배너 + `ResetRuntimeFault` /
d 레시피 편집 / e 통신·통로 설정 / f ViewModel 테스트 36건 + 문서

**S7.5-a 세부**: 스냅샷에 디바이스별 통신 상태(`DeviceHealth`) 신설 / I/O 화면 4구역
(램프 12종 · PLC DI · 압력 원시값 · 포트 통계) / **D19 PLC 키 불일치** — 알람 10건이
울리지 않던 것 / **D20 대시보드 고정값** — 우측 패널과 포트 진단이 프로토타입 값이던 것 /
테스트 35건(건강 상태 15 · 계약 5 · I/O 화면 15)

---

## 3. 지금 화면에서 되는 것

- 대시보드가 **실제 스냅샷**을 끌어옴 (200ms 주기, pull 방식).
  우측 보조 계측·포트 진단도 실제 값이다(D20 이전에는 고정 문자열이었다)
- AUTO 시작/정지, 거부 사유 표시
- 하단 배너에 **구성 경고**(SIM-01·SAFE-02·ALM-02·COM-01) — 확인하면 접히지만 **사라지지 않음**
- `Recipe` 화면에서 센서 13대의 설정값·하한·상한 편집 → 저장은 **로드와 같은 검증** 경유
- `Settings` 화면에서 전송 방식(시뮬레이션↔Serial)·COM 포트·보레이트·통로 활성화
- **쓰기 잠금 관문** — 기본값 거부, "정비 모드 진입" 으로 해제 (S9 에서 계정으로 교체 예정)

- **I/O Status 화면** — 상태 램프 12종(6상태), PLC DI 극성 대조, 압력 원시 레지스터,
  포트별 사이클타임·성공률. **읽기 전용이라 쓰기 잠금 대상이 아니다**
- **Maintenance 화면** — 센서 13대 영점 교정(제안·적용·되돌리기), 밸브 개도·팬 회전수
  수동 지령, 원점 복귀. **관문 3겹**(쓰기 잠금·단계·인터록)을 지나야 하고,
  화면을 떠나면 수동 조작이 자동으로 정리된다

- **알람 설정 화면** — 74룰의 임계값·확정 시간·심각도·해제정책·활성 여부 편집.
  주석을 보존해 저장하고, 운전 중에도 즉시 반영된다(발생 중인 알람은 살아남는다)

### 아직 없는 것

- **Data Log** — SQLite 적재·트렌드 조회·CSV Export. 고속 로거도 여기 묶인다(S8)
- **제어 파라미터 실시간 조정 화면** — `control.json` 이 생겼으므로 이제 가능하다
- Maintenance (영점 교정·수동 Jog·파라미터 실시간 조정·고속 로거)
- Data Log (SQLite 적재 + 트렌드 조회 + CSV Export)
- GEM/SECS 연동 (`gem-map.json` 미작성)

---

## 4. 설계에서 반복되는 판단 기준

새 작업에서도 이 기준을 유지하십시오. 지금까지의 결함 수정 대부분이 여기서 나왔습니다.

1. **"조용히 틀린 것" 보다 "요란하게 틀린 것" 을 고른다.**
   해석 불가한 알람 경로 → 로드 오류 / 열 수 없는 포트 → 차단 구성 경고 /
   품질 나쁜 센서값 → 트렌드 선을 끊는다(평탄화하지 않는다)
2. **검증 규칙은 한 곳에만 둔다.** 화면 저장도 로더와 같은 경로를 지난다.
   두 곳에 두면 한쪽만 바뀌었을 때 화면은 통과시키고 다음 기동에서 로드가 실패한다.
3. **되돌릴 것을 만들지 않는다.** 살아 있는 객체를 고친 뒤 되돌리는 코드보다
   사본에 반영해 검증하는 편이 낫다 (D18).
4. **설정 적용은 통째로 교체한다.** 제자리 수정은 제어 스레드가 반쯤 갱신된 목록을 읽게 한다.
5. **숫자는 항상 `CultureInfo.InvariantCulture`.** 지역 설정에 따라 `6.5` 가 `65` 가 된다.
6. **안전 판정은 레벨 트리거, 결함 *보고* 는 엣지 트리거.** 의도적으로 다르다.
7. 사용자 입력은 **문자열로 보관**하고 저장 시점에 한 번만 파싱한다
   (`double` 바인딩은 `-` / `1.` 중간 상태에서 입력을 되돌린다).

---

## 5. 결함 이력 (총 23건, 전량 해소)

전체 내용은 `docs/DESIGN.md` §11.0 / §11.1 / §11.3 / §11.4 / §11.5 / §11.6. 요점만:

| 그룹 | 결함 | 발견 경로 |
|---|---|---|
| D1~D11 | EMO 가 SafeStop 에 도달하지 않음, 인터록 트리거 소실, 원점 복귀 미확인, 상태머신 경합 등 | S4 정적 리뷰 |
| D12~D14 | 경고 목록 경합, **지령 실패 되먹임(사이클이 끝나지 않음)**, 장애 SafeStop 자동 해제 | 빌드 복구 |
| D15~D17 | **시뮬레이션에 PLC 없음**(안전 입력 경로 미검증), 플랜트 시계 정지, **없는 COM 포트가 프로그램을 죽임** | 화면을 실제로 띄워서 |
| D18 | 설정 검증 실패 시 "되돌린다" 는 주석이 거짓 | ViewModel 테스트 작성 중 |
| D19 | **PLC 측정점 키가 코드(`di.fanStop0`)와 설정(`di.fanStop.0`)에서 다름.** 점 하나 차이로 **송풍팬 정지·과열 알람 10건이 영원히 울리지 않음** | I/O 화면에 올릴 값을 찾다가 |
| D20 | **대시보드 우측 패널·포트 진단이 전부 고정 문자열.** 갱신 코드가 아예 없었음 | 같음 |
| D21 | **`device-map.json`·`recipe.json` 저장이 주석을 전부 지움.** 압력 스케일이 잠정값이라는 근거가 COM 포트 한 번 바꾸면 사라짐 | S7.5-c 준비 중 |
| D22 | **통로 활성화가 메모리에만 남음.** 꺼 둔 통로가 재시작하면 전부 켜짐 | C5 설계 중 |
| D23 | **기동할 때마다 Fault 로 갇힘.** 포트 경합으로 IL-04 오탐 → SafeStop → 해제 → Fault. 그런데 `ResetRequested` 를 쏘는 코드가 프로덕션에 없어 **재시작 말고는 복구 수단이 없었음** | 정비 화면이 단계를 사유로 찍으면서 |

> **D19 도 441건을 전부 통과한 상태에서 나왔습니다.** 통합 테스트가 자체 맵을
> **코드와 같은 방식으로** 만들었기 때문입니다. 양쪽이 같은 오타를 공유하면
> 어긋난 사실이 드러나지 않습니다. 그래서 대책은 수정이 아니라 **배포 파일과의 대조**입니다
> (`PlcPointContractTests`).
>
> **D15~D17 은 통합 테스트 385건을 전부 통과한 상태에서 나왔습니다.**
> 테스트는 자체 `CreateMap()` 을 쓰고, 시간을 직접 통제하고, 단일 스레드로 돕니다.
> 셋 다 그 전제가 실제 실행과 다른 지점에서 터졌습니다.
> **시뮬레이션이 실제와 다른 부분은 그 자체로 검증 공백입니다.**

---

## 6. 함정 목록 (같은 실수를 반복했던 것들)

| 함정 | 증상 |
|---|---|
| `Directory.Build.props` 가 `TreatWarningsAsErrors=true`, 예외는 `CS1591` 뿐 | **CS1570/1571/1572/1573/1574 가 전부 오류.** 문서 주석 하나로 빌드가 죽고 "메타데이터를 찾을 수 없음" 으로만 표시된다 |
| 새 메서드를 기존 문서 주석 **위에** 삽입 | 두 주석 블록이 합쳐져 `<param>` 중복(CS1571). 3회 반복했다 |
| XAML 에서 `Style` 을 속성과 요소로 동시 지정 | "속성이 이미 설정되었으며" — `BasedOn` 을 쓸 것. 2회 반복했다 |
| xUnit `[Fact(Timeout=…)]` | **비동기 테스트 전용.** 동기 테스트는 즉시 실패한다. 벽시계 예산을 헬퍼 안에 두는 방식으로 대체했다 |
| 클래식 csproj + SDK 스타일 `ProjectReference` | 전이 `PackageReference` 가 흐르지 않는다. `RestoreProjectStyle=PackageReference` + 명시 참조 필요 |
| `Esam.Tests` → `Esam.Hmi` 참조 | net472 WPF 는 `<UseWPF>` 불가. `PresentationCore`·`PresentationFramework`·`WindowsBase`·`System.Xaml` 명시. `System.Xml` 등 기본 어셈블리는 적으면 중복 |
| 실제 워커 스레드를 띄우는 테스트 | `Dispose` 기본 `Stop(5000)` 이 파킹을 기다린다. 성립 불가한 조건이면 `Stop(0)` 으로 끊을 것 |
| 예외 절 순서 | `ObjectDisposedException` 은 **`InvalidOperationException` 의 하위형**이다. 상위형을 먼저 잡으면 뒤 절이 도달 불가가 되어 컴파일되지 않는다(CS0160). `BlockingCollection` 을 다룰 때 둘 다 잡게 되므로 특히 걸린다 |
| 외부 라이브러리 반환형을 추측 | `SQLiteParameterCollection.Add` 는 파라미터가 아니라 **삽입 위치(int)** 를 돌려준다. 다른 ADO.NET 구현과 다르다. 시그니처를 확인할 수 없으면 반환값을 쓰지 말고 객체를 직접 잡아 둘 것 (CS0029) |
| 검증 통과 vs 빌드 통과 | 샌드박스에 .NET SDK·네트워크가 없어 **정적 검사만** 가능하다 (괄호 균형, 문서 주석 XML 파싱, 제네릭 불일치, XAML 유효성, JSON 키↔모델 대조) |

---

## 7. 사용자 지시 (계속 적용)

- **`.ai/CLAUDE.md`**: 작업 시 Plan 설명 / Plan 피드백받기 / 타입 힌트 필수 /
  코드 작성 시 설명 주석 / **허락 없이 코드 작성 금지** / **허락 없이 커밋 금지**
- **`docs` 폴더에서 `.md` 이외는 커밋하지 않는다.**
  (CONFIDENTIAL DSE xlsx, ESAM pdf, IO List xlsx, pptx — 절대 커밋 금지)
- `Esam.Tests` 에서 `Assert.True(false, message)` 로 실패시키지 말 것.
  **`Assert.Fail(message)`** 를 쓸 것
- 응답은 **가능한 한 간결하고 직접적으로**. 불필요한 설명·장황함 배제
- 빌드·테스트·실행은 **사용자가 Windows 에서 수행**한다. push 도 사용자가 한다
  (샌드박스에 자격증명 없음)

### 샌드박스 운영 메모

- git 커밋은 가능하지만 `index.lock` 이 남는 일이 있다.
  `mv .git/index.lock .git/index.lock.stale.$$` 로 치우고 진행할 것
- 세션 종료 후 Windows 에서 `Remove-Item .git\*.stale.* -Force` 정리 필요
- `README.md` 가 수정 상태로 남아 있다 (`git checkout -- README.md` 로 정리)

---

## 8. 다음 단계 — S8 (데이터 로깅)

**화면 4종이 끝났습니다.** Operate · Config(Recipe/Settings) · I/O Status ·
Alarm · Maintenance. 남은 큰 덩어리는 **S8 — SQLite 적재 + 트렌드 조회 + CSV Export** 입니다.
고속 로거도 여기 묶입니다.

**시작 전에 정해야 할 것**: Data Log Viewer 방식이 협의 항목 #8 로 남아 있습니다(CTO).
`DESIGN.md` §6 에 스키마 초안이 있습니다.

곁가지로 남은 것:

- **제어 파라미터 실시간 조정 화면** — `control.json` 이 생겼으므로 이제 가능
- **C4 GEM 연동** — 상위 호스트 명세 확정 대기
- **S9 권한·감사로그** — 지금의 "정비 모드 진입" 을 계정으로 교체
- **대시보드 헤더의 죽은 버튼 5건** — `DashboardView` 헤더의 `Dashboard`·`Maintenance`·
  `Config`·`I / O`·`Data Log` RadioButton 은 **명령이 없고** `IsChecked="True"` 가 박혀 있다.
  실제 화면 전환은 좌측 메뉴가 한다. D20(고정값이 진실을 말하지 않는 것)과 같은 종류다.
  좌측 메뉴를 접으면 이 줄만 남아 더 눈에 띈다.
  → **추후 관리 포인트로 남김** (사용자 결정, 2026-08-19)


**설정 파일 저장은 이제 세 파일 모두 주석을 보존합니다.** 공용 스캐너는
`JsonTextScanner`·`JsonTextPatch` 이고, 파일별 편집기(`AlarmDocumentEditor`,
`DeviceMapDocumentEditor`, `RecipeDocumentEditor`)가 "어느 값을 바꿀지" 만 압니다.
**영점 교정은 `DeviceMapDocumentEditor` 를 그대로 씁니다.**



**2026-08-12.** S7.5 를 셋으로 나눠 **a(I/O Status)와 b(알람 설정)를 끝냈습니다.**
남은 것은 **c — Maintenance · Data Log** 입니다.

아래는 b 에서 확정해 지킨 결정들입니다. 같은 판단이 c 에도 적용됩니다.

### 확정된 설계 결정 3건

| | 결정 | 이유 |
|---|---|---|
| 저장 | `alarms.json` 을 Newtonsoft **`JToken` 부분 수정**으로 쓴다 (`CommentHandling.Load`) | 파일에 비활성 규칙의 사유를 적은 한글 주석이 200여 줄 있다. 전체 재직렬화하면 사라진다 |
| 적용 | **`AlarmService.ReplaceRules()`** 신설. 규칙·상태 사전을 통째로 교체하되 같은 코드의 활성 `AlarmState` 는 승계 | 런타임 전체 재조립은 워커 정지·포트 재개방·밸브 원점복귀를 유발해 **운전 중 임계값 조정에 쓸 수 없다.** 승계하지 않으면 Manual 정책 알람이 저장 한 번에 조용히 사라진다 |
| 범위 | `threshold`·`debounceMs`·`enabled`·`severity`·`resetPolicy` 만 편집 | `code`·`source`·`condition` 은 구조다. 화면에서 바꾸면 경로 검증을 통과하지 못하는 조합이 나온다. pptx SCREEN 08 의 런타임 등록·편집은 S9 |

압력 26룰은 임계값이 `recipe.json` 소관이므로 **입력을 잠근다.** 숫자가 두 곳에 생기는 것을
화면 차원에서 막는다.

### 그 뒤

- **S7.5-c** — Maintenance(영점 교정·수동 Jog·파라미터 조정·고속 로거), Data Log.
  **Maintenance 는 쓰기가 많은 첫 화면**이다. 지금까지의 쓰기는 설정 파일이었고,
  거기는 액추에이터를 직접 움직인다. 쓰기 잠금 관문만으로 충분한지 다시 볼 것
- **C4** — `gem-map.json` + `recipe:` 경로 스킴. 상대(상위 호스트 명세)가 준비되어야 의미가 있다
- **S8** — SQLite 로깅 + CSV Export
- **S6** — 실장비 검증. **판정 수단은 갖춰졌다**(I/O 화면)

## 9. 외부 확정 대기 항목 (커미셔닝 전 필수)

| 항목 | 확정되지 않으면 |
|---|---|
| 압력 스케일 **0.1 Pa/LSB** 확인 | 모든 압력값이 10배 틀어진다. `COMM_MAP.md` §7.1 의 **3곳을 함께** 고쳐야 한다 |
| PLC 입력 비트 극성 | 안전 입력이 반대로 읽힌다. EMO 미조작 상태가 EMO 로 판정될 수 있다 |
| IO List `Driver` 시트 `0x4007` → `0x4006` | 밸브 지령이 엉뚱한 레지스터로 간다. **수정 요청 미반영** |
| `SVID` 시트 24~28 "Front" → "Bottom" | PSM 은 Bottom(Blower Pressure Sensor)으로 확정. 시트 표기 오류 |
| 블로워 보레이트 115200 → 38400 | CH2 통신이 성립하지 않는다 |
| PLC 미러링 차압값 워드 수 | 주소 간격이 2워드라 32비트 가능성. Int16 으로 읽으면 값이 완전히 틀어진다 |

### 미해결 협의 항목 (DESIGN.md §11)

- #2 Sensor 1 Band 불일치 (`±2 Pa` vs `5<x<7`) — CTO
- #4 1차 릴리스 제어 방식: 밴드제어 확정 vs PID 병행 — CTO
- #6 인터록 IL-01 범위: 해당 체인만 vs 전체 정지 — **안전**, CTO
- #8 Data Log Viewer 방식 — CTO
- Recipe 의 S4·ENV 탭 (센서 모드가 4개면 `SensorMode` 열거형 확장 필요)

---

## 10. 테스트 구성 (600건)

**세는 규칙**: `[Fact]` 는 1건, `[Theory]` 는 `[InlineData]` 개수만큼 센다.
종전 표는 메서드 수를 적어 두어 실제 실행 건수와 최대 23건까지 어긋나 있었다
(`DeviceMapTests` 40 → 실제 63). 아래는 실행 결과와 대조한 값이다.

| 파일 | 건수 | 대상 |
|---|---|---|
| `ServicesIntegrationTests.cs` | 73 | 종단 통합 — 폴링→스냅샷→제어→인터록. 시뮬레이션 전 경로 |
| `DeviceMapTests.cs` | 63 | 설정 로더·스케일링·검증 |
| `HmiViewModelTests.cs` | 38 | **ViewModel 판단 — 파싱·쓰기 잠금·저장 검증 경유·메뉴 토글** |
| `PortWorkerTests.cs` | 38 | 워커 사이클·지령 큐·재개방 |
| `ModbusCodecTests.cs` | 31 | CRC·프레임 |
| `InterlockEvaluatorTests.cs` | 29 | 인터록 판정 |
| `SystemStateMachineTests.cs` | 28 | 상태 전이·금지 전이 |
| `UnitConversionTests.cs` | 28 | 단위 변환·스케일 |
| `RecipeConfigTests.cs` | 22 | **배포 `recipe.json` 자체**를 검증 |
| `AlarmConfigTests.cs` | 20 | **배포 `alarms.json` 자체**를 검증 |
| `BandControlPolicyTests.cs` | 18 | 밴드 제어 판단 |
| `SimulatedTransportTests.cs` | 18 | 가상 전송계층 |
| `PlantModelTests.cs` | 17 | 가상 플랜트 응답 |
| `IoStatusViewModelTests.cs` | 16 | I/O 화면 판정 — 램프·DI 극성·압력 원시값 |
| `AlarmEvaluatorTests.cs` | 15 | 알람 판정 |
| `ConfigDocumentEditorTests.cs` | 15 | **device-map·recipe 주석 보존**(D21) + 공용 스캐너 |
| `ControlConfigTests.cs` | 16 | **배포 `control.json`** — 값이 코드 기본값과 일치하는가(C5) |
| `SnapshotHealthTests.cs` | 15 | 디바이스별 통신 상태 — 무응답/노후/미구성 구분 |
| `AlarmEditorViewModelTests.cs` | 14 | 알람 화면 판단 — 파싱·압력룰 잠금·치명 확인 절차 |
| `ManualControlTests.cs` | 14 | **수동 조작 관문** — 자동 운전 중·원점 복귀 전 거부, 영점 반영 |
| `SqliteLogStoreTests.cs` | 13 | **실제 파일로 적재** — 일별 롤링·자정 분기·WAL·메타·리텐션(S8-a) |
| `AlarmDocumentEditorTests.cs` | 12 | **주석 보존 저장** — 무편집 시 바이트 동일, 편집 시 한 줄만 변경 |
| `AlarmRuleSwapTests.cs` | 9 | **발생 상태 승계** — 저장이 Reset 을 대신하지 않을 것 |
| `ClosedLoopSimulationTests.cs` | 8 | 폐루프 수렴 |
| `TrendRowTests.cs` | 8 | 스냅샷 → 트렌드 행 — 열 순서, 품질 나쁜 값은 기록하지 않음(S8-a) |
| `DataLoggerTests.cs` | 12 | **기록이 제어를 멈추지 않는가** — 큐 넘침 시 무대기·버린 수 집계, 종료 시 잔여 배출 |
| `FaultRecoveryTests.cs` | 5 | **Fault 에서 나올 수 있는가**(D23) |
| `PlcPointContractTests.cs` | 5 | **배포 `device-map.json` 의 PLC 키 ↔ 코드 조회 키 대조**(D19) |

**알려진 검증 공백 1건.** `DataLogger` 의 **연속 실패 후 중단**(`LOG-01`) 경로는 자동
테스트가 없습니다. 적재 실패를 재현하려면 파일 잠금이나 디스크 만료를 흉내내야 하는데,
그런 테스트는 OS 사정에 따라 간헐적으로 실패합니다. **간헐적으로 실패하는 안전 테스트는
없는 것보다 나쁩니다** — 사람이 결과를 믿지 않게 됩니다. 생성 단계 실패(`LOG-04`)는
덮었고, 중단 판정은 코드 검토로만 확인했습니다.

**통합 테스트가 배포 설정(`config/*.json`)을 그대로 읽습니다.**
샘플을 따로 만들어 검증하면 배포본이 깨져도 테스트는 통과합니다.

> D19 는 이 원칙이 **측정점 키에는 적용되지 않고 있었다**는 사실을 드러냈습니다.
> 배포 설정을 읽는 것과, 배포 설정의 **문자열 계약을 코드와 대조**하는 것은 다릅니다.
