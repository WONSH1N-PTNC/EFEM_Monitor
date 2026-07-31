using System;
using Esam.Communication.Abstractions;
using Esam.Communication.Modbus;
using Xunit;

namespace Esam.Tests
{
    /// <summary>
    /// CRC-16/MODBUS 구현 검증.
    /// </summary>
    public class ModbusCrcTests
    {
        [Fact]
        public void 표준_검사값_123456789는_0x4B37이다()
        {
            // CRC-16/MODBUS 알고리즘의 공인 check value.
            // 이 값이 맞으면 다항식·초기값·반사 방향이 모두 올바르다.
            byte[] data = System.Text.Encoding.ASCII.GetBytes("123456789");

            Assert.Equal(0x4B37, ModbusCrc.Compute(data));
        }

        [Theory]
        [InlineData(new byte[] { 0x01, 0x03, 0x00, 0x00, 0x00, 0x02 }, 0x0BC4)]
        [InlineData(new byte[] { 0x01, 0x04, 0x00, 0x00, 0x00, 0x01 }, 0xCA31)]
        [InlineData(new byte[] { 0x01, 0x06, 0x62, 0x02, 0x09, 0xC4 }, 0x7130)]
        [InlineData(new byte[] { 0x0D, 0x03, 0x60, 0x2B, 0x00, 0x01 }, 0xCEEA)]
        [InlineData(new byte[] { 0x01, 0x83, 0x02 }, 0xF1C0)]
        public void 실제_프레임_CRC가_규격과_일치한다(byte[] payload, int expected)
        {
            Assert.Equal((ushort)expected, ModbusCrc.Compute(payload));
        }

        [Fact]
        public void CRC는_하위바이트를_먼저_기록한다()
        {
            // Modbus RTU 의 CRC 바이트 순서는 리틀엔디안이다.
            // 이것을 뒤집으면 모든 통신이 실패하므로 반드시 고정 검증한다.
            byte[] frame = new byte[8];
            frame[0] = 0x01;
            frame[1] = 0x03;
            frame[2] = 0x00;
            frame[3] = 0x00;
            frame[4] = 0x00;
            frame[5] = 0x02;

            ModbusCrc.Append(frame, 6);

            Assert.Equal(0xC4, frame[6]); // 하위 바이트
            Assert.Equal(0x0B, frame[7]); // 상위 바이트
        }

        [Fact]
        public void Append한_프레임은_Verify를_통과한다()
        {
            byte[] frame = new byte[8];
            frame[0] = 0x0D;
            frame[1] = 0x03;
            frame[2] = 0x60;
            frame[3] = 0x2B;
            frame[4] = 0x00;
            frame[5] = 0x01;

            ModbusCrc.Append(frame, 6);

            Assert.True(ModbusCrc.Verify(frame, 8));
        }

        [Fact]
        public void 1비트라도_바뀌면_Verify가_실패한다()
        {
            byte[] frame = new byte[8];
            frame[0] = 0x01;
            frame[1] = 0x03;
            ModbusCrc.Append(frame, 6);

            Assert.True(ModbusCrc.Verify(frame, 8));

            frame[3] ^= 0x01;

            Assert.False(ModbusCrc.Verify(frame, 8));
        }

        [Fact]
        public void 잘못된_인자는_예외를_던진다()
        {
            Assert.Throws<ArgumentNullException>(() => ModbusCrc.Compute(null));
            Assert.Throws<ArgumentOutOfRangeException>(() => ModbusCrc.Compute(new byte[4], 0, 5));
        }
    }

    /// <summary>
    /// RTU 프레임 인코딩/디코딩 검증.
    /// </summary>
    public class ModbusRtuCodecTests
    {
        [Fact]
        public void 읽기_요청_프레임을_규격대로_조립한다()
        {
            ModbusRequest request = ModbusRequest.ReadHolding(13, 0x602B, 1);
            byte[] frame = ModbusRtuCodec.BuildRequest(request);

            Assert.Equal(8, frame.Length);
            Assert.Equal(0x0D, frame[0]);       // 슬레이브 13
            Assert.Equal(0x03, frame[1]);       // FC03
            Assert.Equal(0x60, frame[2]);       // 주소 상위 (Big-endian)
            Assert.Equal(0x2B, frame[3]);       // 주소 하위
            Assert.Equal(0x00, frame[4]);       // 개수 상위
            Assert.Equal(0x01, frame[5]);       // 개수 하위
            Assert.True(ModbusCrc.Verify(frame, frame.Length));
        }

        [Fact]
        public void 단일쓰기_요청_프레임을_규격대로_조립한다()
        {
            // 밸브 0x6202 에 2500 pulse(45도) 설정
            ModbusRequest request = ModbusRequest.WriteSingle(1, 0x6202, 2500);
            byte[] frame = ModbusRtuCodec.BuildRequest(request);

            Assert.Equal(8, frame.Length);
            Assert.Equal(0x06, frame[1]);
            Assert.Equal(0x62, frame[2]);
            Assert.Equal(0x02, frame[3]);
            Assert.Equal(0x09, frame[4]);       // 2500 = 0x09C4
            Assert.Equal(0xC4, frame[5]);
            Assert.True(ModbusCrc.Verify(frame, frame.Length));
        }

        [Fact]
        public void 다중쓰기_요청_프레임을_규격대로_조립한다()
        {
            ModbusRequest request = ModbusRequest.WriteMultiple(1, 0x2000, new ushort[] { 1000, 1 });
            byte[] frame = ModbusRtuCodec.BuildRequest(request);

            // slave + fc + addr(2) + count(2) + byteCount + data(4) + crc(2) = 13
            Assert.Equal(13, frame.Length);
            Assert.Equal(0x10, frame[1]);
            Assert.Equal(0x00, frame[4]);
            Assert.Equal(0x02, frame[5]);       // 레지스터 2개
            Assert.Equal(0x04, frame[6]);       // 바이트 수 4
            Assert.Equal(0x03, frame[7]);       // 1000 = 0x03E8
            Assert.Equal(0xE8, frame[8]);
            Assert.True(ModbusCrc.Verify(frame, frame.Length));
        }

        [Theory]
        [InlineData(1, 7)]     // 5 + 2*1
        [InlineData(2, 9)]
        [InlineData(13, 31)]
        public void 읽기_응답_예상길이를_정확히_계산한다(int count, int expected)
        {
            ModbusRequest request = ModbusRequest.ReadHolding(1, 0, (ushort)count);

            Assert.Equal(expected, ModbusRtuCodec.ExpectedResponseLength(request));
        }

        [Fact]
        public void 읽기_응답을_파싱한다()
        {
            ModbusRequest request = ModbusRequest.ReadHolding(1, 0, 2);

            byte[] response = new byte[9];
            response[0] = 0x01;
            response[1] = 0x03;
            response[2] = 0x04;   // 바이트 수
            response[3] = 0x12;   // 레지스터 0 = 0x1234
            response[4] = 0x34;
            response[5] = 0xFF;   // 레지스터 1 = 0xFFFF (= -1)
            response[6] = 0xFF;
            ModbusCrc.Append(response, 7);

            FrameParseResult result = ModbusRtuCodec.ParseResponse(request, response, 9);

            Assert.True(result.IsSuccess);
            Assert.Equal(2, result.Registers.Length);
            Assert.Equal(0x1234, result.Registers[0]);
            Assert.Equal(0xFFFF, result.Registers[1]);
        }

        [Fact]
        public void 음압값이_부호있는_16비트로_해석된다()
        {
            // 차압센서는 음압(-200 Pa 등)을 반환하므로 부호 처리가 필수다.
            // -200 Pa / 0.1 Pa-per-LSB = -2000 = 0xF830
            ModbusRequest request = ModbusRequest.ReadInput(9, 0, 1);

            byte[] response = new byte[7];
            response[0] = 0x09;
            response[1] = 0x04;
            response[2] = 0x02;
            response[3] = 0xF8;
            response[4] = 0x30;
            ModbusCrc.Append(response, 5);

            FrameParseResult result = ModbusRtuCodec.ParseResponse(request, response, 7);
            Assert.True(result.IsSuccess);

            ModbusResponse wrapped = ModbusResponse.Success(request, result.Registers, 10.0, 0);

            Assert.Equal(-2000, wrapped.GetInt16(0));
            Assert.Equal(-200.0, wrapped.GetInt16(0) * 0.1, 6);
        }

        [Fact]
        public void 예외응답을_인식한다()
        {
            ModbusRequest request = ModbusRequest.ReadHolding(1, 0x9999, 1);

            byte[] response = new byte[5];
            response[0] = 0x01;
            response[1] = 0x83;   // FC03 | 0x80
            response[2] = 0x02;   // IllegalDataAddress
            ModbusCrc.Append(response, 3);

            FrameParseResult result = ModbusRtuCodec.ParseResponse(request, response, 5);

            Assert.False(result.IsSuccess);
            Assert.Equal(ModbusFailureKind.ExceptionResponse, result.FailureKind);
            Assert.Equal(ModbusExceptionCode.IllegalDataAddress, result.ExceptionCode);
        }

        [Fact]
        public void CRC가_틀리면_내용을_해석하지_않고_실패시킨다()
        {
            ModbusRequest request = ModbusRequest.ReadHolding(1, 0, 1);

            byte[] response = new byte[7];
            response[0] = 0x01;
            response[1] = 0x03;
            response[2] = 0x02;
            response[3] = 0xAA;
            response[4] = 0xBB;
            response[5] = 0x00;   // 고의로 잘못된 CRC
            response[6] = 0x00;

            FrameParseResult result = ModbusRtuCodec.ParseResponse(request, response, 7);

            Assert.False(result.IsSuccess);
            Assert.Equal(ModbusFailureKind.CrcError, result.FailureKind);
        }

        [Fact]
        public void 슬레이브ID가_다르면_실패시킨다()
        {
            ModbusRequest request = ModbusRequest.ReadHolding(1, 0, 1);

            byte[] response = new byte[7];
            response[0] = 0x02;   // 다른 슬레이브
            response[1] = 0x03;
            response[2] = 0x02;
            ModbusCrc.Append(response, 5);

            FrameParseResult result = ModbusRtuCodec.ParseResponse(request, response, 7);

            Assert.False(result.IsSuccess);
            Assert.Equal(ModbusFailureKind.UnexpectedEcho, result.FailureKind);
        }

        [Fact]
        public void 바이트수가_기대와_다르면_실패시킨다()
        {
            ModbusRequest request = ModbusRequest.ReadHolding(1, 0, 2);

            byte[] response = new byte[9];
            response[0] = 0x01;
            response[1] = 0x03;
            response[2] = 0x02;   // 기대 4인데 2를 선언
            ModbusCrc.Append(response, 7);

            FrameParseResult result = ModbusRtuCodec.ParseResponse(request, response, 9);

            Assert.False(result.IsSuccess);
            Assert.Equal(ModbusFailureKind.MalformedFrame, result.FailureKind);
        }

        [Fact]
        public void 쓰기응답의_에코를_검증한다()
        {
            ModbusRequest request = ModbusRequest.WriteSingle(1, 0x6202, 2500);
            byte[] echo = ModbusRtuCodec.BuildRequest(request);

            // 규격상 FC06 응답은 요청과 완전히 동일한 프레임이다.
            FrameParseResult ok = ModbusRtuCodec.ParseResponse(request, echo, echo.Length);
            Assert.True(ok.IsSuccess);

            // 값이 다르게 에코되면 실패해야 한다.
            byte[] wrong = new byte[8];
            Array.Copy(echo, wrong, 8);
            wrong[4] = 0x00;
            wrong[5] = 0x00;
            ModbusCrc.Append(wrong, 6);

            FrameParseResult bad = ModbusRtuCodec.ParseResponse(request, wrong, 8);
            Assert.False(bad.IsSuccess);
            Assert.Equal(ModbusFailureKind.UnexpectedEcho, bad.FailureKind);
        }

        [Fact]
        public void 브로드캐스트_주소는_거부한다()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => ModbusRequest.ReadHolding(0, 0, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => ModbusRequest.ReadHolding(248, 0, 1));
        }

        [Fact]
        public void 규격_범위를_넘는_읽기_개수는_거부한다()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => ModbusRequest.ReadHolding(1, 0, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => ModbusRequest.ReadHolding(1, 0, 126));
        }
    }

    /// <summary>
    /// RTU 타이밍 계산 검증. DESIGN.md 2.2 (B) 폴링 예산의 근거가 되는 계산이다.
    /// </summary>
    public class ModbusTimingTests
    {
        [Fact]
        public void 문자시간은_11비트_기준으로_계산된다()
        {
            // 19200bps, 11비트/문자 → 0.573 ms
            Assert.Equal(11.0 * 1000.0 / 19200.0, ModbusTiming.CharacterTimeMs(19200), 6);
        }

        [Fact]
        public void 저속에서는_t3_5가_문자시간_비례로_계산된다()
        {
            double expected = 3.5 * ModbusTiming.CharacterTimeMs(19200);

            Assert.Equal(expected, ModbusTiming.InterFrameDelayMs(19200), 6);
            Assert.True(ModbusTiming.InterFrameDelayMs(19200) > ModbusTiming.FixedT35Ms);
        }

        [Fact]
        public void 19200초과에서는_규격_고정값을_사용한다()
        {
            Assert.Equal(ModbusTiming.FixedT35Ms, ModbusTiming.InterFrameDelayMs(38400), 6);
            Assert.Equal(ModbusTiming.FixedT15Ms, ModbusTiming.IntraFrameGapMs(115200), 6);
        }

        [Fact]
        public void 차압센서_13채널_폴링예산이_설계문서_추정과_일치한다()
        {
            // DESIGN.md 2.2 (B): 19200bps 에서 트랜잭션 1건 약 16~31ms,
            // 차압센서 13대 순차 폴링 시 약 204~399ms → 100ms 목표 달성 불가.
            double fast = ModbusTiming.EstimateTransactionMs(8, 7, 19200, 5.0);
            double slow = ModbusTiming.EstimateTransactionMs(8, 7, 19200, 20.0);

            Assert.InRange(fast, 12.0, 20.0);
            Assert.InRange(slow, 27.0, 35.0);

            Assert.InRange(fast * 13, 160.0, 260.0);
            Assert.InRange(slow * 13, 350.0, 460.0);

            // 결론: 단일 버스로는 100ms 를 만족할 수 없다.
            Assert.True(fast * 13 > 100.0);
        }

        [Fact]
        public void 포트를_4대씩_분할하면_100ms대에_들어온다()
        {
            // DESIGN.md 2.2 (B) 안 1의 근거.
            double slow = ModbusTiming.EstimateTransactionMs(8, 7, 19200, 20.0);

            Assert.True(slow * 4 < 135.0);
        }

        [Fact]
        public void 잘못된_통신속도는_예외를_던진다()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => ModbusTiming.CharacterTimeMs(0));
        }
    }
}
