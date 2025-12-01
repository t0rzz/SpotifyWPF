using System;
using System.Threading.Tasks;
using System.Windows;
using SpotifyWPF.Model;
using SpotifyWPF.Service;

namespace SpotifyWPF.ViewModel.Component
{
    /// <summary>
    /// Centralized player state processing service.
    /// Responsibilities:
    /// - Deduplicate repeated states
    /// - Detect device transfers
    /// - Pause/resume API polling timer appropriately
    /// - Notify UI layer to apply the processed state
    /// </summary>
    public class PlayerStateService : IDisposable
    {
        private readonly TimerManager _timerManager;
        private readonly DeviceManager _deviceManager;
        private readonly ILoggingService _loggingService;

        private DateTimeOffset _lastStateUpdate = DateTimeOffset.MinValue;
        private string _lastStateKey = string.Empty;

        private readonly Action<PlayerState> _applyStateCallback;
        private PlayerState? _lastState;

        /// <summary>
        /// Event raised after a state is processed and enriched with higher-level flags.
        /// </summary>
        public event Action<SpotifyWPF.Model.ProcessedPlayerState>? OnProcessedState;

        public PlayerStateService(DeviceManager deviceManager, TimerManager timerManager, ILoggingService loggingService, Action<PlayerState> applyStateCallback)
        {
            _deviceManager = deviceManager ?? throw new ArgumentNullException(nameof(deviceManager));
            _timerManager = timerManager ?? throw new ArgumentNullException(nameof(timerManager));
            _loggingService = loggingService ?? throw new ArgumentNullException(nameof(loggingService));
            _applyStateCallback = applyStateCallback ?? throw new ArgumentNullException(nameof(applyStateCallback));
        }

        /// <summary>
        /// Process incoming state from WebPlayback SDK or API and apply it via the provided callback on UI thread
        /// </summary>
        public void ProcessIncomingState(PlayerState state)
        {
            try
            {
                var now = DateTimeOffset.UtcNow;
                var stateKey = $"{state.TrackId ?? "null"}|{state.IsPlaying}|{state.PositionMs}|{state.Shuffled}";

                bool isRemoteDeviceActiveAndUiEmpty = _deviceManager.SelectedDevice != null &&
                                                      _deviceManager.SelectedDevice.Id != _deviceManager.WebPlaybackDeviceId &&
                                                      string.IsNullOrEmpty(_deviceManager.WebPlaybackDeviceId) == false &&
                                                      // Note: PlayerViewModel will pass its CurrentTrack when wiring up; we cannot inspect it here.
                                                      false; // Caller (PlayerViewModel) may override; allow fallback via callback

                if ((now - _lastStateUpdate) < TimeSpan.FromMilliseconds(500) && stateKey == _lastStateKey && !isRemoteDeviceActiveAndUiEmpty)
                {
                    _loggingService.LogDebug($"[STATE_UPDATE] 🛑 Duplicate state ignored: {stateKey}");
                    return;
                }

                _lastStateUpdate = now;
                _lastStateKey = stateKey;

                // Determine higher-level events
                var processed = new SpotifyWPF.Model.ProcessedPlayerState()
                {
                    RawState = state,
                    WasDuplicate = false
                };

                if (_lastState != null)
                {
                    // Track change detection
                    if (!string.Equals(_lastState.TrackId, state.TrackId, StringComparison.Ordinal))
                    {
                        processed.TrackChanged = true;
                        processed.Reason = "TrackChanged";
                    }

                    // Device transfer heuristics: same track but missing image on incoming state
                    if (!string.IsNullOrEmpty(_lastState.ImageUrl) && string.IsNullOrEmpty(state.ImageUrl) && !string.IsNullOrEmpty(state.TrackId) && state.TrackId == _lastState.TrackId)
                    {
                        processed.DeviceTransferDetected = true;
                        processed.Reason += (string.IsNullOrEmpty(processed.Reason) ? string.Empty : ",") + "DeviceTransfer";
                    }

                    // Position wrapped detection
                    if (state.IsPlaying && state.PositionMs < Math.Max(0, _lastState.PositionMs - 1000))
                    {
                        processed.PositionWrapped = true;
                        processed.Reason += (string.IsNullOrEmpty(processed.Reason) ? string.Empty : ",") + "PositionWrapped";
                    }
                }

                // Ensure UI updates happen on UI thread via callback
                if (Application.Current != null && !Application.Current.Dispatcher.CheckAccess())
                {
                    Application.Current.Dispatcher.Invoke(() => _applyStateCallback(state));
                }
                else
                {
                    _applyStateCallback(state);
                }

                // Update last state and emit processed event
                _lastState = state;
                try
                {
                    OnProcessedState?.Invoke(processed);
                }
                catch (Exception ex)
                {
                    _loggingService.LogError($"Error raising OnProcessedState: {ex.Message}", ex);
                }

                // Control smart polling: if Web Playback is active, pause polling for longer
                bool isWebPlayerActive = !string.IsNullOrEmpty(_deviceManager.WebPlaybackDeviceId) &&
                                       _deviceManager.SelectedDevice != null &&
                                       _deviceManager.SelectedDevice.Id == _deviceManager.WebPlaybackDeviceId;

                int pauseDurationMs = isWebPlayerActive ? 10000 : 2000; // 10s for Web Player, 2s for remote

                _timerManager.StopStatePollTimer();
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await Task.Delay(pauseDurationMs);
                        _timerManager.StartStatePollTimer();
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error restarting state poll timer: {ex.Message}");
                    }
                });
            }
            catch (Exception ex)
            {
                _loggingService.LogError($"[STATE_UPDATE] ProcessIncomingState error: {ex.Message}", ex);
            }
        }

        public void Dispose()
        {
            try
            {
                _loggingService.LogDebug("[STATE_SERVICE] Dispose called");
            }
            catch { }
        }
    }
}