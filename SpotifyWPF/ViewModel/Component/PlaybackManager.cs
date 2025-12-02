using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using SpotifyAPI.Web;
using SpotifyWPF.Model;
using SpotifyWPF.Service;

namespace SpotifyWPF.ViewModel.Component
{
    /// <summary>
    /// Component responsible for all playback control operations
    /// </summary>
    public class PlaybackManager : System.ComponentModel.INotifyPropertyChanged
    {
        private readonly ISpotify _spotify;
        private readonly IWebPlaybackBridge _webPlaybackBridge;
        private readonly ILoggingService _loggingService;
        private readonly DeviceManager _deviceManager;
        private readonly TimerManager _timerManager;

        // Playback state fields
        private TrackModel? _currentTrack;
        private bool _isPlaying;
        private int _positionMs;
        private double _volume = 1.0;
        private bool _isDraggingSlider;
        private DateTimeOffset _lastSeekSent = DateTimeOffset.MinValue;
        private bool _isShuffled;
        private int _repeatMode;
        private DateTimeOffset _lastRepeatUpdate = DateTimeOffset.MinValue;

        // Pending track management
        private string? _pendingTrackId;
        private DateTimeOffset _pendingTrackUntil = DateTimeOffset.MinValue;
        private string? _pendingPlayTrackId;
        private DateTimeOffset _pendingPlayUntil = DateTimeOffset.MinValue;

        // Cancellation and threading
        private CancellationTokenSource? _clickCts;
        private readonly SemaphoreSlim _trackOperationSemaphore = new SemaphoreSlim(1, 1);

        // UI progress tracking
        private DateTime _lastUiProgressUpdate = DateTime.UtcNow;

        // Resume/pause tracking
        private DateTimeOffset _resumeRequestedAt = DateTimeOffset.MinValue;
        private DateTimeOffset _pauseRequestedAt = DateTimeOffset.MinValue;

        // Volume throttling
        private DateTime _lastVolumeSent = DateTime.MinValue;

        public PlaybackManager(ISpotify spotify, IWebPlaybackBridge webPlaybackBridge, ILoggingService loggingService, DeviceManager deviceManager, TimerManager timerManager)
        {
            _spotify = spotify ?? throw new ArgumentNullException(nameof(spotify));
            _webPlaybackBridge = webPlaybackBridge ?? throw new ArgumentNullException(nameof(webPlaybackBridge));
            _loggingService = loggingService ?? throw new ArgumentNullException(nameof(loggingService));
            _deviceManager = deviceManager ?? throw new ArgumentNullException(nameof(deviceManager));
            _timerManager = timerManager ?? throw new ArgumentNullException(nameof(timerManager));
        }

        #region Public Properties

        public TrackModel? CurrentTrack
        {
            get => _currentTrack;
            set
            {
                _currentTrack = value;
                OnPropertyChanged(nameof(CurrentTrack));
            }
        }

        public bool IsPlaying
        {
            get => _isPlaying;
            set
            {
                _isPlaying = value;
                OnPropertyChanged(nameof(IsPlaying));
            }
        }

        public int PositionMs
        {
            get => _positionMs;
            set
            {
                _positionMs = value;
                OnPropertyChanged(nameof(PositionMs));
            }
        }

        public double Volume
        {
            get => _volume;
            set
            {
                if (value < 0.0 || value > 1.0) return;

                _volume = value;
                // Property change notification handled by PlayerViewModel
            }
        }

        public bool IsDraggingSlider
        {
            get => _isDraggingSlider;
            set => _isDraggingSlider = value;
        }

        public bool IsShuffled
        {
            get => _isShuffled;
            set => _isShuffled = value;
        }

        public int RepeatMode
        {
            get => _repeatMode;
            set => _repeatMode = value;
        }

        /// <summary>
        /// Last time repeat mode was optimistically updated
        /// </summary>
        public DateTimeOffset LastRepeatUpdate => _lastRepeatUpdate;

        public DateTimeOffset ResumeRequestedAt => _resumeRequestedAt;
        public DateTimeOffset PauseRequestedAt => _pauseRequestedAt;
        public string? PendingTrackId => _pendingTrackId;
        public DateTimeOffset PendingTrackUntil => _pendingTrackUntil;
        public string? PendingPlayTrackId => _pendingPlayTrackId;
        public DateTimeOffset PendingPlayUntil => _pendingPlayUntil;

        #endregion

        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged(string propName)
        {
            try
            {
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(propName));
            }
            catch { }
        }

        #region Playback Control Methods

        /// <summary>
        /// Execute play/pause command
        /// </summary>
        public async Task ExecutePlayPauseAsync(ObservableCollection<DeviceModel> devices, string webPlaybackDeviceId)
        {
            try
            {
                _loggingService.LogDebug($"[PLAYPAUSE_EXECUTE] ExecutePlayPauseAsync called, IsPlaying={IsPlaying}");

                // Always start with a fresh devices snapshot
                await _deviceManager.RefreshDevicesAsync();

                if (IsPlaying)
                {
                    _loggingService.LogDebug($"[PLAYPAUSE_EXECUTE] Currently playing, attempting to pause");

                    // If no active device, avoid calling Pause on API/bridge; just update UI and exit quickly
                    var hasActiveDevice = devices.Any(d => d.IsActive);
                    _loggingService.LogDebug($"[PLAYPAUSE_EXECUTE] Has active device: {hasActiveDevice}");
                    if (!hasActiveDevice)
                    {
                        System.Diagnostics.Debug.WriteLine("⏸ PlayPause: No active device detected; skipping Pause call.");
                        _loggingService.LogDebug($"[PLAYPAUSE_EXECUTE] No active device, updating UI only");
                        IsPlaying = false;
                        _pauseRequestedAt = DateTimeOffset.UtcNow;
                        return;
                    }

                    // Decide control path based on current selection after refresh
                    var selectedDevice = devices.FirstOrDefault(d => d.IsActive);
                    var useWebBridgeQuick = selectedDevice != null && !string.IsNullOrEmpty(webPlaybackDeviceId) && selectedDevice.Id == webPlaybackDeviceId;

                    _loggingService.LogDebug($"[PLAYPAUSE_EXECUTE] Selected device: {selectedDevice?.Name ?? "none"} (ID: {selectedDevice?.Id ?? "none"}), Use Web Bridge: {useWebBridgeQuick}");

                    // Optimistic UI update first for snappy feel
                    IsPlaying = false;
                    _pauseRequestedAt = DateTimeOffset.UtcNow;

                    System.Diagnostics.Debug.WriteLine($"⏸ PlayPause: Pausing via {(useWebBridgeQuick ? "WebPlaybackBridge" : "Spotify API")} (selected={selectedDevice?.Id}, webId={webPlaybackDeviceId})");
                    _loggingService.LogDebug($"[PLAYPAUSE_EXECUTE] Attempting pause via {(useWebBridgeQuick ? "WebPlaybackBridge" : "Spotify API")}");
                    try
                    {
                        if (useWebBridgeQuick)
                        {
                            _loggingService.LogDebug($"[PLAYPAUSE_EXECUTE] Calling WebPlaybackBridge.PauseAsync()");
                            await _webPlaybackBridge.PauseAsync();
                            _loggingService.LogDebug($"[PLAYPAUSE_EXECUTE] WebPlaybackBridge.PauseAsync() completed");
                        }
                        else
                        {
                            var active = devices.FirstOrDefault(d => d.IsActive);
                            var targetDeviceId = active?.Id ?? selectedDevice?.Id ?? webPlaybackDeviceId;
                            _loggingService.LogDebug($"[PLAYPAUSE_EXECUTE] Calling Spotify.PauseCurrentPlaybackAsync(device: {targetDeviceId})");
                            await _spotify.PauseCurrentPlaybackAsync(targetDeviceId);
                            _loggingService.LogDebug($"[PLAYPAUSE_EXECUTE] Spotify.PauseCurrentPlaybackAsync() completed");
                        }
                        _loggingService.LogDebug($"[PLAYPAUSE_EXECUTE] Pause command completed successfully");
                    }
                    catch (Exception pauseEx)
                    {
                        System.Diagnostics.Debug.WriteLine($"⚠️ Pause via primary path failed: {pauseEx.Message}. Falling back to Spotify API.");
                        try
                        {
                            var active = devices.FirstOrDefault(d => d.IsActive);
                            var targetDeviceId = active?.Id ?? selectedDevice?.Id ?? webPlaybackDeviceId;
                            await _spotify.PauseCurrentPlaybackAsync(targetDeviceId);
                        }
                        catch { try { await _webPlaybackBridge.PauseAsync(); } catch { } }
                    }
                }
                else
                {
                    _loggingService.LogDebug($"[PLAYPAUSE_EXECUTE] Currently paused, attempting to resume");

                    // If there is no active device, promote the in-app Web Playback device as active first
                    var hasActive = devices.Any(d => d.IsActive);
                    _loggingService.LogDebug($"[PLAYPAUSE_EXECUTE] Has active device: {hasActive}");
                    if (!hasActive)
                    {
                        // Wait briefly for the Web Playback device id if not yet known
                        if (string.IsNullOrEmpty(webPlaybackDeviceId))
                        {
                            var waitedId = await _deviceManager.WaitForWebDeviceIdAsync(TimeSpan.FromSeconds(4));
                            if (!string.IsNullOrEmpty(waitedId))
                            {
                                _deviceManager.WebPlaybackDeviceId = waitedId;
                            }
                        }

                        var ensured = await _deviceManager.EnsureAppDeviceActiveAsync();
                        _loggingService.LogDebug($"[PLAYPAUSE_EXECUTE] EnsureAppDeviceActive result: {ensured}");
                        System.Diagnostics.Debug.WriteLine($"🎯 EnsureAppDeviceActive before Resume result: {ensured}");
                        // Refresh snapshot after potential transfer
                        await _deviceManager.RefreshDevicesAsync();
                    }

                    // Decide control path based on updated selection
                    var selectedDevice = devices.FirstOrDefault(d => d.IsActive);
                    var useWebBridgeQuick = selectedDevice != null && !string.IsNullOrEmpty(webPlaybackDeviceId) && selectedDevice.Id == webPlaybackDeviceId;

                    // Optimistic UI update first for snappy feel
                    IsPlaying = true;
                    _resumeRequestedAt = DateTimeOffset.UtcNow;

                    System.Diagnostics.Debug.WriteLine($"▶️ PlayPause: Resuming via {(useWebBridgeQuick ? "WebPlaybackBridge" : "Spotify API")} (selected={selectedDevice?.Id}, webId={webPlaybackDeviceId})");
                    try
                    {
                        if (useWebBridgeQuick)
                        {
                            // Proactively enable audio context on the web player
                            await _webPlaybackBridge.ResumeAsync();
                        }
                        else
                        {
                            var active = devices.FirstOrDefault(d => d.IsActive);
                            var targetDeviceId = active?.Id ?? selectedDevice?.Id ?? webPlaybackDeviceId;
                            await _spotify.ResumeCurrentPlaybackAsync(targetDeviceId);
                        }
                    }
                    catch (Exception resumeEx)
                    {
                        System.Diagnostics.Debug.WriteLine($"⚠️ Resume via primary path failed: {resumeEx.Message}. Falling back to Spotify API or web bridge.");
                        try
                        {
                            var active = devices.FirstOrDefault(d => d.IsActive);
                            var targetDeviceId = active?.Id ?? selectedDevice?.Id ?? webPlaybackDeviceId;
                            await _spotify.ResumeCurrentPlaybackAsync(targetDeviceId);
                        }
                        catch { try { await _webPlaybackBridge.ResumeAsync(); } catch { } }
                    }
                }
                _loggingService.LogDebug($"[PLAYPAUSE_EXECUTE] ExecutePlayPauseAsync completed");
            }
            catch (Exception ex)
            {
                _loggingService.LogError($"[PLAYPAUSE_EXECUTE] PlayPause error: {ex.Message}", ex);
                System.Diagnostics.Debug.WriteLine($"PlayPause error: {ex.Message}");
            }
        }

        /// <summary>
        /// Execute next track command
        /// </summary>
        public async Task ExecuteNextAsync(ObservableCollection<DeviceModel> devices)
        {
            try
            {
                _loggingService.LogDebug($"[NEXT] ⏭️ ExecuteNextAsync called");
                await _deviceManager.RefreshDevicesAsync();
                var active = devices.FirstOrDefault(d => d.IsActive);
                var targetDeviceId = active?.Id;

                System.Diagnostics.Debug.WriteLine($"⏭️ SkipToNext: skipping to next track on device {targetDeviceId ?? "(current)"}");
                _loggingService.LogDebug($"[NEXT] ⏭️ Calling SkipToNextAsync for device {targetDeviceId ?? "(current)"}");
                var ok = await _spotify.SkipToNextAsync(targetDeviceId);
                if (ok)
                {
                    _loggingService.LogDebug($"[NEXT] ✅ SkipToNextAsync succeeded, scheduling refresh");
                    // Refresh state shortly after to sync UI (fire-and-forget with internal handling)
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await Task.Delay(300);
                            // RefreshPlayerStateAsync will be called by PlayerViewModel
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"Error refreshing player state after SkipToNext: {ex.Message}");
                        }
                    });
                }
                else
                {
                    _loggingService.LogDebug($"[NEXT] ❌ SkipToNextAsync failed");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SkipToNext error: {ex.Message}");
                _loggingService.LogError($"[NEXT] ❌ SkipToNext error: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Execute previous track command
        /// </summary>
        public async Task ExecutePrevAsync(ObservableCollection<DeviceModel> devices)
        {
            try
            {
                _loggingService.LogDebug($"[PREV] ⏮️ ExecutePrevAsync called");
                await _deviceManager.RefreshDevicesAsync();
                var active = devices.FirstOrDefault(d => d.IsActive);
                var targetDeviceId = active?.Id;

                System.Diagnostics.Debug.WriteLine($"⏮️ SkipToPrev: skipping to previous track on device {targetDeviceId ?? "(current)"}");
                _loggingService.LogDebug($"[PREV] ⏮️ Calling SkipToPrevAsync for device {targetDeviceId ?? "(current)"}");
                var ok = await _spotify.SkipToPrevAsync(targetDeviceId);
                if (ok)
                {
                    _loggingService.LogDebug($"[PREV] ✅ SkipToPrevAsync succeeded, scheduling refresh");
                    // Refresh state shortly after to sync UI (fire-and-forget with internal handling)
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await Task.Delay(300);
                            // RefreshPlayerStateAsync will be called by PlayerViewModel
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"Error refreshing player state after SkipToPrev: {ex.Message}");
                        }
                    });
                }
                else
                {
                    _loggingService.LogDebug($"[PREV] ❌ SkipToPrevAsync failed");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SkipToPrev error: {ex.Message}");
                _loggingService.LogError($"[PREV] ❌ SkipToPrev error: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Execute seek command with throttling
        /// </summary>
        public async Task ExecuteSeekAsync(int positionMs, ObservableCollection<DeviceModel> devices, string webPlaybackDeviceId)
        {
            try
            {
                var now = DateTimeOffset.UtcNow;

                // Throttle seeks to avoid overwhelming the API
                if (now - _lastSeekSent < TimeSpan.FromMilliseconds(120))
                {
                    // Start/restart the throttle timer
                    _timerManager.RestartSeekThrottleTimer();
                    _loggingService.LogDebug($"[SEEK_THROTTLE] Seek throttled: {now - _lastSeekSent}ms since last seek");
                    return;
                }

                _lastSeekSent = now;
                _loggingService.LogDebug($"[SEEK_EXECUTE] Executing seek to {positionMs}ms");

                // Decide control path based on active device
                await _deviceManager.RefreshDevicesAsync();
                var active = devices.FirstOrDefault(d => d.IsActive);
                var useWebBridge = active != null && !string.IsNullOrEmpty(webPlaybackDeviceId) && active.Id == webPlaybackDeviceId;

                _loggingService.LogDebug($"[SEEK_EXECUTE] Active device: {active?.Name ?? "none"} (ID: {active?.Id ?? "none"}), Use Web Bridge: {useWebBridge}");

                if (useWebBridge)
                {
                    _loggingService.LogDebug($"[SEEK_EXECUTE] Using WebPlaybackBridge.SeekAsync({positionMs}ms)");
                    await _webPlaybackBridge.SeekAsync(positionMs);
                    _loggingService.LogDebug($"[SEEK_EXECUTE] WebPlaybackBridge.SeekAsync completed");
                }
                else
                {
                    _loggingService.LogDebug($"[SEEK_EXECUTE] Using Spotify.SeekCurrentPlaybackAsync({positionMs}ms, device: {active?.Id ?? "current"})");
                    await _spotify.SeekCurrentPlaybackAsync(positionMs, active?.Id);
                    _loggingService.LogDebug($"[SEEK_EXECUTE] Spotify.SeekCurrentPlaybackAsync completed");
                }

                // Update UI immediately for responsiveness
                PositionMs = positionMs;
                _loggingService.LogDebug($"[SEEK_EXECUTE] Seek execution completed successfully");
            }
            catch (Exception ex)
            {
                _loggingService.LogError($"[SEEK_EXECUTE] Seek error: {ex.Message}", ex);
                System.Diagnostics.Debug.WriteLine($"Seek error: {ex.Message}");
                try
                {
                    await _deviceManager.RefreshDevicesAsync();
                    var active = devices.FirstOrDefault(d => d.IsActive);
                    await _spotify.SeekCurrentPlaybackAsync(positionMs, active?.Id);
                }
                catch { }
            }
        }

        /// <summary>
        /// Execute set volume command
        /// </summary>
        public async Task ExecuteSetVolumeAsync(double volume, ObservableCollection<DeviceModel> devices, string webPlaybackDeviceId)
        {
            try
            {
                // Clamp and snap tiny values to 0 to avoid near-silent confusion
                var clamped = Math.Max(0.0, Math.Min(1.0, volume));
                if (clamped > 0 && clamped < 0.005) clamped = 0.0;

                _loggingService.LogDebug($"[VOLUME_EXECUTE] Executing volume change to {clamped:F3}");

                // Throttle to prevent flooding the bridge and API while dragging
                var now = DateTime.UtcNow;
                if ((now - _lastVolumeSent) < TimeSpan.FromMilliseconds(80) && Math.Abs(clamped - _volume) < 0.01)
                {
                    _loggingService.LogDebug($"[VOLUME_THROTTLE] Volume change throttled: {now - _lastVolumeSent}ms since last, delta={Math.Abs(clamped - _volume):F3}");
                    return;
                }

                _lastVolumeSent = now;
                _volume = clamped;

                // Always apply to Web Playback SDK (local bridge) for immediate UX when app is the device
                try
                {
                    _loggingService.LogDebug($"[VOLUME_EXECUTE] Setting volume on WebPlaybackBridge: {clamped:F3}");
                    await _webPlaybackBridge.SetVolumeAsync(_volume);
                    _loggingService.LogDebug($"[VOLUME_EXECUTE] WebPlaybackBridge.SetVolumeAsync completed");
                }
                catch (Exception bridgeEx)
                {
                    _loggingService.LogDebug($"[VOLUME_EXECUTE] WebPlaybackBridge volume error (non-fatal): {bridgeEx.Message}");
                    System.Diagnostics.Debug.WriteLine($"SetVolume bridge error (non-fatal): {bridgeEx.Message}");
                }

                // Also apply via Spotify Web API to the currently active device (web or remote)
                // This ensures the volume changes even if the SDK isn't the active controller yet.
                await _deviceManager.RefreshDevicesAsync();
                var active = devices.FirstOrDefault(d => d.IsActive);
                if (active != null && !string.IsNullOrEmpty(active.Id))
                {
                    var percent = (int)Math.Round(_volume * 100);
                    _loggingService.LogDebug($"[VOLUME_EXECUTE] Setting volume via API: {percent}% on device {active.Id} ({active.Name})");
                    try
                    {
                        await _spotify.SetVolumePercentOnDeviceAsync(active.Id, percent);
                        _loggingService.LogDebug($"[VOLUME_EXECUTE] Spotify.SetVolumePercentOnDeviceAsync completed");
                    }
                    catch (Exception apiEx)
                    {
                        _loggingService.LogDebug($"[VOLUME_EXECUTE] API volume error (non-fatal): {apiEx.Message}");
                        System.Diagnostics.Debug.WriteLine($"SetVolume API error (non-fatal): {apiEx.Message}");
                    }
                }
                else
                {
                    _loggingService.LogDebug($"[VOLUME_EXECUTE] No active device found for API volume control");
                }
                _loggingService.LogDebug($"[VOLUME_EXECUTE] Volume execution completed");
            }
            catch (Exception ex)
            {
                _loggingService.LogError($"[VOLUME_EXECUTE] SetVolume error: {ex.Message}", ex);
                System.Diagnostics.Debug.WriteLine($"SetVolume error: {ex.Message}");
            }
        }

        /// <summary>
        /// Execute device selection command
        /// </summary>
        public async Task ExecuteSelectDeviceAsync(DeviceModel device, ObservableCollection<DeviceModel> devices, string webPlaybackDeviceId)
        {
            try
            {
                if (device == null) return;

                // Transfer playback to selected device (false = keep current playback state)
                await _spotify.TransferPlaybackAsync(new[] { device.Id }, false);

                // Give Spotify a brief moment to apply the transfer, then resync devices and state
                await Task.Delay(300);
                await _deviceManager.RefreshDevicesAsync();

                // If the selected device became active, keep it selected; otherwise, switch to whatever is active
                var active = devices.FirstOrDefault(d => d.IsActive);
                if (active != null)
                {
                    // SelectedDevice update handled by PlayerViewModel
                }

                // Force a fresh player state read so PositionMs/IsPlaying reflect the transferred playback
                _lastUiProgressUpdate = DateTime.UtcNow; // reset UI progress clock base
                // RefreshPlayerStateAsync will be called by PlayerViewModel

                // Update WebPlaybackDeviceId if this is the web player device
                if (device.Id == webPlaybackDeviceId)
                {
                    // Already using web player
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SelectDevice error: {ex.Message}");

                // Retry once after 250ms for 404 errors, then resync state
                if (ex.Message.Contains("404"))
                {
                    await Task.Delay(250);
                    try
                    {
                        await _spotify.TransferPlaybackAsync(new[] { device.Id }, false);
                        await Task.Delay(300);
                        await _deviceManager.RefreshDevicesAsync();
                        var active = devices.FirstOrDefault(d => d.IsActive);
                        if (active != null)
                        {
                            // SelectedDevice update handled by PlayerViewModel
                        }
                        _lastUiProgressUpdate = DateTime.UtcNow;
                        // RefreshPlayerStateAsync will be called by PlayerViewModel
                    }
                    catch (Exception retryEx)
                    {
                        System.Diagnostics.Debug.WriteLine($"SelectDevice retry error: {retryEx.Message}");
                    }
                }
            }
        }

        /// <summary>
        /// Execute toggle shuffle command
        /// </summary>
        public async Task ExecuteToggleShuffleAsync(ObservableCollection<DeviceModel> devices)
        {
            try
            {
                await _deviceManager.RefreshDevicesAsync();
                var active = devices.FirstOrDefault(d => d.IsActive);
                var targetDeviceId = active?.Id;

                var newState = !IsShuffled;
                System.Diagnostics.Debug.WriteLine($"🔀 ToggleShuffle: setting shuffle={(newState ? "on" : "off")} for device {targetDeviceId ?? "(current)"}");
                var ok = await _spotify.SetShuffleAsync(newState, targetDeviceId);
                if (ok)
                {
                    IsShuffled = newState; // Optimistic update
                }

                // Refresh state shortly after to sync UI from authoritative source
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await Task.Delay(300);
                        // RefreshPlayerStateAsync will be called by PlayerViewModel
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error refreshing player state after ToggleShuffle/CycleRepeat: {ex.Message}");
                    }
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ToggleShuffle error: {ex.Message}");
            }
        }

        /// <summary>
        /// Execute cycle repeat command
        /// </summary>
        public async Task ExecuteCycleRepeatAsync(ObservableCollection<DeviceModel> devices)
        {
#pragma warning disable CS4014
            try
            {
                await _deviceManager.RefreshDevicesAsync();
                var active = devices.FirstOrDefault(d => d.IsActive);
                var targetDeviceId = active?.Id;

                // Cycle: off(0) -> context(1) -> track(2) -> off(0)
                var next = RepeatMode switch { 0 => 1, 1 => 2, 2 => 0, _ => 0 };
                var state = next == 2 ? "track" : next == 1 ? "context" : "off";

                System.Diagnostics.Debug.WriteLine($"🔁 CycleRepeat: setting repeat={state} for device {targetDeviceId ?? "(current)"}");
                var ok = await _spotify.SetRepeatAsync(state, targetDeviceId);
                if (ok)
                {
                    RepeatMode = next; // Optimistic update
                    _lastRepeatUpdate = DateTimeOffset.UtcNow; // Track the optimistic update time
                }

                // Refresh state shortly after to sync UI (observe exceptions)
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await Task.Delay(300);
                        // RefreshPlayerStateAsync will be called by PlayerViewModel
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error refreshing player state after CycleRepeat: {ex.Message}");
                    }
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"CycleRepeat error: {ex.Message}");
            }
        }

        /// <summary>
        /// Execute refresh command
        /// </summary>
        public async Task ExecuteRefreshAsync()
        {
            try
            {
                _loggingService.LogDebug($"[MANUAL_REFRESH] 🔄 Manual refresh requested by user");
                await _deviceManager.RefreshDevicesAsync();
                // RefreshPlayerStateAsync will be called by PlayerViewModel
                _loggingService.LogDebug($"[MANUAL_REFRESH] ✅ Manual refresh completed");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Manual refresh error: {ex.Message}");
            }
        }

        #endregion

        #region Click-to-Play Orchestration

        /// <summary>
        /// Execute click track command (optimistic playback start)
        /// </summary>
        public async Task ExecuteClickTrackAsync(object trackObj, ObservableCollection<DeviceModel> devices, string webPlaybackDeviceId)
        {
            var operationId = Guid.NewGuid().ToString().Substring(0, 8);
            LoggingService.LogToFile($"=== TRACK CLICK STARTED [OP:{operationId}] ===\n");

            try
            {
                if (trackObj == null)
                {
                    LoggingService.LogToFile($"❌ NULL TRACK OBJECT RECEIVED [OP:{operationId}]\n");
                    System.Diagnostics.Debug.WriteLine($"❌ CLICK TRACK: NULL TRACK [OP:{operationId}]");
                    return;
                }

                LoggingService.LogToFile($"📦 Received track object of type: {trackObj.GetType().Name} [OP:{operationId}]\n");

                // Convert to TrackModel if needed
                TrackModel track;
                if (trackObj is TrackModel trackModel)
                {
                    track = trackModel;
                    LoggingService.LogToFile($"✅ Using existing TrackModel: {track.Title}\n");
                }
                else if (trackObj is FullTrack apiTrack)
                {
                    LoggingService.LogToFile($"🔄 Converting SpotifyAPI.Web.FullTrack to TrackModel\n");
                    // Convert from SpotifyAPI.Web.FullTrack to TrackModel
                    track = new TrackModel
                    {
                        Id = apiTrack.Id ?? string.Empty,
                        Title = apiTrack.Name ?? string.Empty,
                        Artist = string.Join(", ", apiTrack.Artists?.Select(a => a.Name) ?? new List<string>()),
                        Uri = apiTrack.Uri ?? string.Empty,
                        DurationMs = apiTrack.DurationMs,
                        AlbumArtUri = null // Will be set later if available
                    };
                    LoggingService.LogToFile($"✅ Converted to TrackModel: {track.Title} by {track.Artist}\n");
                }
                else
                {
                    LoggingService.LogToFile($"❌ UNSUPPORTED TRACK TYPE: {trackObj.GetType().FullName}\n");
                    System.Diagnostics.Debug.WriteLine($"❌ CLICK TRACK: Unsupported track type: {trackObj.GetType()}");
                    return;
                }

                LoggingService.LogToFile($"🎵 Processing track: '{track.Title}' by '{track.Artist}' (ID: {track.Id})\n");

                System.Diagnostics.Debug.WriteLine($"🎵 ClickTrack: {track.Title} by {track.Artist}");

                // Optimistic UI updates (instant feedback)
                LoggingService.LogToFile("🎨 Updating UI with optimistic changes\n");
                CurrentTrack = track;
                PositionMs = 0;
                // Do not optimistically flip to playing; wait for confirmation to avoid Pause-on-nothing scenarios
                IsPlaying = false;
                _resumeRequestedAt = DateTimeOffset.UtcNow; // Suppress stale paused states from API shortly after click-to-play

                LoggingService.LogToFile($"⏰ Set resume suppression until: {_resumeRequestedAt.AddSeconds(2):HH:mm:ss}\n");

                // Suppress transient reversion and remember to auto-start this track on web activation
                _pendingTrackId = track.Id;
                _pendingTrackUntil = DateTimeOffset.UtcNow.AddSeconds(4);
                _pendingPlayTrackId = track.Id;
                _pendingPlayUntil = DateTimeOffset.UtcNow.AddSeconds(8);

                LoggingService.LogToFile($"🎯 Set pending track ID: {track.Id}\n");
                LoggingService.LogToFile($"⏳ Pending track valid until: {_pendingTrackUntil:HH:mm:ss}\n");
                LoggingService.LogToFile($"▶️ Pending play valid until: {_pendingPlayUntil:HH:mm:ss}\n");

                // Nudge the web player's audio context immediately under the user click gesture
                LoggingService.LogToFile("🔊 Nudging web player audio context\n");
                try { await _webPlaybackBridge.ResumeAsync(); } catch { }

                // Cancel any previous click-to-play orchestration
                LoggingService.LogToFile("🛑 Cancelling previous click-to-play orchestration\n");
                _clickCts?.Cancel();
                _clickCts = new CancellationTokenSource();
                var token = _clickCts.Token;

                // Fire-and-forget the heavy orchestration so clicks remain responsive
                // Use semaphore to prevent concurrent track operations
                LoggingService.LogToFile($"🚀 Starting OrchestrateClickPlayAsync (fire-and-forget with semaphore) [OP:{operationId}]\n");
                _ = Task.Run(async () =>
                {
                    await _trackOperationSemaphore.WaitAsync(token);
                    try
                    {
                        await OrchestrateClickPlayAsync(track, token, operationId, devices, webPlaybackDeviceId);
                    }
                    finally
                    {
                        _trackOperationSemaphore.Release();
                    }
                }, token);
            }
            catch (Exception ex)
            {
                LoggingService.LogToFile($"💥 ERROR in ExecuteClickTrackAsync: {ex.Message}\n");
                System.Diagnostics.Debug.WriteLine($"ClickTrack error (fast path): {ex.Message}");
                IsPlaying = false;
            }
            finally
            {
                // Reset UI progress baseline and burst refresh to quickly sync UI after starting playback
                _lastUiProgressUpdate = DateTime.UtcNow;
                // SchedulePostActionStateRefresh will be called by PlayerViewModel
                LoggingService.LogToFile("=== TRACK CLICK COMPLETED ===\n");
            }
        }

        // Fire-and-forget background orchestration for click-to-play to avoid blocking UI
        private async Task OrchestrateClickPlayAsync(TrackModel track, CancellationToken ct, string operationId, ObservableCollection<DeviceModel> devices, string webPlaybackDeviceId)
        {
            LoggingService.LogToFile($"ORCHESTRATE === STARTING ORCHESTRATION for track: {track.Title} ===\n");

            try
            {
                if (ct.IsCancellationRequested)
                {
                    LoggingService.LogToFile("ORCHESTRATE ❌ Orchestration cancelled before start\n");
                    return;
                }

                // Ensure devices list is fresh for accurate active device detection
                try { await _deviceManager.RefreshDevicesAsync(); } catch { }

                // Try to get web device id quickly if missing
                if (string.IsNullOrEmpty(webPlaybackDeviceId))
                {
                    var waitedId = await _deviceManager.WaitForWebDeviceIdAsync(TimeSpan.FromSeconds(3));
                    if (!string.IsNullOrEmpty(waitedId))
                    {
                        _deviceManager.WebPlaybackDeviceId = waitedId;
                    }
                    else
                    {
                        // Fallback: try to get device ID directly from WebPlaybackBridge
                        try
                        {
                            var bridgeDeviceId = await _webPlaybackBridge.GetWebPlaybackDeviceIdAsync();
                            if (!string.IsNullOrEmpty(bridgeDeviceId))
                            {
                                _deviceManager.WebPlaybackDeviceId = bridgeDeviceId;
                            }
                        }
                        catch (Exception ex)
                        {
                            LoggingService.LogToFile($"ORCHESTRATE ❌ Error getting device ID from bridge: {ex.Message}\n");
                        }
                    }
                }

                if (ct.IsCancellationRequested)
                {
                    LoggingService.LogToFile("ORCHESTRATE ❌ Orchestration cancelled after device check\n");
                    return;
                }

                // Proactively enable audio context on the web player (best-effort)
                try { await _webPlaybackBridge.ResumeAsync(); } catch { }

                if (ct.IsCancellationRequested)
                {
                    LoggingService.LogToFile("ORCHESTRATE ❌ Orchestration cancelled\n");
                    return;
                }

                // 🎯 PRIORITY 1: Try to play on the currently active device first
                var activeDevice = devices.FirstOrDefault(d => d.IsActive);
                if (activeDevice != null && !string.IsNullOrEmpty(activeDevice.Id) && !string.IsNullOrEmpty(track.Id))
                {
                        LoggingService.LogToFile($"[PLAY_CALL] Attempting PlayTrackOnDeviceAsync on device: {activeDevice.Id} track: {track.Id}\n");
                    try
                    {
                        var ok = await _spotify.PlayTrackOnDeviceAsync(activeDevice.Id, track.Id);
                        LoggingService.LogToFile($"[PLAY_CALL] PlayTrackOnDeviceAsync result for device {activeDevice.Id}: {ok}\n");
                        if (ok)
                        {
                            // Immediately set playback state so UI can update without waiting for polling
                            CurrentTrack = track;
                            PositionMs = 0;
                            IsPlaying = true;
                            LoggingService.LogToFile("ORCHESTRATE ✅ Click-to-play started successfully on active device\n");
                            // Quick refresh to ensure UI learns about active device and updates player state
                            try { await _deviceManager.RefreshDevicesAsync(); } catch { }
                                try
                                {
                                    var payload = new SpotifyWPF.ViewModel.Messages.PlaybackUpdatePayload
                                    {
                                        DeviceId = activeDevice?.Id,
                                        TrackId = track.Id
                                    };
                                    GalaSoft.MvvmLight.Messenger.Default.Send(payload, SpotifyWPF.ViewModel.MessageType.PlaybackUpdated);
                                }
                            catch { }
                            return;
                        }
                    }
                    catch (SpotifyWPF.Service.RateLimitException rateEx)
                    {
                        LoggingService.LogToFile($"[PLAY_CALL] Rate limit exceeded on active device {activeDevice.Id}: {rateEx.Message}\n");
                        // Show user-friendly message and stop trying
                        int? retryAfter = null;
                        if (rateEx.InnerException is APIException apiEx)
                        {
                            retryAfter = GetRetryAfterSeconds(apiEx.Response?.Headers);
                        }
                        var message = retryAfter.HasValue ?
                            $"Spotify API rate limit exceeded. Please wait {retryAfter.Value} seconds before trying to play tracks again." :
                            "Spotify API rate limit exceeded. Please wait a moment before trying to play tracks again.";
                        await App.Current.Dispatcher.InvokeAsync(() =>
                        {
                            System.Windows.MessageBox.Show(
                                message,
                                "Rate Limit Exceeded",
                                System.Windows.MessageBoxButton.OK,
                                System.Windows.MessageBoxImage.Warning);
                        });
                        return;
                    }
                }

                if (ct.IsCancellationRequested)
                {
                    LoggingService.LogToFile("ORCHESTRATE ❌ Orchestration cancelled after active device check\n");
                    return;
                }

                // 🎯 PRIORITY 2: Try to get current playback device from API
                try
                {
                    var playback = await _spotify.GetCurrentPlaybackAsync();
                    var playbackDeviceId = playback?.Device?.Id;
                    if (!string.IsNullOrEmpty(playbackDeviceId) && !string.IsNullOrEmpty(track.Id))
                    {
                        LoggingService.LogToFile($"[PLAY_CALL] Attempting PlayTrackOnDeviceAsync on device (from API): {playbackDeviceId} track: {track.Id}\n");
                        var ok = await _spotify.PlayTrackOnDeviceAsync(playbackDeviceId!, track.Id);
                        LoggingService.LogToFile($"[PLAY_CALL] PlayTrackOnDeviceAsync result for device {playbackDeviceId}: {ok}\n");
                        if (ok)
                        {
                            CurrentTrack = track;
                            PositionMs = 0;
                            IsPlaying = true;
                            LoggingService.LogToFile("ORCHESTRATE ✅ Click-to-play started successfully on API playback device\n");
                            try { await _deviceManager.RefreshDevicesAsync(); } catch { }
                            try
                            {
                                // Notify UI that playback started for immediate refresh; include device/track payload
                                var payload = new SpotifyWPF.ViewModel.Messages.PlaybackUpdatePayload
                                {
                                    DeviceId = playback?.Device?.Id,
                                    TrackId = track.Id
                                };
                                GalaSoft.MvvmLight.Messenger.Default.Send(payload, SpotifyWPF.ViewModel.MessageType.PlaybackUpdated);
                            }
                            catch { }
                            return;
                        }
                        else
                        {
                            try
                            {
                                LoggingService.LogToFile($"[PLAY_CALL] PlayTrackOnDeviceAsync failed for {playbackDeviceId}. Trying TransferPlaybackAsync(play=true) and retry...\n");
                                await _spotify.TransferPlaybackAsync(new[] { playbackDeviceId! }, true);
                                try { await Task.Delay(350, ct); } catch { }
                                var ok2 = await _spotify.PlayTrackOnDeviceAsync(playbackDeviceId!, track.Id);
                                LoggingService.LogToFile($"[PLAY_CALL] Retry PlayTrackOnDeviceAsync result for device {playbackDeviceId}: {ok2}\n");
                                if (ok2)
                                {
                                    CurrentTrack = track;
                                    PositionMs = 0;
                                    IsPlaying = true;
                                    LoggingService.LogToFile("ORCHESTRATE ✅ Click-to-play started successfully on API playback device after transfer fallback\n");
                                    try { await _deviceManager.RefreshDevicesAsync(); } catch { }
                                    try { GalaSoft.MvvmLight.Messenger.Default.Send(new SpotifyWPF.ViewModel.Messages.PlaybackUpdatePayload { DeviceId = playbackDeviceId, TrackId = track.Id }, SpotifyWPF.ViewModel.MessageType.PlaybackUpdated); } catch { }
                                    return;
                                }
                            }
                            catch (Exception ex)
                            {
                                LoggingService.LogToFile($"[PLAY_CALL] TransferPlaybackAsync fallback failed: {ex.Message}\n");
                                if (ex is SpotifyWPF.Service.RateLimitException rateEx)
                                {
                                    // Show user-friendly message for rate limit
                                    int? retryAfter = null;
                                    if (rateEx.InnerException is APIException apiEx)
                                    {
                                        retryAfter = GetRetryAfterSeconds(apiEx.Response?.Headers);
                                    }
                                    var message = retryAfter.HasValue ?
                                        $"Spotify API rate limit exceeded. Please wait {retryAfter.Value} seconds before trying to play tracks again." :
                                        "Spotify API rate limit exceeded. Please wait a moment before trying to play tracks again.";
                                    await App.Current.Dispatcher.InvokeAsync(() =>
                                    {
                                        System.Windows.MessageBox.Show(
                                            message,
                                            "Rate Limit Exceeded",
                                            System.Windows.MessageBoxButton.OK,
                                            System.Windows.MessageBoxImage.Warning);
                                    });
                                    return;
                                }
                            }
                        }
                    }
                }
                catch (Exception apiEx)
                {
                    LoggingService.LogToFile($"ORCHESTRATE ❌ Error getting playback from API: {apiEx.Message}\n");
                    if (apiEx is SpotifyWPF.Service.RateLimitException rateEx)
                    {
                        // Show user-friendly message for rate limit
                        int? retryAfter = null;
                        if (rateEx.InnerException is APIException innerApiEx)
                        {
                            retryAfter = GetRetryAfterSeconds(innerApiEx.Response?.Headers);
                        }
                        var message = retryAfter.HasValue ?
                            $"Spotify API rate limit exceeded. Please wait {retryAfter.Value} seconds before trying to play tracks again." :
                            "Spotify API rate limit exceeded. Please wait a moment before trying to play tracks again.";
                        await App.Current.Dispatcher.InvokeAsync(() =>
                        {
                            System.Windows.MessageBox.Show(
                                message,
                                "Rate Limit Exceeded",
                                System.Windows.MessageBoxButton.OK,
                                System.Windows.MessageBoxImage.Warning);
                        });
                        return;
                    }
                }

                if (ct.IsCancellationRequested)
                {
                    LoggingService.LogToFile("ORCHESTRATE ❌ Orchestration cancelled after API device check\n");
                    return;
                }

                // 🎯 PRIORITY 3: Try Web Playback device transfer (original logic, but as fallback)
                if (!string.IsNullOrEmpty(webPlaybackDeviceId) && !string.IsNullOrEmpty(track.Id))
                {

                    try
                    {
                        await _spotify.TransferPlaybackAsync(new[] { webPlaybackDeviceId }, false);
                    }
                    catch (Exception txEx)
                    {
                        LoggingService.LogToFile($"ORCHESTRATE ⚠️ Transfer (play=false) failed: {txEx.Message}\n");
                        if (txEx is SpotifyWPF.Service.RateLimitException rateEx)
                        {
                            // Show user-friendly message for rate limit
                            int? retryAfter = null;
                            if (rateEx.InnerException is APIException apiEx)
                            {
                                retryAfter = GetRetryAfterSeconds(apiEx.Response?.Headers);
                            }
                            var message = retryAfter.HasValue ?
                                $"Spotify API rate limit exceeded. Please wait {retryAfter.Value} seconds before trying to play tracks again." :
                                "Spotify API rate limit exceeded. Please wait a moment before trying to play tracks again.";
                            await App.Current.Dispatcher.InvokeAsync(() =>
                            {
                                System.Windows.MessageBox.Show(
                                    message,
                                    "Rate Limit Exceeded",
                                    System.Windows.MessageBoxButton.OK,
                                    System.Windows.MessageBoxImage.Warning);
                            });
                            return;
                        }
                    }

                    try { await Task.Delay(350, ct); } catch { }

                    if (ct.IsCancellationRequested)
                    {
                        LoggingService.LogToFile("ORCHESTRATE ❌ Orchestration cancelled\n");
                        return;
                    }

                    LoggingService.LogToFile($"[PLAY_CALL] Attempting PlayTrackOnDeviceAsync on WEB device: {webPlaybackDeviceId} track: {track.Id}\n");
                    var ok = await _spotify.PlayTrackOnDeviceAsync(webPlaybackDeviceId, track.Id);
                    LoggingService.LogToFile($"[PLAY_CALL] PlayTrackOnDeviceAsync result for WEB device {webPlaybackDeviceId}: {ok}\n");
                    if (ok)
                    {
                            CurrentTrack = track;
                            PositionMs = 0;
                            IsPlaying = true;
                        LoggingService.LogToFile("ORCHESTRATE ✅ Click-to-play started successfully after Web device transfer\n");
                        try { await _deviceManager.RefreshDevicesAsync(); } catch { }
                        return;
                    }
                    else
                    {
                        LoggingService.LogToFile("ORCHESTRATE ❌ PlayTrackOnDeviceAsync returned false on Web device\n");
                    }

                    // Retry once with transfer(play=true) to force activation if needed, then play again
                    try
                    {
                        System.Diagnostics.Debug.WriteLine("� Retry: transferring with play=true to force activation...");
                        await _spotify.TransferPlaybackAsync(new[] { webPlaybackDeviceId }, true);
                        LoggingService.LogToFile("ORCHESTRATE ⏳ Waiting 400ms after retry transfer\n");
                        try { await Task.Delay(400, ct); } catch { }
                    }
                    catch (Exception txEx2)
                    {
                        LoggingService.LogToFile($"ORCHESTRATE ⚠️ Transfer retry (play=true) failed: {txEx2.Message}\n");
                        if (txEx2 is SpotifyWPF.Service.RateLimitException rateEx)
                        {
                            // Show user-friendly message for rate limit
                            int? retryAfter = null;
                            if (rateEx.InnerException is APIException apiEx)
                            {
                                retryAfter = GetRetryAfterSeconds(apiEx.Response?.Headers);
                            }
                            var message = retryAfter.HasValue ?
                                $"Spotify API rate limit exceeded. Please wait {retryAfter.Value} seconds before trying to play tracks again." :
                                "Spotify API rate limit exceeded. Please wait a moment before trying to play tracks again.";
                            await App.Current.Dispatcher.InvokeAsync(() =>
                            {
                                System.Windows.MessageBox.Show(
                                    message,
                                    "Rate Limit Exceeded",
                                    System.Windows.MessageBoxButton.OK,
                                    System.Windows.MessageBoxImage.Warning);
                            });
                            return;
                        }
                    }

                    if (ct.IsCancellationRequested)
                    {
                        LoggingService.LogToFile("ORCHESTRATE ❌ Orchestration cancelled after retry transfer delay\n");
                        return;
                    }

                    LoggingService.LogToFile($"ORCHESTRATE � Retrying play on device {webPlaybackDeviceId}\n");
                    ok = await _spotify.PlayTrackOnDeviceAsync(webPlaybackDeviceId, track.Id);
                    if (ok)
                    {
                        IsPlaying = true;
                        LoggingService.LogToFile("ORCHESTRATE ✅ Click-to-play started successfully after retry transfer\n");
                        try { await _deviceManager.RefreshDevicesAsync(); } catch { }
                        return;
                    }
                    else
                    {
                        LoggingService.LogToFile("ORCHESTRATE ❌ Retry PlayTrackOnDeviceAsync also returned false on Web device\n");
                    }
                }
                else
                {
                    LoggingService.LogToFile($"ORCHESTRATE ❌ Cannot use Web device transfer: WebPlaybackDeviceId='{webPlaybackDeviceId}', TrackId='{track.Id}'\n");
                }

                if (ct.IsCancellationRequested)
                {
                    LoggingService.LogToFile("ORCHESTRATE ❌ Orchestration cancelled before fallback\n");
                    return;
                }

                // 🎯 PRIORITY 4: Last resort - use JS bridge to start playback locally by URI
                if (!string.IsNullOrEmpty(track.Uri))
                {
                    if (ct.IsCancellationRequested)
                    {
                        LoggingService.LogToFile("ORCHESTRATE ❌ Orchestration cancelled before JS bridge fallback\n");
                        return;
                    }

                    try
                    {
                        // Fix threading issue: dispatch to UI thread
                        await App.Current.Dispatcher.InvokeAsync(async () =>
                        {
                            try { LoggingService.LogToFile($"[WEB_CALL] Calling WebPlaybackBridge.PlayAsync for URI {track.Uri}\n"); } catch { }
                            await _webPlaybackBridge.PlayAsync(new[] { track.Uri });
                            try { LoggingService.LogToFile($"[WEB_CALL] WebPlaybackBridge.PlayAsync invoked for URI {track.Uri}\n"); } catch { }
                        });
                        IsPlaying = true;
                        LoggingService.LogToFile("ORCHESTRATE ✅ Click-to-play started via JS bridge\n");
                        try { await _deviceManager.RefreshDevicesAsync(); } catch { }
                    }
                    catch (Exception jsEx)
                    {
                        LoggingService.LogToFile($"ORCHESTRATE ❌ JS bridge play failed: {jsEx.Message}\n");
                    }
                }                LoggingService.LogToFile("ORCHESTRATE === ORCHESTRATION COMPLETED ===\n");
            }
            catch (Exception ex)
            {
                LoggingService.LogToFile($"ORCHESTRATE 💥 ERROR in OrchestrateClickPlayAsync: {ex.Message}\n");
            }
        }

        #endregion

        #region Timer Event Handlers

        /// <summary>
        /// Handle seek throttle timer tick
        /// </summary>
        public async void OnSeekThrottleTimerTick(object? sender, EventArgs e, ObservableCollection<DeviceModel> devices, string webPlaybackDeviceId)
        {
            try
            {
                _lastSeekSent = DateTimeOffset.UtcNow;

                // Route seek based on active device at the moment of tick
                await _deviceManager.RefreshDevicesAsync();
                var active = devices.FirstOrDefault(d => d.IsActive);
                var useWebBridge = active != null && !string.IsNullOrEmpty(webPlaybackDeviceId) && active.Id == webPlaybackDeviceId;

                if (useWebBridge)
                {
                    await _webPlaybackBridge.SeekAsync(PositionMs);
                }
                else
                {
                    await _spotify.SeekCurrentPlaybackAsync(PositionMs, active?.Id);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Throttled seek error: {ex.Message}");
                try
                {
                    await _deviceManager.RefreshDevicesAsync();
                    var active = devices.FirstOrDefault(d => d.IsActive);
                    await _spotify.SeekCurrentPlaybackAsync(PositionMs, active?.Id);
                }
                catch { }
            }
        }

        /// <summary>
        /// UI progress timer tick: advance slider locally when playing on any device
        /// </summary>
        public void OnUiProgressTick(object? sender, EventArgs e)
        {
            try
            {
                var now = DateTime.UtcNow;
                var delta = now - _lastUiProgressUpdate;
                _lastUiProgressUpdate = now;

                if (!IsPlaying) return;
                if (IsDraggingSlider) return;
                if (CurrentTrack == null || CurrentTrack.DurationMs <= 0) return;

                var inc = (int)Math.Max(0, Math.Min(2000, delta.TotalMilliseconds));
                var newPos = PositionMs + inc;

                // If we hit or passed the end while still playing (auto-advance/repeat), snap to the duration
                // but do not immediately wrap to 0. The authoritative state will be refreshed by polling/bridge
                // and will perform the actual track change or reset when appropriate. This avoids flicker and
                // incorrect immediate resets while audio is still playing.
                if (newPos >= (CurrentTrack.DurationMs - 20))
                {
                    PositionMs = CurrentTrack.DurationMs;
                    _lastUiProgressUpdate = now;
                    // _loggingService.LogDebug($"[UI_PROGRESS] Snapped PositionMs to Duration: {PositionMs}ms (Duration:{CurrentTrack.DurationMs}ms)");
                    return;
                }

                PositionMs = newPos;
                // _loggingService.LogDebug($"[UI_PROGRESS] Advanced PositionMs by {inc}ms -> {newPos} (Duration:{CurrentTrack?.DurationMs}ms)");
            }
            catch { }
        }

        #endregion

        #region State Update Methods

        /// <summary>
        /// Update volume internally without triggering user-set flag
        /// </summary>
        public void UpdateVolumeFromState(double newVolume)
        {
            var clamped = Math.Max(0.0, Math.Min(1.0, newVolume));
            if (Math.Abs(_volume - clamped) > 0.001)
            {
                _volume = clamped;
                // Property change notification handled by PlayerViewModel
            }
        }

        /// <summary>
        /// Clear pending track state
        /// </summary>
        public void ClearPendingTracks()
        {
            _pendingTrackId = null;
            _pendingTrackUntil = DateTimeOffset.MinValue;
            _pendingPlayTrackId = null;
            _pendingPlayUntil = DateTimeOffset.MinValue;
        }

        /// <summary>
        /// Set pending track for click-to-play
        /// </summary>
        public void SetPendingTrack(string trackId, DateTimeOffset until)
        {
            _pendingTrackId = trackId;
            _pendingTrackUntil = until;
        }

        /// <summary>
        /// Set pending play track for device activation
        /// </summary>
        public void SetPendingPlayTrack(string trackId, DateTimeOffset until)
        {
            _pendingPlayTrackId = trackId;
            _pendingPlayUntil = until;
        }

        /// <summary>
        /// Handle state poll timer tick
        /// </summary>
        public async Task OnStatePollTimerTickAsync(object? sender, EventArgs e)
        {
            try
            {
                // 🛑 SMART POLLING: Skip polling during active playback to avoid disrupting real-time updates
                // But resume polling if Web Player is no longer the active device (playback transferred elsewhere)
                var webId = _deviceManager.WebPlaybackDeviceId;
                var devices = _deviceManager.Devices;
                bool isWebPlayerActive = false;
                if (!string.IsNullOrEmpty(webId) && devices != null)
                {
                    // Use local device collection and explicit checks so the compiler can prove non-nullability
                    isWebPlayerActive = devices.Any(d => d.IsActive && string.Equals(d.Id, webId, StringComparison.Ordinal));
                }

                if (IsPlaying && CurrentTrack != null && !string.IsNullOrEmpty(CurrentTrack.Id) && isWebPlayerActive)
                {
                    // _loggingService.LogDebug($"[SMART_POLL] ⏸️ Skipping API poll during active playback of '{CurrentTrack.Title}' on Web Player");
                    return;
                }

                // Keep device selection in sync even if the active device changes outside this app
                await Application.Current.Dispatcher.InvokeAsync(async () =>
                {
                    await _deviceManager.RefreshDevicesAsync();
                    // RefreshPlayerStateAsync will be called by PlayerViewModel
                });
            }
            catch { }
        }

        private int? GetRetryAfterSeconds(IReadOnlyDictionary<string, string>? headers)
        {
            if (headers == null) return null;
            if (headers.TryGetValue("Retry-After", out var value))
            {
                if (int.TryParse(value, out var seconds))
                {
                    return seconds;
                }
            }
            return null;
        }

        #endregion
    }
}