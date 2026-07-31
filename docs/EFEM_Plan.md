현장 상황과 정확한 역할

**C# (.NET Framework 4.72)** 환경에서 **HMI/모니터링 시스템 개발(Phase 4)을 메인**으로 담당하시면서, 순차적으로 제어 로직 연결(Phase 3) 및 현장 교정/튜닝(Phase 5)까지 이끌어 가기 위한 **맞춤형 C# 아키텍처 및 구현 전략**을 제안해 드립니다.

## 1. Phase 4: HMI / 모니터링 시스템 아키텍처 (주업무)

13개 센서, 5개 밸브, 5개 팬의 실시간 데이터(100~200ms 주기)를 UI 멈춤(Freezing) 없이 매끄럽게 표현하는 것이 핵심입니다.

### ① UI 프레임워크 및 차트 라이브러리 추천

- **추천 UI 프레임워크:** **WPF (MVVM 패턴)**
  
  - *이유:* 데이터 바인딩이 강력하여 센서 13개의 실시간 값 업데이트 시 UI 스레드 과부하를 최소화할 수 있습니다. 

- **고속 트렌드 차트 라이브러리:** **ScottPlot** 또는 **LiveCharts**
  
  - **ScottPlot:** .NET 4.72 지원, 경량화되어 있으며 대용량 실시간 차트 렌더링 성능이 매우 뛰어남 (100ms 단위 실시간 갱신에 최적).

### ② 시각화 화면 구성 (기류 및 장비 상태)

- **구조적 기류 파이프라인 컴포넌트:**
  
  `[센서 1] ──> [스로틀 밸브] ──> [송풍팬] ──> [센서 2]`
  
  - 단순 숫자가 아닌, 각 체인별 **상태(정상/경고/에러)** 및 **개도율/RPM/차압**을 한눈에 볼 수 있는 커스텀 사용자 컨트롤(UserControl) 모듈화.

- **확장성(MFC / FFU) 대응 동적 UI:**
  
  - 설비 확장 시 C# 코드를 재컴파일하지 않도록 **JSON/XML 기반 Config 매핑 구조** 설계.
  
  - 예: `Config.json`에 Modbus Register 주소를 정의해 두면, UI가 실행될 때 자동으로 통신 모듈과 바인딩되도록 구현.

## 2. Phase 2 & 3: C# 기반 통신 및 제어 알고리즘 연동

타부서에서 설계한 하드웨어/통신 아키텍처를 C# 백엔드로 안정적으로 받아내는 구조입니다.

### ① 통신 엔진 (Async / Task 기반)

- UI 스레드와 통신 스레드의 완전한 분리가 필요합니다.

- **추천 라이브러리:** `NModbus4` 또는 `NModbus3` (.NET 4.72 호환)

- `Task.Run()`과 `CancellationTokenSource`를 활용한 비동기 폴링 루프 구축:

C#

```
// 백그라운드 비동기 통신 예시 구조
public async Task StartMonitoringAsync(CancellationToken token)
{
    while (!token.IsCancellationRequested)
    {
        // 1. 13개 차압 센서 일괄 Read (ReadInputRegisters)
        var sensorData = await ReadSensorsAsync();

        // 2. UI 모델 업데이트 (Progress<T> 또는 Dispatcher 활용)
        UpdateUI(sensorData);

        // 3. 제어 순서도(Flow Chart)에 따른 밸브/팬 제어 판단 및 Write
        ExecuteControlLogic(sensorData);

        await Task.Delay(100, token); // 100ms 주기
    }
}
```

### ② 순서도(Flow Chart)의 C# 상태 머신(State Machine)화

- 전달받은 순서도는 `Enum` 기반의 **State Machine 패턴**이나 **Behavior Tree**로 코딩하면 유지보수가 편해집니다.

- `Init` $\rightarrow$ `ZeroCalibration` $\rightarrow$ `AutoControl(PID)` $\rightarrow$ `Interlock/Alarm` 상태 전환을 명확히 구현합니다.

## 3. Phase 5: 현장 튜닝 및 교정을 대비해 미리 구축할 HMI 기능

현장 시운전(Phase 5) 시 엔지니어의 작업 시간을 획기적으로 줄여주는 HMI 전용 툴을 지금 단계에서 준비해 두어야 합니다.

> 1. **센서 영점 교정(Zero-Point Offset) 탭:** 대기압 상태에서 클릭 한 번으로 13개 센서의 Offset 값을 C# 내부에 저장/설정하는 기능
> 
> 2. **수동 제어(Manual Jog/Override) 모드:** 자동 제어 루프를 끄고, 밸브 개도율(%)과 팬 RPM을 수동으로 입력하여 테스트할 수 있는 엔지니어 전용 화면
> 
> 3. **PID 파라미터 실시간 조정창:** P, I, D Gain 값을 프로그램 재시작 없이 실시간 수정 및 적용 가능한 UI
> 
> 4. **고속 데이터 로거 (CSV Export):** 제어 루프 응답성을 분석할 수 있도록 센서값, 밸브 개도율, 팬 RPM을 ms 단위 로그로 저장하는 기능

현재 개발 중이신 HMI의 UI 프레임워크(WPF vs WinForms)는 어떤 것으로 확정되어 있나요? 선택하신 프레임워크에 맞춰 데이터 바인딩 및 실시간 차트 연동 코드 구조를 더 구체적으로 가이드해 드릴 수 있습니다.
