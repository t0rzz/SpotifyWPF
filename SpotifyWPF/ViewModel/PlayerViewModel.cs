using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using GalaSoft.MvvmLight;
using GalaSoft.MvvmLight.Command;
using SpotifyAPI.Web;
using SpotifyWPF.Model;
using SpotifyWPF.Service;
using SpotifyWPF.View;

namespace SpotifyWPF.ViewModel
{           
    /// <summary>
    /// ViewModel for the modern Spotify player with Web Playback SDK integration
    /// </summary>
    public class PlayerViewModel : ViewModelBase, IDisposable
    {
        private readonly ISpotify _spotify;
        private readonly IWebPlaybackBridge _webPlaybackBridge;
        private readonly ILoggingService _loggingService;
    private readonly DispatcherTimer _seekThrottleTimer;
    private readonly DispatcherTimer _statePollTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(Constants.StatePollIntervalMs) };
    private TaskCompletionSource<string>? _webDeviceReadyTcs;
    private readonly DispatcherTimer _uiProgressTimer;
    private DateTime _lastUiProgressUpdate = DateTime.UtcNow;
    private DateTimeOffset _resumeRequestedAt = DateTimeOffset.MinValue;
    private DateTimeOffset _pauseRequestedAt = DateTimeOffset.MinValue;
    // Suppress transient UI reverts right after click-to-play by tracking the intended track
    private string? _pendingTrackId;
    private DateTimeOffset _pendingTrackUntil = DateTimeOffset.MinValue;

    // When a user clicks a track, remember it so we can (re)start playback as soon as the web device becomes active
    private string? _pendingPlayTrackId;
    private DateTimeOffset _pendingPlayUntil = DateTimeOffset.MinValue;

    // Track last known active device to detect activation transitions
    private string? _lastActiveDeviceId;

    // Cancellation for click-to-play orchestration so a new click cancels the previous one
    private CancellationTokenSource? _clickCts;

    // Semaphore to prevent concurrent track operations (race condition fix)
    private readonly SemaphoreSlim _trackOperationSemaphore = new SemaphoreSlim(1, 1);

        // Private fields for properties
        private TrackModel? _currentTrack;
        private bool _isPlaying;
        private int _positionMs;
        private double _volume = 1.0; // Start at 100% volume
        private bool _volumeSetByUser = false; // Track if user has manually set volume
        private bool _isUpdatingVolumeFromState = false; // Prevent recursive volume updates
        private DeviceModel? _selectedDevice;
        private string _webPlaybackDeviceId = string.Empty;
        private bool _isDraggingSlider;
        private DateTimeOffset _lastSeekSent = DateTimeOffset.MinValue;
        private bool _isShuffled;
        private int _repeatMode; // 0=off, 1=context, 2=track
        private DateTimeOffset _lastRepeatUpdate = DateTimeOffset.MinValue; // Track optimistic repeat updates

        // Top tracks collection
        private ObservableCollection<TrackModel> _topTracks = new();

        public PlayerViewModel(ISpotify spotify, IWebPlaybackBridge webPlaybackBridge, ILoggingService loggingService)
        {
            _spotify = spotify ?? throw new ArgumentNullException(nameof(spotify));
            _webPlaybackBridge = webPlaybackBridge ?? throw new ArgumentNullException(nameof(webPlaybackBridge));
            _loggingService = loggingService ?? throw new ArgumentNullException(nameof(loggingService));

            // Initialize collections
            Devices = new ObservableCollection<DeviceModel>();
            TopTracks = new ObservableCollection<TrackModel>();

            // Initialize commands
            PlayPauseCommand = new AsyncRelayCommand(ExecutePlayPauseAsync, CanExecutePlayPause);
            NextCommand = new AsyncRelayCommand(ExecuteNextAsync, CanExecuteNext);
            PrevCommand = new AsyncRelayCommand(ExecutePrevAsync, CanExecutePrev);
            SeekCommand = new AsyncRelayCommand<int>(ExecuteSeekAsync);
            SetVolumeCommand = new AsyncRelayCommand<double>(ExecuteSetVolumeAsync);
            SelectDeviceCommand = new AsyncRelayCommand<DeviceModel>(ExecuteSelectDeviceAsync);
            ClickTrackCommand = new AsyncRelayCommand<object>(ExecuteClickTrackAsync);
            ToggleShuffleCommand = new AsyncRelayCommand(ExecuteToggleShuffleAsync);
            CycleRepeatCommand = new AsyncRelayCommand(ExecuteCycleRepeatAsync);
            RefreshCommand = new AsyncRelayCommand(ExecuteRefreshAsync);

            // Initialize seek throttling timer
            _seekThrottleTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(Constants.SeekThrottleDelayMs)
            };
            _seekThrottleTimer.Tick += OnSeekThrottleTimerTick;

            // Subscribe to Web Playback events
            _webPlaybackBridge.OnPlayerStateChanged += OnPlayerStateChanged;
            _webPlaybackBridge.OnReadyDeviceId += OnReadyDeviceId;
            _webDeviceReadyTcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

            // Fallback state polling (in case web messages are delayed)
            _statePollTimer.Tick += async (s, e) =>
            {
                try
                {
                    // 🛑 SMART POLLING: Skip polling during active playback to avoid disrupting real-time updates
                    // But resume polling if Web Player is no longer the active device (playback transferred elsewhere)
                    bool isWebPlayerActive = !string.IsNullOrEmpty(WebPlaybackDeviceId) && 
                                           SelectedDevice != null && 
                                           SelectedDevice.Id == WebPlaybackDeviceId;
                    
                    if (IsPlaying && CurrentTrack != null && !string.IsNullOrEmpty(CurrentTrack.Id) && isWebPlayerActive)
                    {
                        _loggingService.LogDebug($"[SMART_POLL] ⏸️ Skipping API poll during active playback of '{CurrentTrack.Title}' on Web Player");
                        return;
                    }

                    // Keep device selection in sync even if the active device changes outside this app
                    await RefreshDevicesAsync();
                    await RefreshPlayerStateAsync();
                }
                catch { }
            };
            _statePollTimer.Start();

            // UI progress timer to advance slider when playing locally
            _uiProgressTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            _lastUiProgressUpdate = DateTime.UtcNow;
            _uiProgressTimer.Tick += OnUiProgressTick;
            _uiProgressTimer.Start();

            // Don't load top tracks in constructor - will be loaded during initialization
        }

        #region Public Properties Access

        /// <summary>
        /// Access to the WebPlayback bridge for initialization
        /// </summary>
        public IWebPlaybackBridge WebPlaybackBridge => _webPlaybackBridge;

        #endregion

        #region Properties

        /// <summary>
        /// Currently playing track
        /// </summary>
        public TrackModel? CurrentTrack
        {
            get => _currentTrack;
            set
            {
                _currentTrack = value;
                RaisePropertyChanged();
                RaiseCommandsCanExecuteChanged();
            }
        }

        /// <summary>
        /// Whether playback is active
        /// </summary>
        public bool IsPlaying
        {
            get => _isPlaying;
            set
            {
                _isPlaying = value;
                RaisePropertyChanged();
                RaiseCommandsCanExecuteChanged();
            }
        }

        /// <summary>
        /// Current position in milliseconds
        /// </summary>
        public int PositionMs
        {
            get => _positionMs;
            set
            {
                _positionMs = value;
                RaisePropertyChanged();
            }
        }

        /// <summary>
        /// Volume level (0.0 - 1.0)
        /// </summary>
        public double Volume
        {
            get => _volume;
            set
            {
                if (_isUpdatingVolumeFromState) return; // Prevent recursive updates
                
                _volume = Math.Max(0.0, Math.Min(1.0, value));
                _volumeSetByUser = true; // Mark that user has manually set volume
                RaisePropertyChanged();

                // Apply volume immediately when bound value changes
                // Fire-and-forget with internal throttling to avoid flooding
                _ = ExecuteSetVolumeAsync(_volume);
            }
        }

        /// <summary>
        /// Update volume internally without triggering user-set flag or commands
        /// </summary>
        private void UpdateVolumeFromState(double newVolume)
        {
            _isUpdatingVolumeFromState = true;
            try
            {
                var clamped = Math.Max(0.0, Math.Min(1.0, newVolume));
                if (Math.Abs(_volume - clamped) > 0.001)
                {
                    _volume = clamped;
                    RaisePropertyChanged(nameof(Volume));
                }
            }
            finally
            {
                _isUpdatingVolumeFromState = false;
            }
        }

        /// <summary>
        /// Available playback devices
        /// </summary>
        public ObservableCollection<DeviceModel> Devices { get; }

        /// <summary>
        /// Currently selected device
        /// </summary>
        public DeviceModel? SelectedDevice
        {
            get => _selectedDevice;
            set
            {
                _selectedDevice = value;
                RaisePropertyChanged();
            }
        }

        /// <summary>
        /// Web Playback SDK device ID
        /// </summary>
        public string WebPlaybackDeviceId
        {
            get => _webPlaybackDeviceId;
            private set
            {
                var oldValue = _webPlaybackDeviceId;
                _webPlaybackDeviceId = value;
                _loggingService.LogDebug($"[WEB_DEVICE] Property setter: WebPlaybackDeviceId changed from '{oldValue}' to '{value}'");
                RaisePropertyChanged();
            }
        }

        /// <summary>
        /// Whether the user is currently dragging the position slider
        /// </summary>
        public bool IsDraggingSlider
        {
            get => _isDraggingSlider;
            set
            {
                _isDraggingSlider = value;
                RaisePropertyChanged();
            }
        }

        /// <summary>
        /// Top tracks for horizontal scrolling display
        /// </summary>
        public ObservableCollection<TrackModel> TopTracks
        {
            get => _topTracks;
            set
            {
                _topTracks = value;
                RaisePropertyChanged();
            }
        }

        /// <summary>
        /// Shuffle state
        /// </summary>
        public bool IsShuffled
        {
            get => _isShuffled;
            set
            {
                _isShuffled = value;
                RaisePropertyChanged();
            }
        }

        /// <summary>
        /// Repeat mode: 0 = off, 1 = context, 2 = track
        /// </summary>
        public int RepeatMode
        {
            get => _repeatMode;
            set
            {
                _repeatMode = value;
                RaisePropertyChanged();
            }
        }

        #endregion

        #region Commands

        public ICommand PlayPauseCommand { get; }
        public ICommand NextCommand { get; }
        public ICommand PrevCommand { get; }
        public ICommand SeekCommand { get; }
        public ICommand SetVolumeCommand { get; }
        public ICommand SelectDeviceCommand { get; }
        public ICommand ClickTrackCommand { get; }
        public ICommand ToggleShuffleCommand { get; }
        public ICommand CycleRepeatCommand { get; }
        public ICommand RefreshCommand { get; }

        #endregion

        #region Command Implementations

        /// <summary>
        /// Execute play/pause command
        /// </summary>
        private async Task ExecutePlayPauseAsync()
        {
            try
            {
                // Always start with a fresh devices snapshot
                await RefreshDevicesAsync();

                if (IsPlaying)
                {
                    // If no active device, avoid calling Pause on API/bridge; just update UI and exit quickly
                    var hasActiveDevice = Devices.Any(d => d.IsActive);
                    if (!hasActiveDevice)
                    {
                        System.Diagnostics.Debug.WriteLine("⏸ PlayPause: No active device detected; skipping Pause call.");
                        IsPlaying = false;
                        _pauseRequestedAt = DateTimeOffset.UtcNow;
                        return;
                    }

                    // Decide control path based on current selection after refresh
                    var useWebBridgeQuick = SelectedDevice != null && !string.IsNullOrEmpty(WebPlaybackDeviceId) && SelectedDevice.Id == WebPlaybackDeviceId;

                    // Optimistic UI update first for snappy feel
                    IsPlaying = false;
                    _pauseRequestedAt = DateTimeOffset.UtcNow;

                    System.Diagnostics.Debug.WriteLine($"⏸ PlayPause: Pausing via {(useWebBridgeQuick ? "WebPlaybackBridge" : "Spotify API")} (selected={SelectedDevice?.Id}, webId={WebPlaybackDeviceId})");
                    try
                    {
                        if (useWebBridgeQuick)
                        {
                            await _webPlaybackBridge.PauseAsync();
                        }
                        else
                        {
                            var active = Devices.FirstOrDefault(d => d.IsActive);
                            var targetDeviceId = active?.Id ?? SelectedDevice?.Id ?? WebPlaybackDeviceId;
                            await _spotify.PauseCurrentPlaybackAsync(targetDeviceId);
                        }
                    }
                    catch (Exception pauseEx)
                    {
                        System.Diagnostics.Debug.WriteLine($"⚠️ Pause via primary path failed: {pauseEx.Message}. Falling back to Spotify API.");
                        try 
                        { 
                            var active = Devices.FirstOrDefault(d => d.IsActive);
                            var targetDeviceId = active?.Id ?? SelectedDevice?.Id ?? WebPlaybackDeviceId;
                            await _spotify.PauseCurrentPlaybackAsync(targetDeviceId); 
                        } 
                        catch { }
                    }
                }
                else
                {
                    // If there is no active device, promote the in-app Web Playback device as active first
                    var hasActive = Devices.Any(d => d.IsActive);
                    if (!hasActive)
                    {
                        // Wait briefly for the Web Playback device id if not yet known
                        if (string.IsNullOrEmpty(WebPlaybackDeviceId))
                        {
                            var waitedId = await WaitForWebDeviceIdAsync(TimeSpan.FromSeconds(4));
                            if (!string.IsNullOrEmpty(waitedId))
                            {
                                WebPlaybackDeviceId = waitedId;
                            }
                        }

                        var ensured = await EnsureAppDeviceActiveAsync();
                        System.Diagnostics.Debug.WriteLine($"🎯 EnsureAppDeviceActive before Resume result: {ensured}");
                        // Refresh snapshot after potential transfer
                        await RefreshDevicesAsync();
                    }

                    // Decide control path based on updated selection
                    var useWebBridgeQuick = SelectedDevice != null && !string.IsNullOrEmpty(WebPlaybackDeviceId) && SelectedDevice.Id == WebPlaybackDeviceId;

                    // Optimistic UI update first for snappy feel
                    IsPlaying = true;
                    _resumeRequestedAt = DateTimeOffset.UtcNow;

                    System.Diagnostics.Debug.WriteLine($"▶️ PlayPause: Resuming via {(useWebBridgeQuick ? "WebPlaybackBridge" : "Spotify API")} (selected={SelectedDevice?.Id}, webId={WebPlaybackDeviceId})");
                    try
                    {
                        if (useWebBridgeQuick)
                        {
                            // Proactively enable audio context on the web player
                            await _webPlaybackBridge.ResumeAsync();
                        }
                        else
                        {
                            var active = Devices.FirstOrDefault(d => d.IsActive);
                            var targetDeviceId = active?.Id ?? SelectedDevice?.Id ?? WebPlaybackDeviceId;
                            await _spotify.ResumeCurrentPlaybackAsync(targetDeviceId);
                        }
                    }
                    catch (Exception resumeEx)
                    {
                        System.Diagnostics.Debug.WriteLine($"⚠️ Resume via primary path failed: {resumeEx.Message}. Falling back to Spotify API or web bridge.");
                        try 
                        { 
                            var active = Devices.FirstOrDefault(d => d.IsActive);
                            var targetDeviceId = active?.Id ?? SelectedDevice?.Id ?? WebPlaybackDeviceId;
                            await _spotify.ResumeCurrentPlaybackAsync(targetDeviceId); 
                        }
                        catch { try { await _webPlaybackBridge.ResumeAsync(); } catch { } }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"PlayPause error: {ex.Message}");
            }
            finally
            {
                // Burst refresh to quickly populate UI after user action
                SchedulePostActionStateRefresh();
            }
        }

        /// <summary>
        /// Execute next track command
        /// </summary>
        private async Task ExecuteNextAsync()
        {
            try
            {
                _loggingService.LogDebug($"[NEXT] ⏭️ ExecuteNextAsync called");
                await RefreshDevicesAsync();
                var active = Devices.FirstOrDefault(d => d.IsActive);
                var targetDeviceId = active?.Id;

                System.Diagnostics.Debug.WriteLine($"⏭️ SkipToNext: skipping to next track on device {targetDeviceId ?? "(current)"}");
                _loggingService.LogDebug($"[NEXT] ⏭️ Calling SkipToNextAsync for device {targetDeviceId ?? "(current)"}");
                var ok = await _spotify.SkipToNextAsync(targetDeviceId);
                if (ok)
                {
                    _loggingService.LogDebug($"[NEXT] ✅ SkipToNextAsync succeeded, scheduling refresh");
                    // Refresh state shortly after to sync UI
                    _ = Task.Delay(300).ContinueWith(async _ => await RefreshPlayerStateAsync());
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
        private async Task ExecutePrevAsync()
        {
            try
            {
                // In a real implementation, this would call Spotify API to skip to previous track
                // For now, we'll use a placeholder
                await Task.Delay(100);
                System.Diagnostics.Debug.WriteLine("Previous track requested");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Previous error: {ex.Message}");
            }
        }

        /// <summary>
        /// Execute seek command with throttling
        /// </summary>
        private async Task ExecuteSeekAsync(int positionMs)
        {
            try
            {
                var now = DateTimeOffset.UtcNow;
                
                // Throttle seeks to avoid overwhelming the API
                if (now - _lastSeekSent < TimeSpan.FromMilliseconds(120))
                {
                    // Start/restart the throttle timer
                    _seekThrottleTimer.Stop();
                    _seekThrottleTimer.Start();
                    return;
                }

                _lastSeekSent = now;

                // Decide control path based on active device
                await RefreshDevicesAsync();
                var active = Devices.FirstOrDefault(d => d.IsActive);
                var useWebBridge = active != null && !string.IsNullOrEmpty(WebPlaybackDeviceId) && active.Id == WebPlaybackDeviceId;

                if (useWebBridge)
                {
                    await _webPlaybackBridge.SeekAsync(positionMs);
                }
                else
                {
                    await _spotify.SeekCurrentPlaybackAsync(positionMs, active?.Id);
                }
                
                // Update UI immediately for responsiveness
                PositionMs = positionMs;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Seek error: {ex.Message}");
                try 
                { 
                    await RefreshDevicesAsync();
                    var active = Devices.FirstOrDefault(d => d.IsActive);
                    await _spotify.SeekCurrentPlaybackAsync(positionMs, active?.Id); 
                } 
                catch { }
            }
        }

        /// <summary>
        /// Execute set volume command
        /// </summary>
    private DateTime _lastVolumeSent = DateTime.MinValue;

        private async Task ExecuteSetVolumeAsync(double volume)
        {
            try
            {
                // Clamp and snap tiny values to 0 to avoid near-silent confusion
                var clamped = Math.Max(0.0, Math.Min(1.0, volume));
                if (clamped > 0 && clamped < 0.005) clamped = 0.0;

                // Throttle to prevent flooding the bridge and API while dragging
                var now = DateTime.UtcNow;
                if ((now - _lastVolumeSent) < TimeSpan.FromMilliseconds(80) && Math.Abs(clamped - _volume) < 0.01)
                {
                    return;
                }

                _lastVolumeSent = now;
                _volume = clamped;
                _volumeSetByUser = true; // Mark that volume has been set
                RaisePropertyChanged(nameof(Volume));

                // Always apply to Web Playback SDK (local bridge) for immediate UX when app is the device
                try
                {
                    await _webPlaybackBridge.SetVolumeAsync(_volume);
                }
                catch (Exception bridgeEx)
                {
                    System.Diagnostics.Debug.WriteLine($"SetVolume bridge error (non-fatal): {bridgeEx.Message}");
                }

                // Also apply via Spotify Web API to the currently active device (web or remote)
                // This ensures the volume changes even if the SDK isn't the active controller yet.
                await RefreshDevicesAsync();
                var active = Devices.FirstOrDefault(d => d.IsActive);
                if (active != null && !string.IsNullOrEmpty(active.Id))
                {
                    var percent = (int)Math.Round(_volume * 100);
                    try
                    {
                        await _spotify.SetVolumePercentOnDeviceAsync(active.Id, percent);
                    }
                    catch (Exception apiEx)
                    {
                        System.Diagnostics.Debug.WriteLine($"SetVolume API error (non-fatal): {apiEx.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SetVolume error: {ex.Message}");
            }
        }

        /// <summary>
        /// Execute device selection command
        /// </summary>
        private async Task ExecuteToggleShuffleAsync()
        {
            try
            {
                await RefreshDevicesAsync();
                var active = Devices.FirstOrDefault(d => d.IsActive);
                var targetDeviceId = active?.Id;

                var newState = !IsShuffled;
                System.Diagnostics.Debug.WriteLine($"🔀 ToggleShuffle: setting shuffle={(newState ? "on" : "off")} for device {targetDeviceId ?? "(current)"}");
                var ok = await _spotify.SetShuffleAsync(newState, targetDeviceId);
                if (ok)
                {
                    IsShuffled = newState; // Optimistic update
                }

                // Refresh state shortly after to sync UI from authoritative source
                _ = Task.Delay(300).ContinueWith(async _ => await RefreshPlayerStateAsync());
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ToggleShuffle error: {ex.Message}");
            }
        }

        private async Task ExecuteCycleRepeatAsync()
        {
            try
            {
                await RefreshDevicesAsync();
                var active = Devices.FirstOrDefault(d => d.IsActive);
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

                // Refresh state shortly after to sync UI
                _ = Task.Delay(300).ContinueWith(async _ => await RefreshPlayerStateAsync());
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"CycleRepeat error: {ex.Message}");
            }
        }

        private async Task ExecuteRefreshAsync()
        {
            try
            {
                _loggingService.LogDebug($"[MANUAL_REFRESH] 🔄 Manual refresh requested by user");
                await RefreshDevicesAsync();
                await RefreshPlayerStateAsync();
                _loggingService.LogDebug($"[MANUAL_REFRESH] ✅ Manual refresh completed");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Manual refresh error: {ex.Message}");
            }
        }

        private async Task ExecuteSelectDeviceAsync(DeviceModel device)
        {
            try
            {
                if (device == null) return;

                SelectedDevice = device;
                
                // Transfer playback to selected device (false = keep current playback state)
                await _spotify.TransferPlaybackAsync(new[] { device.Id }, false);

                // Give Spotify a brief moment to apply the transfer, then resync devices and state
                await Task.Delay(300);
                await RefreshDevicesAsync();

                // If the selected device became active, keep it selected; otherwise, switch to whatever is active
                var active = Devices.FirstOrDefault(d => d.IsActive);
                if (active != null)
                {
                    SelectedDevice = active;
                }

                // Force a fresh player state read so PositionMs/IsPlaying reflect the transferred playback
                _lastUiProgressUpdate = DateTime.UtcNow; // reset UI progress clock base
                await RefreshPlayerStateAsync();
                
                // Update WebPlaybackDeviceId if this is the web player device
                if (device.Id == WebPlaybackDeviceId)
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
                        await RefreshDevicesAsync();
                        var active = Devices.FirstOrDefault(d => d.IsActive);
                        if (active != null) SelectedDevice = active;
                        _lastUiProgressUpdate = DateTime.UtcNow;
                        await RefreshPlayerStateAsync();
                    }
                    catch (Exception retryEx)
                    {
                        System.Diagnostics.Debug.WriteLine($"SelectDevice retry error: {retryEx.Message}");
                    }
                }
            }
        }

        /// <summary>
        /// Execute click track command (optimistic playback start)
        /// </summary>
    private async Task ExecuteClickTrackAsync(object trackObj)
        {
            var operationId = Guid.NewGuid().ToString().Substring(0, 8);
            _loggingService.LogDebug("TRACK_CLICK", $"=== TRACK CLICK STARTED [OP:{operationId}] ===");

            try
            {
                if (trackObj == null)
                {
                    _loggingService.LogDebug("TRACK_CLICK", $"❌ NULL TRACK OBJECT RECEIVED [OP:{operationId}]");
                    System.Diagnostics.Debug.WriteLine($"❌ CLICK TRACK: NULL TRACK [OP:{operationId}]");
                    return;
                }

                _loggingService.LogDebug("TRACK_CLICK", $"📦 Received track object of type: {trackObj.GetType().Name} [OP:{operationId}]");

                // Convert to TrackModel if needed
                TrackModel track;
                if (trackObj is TrackModel trackModel)
                {
                    track = trackModel;
                    _loggingService.LogDebug("TRACK_CLICK", $"✅ Using existing TrackModel: {track.Title}");
                }
                else if (trackObj is FullTrack apiTrack)
                {
                    _loggingService.LogDebug("TRACK_CLICK", $"🔄 Converting SpotifyAPI.Web.FullTrack to TrackModel");
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
                    _loggingService.LogDebug("TRACK_CLICK", $"✅ Converted to TrackModel: {track.Title} by {track.Artist}");
                }
                else
                {
                    _loggingService.LogDebug("TRACK_CLICK", $"❌ UNSUPPORTED TRACK TYPE: {trackObj.GetType().FullName}");
                    System.Diagnostics.Debug.WriteLine($"❌ CLICK TRACK: Unsupported track type: {trackObj.GetType()}");
                    return;
                }

                _loggingService.LogDebug("TRACK_CLICK", $"🎵 Processing track: '{track.Title}' by '{track.Artist}' (ID: {track.Id})");

                System.Diagnostics.Debug.WriteLine($"🎵 ClickTrack: {track.Title} by {track.Artist}");

                // Optimistic UI updates (instant feedback)
                _loggingService.LogDebug("TRACK_CLICK", "🎨 Updating UI with optimistic changes");
                CurrentTrack = track;
                PositionMs = 0;
                // Do not optimistically flip to playing; wait for confirmation to avoid Pause-on-nothing scenarios
                IsPlaying = false;
                _resumeRequestedAt = DateTimeOffset.UtcNow; // Suppress stale paused states from API shortly after click-to-play

                _loggingService.LogDebug("TRACK_CLICK", $"⏰ Set resume suppression until: {_resumeRequestedAt.AddSeconds(2):HH:mm:ss}");

                // Suppress transient reversion and remember to auto-start this track on web activation
                _pendingTrackId = track.Id;
                _pendingTrackUntil = DateTimeOffset.UtcNow.AddSeconds(4);
                _pendingPlayTrackId = track.Id;
                _pendingPlayUntil = DateTimeOffset.UtcNow.AddSeconds(8);

                _loggingService.LogDebug("TRACK_CLICK", $"🎯 Set pending track ID: {track.Id}");
                _loggingService.LogDebug("TRACK_CLICK", $"⏳ Pending track valid until: {_pendingTrackUntil:HH:mm:ss}");
                _loggingService.LogDebug("TRACK_CLICK", $"▶️ Pending play valid until: {_pendingPlayUntil:HH:mm:ss}");

                // Nudge the web player's audio context immediately under the user click gesture
                _loggingService.LogDebug("TRACK_CLICK", "🔊 Nudging web player audio context");
                try { await _webPlaybackBridge.ResumeAsync(); } catch { }

                // Cancel any previous click-to-play orchestration
                _loggingService.LogDebug("TRACK_CLICK", "🛑 Cancelling previous click-to-play orchestration");
                _clickCts?.Cancel();
                _clickCts = new CancellationTokenSource();
                var token = _clickCts.Token;

                // Fire-and-forget the heavy orchestration so clicks remain responsive
                // Use semaphore to prevent concurrent track operations
                _loggingService.LogDebug("TRACK_CLICK", $"🚀 Starting OrchestrateClickPlayAsync (fire-and-forget with semaphore) [OP:{operationId}]");
                _ = Task.Run(async () =>
                {
                    await _trackOperationSemaphore.WaitAsync(token);
                    try
                    {
                        await OrchestrateClickPlayAsync(track, token, operationId);
                    }
                    finally
                    {
                        _trackOperationSemaphore.Release();
                    }
                }, token);
            }
            catch (Exception ex)
            {
                _loggingService.LogError($"💥 ERROR in ExecuteClickTrackAsync: {ex.Message}", ex);
                System.Diagnostics.Debug.WriteLine($"ClickTrack error (fast path): {ex.Message}");
                IsPlaying = false;
            }
            finally
            {
                // Reset UI progress baseline and burst refresh to quickly sync UI after starting playback
                _lastUiProgressUpdate = DateTime.UtcNow;
                SchedulePostActionStateRefresh();
                _loggingService.LogDebug("TRACK_CLICK", "=== TRACK CLICK COMPLETED ===");
            }
        }

        // Small helper: poll API until the expected device becomes active
        private async Task<bool> WaitForActiveDeviceAsync(string expectedDeviceId, int attempts = 10, int delayMs = 250)
        {
            for (var i = 0; i < attempts; i++)
            {
                try
                {
                    var playback = await _spotify.GetCurrentPlaybackAsync();
                    var activeId = playback?.Device?.Id;
                    if (!string.IsNullOrEmpty(activeId) && string.Equals(activeId, expectedDeviceId, StringComparison.Ordinal))
                    {
                        return true;
                    }
                }
                catch { }
                await Task.Delay(delayMs);
            }
            return false;
        }

        /// <summary>
        /// Ensure there is an active device; if none, transfer playback to the Web Playback SDK device (this app)
        /// </summary>
    private async Task<bool> EnsureAppDeviceActiveAsync()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("🎵 EnsureAppDeviceActiveAsync: Starting device check");
                
                // Refresh devices to get current active status
                await RefreshDevicesAsync();

                // Fast-path: if there is already an active device, honor it
                var activeDevice = Devices.FirstOrDefault(d => d.IsActive);
                if (activeDevice != null)
                {
                    System.Diagnostics.Debug.WriteLine($"🎵 Active device found: {activeDevice.Name} ({activeDevice.Id})");
                    SelectedDevice = activeDevice;
                    return true;
                }

                // If WebPlaybackDeviceId not yet known, wait briefly for the Web Playback SDK to report it
                if (string.IsNullOrEmpty(WebPlaybackDeviceId))
                {
                    var waitedId = await WaitForWebDeviceIdAsync(TimeSpan.FromSeconds(4));
                    if (!string.IsNullOrEmpty(waitedId))
                    {
                        WebPlaybackDeviceId = waitedId;
                        System.Diagnostics.Debug.WriteLine($"🎵 Web device became ready: {WebPlaybackDeviceId}");
                    }
                }

                System.Diagnostics.Debug.WriteLine($"🎵 No active device found. WebPlaybackDeviceId: {WebPlaybackDeviceId}");
                System.Diagnostics.Debug.WriteLine($"🎵 Available devices: {string.Join(", ", Devices.Select(d => $"{d.Name}({d.Id}, Active:{d.IsActive})"))}");

                // If WebPlaybackDeviceId is empty, try to find our web player device in the devices list
                if (string.IsNullOrEmpty(WebPlaybackDeviceId))
                {
                    var webDevice = Devices.FirstOrDefault(d => d.Name == "Web Player" || d.Name.Contains("SpotifyWPF"));
                    if (webDevice != null)
                    {
                        System.Diagnostics.Debug.WriteLine($"🎵 Found Web Player device in list: {webDevice.Id}");
                        WebPlaybackDeviceId = webDevice.Id;
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine("⚠️ WebPlaybackDeviceId is empty and no Web Player found in devices list");
                        System.Diagnostics.Debug.WriteLine("⚠️ Web Playback SDK may not be ready yet. Trying to reconnect...");
                        
                        // Try to reconnect the Web Playback bridge
                        try
                        {
                            await _webPlaybackBridge.ConnectAsync();
                            await Task.Delay(1000); // Wait for connection and ready event
                            await RefreshDevicesAsync();
                            
                            // Try to find the device again
                            var webDeviceRetry = Devices.FirstOrDefault(d => d.Name == "Web Player" || d.Name.Contains("SpotifyWPF"));
                            if (webDeviceRetry != null)
                            {
                                System.Diagnostics.Debug.WriteLine($"🎵 Found Web Player device after reconnect: {webDeviceRetry.Id}");
                                WebPlaybackDeviceId = webDeviceRetry.Id;
                            }
                        }
                        catch (Exception reconnectEx)
                        {
                            System.Diagnostics.Debug.WriteLine($"❌ Error reconnecting Web Playback: {reconnectEx.Message}");
                        }
                    }
                }

                // No active device – try to promote our Web Playback device
                if (!string.IsNullOrEmpty(WebPlaybackDeviceId))
                {
                    System.Diagnostics.Debug.WriteLine($"🎵 Transferring playback to Web Playback device: {WebPlaybackDeviceId} (with play=true to activate) ");
                    await _spotify.TransferPlaybackAsync(new[] { WebPlaybackDeviceId }, true);
                    System.Diagnostics.Debug.WriteLine("🎵 TransferPlayback call completed (play=true), waiting for device switch...");
                    
                    // Poll until device becomes active (up to ~4.5s)
                    var attempts = 0;
                    while (attempts++ < 15)
                    {
                        await Task.Delay(300);
                        await RefreshDevicesAsync();
                        var webDevice = Devices.FirstOrDefault(d => d.Id == WebPlaybackDeviceId);
                        if (webDevice != null)
                        {
                            System.Diagnostics.Debug.WriteLine($"🎵 Web device polled: {webDevice.Name}, IsActive: {webDevice.IsActive}");
                            if (webDevice.IsActive)
                            {
                                SelectedDevice = webDevice;
                                return true;
                            }
                        }
                    }
                    System.Diagnostics.Debug.WriteLine("⚠️ Web device did not become active within timeout");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("⚠️ WebPlaybackDeviceId is still empty after all attempts, cannot transfer playback");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ EnsureAppDeviceActiveAsync error: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"❌ Stack trace: {ex.StackTrace}");
            }
            return false;
        }

        #endregion

        #region Command CanExecute

    private bool CanExecutePlayPause() => true;
    private bool CanExecuteNext() => true;
    private bool CanExecutePrev() => true;

        #endregion

        private void SchedulePostActionStateRefresh()
        {
            var delays = new int[] { 0, 250, 750, 1500 };
            foreach (var d in delays)
            {
                _ = Task.Delay(d).ContinueWith(async _ =>
                {
                    try
                    {
                        await RefreshDevicesAsync();
                        await RefreshPlayerStateAsync();
                    }
                    catch { }
                });
            }
        }

        #region Event Handlers

        // Track last state update to prevent duplicate processing
        private DateTimeOffset _lastStateUpdate = DateTimeOffset.MinValue;
        private string _lastStateKey = string.Empty;
        
        // Track last auto-advance to prevent multiple SkipToNext calls
        private DateTimeOffset _lastAutoAdvance = DateTimeOffset.MinValue;
        
        // Track the trackId that was auto-advanced from to suppress reappearance
        private string _lastAutoAdvancedTrackId = string.Empty;
        private DateTimeOffset _lastAutoAdvancedUntil = DateTimeOffset.MinValue;
        
        // Track volume update timing to prevent rapid changes during transitions
        private DateTimeOffset _lastVolumeUpdate = DateTimeOffset.MinValue;
        
        // Track recent disconnected states to prevent polling from overriding Web SDK
        private DateTimeOffset _lastDisconnectedState = DateTimeOffset.MinValue;
        
        // Track the track ID that was disconnected to suppress reappearance
        private string _lastDisconnectedTrackId = string.Empty;

        /// <summary>
        /// Handle player state changes from Web Playback SDK - Enhanced for external control
        /// </summary>
        private void OnPlayerStateChanged(PlayerState state)
        {
            try
            {
                // 🛑 DEDUPLICATION: Prevent processing the same state multiple times within 500ms
                var now = DateTimeOffset.UtcNow;
                var stateKey = $"{state.TrackId ?? "null"}|{state.IsPlaying}|{state.PositionMs}|{state.Shuffled}";
                
                if ((now - _lastStateUpdate) < TimeSpan.FromMilliseconds(500) && stateKey == _lastStateKey)
                {
                    _loggingService.LogDebug($"[STATE_UPDATE] 🛑 Duplicate state ignored: {stateKey}");
                    return;
                }
                
                _lastStateUpdate = now;
                _lastStateKey = stateKey;

                _loggingService.LogDebug($"[STATE_UPDATE] === RECEIVED STATE UPDATE ===");
                _loggingService.LogDebug($"[STATE_UPDATE] Track: {state.TrackName ?? "null"}, Playing: {state.IsPlaying}, Position: {state.PositionMs}ms");
                _loggingService.LogDebug($"[STATE_UPDATE] TrackId: {state.TrackId ?? "null"}, Duration: {state.DurationMs}ms");

                // 🔥 CRITICAL: Ensure UI updates happen on UI thread
                Application.Current.Dispatcher.Invoke(() =>
                {
                    UpdateUIFromPlayerState(state);
                });

                // 🛑 SMART POLLING PAUSE: Prevent API from overwriting real-time updates
                // Use shorter pause for remote devices (2 seconds) vs Web Player (10 seconds)
                bool isWebPlayerActive = !string.IsNullOrEmpty(WebPlaybackDeviceId) && 
                                       SelectedDevice != null && 
                                       SelectedDevice.Id == WebPlaybackDeviceId;
                
                int pauseDurationMs = isWebPlayerActive ? 10000 : 2000; // 10s for Web Player, 2s for remote
                
                _statePollTimer.Stop();
                Task.Delay(pauseDurationMs).ContinueWith(_ =>
                {
                    Application.Current.Dispatcher.Invoke(() => _statePollTimer.Start());
                });
            }
            catch (Exception ex)
            {
                _loggingService.LogError($"[STATE_UPDATE] ❌ OnPlayerStateChanged error: {ex.Message}", ex);
                System.Diagnostics.Debug.WriteLine($"❌ OnPlayerStateChanged error: {ex.Message}");
            }
        }

        /// <summary>
        /// Update UI from player state - must be called on UI thread
        /// </summary>
        private void UpdateUIFromPlayerState(PlayerState state)
        {
            try
            {
                // 🎵 CRITICAL: Capture shuffle state BEFORE any updates from incoming state
                // This ensures we detect shuffle auto-advance correctly when tracks end
                bool shuffleWasEnabledAtStart = IsShuffled;

                // Guard against spurious "playing" states when there is no active device and no track info
                if (SelectedDevice == null && state.IsPlaying && string.IsNullOrEmpty(state.TrackId))
                {
                    var sinceResume = DateTimeOffset.UtcNow - _resumeRequestedAt;
                    if (sinceResume > TimeSpan.FromSeconds(5))
                    {
                        System.Diagnostics.Debug.WriteLine("🛑 Ignoring spurious playing state: no active device and no track ID (no recent resume)");
                        return;
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine("ℹ️ Accepting transitional playing state after recent resume (track info pending)...");
                    }
                }

                // Detect device transfer: if we have a current track but incoming state has no artwork, preserve existing artwork
                bool isDeviceTransfer = CurrentTrack != null &&
                                       !string.IsNullOrEmpty(state.TrackId) &&
                                       state.TrackId == CurrentTrack.Id &&
                                       string.IsNullOrEmpty(state.ImageUrl) &&
                                       CurrentTrack.AlbumArtUri != null;

                // 🎵 ENHANCED TRANSFER DETECTION: Also detect transfers by device change
                // Since PlayerState doesn't have DeviceId, we detect transfers by checking if we're getting
                // incomplete state (missing artwork) for the current track when device might have changed
                bool isDeviceIdTransfer = false; // PlayerState doesn't provide device info, so we rely on artwork-based detection

                if (isDeviceTransfer || isDeviceIdTransfer)
                {
                    string transferType = isDeviceTransfer ? "artwork-based" : "device ID-based";
                    _loggingService.LogDebug($"[DEVICE_TRANSFER] 🎵 Detected device transfer ({transferType}) for track '{CurrentTrack?.Title ?? "unknown"}' - preserving existing artwork");

                    // 🎵 POSITION PRESERVATION: During transfer, if position is 0 but we have a valid track, preserve current position
                    bool shouldPreservePosition = isDeviceTransfer &&  // Since we can't detect device ID changes from PlayerState, rely on artwork-based detection
                                                 state.PositionMs == 0 &&
                                                 CurrentTrack != null &&
                                                 CurrentTrack.DurationMs > 0 &&
                                                 PositionMs > 0;

                    if (shouldPreservePosition)
                    {
                        _loggingService.LogDebug($"[DEVICE_TRANSFER] ⏱️ Preserving position during transfer - Current: {PositionMs}ms, Incoming: {state.PositionMs}ms");
                        // Don't update position - keep current value
                        PositionMs = PositionMs; // Explicitly preserve
                    }
                    else
                    {
                        if (!IsDraggingSlider)
                        {
                            PositionMs = state.PositionMs;
                        }
                    }

                    // Don't update the track info that would clear the artwork
                    // Only update playback state
                    IsPlaying = state.IsPlaying;
                    return;
                }

                // Suppress transient reversion to previous track during a short pending window after click-to-play
                if (!string.IsNullOrEmpty(_pendingTrackId) && DateTimeOffset.UtcNow <= _pendingTrackUntil)
                {
                    var incomingIdTmp = state.TrackId ?? string.Empty;
                    if (!string.IsNullOrEmpty(incomingIdTmp) && incomingIdTmp != _pendingTrackId && CurrentTrack != null && CurrentTrack.Id == _pendingTrackId)
                    {
                        System.Diagnostics.Debug.WriteLine($"⏳ Suppressing state that would revert to previous track (incoming={incomingIdTmp}, pending={_pendingTrackId})");
                        return;
                    }
                }

                // If the incoming state confirms the pending track, clear the suppression window
                if (!string.IsNullOrEmpty(_pendingTrackId) && !string.IsNullOrEmpty(state.TrackId) && state.TrackId == _pendingTrackId)
                {
                    System.Diagnostics.Debug.WriteLine("✅ Pending track now active; clearing suppression window.");
                    _pendingTrackId = null;
                    _pendingTrackUntil = DateTimeOffset.MinValue;
                }

                // Ignore position updates while dragging slider to prevent feedback loops
                if (IsDraggingSlider && Math.Abs(state.PositionMs - PositionMs) < 1000)
                {
                    System.Diagnostics.Debug.WriteLine("⏭️ Ignoring position update while dragging slider");
                    // Still update other properties except position
                    IsPlaying = state.IsPlaying;

                    // Only update volume from state if user hasn't manually set it; avoid setter to prevent feedback
                    if (!_volumeSetByUser)
                    {
                        _volume = Math.Max(0.0, Math.Min(1.0, state.Volume));
                        RaisePropertyChanged(nameof(Volume));
                        System.Diagnostics.Debug.WriteLine($"🔊 Volume updated from state (dragging): {_volume:F2}");
                    }
                    return;
                }

                // Update ALL properties from enhanced state
                // Reset baseline if track changed or position wrapped to start (auto-advance/repeat)
                try
                {
                    var incomingTrackIdForBaseline = state.TrackId ?? string.Empty;
                    bool trackChangedBaseline = (CurrentTrack?.Id ?? string.Empty) != incomingTrackIdForBaseline && !string.IsNullOrEmpty(incomingTrackIdForBaseline);
                    bool positionWrappedBaseline = state.IsPlaying && state.PositionMs < Math.Max(0, PositionMs - 1000);
                    if (trackChangedBaseline || positionWrappedBaseline)
                    {
                        _lastUiProgressUpdate = DateTime.UtcNow;
                    }
                }
                catch { }

                if (!IsDraggingSlider)
                {
                    var incoming = state.PositionMs;
                    var current = PositionMs;
                    bool sameTrack = true;
                    try
                    {
                        var incomingIdCheck = state.TrackId ?? string.Empty;
                        sameTrack = string.IsNullOrEmpty(incomingIdCheck) || CurrentTrack == null || (CurrentTrack.Id == incomingIdCheck);
                        if (!sameTrack && CurrentTrack != null && !string.IsNullOrEmpty(state.TrackName))
                        {
                            // Fallback heuristic when only names are available
                            sameTrack = string.Equals(CurrentTrack.Title ?? string.Empty, state.TrackName, StringComparison.OrdinalIgnoreCase)
                                        && (state.DurationMs <= 0 || Math.Abs((CurrentTrack.DurationMs) - state.DurationMs) < 1500);
                        }
                    }
                    catch { }

                    if (IsPlaying && sameTrack)
                    {
                        // Ignore small backward jitter while playing the same track
                        if (incoming + 1500 < current)
                        {
                            // Large backward jump (seek/restart) — accept
                            PositionMs = incoming;
                        }
                        else if (incoming < current)
                        {
                            System.Diagnostics.Debug.WriteLine($"⏱️ Ignoring small backward position jitter: incoming={incoming} current={current}");
                            // keep current PositionMs
                        }
                        else
                        {
                            PositionMs = incoming;
                        }
                    }
                    else
                    {
                        PositionMs = incoming;
                    }
                }
                
                // Update playback state (prefer explicit IsPlaying flag if available)
                var wasPlaying = IsPlaying;
                IsPlaying = state.IsPlaying;
                
                // 🛑 TRACK END DETECTION: If API still reports playing but position is at/near end, track has finished
                if (IsPlaying && state.DurationMs > 0 && state.PositionMs >= state.DurationMs - 1000)
                {
                    _loggingService.LogDebug($"[TRACK_END] 🎵 Track finished - Position: {state.PositionMs}ms, Duration: {state.DurationMs}ms");
                    IsPlaying = false;
                    // Update position to full duration to show track has completed
                    PositionMs = state.DurationMs;
                    
                    // 🎵 SHUFFLE AUTO-ADVANCE: If shuffle is enabled, automatically skip to next track
                    if (IsShuffled)
                    {
                        // 🛑 PREVENT DUPLICATE AUTO-ADVANCE: Only allow one auto-advance per track end event
                        var now = DateTimeOffset.UtcNow;
                        if ((now - _lastAutoAdvance) < TimeSpan.FromSeconds(3))
                        {
                            _loggingService.LogDebug($"[SHUFFLE] � Duplicate auto-advance ignored (last: {_lastAutoAdvance:HH:mm:ss})");
                        }
                        else
                        {
                            _lastAutoAdvance = now;
                            
                            // Track the trackId that was auto-advanced from to suppress reappearance
                            _lastAutoAdvancedTrackId = CurrentTrack?.Id ?? string.Empty;
                            _lastAutoAdvancedUntil = now.AddSeconds(5); // Suppress for 5 seconds
                            
                            _loggingService.LogDebug($"[SHUFFLE] �🔀 Auto-advancing to next track (shuffle enabled: {IsShuffled})");
                            _ = Task.Run(async () =>
                            {
                                try
                                {
                                    await Task.Delay(500); // Small delay to ensure track end is processed
                                    _loggingService.LogDebug($"[SHUFFLE] 🔀 Executing SkipToNext after delay");
                                    await ExecuteNextAsync();
                                }
                                catch (Exception ex)
                                {
                                    System.Diagnostics.Debug.WriteLine($"Shuffle auto-advance error: {ex.Message}");
                                }
                            });
                        }
                    }
                    else
                    {
                        _loggingService.LogDebug($"[SHUFFLE] ❌ Not auto-advancing (shuffle disabled: {IsShuffled})");
                    }
                }
                
                // 🟢 SMART POLLING: Resume API polling when playback stops
                if (wasPlaying && !IsPlaying && !_statePollTimer.IsEnabled)
                {
                    _loggingService.LogDebug($"[SMART_POLL] ▶️ Resuming API polling after playback stopped");
                    _statePollTimer.Start();
                }
                
                // Update volume from state - always sync with Web SDK when it's the active device
                bool isWebPlayerActive = !string.IsNullOrEmpty(WebPlaybackDeviceId) && 
                                       SelectedDevice != null && 
                                       SelectedDevice.Id == WebPlaybackDeviceId;
                
                // 🛑 VOLUME STABILITY: Don't update volume during disconnected states or track transitions
                bool isDisconnectedState = string.IsNullOrEmpty(state.TrackId) && string.IsNullOrEmpty(state.TrackName) && CurrentTrack != null;
                bool isTrackTransition = CurrentTrack != null && !string.IsNullOrEmpty(state.TrackId) && state.TrackId != CurrentTrack.Id;
                bool isRapidUpdate = (DateTimeOffset.UtcNow - _lastVolumeUpdate) < TimeSpan.FromMilliseconds(500);
                bool isUnreliableVolume = isDisconnectedState && (state.Volume <= 0.01 || state.Volume >= 0.99); // Suspicious edge values during disconnection
                bool noTrackPlaying = string.IsNullOrEmpty(state.TrackId) && string.IsNullOrEmpty(state.TrackName) && CurrentTrack == null;
                bool isApiDefaultVolume = noTrackPlaying && state.Volume <= 0.01 && !isWebPlayerActive; // API returns 0 when no track playing
                bool isWebSdkZeroVolume = noTrackPlaying && state.Volume <= 0.01 && isWebPlayerActive; // Web SDK sending 0 when no track playing
                
                if (!(noTrackPlaying && _volumeSetByUser) && (isWebPlayerActive || (!_volumeSetByUser && !noTrackPlaying)) && !isDisconnectedState && !isTrackTransition && !isRapidUpdate && !isUnreliableVolume && !isApiDefaultVolume && !isWebSdkZeroVolume && state.Volume >= 0.0)
                {
                    var oldVolume = _volume;
                    var newVolume = Math.Max(0.0, Math.Min(1.0, state.Volume));
                    
                    // Only reset user-set flag if volume actually changed from external source
                    // Don't reset flag when no track is playing or when volume is 0 (API default)
                    bool shouldResetUserFlag = isWebPlayerActive && _volumeSetByUser && Math.Abs(newVolume - oldVolume) > 0.01 
                                              && !noTrackPlaying && newVolume > 0.01;
                    
                    if (shouldResetUserFlag)
                    {
                        _volumeSetByUser = false; // Allow future updates
                        _loggingService.LogDebug($"[VOLUME] 🔄 Volume changed externally: {oldVolume:F2} → {newVolume:F2}, resetting user flag");
                    }
                    else if (_volumeSetByUser && noTrackPlaying && Math.Abs(newVolume - oldVolume) > 0.01)
                    {
                        // Don't reset user flag, but log that we would have
                        _loggingService.LogDebug($"[VOLUME] 🛑 Would reset user flag but no track playing: {oldVolume:F2} → {newVolume:F2}");
                    }
                    
                    UpdateVolumeFromState(newVolume);
                    _lastVolumeUpdate = DateTimeOffset.UtcNow;
                    System.Diagnostics.Debug.WriteLine($"🔊 Volume updated from state: {newVolume:F2} (Web active: {isWebPlayerActive})");
                }
                else if (isDisconnectedState || isTrackTransition || isRapidUpdate || isUnreliableVolume || (noTrackPlaying && _volumeSetByUser) || state.Volume < 0.0 || isApiDefaultVolume || isWebSdkZeroVolume)
                {
                    string reason = isDisconnectedState ? "disconnected state" : 
                                   isTrackTransition ? "track transition" : 
                                   isRapidUpdate ? "rapid update" : 
                                   isUnreliableVolume ? "unreliable volume" : 
                                   (noTrackPlaying && _volumeSetByUser) ? "no track playing with user volume set" : 
                                   isApiDefaultVolume ? "API default volume when no track" : 
                                   isWebSdkZeroVolume ? "Web SDK zero volume when no track" :
                                   state.Volume < 0.0 ? "invalid volume" : "unknown";
                    System.Diagnostics.Debug.WriteLine($"🔊 Volume NOT updated during {reason} - preserving user setting (state vol: {state.Volume:F2})");
                }

                // Update shuffle/repeat from state
                var oldShuffleState = IsShuffled;
                IsShuffled = state.Shuffled;
                if (oldShuffleState != IsShuffled)
                {
                    _loggingService.LogDebug($"[SHUFFLE] 🔀 Shuffle state changed: {oldShuffleState} -> {IsShuffled}");
                    _loggingService.LogDebug($"[SHUFFLE_DEBUG] 🔄 Shuffle state update - Old: {oldShuffleState}, New: {IsShuffled}, Track: {state.TrackName ?? "null"}");
                }
                
                // 🛑 PREVENT OVERWRITING RECENT OPTIMISTIC REPEAT UPDATES
                // Don't update repeat mode if we recently did an optimistic update (within 2 seconds)
                var timeSinceRepeatUpdate = DateTimeOffset.UtcNow - _lastRepeatUpdate;
                if (timeSinceRepeatUpdate > TimeSpan.FromSeconds(2))
                {
                    RepeatMode = state.RepeatMode;
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"🔁 Skipping repeat mode update from API (optimistic update {timeSinceRepeatUpdate.TotalSeconds:F1}s ago)");
                }

                // Update current track information if we have at least an ID or a name
                if (!string.IsNullOrEmpty(state.TrackId) || !string.IsNullOrEmpty(state.TrackName))
                {
                    var incomingId = state.TrackId ?? string.Empty;

                    // 🛑 SUPPRESS TRACK REAPPEARANCE: If this is the same track that was just auto-advanced from or recently disconnected, suppress the update
                    if ((!string.IsNullOrEmpty(_lastAutoAdvancedTrackId) && 
                         DateTimeOffset.UtcNow <= _lastAutoAdvancedUntil && 
                         incomingId == _lastAutoAdvancedTrackId) ||
                        (!string.IsNullOrEmpty(_lastDisconnectedTrackId) && 
                         DateTimeOffset.UtcNow - _lastDisconnectedState < TimeSpan.FromSeconds(5) && 
                         incomingId == _lastDisconnectedTrackId))
                    {
                        _loggingService.LogDebug($"[SUPPRESS_REAPPEARANCE] 🛑 Suppressing reappearance of track: {incomingId} ({state.TrackName ?? "unknown"})");
                        _loggingService.LogDebug($"[SUPPRESS_REAPPEARANCE] Auto-advanced: '{_lastAutoAdvancedTrackId}', Disconnected: '{_lastDisconnectedTrackId}'");
                        System.Diagnostics.Debug.WriteLine($"🛑 Suppressing reappearance of track: {incomingId}");
                        return;
                    }

                    if (CurrentTrack == null || CurrentTrack.Id != incomingId || string.IsNullOrEmpty(CurrentTrack.Title))
                    {
                        System.Diagnostics.Debug.WriteLine($"🎵 Updating track info - Old: {CurrentTrack?.Title ?? "NULL"}, New: {state.TrackName ?? "(pending)"}");

                        var trackChanged = CurrentTrack == null || CurrentTrack.Id != incomingId;
                        if (trackChanged && CurrentTrack != null && !_statePollTimer.IsEnabled)
                        {
                            _loggingService.LogDebug($"[SMART_POLL] 🔄 Resuming API polling after track changed from '{CurrentTrack.Title}'");
                            _statePollTimer.Start();
                        }

                        if (CurrentTrack == null || CurrentTrack.Id != incomingId)
                        {
                            CurrentTrack = new TrackModel
                            {
                                Id = incomingId,
                                Title = state.TrackName ?? string.Empty,
                                DurationMs = state.DurationMs
                            };
                        }
                        else
                        {
                            if (!string.IsNullOrEmpty(state.TrackName))
                                CurrentTrack.Title = state.TrackName;
                            if (state.DurationMs > 0)
                                CurrentTrack.DurationMs = state.DurationMs;
                        }

                        if (!string.IsNullOrEmpty(state.Artists))
                        {
                            CurrentTrack.Artist = state.Artists;
                        }

                        if (!string.IsNullOrEmpty(state.ImageUrl))
                        {
                            try { CurrentTrack.AlbumArtUri = new Uri(state.ImageUrl); }
                            catch (Exception ex)
                            {
                                System.Diagnostics.Debug.WriteLine($"⚠️ Invalid image URL: {state.ImageUrl}, Error: {ex.Message}");
                            }
                        }

                        RaisePropertyChanged(nameof(CurrentTrack));
                        System.Diagnostics.Debug.WriteLine($"✅ Track updated: {CurrentTrack.Title} by {CurrentTrack.Artist}");
                    }
                }
                else if (string.IsNullOrEmpty(state.TrackId) && string.IsNullOrEmpty(state.TrackName) && CurrentTrack != null)
                {
                    // 🎵 TRANSFER DETECTION: Check if this is a transfer FROM Web Player TO remote device
                    // If we have a current track and Web Player is selected, but Web SDK sends null state,
                    // it might be a transfer rather than a disconnection
                    bool isPossibleTransferFromWeb = SelectedDevice != null &&
                                                   SelectedDevice.Id == WebPlaybackDeviceId &&
                                                   CurrentTrack != null &&
                                                   !string.IsNullOrEmpty(CurrentTrack.Id);

                    if (isPossibleTransferFromWeb)
                    {
                        _loggingService.LogDebug($"[WEB_TRANSFER] 🎵 Possible transfer FROM Web Player detected - preserving track '{CurrentTrack?.Title ?? "unknown"}' and waiting for API data");
                        // Don't clear the track yet - let API polling handle the device transfer
                        // This prevents the UI from going blank during transfers
                        return;
                    }

                    // Handle disconnected state - clear current track when WebView2 sends null state
                    System.Diagnostics.Debug.WriteLine($"🔌 Disconnected state detected - clearing current track");
                    _loggingService.LogDebug($"[STATE_UPDATE] 🔌 Disconnected state - clearing track: {CurrentTrack?.Title ?? "unknown"}");
                    
                    // 🛑 TRACK DISCONNECTED STATE: Prevent polling from overriding this for a short time
                    _lastDisconnectedState = DateTimeOffset.UtcNow;
                    
                    // Track the disconnected track ID to suppress reappearance
                    _lastDisconnectedTrackId = CurrentTrack?.Id ?? string.Empty;
                    
                    // 🎵 SHUFFLE AUTO-ADVANCE: Check if we were playing and track ended naturally
                    // We detect track end when we have a current track but incoming state is disconnected (null)
                    bool wasPlayingTrack = CurrentTrack != null && !string.IsNullOrEmpty(CurrentTrack.Id);
                    
                    // Capture the track ID before clearing for auto-advance suppression
                    string trackIdBeforeClearing = CurrentTrack?.Id ?? string.Empty;
                    
                    // Always clear the track first, regardless of auto-advance logic
                    CurrentTrack = null;
                    RaisePropertyChanged(nameof(CurrentTrack));
                    
                    // Reset position and playback state
                    PositionMs = 0;
                    IsPlaying = false;
                    
                    // Resume API polling when disconnected
                    if (!_statePollTimer.IsEnabled)
                    {
                        _loggingService.LogDebug($"[SMART_POLL] ▶️ Resuming API polling after disconnection");
                        _statePollTimer.Start();
                    }
                    
                    // Now handle auto-advance logic (after track is cleared)
                    if (wasPlayingTrack && shuffleWasEnabledAtStart)
                    {
                        // 🛑 PREVENT DUPLICATE AUTO-ADVANCE: Only allow one auto-advance per track end event
                        var now = DateTimeOffset.UtcNow;
                        if ((now - _lastAutoAdvance) < TimeSpan.FromSeconds(3))
                        {
                            _loggingService.LogDebug($"[SHUFFLE_AUTO_ADVANCE] 🛑 Duplicate auto-advance ignored (last: {_lastAutoAdvance:HH:mm:ss})");
                        }
                        else
                        {
                            _lastAutoAdvance = now;
                            
                            // Track the trackId that was auto-advanced from to suppress reappearance
                            _lastAutoAdvancedTrackId = trackIdBeforeClearing;
                            _lastAutoAdvancedUntil = now.AddSeconds(5); // Suppress for 5 seconds
                            
                            _loggingService.LogDebug($"[SHUFFLE_AUTO_ADVANCE] 🎵 Track ended naturally, shuffle was enabled ({shuffleWasEnabledAtStart}) - auto-advancing to next track");
                            _ = Task.Run(async () =>
                            {
                                try
                                {
                                    await Task.Delay(300); // Small delay to ensure state is processed
                                    _loggingService.LogDebug($"[SHUFFLE_AUTO_ADVANCE] 🔀 Executing SkipToNext after track end");
                                    await ExecuteNextAsync();
                                }
                                catch (Exception ex)
                                {
                                    _loggingService.LogError($"[SHUFFLE_AUTO_ADVANCE] ❌ Auto-advance error: {ex.Message}", ex);
                                    System.Diagnostics.Debug.WriteLine($"Shuffle auto-advance error: {ex.Message}");
                                }
                            });
                        }
                    }
                    else if (wasPlayingTrack && !shuffleWasEnabledAtStart)
                    {
                        _loggingService.LogDebug($"[SHUFFLE_AUTO_ADVANCE] ❌ Track ended but shuffle was disabled ({shuffleWasEnabledAtStart}) - not auto-advancing");
                    }
                }

                // Debug summary of current UI state (avoid redundant PropertyChanged to prevent flicker)
                System.Diagnostics.Debug.WriteLine($"✅ UI updated - Playing: {IsPlaying}, Position: {PositionMs}ms, Volume: {Volume:F2}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ UpdateUIFromPlayerState error: {ex.Message}");
            }
        }

        /// <summary>
        /// Handle Web Playback SDK ready event
        /// </summary>
        private void OnReadyDeviceId(string deviceId)
        {
            try
            {
                _loggingService.LogDebug($"[WEB_DEVICE] OnReadyDeviceId called with: {deviceId}");
                System.Diagnostics.Debug.WriteLine($"PlayerViewModel.OnReadyDeviceId called with: {deviceId}");
                WebPlaybackDeviceId = deviceId;
                _loggingService.LogDebug($"[WEB_DEVICE] Set WebPlaybackDeviceId to: {WebPlaybackDeviceId}");
                _webDeviceReadyTcs?.TrySetResult(deviceId);
                _loggingService.LogDebug($"[WEB_DEVICE] Completed TaskCompletionSource with: {deviceId}");
                
                // Add to devices collection if not already present
                if (!Devices.Any(d => d.Id == deviceId))
                {
                    var webDevice = new DeviceModel
                    {
                        Id = deviceId,
                        Name = "Web Player",
                        IsActive = false
                    };
                    Devices.Add(webDevice);
                    _loggingService.LogDebug($"[WEB_DEVICE] Added Web Player device with ID: {deviceId}");
                    System.Diagnostics.Debug.WriteLine($"Added Web Player device with ID: {deviceId}");
                }
            }
            catch (Exception ex)
            {
                _loggingService.LogError($"[WEB_DEVICE] OnReadyDeviceId error: {ex.Message}", ex);
                System.Diagnostics.Debug.WriteLine($"OnReadyDeviceId error: {ex.Message}");
            }
        }

        private async Task<string?> WaitForWebDeviceIdAsync(TimeSpan timeout)
        {
            if (!string.IsNullOrEmpty(WebPlaybackDeviceId)) 
            {
                _loggingService.LogDebug($"[WEB_DEVICE] WaitForWebDeviceIdAsync: Already have device ID: {WebPlaybackDeviceId}");
                return WebPlaybackDeviceId;
            }
            try
            {
                _loggingService.LogDebug($"[WEB_DEVICE] WaitForWebDeviceIdAsync: Waiting for device ID with timeout {timeout.TotalSeconds}s");
                var tcs = _webDeviceReadyTcs;
                if (tcs == null)
                {
                    _loggingService.LogDebug($"[WEB_DEVICE] WaitForWebDeviceIdAsync: Creating new TaskCompletionSource");
                    _webDeviceReadyTcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
                    tcs = _webDeviceReadyTcs;
                }
                else
                {
                    _loggingService.LogDebug($"[WEB_DEVICE] WaitForWebDeviceIdAsync: Using existing TaskCompletionSource");
                }
                var completed = await Task.WhenAny(tcs.Task, Task.Delay(timeout));
                if (completed == tcs.Task)
                {
                    var result = tcs.Task.Result;
                    _loggingService.LogDebug($"[WEB_DEVICE] WaitForWebDeviceIdAsync: Got device ID: {result}");
                    return result;
                }
                else
                {
                    _loggingService.LogDebug($"[WEB_DEVICE] WaitForWebDeviceIdAsync: Timeout waiting for device ID");
                }
            }
            catch (Exception ex)
            {
                _loggingService.LogError($"[WEB_DEVICE] WaitForWebDeviceIdAsync: Error: {ex.Message}", ex);
            }
            return null;
        }

        /// <summary>
        /// Handle seek throttle timer tick
        /// </summary>
        private async void OnSeekThrottleTimerTick(object? sender, EventArgs e)
        {
            _seekThrottleTimer.Stop();
            
            try
            {
                _lastSeekSent = DateTimeOffset.UtcNow;

                // Route seek based on active device at the moment of tick
                await RefreshDevicesAsync();
                var active = Devices.FirstOrDefault(d => d.IsActive);
                var useWebBridge = active != null && !string.IsNullOrEmpty(WebPlaybackDeviceId) && active.Id == WebPlaybackDeviceId;

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
                    await RefreshDevicesAsync();
                    var active = Devices.FirstOrDefault(d => d.IsActive);
                    await _spotify.SeekCurrentPlaybackAsync(PositionMs, active?.Id); 
                } 
                catch { }
            }
        }

        /// <summary>
        /// UI progress timer tick: advance slider locally when web player is active
        /// </summary>
        private void OnUiProgressTick(object? sender, EventArgs e)
        {
            try
            {
                var now = DateTime.UtcNow;
                var delta = now - _lastUiProgressUpdate;
                _lastUiProgressUpdate = now;

                if (!IsPlaying) return;
                if (IsDraggingSlider) return;
                if (string.IsNullOrEmpty(WebPlaybackDeviceId)) return;
                if (CurrentTrack == null || CurrentTrack.DurationMs <= 0) return;

                // Consider web player likely active immediately after a recent resume/click-to-play
                var recentlyResumed = (DateTimeOffset.UtcNow - _resumeRequestedAt) < TimeSpan.FromSeconds(4);
                var webIsSelected = SelectedDevice != null && SelectedDevice.Id == WebPlaybackDeviceId;
                if (!webIsSelected && !recentlyResumed) return;

                var inc = (int)Math.Max(0, Math.Min(2000, delta.TotalMilliseconds));
                var newPos = PositionMs + inc;

                // If we hit or passed the end while still playing (auto-advance/repeat), wrap to 0
                if (newPos >= (CurrentTrack.DurationMs - 20))
                {
                    // Reset to start and fetch fresh state to get the next track/position
                    PositionMs = 0;
                    _lastUiProgressUpdate = now;
                    // Fire-and-forget a quick state refresh so artwork/title/progress update promptly
                    _ = RefreshPlayerStateAsync();
                    return;
                }

                PositionMs = newPos;
            }
            catch { }
        }

        #endregion

        #region Private Orchestration

        // Fire-and-forget background orchestration for click-to-play to avoid blocking UI
        private async Task OrchestrateClickPlayAsync(TrackModel track, CancellationToken ct, string operationId = "")
        {
            _loggingService.LogDebug("ORCHESTRATE", $"=== STARTING ORCHESTRATION for track: {track.Title} [OP:{operationId}] ===");

            try
            {
                if (ct.IsCancellationRequested)
                {
                    _loggingService.LogDebug("ORCHESTRATE", "❌ Orchestration cancelled before start");
                    return;
                }

                _loggingService.LogDebug("ORCHESTRATE", $"🔍 Checking WebPlaybackDeviceId: '{WebPlaybackDeviceId}'");
                _loggingService.LogDebug("ORCHESTRATE", $"🔍 Current _webPlaybackDeviceId field: '{_webPlaybackDeviceId}'");

                // Try to get web device id quickly if missing
                if (string.IsNullOrEmpty(WebPlaybackDeviceId))
                {
                    _loggingService.LogDebug("ORCHESTRATE", "⏳ WebPlaybackDeviceId is empty, waiting for it...");
                    var waitedId = await WaitForWebDeviceIdAsync(TimeSpan.FromSeconds(3));
                    if (!string.IsNullOrEmpty(waitedId))
                    {
                        WebPlaybackDeviceId = waitedId;
                        _loggingService.LogDebug("ORCHESTRATE", $"✅ WebPlayback device ready: {WebPlaybackDeviceId}");
                        System.Diagnostics.Debug.WriteLine($"✅ WebPlayback device ready for orchestration: {WebPlaybackDeviceId}");
                    }
                    else
                    {
                        // Fallback: try to get device ID directly from WebPlaybackBridge
                        _loggingService.LogDebug("ORCHESTRATE", "⏳ Wait failed, trying direct WebPlaybackBridge query...");
                        try
                        {
                            var bridgeDeviceId = await _webPlaybackBridge.GetWebPlaybackDeviceIdAsync();
                            if (!string.IsNullOrEmpty(bridgeDeviceId))
                            {
                                WebPlaybackDeviceId = bridgeDeviceId;
                                _loggingService.LogDebug("ORCHESTRATE", $"✅ Got WebPlaybackDeviceId from bridge: {WebPlaybackDeviceId}");
                            }
                            else
                            {
                                _loggingService.LogDebug("ORCHESTRATE", "❌ WebPlaybackBridge also has empty device ID");
                            }
                        }
                        catch (Exception ex)
                        {
                            _loggingService.LogError($"❌ Error getting device ID from bridge: {ex.Message}", ex);
                        }
                        _loggingService.LogDebug("ORCHESTRATE", "❌ Failed to get WebPlayback device ID");
                    }
                }

                if (ct.IsCancellationRequested)
                {
                    _loggingService.LogDebug("ORCHESTRATE", "❌ Orchestration cancelled after device check");
                    return;
                }

                // Proactively enable audio context on the web player (best-effort)
                _loggingService.LogDebug("ORCHESTRATE", "🔊 Enabling web player audio context");
                try { await _webPlaybackBridge.ResumeAsync(); } catch { }

                if (ct.IsCancellationRequested)
                {
                    _loggingService.LogDebug("ORCHESTRATE", "❌ Orchestration cancelled after audio context");
                    return;
                }

                // Prefer transfer-first to make the Web Playback device active, then start the selected track.
                if (!string.IsNullOrEmpty(WebPlaybackDeviceId) && !string.IsNullOrEmpty(track.Id))
                {
                    _loggingService.LogDebug("ORCHESTRATE", $"🎯 Attempting transfer-first flow with device: {WebPlaybackDeviceId}");

                    try
                    {
                        _loggingService.LogDebug("ORCHESTRATE", "📡 Transferring to web device (play=false)");
                        System.Diagnostics.Debug.WriteLine("🔁 Transferring to web device with play=false before play (activate without auto-playing last track)...");
                        await _spotify.TransferPlaybackAsync(new[] { WebPlaybackDeviceId }, false);
                        _loggingService.LogDebug("ORCHESTRATE", "✅ Transfer (play=false) successful");
                    }
                    catch (Exception txEx)
                    {
                        _loggingService.LogError($"⚠️ Transfer (play=false) failed: {txEx.Message}", txEx);
                        System.Diagnostics.Debug.WriteLine($"⚠️ Transfer (play=false) threw: {txEx.Message}");
                    }

                    _loggingService.LogDebug("ORCHESTRATE", "⏳ Waiting 350ms for transfer to complete");
                    try { await Task.Delay(350, ct); } catch { }
                    
                    if (ct.IsCancellationRequested)
                    {
                        _loggingService.LogDebug("ORCHESTRATE", "❌ Orchestration cancelled after transfer delay");
                        return;
                    }
                    
                    // Poll quickly to confirm activation
                    try
                    {
                        var activated = await WaitForActiveDeviceAsync(WebPlaybackDeviceId, attempts: 12, delayMs: 250);
                        _loggingService.LogDebug("ORCHESTRATE", $"🟢 Web device active after transfer? {activated}");
                        System.Diagnostics.Debug.WriteLine($"🟢 Web device active after transfer? {activated}");
                    }
                    catch (Exception ex)
                    {
                        _loggingService.LogError($"❌ Error checking device activation: {ex.Message}", ex);
                    }

                    if (ct.IsCancellationRequested)
                    {
                        _loggingService.LogDebug("ORCHESTRATE", "❌ Orchestration cancelled after device activation check");
                        return;
                    }

                    _loggingService.LogDebug($"🎵 Playing track {track.Id} on device {WebPlaybackDeviceId}");
                    var ok = await _spotify.PlayTrackOnDeviceAsync(WebPlaybackDeviceId, track.Id);
                    if (ok)
                    {
                        IsPlaying = true;
                        _loggingService.LogDebug("ORCHESTRATE", "✅ Click-to-play started successfully after transfer-first flow");
                        System.Diagnostics.Debug.WriteLine("▶️ Click-to-play started after transfer-first flow");
                        return;
                    }
                    else
                    {
                        _loggingService.LogDebug("ORCHESTRATE", "❌ PlayTrackOnDeviceAsync returned false");
                    }

                    // Retry once with transfer(play=true) to force activation if needed, then play again
                    _loggingService.LogDebug("ORCHESTRATE", "🔄 Retrying with transfer(play=true)");
                    try
                    {
                        System.Diagnostics.Debug.WriteLine("🔁 Retry: transferring with play=true to force activation...");
                        await _spotify.TransferPlaybackAsync(new[] { WebPlaybackDeviceId }, true);
                        _loggingService.LogDebug("ORCHESTRATE", "⏳ Waiting 400ms after retry transfer");
                        try { await Task.Delay(400, ct); } catch { }
                    }
                    catch (Exception txEx2)
                    {
                        _loggingService.LogError($"⚠️ Transfer retry (play=true) failed: {txEx2.Message}", txEx2);
                        System.Diagnostics.Debug.WriteLine($"⚠️ Transfer retry (play=true) threw: {txEx2.Message}");
                    }

                    if (ct.IsCancellationRequested)
                    {
                        _loggingService.LogDebug("ORCHESTRATE", "❌ Orchestration cancelled after retry transfer delay");
                        return;
                    }

                    _loggingService.LogDebug($"🎵 Retrying play on device {WebPlaybackDeviceId}");
                    ok = await _spotify.PlayTrackOnDeviceAsync(WebPlaybackDeviceId, track.Id);
                    if (ok)
                    {
                        IsPlaying = true;
                        _loggingService.LogDebug("ORCHESTRATE", "✅ Click-to-play started successfully after retry");
                        System.Diagnostics.Debug.WriteLine("▶️ Click-to-play started after retry transfer");
                        return;
                    }
                    else
                    {
                        _loggingService.LogDebug("ORCHESTRATE", "❌ Retry PlayTrackOnDeviceAsync also returned false");
                    }
                }
                else
                {
                    _loggingService.LogDebug("ORCHESTRATE", $"❌ Cannot use transfer-first flow: WebPlaybackDeviceId='{WebPlaybackDeviceId}', TrackId='{track.Id}'");
                }

                if (ct.IsCancellationRequested)
                {
                    _loggingService.LogDebug("ORCHESTRATE", "❌ Orchestration cancelled before fallback");
                    return;
                }

                // Fallback: try the currently active device if any
                _loggingService.LogDebug("ORCHESTRATE", "🔄 Starting fallback to active device");
                for (int i = 0; i < 3 && !ct.IsCancellationRequested; i++)
                {
                    _loggingService.LogDebug("ORCHESTRATE", $"🔄 Fallback attempt {i + 1}/3");
                    try { await Task.Delay(200, ct); } catch { break; }
                    try { await RefreshDevicesAsync(); } catch { }

                    var active = Devices.FirstOrDefault(d => d.IsActive);
                    if (active != null && !string.IsNullOrEmpty(active.Id) && !string.IsNullOrEmpty(track.Id))
                    {
                        if (ct.IsCancellationRequested)
                        {
                            _loggingService.LogDebug("ORCHESTRATE", "❌ Orchestration cancelled before fallback play");
                            return;
                        }
                        
                        _loggingService.LogDebug("ORCHESTRATE", $"🎯 Found active device: {active.Name} ({active.Id})");
                        var ok = await _spotify.PlayTrackOnDeviceAsync(active.Id, track.Id);
                        if (ok)
                        {
                            IsPlaying = true;
                            _loggingService.LogDebug("ORCHESTRATE", "✅ Click-to-play started on active device (fallback)");
                            System.Diagnostics.Debug.WriteLine("▶️ Click-to-play started on active device (fallback)");
                            return;
                        }
                        else
                        {
                            _loggingService.LogDebug("ORCHESTRATE", "❌ PlayTrackOnDeviceAsync failed on active device");
                        }
                    }
                    else
                    {
                        _loggingService.LogDebug("ORCHESTRATE", "❌ No active device found for fallback");
                    }
                }

                if (ct.IsCancellationRequested)
                {
                    _loggingService.LogDebug("ORCHESTRATE", "❌ Orchestration cancelled during fallback");
                    return;
                }

                // Last resort: use JS bridge to start playback locally by URI
                if (!string.IsNullOrEmpty(track.Uri))
                {
                    if (ct.IsCancellationRequested)
                    {
                        _loggingService.LogDebug("ORCHESTRATE", "❌ Orchestration cancelled before JS bridge fallback");
                        return;
                    }
                    
                    _loggingService.LogDebug("ORCHESTRATE", $"🎯 Last resort: using JS bridge with URI: {track.Uri}");
                    try
                    {
                        await _webPlaybackBridge.PlayAsync(new[] { track.Uri });
                        IsPlaying = true;
                        _loggingService.LogDebug("ORCHESTRATE", "✅ Click-to-play started via JS bridge");
                        System.Diagnostics.Debug.WriteLine("▶️ Click-to-play started via JS bridge");
                    }
                    catch (Exception jsEx)
                    {
                        _loggingService.LogError($"❌ JS bridge play failed: {jsEx.Message}", jsEx);
                        System.Diagnostics.Debug.WriteLine($"JS bridge play() error: {jsEx.Message}");
                    }
                }
                else
                {
                    _loggingService.LogDebug("ORCHESTRATE", "❌ No track URI available for JS bridge fallback");
                }

                _loggingService.LogDebug("ORCHESTRATE", "=== ORCHESTRATION COMPLETED ===");
            }
            catch (Exception ex)
            {
                _loggingService.LogError($"💥 ERROR in OrchestrateClickPlayAsync: {ex.Message}", ex);
                System.Diagnostics.Debug.WriteLine($"OrchestrateClickPlayAsync error: {ex.Message}");
            }
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Initialize the player with access token
        /// </summary>
        public async Task InitializeAsync(string accessToken, string playerHtmlPath)
        {
            try
            {
                await _webPlaybackBridge.InitializeAsync(accessToken, playerHtmlPath);
                await _webPlaybackBridge.ConnectAsync();
                await RefreshDevicesAsync();
                
                // Load user's top tracks after successful initialization
                await LoadUserTopTracksAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"PlayerViewModel.InitializeAsync error: {ex.Message}");
            }
        }

        /// <summary>
        /// Public method to reload user's top tracks
        /// </summary>
        public async Task LoadTopTracksAsync()
        {
            await LoadUserTopTracksAsync();
        }
        
        /// <summary>
        /// Test method to load sample tracks for debugging
        /// </summary>
    public void LoadTestTracks()
        {
            
            TopTracks.Clear();
            
            TopTracks.Add(new TrackModel
            {
                Id = "test-1",
                Title = "Test Track 1",
                Artist = "Debug Artist",
                DurationMs = 180000,
                Uri = "spotify:track:test1"
            });
            
            TopTracks.Add(new TrackModel
            {
                Id = "test-2",
                Title = "Test Track 2", 
                Artist = "Debug Artist",
                DurationMs = 200000,
                Uri = "spotify:track:test2"
            });
            
            TopTracks.Add(new TrackModel
            {
                Id = "test-3",
                Title = "Test Track 3",
                Artist = "Debug Artist", 
                DurationMs = 220000,
                Uri = "spotify:track:test3"
            });
            
            System.Diagnostics.Debug.WriteLine($"✅ Added {TopTracks.Count} test tracks to collection");
        }

        /// <summary>
        /// Refresh available devices
        /// </summary>
        public async Task RefreshDevicesAsync()
        {
            try
            {
                // Capture previous active device id before refresh
                var prevActiveId = SelectedDevice?.Id;

                var devices = await _spotify.GetDevicesAsync();
                
                Devices.Clear();
                foreach (var device in devices)
                {
                    Devices.Add(new DeviceModel
                    {
                        Id = device.Id,
                        Name = device.Name,
                        Type = device.Type ?? string.Empty,
                        IsActive = device.IsActive
                    });
                }

                // Ensure Web Playback device appears in the list for bindings, even if API doesn't report it yet
                if (!string.IsNullOrEmpty(WebPlaybackDeviceId) && !Devices.Any(d => d.Id == WebPlaybackDeviceId))
                {
                    Devices.Add(new DeviceModel
                    {
                        Id = WebPlaybackDeviceId,
                        Name = "Web Player",
                        Type = "computer", // Web Player is a computer-type device
                        IsActive = false
                    });
                }

                // Set selected device to active one
                SelectedDevice = Devices.FirstOrDefault(d => d.IsActive);
                var newActiveId = SelectedDevice?.Id;

                // Update last-known active id
                _lastActiveDeviceId = newActiveId;

                // If the Web Playback device just became active, ensure playback starts
                if (!string.IsNullOrEmpty(WebPlaybackDeviceId)
                    && string.Equals(newActiveId, WebPlaybackDeviceId, StringComparison.Ordinal)
                    && !string.Equals(prevActiveId, newActiveId, StringComparison.Ordinal))
                {
                    System.Diagnostics.Debug.WriteLine($"🎯 Web device became active: {newActiveId}. Starting playback (pending track if any, else resume)...");

                    // Prefer the track the user clicked recently
                    var havePendingTrack = !string.IsNullOrEmpty(_pendingPlayTrackId) && DateTimeOffset.UtcNow <= _pendingPlayUntil;

                    try
                    {
                        // Proactively enable audio context in the web player
                        try { await _webPlaybackBridge.ResumeAsync(); } catch { }

                        bool ok = false;
                        if (havePendingTrack && !string.IsNullOrEmpty(_pendingPlayTrackId))
                        {
                            ok = await _spotify.PlayTrackOnDeviceAsync(WebPlaybackDeviceId, _pendingPlayTrackId);
                            System.Diagnostics.Debug.WriteLine($"▶️ Pending track play issued on web device. ok={ok}");
                        }
                        else
                        {
                            ok = await _spotify.ResumeCurrentPlaybackAsync(WebPlaybackDeviceId);
                            System.Diagnostics.Debug.WriteLine($"▶️ Resume issued on web device. ok={ok}");
                        }

                        _resumeRequestedAt = DateTimeOffset.UtcNow; // suppress transient stale states
                        if (ok) IsPlaying = true;
                    }
                    catch (Exception startEx)
                    {
                        System.Diagnostics.Debug.WriteLine($"⚠️ Auto-start on web activation failed: {startEx.Message}");
                    }
                    finally
                    {
                        // Clear pending track marker regardless of outcome
                        _pendingPlayTrackId = null;
                        _pendingPlayUntil = DateTimeOffset.MinValue;
                    }

                    // Quick follow-up refresh to sync UI
                    _ = Task.Delay(250).ContinueWith(async _ =>
                    {
                        try { await RefreshPlayerStateAsync(); } catch { }
                    });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"RefreshDevices error: {ex.Message}");
            }
        }

        /// <summary>
        /// Refresh current player state
        /// Chooses the correct source based on the active device:
        /// - If the in-app Web Playback device is active, prefer the WebPlayback bridge state.
        /// - Otherwise, use Spotify Web API playback state so remote progress updates.
        /// </summary>
        public async Task RefreshPlayerStateAsync()
        {
            try
            {
                // Determine active device
                string? activeDeviceId = SelectedDevice?.Id;
                if (string.IsNullOrEmpty(activeDeviceId))
                {
                    try { await RefreshDevicesAsync(); activeDeviceId = SelectedDevice?.Id; } catch { }
                }

                bool isWebActive = !string.IsNullOrEmpty(WebPlaybackDeviceId) && WebPlaybackDeviceId == activeDeviceId;

                // Prefer the Web Playback bridge state not only when it's the active device,
                // but also for a short window after a user-initiated play/resume or when a
                // pending clicked track exists. This avoids relying on the Spotify API while
                // it still reports the previous device or stale paused state.
                var now = DateTimeOffset.UtcNow;
                bool recentlyResumed = (now - _resumeRequestedAt) < TimeSpan.FromMilliseconds(2500);
                bool havePendingWebStart = !string.IsNullOrEmpty(WebPlaybackDeviceId)
                                            && !string.IsNullOrEmpty(_pendingPlayTrackId)
                                            && now <= _pendingPlayUntil;
                bool preferWeb = !string.IsNullOrEmpty(WebPlaybackDeviceId) && (isWebActive || recentlyResumed || havePendingWebStart);

                if (preferWeb)
                {
                    var webState = await _webPlaybackBridge.GetStateAsync();
                    if (webState != null)
                    {
                        // 🛑 PREVENT OVERRIDING RECENT DISCONNECTED STATE
                        // If we recently received a disconnected state, don't let Web polling restore old track
                        var timeSinceDisconnected = DateTimeOffset.UtcNow - _lastDisconnectedState;
                        if (timeSinceDisconnected < TimeSpan.FromSeconds(5) && CurrentTrack == null && 
                            !string.IsNullOrEmpty(webState.TrackId))
                        {
                            _loggingService.LogDebug($"[WEB_POLL] 🛑 Suppressing Web state update after recent disconnection ({timeSinceDisconnected.TotalSeconds:F1}s ago)");
                            return;
                        }

                        // 🎵 TRANSFER DETECTION: If Web SDK shows null/empty state but we have a current track,
                        // it might be a transfer FROM Web Player to remote device
                        bool isPossibleTransferFromWeb = (webState.TrackId == null || webState.TrackName == null) && 
                                                       CurrentTrack != null && 
                                                       !string.IsNullOrEmpty(CurrentTrack.Id) &&
                                                       SelectedDevice != null &&
                                                       SelectedDevice.Id == WebPlaybackDeviceId;

                        if (isPossibleTransferFromWeb)
                        {
                            _loggingService.LogDebug($"[WEB_TRANSFER] 🎵 Detected possible transfer FROM Web Player - Web SDK sent null/empty state, falling back to API");
                            // Don't use Web SDK state, fall through to API
                        }
                        else
                        {
                            OnPlayerStateChanged(webState);
                            return;
                        }
                    }
                    // If bridge returned null, fall through to API as a safety
                }

                // Remote device (or web not active): query Spotify Web API
                var playback = await _spotify.GetCurrentPlaybackAsync();
                if (playback != null)
                {
                    try
                    {
                        // 🛑 CRITICAL: During active playback on Web Player, don't let API polling disrupt real-time updates
                        // Only suppress API updates when Web Player is active - for remote devices, API is the source of truth
                        bool isWebPlayerActive = !string.IsNullOrEmpty(WebPlaybackDeviceId) &&
                                               SelectedDevice != null &&
                                               SelectedDevice.Id == WebPlaybackDeviceId;

                        // 🎵 DEVICE TRANSFER DETECTION: If we're transferring FROM Web Player TO remote device, immediately use API data
                        bool isTransferringFromWeb = !string.IsNullOrEmpty(WebPlaybackDeviceId) &&
                                                   playback.Device != null &&
                                                   playback.Device.Id != WebPlaybackDeviceId &&
                                                   SelectedDevice != null &&
                                                   SelectedDevice.Id == WebPlaybackDeviceId;

                        if (isTransferringFromWeb)
                        {
                            _loggingService.LogDebug($"[DEVICE_TRANSFER] 🎵 Detected transfer FROM Web Player TO remote device '{playback.Device?.Name ?? "unknown"}' - using API data immediately");
                            // Don't suppress API updates during transfer - we need the correct device and track info
                        }
                        else if (IsPlaying && CurrentTrack != null && !string.IsNullOrEmpty(CurrentTrack.Id) && isWebPlayerActive)
                        {
                            var apiTrackForCheck = playback.Item as SpotifyAPI.Web.FullTrack;
                            var apiTrackId = apiTrackForCheck?.Id;

                            // If API shows different track or no track, don't update UI - let WebView2 control it
                            if (string.IsNullOrEmpty(apiTrackId) || apiTrackId != CurrentTrack.Id)
                            {
                                _loggingService.LogDebug($"[API_POLL] 🛑 Suppressing API update during active Web Player playback - API track: '{apiTrackForCheck?.Name ?? "null"}' vs Current: '{CurrentTrack.Title}'");
                                return;
                            }

                            // If API shows paused but we're actively playing, don't update UI
                            if (!playback.IsPlaying && IsPlaying)
                            {
                                _loggingService.LogDebug($"[API_POLL] 🛑 Suppressing API paused state during active Web Player playback");
                                return;
                            }
                        }

                        // 🎵 DEVICE UPDATE: Update selected device based on API data
                        if (playback.Device != null)
                        {
                            var apiDevice = Devices.FirstOrDefault(d => d.Id == playback.Device.Id);
                            if (apiDevice == null)
                            {
                                // Add the device if it's not in our list
                                apiDevice = new DeviceModel
                                {
                                    Id = playback.Device.Id,
                                    Name = playback.Device.Name ?? "Unknown Device",
                                    IsActive = playback.Device.IsActive,
                                    Type = playback.Device.Type ?? "Unknown"
                                };
                                Devices.Add(apiDevice);
                                _loggingService.LogDebug($"[DEVICE_UPDATE] ➕ Added new device from API: '{apiDevice.Name}' ({apiDevice.Id})");
                            }

                            // Update device active status
                            apiDevice.IsActive = playback.Device.IsActive;

                            // Update selected device if API shows different active device
                            if (playback.Device.IsActive && (SelectedDevice == null || SelectedDevice.Id != playback.Device.Id))
                            {
                                var previousDevice = SelectedDevice;
                                SelectedDevice = apiDevice;
                                _loggingService.LogDebug($"[DEVICE_UPDATE] � Updated selected device from API: '{previousDevice?.Name ?? "null"}' → '{SelectedDevice.Name}'");
                            }
                        }

                        // Suppress transient contradictory states right after user-initiated play/pause
                        var nowTs = DateTimeOffset.UtcNow;
                        if ((nowTs - _resumeRequestedAt) < TimeSpan.FromMilliseconds(2500) && !playback.IsPlaying)
                        {
                            System.Diagnostics.Debug.WriteLine("⏳ Suppressing stale paused state shortly after resume request (API not updated yet)");
                            return; // Keep optimistic IsPlaying=true without flipping UI back
                        }
                        if ((nowTs - _pauseRequestedAt) < TimeSpan.FromMilliseconds(1500) && playback.IsPlaying)
                        {
                            System.Diagnostics.Debug.WriteLine("⏳ Suppressing stale playing state shortly after pause request (API not updated yet)");
                            return; // Keep optimistic IsPlaying=false
                        }
                        var apiTrack = playback.Item as SpotifyAPI.Web.FullTrack;
                        var artists = apiTrack?.Artists != null
                            ? string.Join(", ", apiTrack.Artists.Select(a => a?.Name).Where(n => !string.IsNullOrWhiteSpace(n)))
                            : null;
                        var imageUrl = apiTrack?.Album?.Images != null ? apiTrack.Album.Images.FirstOrDefault()?.Url : null;
                        var ps = new PlayerState
                        {
                            TrackId = apiTrack?.Id,
                            TrackName = apiTrack?.Name,
                            Artists = artists,
                            Album = apiTrack?.Album?.Name,
                            ImageUrl = imageUrl,
                            PositionMs = playback.ProgressMs,
                            DurationMs = apiTrack?.DurationMs ?? 0,
                            Paused = !playback.IsPlaying,
                            Volume = (playback.Device?.VolumePercent ?? (_volumeSetByUser ? (int)(_volume * 100) : 100)) / 100.0, // Preserve user volume when no track
                            Shuffled = playback.ShuffleState,
                            RepeatMode = (playback.RepeatState ?? "off") switch
                            {
                                "track" => 2,
                                "context" => 1,
                                _ => 0
                            },
                            IsPlaying = playback.IsPlaying,
                            HasNextTrack = false,
                            HasPreviousTrack = false,
                            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                        };

                        // 🛑 PREVENT OVERWRITING RECENT OPTIMISTIC REPEAT UPDATES in API polling
                        var timeSinceRepeatUpdate = DateTimeOffset.UtcNow - _lastRepeatUpdate;
                        if (timeSinceRepeatUpdate <= TimeSpan.FromSeconds(2))
                        {
                            // Override the API's repeat mode with our optimistic value
                            ps.RepeatMode = _repeatMode;
                            System.Diagnostics.Debug.WriteLine($"🔁 API polling: preserving optimistic repeat mode {_repeatMode} ({timeSinceRepeatUpdate.TotalSeconds:F1}s ago)");
                        }

                        _loggingService.LogDebug($"[INIT] 📊 Initial state loaded - Shuffle: {ps.Shuffled}, Playing: {ps.IsPlaying}, Track: {ps.TrackName ?? "none"}");
                        _loggingService.LogDebug($"[INIT_DEBUG] 📊 Detailed initial state - Shuffle: {ps.Shuffled}, Repeat: {ps.RepeatMode}, Position: {ps.PositionMs}ms, Duration: {ps.DurationMs}ms");
                        OnPlayerStateChanged(ps);
                        return;
                    }
                    catch (Exception mapEx)
                    {
                        System.Diagnostics.Debug.WriteLine($"RefreshPlayerState API mapping error: {mapEx.Message}");
                    }
                }

                // Safety fallback: if API gave no data and web device might have info, try bridge once BEFORE deciding idle
                try
                {
                    var stateFallback = await _webPlaybackBridge.GetStateAsync();
                    if (stateFallback != null)
                    {
                        OnPlayerStateChanged(stateFallback);
                        return;
                    }
                }
                catch { }

                // If still no playback information and no active device, avoid forcing idle to prevent flicker
                if (SelectedDevice == null)
                {
                    System.Diagnostics.Debug.WriteLine("ℹ️ No active device/playback; preserving current UI state (no forced idle).");
                    // A subsequent bridge push or API poll will sync the correct state.
                    return;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"RefreshPlayerState error: {ex.Message}");
            }
        }
        #endregion

        #region Private Methods

        /// <summary>
        /// Raise CanExecuteChanged for all commands
        /// </summary>
        private void RaiseCommandsCanExecuteChanged()
        {
            (PlayPauseCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
            (NextCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
            (PrevCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        }

        /// <summary>
        /// Load user's top tracks from Spotify API
        /// </summary>
    private async Task LoadUserTopTracksAsync()
        {
            try
            {
                var topTracksDto = await _spotify.GetUserTopTracksAsync(20, 0, "medium_term");
                
                // Clear existing tracks and add the real user data
                await App.Current.Dispatcher.InvokeAsync(() =>
                {
                    TopTracks.Clear();
                    
                    if (topTracksDto.Items != null)
                    {
                        foreach (var dto in topTracksDto.Items)
                        {
                            var track = new TrackModel
                            {
                                Id = dto.Id,
                                Title = dto.Name ?? "Unknown Track",
                                Artist = dto.Artists ?? "Unknown Artist",
                                DurationMs = dto.DurationMs,
                                Uri = dto.Uri
                            };
                            
                            // Set album artwork if available
                            if (!string.IsNullOrEmpty(dto.AlbumImageUrl))
                            {
                                try
                                {
                                    track.AlbumArtUri = new Uri(dto.AlbumImageUrl);
                                }
                                catch (UriFormatException)
                                {
                                    // Invalid URI, leave as null
                                    track.AlbumArtUri = null;
                                }
                            }
                            
                            TopTracks.Add(track);
                        }
                    }
                });
            }
            catch (Exception)
            {
                
                // Don't clear tracks on error - instead load some fallback tracks so UI isn't empty
                await App.Current.Dispatcher.InvokeAsync(() =>
                {
                    if (TopTracks.Count == 0)
                    {
                        // Add a few fallback tracks so the UI shows something
                        TopTracks.Add(new TrackModel
                        {
                            Id = "fallback-1",
                            Title = "API Error - Fallback Track 1",
                            Artist = "Test Artist",
                            DurationMs = 180000,
                            Uri = "spotify:track:fallback1"
                        });
                        
                        TopTracks.Add(new TrackModel
                        {
                            Id = "fallback-2", 
                            Title = "API Error - Fallback Track 2",
                            Artist = "Test Artist",
                            DurationMs = 200000,
                            Uri = "spotify:track:fallback2"
                        });
                        
                        TopTracks.Add(new TrackModel
                        {
                            Id = "fallback-3", 
                            Title = "API Error - Fallback Track 3",
                            Artist = "Test Artist",
                            DurationMs = 220000,
                            Uri = "spotify:track:fallback3"
                        });
                    }
                });
            }
        }

        #endregion

        #region IDisposable Implementation

        private bool _disposed = false;

        /// <summary>
        /// Dispose resources and cleanup timers
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Dispose implementation
        /// </summary>
        protected virtual void Dispose(bool disposing)
        {
            if (_disposed)
                return;

            if (disposing)
            {
                // Dispose managed resources
                _seekThrottleTimer?.Stop();
                _statePollTimer?.Stop();
                _uiProgressTimer?.Stop();

                _clickCts?.Cancel();
                _clickCts?.Dispose();

                _trackOperationSemaphore?.Dispose();

                // Unsubscribe from events
                if (_webPlaybackBridge != null)
                {
                    _webPlaybackBridge.OnPlayerStateChanged -= OnPlayerStateChanged;
                    _webPlaybackBridge.OnReadyDeviceId -= OnReadyDeviceId;
                }
            }

            _disposed = true;
        }

        #endregion
    }
}
