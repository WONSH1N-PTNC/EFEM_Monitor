using System;
using System.Globalization;
using System.Windows.Media;
using Esam.Domain.Models;
using Esam.Hmi.Infrastructure;

namespace Esam.Hmi.ViewModels
{
    /// <summary>
    /// 센서 1대의 영점 교정 행.
    /// </summary>
    /// <remarks>
    /// <para>영점은 <b>보정 전 값(RawPa)의 평균</b>이다. 보정 후 값을 쓰면 이미
    /// 적용된 오프셋이 두 번 반영되어, 교정할 때마다 값이 0 에서 멀어진다.</para>
    /// <para>제안값과 적용값을 나란히 둔다. 곧바로 덮어쓰면 대기압이 아닌 상태에서
    /// 잡은 영점을 확인할 기회가 없다. <b>그 오차는 이후 모든 측정에 실리고
    /// 한참 뒤에야 드러난다.</b></para>
    /// </remarks>
    public sealed class ZeroCalibrationRowViewModel : ObservableObject
    {
        private double _currentOffset;
        private double _proposedOffset;
        private bool _hasProposal;
        private int _sampleCount;
        private string _measured = "- -";
        private Quality _quality = Quality.NoData;

        /// <summary>행을 만든다.</summary>
        /// <param name="deviceId">디바이스 ID.</param>
        /// <param name="currentOffset">현재 적용 중인 오프셋 [Pa].</param>
        /// <exception cref="ArgumentNullException">디바이스 ID 가 null 일 때.</exception>
        public ZeroCalibrationRowViewModel(string deviceId, double currentOffset)
        {
            if (deviceId == null)
            {
                throw new ArgumentNullException("deviceId");
            }

            DeviceId = deviceId;
            _currentOffset = currentOffset;
        }

        /// <summary>디바이스 ID.</summary>
        public string DeviceId { get; private set; }

        /// <summary>현재 적용 중인 오프셋 [Pa].</summary>
        public double CurrentOffset
        {
            get { return _currentOffset; }
        }

        /// <summary>제안된 오프셋 [Pa].</summary>
        public double ProposedOffset
        {
            get { return _proposedOffset; }
        }

        /// <summary>제안값이 있는지 여부.</summary>
        public bool HasProposal
        {
            get { return _hasProposal; }
        }

        /// <summary>현재 오프셋 표기.</summary>
        public string CurrentText
        {
            get { return Format(_currentOffset); }
        }

        /// <summary>제안 오프셋 표기. 없으면 "- -".</summary>
        public string ProposedText
        {
            get { return _hasProposal ? Format(_proposedOffset) : "- -"; }
        }

        /// <summary>표본 수 표기.</summary>
        public string SampleText
        {
            get
            {
                return _sampleCount > 0
                    ? _sampleCount.ToString(CultureInfo.InvariantCulture)
                    : "- -";
            }
        }

        /// <summary>현재 측정값 표기(보정 후).</summary>
        public string MeasuredText
        {
            get { return _measured; }
            private set { Set(ref _measured, value); }
        }

        /// <summary>측정값 색.</summary>
        public Brush MeasuredBrush
        {
            get
            {
                switch (_quality)
                {
                    case Quality.Good:
                        return HmiPalette.TextPrimary;

                    case Quality.Uncertain:
                    case Quality.Stale:
                        return HmiPalette.Warn;

                    default:
                        return HmiPalette.Bad;
                }
            }
        }

        /// <summary>
        /// 제안값과 현재값의 차이 [Pa]. 제안값이 없으면 "- -".
        /// </summary>
        /// <remarks>
        /// 차이가 크면 대기압 상태가 아니었을 가능성이 있다.
        /// 숫자를 나란히 두는 것보다 차이를 적어 두는 편이 눈에 띈다.
        /// </remarks>
        public string DeltaText
        {
            get { return _hasProposal ? Format(_proposedOffset - _currentOffset) : "- -"; }
        }

        /// <summary>측정값을 갱신한다.</summary>
        /// <param name="reading">판독값. null 이면 값을 지운다.</param>
        public void Update(PressureReading reading)
        {
            if (reading == null || reading.Quality == Quality.NoData)
            {
                _quality = Quality.NoData;
                MeasuredText = "- -";
                Raise("MeasuredBrush");
                return;
            }

            _quality = reading.Quality;
            MeasuredText = reading.Pa.ToString("0.##", CultureInfo.InvariantCulture);
            Raise("MeasuredBrush");
        }

        /// <summary>제안값을 설정한다.</summary>
        /// <param name="offset">제안 오프셋 [Pa].</param>
        /// <param name="sampleCount">평균에 쓴 표본 수.</param>
        public void SetProposal(double offset, int sampleCount)
        {
            _proposedOffset = offset;
            _sampleCount = sampleCount;
            _hasProposal = true;

            RaiseProposal();
        }

        /// <summary>제안값을 지운다.</summary>
        public void ClearProposal()
        {
            _hasProposal = false;
            _sampleCount = 0;

            RaiseProposal();
        }

        /// <summary>제안값을 적용값으로 확정한다.</summary>
        public void Commit()
        {
            if (!_hasProposal)
            {
                return;
            }

            _currentOffset = _proposedOffset;
            _hasProposal = false;
            _sampleCount = 0;

            Raise("CurrentText");
            RaiseProposal();
        }

        /// <summary>적용값을 지정한 값으로 되돌린다.</summary>
        /// <param name="offset">되돌릴 오프셋 [Pa].</param>
        public void Restore(double offset)
        {
            _currentOffset = offset;
            _hasProposal = false;
            _sampleCount = 0;

            Raise("CurrentText");
            RaiseProposal();
        }

        /// <summary>제안 관련 표시를 갱신한다.</summary>
        private void RaiseProposal()
        {
            Raise("HasProposal");
            Raise("ProposedText");
            Raise("SampleText");
            Raise("DeltaText");
        }

        /// <summary>표시용 문자열로 만든다.</summary>
        /// <param name="value">값.</param>
        /// <returns>문자열.</returns>
        private static string Format(double value)
        {
            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }
    }
}
