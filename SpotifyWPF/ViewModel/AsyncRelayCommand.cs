using System;
using System.Threading.Tasks;
using System.Windows.Input;
using GalaSoft.MvvmLight.Command;

namespace SpotifyWPF.ViewModel
{
    /// <summary>
    /// Base class for async relay commands to reduce code duplication
    /// </summary>
    public abstract class AsyncRelayCommandBase : ICommand
    {
        private readonly Func<bool>? _canExecute;
        private bool _isExecuting;

        protected AsyncRelayCommandBase(Func<bool>? canExecute = null)
        {
            _canExecute = canExecute;
        }

        public bool IsExecuting
        {
            get => _isExecuting;
            private set
            {
                _isExecuting = value;
                CanExecuteChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public event EventHandler? CanExecuteChanged;

        public bool CanExecute(object? parameter)
        {
            return !_isExecuting && (_canExecute?.Invoke() ?? true);
        }

        public async void Execute(object? parameter)
        {
            if (!CanExecute(parameter))
                return;

            IsExecuting = true;
            try
            {
                await ExecuteAsync(parameter);
            }
            finally
            {
                IsExecuting = false;
            }
        }

        /// <summary>
        /// Manually raise CanExecuteChanged event
        /// </summary>
        public void RaiseCanExecuteChanged()
        {
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }

        protected abstract Task ExecuteAsync(object? parameter);
    }

    /// <summary>
    /// Async relay command without parameters
    /// </summary>
    public class AsyncRelayCommand : AsyncRelayCommandBase
    {
        private readonly Func<Task> _execute;

        public AsyncRelayCommand(Func<Task> execute, Func<bool>? canExecute = null)
            : base(canExecute)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        }

        protected override Task ExecuteAsync(object? parameter)
        {
            return _execute();
        }
    }

    /// <summary>
    /// Async relay command with typed parameter
    /// </summary>
    public class AsyncRelayCommand<T> : AsyncRelayCommandBase
    {
        private readonly Func<T, Task> _execute;

        public AsyncRelayCommand(Func<T, Task> execute, Func<bool>? canExecute = null)
            : base(canExecute)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        }

        protected override Task ExecuteAsync(object? parameter)
        {
            return _execute((T)(parameter ?? default(T)!));
        }
    }
}