테스트 출력
==========

[xUnit.net 00:00:00.00] xUnit.net VSTest Adapter v2.5.7+8f2703126a (64-bit .NET Framework 4.8.9337.0)
[xUnit.net 00:00:01.62]   Starting:    Esam.Tests
[xUnit.net 00:00:02.52]     Esam.Tests.PlantBehaviorTests.기본_파라미터는_ESAM_목표_운전점을_재현한다 [FAIL]
[xUnit.net 00:00:02.52]       Assert.InRange() Failure: Value not in range
[xUnit.net 00:00:02.52]       Range:  (-210 - -190)
[xUnit.net 00:00:02.52]       Actual: -187.49999999999986
[xUnit.net 00:00:02.52]       Stack Trace:
[xUnit.net 00:00:02.52]            위치: Xunit.Assert.InRange[T](T actual, T low, T high, IComparer`1 comparer) 파일 /_/src/xunit.assert/Asserts/RangeAsserts.cs:줄 57
[xUnit.net 00:00:02.52]            위치: Esam.Tests.PlantBehaviorTests.기본_파라미터는_ESAM_목표_운전점을_재현한다() 파일 C:\PTNC_lib\EFEM_Monitor\src\Esam.Tests\PlantModelTests.cs:줄 287
[xUnit.net 00:00:02.52]     Esam.Tests.ModbusPortWorkerTests.구독자_예외가_폴링을_멈추지_않는다 [FAIL]
[xUnit.net 00:00:02.52]       Assert.True() Failure
[xUnit.net 00:00:02.52]       Expected: True
[xUnit.net 00:00:02.52]       Actual:   False
[xUnit.net 00:00:02.52]       Stack Trace:
[xUnit.net 00:00:02.52]            위치: Xunit.Assert.True(Nullable`1 condition, String userMessage) 파일 /_/src/xunit.assert/Asserts/BooleanAsserts.cs:줄 147
[xUnit.net 00:00:02.52]            위치: Esam.Tests.ModbusPortWorkerTests.구독자_예외가_폴링을_멈추지_않는다() 파일 C:\PTNC_lib\EFEM_Monitor\src\Esam.Tests\PortWorkerTests.cs:줄 765
[xUnit.net 00:00:02.52]     Esam.Tests.ConfigurationTests.기본_설정은_팬_MaxRpm_미확정이라_자동제어에_사용할_수_없다 [FAIL]
[xUnit.net 00:00:02.52]       Assert.False() Failure
[xUnit.net 00:00:02.52]       Expected: False
[xUnit.net 00:00:02.52]       Actual:   True
[xUnit.net 00:00:02.52]       Stack Trace:
[xUnit.net 00:00:02.52]            위치: Xunit.Assert.False(Nullable`1 condition, String userMessage) 파일 /_/src/xunit.assert/Asserts/BooleanAsserts.cs:줄 84
[xUnit.net 00:00:02.52]            위치: Esam.Tests.ConfigurationTests.기본_설정은_팬_MaxRpm_미확정이라_자동제어에_사용할_수_없다() 파일 C:\PTNC_lib\EFEM_Monitor\src\Esam.Tests\UnitConversionTests.cs:줄 181
[xUnit.net 00:00:02.53]     Esam.Tests.InterlockEvaluatorTests.히스테리시스_구간에서는_Auto정책이어도_해제되지_않는다 [FAIL]
[xUnit.net 00:00:02.53]       Assert.False() Failure
[xUnit.net 00:00:02.53]       Expected: False
[xUnit.net 00:00:02.53]       Actual:   True
[xUnit.net 00:00:02.53]       Stack Trace:
[xUnit.net 00:00:02.53]            위치: Xunit.Assert.False(Nullable`1 condition, String userMessage) 파일 /_/src/xunit.assert/Asserts/BooleanAsserts.cs:줄 84
[xUnit.net 00:00:02.53]            위치: Esam.Tests.InterlockEvaluatorTests.히스테리시스_구간에서는_Auto정책이어도_해제되지_않는다() 파일 C:\PTNC_lib\EFEM_Monitor\src\Esam.Tests\InterlockEvaluatorTests.cs:줄 363
[xUnit.net 00:00:02.53]     Esam.Tests.SimulatedTransportTests.통계가_성공률과_응답시간을_집계한다 [FAIL]
[xUnit.net 00:00:02.53]       Assert.Equal() Failure: Values differ
[xUnit.net 00:00:02.53]       Expected: 10
[xUnit.net 00:00:02.53]       Actual:   0
[xUnit.net 00:00:02.53]       Stack Trace:
[xUnit.net 00:00:02.53]            위치: Xunit.Assert.Equal[T](T expected, T actual, IEqualityComparer`1 comparer) 파일 /_/src/xunit.assert/Asserts/EqualityAsserts.cs:줄 148
[xUnit.net 00:00:02.53]            위치: Esam.Tests.SimulatedTransportTests.통계가_성공률과_응답시간을_집계한다() 파일 C:\PTNC_lib\EFEM_Monitor\src\Esam.Tests\SimulatedTransportTests.cs:줄 369
[xUnit.net 00:00:02.54]     Esam.Tests.SimulatedTransportTests.팬_RPM을_설정하고_현재값을_읽는다 [FAIL]
[xUnit.net 00:00:02.54]       Assert.Equal() Failure: Values differ
[xUnit.net 00:00:02.54]       Expected: 2
[xUnit.net 00:00:02.54]       Actual:   0
[xUnit.net 00:00:02.54]       Stack Trace:
[xUnit.net 00:00:02.54]            위치: Xunit.Assert.Equal[T](T expected, T actual, IEqualityComparer`1 comparer) 파일 /_/src/xunit.assert/Asserts/EqualityAsserts.cs:줄 148
[xUnit.net 00:00:02.54]            위치: Esam.Tests.SimulatedTransportTests.팬_RPM을_설정하고_현재값을_읽는다() 파일 C:\PTNC_lib\EFEM_Monitor\src\Esam.Tests\SimulatedTransportTests.cs:줄 252
[xUnit.net 00:00:02.54]     Esam.Tests.ModbusPortWorkerTests.영점_오프셋은_상태_레지스터를_오염시키지_않는다 [FAIL]
[xUnit.net 00:00:02.54]       Assert.True() Failure
[xUnit.net 00:00:02.54]       Expected: True
[xUnit.net 00:00:02.54]       Actual:   False
[xUnit.net 00:00:02.54]       Stack Trace:
[xUnit.net 00:00:02.54]            위치: Xunit.Assert.True(Nullable`1 condition, String userMessage) 파일 /_/src/xunit.assert/Asserts/BooleanAsserts.cs:줄 147
[xUnit.net 00:00:02.54]            위치: Esam.Tests.ModbusPortWorkerTests.영점_오프셋은_상태_레지스터를_오염시키지_않는다() 파일 C:\PTNC_lib\EFEM_Monitor\src\Esam.Tests\PortWorkerTests.cs:줄 818
[xUnit.net 00:00:02.54]     Esam.Tests.SimulatedTransportTests.성공하면_연속실패_카운터가_초기화된다 [FAIL]
[xUnit.net 00:00:02.55]       Assert.Equal() Failure: Values differ
[xUnit.net 00:00:02.55]       Expected: 0
[xUnit.net 00:00:02.55]       Actual:   2
[xUnit.net 00:00:02.55]       Stack Trace:
[xUnit.net 00:00:02.55]            위치: Xunit.Assert.Equal[T](T expected, T actual, IEqualityComparer`1 comparer) 파일 /_/src/xunit.assert/Asserts/EqualityAsserts.cs:줄 148
[xUnit.net 00:00:02.55]            위치: Esam.Tests.SimulatedTransportTests.성공하면_연속실패_카운터가_초기화된다() 파일 C:\PTNC_lib\EFEM_Monitor\src\Esam.Tests\SimulatedTransportTests.cs:줄 384
[xUnit.net 00:00:02.55]     Esam.Tests.ModbusPortWorkerTests.통계가_트랜잭션을_집계한다 [FAIL]
[xUnit.net 00:00:02.55]       Assert.Equal() Failure: Values are not within 6 decimal places
[xUnit.net 00:00:02.55]       Expected: 100 (rounded from 100)
[xUnit.net 00:00:02.55]       Actual:   0 (rounded from 0)
[xUnit.net 00:00:02.55]       Stack Trace:
[xUnit.net 00:00:02.55]            위치: Xunit.Assert.Equal(Double expected, Double actual, Int32 precision) 파일 /_/src/xunit.assert/Asserts/EqualityAsserts.cs:줄 308
[xUnit.net 00:00:02.55]            위치: Esam.Tests.ModbusPortWorkerTests.통계가_트랜잭션을_집계한다() 파일 C:\PTNC_lib\EFEM_Monitor\src\Esam.Tests\PortWorkerTests.cs:줄 787
[xUnit.net 00:00:02.56]     Esam.Tests.ModbusPortWorkerTests.음압을_부호있게_디코딩한다 [FAIL]
[xUnit.net 00:00:02.56]       System.Collections.Generic.KeyNotFoundException : 지정한 키가 사전에 없습니다.
[xUnit.net 00:00:02.56]       Stack Trace:
[xUnit.net 00:00:02.56]            위치: System.ThrowHelper.ThrowKeyNotFoundException()
[xUnit.net 00:00:02.56]            위치: System.Collections.Generic.Dictionary`2.get_Item(TKey key)
[xUnit.net 00:00:02.56]            위치: Esam.Tests.ModbusPortWorkerTests.음압을_부호있게_디코딩한다() 파일 C:\PTNC_lib\EFEM_Monitor\src\Esam.Tests\PortWorkerTests.cs:줄 596
[xUnit.net 00:00:02.56]     Esam.Tests.ModbusPortWorkerTests.연속_실패가_누적되어_통신상실을_판정할_수_있다 [FAIL]
[xUnit.net 00:00:02.56]       Assert.False() Failure
[xUnit.net 00:00:02.56]       Expected: False
[xUnit.net 00:00:02.56]       Actual:   True
[xUnit.net 00:00:02.56]       Stack Trace:
[xUnit.net 00:00:02.56]            위치: Xunit.Assert.False(Nullable`1 condition, String userMessage) 파일 /_/src/xunit.assert/Asserts/BooleanAsserts.cs:줄 84
[xUnit.net 00:00:02.56]            위치: Esam.Tests.ModbusPortWorkerTests.연속_실패가_누적되어_통신상실을_판정할_수_있다() 파일 C:\PTNC_lib\EFEM_Monitor\src\Esam.Tests\PortWorkerTests.cs:줄 639
[xUnit.net 00:00:02.56]     Esam.Tests.ModbusPortWorkerTests.압력값을_공학단위로_디코딩한다 [FAIL]
[xUnit.net 00:00:02.56]       Assert.True() Failure
[xUnit.net 00:00:02.56]       Expected: True
[xUnit.net 00:00:02.56]       Actual:   False
[xUnit.net 00:00:02.56]       Stack Trace:
[xUnit.net 00:00:02.56]            위치: Xunit.Assert.True(Nullable`1 condition, String userMessage) 파일 /_/src/xunit.assert/Asserts/BooleanAsserts.cs:줄 147
[xUnit.net 00:00:02.56]            위치: Esam.Tests.ModbusPortWorkerTests.압력값을_공학단위로_디코딩한다() 파일 C:\PTNC_lib\EFEM_Monitor\src\Esam.Tests\PortWorkerTests.cs:줄 574
[xUnit.net 00:00:02.56]     Esam.Tests.ModbusPortWorkerTests.폴링_완료_이벤트가_발생한다 [FAIL]
[xUnit.net 00:00:02.56]       Assert.True() Failure
[xUnit.net 00:00:02.56]       Expected: True
[xUnit.net 00:00:02.56]       Actual:   False
[xUnit.net 00:00:02.56]       Stack Trace:
[xUnit.net 00:00:02.56]            위치: Xunit.Assert.True(Nullable`1 condition, String userMessage) 파일 /_/src/xunit.assert/Asserts/BooleanAsserts.cs:줄 147
[xUnit.net 00:00:02.56]            위치: Esam.Tests.ModbusPortWorkerTests.폴링_완료_이벤트가_발생한다() 파일 C:\PTNC_lib\EFEM_Monitor\src\Esam.Tests\PortWorkerTests.cs:줄 753
[xUnit.net 00:00:02.57]     Esam.Tests.ModbusPortWorkerTests.영점_오프셋을_적용하면_측정값이_보정된다 [FAIL]
[xUnit.net 00:00:02.57]       System.Collections.Generic.KeyNotFoundException : 지정한 키가 사전에 없습니다.
[xUnit.net 00:00:02.57]       Stack Trace:
[xUnit.net 00:00:02.57]            위치: System.ThrowHelper.ThrowKeyNotFoundException()
[xUnit.net 00:00:02.57]            위치: System.Collections.Generic.Dictionary`2.get_Item(TKey key)
[xUnit.net 00:00:02.57]            위치: Esam.Tests.ModbusPortWorkerTests.영점_오프셋을_적용하면_측정값이_보정된다() 파일 C:\PTNC_lib\EFEM_Monitor\src\Esam.Tests\PortWorkerTests.cs:줄 799
[xUnit.net 00:00:02.58]     Esam.Tests.RecipeConfigTests.압력센서가_아닌_장치는_오류로_막는다 [FAIL]
[xUnit.net 00:00:02.58]       통신 구성 오류:
device-map.json: JSON 파싱 실패: Could not find member 'readTimeoutMs' on object of type 'SerialPortSettings'. Path 'ports[0].serial.readTimeoutMs', line 26, position 24.
[xUnit.net 00:00:02.58]       Stack Trace:
[xUnit.net 00:00:02.58]            위치: Xunit.Assert.True(Nullable`1 condition, String userMessage) 파일 /_/src/xunit.assert/Asserts/BooleanAsserts.cs:줄 147
[xUnit.net 00:00:02.58]            위치: Esam.Tests.RecipeConfigTests.LoadShippedMap() 파일 C:\PTNC_lib\EFEM_Monitor\src\Esam.Tests\RecipeConfigTests.cs:줄 27
[xUnit.net 00:00:02.58]            위치: Esam.Tests.RecipeConfigTests.압력센서가_아닌_장치는_오류로_막는다() 파일 C:\PTNC_lib\EFEM_Monitor\src\Esam.Tests\RecipeConfigTests.cs:줄 210
[xUnit.net 00:00:02.58]     Esam.Tests.RecipeConfigTests.센서_레인지를_벗어난_값은_오류로_막는다 [FAIL]
[xUnit.net 00:00:02.58]       통신 구성 오류:
device-map.json: JSON 파싱 실패: Could not find member 'readTimeoutMs' on object of type 'SerialPortSettings'. Path 'ports[0].serial.readTimeoutMs', line 26, position 24.
[xUnit.net 00:00:02.58]       Stack Trace:
[xUnit.net 00:00:02.58]            위치: Xunit.Assert.True(Nullable`1 condition, String userMessage) 파일 /_/src/xunit.assert/Asserts/BooleanAsserts.cs:줄 147
[xUnit.net 00:00:02.58]            위치: Esam.Tests.RecipeConfigTests.LoadShippedMap() 파일 C:\PTNC_lib\EFEM_Monitor\src\Esam.Tests\RecipeConfigTests.cs:줄 27
[xUnit.net 00:00:02.58]            위치: Esam.Tests.RecipeConfigTests.센서_레인지를_벗어난_값은_오류로_막는다() 파일 C:\PTNC_lib\EFEM_Monitor\src\Esam.Tests\RecipeConfigTests.cs:줄 145
[xUnit.net 00:00:02.58]     Esam.Tests.RecipeConfigTests.통신_구성에_없는_센서는_오류로_막는다 [FAIL]
[xUnit.net 00:00:02.58]       통신 구성 오류:
device-map.json: JSON 파싱 실패: Could not find member 'readTimeoutMs' on object of type 'SerialPortSettings'. Path 'ports[0].serial.readTimeoutMs', line 26, position 24.
[xUnit.net 00:00:02.59]       Stack Trace:
[xUnit.net 00:00:02.59]            위치: Xunit.Assert.True(Nullable`1 condition, String userMessage) 파일 /_/src/xunit.assert/Asserts/BooleanAsserts.cs:줄 147
[xUnit.net 00:00:02.59]            위치: Esam.Tests.RecipeConfigTests.LoadShippedMap() 파일 C:\PTNC_lib\EFEM_Monitor\src\Esam.Tests\RecipeConfigTests.cs:줄 27
[xUnit.net 00:00:02.59]            위치: Esam.Tests.RecipeConfigTests.통신_구성에_없는_센서는_오류로_막는다() 파일 C:\PTNC_lib\EFEM_Monitor\src\Esam.Tests\RecipeConfigTests.cs:줄 129
[xUnit.net 00:00:02.59]     Esam.Tests.RecipeConfigTests.레시피에_빠진_센서는_경고로_알린다 [FAIL]
[xUnit.net 00:00:02.59]       통신 구성 오류:
device-map.json: JSON 파싱 실패: Could not find member 'readTimeoutMs' on object of type 'SerialPortSettings'. Path 'ports[0].serial.readTimeoutMs', line 26, position 24.
[xUnit.net 00:00:02.59]       Stack Trace:
[xUnit.net 00:00:02.59]            위치: Xunit.Assert.True(Nullable`1 condition, String userMessage) 파일 /_/src/xunit.assert/Asserts/BooleanAsserts.cs:줄 147
[xUnit.net 00:00:02.59]            위치: Esam.Tests.RecipeConfigTests.LoadShippedMap() 파일 C:\PTNC_lib\EFEM_Monitor\src\Esam.Tests\RecipeConfigTests.cs:줄 27
[xUnit.net 00:00:02.59]            위치: Esam.Tests.RecipeConfigTests.레시피에_빠진_센서는_경고로_알린다() 파일 C:\PTNC_lib\EFEM_Monitor\src\Esam.Tests\RecipeConfigTests.cs:줄 250
[xUnit.net 00:00:02.59]     Esam.Tests.RecipeConfigTests.ECID_항목_수가_39개로_맞는다 [FAIL]
[xUnit.net 00:00:02.59]       통신 구성 오류:
device-map.json: JSON 파싱 실패: Could not find member 'readTimeoutMs' on object of type 'SerialPortSettings'. Path 'ports[0].serial.readTimeoutMs', line 26, position 24.
[xUnit.net 00:00:02.59]       Stack Trace:
[xUnit.net 00:00:02.59]            위치: Xunit.Assert.True(Nullable`1 condition, String userMessage) 파일 /_/src/xunit.assert/Asserts/BooleanAsserts.cs:줄 147
[xUnit.net 00:00:02.59]            위치: Esam.Tests.RecipeConfigTests.LoadShippedMap() 파일 C:\PTNC_lib\EFEM_Monitor\src\Esam.Tests\RecipeConfigTests.cs:줄 27
[xUnit.net 00:00:02.59]            위치: Esam.Tests.RecipeConfigTests.LoadShipped() 파일 C:\PTNC_lib\EFEM_Monitor\src\Esam.Tests\RecipeConfigTests.cs:줄 34
[xUnit.net 00:00:02.59]            위치: Esam.Tests.RecipeConfigTests.ECID_항목_수가_39개로_맞는다() 파일 C:\PTNC_lib\EFEM_Monitor\src\Esam.Tests\RecipeConfigTests.cs:줄 76
[xUnit.net 00:00:02.60]     Esam.Tests.RecipeConfigTests.저장하고_다시_읽어도_값이_보존된다 [FAIL]
[xUnit.net 00:00:02.60]       통신 구성 오류:
device-map.json: JSON 파싱 실패: Could not find member 'readTimeoutMs' on object of type 'SerialPortSettings'. Path 'ports[0].serial.readTimeoutMs', line 26, position 24.
[xUnit.net 00:00:02.60]       Stack Trace:
[xUnit.net 00:00:02.60]            위치: Xunit.Assert.True(Nullable`1 condition, String userMessage) 파일 /_/src/xunit.assert/Asserts/BooleanAsserts.cs:줄 147
[xUnit.net 00:00:02.60]            위치: Esam.Tests.RecipeConfigTests.LoadShippedMap() 파일 C:\PTNC_lib\EFEM_Monitor\src\Esam.Tests\RecipeConfigTests.cs:줄 27
[xUnit.net 00:00:02.60]            위치: Esam.Tests.RecipeConfigTests.LoadShipped() 파일 C:\PTNC_lib\EFEM_Monitor\src\Esam.Tests\RecipeConfigTests.cs:줄 34
[xUnit.net 00:00:02.60]            위치: Esam.Tests.RecipeConfigTests.저장하고_다시_읽어도_값이_보존된다() 파일 C:\PTNC_lib\EFEM_Monitor\src\Esam.Tests\RecipeConfigTests.cs:줄 278
[xUnit.net 00:00:02.60]     Esam.Tests.RecipeConfigTests.차압센서_13대_전량에_설정값이_있다 [FAIL]
[xUnit.net 00:00:02.60]       통신 구성 오류:
device-map.json: JSON 파싱 실패: Could not find member 'readTimeoutMs' on object of type 'SerialPortSettings'. Path 'ports[0].serial.readTimeoutMs', line 26, position 24.
[xUnit.net 00:00:02.60]       Stack Trace:
[xUnit.net 00:00:02.60]            위치: Xunit.Assert.True(Nullable`1 condition, String userMessage) 파일 /_/src/xunit.assert/Asserts/BooleanAsserts.cs:줄 147
[xUnit.net 00:00:02.60]            위치: Esam.Tests.RecipeConfigTests.LoadShippedMap() 파일 C:\PTNC_lib\EFEM_Monitor\src\Esam.Tests\RecipeConfigTests.cs:줄 27
[xUnit.net 00:00:02.60]            위치: Esam.Tests.RecipeConfigTests.LoadShipped() 파일 C:\PTNC_lib\EFEM_Monitor\src\Esam.Tests\RecipeConfigTests.cs:줄 34
[xUnit.net 00:00:02.60]            위치: Esam.Tests.RecipeConfigTests.차압센서_13대_전량에_설정값이_있다() 파일 C:\PTNC_lib\EFEM_Monitor\src\Esam.Tests\RecipeConfigTests.cs:줄 54
[xUnit.net 00:00:02.60]     Esam.Tests.RecipeConfigTests.배포_레시피에_남는_경고가_없다 [FAIL]
[xUnit.net 00:00:02.60]       통신 구성 오류:
device-map.json: JSON 파싱 실패: Could not find member 'readTimeoutMs' on object of type 'SerialPortSettings'. Path 'ports[0].serial.readTimeoutMs', line 26, position 24.
[xUnit.net 00:00:02.60]       Stack Trace:
[xUnit.net 00:00:02.60]            위치: Xunit.Assert.True(Nullable`1 condition, String userMessage) 파일 /_/src/xunit.assert/Asserts/BooleanAsserts.cs:줄 147
[xUnit.net 00:00:02.60]            위치: Esam.Tests.RecipeConfigTests.LoadShippedMap() 파일 C:\PTNC_lib\EFEM_Monitor\src\Esam.Tests\RecipeConfigTests.cs:줄 27
[xUnit.net 00:00:02.60]            위치: Esam.Tests.RecipeConfigTests.LoadShipped() 파일 C:\PTNC_lib\EFEM_Monitor\src\Esam.Tests\RecipeConfigTests.cs:줄 34
[xUnit.net 00:00:02.60]            위치: Esam.Tests.RecipeConfigTests.배포_레시피에_남는_경고가_없다() 파일 C:\PTNC_lib\EFEM_Monitor\src\Esam.Tests\RecipeConfigTests.cs:줄 86
[xUnit.net 00:00:02.60]     Esam.Tests.RecipeConfigTests.모드별_시간과_합쳐_제어_파라미터가_된다 [FAIL]
[xUnit.net 00:00:02.60]       통신 구성 오류:
device-map.json: JSON 파싱 실패: Could not find member 'readTimeoutMs' on object of type 'SerialPortSettings'. Path 'ports[0].serial.readTimeoutMs', line 26, position 24.
[xUnit.net 00:00:02.60]       Stack Trace:
[xUnit.net 00:00:02.60]            위치: Xunit.Assert.True(Nullable`1 condition, String userMessage) 파일 /_/src/xunit.assert/Asserts/BooleanAsserts.cs:줄 147
[xUnit.net 00:00:02.60]            위치: Esam.Tests.RecipeConfigTests.LoadShippedMap() 파일 C:\PTNC_lib\EFEM_Monitor\src\Esam.Tests\RecipeConfigTests.cs:줄 27
[xUnit.net 00:00:02.60]            위치: Esam.Tests.RecipeConfigTests.LoadShipped() 파일 C:\PTNC_lib\EFEM_Monitor\src\Esam.Tests\RecipeConfigTests.cs:줄 34
[xUnit.net 00:00:02.60]            위치: Esam.Tests.RecipeConfigTests.모드별_시간과_합쳐_제어_파라미터가_된다() 파일 C:\PTNC_lib\EFEM_Monitor\src\Esam.Tests\RecipeConfigTests.cs:줄 96
[xUnit.net 00:00:02.60]     Esam.Tests.RecipeConfigTests.설정이_없는_센서는_null을_반환한다 [FAIL]
[xUnit.net 00:00:02.60]       통신 구성 오류:
device-map.json: JSON 파싱 실패: Could not find member 'readTimeoutMs' on object of type 'SerialPortSettings'. Path 'ports[0].serial.readTimeoutMs', line 26, position 24.
[xUnit.net 00:00:02.60]       Stack Trace:
[xUnit.net 00:00:02.60]            위치: Xunit.Assert.True(Nullable`1 condition, String userMessage) 파일 /_/src/xunit.assert/Asserts/BooleanAsserts.cs:줄 147
[xUnit.net 00:00:02.60]            위치: Esam.Tests.RecipeConfigTests.LoadShippedMap() 파일 C:\PTNC_lib\EFEM_Monitor\src\Esam.Tests\RecipeConfigTests.cs:줄 27
[xUnit.net 00:00:02.60]            위치: Esam.Tests.RecipeConfigTests.LoadShipped() 파일 C:\PTNC_lib\EFEM_Monitor\src\Esam.Tests\RecipeConfigTests.cs:줄 34
[xUnit.net 00:00:02.60]            위치: Esam.Tests.RecipeConfigTests.설정이_없는_센서는_null을_반환한다() 파일 C:\PTNC_lib\EFEM_Monitor\src\Esam.Tests\RecipeConfigTests.cs:줄 112
[xUnit.net 00:00:02.61]     Esam.Tests.RecipeConfigTests.배포용_recipe_json이_통신_구성과_맞물린다 [FAIL]
[xUnit.net 00:00:02.61]       통신 구성 오류:
device-map.json: JSON 파싱 실패: Could not find member 'readTimeoutMs' on object of type 'SerialPortSettings'. Path 'ports[0].serial.readTimeoutMs', line 26, position 24.
[xUnit.net 00:00:02.61]       Stack Trace:
[xUnit.net 00:00:02.61]            위치: Xunit.Assert.True(Nullable`1 condition, String userMessage) 파일 /_/src/xunit.assert/Asserts/BooleanAsserts.cs:줄 147
[xUnit.net 00:00:02.61]            위치: Esam.Tests.RecipeConfigTests.LoadShippedMap() 파일 C:\PTNC_lib\EFEM_Monitor\src\Esam.Tests\RecipeConfigTests.cs:줄 27
[xUnit.net 00:00:02.61]            위치: Esam.Tests.RecipeConfigTests.LoadShipped() 파일 C:\PTNC_lib\EFEM_Monitor\src\Esam.Tests\RecipeConfigTests.cs:줄 34
[xUnit.net 00:00:02.61]            위치: Esam.Tests.RecipeConfigTests.배포용_recipe_json이_통신_구성과_맞물린다() 파일 C:\PTNC_lib\EFEM_Monitor\src\Esam.Tests\RecipeConfigTests.cs:줄 42
[xUnit.net 00:00:02.61]     Esam.Tests.ServicesIntegrationTests.밸브_상태가_pulse와_개도율로_변환된다 [FAIL]
[xUnit.net 00:00:02.61]       Assert.True() Failure
[xUnit.net 00:00:02.61]       Expected: True
[xUnit.net 00:00:02.61]       Actual:   False
[xUnit.net 00:00:02.61]       Stack Trace:
[xUnit.net 00:00:02.61]            위치: Xunit.Assert.True(Nullable`1 condition, String userMessage) 파일 /_/src/xunit.assert/Asserts/BooleanAsserts.cs:줄 147
[xUnit.net 00:00:02.61]            위치: Esam.Tests.ServicesIntegrationTests.밸브_상태가_pulse와_개도율로_변환된다() 파일 C:\PTNC_lib\EFEM_Monitor\src\Esam.Tests\ServicesIntegrationTests.cs:줄 508
========== 테스트 실행이 중단됨: < 1ms 동안 테스트 320개가 실행됨(294개 통과, 26개 실패, 0개 건너뜀) ==========
========== 테스트 실행을 시작하는 중 ==========
[xUnit.net 00:00:00.00] xUnit.net VSTest Adapter v2.5.7+8f2703126a (64-bit .NET Framework 4.8.9337.0)
[xUnit.net 00:00:01.84]   Starting:    Esam.Tests
[xUnit.net 00:00:02.63]     Esam.Tests.SimulatedTransportTests.통계가_성공률과_응답시간을_집계한다 [FAIL]
[xUnit.net 00:00:02.63]       Assert.Equal() Failure: Values differ
[xUnit.net 00:00:02.64]       Expected: 10
[xUnit.net 00:00:02.64]       Actual:   0
[xUnit.net 00:00:02.64]       Stack Trace:
[xUnit.net 00:00:02.64]            위치: Xunit.Assert.Equal[T](T expected, T actual, IEqualityComparer`1 comparer) 파일 /_/src/xunit.assert/Asserts/EqualityAsserts.cs:줄 148
[xUnit.net 00:00:02.64]            위치: Esam.Tests.SimulatedTransportTests.통계가_성공률과_응답시간을_집계한다() 파일 C:\PTNC_lib\EFEM_Monitor\src\Esam.Tests\SimulatedTransportTests.cs:줄 369
[xUnit.net 00:00:02.64]     Esam.Tests.ConfigurationTests.기본_설정은_팬_MaxRpm_미확정이라_자동제어에_사용할_수_없다 [FAIL]
[xUnit.net 00:00:02.64]       Assert.False() Failure
[xUnit.net 00:00:02.64]       Expected: False
[xUnit.net 00:00:02.64]       Actual:   True
[xUnit.net 00:00:02.64]       Stack Trace:
[xUnit.net 00:00:02.64]            위치: Xunit.Assert.False(Nullable`1 condition, String userMessage) 파일 /_/src/xunit.assert/Asserts/BooleanAsserts.cs:줄 84
[xUnit.net 00:00:02.64]            위치: Esam.Tests.ConfigurationTests.기본_설정은_팬_MaxRpm_미확정이라_자동제어에_사용할_수_없다() 파일 C:\PTNC_lib\EFEM_Monitor\src\Esam.Tests\UnitConversionTests.cs:줄 181
[xUnit.net 00:00:02.64]     Esam.Tests.PlantBehaviorTests.기본_파라미터는_ESAM_목표_운전점을_재현한다 [FAIL]
[xUnit.net 00:00:02.64]       Assert.InRange() Failure: Value not in range
[xUnit.net 00:00:02.64]       Range:  (-210 - -190)
[xUnit.net 00:00:02.64]       Actual: -187.49999999999986
[xUnit.net 00:00:02.64]       Stack Trace:
[xUnit.net 00:00:02.64]            위치: Xunit.Assert.InRange[T](T actual, T low, T high, IComparer`1 comparer) 파일 /_/src/xunit.assert/Asserts/RangeAsserts.cs:줄 57
[xUnit.net 00:00:02.64]            위치: Esam.Tests.PlantBehaviorTests.기본_파라미터는_ESAM_목표_운전점을_재현한다() 파일 C:\PTNC_lib\EFEM_Monitor\src\Esam.Tests\PlantModelTests.cs:줄 287
[xUnit.net 00:00:02.64]     Esam.Tests.InterlockEvaluatorTests.히스테리시스_구간에서는_Auto정책이어도_해제되지_않는다 [FAIL]
[xUnit.net 00:00:02.64]       Assert.False() Failure
[xUnit.net 00:00:02.64]       Expected: False
[xUnit.net 00:00:02.64]       Actual:   True
[xUnit.net 00:00:02.64]       Stack Trace:
[xUnit.net 00:00:02.64]            위치: Xunit.Assert.False(Nullable`1 condition, String userMessage) 파일 /_/src/xunit.assert/Asserts/BooleanAsserts.cs:줄 84
[xUnit.net 00:00:02.64]            위치: Esam.Tests.InterlockEvaluatorTests.히스테리시스_구간에서는_Auto정책이어도_해제되지_않는다() 파일 C:\PTNC_lib\EFEM_Monitor\src\Esam.Tests\InterlockEvaluatorTests.cs:줄 363
[xUnit.net 00:00:02.64]     Esam.Tests.ModbusPortWorkerTests.구독자_예외가_폴링을_멈추지_않는다 [FAIL]
[xUnit.net 00:00:02.64]       Assert.True() Failure
[xUnit.net 00:00:02.64]       Expected: True
[xUnit.net 00:00:02.64]       Actual:   False
[xUnit.net 00:00:02.64]       Stack Trace:
[xUnit.net 00:00:02.64]            위치: Xunit.Assert.True(Nullable`1 condition, String userMessage) 파일 /_/src/xunit.assert/Asserts/BooleanAsserts.cs:줄 147
[xUnit.net 00:00:02.64]            위치: Esam.Tests.ModbusPortWorkerTests.구독자_예외가_폴링을_멈추지_않는다() 파일 C:\PTNC_lib\EFEM_Monitor\src\Esam.Tests\PortWorkerTests.cs:줄 765
[xUnit.net 00:00:02.65]     Esam.Tests.SimulatedTransportTests.팬_RPM을_설정하고_현재값을_읽는다 [FAIL]
[xUnit.net 00:00:02.65]       Assert.Equal() Failure: Values differ
[xUnit.net 00:00:02.65]       Expected: 2
[xUnit.net 00:00:02.65]       Actual:   0
[xUnit.net 00:00:02.65]       Stack Trace:
[xUnit.net 00:00:02.65]            위치: Xunit.Assert.Equal[T](T expected, T actual, IEqualityComparer`1 comparer) 파일 /_/src/xunit.assert/Asserts/EqualityAsserts.cs:줄 148
[xUnit.net 00:00:02.65]            위치: Esam.Tests.SimulatedTransportTests.팬_RPM을_설정하고_현재값을_읽는다() 파일 C:\PTNC_lib\EFEM_Monitor\src\Esam.Tests\SimulatedTransportTests.cs:줄 252
[xUnit.net 00:00:02.66]     Esam.Tests.SimulatedTransportTests.성공하면_연속실패_카운터가_초기화된다 [FAIL]
[xUnit.net 00:00:02.66]       Assert.Equal() Failure: Values differ
[xUnit.net 00:00:02.66]       Expected: 0
[xUnit.net 00:00:02.66]       Actual:   2
[xUnit.net 00:00:02.66]       Stack Trace:
[xUnit.net 00:00:02.66]            위치: Xunit.Assert.Equal[T](T expected, T actual, IEqualityComparer`1 comparer) 파일 /_/src/xunit.assert/Asserts/EqualityAsserts.cs:줄 148
[xUnit.net 00:00:02.66]            위치: Esam.Tests.SimulatedTransportTests.성공하면_연속실패_카운터가_초기화된다() 파일 C:\PTNC_lib\EFEM_Monitor\src\Esam.Tests\SimulatedTransportTests.cs:줄 384
[xUnit.net 00:00:02.66]     Esam.Tests.ModbusPortWorkerTests.영점_오프셋은_상태_레지스터를_오염시키지_않는다 [FAIL]
[xUnit.net 00:00:02.66]       Assert.True() Failure
[xUnit.net 00:00:02.66]       Expected: True
[xUnit.net 00:00:02.66]       Actual:   False
[xUnit.net 00:00:02.66]       Stack Trace:
[xUnit.net 00:00:02.66]            위치: Xunit.Assert.True(Nullable`1 condition, String userMessage) 파일 /_/src/xunit.assert/Asserts/BooleanAsserts.cs:줄 147
[xUnit.net 00:00:02.66]            위치: Esam.Tests.ModbusPortWorkerTests.영점_오프셋은_상태_레지스터를_오염시키지_않는다() 파일 C:\PTNC_lib\EFEM_Monitor\src\Esam.Tests\PortWorkerTests.cs:줄 818
[xUnit.net 00:00:02.67]     Esam.Tests.ModbusPortWorkerTests.통계가_트랜잭션을_집계한다 [FAIL]
[xUnit.net 00:00:02.67]       Assert.Equal() Failure: Values are not within 6 decimal places
[xUnit.net 00:00:02.67]       Expected: 100 (rounded from 100)
[xUnit.net 00:00:02.67]       Actual:   0 (rounded from 0)
[xUnit.net 00:00:02.67]       Stack Trace:
[xUnit.net 00:00:02.67]            위치: Xunit.Assert.Equal(Double expected, Double actual, Int32 precision) 파일 /_/src/xunit.assert/Asserts/EqualityAsserts.cs:줄 308
[xUnit.net 00:00:02.67]            위치: Esam.Tests.ModbusPortWorkerTests.통계가_트랜잭션을_집계한다() 파일 C:\PTNC_lib\EFEM_Monitor\src\Esam.Tests\PortWorkerTests.cs:줄 787
[xUnit.net 00:00:02.67]     Esam.Tests.ModbusPortWorkerTests.음압을_부호있게_디코딩한다 [FAIL]
[xUnit.net 00:00:02.67]       System.Collections.Generic.KeyNotFoundException : 지정한 키가 사전에 없습니다.
[xUnit.net 00:00:02.67]       Stack Trace:
[xUnit.net 00:00:02.67]            위치: System.ThrowHelper.ThrowKeyNotFoundException()
[xUnit.net 00:00:02.67]            위치: System.Collections.Generic.Dictionary`2.get_Item(TKey key)
[xUnit.net 00:00:02.67]            위치: Esam.Tests.ModbusPortWorkerTests.음압을_부호있게_디코딩한다() 파일 C:\PTNC_lib\EFEM_Monitor\src\Esam.Tests\PortWorkerTests.cs:줄 596
[xUnit.net 00:00:02.67]     Esam.Tests.ModbusPortWorkerTests.연속_실패가_누적되어_통신상실을_판정할_수_있다 [FAIL]
[xUnit.net 00:00:02.67]       Assert.False() Failure
[xUnit.net 00:00:02.67]       Expected: False
[xUnit.net 00:00:02.67]       Actual:   True
[xUnit.net 00:00:02.67]       Stack Trace:
[xUnit.net 00:00:02.67]            위치: Xunit.Assert.False(Nullable`1 condition, String userMessage) 파일 /_/src/xunit.assert/Asserts/BooleanAsserts.cs:줄 84
[xUnit.net 00:00:02.67]            위치: Esam.Tests.ModbusPortWorkerTests.연속_실패가_누적되어_통신상실을_판정할_수_있다() 파일 C:\PTNC_lib\EFEM_Monitor\src\Esam.Tests\PortWorkerTests.cs:줄 639
[xUnit.net 00:00:02.67]     Esam.Tests.ModbusPortWorkerTests.압력값을_공학단위로_디코딩한다 [FAIL]
[xUnit.net 00:00:02.67]       Assert.True() Failure
[xUnit.net 00:00:02.67]       Expected: True
[xUnit.net 00:00:02.67]       Actual:   False
[xUnit.net 00:00:02.67]       Stack Trace:
[xUnit.net 00:00:02.67]            위치: Xunit.Assert.True(Nullable`1 condition, String userMessage) 파일 /_/src/xunit.assert/Asserts/BooleanAsserts.cs:줄 147
[xUnit.net 00:00:02.67]            위치: Esam.Tests.ModbusPortWorkerTests.압력값을_공학단위로_디코딩한다() 파일 C:\PTNC_lib\EFEM_Monitor\src\Esam.Tests\PortWorkerTests.cs:줄 574
[xUnit.net 00:00:02.68]     Esam.Tests.ModbusPortWorkerTests.폴링_완료_이벤트가_발생한다 [FAIL]
[xUnit.net 00:00:02.68]       Assert.True() Failure
[xUnit.net 00:00:02.68]       Expected: True
[xUnit.net 00:00:02.68]       Actual:   False
[xUnit.net 00:00:02.68]       Stack Trace:
[xUnit.net 00:00:02.68]            위치: Xunit.Assert.True(Nullable`1 condition, String userMessage) 파일 /_/src/xunit.assert/Asserts/BooleanAsserts.cs:줄 147
[xUnit.net 00:00:02.68]            위치: Esam.Tests.ModbusPortWorkerTests.폴링_완료_이벤트가_발생한다() 파일 C:\PTNC_lib\EFEM_Monitor\src\Esam.Tests\PortWorkerTests.cs:줄 753
[xUnit.net 00:00:02.68]     Esam.Tests.ModbusPortWorkerTests.영점_오프셋을_적용하면_측정값이_보정된다 [FAIL]
[xUnit.net 00:00:02.68]       System.Collections.Generic.KeyNotFoundException : 지정한 키가 사전에 없습니다.
[xUnit.net 00:00:02.68]       Stack Trace:
[xUnit.net 00:00:02.68]            위치: System.ThrowHelper.ThrowKeyNotFoundException()
[xUnit.net 00:00:02.68]            위치: System.Collections.Generic.Dictionary`2.get_Item(TKey key)
[xUnit.net 00:00:02.68]            위치: Esam.Tests.ModbusPortWorkerTests.영점_오프셋을_적용하면_측정값이_보정된다() 파일 C:\PTNC_lib\EFEM_Monitor\src\Esam.Tests\PortWorkerTests.cs:줄 799
[xUnit.net 00:00:02.69]     Esam.Tests.RecipeConfigTests.압력센서가_아닌_장치는_오류로_막는다 [FAIL]
[xUnit.net 00:00:02.69]       통신 구성 오류:
device-map.json: JSON 파싱 실패: Could not find member 'readTimeoutMs' on object of type 'SerialPortSettings'. Path 'ports[0].serial.readTimeoutMs', line 26, position 24.
[xUnit.net 00:00:02.69]       Stack Trace:
[xUnit.net 00:00:02.69]            위치: Xunit.Assert.True(Nullable`1 condition, String userMessage) 파일 /_/src/xunit.assert/Asserts/BooleanAsserts.cs:줄 147
[xUnit.net 00:00:02.69]            위치: Esam.Tests.RecipeConfigTests.LoadShippedMap() 파일 C:\PTNC_lib\EFEM_Monitor\src\Esam.Tests\RecipeConfigTests.cs:줄 27
[xUnit.net 00:00:02.69]            위치: Esam.Tests.RecipeConfigTests.압력센서가_아닌_장치는_오류로_막는다() 파일 C:\PTNC_lib\EFEM_Monitor\src\Esam.Tests\RecipeConfigTests.cs:줄 210
[xUnit.net 00:00:02.70]     Esam.Tests.RecipeConfigTests.센서_레인지를_벗어난_값은_오류로_막는다 [FAIL]
[xUnit.net 00:00:02.70]       통신 구성 오류:
device-map.json: JSON 파싱 실패: Could not find member 'readTimeoutMs' on object of type 'SerialPortSettings'. Path 'ports[0].serial.readTimeoutMs', line 26, position 24.
[xUnit.net 00:00:02.70]       Stack Trace:
[xUnit.net 00:00:02.70]            위치: Xunit.Assert.True(Nullable`1 condition, String userMessage) 파일 /_/src/xunit.assert/Asserts/BooleanAsserts.cs:줄 147
[xUnit.net 00:00:02.70]            위치: Esam.Tests.RecipeConfigTests.LoadShippedMap() 파일 C:\PTNC_lib\EFEM_Monitor\src\Esam.Tests\RecipeConfigTests.cs:줄 27
[xUnit.net 00:00:02.70]            위치: Esam.Tests.RecipeConfigTests.센서_레인지를_벗어난_값은_오류로_막는다() 파일 C:\PTNC_lib\EFEM_Monitor\src\Esam.Tests\RecipeConfigTests.cs:줄 145
[xUnit.net 00:00:02.70]     Esam.Tests.RecipeConfigTests.통신_구성에_없는_센서는_오류로_막는다 [FAIL]
[xUnit.net 00:00:02.70]       통신 구성 오류:
device-map.json: JSON 파싱 실패: Could not find member 'readTimeoutMs' on object of type 'SerialPortSettings'. Path 'ports[0].serial.readTimeoutMs', line 26, position 24.
[xUnit.net 00:00:02.70]       Stack Trace:
[xUnit.net 00:00:02.70]            위치: Xunit.Assert.True(Nullable`1 condition, String userMessage) 파일 /_/src/xunit.assert/Asserts/BooleanAsserts.cs:줄 147
[xUnit.net 00:00:02.70]            위치: Esam.Tests.RecipeConfigTests.LoadShippedMap() 파일 C:\PTNC_lib\EFEM_Monitor\src\Esam.Tests\RecipeConfigTests.cs:줄 27
[xUnit.net 00:00:02.70]            위치: Esam.Tests.RecipeConfigTests.통신_구성에_없는_센서는_오류로_막는다() 파일 C:\PTNC_lib\EFEM_Monitor\src\Esam.Tests\RecipeConfigTests.cs:줄 129
[xUnit.net 00:00:02.71]     Esam.Tests.RecipeConfigTests.레시피에_빠진_센서는_경고로_알린다 [FAIL]
[xUnit.net 00:00:02.71]       통신 구성 오류:
device-map.json: JSON 파싱 실패: Could not find member 'readTimeoutMs' on object of type 'SerialPortSettings'. Path 'ports[0].serial.readTimeoutMs', line 26, position 24.
[xUnit.net 00:00:02.71]       Stack Trace:
[xUnit.net 00:00:02.71]            위치: Xunit.Assert.True(Nullable`1 condition, String userMessage) 파일 /_/src/xunit.assert/Asserts/BooleanAsserts.cs:줄 147
[xUnit.net 00:00:02.71]            위치: Esam.Tests.RecipeConfigTests.LoadShippedMap() 파일 C:\PTNC_lib\EFEM_Monitor\src\Esam.Tests\RecipeConfigTests.cs:줄 27
[xUnit.net 00:00:02.71]            위치: Esam.Tests.RecipeConfigTests.레시피에_빠진_센서는_경고로_알린다() 파일 C:\PTNC_lib\EFEM_Monitor\src\Esam.Tests\RecipeConfigTests.cs:줄 250
[xUnit.net 00:00:02.71]     Esam.Tests.RecipeConfigTests.ECID_항목_수가_39개로_맞는다 [FAIL]
[xUnit.net 00:00:02.71]       통신 구성 오류:
device-map.json: JSON 파싱 실패: Could not find member 'readTimeoutMs' on object of type 'SerialPortSettings'. Path 'ports[0].serial.readTimeoutMs', line 26, position 24.
[xUnit.net 00:00:02.71]       Stack Trace:
[xUnit.net 00:00:02.71]            위치: Xunit.Assert.True(Nullable`1 condition, String userMessage) 파일 /_/src/xunit.assert/Asserts/BooleanAsserts.cs:줄 147
[xUnit.net 00:00:02.71]            위치: Esam.Tests.RecipeConfigTests.LoadShippedMap() 파일 C:\PTNC_lib\EFEM_Monitor\src\Esam.Tests\RecipeConfigTests.cs:줄 27
[xUnit.net 00:00:02.71]            위치: Esam.Tests.RecipeConfigTests.LoadShipped() 파일 C:\PTNC_lib\EFEM_Monitor\src\Esam.Tests\RecipeConfigTests.cs:줄 34
[xUnit.net 00:00:02.71]            위치: Esam.Tests.RecipeConfigTests.ECID_항목_수가_39개로_맞는다() 파일 C:\PTNC_lib\EFEM_Monitor\src\Esam.Tests\RecipeConfigTests.cs:줄 76
[xUnit.net 00:00:02.72]     Esam.Tests.RecipeConfigTests.저장하고_다시_읽어도_값이_보존된다 [FAIL]
[xUnit.net 00:00:02.72]       통신 구성 오류:
device-map.json: JSON 파싱 실패: Could not find member 'readTimeoutMs' on object of type 'SerialPortSettings'. Path 'ports[0].serial.readTimeoutMs', line 26, position 24.
[xUnit.net 00:00:02.72]       Stack Trace:
[xUnit.net 00:00:02.72]            위치: Xunit.Assert.True(Nullable`1 condition, String userMessage) 파일 /_/src/xunit.assert/Asserts/BooleanAsserts.cs:줄 147
[xUnit.net 00:00:02.72]            위치: Esam.Tests.RecipeConfigTests.LoadShippedMap() 파일 C:\PTNC_lib\EFEM_Monitor\src\Esam.Tests\RecipeConfigTests.cs:줄 27
[xUnit.net 00:00:02.72]            위치: Esam.Tests.RecipeConfigTests.LoadShipped() 파일 C:\PTNC_lib\EFEM_Monitor\src\Esam.Tests\RecipeConfigTests.cs:줄 34
[xUnit.net 00:00:02.72]            위치: Esam.Tests.RecipeConfigTests.저장하고_다시_읽어도_값이_보존된다() 파일 C:\PTNC_lib\EFEM_Monitor\src\Esam.Tests\RecipeConfigTests.cs:줄 278
[xUnit.net 00:00:02.72]     Esam.Tests.RecipeConfigTests.차압센서_13대_전량에_설정값이_있다 [FAIL]
[xUnit.net 00:00:02.72]       통신 구성 오류:
device-map.json: JSON 파싱 실패: Could not find member 'readTimeoutMs' on object of type 'SerialPortSettings'. Path 'ports[0].serial.readTimeoutMs', line 26, position 24.
[xUnit.net 00:00:02.72]       Stack Trace:
[xUnit.net 00:00:02.72]            위치: Xunit.Assert.True(Nullable`1 condition, String userMessage) 파일 /_/src/xunit.assert/Asserts/BooleanAsserts.cs:줄 147
[xUnit.net 00:00:02.72]            위치: Esam.Tests.RecipeConfigTests.LoadShippedMap() 파일 C:\PTNC_lib\EFEM_Monitor\src\Esam.Tests\RecipeConfigTests.cs:줄 27
[xUnit.net 00:00:02.72]            위치: Esam.Tests.RecipeConfigTests.LoadShipped() 파일 C:\PTNC_lib\EFEM_Monitor\src\Esam.Tests\RecipeConfigTests.cs:줄 34
[xUnit.net 00:00:02.72]            위치: Esam.Tests.RecipeConfigTests.차압센서_13대_전량에_설정값이_있다() 파일 C:\PTNC_lib\EFEM_Monitor\src\Esam.Tests\RecipeConfigTests.cs:줄 54
[xUnit.net 00:00:02.72]     Esam.Tests.RecipeConfigTests.배포_레시피에_남는_경고가_없다 [FAIL]
[xUnit.net 00:00:02.72]       통신 구성 오류:
device-map.json: JSON 파싱 실패: Could not find member 'readTimeoutMs' on object of type 'SerialPortSettings'. Path 'ports[0].serial.readTimeoutMs', line 26, position 24.
[xUnit.net 00:00:02.72]       Stack Trace:
[xUnit.net 00:00:02.72]            위치: Xunit.Assert.True(Nullable`1 condition, String userMessage) 파일 /_/src/xunit.assert/Asserts/BooleanAsserts.cs:줄 147
[xUnit.net 00:00:02.72]            위치: Esam.Tests.RecipeConfigTests.LoadShippedMap() 파일 C:\PTNC_lib\EFEM_Monitor\src\Esam.Tests\RecipeConfigTests.cs:줄 27
[xUnit.net 00:00:02.72]            위치: Esam.Tests.RecipeConfigTests.LoadShipped() 파일 C:\PTNC_lib\EFEM_Monitor\src\Esam.Tests\RecipeConfigTests.cs:줄 34
[xUnit.net 00:00:02.72]            위치: Esam.Tests.RecipeConfigTests.배포_레시피에_남는_경고가_없다() 파일 C:\PTNC_lib\EFEM_Monitor\src\Esam.Tests\RecipeConfigTests.cs:줄 86
[xUnit.net 00:00:02.72]     Esam.Tests.RecipeConfigTests.모드별_시간과_합쳐_제어_파라미터가_된다 [FAIL]
[xUnit.net 00:00:02.72]       통신 구성 오류:
device-map.json: JSON 파싱 실패: Could not find member 'readTimeoutMs' on object of type 'SerialPortSettings'. Path 'ports[0].serial.readTimeoutMs', line 26, position 24.
[xUnit.net 00:00:02.72]       Stack Trace:
[xUnit.net 00:00:02.72]            위치: Xunit.Assert.True(Nullable`1 condition, String userMessage) 파일 /_/src/xunit.assert/Asserts/BooleanAsserts.cs:줄 147
[xUnit.net 00:00:02.72]            위치: Esam.Tests.RecipeConfigTests.LoadShippedMap() 파일 C:\PTNC_lib\EFEM_Monitor\src\Esam.Tests\RecipeConfigTests.cs:줄 27
[xUnit.net 00:00:02.72]            위치: Esam.Tests.RecipeConfigTests.LoadShipped() 파일 C:\PTNC_lib\EFEM_Monitor\src\Esam.Tests\RecipeConfigTests.cs:줄 34
[xUnit.net 00:00:02.72]            위치: Esam.Tests.RecipeConfigTests.모드별_시간과_합쳐_제어_파라미터가_된다() 파일 C:\PTNC_lib\EFEM_Monitor\src\Esam.Tests\RecipeConfigTests.cs:줄 96
[xUnit.net 00:00:02.73]     Esam.Tests.RecipeConfigTests.설정이_없는_센서는_null을_반환한다 [FAIL]
[xUnit.net 00:00:02.73]       통신 구성 오류:
device-map.json: JSON 파싱 실패: Could not find member 'readTimeoutMs' on object of type 'SerialPortSettings'. Path 'ports[0].serial.readTimeoutMs', line 26, position 24.
[xUnit.net 00:00:02.73]       Stack Trace:
[xUnit.net 00:00:02.73]            위치: Xunit.Assert.True(Nullable`1 condition, String userMessage) 파일 /_/src/xunit.assert/Asserts/BooleanAsserts.cs:줄 147
[xUnit.net 00:00:02.73]            위치: Esam.Tests.RecipeConfigTests.LoadShippedMap() 파일 C:\PTNC_lib\EFEM_Monitor\src\Esam.Tests\RecipeConfigTests.cs:줄 27
[xUnit.net 00:00:02.73]            위치: Esam.Tests.RecipeConfigTests.LoadShipped() 파일 C:\PTNC_lib\EFEM_Monitor\src\Esam.Tests\RecipeConfigTests.cs:줄 34
[xUnit.net 00:00:02.73]            위치: Esam.Tests.RecipeConfigTests.설정이_없는_센서는_null을_반환한다() 파일 C:\PTNC_lib\EFEM_Monitor\src\Esam.Tests\RecipeConfigTests.cs:줄 112
[xUnit.net 00:00:02.73]     Esam.Tests.RecipeConfigTests.배포용_recipe_json이_통신_구성과_맞물린다 [FAIL]
[xUnit.net 00:00:02.73]       통신 구성 오류:
device-map.json: JSON 파싱 실패: Could not find member 'readTimeoutMs' on object of type 'SerialPortSettings'. Path 'ports[0].serial.readTimeoutMs', line 26, position 24.
[xUnit.net 00:00:02.73]       Stack Trace:
[xUnit.net 00:00:02.73]            위치: Xunit.Assert.True(Nullable`1 condition, String userMessage) 파일 /_/src/xunit.assert/Asserts/BooleanAsserts.cs:줄 147
[xUnit.net 00:00:02.73]            위치: Esam.Tests.RecipeConfigTests.LoadShippedMap() 파일 C:\PTNC_lib\EFEM_Monitor\src\Esam.Tests\RecipeConfigTests.cs:줄 27
[xUnit.net 00:00:02.73]            위치: Esam.Tests.RecipeConfigTests.LoadShipped() 파일 C:\PTNC_lib\EFEM_Monitor\src\Esam.Tests\RecipeConfigTests.cs:줄 34
[xUnit.net 00:00:02.73]            위치: Esam.Tests.RecipeConfigTests.배포용_recipe_json이_통신_구성과_맞물린다() 파일 C:\PTNC_lib\EFEM_Monitor\src\Esam.Tests\RecipeConfigTests.cs:줄 42
[xUnit.net 00:00:02.73]     Esam.Tests.ServicesIntegrationTests.밸브_상태가_pulse와_개도율로_변환된다 [FAIL]
[xUnit.net 00:00:02.73]       Assert.True() Failure
[xUnit.net 00:00:02.73]       Expected: True
[xUnit.net 00:00:02.73]       Actual:   False
[xUnit.net 00:00:02.73]       Stack Trace:
[xUnit.net 00:00:02.73]            위치: Xunit.Assert.True(Nullable`1 condition, String userMessage) 파일 /_/src/xunit.assert/Asserts/BooleanAsserts.cs:줄 147
[xUnit.net 00:00:02.73]            위치: Esam.Tests.ServicesIntegrationTests.밸브_상태가_pulse와_개도율로_변환된다() 파일 C:\PTNC_lib\EFEM_Monitor\src\Esam.Tests\ServicesIntegrationTests.cs:줄 508
========== 테스트 실행이 중단됨: < 1ms 동안 테스트 320개가 실행됨(294개 통과, 26개 실패, 0개 건너뜀) ==========
테스트 프로젝트 구축
요청한 테스트 실행에 대한 테스트 검색을 시작하는 중
========== 테스트 검색을 시작하는 중 ==========
[xUnit.net 00:00:00.00] xUnit.net VSTest Adapter v2.5.7+8f2703126a (64-bit .NET Framework 4.8.9337.0)
[xUnit.net 00:00:02.33]   Discovering: Esam.Tests
[xUnit.net 00:00:02.56]   Discovered:  Esam.Tests
========== 테스트 검색이 완료됨: 4.5초 동안 테스트 376개를 찾음 ==========
네임스페이스의 모든 테스트 실행: Esam.Tests
========== 테스트 실행을 시작하는 중 ==========
[xUnit.net 00:00:00.00] xUnit.net VSTest Adapter v2.5.7+8f2703126a (64-bit .NET Framework 4.8.9337.0)
[xUnit.net 00:00:02.37]   Starting:    Esam.Tests
[xUnit.net 00:11:29.99]     Esam.Tests.SimulatedTransportTests.통계가_성공률과_응답시간을_집계한다 [FAIL]
[xUnit.net 00:11:30.00]       Assert.Equal() Failure: Values differ
[xUnit.net 00:11:30.00]       Expected: 10
[xUnit.net 00:11:30.00]       Actual:   0
[xUnit.net 00:11:30.00]       Stack Trace:
[xUnit.net 00:11:30.00]            위치: Xunit.Assert.Equal[T](T expected, T actual, IEqualityComparer`1 comparer) 파일 /_/src/xunit.assert/Asserts/EqualityAsserts.cs:줄 148
[xUnit.net 00:11:30.00]            위치: Esam.Tests.SimulatedTransportTests.통계가_성공률과_응답시간을_집계한다() 파일 C:\PTNC_lib\EFEM_Monitor\src\Esam.Tests\SimulatedTransportTests.cs:줄 369
[xUnit.net 00:11:30.00]     Esam.Tests.ConfigurationTests.기본_설정은_팬_MaxRpm_미확정이라_자동제어에_사용할_수_없다 [FAIL]
[xUnit.net 00:11:30.00]       Assert.False() Failure
[xUnit.net 00:11:30.00]       Expected: False
[xUnit.net 00:11:30.00]       Actual:   True
[xUnit.net 00:11:30.00]       Stack Trace:
[xUnit.net 00:11:30.00]            위치: Xunit.Assert.False(Nullable`1 condition, String userMessage) 파일 /_/src/xunit.assert/Asserts/BooleanAsserts.cs:줄 84
[xUnit.net 00:11:30.00]            위치: Esam.Tests.ConfigurationTests.기본_설정은_팬_MaxRpm_미확정이라_자동제어에_사용할_수_없다() 파일 C:\PTNC_lib\EFEM_Monitor\src\Esam.Tests\UnitConversionTests.cs:줄 181
[xUnit.net 00:11:30.00]     Esam.Tests.InterlockEvaluatorTests.히스테리시스_구간에서는_Auto정책이어도_해제되지_않는다 [FAIL]
[xUnit.net 00:11:30.00]       Assert.False() Failure
[xUnit.net 00:11:30.00]       Expected: False
[xUnit.net 00:11:30.00]       Actual:   True
[xUnit.net 00:11:30.00]       Stack Trace:
[xUnit.net 00:11:30.00]            위치: Xunit.Assert.False(Nullable`1 condition, String userMessage) 파일 /_/src/xunit.assert/Asserts/BooleanAsserts.cs:줄 84
[xUnit.net 00:11:30.00]            위치: Esam.Tests.InterlockEvaluatorTests.히스테리시스_구간에서는_Auto정책이어도_해제되지_않는다() 파일 C:\PTNC_lib\EFEM_Monitor\src\Esam.Tests\InterlockEvaluatorTests.cs:줄 363
[xUnit.net 00:11:30.00]     Esam.Tests.ModbusPortWorkerTests.구독자_예외가_폴링을_멈추지_않는다 [FAIL]
[xUnit.net 00:11:30.00]       Assert.True() Failure
[xUnit.net 00:11:30.00]       Expected: True
[xUnit.net 00:11:30.00]       Actual:   False
[xUnit.net 00:11:30.00]       Stack Trace:
[xUnit.net 00:11:30.00]            위치: Xunit.Assert.True(Nullable`1 condition, String userMessage) 파일 /_/src/xunit.assert/Asserts/BooleanAsserts.cs:줄 147
[xUnit.net 00:11:30.00]            위치: Esam.Tests.ModbusPortWorkerTests.구독자_예외가_폴링을_멈추지_않는다() 파일 C:\PTNC_lib\EFEM_Monitor\src\Esam.Tests\PortWorkerTests.cs:줄 765
[xUnit.net 00:11:30.00]     Esam.Tests.PlantBehaviorTests.기본_파라미터는_ESAM_목표_운전점을_재현한다 [FAIL]
[xUnit.net 00:11:30.00]       Assert.InRange() Failure: Value not in range
[xUnit.net 00:11:30.00]       Range:  (-210 - -190)
[xUnit.net 00:11:30.00]       Actual: -187.49999999999986
[xUnit.net 00:11:30.00]       Stack Trace:
[xUnit.net 00:11:30.00]            위치: Xunit.Assert.InRange[T](T actual, T low, T high, IComparer`1 comparer) 파일 /_/src/xunit.assert/Asserts/RangeAsserts.cs:줄 57
[xUnit.net 00:11:30.00]            위치: Esam.Tests.PlantBehaviorTests.기본_파라미터는_ESAM_목표_운전점을_재현한다() 파일 C:\PTNC_lib\EFEM_Monitor\src\Esam.Tests\PlantModelTests.cs:줄 287
[xUnit.net 00:11:30.01]     Esam.Tests.SimulatedTransportTests.팬_RPM을_설정하고_현재값을_읽는다 [FAIL]
[xUnit.net 00:11:30.01]       Assert.Equal() Failure: Values differ
[xUnit.net 00:11:30.01]       Expected: 2
[xUnit.net 00:11:30.01]       Actual:   0
[xUnit.net 00:11:30.01]       Stack Trace:
[xUnit.net 00:11:30.01]            위치: Xunit.Assert.Equal[T](T expected, T actual, IEqualityComparer`1 comparer) 파일 /_/src/xunit.assert/Asserts/EqualityAsserts.cs:줄 148
[xUnit.net 00:11:30.01]            위치: Esam.Tests.SimulatedTransportTests.팬_RPM을_설정하고_현재값을_읽는다() 파일 C:\PTNC_lib\EFEM_Monitor\src\Esam.Tests\SimulatedTransportTests.cs:줄 252
[xUnit.net 00:11:30.01]     Esam.Tests.ModbusPortWorkerTests.영점_오프셋은_상태_레지스터를_오염시키지_않는다 [FAIL]
[xUnit.net 00:11:30.01]       Assert.True() Failure
[xUnit.net 00:11:30.01]       Expected: True
[xUnit.net 00:11:30.01]       Actual:   False
[xUnit.net 00:11:30.01]       Stack Trace:
[xUnit.net 00:11:30.01]            위치: Xunit.Assert.True(Nullable`1 condition, String userMessage) 파일 /_/src/xunit.assert/Asserts/BooleanAsserts.cs:줄 147
[xUnit.net 00:11:30.01]            위치: Esam.Tests.ModbusPortWorkerTests.영점_오프셋은_상태_레지스터를_오염시키지_않는다() 파일 C:\PTNC_lib\EFEM_Monitor\src\Esam.Tests\PortWorkerTests.cs:줄 818
[xUnit.net 00:11:30.02]     Esam.Tests.SimulatedTransportTests.성공하면_연속실패_카운터가_초기화된다 [FAIL]
[xUnit.net 00:11:30.02]       Assert.Equal() Failure: Values differ
[xUnit.net 00:11:30.02]       Expected: 0
[xUnit.net 00:11:30.02]       Actual:   2
[xUnit.net 00:11:30.02]       Stack Trace:
[xUnit.net 00:11:30.02]            위치: Xunit.Assert.Equal[T](T expected, T actual, IEqualityComparer`1 comparer) 파일 /_/src/xunit.assert/Asserts/EqualityAsserts.cs:줄 148
[xUnit.net 00:11:30.02]            위치: Esam.Tests.SimulatedTransportTests.성공하면_연속실패_카운터가_초기화된다() 파일 C:\PTNC_lib\EFEM_Monitor\src\Esam.Tests\SimulatedTransportTests.cs:줄 384
[xUnit.net 00:11:30.02]     Esam.Tests.ModbusPortWorkerTests.통계가_트랜잭션을_집계한다 [FAIL]
[xUnit.net 00:11:30.02]       Assert.Equal() Failure: Values are not within 6 decimal places
[xUnit.net 00:11:30.02]       Expected: 100 (rounded from 100)
[xUnit.net 00:11:30.02]       Actual:   0 (rounded from 0)
[xUnit.net 00:11:30.02]       Stack Trace:
[xUnit.net 00:11:30.02]            위치: Xunit.Assert.Equal(Double expected, Double actual, Int32 precision) 파일 /_/src/xunit.assert/Asserts/EqualityAsserts.cs:줄 308
[xUnit.net 00:11:30.02]            위치: Esam.Tests.ModbusPortWorkerTests.통계가_트랜잭션을_집계한다() 파일 C:\PTNC_lib\EFEM_Monitor\src\Esam.Tests\PortWorkerTests.cs:줄 787
[xUnit.net 00:11:30.03]     Esam.Tests.ModbusPortWorkerTests.음압을_부호있게_디코딩한다 [FAIL]
[xUnit.net 00:11:30.03]       System.Collections.Generic.KeyNotFoundException : 지정한 키가 사전에 없습니다.
[xUnit.net 00:11:30.03]       Stack Trace:
[xUnit.net 00:11:30.03]            위치: System.ThrowHelper.ThrowKeyNotFoundException()
[xUnit.net 00:11:30.03]            위치: System.Collections.Generic.Dictionary`2.get_Item(TKey key)
[xUnit.net 00:11:30.03]            위치: Esam.Tests.ModbusPortWorkerTests.음압을_부호있게_디코딩한다() 파일 C:\PTNC_lib\EFEM_Monitor\src\Esam.Tests\PortWorkerTests.cs:줄 596
[xUnit.net 00:11:30.03]     Esam.Tests.ModbusPortWorkerTests.연속_실패가_누적되어_통신상실을_판정할_수_있다 [FAIL]
[xUnit.net 00:11:30.03]       Assert.False() Failure
[xUnit.net 00:11:30.03]       Expected: False
[xUnit.net 00:11:30.03]       Actual:   True
[xUnit.net 00:11:30.03]       Stack Trace:
[xUnit.net 00:11:30.03]            위치: Xunit.Assert.False(Nullable`1 condition, String userMessage) 파일 /_/src/xunit.assert/Asserts/BooleanAsserts.cs:줄 84
[xUnit.net 00:11:30.03]            위치: Esam.Tests.ModbusPortWorkerTests.연속_실패가_누적되어_통신상실을_판정할_수_있다() 파일 C:\PTNC_lib\EFEM_Monitor\src\Esam.Tests\PortWorkerTests.cs:줄 639
[xUnit.net 00:11:30.04]     Esam.Tests.ModbusPortWorkerTests.압력값을_공학단위로_디코딩한다 [FAIL]
[xUnit.net 00:11:30.04]       Assert.True() Failure
[xUnit.net 00:11:30.04]       Expected: True
[xUnit.net 00:11:30.04]       Actual:   False
[xUnit.net 00:11:30.04]       Stack Trace:
[xUnit.net 00:11:30.04]            위치: Xunit.Assert.True(Nullable`1 condition, String userMessage) 파일 /_/src/xunit.assert/Asserts/BooleanAsserts.cs:줄 147
[xUnit.net 00:11:30.04]            위치: Esam.Tests.ModbusPortWorkerTests.압력값을_공학단위로_디코딩한다() 파일 C:\PTNC_lib\EFEM_Monitor\src\Esam.Tests\PortWorkerTests.cs:줄 574
[xUnit.net 00:11:30.04]     Esam.Tests.ModbusPortWorkerTests.폴링_완료_이벤트가_발생한다 [FAIL]
[xUnit.net 00:11:30.04]       Assert.True() Failure
[xUnit.net 00:11:30.04]       Expected: True
[xUnit.net 00:11:30.04]       Actual:   False
[xUnit.net 00:11:30.04]       Stack Trace:
[xUnit.net 00:11:30.04]            위치: Xunit.Assert.True(Nullable`1 condition, String userMessage) 파일 /_/src/xunit.assert/Asserts/BooleanAsserts.cs:줄 147
[xUnit.net 00:11:30.04]            위치: Esam.Tests.ModbusPortWorkerTests.폴링_완료_이벤트가_발생한다() 파일 C:\PTNC_lib\EFEM_Monitor\src\Esam.Tests\PortWorkerTests.cs:줄 753
[xUnit.net 00:11:30.04]     Esam.Tests.ModbusPortWorkerTests.영점_오프셋을_적용하면_측정값이_보정된다 [FAIL]
[xUnit.net 00:11:30.04]       System.Collections.Generic.KeyNotFoundException : 지정한 키가 사전에 없습니다.
[xUnit.net 00:11:30.04]       Stack Trace:
[xUnit.net 00:11:30.04]            위치: System.ThrowHelper.ThrowKeyNotFoundException()
[xUnit.net 00:11:30.04]            위치: System.Collections.Generic.Dictionary`2.get_Item(TKey key)
[xUnit.net 00:11:30.04]            위치: Esam.Tests.ModbusPortWorkerTests.영점_오프셋을_적용하면_측정값이_보정된다() 파일 C:\PTNC_lib\EFEM_Monitor\src\Esam.Tests\PortWorkerTests.cs:줄 799
[xUnit.net 00:11:30.05]     Esam.Tests.RecipeConfigTests.압력센서가_아닌_장치는_오류로_막는다 [FAIL]
[xUnit.net 00:11:30.05]       통신 구성 오류:
device-map.json: JSON 파싱 실패: Could not find member 'readTimeoutMs' on object of type 'SerialPortSettings'. Path 'ports[0].serial.readTimeoutMs', line 26, position 24.
[xUnit.net 00:11:30.05]       Stack Trace:
[xUnit.net 00:11:30.05]            위치: Xunit.Assert.True(Nullable`1 condition, String userMessage) 파일 /_/src/xunit.assert/Asserts/BooleanAsserts.cs:줄 147
[xUnit.net 00:11:30.05]            위치: Esam.Tests.RecipeConfigTests.LoadShippedMap() 파일 C:\PTNC_lib\EFEM_Monitor\src\Esam.Tests\RecipeConfigTests.cs:줄 27
[xUnit.net 00:11:30.05]            위치: Esam.Tests.RecipeConfigTests.압력센서가_아닌_장치는_오류로_막는다() 파일 C:\PTNC_lib\EFEM_Monitor\src\Esam.Tests\RecipeConfigTests.cs:줄 210
[xUnit.net 00:11:30.06]     Esam.Tests.RecipeConfigTests.센서_레인지를_벗어난_값은_오류로_막는다 [FAIL]
[xUnit.net 00:11:30.06]       통신 구성 오류:
device-map.json: JSON 파싱 실패: Could not find member 'readTimeoutMs' on object of type 'SerialPortSettings'. Path 'ports[0].serial.readTimeoutMs', line 26, position 24.
[xUnit.net 00:11:30.06]       Stack Trace:
[xUnit.net 00:11:30.06]            위치: Xunit.Assert.True(Nullable`1 condition, String userMessage) 파일 /_/src/xunit.assert/Asserts/BooleanAsserts.cs:줄 147
[xUnit.net 00:11:30.06]            위치: Esam.Tests.RecipeConfigTests.LoadShippedMap() 파일 C:\PTNC_lib\EFEM_Monitor\src\Esam.Tests\RecipeConfigTests.cs:줄 27
[xUnit.net 00:11:30.06]            위치: Esam.Tests.RecipeConfigTests.센서_레인지를_벗어난_값은_오류로_막는다() 파일 C:\PTNC_lib\EFEM_Monitor\src\Esam.Tests\RecipeConfigTests.cs:줄 145
[xUnit.net 00:11:30.06]     Esam.Tests.RecipeConfigTests.통신_구성에_없는_센서는_오류로_막는다 [FAIL]
[xUnit.net 00:11:30.06]       통신 구성 오류:
device-map.json: JSON 파싱 실패: Could not find member 'readTimeoutMs' on object of type 'SerialPortSettings'. Path 'ports[0].serial.readTimeoutMs', line 26, position 24.
[xUnit.net 00:11:30.06]       Stack Trace:
[xUnit.net 00:11:30.06]            위치: Xunit.Assert.True(Nullable`1 condition, String userMessage) 파일 /_/src/xunit.assert/Asserts/BooleanAsserts.cs:줄 147
[xUnit.net 00:11:30.06]            위치: Esam.Tests.RecipeConfigTests.LoadShippedMap() 파일 C:\PTNC_lib\EFEM_Monitor\src\Esam.Tests\RecipeConfigTests.cs:줄 27
[xUnit.net 00:11:30.06]            위치: Esam.Tests.RecipeConfigTests.통신_구성에_없는_센서는_오류로_막는다() 파일 C:\PTNC_lib\EFEM_Monitor\src\Esam.Tests\RecipeConfigTests.cs:줄 129
[xUnit.net 00:11:30.07]     Esam.Tests.RecipeConfigTests.레시피에_빠진_센서는_경고로_알린다 [FAIL]
[xUnit.net 00:11:30.07]       통신 구성 오류:
device-map.json: JSON 파싱 실패: Could not find member 'readTimeoutMs' on object of type 'SerialPortSettings'. Path 'ports[0].serial.readTimeoutMs', line 26, position 24.
[xUnit.net 00:11:30.07]       Stack Trace:
[xUnit.net 00:11:30.07]            위치: Xunit.Assert.True(Nullable`1 condition, String userMessage) 파일 /_/src/xunit.assert/Asserts/BooleanAsserts.cs:줄 147
[xUnit.net 00:11:30.07]            위치: Esam.Tests.RecipeConfigTests.LoadShippedMap() 파일 C:\PTNC_lib\EFEM_Monitor\src\Esam.Tests\RecipeConfigTests.cs:줄 27
[xUnit.net 00:11:30.07]            위치: Esam.Tests.RecipeConfigTests.레시피에_빠진_센서는_경고로_알린다() 파일 C:\PTNC_lib\EFEM_Monitor\src\Esam.Tests\RecipeConfigTests.cs:줄 250
[xUnit.net 00:11:30.08]     Esam.Tests.RecipeConfigTests.ECID_항목_수가_39개로_맞는다 [FAIL]
[xUnit.net 00:11:30.08]       통신 구성 오류:
device-map.json: JSON 파싱 실패: Could not find member 'readTimeoutMs' on object of type 'SerialPortSettings'. Path 'ports[0].serial.readTimeoutMs', line 26, position 24.
[xUnit.net 00:11:30.08]       Stack Trace:
[xUnit.net 00:11:30.08]            위치: Xunit.Assert.True(Nullable`1 condition, String userMessage) 파일 /_/src/xunit.assert/Asserts/BooleanAsserts.cs:줄 147
[xUnit.net 00:11:30.08]            위치: Esam.Tests.RecipeConfigTests.LoadShippedMap() 파일 C:\PTNC_lib\EFEM_Monitor\src\Esam.Tests\RecipeConfigTests.cs:줄 27
[xUnit.net 00:11:30.08]            위치: Esam.Tests.RecipeConfigTests.LoadShipped() 파일 C:\PTNC_lib\EFEM_Monitor\src\Esam.Tests\RecipeConfigTests.cs:줄 34
[xUnit.net 00:11:30.08]            위치: Esam.Tests.RecipeConfigTests.ECID_항목_수가_39개로_맞는다() 파일 C:\PTNC_lib\EFEM_Monitor\src\Esam.Tests\RecipeConfigTests.cs:줄 76
[xUnit.net 00:11:30.08]     Esam.Tests.RecipeConfigTests.저장하고_다시_읽어도_값이_보존된다 [FAIL]
[xUnit.net 00:11:30.08]       통신 구성 오류:
device-map.json: JSON 파싱 실패: Could not find member 'readTimeoutMs' on object of type 'SerialPortSettings'. Path 'ports[0].serial.readTimeoutMs', line 26, position 24.
[xUnit.net 00:11:30.08]       Stack Trace:
[xUnit.net 00:11:30.08]            위치: Xunit.Assert.True(Nullable`1 condition, String userMessage) 파일 /_/src/xunit.assert/Asserts/BooleanAsserts.cs:줄 147
[xUnit.net 00:11:30.08]            위치: Esam.Tests.RecipeConfigTests.LoadShippedMap() 파일 C:\PTNC_lib\EFEM_Monitor\src\Esam.Tests\RecipeConfigTests.cs:줄 27
[xUnit.net 00:11:30.08]            위치: Esam.Tests.RecipeConfigTests.LoadShipped() 파일 C:\PTNC_lib\EFEM_Monitor\src\Esam.Tests\RecipeConfigTests.cs:줄 34
[xUnit.net 00:11:30.08]            위치: Esam.Tests.RecipeConfigTests.저장하고_다시_읽어도_값이_보존된다() 파일 C:\PTNC_lib\EFEM_Monitor\src\Esam.Tests\RecipeConfigTests.cs:줄 278
[xUnit.net 00:11:30.09]     Esam.Tests.RecipeConfigTests.차압센서_13대_전량에_설정값이_있다 [FAIL]
[xUnit.net 00:11:30.09]       통신 구성 오류:
device-map.json: JSON 파싱 실패: Could not find member 'readTimeoutMs' on object of type 'SerialPortSettings'. Path 'ports[0].serial.readTimeoutMs', line 26, position 24.
[xUnit.net 00:11:30.09]       Stack Trace:
[xUnit.net 00:11:30.09]            위치: Xunit.Assert.True(Nullable`1 condition, String userMessage) 파일 /_/src/xunit.assert/Asserts/BooleanAsserts.cs:줄 147
[xUnit.net 00:11:30.09]            위치: Esam.Tests.RecipeConfigTests.LoadShippedMap() 파일 C:\PTNC_lib\EFEM_Monitor\src\Esam.Tests\RecipeConfigTests.cs:줄 27
[xUnit.net 00:11:30.09]            위치: Esam.Tests.RecipeConfigTests.LoadShipped() 파일 C:\PTNC_lib\EFEM_Monitor\src\Esam.Tests\RecipeConfigTests.cs:줄 34
[xUnit.net 00:11:30.09]            위치: Esam.Tests.RecipeConfigTests.차압센서_13대_전량에_설정값이_있다() 파일 C:\PTNC_lib\EFEM_Monitor\src\Esam.Tests\RecipeConfigTests.cs:줄 54
[xUnit.net 00:11:30.09]     Esam.Tests.RecipeConfigTests.배포_레시피에_남는_경고가_없다 [FAIL]
[xUnit.net 00:11:30.09]       통신 구성 오류:
device-map.json: JSON 파싱 실패: Could not find member 'readTimeoutMs' on object of type 'SerialPortSettings'. Path 'ports[0].serial.readTimeoutMs', line 26, position 24.
[xUnit.net 00:11:30.09]       Stack Trace:
[xUnit.net 00:11:30.09]            위치: Xunit.Assert.True(Nullable`1 condition, String userMessage) 파일 /_/src/xunit.assert/Asserts/BooleanAsserts.cs:줄 147
[xUnit.net 00:11:30.09]            위치: Esam.Tests.RecipeConfigTests.LoadShippedMap() 파일 C:\PTNC_lib\EFEM_Monitor\src\Esam.Tests\RecipeConfigTests.cs:줄 27
[xUnit.net 00:11:30.09]            위치: Esam.Tests.RecipeConfigTests.LoadShipped() 파일 C:\PTNC_lib\EFEM_Monitor\src\Esam.Tests\RecipeConfigTests.cs:줄 34
[xUnit.net 00:11:30.09]            위치: Esam.Tests.RecipeConfigTests.배포_레시피에_남는_경고가_없다() 파일 C:\PTNC_lib\EFEM_Monitor\src\Esam.Tests\RecipeConfigTests.cs:줄 86
[xUnit.net 00:11:30.09]     Esam.Tests.RecipeConfigTests.모드별_시간과_합쳐_제어_파라미터가_된다 [FAIL]
[xUnit.net 00:11:30.09]       통신 구성 오류:
device-map.json: JSON 파싱 실패: Could not find member 'readTimeoutMs' on object of type 'SerialPortSettings'. Path 'ports[0].serial.readTimeoutMs', line 26, position 24.
[xUnit.net 00:11:30.09]       Stack Trace:
[xUnit.net 00:11:30.09]            위치: Xunit.Assert.True(Nullable`1 condition, String userMessage) 파일 /_/src/xunit.assert/Asserts/BooleanAsserts.cs:줄 147
[xUnit.net 00:11:30.09]            위치: Esam.Tests.RecipeConfigTests.LoadShippedMap() 파일 C:\PTNC_lib\EFEM_Monitor\src\Esam.Tests\RecipeConfigTests.cs:줄 27
[xUnit.net 00:11:30.09]            위치: Esam.Tests.RecipeConfigTests.LoadShipped() 파일 C:\PTNC_lib\EFEM_Monitor\src\Esam.Tests\RecipeConfigTests.cs:줄 34
[xUnit.net 00:11:30.09]            위치: Esam.Tests.RecipeConfigTests.모드별_시간과_합쳐_제어_파라미터가_된다() 파일 C:\PTNC_lib\EFEM_Monitor\src\Esam.Tests\RecipeConfigTests.cs:줄 96
[xUnit.net 00:11:30.09]     Esam.Tests.RecipeConfigTests.설정이_없는_센서는_null을_반환한다 [FAIL]
[xUnit.net 00:11:30.09]       통신 구성 오류:
device-map.json: JSON 파싱 실패: Could not find member 'readTimeoutMs' on object of type 'SerialPortSettings'. Path 'ports[0].serial.readTimeoutMs', line 26, position 24.
[xUnit.net 00:11:30.09]       Stack Trace:
[xUnit.net 00:11:30.09]            위치: Xunit.Assert.True(Nullable`1 condition, String userMessage) 파일 /_/src/xunit.assert/Asserts/BooleanAsserts.cs:줄 147
[xUnit.net 00:11:30.09]            위치: Esam.Tests.RecipeConfigTests.LoadShippedMap() 파일 C:\PTNC_lib\EFEM_Monitor\src\Esam.Tests\RecipeConfigTests.cs:줄 27
[xUnit.net 00:11:30.09]            위치: Esam.Tests.RecipeConfigTests.LoadShipped() 파일 C:\PTNC_lib\EFEM_Monitor\src\Esam.Tests\RecipeConfigTests.cs:줄 34
[xUnit.net 00:11:30.09]            위치: Esam.Tests.RecipeConfigTests.설정이_없는_센서는_null을_반환한다() 파일 C:\PTNC_lib\EFEM_Monitor\src\Esam.Tests\RecipeConfigTests.cs:줄 112
[xUnit.net 00:11:30.09]     Esam.Tests.RecipeConfigTests.배포용_recipe_json이_통신_구성과_맞물린다 [FAIL]
[xUnit.net 00:11:30.09]       통신 구성 오류:
device-map.json: JSON 파싱 실패: Could not find member 'readTimeoutMs' on object of type 'SerialPortSettings'. Path 'ports[0].serial.readTimeoutMs', line 26, position 24.
[xUnit.net 00:11:30.09]       Stack Trace:
[xUnit.net 00:11:30.09]            위치: Xunit.Assert.True(Nullable`1 condition, String userMessage) 파일 /_/src/xunit.assert/Asserts/BooleanAsserts.cs:줄 147
[xUnit.net 00:11:30.09]            위치: Esam.Tests.RecipeConfigTests.LoadShippedMap() 파일 C:\PTNC_lib\EFEM_Monitor\src\Esam.Tests\RecipeConfigTests.cs:줄 27
[xUnit.net 00:11:30.09]            위치: Esam.Tests.RecipeConfigTests.LoadShipped() 파일 C:\PTNC_lib\EFEM_Monitor\src\Esam.Tests\RecipeConfigTests.cs:줄 34
[xUnit.net 00:11:30.09]            위치: Esam.Tests.RecipeConfigTests.배포용_recipe_json이_통신_구성과_맞물린다() 파일 C:\PTNC_lib\EFEM_Monitor\src\Esam.Tests\RecipeConfigTests.cs:줄 42
[xUnit.net 00:11:30.10]     Esam.Tests.ServicesIntegrationTests.밸브_상태가_pulse와_개도율로_변환된다 [FAIL]
[xUnit.net 00:11:30.10]       Assert.True() Failure
[xUnit.net 00:11:30.10]       Expected: True
[xUnit.net 00:11:30.10]       Actual:   False
[xUnit.net 00:11:30.10]       Stack Trace:
[xUnit.net 00:11:30.10]            위치: Xunit.Assert.True(Nullable`1 condition, String userMessage) 파일 /_/src/xunit.assert/Asserts/BooleanAsserts.cs:줄 147
[xUnit.net 00:11:30.10]            위치: Esam.Tests.ServicesIntegrationTests.밸브_상태가_pulse와_개도율로_변환된다() 파일 C:\PTNC_lib\EFEM_Monitor\src\Esam.Tests\ServicesIntegrationTests.cs:줄 508
