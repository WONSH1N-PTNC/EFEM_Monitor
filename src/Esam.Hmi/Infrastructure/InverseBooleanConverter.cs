using System;
using System.Globalization;
using System.Windows.Data;

namespace Esam.Hmi.Infrastructure
{
    /// <summary>
    /// 논리값을 뒤집는다.
    /// </summary>
    /// <remarks>
    /// <para>"잠겨 있다(IsLocked)" 를 "입력 가능(IsEnabled)" 으로 바꿀 때 쓴다.</para>
    /// <para>ViewModel 에 <c>IsUnlocked</c> 같은 반대 속성을 하나 더 두는 방법도 있으나,
    /// 같은 사실을 두 속성이 들고 있으면 한쪽만 갱신되는 순간이 생긴다.
    /// 표현 계층의 변환은 표현 계층에서 한다.</para>
    /// </remarks>
    public sealed class InverseBooleanConverter : IValueConverter
    {
        /// <inheritdoc />
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return !(value is bool && (bool)value);
        }

        /// <inheritdoc />
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return !(value is bool && (bool)value);
        }
    }
}
