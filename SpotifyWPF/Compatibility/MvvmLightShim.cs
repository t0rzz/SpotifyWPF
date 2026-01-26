using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Runtime.CompilerServices;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;

namespace GalaSoft.MvvmLight
{
    // ViewModel base che instrada verso ObservableObject del CommunityToolkit
    public abstract class ViewModelBase : ObservableObject
    {
        protected void RaisePropertyChanged([CallerMemberName] string? propertyName = null)
            => OnPropertyChanged(propertyName);

        public Messenger MessengerInstance => Messenger.Default;
    }

    // Messenger compatibile che usa WeakReferenceMessenger internamente
    public sealed class Messenger
    {
        private static readonly Lazy<Messenger> _default = new(() => new Messenger());
        public static Messenger Default => _default.Value;

        private readonly WeakReferenceMessenger _inner = WeakReferenceMessenger.Default;

        // T deve essere reference type per WeakReferenceMessenger
        public void Register<T>(object recipient, object? token, Action<T> action) where T : class
        {
            var tk = token?.ToString() ?? string.Empty;
            _inner.Register<T, string>(recipient, tk, (r, msg) => action(msg));
        }

        public void Send<T>(T message, object? token) where T : class
        {
            var tk = token?.ToString() ?? string.Empty;
            _inner.Send<T, string>(message, tk);
        }

        public void Unregister(object recipient)
        {
            _inner.UnregisterAll(recipient);
        }
    }
}

namespace GalaSoft.MvvmLight.Command
{
    // Wrapper su RelayCommand del CommunityToolkit
    public class RelayCommand : ICommand
    {
        private readonly CommunityToolkit.Mvvm.Input.RelayCommand _inner;

        public RelayCommand(Action execute, Func<bool>? canExecute = null)
        {
            _inner = canExecute is null
                ? new CommunityToolkit.Mvvm.Input.RelayCommand(execute)
                : new CommunityToolkit.Mvvm.Input.RelayCommand(execute, canExecute);
        }

        public bool CanExecute(object? parameter) => _inner.CanExecute(null);
        public void Execute(object? parameter) => _inner.Execute(null);
        public event EventHandler? CanExecuteChanged
        {
            add { _inner.CanExecuteChanged += value; }
            remove { _inner.CanExecuteChanged -= value; }
        }
        public void RaiseCanExecuteChanged() => _inner.NotifyCanExecuteChanged();
    }

    public class RelayCommand<T> : ICommand
    {
        private readonly CommunityToolkit.Mvvm.Input.RelayCommand<T> _inner;

        public RelayCommand(Action<T> execute, Func<T, bool>? canExecute = null)
        {
            // Adatta alle firme nullable del Toolkit (Action<T?>, Predicate<T?>?)
            Action<T?> execWrapped = t => execute(t!);
            Predicate<T?>? predicate = canExecute is null ? null : new Predicate<T?>(t => canExecute(t!));

            _inner = predicate is null
                ? new CommunityToolkit.Mvvm.Input.RelayCommand<T>(execWrapped)
                : new CommunityToolkit.Mvvm.Input.RelayCommand<T>(execWrapped, predicate);
        }

        public bool CanExecute(object? parameter) => _inner.CanExecute(ConvertParam(parameter));
        public void Execute(object? parameter) => _inner.Execute(ConvertParam(parameter));
        public event EventHandler? CanExecuteChanged
        {
            add { _inner.CanExecuteChanged += value; }
            remove { _inner.CanExecuteChanged -= value; }
        }
        public void RaiseCanExecuteChanged() => _inner.NotifyCanExecuteChanged();

        private static T ConvertParam(object? parameter)
        {
            if (parameter is T t) return t;
            if (parameter is null) return default!;

            // Try to convert using ChangeType, but handle cases where it's not possible
            try
            {
                return (T)System.Convert.ChangeType(parameter, typeof(T));
            }
            catch (InvalidCastException)
            {
                // If conversion fails, return default for complex types
                // This prevents crashes when the parameter type doesn't match expectations
                return default!;
            }
            catch (Exception)
            {
                // Handle any other conversion errors gracefully
                return default!;
            }
        }
    }

    // AsyncRelayCommand implementation for async operations
    public class AsyncRelayCommand : ICommand
    {
        private readonly Func<Task> _execute;
        private readonly Func<bool>? _canExecute;
        private bool _isExecuting;

        public AsyncRelayCommand(Func<Task> execute, Func<bool>? canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public bool CanExecute(object? parameter)
        {
            return !_isExecuting && (_canExecute?.Invoke() ?? true);
        }

        public async void Execute(object? parameter)
        {
            if (CanExecute(parameter))
            {
                try
                {
                    _isExecuting = true;
                    RaiseCanExecuteChanged();
                    await _execute();
                }
                catch (Exception ex)
                {
                    // Log unobserved exceptions to prevent UnobservedTaskException
                    System.Diagnostics.Debug.WriteLine($"AsyncRelayCommand.Execute error: {ex.Message}");
                }
                finally
                {
                    _isExecuting = false;
                    RaiseCanExecuteChanged();
                }
            }
        }

        public event EventHandler? CanExecuteChanged;

        public void RaiseCanExecuteChanged()
        {
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public class AsyncRelayCommand<T> : ICommand
    {
        private readonly Func<T, Task> _execute;
        private readonly Func<T, bool>? _canExecute;
        private bool _isExecuting;

        public AsyncRelayCommand(Func<T, Task> execute, Func<T, bool>? canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public bool CanExecute(object? parameter)
        {
            if (_isExecuting) return false;
            var param = ConvertParam(parameter);
            return _canExecute?.Invoke(param) ?? true;
        }

        public async void Execute(object? parameter)
        {
            if (CanExecute(parameter))
            {
                try
                {
                    _isExecuting = true;
                    RaiseCanExecuteChanged();
                    var param = ConvertParam(parameter);
                    await _execute(param);
                }
                catch (Exception ex)
                {
                    // Log unobserved exceptions to prevent UnobservedTaskException
                    System.Diagnostics.Debug.WriteLine($"AsyncRelayCommand<T>.Execute error: {ex.Message}");
                }
                finally
                {
                    _isExecuting = false;
                    RaiseCanExecuteChanged();
                }
            }
        }

        public event EventHandler? CanExecuteChanged;

        public void RaiseCanExecuteChanged()
        {
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }

        private static T ConvertParam(object? parameter)
        {
            if (parameter is T t) return t;
            if (parameter is null) return default!;
            return (T)System.Convert.ChangeType(parameter, typeof(T));
        }
    }
}

namespace CommonServiceLocator
{
    public interface IServiceLocator
    {
        T GetInstance<T>();
    }

    public static class ServiceLocator
    {
        private static IServiceLocator? _current;
        public static void SetLocatorProvider(Func<IServiceLocator> provider) => _current = provider?.Invoke();
        public static IServiceLocator Current => _current ?? throw new InvalidOperationException("ServiceLocator not configured.");
    }
}

namespace GalaSoft.MvvmLight.Ioc
{
    using CommonServiceLocator;
    using System.Reflection;

    // Simple IoC minimo invariato (usato dalle tue registrazioni esistenti)
    public sealed class SimpleIoc : IServiceLocator
    {
        private static readonly Lazy<SimpleIoc> _default = new(() => new SimpleIoc());
        public static SimpleIoc Default => _default.Value;

        private readonly ConcurrentDictionary<Type, Func<object>> _registrations = new();
        private readonly ConcurrentDictionary<Type, object> _singletons = new();

        public void Reset()
        {
            _registrations.Clear();
            _singletons.Clear();
        }

        public void Register<TService, TImpl>() where TImpl : TService
            => _registrations[typeof(TService)] = () => Create(typeof(TImpl));

        public void Register<TService>(Func<TService> factory)
        {
            if (factory == null) throw new ArgumentNullException(nameof(factory));
            _registrations[typeof(TService)] = () => factory()!;
        }

        public void Register<TService>() where TService : class
            => _registrations[typeof(TService)] = () => Create(typeof(TService));

        public T GetInstance<T>()
        {
            var t = typeof(T);
            if (_singletons.TryGetValue(t, out var existing))
                return (T)existing;

            if (_registrations.TryGetValue(t, out var factory))
            {
                var obj = (T)factory();
                _singletons[t] = obj!;
                return obj;
            }

            var created = (T)Create(t);
            _singletons[t] = created!;
            return created;
        }

        private object Create(Type t)
        {
            var ctors = t.GetConstructors(BindingFlags.Public | BindingFlags.Instance);
            var ctor = ctors.OrderByDescending(c => c.GetParameters().Length).FirstOrDefault();
            if (ctor == null) return Activator.CreateInstance(t)!;

            var args = ctor.GetParameters()
                           .Select(p =>
                           {
                               var method = typeof(SimpleIoc).GetMethod(nameof(GetInstance))!.MakeGenericMethod(p.ParameterType);
                               return method.Invoke(this, null)!;
                           }).ToArray();

            return ctor.Invoke(args);
        }

        T IServiceLocator.GetInstance<T>() => GetInstance<T>();
    }
}
