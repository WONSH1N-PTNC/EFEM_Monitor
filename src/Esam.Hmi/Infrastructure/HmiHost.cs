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

                ControlConfig control = LoadControl();

                RuntimeOptions options = new RuntimeOptions();
                options.Transport = transport;
                options.AlarmRulesPath = Path.Combine(ConfigFolder, "alarms.json");
                options.RecipePath = Path.Combine(ConfigFolder, "recipe.json");

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
        /// <returns>제어 설정.</returns>
        /// <remarks>
        /// <c>control.json</c> 은 아직 배포 파일이 없다. 기본값으로 동작하되
        /// 파일이 생기면 그것을 읽도록 경로만 잡아 둔다.
        /// </remarks>
        private ControlConfig LoadControl()
        {
            ControlConfig control = new ControlConfig();

            // 체인 정의는 통신 구성에서 파생되지 않으므로 기본 5조로 세운다.
            // 설정 화면의 "기류 순환 통로" 체크박스가 Enabled 를 조정한다.
            if (control.Chains.Count == 0)
            {
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
                    // ControlConfig.Sensor1Reference 가 어느 것을 기준으로 쓸지 정한다.
                    chain.Sensor1Id = "S1-1";

                    control.Chains.Add(chain);
                }
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
