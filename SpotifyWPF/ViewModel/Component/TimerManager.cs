using System;
using System.Windows.Threading;
using SpotifyWPF.Service;

namespace SpotifyWPF.ViewModel.Component
{
    /// <summary>
    /// Manages all timers used by the PlayerViewModel
    /// </summary>
    public class TimerManager : IDisposable
    {
        private readonly ILoggingService _loggingService;

        // Timer instances
        private readonly DispatcherTimer _seekThrottleTimer;
        private readonly DispatcherTimer _statePollTimer;
        private readonly DispatcherTimer _uiProgressTimer;

        // Event handlers
        private EventHandler? _seekThrottleTick;
        private EventHandler? _statePollTick;
        private EventHandler? _uiProgressTick;

        private bool _disposed = false;

        public TimerManager(ILoggingService loggingService)
        {
            _loggingService = loggingService ?? throw new ArgumentNullException(nameof(loggingService));

            // Initialize seek throttle timer
            _seekThrottleTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(Constants.SeekThrottleDelayMs)
            };
            _seekThrottleTimer.Tick += OnSeekThrottleTimerTick;

            // Initialize state poll timer
            _statePollTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(Constants.StatePollIntervalMs) };
            _statePollTimer.Tick += OnStatePollTimerTick;

            // Initialize UI progress timer
            _uiProgressTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            _uiProgressTimer.Tick += OnUiProgressTimerTick;

            _loggingService.LogDebug("[TIMER_MANAGER] TimerManager initialized");
        }

        #region Public Properties

        /// <summary>
        /// Gets whether the seek throttle timer is enabled
        /// </summary>
        public bool IsSeekThrottleEnabled => _seekThrottleTimer.IsEnabled;

        /// <summary>
        /// Gets whether the state poll timer is enabled
        /// </summary>
        public bool IsStatePollEnabled => _statePollTimer.IsEnabled;

        /// <summary>
        /// Gets whether the UI progress timer is enabled
        /// </summary>
        public bool IsUiProgressEnabled => _uiProgressTimer.IsEnabled;

        #endregion

        #region Event Registration

        /// <summary>
        /// Registers the seek throttle timer event handler
        /// </summary>
        public void RegisterSeekThrottleHandler(EventHandler handler)
        {
            _seekThrottleTick = handler;
        }

        /// <summary>
        /// Registers the state poll timer event handler
        /// </summary>
        public void RegisterStatePollHandler(EventHandler handler)
        {
            _statePollTick = handler;
        }

        /// <summary>
        /// Registers the UI progress timer event handler
        /// </summary>
        public void RegisterUiProgressHandler(EventHandler handler)
        {
            _uiProgressTick = handler;
        }

        #endregion

        #region Timer Control Methods

        /// <summary>
        /// Starts the seek throttle timer
        /// </summary>
        public void StartSeekThrottleTimer()
        {
            if (!_disposed)
            {
                _seekThrottleTimer.Start();
                _loggingService.LogDebug("[TIMER_MANAGER] Seek throttle timer started");
            }
        }

        /// <summary>
        /// Stops the seek throttle timer
        /// </summary>
        public void StopSeekThrottleTimer()
        {
            if (!_disposed)
            {
                _seekThrottleTimer.Stop();
                _loggingService.LogDebug("[TIMER_MANAGER] Seek throttle timer stopped");
            }
        }

        /// <summary>
        /// Starts the state poll timer
        /// </summary>
        public void StartStatePollTimer()
        {
            if (!_disposed)
            {
                _statePollTimer.Start();
                _loggingService.LogDebug("[TIMER_MANAGER] State poll timer started");
            }
        }

        /// <summary>
        /// Stops the state poll timer
        /// </summary>
        public void StopStatePollTimer()
        {
            if (!_disposed)
            {
                _statePollTimer.Stop();
                _loggingService.LogDebug("[TIMER_MANAGER] State poll timer stopped");
            }
        }

        /// <summary>
        /// Starts the UI progress timer
        /// </summary>
        public void StartUiProgressTimer()
        {
            if (!_disposed)
            {
                _uiProgressTimer.Start();
                _loggingService.LogDebug("[TIMER_MANAGER] UI progress timer started");
            }
        }

        /// <summary>
        /// Stops the UI progress timer
        /// </summary>
        public void StopUiProgressTimer()
        {
            if (!_disposed)
            {
                _uiProgressTimer.Stop();
                _loggingService.LogDebug("[TIMER_MANAGER] UI progress timer stopped");
            }
        }

        /// <summary>
        /// Restarts the seek throttle timer after a delay
        /// </summary>
        public void RestartStatePollTimerAfterDelay(int delayMs)
        {
            if (_disposed) return;

            StopStatePollTimer();

            // Schedule restart after delay
            var dispatcher = Dispatcher.CurrentDispatcher;
            Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(delayMs);
                    dispatcher.Invoke(() => StartStatePollTimer());
                }
                catch (Exception ex)
                {
                    _loggingService.LogError($"[TIMER_MANAGER] Error restarting state poll timer: {ex.Message}", ex);
                }
            });
        }

        /// <summary>
        /// Restarts the seek throttle timer (stops and starts immediately)
        /// </summary>
        public void RestartSeekThrottleTimer()
        {
            if (!_disposed)
            {
                _seekThrottleTimer.Stop();
                _seekThrottleTimer.Start();
                _loggingService.LogDebug("[TIMER_MANAGER] Seek throttle timer restarted");
            }
        }

        #endregion

        #region Private Event Handlers

        private void OnSeekThrottleTimerTick(object? sender, EventArgs e)
        {
            try
            {
                _seekThrottleTick?.Invoke(sender, e);
            }
            catch (Exception ex)
            {
                _loggingService.LogError($"[TIMER_MANAGER] Error in seek throttle handler: {ex.Message}", ex);
            }
        }

        private void OnStatePollTimerTick(object? sender, EventArgs e)
        {
            try
            {
                _statePollTick?.Invoke(sender, e);
            }
            catch (Exception ex)
            {
                _loggingService.LogError($"[TIMER_MANAGER] Error in state poll handler: {ex.Message}", ex);
            }
        }

        private void OnUiProgressTimerTick(object? sender, EventArgs e)
        {
            try
            {
                _uiProgressTick?.Invoke(sender, e);
            }
            catch (Exception ex)
            {
                _loggingService.LogError($"[TIMER_MANAGER] Error in UI progress handler: {ex.Message}", ex);
            }
        }

        #endregion

        #region IDisposable Implementation

        /// <summary>
        /// Dispose resources used by the TimerManager
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Finalizer for safety net
        /// </summary>
        ~TimerManager()
        {
            Dispose(false);
        }

        /// <summary>
        /// Dispose implementation with proper resource ordering
        /// </summary>
        protected virtual void Dispose(bool disposing)
        {
            if (_disposed)
                return;

            if (disposing)
            {
                // Dispose managed resources in reverse order of creation/allocation

                try
                {
                    // 1. Stop all timers first
                    _seekThrottleTimer.Stop();
                    _statePollTimer.Stop();
                    _uiProgressTimer.Stop();
                }
                catch (Exception ex)
                {
                    _loggingService?.LogError($"Error stopping timers in TimerManager.Dispose: {ex.Message}", ex);
                }

                try
                {
                    // 2. Remove event handlers
                    _seekThrottleTimer.Tick -= OnSeekThrottleTimerTick;
                    _statePollTimer.Tick -= OnStatePollTimerTick;
                    _uiProgressTimer.Tick -= OnUiProgressTimerTick;
                }
                catch (Exception ex)
                {
                    _loggingService?.LogError($"Error removing event handlers in TimerManager.Dispose: {ex.Message}", ex);
                }

                // 3. Clear event handler references
                _seekThrottleTick = null;
                _statePollTick = null;
                _uiProgressTick = null;
            }

            _disposed = true;
        }

        #endregion
    }
}