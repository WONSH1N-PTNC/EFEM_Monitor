using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Esam.Hmi.Infrastructure
{
    /// <summary>
    /// <see cref="INotifyPropertyChanged"/> 기본 구현.
    /// </summary>
    /// <remarks>
    /// MVVM 프레임워크를 도입하지 않고 직접 구현한 이유는, 이 화면이 필요로 하는 것이
    /// 속성 변경 통지와 커맨드 두 가지뿐이기 때문이다. 장비 프로그램은 수명이 길어
    /// 외부 의존성을 줄이는 편이 유지보수에 유리하다.
    /// <para><b>성능 주의</b>: 값이 실제로 바뀌지 않았으면 통지하지 않는다.
    /// 200ms 주기로 13개 센서 + 5개 밸브 + 5개 팬을 갱신하는 화면에서
    /// 무조건 통지하면 변화가 없는 항목까지 매번 다시 그려 UI 스레드가 낭비된다.</para>
    /// </remarks>
    public abstract class ObservableObject : INotifyPropertyChanged
    {
        /// <inheritdoc />
        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>속성 변경을 통지한다.</summary>
        /// <param name="propertyName">속성 이름. 호출측에서 생략하면 컴파일러가 채운다.</param>
        protected void Raise([CallerMemberName] string propertyName = null)
        {
            PropertyChangedEventHandler handler = PropertyChanged;

            if (handler != null)
            {
                handler(this, new PropertyChangedEventArgs(propertyName));
            }
        }

        /// <summary>
        /// 값이 달라졌을 때만 필드를 갱신하고 통지한다.
        /// </summary>
        /// <typeparam name="T">속성 타입.</typeparam>
        /// <param name="field">백킹 필드.</param>
        /// <param name="value">새 값.</param>
        /// <param name="propertyName">속성 이름.</param>
        /// <returns>값이 변경되었으면 true.</returns>
        protected bool Set<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
            {
                return false;
            }

            field = value;
            Raise(propertyName);
            return true;
        }
    }
}
