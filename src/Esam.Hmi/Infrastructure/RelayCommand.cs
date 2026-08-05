using System;
using System.Windows.Input;

namespace Esam.Hmi.Infrastructure
{
    /// <summary>
    /// 델리게이트 기반 <see cref="ICommand"/> 구현.
    /// </summary>
    public sealed class RelayCommand : ICommand
    {
        private readonly Action<object> _execute;
        private readonly Func<object, bool> _canExecute;

        /// <summary>커맨드를 생성한다.</summary>
        /// <param name="execute">실행 동작.</param>
        /// <param name="canExecute">실행 가능 여부 판정. null 이면 항상 실행 가능.</param>
        /// <exception cref="ArgumentNullException">실행 동작이 null 일 때.</exception>
        public RelayCommand(Action<object> execute, Func<object, bool> canExecute = null)
        {
            if (execute == null)
            {
                throw new ArgumentNullException("execute");
            }

            _execute = execute;
            _canExecute = canExecute;
        }

        /// <inheritdoc />
        public event EventHandler CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }

        /// <inheritdoc />
        public bool CanExecute(object parameter)
        {
            return _canExecute == null || _canExecute(parameter);
        }

        /// <inheritdoc />
        public void Execute(object parameter)
        {
            _execute(parameter);
        }
    }
}
