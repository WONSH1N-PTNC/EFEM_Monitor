using System;

namespace Esam.Hmi.Infrastructure
{
    /// <summary>
    /// 쓰기 작업 허용 여부를 판정한다.
    /// </summary>
    /// <remarks>
    /// <para>레시피 저장·설정 적용·수동 장치 제어는 모두 <b>설비 거동을 바꾸는 조작</b>이다.
    /// UI 설명서는 로그인 전에 이런 작업을 제한하도록 요구한다(SCREEN 11).</para>
    /// <para>로그인 화면은 이 단계(S7)의 범위가 아니다. 그렇다고 관문을 나중에 넣기로
    /// 미루면, 저장 버튼이 이미 여러 화면에 퍼진 뒤에 전부 찾아 고쳐야 한다.
    /// 그 시점에 하나를 빠뜨리면 <b>권한 없이 통과하는 경로가 남는다.</b></para>
    /// <para>그래서 인터페이스와 관문은 지금 넣고, 판정 근거만 나중에 실제 계정으로
    /// 교체한다. 기본값을 "허용" 으로 두고 TODO 를 남기는 방식은 쓰지 않는다.
    /// 그런 TODO 는 지워지지 않는다.</para>
    /// </remarks>
    public interface IWriteAccessProvider
    {
        /// <summary>현재 쓰기 작업이 허용되는지 여부.</summary>
        bool IsWriteAllowed { get; }

        /// <summary>허용 상태가 바뀌면 발생한다.</summary>
        event EventHandler WriteAccessChanged;

        /// <summary>거부 사유를 사람이 읽을 형태로 반환한다. 허용 상태이면 null.</summary>
        /// <returns>거부 사유. 허용되면 null.</returns>
        string DescribeDenial();
    }

    /// <summary>
    /// 명시적 토글로 쓰기를 허용하는 임시 구현.
    /// </summary>
    /// <remarks>
    /// <para>S9 에서 계정·권한 등급이 들어오면 이 클래스만 교체한다.
    /// 화면과 ViewModel 은 <see cref="IWriteAccessProvider"/> 만 알고 있으므로
    /// 바뀌는 곳이 한 군데로 묶인다.</para>
    /// <para><b>기본값은 거부다.</b> 허용으로 두면 관문이 있으나 마나 한 상태가 되고,
    /// 화면을 만들면서 관문이 동작하는지 한 번도 확인하지 않게 된다.</para>
    /// </remarks>
    public sealed class ManualWriteAccessProvider : IWriteAccessProvider
    {
        private bool _allowed;

        /// <inheritdoc />
        public bool IsWriteAllowed
        {
            get { return _allowed; }
        }

        /// <inheritdoc />
        public event EventHandler WriteAccessChanged;

        /// <summary>쓰기 허용 상태를 설정한다.</summary>
        /// <param name="allowed">허용하면 true.</param>
        public void SetAllowed(bool allowed)
        {
            if (_allowed == allowed)
            {
                return;
            }

            _allowed = allowed;

            EventHandler handler = WriteAccessChanged;
            if (handler != null)
            {
                handler(this, EventArgs.Empty);
            }
        }

        /// <inheritdoc />
        public string DescribeDenial()
        {
            return _allowed
                ? null
                : "쓰기 작업이 잠겨 있습니다. 정비 모드로 전환한 뒤 다시 시도하십시오.";
        }
    }
}
