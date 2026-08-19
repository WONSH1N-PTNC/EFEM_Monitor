using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Esam.Communication.Configuration;
using Esam.Domain.Configuration;
using Esam.Services;

namespace Esam.Hmi.Infrastructure
{
    /// <summary>
    /// HMI 가 사용할 런타임을 조립하고 수명을 관리한다.
    /// </summary>
    /// <remarks>
    /// <para>설정 파일을 읽고 <see cref="EsamRuntime"/> 을 세우는 책임만 갖는다.
    /// 화면과 ViewModel 은 여기서 만든 것을 받아 쓴다.</para>
    /// <para><b>조립 실패를 예외로 던지지 않는다.</b> 설정 파일 하나가 잘못되었을 때
    /// 프로그램이 시작조차 못 하면 작업자는 원인을 볼 방법이 없다. 실패 사유를
    /// <see cref="StartupError"/> 에 담아 화면이 표시하게 한다.</para>
    /// <para>기본 전송 방식은 시뮬레이션이다. 하드웨어 없이 화면 전역을 확인할 수 있고,
    /// 현장 설치 시 설정 화면에서 Serial 로 바꾼다.</para>
    /// </remarks>
    public sealed class HmiHost : IDisposable
    {
        private readonly ManualWriteAccessProvider _writeAccess = new ManualWriteAccessProvider();
        private EsamRuntime _runtime;
        private bool _disposed;

        /// <summary>조립된 런타임. 실패하면 null.</summary>
        public EsamRuntime Runtime
        {
            get { return _runtime; }
        }

        /// <summary>쓰기 권한 제공자.</summary>
        public IWriteAccessProvider WriteAccess
        {
            get { return _writeAccess; }
        }

        /// <summary>쓰기 권한을 조작할 수 있는 형태로 반환한다.</summary>
        public ManualWriteAccessProvider WriteAccessControl
        {
            get { return _writeAccess; }
        }

        /// <summary>기동 실패 사유. 성공했으면 null.</summary>
        public string StartupError { get; private set; }

        /// <summary>사용 중인 설정 폴더.</summary>
        public string ConfigFolder { get; private set; }

        /// <summary>런타임을 조립한다.</summary>
        /// <param name="configFolder">설정 파일 폴더. null 이면 <c>config</c>.</param>
        /// <param name="transport">전송 방식.</param>
        /// <returns>조립에 성공하면 true.</returns>
        /// <remarks>
        /// 이미 조립되어 있으면 먼저 해제한다. 설정 화면에서 전송 방식을 바꾸면
        /// 재조립이 필요하고, 그때 옛 런타임의 워커 스레드와 포트가 남으면
        /// 같은 COM 포트를 두 번 열려다 실패한다.
        /// </remarks>
        public bool Start(string configFolder, TransportMode transport)
        {
            ThrowIfDisposed();
            StopRuntime();

            ConfigFolder = string.IsNullOrEmpty(configFolder) ? "config" : configFolder;
            StartupError = null;

            try
            {
                ConfigLoadResult map = CommunicationConfigLoader.LoadFromFile(
                    Path.Combine(ConfigFolder, "device-map.json"));

                if (!map.IsSuccess)
                {
                    StartupError = Describe("통신 구성(device-map.json)", map.Errors);
                    return false;
                }

                IList<string> controlWarnings;
                ControlConfig control = LoadControl(configFolder, out controlWarnings);

                RuntimeOptions options = new RuntimeOptions();
                options.Transport = transport;
                options.AlarmRulesPath = Path.Combine(ConfigFolder, "alarms.json");
                options.RecipePath = Path.Combine(ConfigFolder, "recipe.json");

                // control.json 관련 경고를 배너에 싣는다. 기본값으로 도는 사실이
                // 화면에 남지 않으면, 현장에서 값을 고쳤는데 반영되지 않는 원인을
                // 찾을 단서가 없다.
                List<ConfigWarning> extra = new List<ConfigWarning>();

                foreach (string warning in controlWarnings)
                {
                    extra.Add(ConfigWarning.Advisory("CFG-CTL", warning, "config/control.json 을 확인하십시오."));
                }

                options.AdditionalWarnings = extra;

                _runtime = EsamRuntime.Create(map.Map, control, options, null);
                return true;
            }
            catch (InvalidOperationException ex)
            {
                // 설정 검증 실패. 사유가 메시지에 담겨 있다.
                StartupError = ex.Message;
                return false;
            }
            catch (IOException ex)
            {
                StartupError = "설정 파일을 읽을 수 없습니다: " + ex.Message;
                return false;
            }
            catch (UnauthorizedAccessException ex)
            {
                StartupError = "설정 파일 접근이 거부되었습니다: " + ex.Message;
                return false;
            }
            catch (Esam.Communication.Abstractions.ModbusTransportException ex)
            {
                // 포트를 열 수 없는 경우. 조립 단계에서 여는 경로는 없지만
                // 실장비 전송 계층이 생성 시점에 검사를 추가할 수 있으므로 함께 잡는다.
                StartupError = "통신 포트를 열 수 없습니다: " + ex.Message;
                return false;
            }
        }

        /// <summary>제어 설정을 읽는다. 파일이 없으면 기본값을 쓴다.</summary>
        /// <param name="configFolder">설정 폴더.</param>
        /// <param name="warnings">읽는 중 생긴 경고(출력).</param>
        /// <returns>제어 설정.</returns>
        /// <remarks>
        /// <para><b>파일이 없어도 기동한다.</b> 제어 파라미터는 코드 기본값이 있고,
        /// 그 값으로도 운전은 성립한다. 여기서 실패시키면 설정 파일 하나가 없다는
        /// 이유로 장비가 뜨지 않는다.</para>
        /// <para>다만 <b>조용히 넘어가지는 않는다.</b> 파일이 없거나 읽지 못하면
        /// 경고로 남겨 배너에 드러낸다. 현장에서 값을 고쳤는데 반영되지 않는
        /// 상태가 가장 나쁘고, 그 원인이 대개 "프로그램이 다른 폴더를 보고 있다" 이다.</para>
        /// <para>파일이 있는데 <b>내용이 틀린</b> 경우는 다르다. 그때는 기본값으로
        /// 조용히 대체하지 않고 경고에 사유를 그대로 싣는다.</para>
        /// </remarks>
        private ControlConfig LoadControl(string configFolder, out IList<string> warnings)
        {
            warnings = new List<string>();

            string path = Path.Combine(configFolder ?? "config", "control.json");

            if (File.Exists(path))
            {
                ControlLoadResult result = ControlConfigLoader.LoadFromFile(path);

                foreach (string warning in result.Warnings)
                {
                    warnings.Add(warning);
                }

                if (result.IsSuccess)
                {
                    return result.Config;
                }

                warnings.Add("control.json 을 읽지 못해 기본값으로 동작합니다: "
                             + string.Join(" / ", result.Errors));
            }
            else
            {
                warnings.Add("control.json 이 없어 기본값으로 동작합니다: " + path);
            }

            return CreateDefaultControl();
        }

        /// <summary>코드 기본값으로 제어 설정을 만든다.</summary>
        /// <returns>제어 설정.</returns>
        private static ControlConfig CreateDefaultControl()
        {
            ControlConfig control = new ControlConfig();

            // 체인 정의는 통신 구성에서 파생되지 않으므로 기본 5조로 세운다.
            for (int i = 1; i <= 5; i++)
            {
                ChainDefinition chain = new ChainDefinition();
                chain.Id = i;
                chain.Name = "통로 " + i.ToString(CultureInfo.InvariantCulture);
                chain.Enabled = true;
                chain.ValveId = "V-" + i.ToString(CultureInfo.InvariantCulture);
                chain.FanId = "F-" + i.ToString(CultureInfo.InvariantCulture);
                chain.Sensor2Id = "S2-" + i.ToString(CultureInfo.InvariantCulture);
                chain.Sensor3Id = "S3-" + i.ToString(CultureInfo.InvariantCulture);

                // 센서 1 은 EC·SL·SR 3곳에만 설치되어 통로와 1:1 대응하지 않는다.
                chain.Sensor1Id = "S1-1";

                control.Chains.Add(chain);
            }

            return control;
        }

        /// <summary>오류 목록을 한 줄짜리 설명으로 만든다.</summary>
        /// <param name="what">무엇을 읽다 실패했는지.</param>
        /// <param name="errors">오류 목록.</param>
        /// <returns>설명 문자열.</returns>
        private static string Describe(string what, IList<string> errors)
        {
            if (errors == null || errors.Count == 0)
            {
                return what + " 을 읽지 못했습니다.";
            }

            return what + " 오류:" + Environment.NewLine
                   + string.Join(Environment.NewLine, errors);
        }

        /// <summary>런타임만 정지·해제한다.</summary>
        private void StopRuntime()
        {
            if (_runtime == null)
            {
                return;
            }

            try
            {
                _runtime.Dispose();
            }
            catch (Exception)
            {
                // 해제 실패가 재조립을 막아서는 안 된다.
                // 여기서 던지면 전송 방식을 되돌릴 방법이 없어진다.
            }

            _runtime = null;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            StopRuntime();
        }

        /// <summary>해제 여부를 확인한다.</summary>
        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException("HmiHost");
            }
        }
    }
}
