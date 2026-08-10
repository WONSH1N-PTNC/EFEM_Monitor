# 통신 맵 / Config 스키마 정규화 (v1.0)

> 출처: `docs/ESAM_IO List_260806.xlsx` **(주 출처, 2026-08-10 반영)**, `docs/CH2.6.JKBLD300V2 User Manual_v1.1.pdf`, `docs/26.PSMC1-V1.5-KO_20250221_W.pdf`, `docs/DSE_통신 자료_260710.xlsx`, `docs/ESAM 운용방법 설명자료_260309 V1.4.6.pdf`
> 작성일: 2026-07-31 / 최종 갱신: 2026-08-10
> `TBD` = 명세 미확보. 해당 항목은 드라이버 인터페이스로 격리하고 시뮬레이터로 개발 진행.

---

## 0. 2026-08-10 확정 — 이전 초안과 달라진 점

`IO List_260801.xlsx` 수령으로 통신 구성이 확정됐습니다. **§1 의 Bus A/B/C 3포트 기술은 폐기**되었고, 아래 §0 이 유효한 구성입니다.

### 0.1 채널 구성 — 2채널

| 채널 | 통신 | 장치 |
|---|---|---|
| **CH1** | RS-485 Modbus RTU, **19200** 8-N-1 | 차압센서 13(ID 1~13), 온습도 1(ID 15), 풍속 3(ID 20~22), PLC 1(ID 25), 압력센서 1대 5채널(ID 26) |
| **CH2** | RS-485 Modbus RTU, **38400** 8-N-1 | 스로틀밸브 5(ID 1~5), **송풍팬 5(ID 6~10)** |

**송풍팬이 전용 버스에 있지 않습니다.** 밸브와 CH2 를 공유하므로 슬레이브 ID 가 6~10 으로 밀렸습니다. 2026-07-31 자 "BUS_C 전용 포트" 기술은 무효입니다.

### 0.2 폴링 예산 재계산 (Open Issue #1 답변)

슬레이브 응답 지연 5 ms, 11 bit/char 가정. `EstimateTransactionMs` 와 동일한 모델입니다.

| 채널 | 티어 | 구성 | 트랜잭션 | 소요 |
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

**100 ms 목표는 Fast 티어만으로도 2.2배 초과합니다.** 개선 선택지:

| 방안 | CH1 Fast | 비고 |
|---|---|---|
| 현행 (19200, 14 tx) | 218 ms | — |
| CH1 을 38400 으로 상향 | 155 ms | 차압센서가 38400 을 지원하는지 확인 필요 |
| 차압센서를 2포트로 분할 | 125 ms | 포트 1개 추가(BOM 변경) |
| 위 둘을 병행 | 약 90 ms | 100 ms 달성 가능 |

19200 을 유지하는 한 어떤 소프트웨어 최적화로도 100 ms 는 불가능합니다. 트랜잭션당 최소 시간이 물리적으로 정해지기 때문입니다.

### 0.3 확정된 레지스터 맵

**차압센서 13대** — FC03, `0x4001` 1워드, Int16, 단위 Pa

| ID | Tag | 위치 | 논리 ID |
|---|---|---|---|
| 1~3 | DP-01~03 | EC, SL, SR | **Sensor 1** = S1-1~1-3 |
| 4~8 | DP-04~08 | EEXCF, EEXLF, EEXRF, EEXSLF, EEXSRF | **Sensor 2** = S2-1~2-5 |
| 9~13 | DP-09~13 | EEXCB, EEXLB, EEXRB, EEXSLB, EEXSRB | **Sensor 3** = S3-1~3-5 |

체인은 EC, EL, ER, SL, SR 5조이며 밸브 U1~U5 / 블로워 U6~U10 과 일치합니다.
**Sensor 1 은 EC·SL·SR 3곳에만 설치**되어 EL·ER 체인에는 없습니다 → Open Issue #16 의 물리적 근거.

**송풍팬 (JKBLD300V2 BLDC 드라이버, ID 6~10)**

| 주소 | 내용 | FC | 범위/값 | 용도 |
|---|---|---|---|---|
| `0x4005` | Control Mode | 3/6 | 0 개루프 / **1 폐루프** | 기동 시 1 로 설정 |
| `0x4006` | **폐루프 속도 설정** | 3/6 | **200~4000 rpm** | 제어 지령 |
| `0x4007` | 개루프 속도 설정 | 3/6 | 100~1000 (10~100% 듀티) | 사용 안 함 |
| `0x4011` | 가감속 시간 | 3/6 | 300~15000 ms | |
| `0x4034` | 통신 기동 | 3/6 | 0 정지 / 1 기동 | 정지는 이 레지스터로 |
| `0x4035` | 회전 방향 | 3/6 | 0 CW / 1 CCW | |
| `0x4037` | **명령 소스** | 3/6 | 0 IO / **1 통신** | 기동 시 1 로 설정 |
| `0x4041` | 현재 속도 | 3 | rpm | 폴링 |
| `0x4042` | 고장 코드 | 3 | | 폴링 (0x4041 과 연속) |
| `0x4043` | 고장 해제 | 6 | 1 | |
| `0x4044` | 485 보레이트 | 3/6 | 0:115200 1:57600 **2:38400** 3:19200 4:9600 | 재기동 후 반영 |

고장 코드: 2 소프트웨어 과전류 / 3 하드웨어 과전류 / 4 저전압 / 5 과전압 / 6 구속 / 7 홀 신호 상실 / 8 홀 결선 오류 / 9 과열

> **⚠ `0x4041`(현재 속도)은 개루프/폐루프와 무관합니다.**
> 읽기 전용 실측값이라 모드와 상관없이 항상 rpm 을 반환합니다. 모드가 갈리는 것은 **쓰기 레지스터**뿐입니다.
> 그래서 `IO List` 의 **IO 시트**(폴링 목록 — 블로워는 `0x4041` 만 기재)는 손댈 필요가 없고,
> **Driver 시트**의 `0x4007` 한 줄만 `0x4006` 으로 수정하면 됩니다.

> **⚠ 속도 지령은 `0x4006`(폐루프)으로 확정했습니다 (2026-08-10).**
> `IO List` 는 `0x4007` 을 "Velocity, 단위 RPM" 으로 적었으나 매뉴얼상 `0x4007` 은 개루프 듀티 %입니다.
> 개루프도 검토했으나 **매뉴얼 안에서 `0x4007` 의 스케일이 모순**이고(§0.4) 실장비 확인이 어려워 폐루프로 결정했습니다.
> `0x4006` 은 단위가 rpm 으로 명확하고 되읽기 값(`0x4041`)과 단위가 같아 편차 계산이 성립합니다.
> **IO List 의 `0x4007` 표기는 `0x4006` 으로 수정 요청 필요.**

#### 송풍팬 기동 시퀀스 (IO List 추가 요청 항목)

순서를 지켜야 합니다. `0x4037` 을 먼저 쓰지 않으면 이후 지령이 전부 무시됩니다.

| 순서 | 주소 | 값 | 의미 |
|---|---|---|---|
| 1 | `0x4037` | 1 | 명령 소스를 **통신**으로. 0 이면 드라이버가 IO·가변저항 입력을 본다 |
| 2 | `0x4005` | 1 | **폐루프** 모드. 0(기본)이면 `0x4006` 이 아니라 `0x4007` 이 유효해진다 |
| 3 | `0x4006` | 초기 rpm | 200~4000 |
| 4 | `0x4034` | 1 | 운전 개시 |

정지는 `0x4034 = 0` 입니다. **최소 설정값이 200 rpm 이라 "속도 0 지령"으로는 멈출 수 없습니다.**

`0x4037` 은 기본값이 0 이므로 드라이버 전원 재투입 시 초기화될 수 있습니다. Slow 티어로 되읽어 0 으로 돌아간 것을 감지하면 재설정하도록 구성했습니다. 이것이 없으면 순간 정전 후 드라이버가 우리 지령을 조용히 무시합니다.

#### 보레이트 변경은 커미셔닝 절차입니다 (앱이 할 수 없음)

> 전체 설치 절차는 **`COMMISSIONING.md`** 로 분리했습니다. 아래는 요약입니다.

`0x4044 = 2`(38400)는 **재기동 후에만** 반영됩니다. 그런데 우리 프로그램은 CH2 를 38400 으로 여는 반면 출하 상태 드라이버는 115200 이라, **애초에 통신이 되지 않아 이 값을 쓸 수 없습니다.**

따라서 별도 절차로 분리합니다.

1. 별도 도구로 **115200** 접속
2. `0x4044 = 2` 쓰기
3. 드라이버 **전원 재투입**
4. 38400 으로 정상 통신 확인

**2026-08-10 확정**: 출하 상태는 115200 이므로 이 절차는 **항상 필요**합니다. `COMMISSIONING.md` §1.1 에 필수 절차로 명시했습니다.

> **⚠ #20 답변 해석.** "최대 20000 / 최소 0 rpm" 은 매뉴얼 §2.1 의 **드라이버 전기 사양**(어떤 모터든 구동 가능한 한계)입니다. Modbus 로 설정 가능한 범위는 `0x4006` 의 **200~4000 rpm** 이며, 제어 파라미터는 후자를 따라야 합니다. 최소가 200 이므로 **정지는 속도 0 지령이 아니라 `0x4034 = 0`** 으로 수행합니다.

> **⚠ 보레이트 변경은 필수입니다 (2026-08-10 확정).** 블로워 드라이버는 **출하 시 115200 bps** 이고 CH2 는 38400 bps 입니다. `0x4044 = 2` 설정 후 재기동해야 하며, **생략하면 해당 블로워와 통신이 전혀 되지 않습니다.** 5대 각각에 수행해야 하고, 드라이버 교체·추가 시에도 매번 필요합니다. 절차는 `COMMISSIONING.md` §1.1.

**PLC (LS XBM, ID 25)**

`0x000A` 한 워드에 디지털 입력 8점이 비트마스크로 들어옵니다. **1트랜잭션으로 전부 읽힙니다.**

| bit | PLC DATA | 마스크 | Tag | 내용 | 접점 |
|---|---|---|---|---|---|
| 0 | D10.0 | 1 | EMO1 | **비상정지** | NO, 정상 OFF |
| 1 | D10.1 | 2 | BF01 | EC BLDC 냉각팬 정지 | NPN |
| 2 | D10.2 | 4 | BF02 | EL BLDC 냉각팬 정지 | NPN |
| 3 | D10.3 | 8 | BF03 | ER BLDC 냉각팬 정지 | NPN |
| 4 | D10.4 | 16 | BF04 | SL BLDC 냉각팬 정지 | NPN |
| 5 | D10.5 | 32 | BF05 | SR BLDC 냉각팬 정지 | NPN |
| 6 | D10.6 | 64 | FAN01 | 컨트롤박스 팬 T 정지 | NPN |
| 7 | D10.7 | 128 | FAN02 | 컨트롤박스 팬 B 정지 | NPN |

> `ESAM_IO List_260806.xlsx` 에서 PLC DATA 표기가 D10.**1~8** 에서 D10.**0~7** 로 바뀌었습니다.
> 마스크 값(1·2·4…128)은 그대로이므로 **비트 인덱스는 변하지 않았습니다.** 코드 수정 불필요.

> **⚠ 도어 열림 · 메인 차단기 입력이 없습니다.** 인터록 IL-03(차단기) · IL-05(도어)는 신호원이 없어 **비활성**입니다. SPARE DI 가 2점 남아 있으므로 배선 추가 시 활성화만 하면 됩니다. → Open Issue #7

> **⚠ 2026-07-31 초안의 EMO = D10.6 은 오류였습니다.** EMO 는 **bit 0** 입니다. 그 매핑으로 배선했다면 비상정지를 냉각팬 알람으로 읽게 됩니다.

온도 (K형 열전대, 0~500 ℃):

| 주소 | 내용 |
|---|---|
| `0x0064`~`0x0068` | EC / EL / ER / SL / SR BLDC 온도 (연속 5) |
| `0x006E`~`0x0072` | 각 채널 센서 단선 (연속 5) |

`0x0064`~`0x0072` 15워드를 1트랜잭션으로 읽는 편이 5+5 두 번보다 빠릅니다.

**압력센서 (Autonics PSM, ID 26)** — **Blower Pressure Sensor**. 1대에 5채널이며 Input Register 이므로 **FC04** 를 씁니다.

> **위치는 Bottom 으로 확정했습니다 (2026-08-10).** `ESAM_IO List_260806.xlsx` 의 `SVID` 시트가
> 24~28번을 "Exhaust ... **Front** PT Sensor" 로 적었으나, 설계상 PSM 은 블로워 압력을 보는
> **Bottom** 계측입니다. IO 시트 표기(EEXCB/LB/RB/SLB/SRB)가 맞고 **SVID 시트가 오류**입니다.
> → SVID 시트 명칭 수정 요청 필요.

| 채널 | PV 주소 | 위치 |
|---|---|---|
| 1 | `0x03E9` | EEXCB |
| 2 | `0x03EE` | EEXLB |
| 3 | `0x03F3` | EEXRB |
| 4 | `0x03F8` | EEXSLB |
| 5 | `0x03FD` | EEXSRB |

PV 는 5레지스터 간격이므로 `0x03E9` 부터 **21워드 1회**로 5채널을 모두 읽습니다(5회 읽기 대비 약 3배 빠름). 채널당 배치는 `Unit / PV / Dot / High Peak / Low Peak` 순입니다.
특수값: `30000` = HHHH(상한 초과), `-30000` = LLLL(하한 미만 또는 단선). **스케일 소수점 위치는 채널별 `Dot` 레지스터(PV+1)에 있으므로 기동 시 1회 읽어 적용해야 합니다.**

**온습도 (ID 15)** — `0x0000` 온도, `0x0001` 습도 (연속 2워드)
**풍속 (ID 20~22)** — `0x0002` 에러, `0x0005` 풍속. 4워드 1트랜잭션으로 묶음

### 0.4 매뉴얼 내부 모순 (벤치 검증 필요)

JKBLD300V2 매뉴얼의 예제가 표와 어긋납니다. 실장비 확인 전에는 확정하지 않습니다.

- §7.4 속도 읽기 응답 `0x0E38 = 3640` 인데 캡션은 "current speed is 1000rpm"
- §7.7 예제가 `0x4007` 에 `0x32 = 50` 을 쓰고 "50%" 라 설명. 표는 100~1000 이 10~100% 이므로 50% 는 500 이어야 함

---

## 1. 디바이스 인벤토리 (2026-07-31 초안 — **§0 으로 대체됨**)

> ⚠ 아래 §1 의 Bus A/B/C 3포트 구성은 `IO List_260801.xlsx` 수령 전의 추정입니다.
> 실제 구성은 **§0.1 의 CH1/CH2 2채널**입니다. 이력 참조용으로만 남깁니다.

### 1.1 Bus A — RS-485 / Modbus RTU / 19200 / 8-N-1

| 명칭 | 품명 | Slave ID | 수량 | 매뉴얼 | 비고 |
|---|---|---|---|---|---|
| 차압센서 1-1 | WTDM-550 | 1 | 1 | 4 | 양압영역, EFEM 내부 압력 |
| 차압센서 1-2 | WTDM-550 | 2 | 1 | 4 | |
| 차압센서 1-3 | WTDM-550 | 3 | 1 | 4 | |
| 차압센서 2-1 | WTDM-550 | 4 | 1 | 4 | 밸브/팬 1조당 1개 |
| 차압센서 2-2 | WTDM-550 | 5 | 1 | 4 | |
| 차압센서 2-3 | WTDM-550 | 6 | 1 | 4 | |
| 차압센서 2-4 | WTDM-550 | 7 | 1 | 4 | |
| 차압센서 2-5 | WTDM-550 | 8 | 1 | 4 | |
| 차압센서 3-1 | WTDM-550 | 9 | 1 | 4 | 배기라인(뒷단), 인터록 트리거 |
| 차압센서 3-2 | WTDM-550 | 10 | 1 | 4 | |
| 차압센서 3-3 | WTDM-550 | 11 | 1 | 4 | |
| 차압센서 3-4 | WTDM-550 | 12 | 1 | 4 | |
| 차압센서 3-5 | WTDM-550 | 13 | 1 | 4 | |
| 온습도센서 | THD-R-T | 15 | 1 | 3 | ID 14 결번 |
| 풍속센서 1 | BECK985-RS485 | 20 | 1 | 1 | BOM은 AVS701(E+E)/985VM(Beck) 2종 병기 |
| 풍속센서 2 | BECK985-RS485 | 21 | 1 | 1 | |
| 풍속센서 3 | BECK985-RS485 | 22 | 1 | 1 | |
| PLC | LS XBM-DR16S | 25 | 1 | 6 | 아래 1.1.1 |

**센서 레인지**: `WTDM-550-N4C±750-MB-D1` (±750 Pa) 또는 `WTDM-550-N4C±2.0K-MB-D1` (±2000 Pa)
→ **어느 위치에 어느 레인지를 쓰는지 확인 필요.** Sensor 3 Mode는 -200 Pa ± 100 Pa (최대 -300 Pa)이므로 ±750 Pa로 커버 가능하나, 배기 역압 시 초과 가능성 검토 필요. Config에서 **채널별 레인지 개별 지정** 구조로 설계.

#### 1.1.1 PLC (Slave 25) 데이터 맵

**Word 영역 (온도, D100~D105)**

| PLC Addr | 내용 | 스케일 | Config Key |
|---|---|---|---|
| D100 | 송풍팬 온도 2-1 | TBD | `plc.temp.fan1` |
| D101 | 송풍팬 온도 2-2 | TBD | `plc.temp.fan2` |
| D102 | 송풍팬 온도 2-3 | TBD | `plc.temp.fan3` |
| D103 | 송풍팬 온도 2-4 | TBD | `plc.temp.fan4` |
| D104 | 송풍팬 온도 2-5 | TBD | `plc.temp.fan5` |
| D105 | 판넬(컨트롤박스) 온도 | TBD | `plc.temp.panel` |

→ D100~D105는 연속 6워드이므로 **FC03 1회 트랜잭션으로 일괄 read**.
→ LS XGB/XBM 계열의 D영역 ↔ Modbus 주소 변환 규칙 확인 필요 (통상 D0000 = 0x0000 또는 오프셋 존재). **TBD**

**Bit 영역 (D10.0 ~ D10.8) — PLC Signal 시트**

| 비트 | 내용 | 판정 | Alarm/Interlock |
|---|---|---|---|
| D10.0 | 송풍팬 FAN1 정지 ALARM | 1 = Alarm | A11 |
| D10.1 | 송풍팬 FAN2 정지 ALARM | 1 = Alarm | A11 |
| D10.2 | 송풍팬 FAN3 정지 ALARM | 1 = Alarm | A11 |
| D10.3 | 송풍팬 FAN4 정지 ALARM | 1 = Alarm | A11 |
| D10.4 | 송풍팬 FAN5 정지 ALARM | 1 = Alarm | A11 |
| D10.5 | 제어박스 FAN 정지 ALARM | 1 = Alarm | A04 Cooling fan error |
| D10.6 | EMO ON/OFF 상태 | TBD (극성) | **IL-02 SafeStop** |
| D10.7 | Door OPEN/CLOSE | TBD (극성) | 정책 미정 (Open Issue #7) |
| D10.8 | 메인 차단기 OFF | TBD (극성) | **IL-03 SafeStop** |

> ⚠ **D10은 1워드(16bit)이므로 D10 워드 1회 read 후 비트 마스킹**으로 처리 (9회 개별 read 금지).
> ⚠ 각 비트의 **Active High / Active Low 극성 확인 필수**. 특히 EMO·차단기는 Fail-Safe 관점에서 "정상 = 1(신호 있음)"이어야 안전합니다. → 확인 필요.

### 1.2 Bus B — RS-485 / Modbus RTU / 38400

| 명칭 | 제조 | Slave ID | 매뉴얼 | 비고 |
|---|---|---|---|---|
| 스로틀밸브 2-1 | 자사(DSE) | 1 | 5 | |
| 스로틀밸브 2-2 | 자사(DSE) | 2 | 5 | |
| 스로틀밸브 2-3 | 자사(DSE) | 3 | 5 | |
| 스로틀밸브 2-4 | 자사(DSE) | 4 | 5 | |
| 스로틀밸브 2-5 | 자사(DSE) | 5 | 5 | |
| MFC 1 | 미정 | 20 | - | 38400, 사양 미정 |
| MFC 2 | 미정 | 21 | - | 38400, 사양 미정 |

> ⚠ **Bus A와 Slave ID 1~5, 20~22가 중복**되고, **Bus C(송풍팬)와도 1~5가 중복**됩니다. 세 버스의 물리 포트 분리 필수 (DESIGN.md 2.2 A).

### 1.3 Bus C — 송풍팬 / RS-485 Modbus RTU 직결 **(2026-07-31 변경)**

> **변경**: CAN(0x721~0x725, 500kbps) → **RS-485 Modbus RTU 직결**. CAN 게이트웨이 폐지.
> xlsx 통신자료의 CAN 관련 기재(CAN ID, 499/500 kbps)는 **무효**이며 재발행이 필요합니다.

| 명칭 | 제조 | Slave ID | Baud | 매뉴얼 | 비고 |
|---|---|---|---|---|---|
| 송풍팬 2-1 | 자사(DSE) | 1 *(가정)* | 38400 *(가정)* | 2 | |
| 송풍팬 2-2 | 자사(DSE) | 2 *(가정)* | 38400 *(가정)* | 2 | |
| 송풍팬 2-3 | 자사(DSE) | 3 *(가정)* | 38400 *(가정)* | 2 | |
| 송풍팬 2-4 | 자사(DSE) | 4 *(가정)* | 38400 *(가정)* | 2 | |
| 송풍팬 2-5 | 자사(DSE) | 5 *(가정)* | 38400 *(가정)* | 2 | |

> ⚠ Slave ID / Baud / Parity 모두 **미확정 (Open Issue #19)**. 위 값은 밸브와 동일 조건을 가정한 잠정값이며 Config로 즉시 변경 가능합니다.
> ⚠ **Bus B(밸브)와 Slave ID 1~5가 중복**되므로 물리 포트 분리 필수. 만약 팬과 밸브를 동일 포트에 올릴 계획이라면 팬 ID를 **6~10으로 재배정**해야 합니다.

**필요 명세 (Open Issue #9)**

| 항목 | 방향 | 용도 |
|---|---|---|
| RPM 설정값 | Write | 자동제어 증속/감속, 수동 Override |
| RPM 현재값 | Read (fast tier) | 화면 표시, 도달 판정, 로깅 |
| ON / OFF | Write | 밴드 제어 하한 이탈 시 팬 Off, 인터록 |
| 운전 상태 | Read (fast tier) | 정지/가속/정속 판정 |
| Alarm code | Read (medium tier) | A11 송풍팬 error |
| Alarm reset | Write | Maintenance 화면 |
| 최대/최소 RPM | 사양 | `control.json` actuator.fan 한계값 (Open Issue #20) |

**설계 요청 사항 (자사 제작품이므로 반영 가능)**

1. **RPM 현재값 + 운전상태를 연속 주소로 배치** → 팬 1대당 fast tier 트랜잭션을 2회 → **1회로 축소** (BUS_C 사이클 103~253ms → 52~127ms)
2. **밸브(`0x6002` 명령 / `0x6202` 설정값 / `0x2203` 알람)와 동일한 주소 체계 채택** → `ModbusFanDriver`와 `ThrottleValveDriver`가 공통 베이스 클래스를 공유, 개발·검증 공수 절감
3. RPM 설정 후 **도달 판정 방식**(상태 비트 vs 현재값 비교) 명시

### 1.4 통신 시트에 없으나 BOM(PDF p.15)에 존재 — 미배정

| 부품 | 품번 | 수량 | 통신 | 상태 |
|---|---|---|---|---|
| 온도센서(EFEM) | PSU650-AR (DOTECH) | 1 | RS-485 Modbus | 통신 시트에 THD-R-T(ID15)만 존재 → 동일 장치? |
| 습도센서(EFEM) | PSU650-AR (DOTECH) | 1 | RS-485 Modbus | 상동 |
| 파티클센서(EFEM) | PSU650-AR (DOTECH) | 1 | RS-485 Modbus | **Slave ID 미배정** |
| 온도센서(컨트롤박스) | 미정 | 1 | RS-485 Modbus | PLC D105와 중복? |
| FAN(컨트롤박스) | 미정 | 1 | RS-485 Modbus | PLC D10.5와 중복? |
| FFU | (PDF Schematic) | ? | 미정 | **통신 방식·주소 전체 미정** — 화면에는 RPM 현재값/High·Low Limit 존재 |

→ Open Issue #10, #11, #12. **BOM 3개 항목이 동일 품번(PSU650-AR)** 이므로 1대 통합형(온도+습도+파티클) 가능성이 큽니다. 이 경우 Slave 1개만 추가되며 화면 3개 값이 동일 디바이스에서 나옵니다.

---

## 2. 스로틀밸브 레지스터 맵

### 2.1 Write

| Address | Value | 동작 | 사용 시점 |
|---|---|---|---|
| `0x6002` | `0x20` | Homing | **전원 ON 직후 필수** / Maintenance 수동 |
| `0x6002` | `0x10` | PR0 Move | 자동/수동 위치 지령 |
| `0x6002` | `0x40` | Quick Stop | **인터록 / E-Stop** |
| `0x6202` | Pulse | PR0 위치값 설정 | Move 직전 |
| `0x6203` | 1~5 | PR0 속도 (RPM) | 초기화 / 튜닝 |
| `0x1801` | `0x1111` | 현재 알람 리셋 | Alarm Reset 버튼 |

### 2.2 Read

| Address | 내용 | 사용처 |
|---|---|---|
| `0x602B` | 현재 위치 (Pulse) | 화면 현재값, Move 완료 판정 |
| `0x2203` | Alarm | A10 Throttle valve error |
| `0x1003` | Motion Status | Move 완료/이동중 판정 |
| `0x0147` | HOME ON/OFF | Init 시퀀스 완료 판정 |

### 2.3 단위 변환

```
90°  = 5000 pulse
pulse   = round(percent / 100.0 * 5000)     # 0% = 0 pulse (0°, Close)
percent = pulse / 5000.0 * 100.0
degree  = pulse / 5000.0 * 90.0
```

### 2.4 필수 시퀀스

**초기화 (전원 ON 후 / Init 상태)**
```
1. 0x0147 read → HOME 완료 여부 확인
2. 미완료 시: 0x6002 ← 0x20 (Homing)
3. 0x1003 / 0x0147 폴링 → 완료 대기 (Timeout: Config, 기본 30s)
4. 0x6203 ← 속도값 (Config)
5. 0x6202 ← 0 → 0x6002 ← 0x10   (초기 위치 Close)
```

**위치 지령**
```
1. 0x6202 ← target pulse
2. 0x6002 ← 0x10 (PR0 Move)
3. 0x602B / 0x1003 폴링 → 도달 판정 (|현재 - 목표| <= tolerance)
4. Timeout 초과 시 → A10 Alarm + 0x2203 read하여 원인 기록
```

**인터록**
```
0x6202 ← 0 → 0x6002 ← 0x10       (Close)
  또는 즉시 정지 필요 시 0x6002 ← 0x40 (Quick Stop) 후 Close
```
→ IL-01 시 **Quick Stop 우선인지 Close Move 우선인지 확인 필요.** 안전 관점에서는 "닫힘" 도달이 목적이므로 Close Move가 맞으나, 이동 중이었다면 Quick Stop 후 재지령이 안전합니다. → Open Issue #5 포함.

### 2.5 미확보 명세 (TBD)

| 항목 | 필요 이유 |
|---|---|
| Modbus 함수코드 (FC03 vs FC04 / FC06 vs FC16) | 프레임 구성 |
| `0x6002` 값이 명령코드인지 비트플래그인지 | 동시 지령 가능 여부 |
| `0x1003` Motion Status 비트 정의 | 완료 판정 로직 |
| `0x2203` Alarm 비트/코드 정의 | 알람 메시지 매핑 |
| `0x602B`, `0x6202`가 16bit인지 32bit(2word)인지 | 5000 pulse는 16bit로 충분하나 다회전 시 초과 가능 |
| 워드 순서 (Big/Little endian, word swap) | 32bit 값 파싱 |
| 위치 도달 허용오차(pulse) 및 최대 Move 시간 | Timeout 설정 |
| 최대 개도 제한(90° 초과 가능 여부) | 안전 제한 |

---

## 3. 차압센서 (WTDM-550) — TBD

매뉴얼 4번 필요. 필요 항목:

| 항목 | 상태 |
|---|---|
| 압력값 레지스터 주소 / 함수코드 | **TBD** |
| 데이터 타입 (INT16 / INT32 / FLOAT32) 및 워드 순서 | **TBD** |
| 스케일 팩터 (예: 0.1 Pa/LSB) 및 부호 처리 | **TBD** |
| 상태/에러 레지스터 (A/P00 Analog card error 판정용) | **TBD** |
| 영점 교정 명령 지원 여부 (장치 내부 zero vs SW offset) | **TBD** — 미지원 시 SW offset으로 구현 |
| 응답 지연 시간 (typ/max) | **TBD** — 사이클타임 산정 핵심 |
| 내부 필터/응답 시간 설정 가능 여부 | **TBD** — 수 Pa 제어 시 노이즈 대책 |

**노이즈 대책 (사양 확보 전 설계 방침)**: 수 Pa 단위 제어이므로 원시값에 **이동평균(N=Config) 또는 1차 IIR 필터**를 적용하고, 필터 전/후 값을 모두 로깅하여 Phase 5에서 필터 상수를 튜닝할 수 있게 합니다.

---

## 4. Config 스키마 (초안)

### 4.1 `ports.json`

```jsonc
{
  "schemaVersion": "1.0",
  "ports": [
    {
      "id": "BUS_A",
      "comPort": "COM3",
      "baudRate": 19200,
      "parity": "None",
      "dataBits": 8,
      "stopBits": 1,
      "responseTimeoutMs": 200,
      "retryCount": 2,
      "interFrameDelayMs": 3,        // t3.5 보정 (19200 → 약 2ms + 여유)
      "rtsToggleForRs485": false,    // 반이중 트랜시버가 자동제어면 false
      "pollingTiers": {
        "fastMs": 200,               // 차압센서 — Open Issue #1 결론에 따라 조정
        "mediumMs": 1000,            // PLC 디지털/온도
        "slowMs": 5000               // 온습도/풍속/파티클
      }
    },
    { "id": "BUS_B", "comPort": "COM4", "baudRate": 38400, "parity": "None",
      "dataBits": 8, "stopBits": 1, "responseTimeoutMs": 150, "retryCount": 2,
      "pollingTiers": { "fastMs": 200, "mediumMs": 1000, "slowMs": 5000 } },
    // BUS_C: 송풍팬 전용 (Modbus 직결, 2026-07-31 변경). 파라미터 TBD
    { "id": "BUS_C", "comPort": "COM5", "baudRate": 38400, "parity": "None",
      "dataBits": 8, "stopBits": 1, "responseTimeoutMs": 200, "retryCount": 2,
      "pollingTiers": { "fastMs": 200, "mediumMs": 1000, "slowMs": 5000 } }
  ]
}
```

### 4.2 `device-map.json`

레지스터 맵을 **선언적으로** 기술 → 신규 디바이스 추가 시 코드 변경 없음.

```jsonc
{
  "schemaVersion": "1.0",
  "deviceTypes": {
    "WTDM550": {
      "driver": "PressureSensor",
      "readGroups": [
        {
          "name": "pressure",
          "tier": "fast",
          "functionCode": 4,           // TBD (FC03/FC04)
          "startAddress": 0,           // TBD
          "count": 2,                  // TBD
          "points": [
            { "key": "pressurePa", "offset": 0, "type": "Int16",
              "wordOrder": "BigEndian", "scale": 0.1, "bias": 0.0, "unit": "Pa" }
          ]
        },
        {
          "name": "status", "tier": "slow",
          "functionCode": 3, "startAddress": 0, "count": 1,   // TBD
          "points": [ { "key": "deviceStatus", "offset": 0, "type": "UInt16" } ]
        }
      ]
    },

    "ThrottleValve": {
      "driver": "ThrottleValve",
      "readGroups": [
        { "name": "position", "tier": "fast", "functionCode": 3,
          "startAddress": "0x602B", "count": 1,
          "points": [ { "key": "positionPulse", "offset": 0, "type": "UInt16" } ] },
        { "name": "status", "tier": "fast", "functionCode": 3,
          "startAddress": "0x1003", "count": 1,
          "points": [ { "key": "motionStatus", "offset": 0, "type": "UInt16" } ] },
        { "name": "alarm", "tier": "medium", "functionCode": 3,
          "startAddress": "0x2203", "count": 1,
          "points": [ { "key": "alarmCode", "offset": 0, "type": "UInt16" } ] },
        { "name": "home", "tier": "medium", "functionCode": 3,
          "startAddress": "0x0147", "count": 1,
          "points": [ { "key": "homeDone", "offset": 0, "type": "Bool" } ] }
      ],
      "commands": {
        "homing":      { "functionCode": 6, "address": "0x6002", "value": "0x20" },
        "prMove":      { "functionCode": 6, "address": "0x6002", "value": "0x10" },
        "quickStop":   { "functionCode": 6, "address": "0x6002", "value": "0x40" },
        "setPosition": { "functionCode": 6, "address": "0x6202", "value": "$arg" },
        "setVelocity": { "functionCode": 6, "address": "0x6203", "value": "$arg" },
        "alarmReset":  { "functionCode": 6, "address": "0x1801", "value": "0x1111" }
      },
      "conversion": {
        "pulsePerFullOpen": 5000,
        "fullOpenDegree": 90,
        "positionToleranceP": 20,
        "moveTimeoutMs": 10000,
        "homingTimeoutMs": 30000
      }
    },

    "LsXbmPlc": {
      "driver": "Plc",
      "readGroups": [
        { "name": "temps", "tier": "medium", "functionCode": 3,
          "startAddress": "TBD(D100)", "count": 6,
          "points": [
            { "key": "temp.fan1",  "offset": 0, "type": "Int16", "scale": 0.1, "unit": "C" },
            { "key": "temp.fan2",  "offset": 1, "type": "Int16", "scale": 0.1, "unit": "C" },
            { "key": "temp.fan3",  "offset": 2, "type": "Int16", "scale": 0.1, "unit": "C" },
            { "key": "temp.fan4",  "offset": 3, "type": "Int16", "scale": 0.1, "unit": "C" },
            { "key": "temp.fan5",  "offset": 4, "type": "Int16", "scale": 0.1, "unit": "C" },
            { "key": "temp.panel", "offset": 5, "type": "Int16", "scale": 0.1, "unit": "C" }
          ] },
        { "name": "digital", "tier": "fast", "functionCode": 3,
          "startAddress": "TBD(D10)", "count": 1,
          "points": [
            { "key": "di.fan1Stop",     "offset": 0, "bit": 0, "type": "Bool", "activeHigh": true },
            { "key": "di.fan2Stop",     "offset": 0, "bit": 1, "type": "Bool", "activeHigh": true },
            { "key": "di.fan3Stop",     "offset": 0, "bit": 2, "type": "Bool", "activeHigh": true },
            { "key": "di.fan4Stop",     "offset": 0, "bit": 3, "type": "Bool", "activeHigh": true },
            { "key": "di.fan5Stop",     "offset": 0, "bit": 4, "type": "Bool", "activeHigh": true },
            { "key": "di.ctrlBoxFan",   "offset": 0, "bit": 5, "type": "Bool", "activeHigh": true },
            { "key": "di.emo",          "offset": 0, "bit": 6, "type": "Bool", "activeHigh": true },
            { "key": "di.door",         "offset": 0, "bit": 7, "type": "Bool", "activeHigh": true },
            { "key": "di.mainBreaker",  "offset": 0, "bit": 8, "type": "Bool", "activeHigh": true }
          ] }
      ]
    },

    // 송풍팬 — Modbus RTU 직결. 주소/타입 전부 TBD(Open Issue #9).
    // 밸브와 동일 주소체계 채택 시 ThrottleValve 정의를 그대로 복제하면 됨.
    "BlowerFan": {
      "driver": "ModbusFan",
      "readGroups": [
        { "name": "runtime", "tier": "fast", "functionCode": 3,
          "startAddress": "TBD", "count": 2,
          "points": [
            { "key": "rpm",       "offset": 0, "type": "UInt16", "scale": 1.0, "unit": "RPM" },
            { "key": "runStatus", "offset": 1, "type": "UInt16" }
          ] },
        { "name": "alarm", "tier": "medium", "functionCode": 3,
          "startAddress": "TBD", "count": 1,
          "points": [ { "key": "alarmCode", "offset": 0, "type": "UInt16" } ] }
      ],
      "commands": {
        "setRpm":     { "functionCode": 6, "address": "TBD", "value": "$arg" },
        "start":      { "functionCode": 6, "address": "TBD", "value": "TBD" },
        "stop":       { "functionCode": 6, "address": "TBD", "value": "TBD" },
        "alarmReset": { "functionCode": 6, "address": "TBD", "value": "TBD" }
      },
      "conversion": {
        "minRpm": 0, "maxRpm": 0,          // TBD (Open Issue #20)
        "offBelowRpm": 100,
        "rpmTolerance": 50,
        "rampTimeoutMs": 15000
      }
    },

    "ThdRt":       { "driver": "TempHumidity", "readGroups": [ /* TBD */ ] },
    "Beck985":     { "driver": "AirVelocity",  "readGroups": [ /* TBD */ ] },
    "Mfc":         { "driver": "Mfc",          "readGroups": [ /* TBD */ ] }
  },

  "devices": [
    { "id": "S1-1", "type": "WTDM550", "port": "BUS_A", "slaveId": 1,
      "range": { "min": -750, "max": 750, "unit": "Pa" }, "offsetPa": 0.0,
      "filter": { "type": "MovingAverage", "windowSize": 5 } },
    { "id": "S1-2", "type": "WTDM550", "port": "BUS_A", "slaveId": 2, "offsetPa": 0.0 },
    { "id": "S1-3", "type": "WTDM550", "port": "BUS_A", "slaveId": 3, "offsetPa": 0.0 },
    { "id": "S2-1", "type": "WTDM550", "port": "BUS_A", "slaveId": 4, "offsetPa": 0.0 },
    { "id": "S2-2", "type": "WTDM550", "port": "BUS_A", "slaveId": 5, "offsetPa": 0.0 },
    { "id": "S2-3", "type": "WTDM550", "port": "BUS_A", "slaveId": 6, "offsetPa": 0.0 },
    { "id": "S2-4", "type": "WTDM550", "port": "BUS_A", "slaveId": 7, "offsetPa": 0.0 },
    { "id": "S2-5", "type": "WTDM550", "port": "BUS_A", "slaveId": 8, "offsetPa": 0.0 },
    { "id": "S3-1", "type": "WTDM550", "port": "BUS_A", "slaveId": 9, "offsetPa": 0.0 },
    { "id": "S3-2", "type": "WTDM550", "port": "BUS_A", "slaveId": 10, "offsetPa": 0.0 },
    { "id": "S3-3", "type": "WTDM550", "port": "BUS_A", "slaveId": 11, "offsetPa": 0.0 },
    { "id": "S3-4", "type": "WTDM550", "port": "BUS_A", "slaveId": 12, "offsetPa": 0.0 },
    { "id": "S3-5", "type": "WTDM550", "port": "BUS_A", "slaveId": 13, "offsetPa": 0.0 },

    { "id": "TH-1",  "type": "ThdRt",    "port": "BUS_A", "slaveId": 15 },
    { "id": "AV-1",  "type": "Beck985",  "port": "BUS_A", "slaveId": 20 },
    { "id": "AV-2",  "type": "Beck985",  "port": "BUS_A", "slaveId": 21 },
    { "id": "AV-3",  "type": "Beck985",  "port": "BUS_A", "slaveId": 22 },
    { "id": "PLC-1", "type": "LsXbmPlc", "port": "BUS_A", "slaveId": 25 },

    { "id": "V-1", "type": "ThrottleValve", "port": "BUS_B", "slaveId": 1 },
    { "id": "V-2", "type": "ThrottleValve", "port": "BUS_B", "slaveId": 2 },
    { "id": "V-3", "type": "ThrottleValve", "port": "BUS_B", "slaveId": 3 },
    { "id": "V-4", "type": "ThrottleValve", "port": "BUS_B", "slaveId": 4 },
    { "id": "V-5", "type": "ThrottleValve", "port": "BUS_B", "slaveId": 5 },

    { "id": "MFC-1", "type": "Mfc", "port": "BUS_B", "slaveId": 20, "enabled": false },
    { "id": "MFC-2", "type": "Mfc", "port": "BUS_B", "slaveId": 21, "enabled": false },

    // 송풍팬 — Modbus 직결. slaveId는 잠정값 (Open Issue #19)
    { "id": "F-1", "type": "BlowerFan", "port": "BUS_C", "slaveId": 1 },
    { "id": "F-2", "type": "BlowerFan", "port": "BUS_C", "slaveId": 2 },
    { "id": "F-3", "type": "BlowerFan", "port": "BUS_C", "slaveId": 3 },
    { "id": "F-4", "type": "BlowerFan", "port": "BUS_C", "slaveId": 4 },
    { "id": "F-5", "type": "BlowerFan", "port": "BUS_C", "slaveId": 5 }
  ]
}
```

### 4.3 `chains.json` — 확장 시 이 파일만 수정

```jsonc
{
  "schemaVersion": "1.0",
  "channel": "CH2",                 // Open Issue #15: CH1 추가 시 이 블록 복제
  "sensor1": ["S1-1", "S1-2", "S1-3"],
  "chains": [
    { "id": 1, "name": "Chain 2-1", "sensor2": "S2-1", "sensor3": "S3-1",
      "valve": "V-1", "fan": "F-1" },
    { "id": 2, "name": "Chain 2-2", "sensor2": "S2-2", "sensor3": "S3-2",
      "valve": "V-2", "fan": "F-2" },
    { "id": 3, "name": "Chain 2-3", "sensor2": "S2-3", "sensor3": "S3-3",
      "valve": "V-3", "fan": "F-3" },
    { "id": 4, "name": "Chain 2-4", "sensor2": "S2-4", "sensor3": "S3-4",
      "valve": "V-4", "fan": "F-4" },
    { "id": 5, "name": "Chain 2-5", "sensor2": "S2-5", "sensor3": "S3-5",
      "valve": "V-5", "fan": "F-5" }
  ],
  "auxiliary": {
    "ffu": null,                    // TBD
    "mfc": ["MFC-1", "MFC-2"],
    "airVelocity": ["AV-1", "AV-2", "AV-3"],
    "tempHumidity": "TH-1",
    "particle": null                // TBD
  }
}
```

> **Sensor 1 Mode 주의**: PDF p.3은 "센서 1은 EFEM 내부에 1개만 설치"라고 하지만 통신 시트/화면에는 1-1, 1-2, 1-3 세 개가 있고 Config 테이블에도 S1-1/1-2/1-3 각각의 Set/Band/Time이 존재합니다.
> → **Sensor 1 Mode에서 5개 체인이 어느 센서를 참조하는지 명시가 필요**합니다. 후보: (a) S1-1 단독 참조, (b) 3개 평균, (c) 체인별 지정 매핑. 설계는 **`sensor1Reference` Config 항목**으로 3가지 모두 선택 가능하게 합니다. → **Open Issue #16 (신규)**

### 4.4 `control.json`

```jsonc
{
  "schemaVersion": "1.0",
  "activeMode": "Sensor2",              // Sensor1 | Sensor2 | Sensor3
  "policy": "Band",                     // Band | Pid  (1차 릴리스는 Band)
  "controlPeriodMs": 200,
  "sensor1Reference": "S1-1",           // S1-1 | Average | PerChain (Open Issue #16)

  "modes": {
    "Sensor1": {
      "setpointPa": 6.0, "bandPa": 2.0, "timeSec": 60,
      "note": "문서상 예시는 5<x<7 (=±1Pa) — Open Issue #2"
    },
    "Sensor2": { "setpointPa": -10.0, "bandPa": 30.0, "timeSec": 120 },
    "Sensor3": { "setpointPa": -200.0, "bandPa": 100.0, "timeSec": 300 }
  },

  "actuator": {
    "valve": {
      "stepPulse": 100,                 // 1회 조정량 (Phase 5 튜닝)
      "minPulse": 0, "maxPulse": 5000,
      "dwellMs": 1000,                  // 조정 후 안정화 대기
      "velocityRpm": 3
    },
    "fan": {
      "stepRpm": 100,
      "minRpm": 0, "maxRpm": 0,         // TBD — 팬 사양 확정 후
      "dwellMs": 1000,
      "offBelowRpm": 100
    },
    "priority": ["Valve", "Fan"]        // 밸브 우선, 포화 후 팬
  },

  "pid": {                              // policy="Pid" 일 때만 사용
    "kp": 0.0, "ki": 0.0, "kd": 0.0,
    "outputMin": 0, "outputMax": 5000,
    "integralLimit": 1000, "derivativeFilterHz": 2.0
  },

  "filter": { "type": "MovingAverage", "windowSize": 5 },
  "logFilteredAndRaw": true             // Phase 5 튜닝용
}
```

### 4.5 `alarms.json` (발췌)

```jsonc
{
  "schemaVersion": "1.0",
  "alarms": [
    { "code": "P00", "name": "Analog card error (차압센서 통신)",
      "severity": "Critical", "source": "bus:BUS_A", "condition": "CommFail",
      "consecutiveFailures": 3, "debounceMs": 0,
      "requiresInterlock": true, "resetPolicy": "Manual" },

    { "code": "P01", "name": "차압 Sensor 1-1 High/Low limit error",
      "severity": "Alarm", "source": "device:S1-1.pressurePa",
      "condition": "OutOfBand", "refMode": "Sensor1",
      "debounceMs": 60000, "requiresInterlock": false, "resetPolicy": "Auto" },
    // P02~P14: S1-2 ~ S3-5 동일 패턴, debounceMs는 modes[*].timeSec 참조

    { "code": "A01", "name": "MFC 1 flow error", "severity": "Alarm",
      "source": "device:MFC-1", "condition": "TBD", "resetPolicy": "Manual" },
    { "code": "A02", "name": "MFC 2 flow error", "severity": "Alarm",
      "source": "device:MFC-2", "condition": "TBD", "resetPolicy": "Manual" },
    { "code": "A03", "name": "FDC communication error", "severity": "Warn",
      "source": "service:FdcAdapter", "condition": "CommFail",
      "debounceMs": 10000, "resetPolicy": "Auto" },
    { "code": "A04", "name": "Cooling fan (Control Box) error", "severity": "Alarm",
      "source": "device:PLC-1.di.ctrlBoxFan", "condition": "BitSet",
      "debounceMs": 3000, "resetPolicy": "Auto" },
    { "code": "A05", "name": "Temp sensor (Control Box) High limit",
      "severity": "Alarm", "source": "device:PLC-1.temp.panel",
      "condition": "GreaterThan", "threshold": 50.0, "debounceMs": 5000 },
    { "code": "A06", "name": "Temp sensor (EFEM) High limit", "severity": "Alarm",
      "source": "device:TH-1.temperature", "condition": "GreaterThan",
      "threshold": 30.0, "debounceMs": 5000 },
    { "code": "A07", "name": "Humidity sensor (EFEM) High limit", "severity": "Alarm",
      "source": "device:TH-1.humidity", "condition": "GreaterThan",
      "threshold": 60.0, "debounceMs": 5000 },
    { "code": "A08", "name": "Humidity sensor (EFEM) Low limit", "severity": "Alarm",
      "source": "device:TH-1.humidity", "condition": "LessThan",
      "threshold": 30.0, "debounceMs": 5000 },
    { "code": "A09", "name": "Particle High limit", "severity": "Alarm",
      "source": "device:TBD", "condition": "GreaterThan", "threshold": 0 },
    { "code": "A10", "name": "Throttle valve error", "severity": "Critical",
      "source": "deviceGroup:ThrottleValve", "condition": "CommFailOrAlarmCode",
      "resetPolicy": "Manual" },
    { "code": "A11", "name": "송풍팬 error", "severity": "Critical",
      "source": "deviceGroup:Fan", "condition": "CommFailOrAlarmCode",
      "resetPolicy": "Manual" },
    { "code": "A12", "name": "FFU error", "severity": "Critical",
      "source": "device:TBD", "condition": "CommFail", "resetPolicy": "Manual" },
    { "code": "A13", "name": "FFU High limit", "severity": "Alarm",
      "source": "device:TBD.rpm", "condition": "GreaterThan", "threshold": 0 },
    { "code": "A14", "name": "FFU Low limit", "severity": "Alarm",
      "source": "device:TBD.rpm", "condition": "LessThan", "threshold": 0 },
    { "code": "A15", "name": "풍속1 Low limit", "severity": "Alarm",
      "source": "device:AV-1.velocity", "condition": "LessThan", "threshold": 0 },
    { "code": "A16", "name": "풍속2 Low limit", "severity": "Alarm",
      "source": "device:AV-2.velocity", "condition": "LessThan", "threshold": 0 },
    { "code": "A17", "name": "풍속3 Low limit", "severity": "Alarm",
      "source": "device:AV-3.velocity", "condition": "LessThan", "threshold": 0 }
  ]
}
```

> Threshold 값 `0` / `TBD`는 **현장 기준값 미확보** 항목입니다. Config 화면에서 입력받도록 설계하며, 미설정 알람은 `enabled: false`로 비활성 시작합니다.

### 4.6 `interlocks.json`

```jsonc
{
  "schemaVersion": "1.0",
  "interlocks": [
    { "id": "IL-01", "name": "S3 High limit → Valve Close + Fan OFF",
      "trigger": { "condition": "AnyOf",
        "sources": ["S3-1","S3-2","S3-3","S3-4","S3-5"],
        "test": "GreaterThan", "thresholdFrom": "modes.Sensor3.high" },
      "scope": "Chain",              // Chain | System  (Open Issue #6)
      "actions": [
        { "target": "valve", "command": "closeFull" },
        { "target": "fan",   "command": "off" }
      ],
      "resetPolicy": "Manual", "requiredRole": "Maintenance",
      "clearCondition": "BackInBand", "clearHysteresisPa": 20 },

    { "id": "IL-02", "name": "EMO → System SafeStop",
      "trigger": { "condition": "BitSet", "source": "PLC-1.di.emo" },
      "scope": "System",
      "actions": [ { "target": "allValves", "command": "closeFull" },
                   { "target": "allFans", "command": "off" } ],
      "resetPolicy": "Manual", "requiredRole": "Maintenance" },

    { "id": "IL-03", "name": "Main Breaker OFF → SafeStop",
      "trigger": { "condition": "BitSet", "source": "PLC-1.di.mainBreaker" },
      "scope": "System",
      "actions": [ { "target": "allValves", "command": "closeFull" },
                   { "target": "allFans", "command": "off" } ],
      "resetPolicy": "Manual" },

    { "id": "IL-04", "name": "통신 상실 → Auto 중단 + Fail-Safe",
      "trigger": { "condition": "CommFail", "source": "bus:*",
                   "consecutiveFailures": 3 },
      "scope": "System",
      "actions": [ { "target": "controlEngine", "command": "stopAuto" } ],
      "resetPolicy": "Auto" },

    { "id": "IL-05", "name": "Door Open (정책 미정 — Open Issue #7)",
      "enabled": false,
      "trigger": { "condition": "BitSet", "source": "PLC-1.di.door" },
      "scope": "System", "actions": [], "resetPolicy": "Auto" }
  ]
}
```

### 4.7 로드 시 검증 규칙 (Fail-fast)

| # | 검증 | 실패 시 |
|---|---|---|
| 1 | 동일 `port` 내 `slaveId` 유일성 | 실행 거부 |
| 2 | `chains.json` 참조 device ID 존재 여부 | 실행 거부 |
| 3 | `deviceTypes`의 `readGroups` 주소 범위 중복 | 경고 |
| 4 | `modes[*].setpoint ± band`가 센서 `range` 내부 | 실행 거부 |
| 5 | 알람 Threshold가 센서 range 내부 | 경고 + 해당 알람 비활성 |
| 6 | `schemaVersion` 호환성 | 실행 거부 |
| 7 | 체인 내 valve/fan/sensor2/sensor3 모두 존재 | 실행 거부 |
| 8 | `interlocks` 참조 source 유효성 | **실행 거부** (안전 기능) |
| 9 | `actuator.fan.maxRpm > 0` (자동제어 사용 시) | 자동제어 진입 차단 |

---

## 5. 요약 — 확보/미확보 현황

| 영역 | 상태 |
|---|---|
| 디바이스 인벤토리 / Slave ID / Baud | ✅ 확보 (ID 충돌 확인 필요) |
| 스로틀밸브 레지스터 주소 | 🟡 주소는 확보, 비트정의·FC·엔디안 미확보 |
| PLC 디지털 신호 정의 | 🟡 신호명 확보, 극성·Modbus 주소변환 미확보 |
| PLC 온도 워드 | 🟡 D주소 확보, 스케일·Modbus 주소변환 미확보 |
| 차압센서 레지스터 | ❌ 미확보 (매뉴얼 4번 필요) |
| 온습도/풍속 레지스터 | ❌ 미확보 (매뉴얼 3, 1번 필요) |
| 송풍팬 (**Modbus 직결**, 2026-07-31 변경) | ❌ 미확보 — Slave ID/Baud/레지스터 전량 (Open Issue #9, #19, #20) |
| MFC / FFU / 파티클 / FDC | ❌ 사양 미정 |
| 제어 순서도 | ✅ 확보 (Sensor1 band 불일치 1건) |
| 알람/인터록 목록 | ✅ 확보 (임계값·범위 미정) |
| 화면 구성 | ✅ 확보 (Data Log Viewer 미정) |

**요청 사항**: 매뉴얼 **1, 3, 4, 5, 6번**(통신 시트 「매뉴얼」 열 참조) 원본을 받으면 위 ❌ 항목 대부분을 `device-map.json`으로 즉시 확정할 수 있습니다.
