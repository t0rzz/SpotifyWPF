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
        private static int _instanceCount = 0;
        private readonly ISpotify _spotify;
        private readonly IWebPlaybackBridge _webPlaybackBridge;
        private readonly ILoggingService _loggingService;
        private readonly Component.TimerManager _timerManager;
        private readonly Component.DeviceManager _deviceManager;
        private readonly Component.PlaybackManager _playbackManager;
        private readonly Service.MediaKeyManager _mediaKeyManager;
        private readonly Service.TrayIconManager _trayIconManager;
    private DateTime _lastUiProgressUpdate = DateTime.UtcNow;
    private DateTimeOffset _resumeRequestedAt = DateTimeOffset.MinValue;
    private DateTimeOffset _pauseRequestedAt = DateTimeOffset.MinValue;
    // Suppress transient UI reverts right after click-to-play by tracking the intended track
    private string? _pendingTrackId;
    private DateTimeOffset _pendingTrackUntil = DateTimeOffset.MinValue;

    // When a user clicks a track, remember it so we can (re)start playback as soon as the web device becomes active
    private string? _pendingPlayTrackId;
    private DateTimeOffset _pendingPlayUntil = DateTimeOffset.MinValue;

    // Cancellation for click-to-play orchestration so a new click cancels the previous one
    private CancellationTokenSource? _clickCts;

    // Semaphore to prevent concurrent track operations (race condition fix)
    private readonly SemaphoreSlim _trackOperationSemaphore = new SemaphoreSlim(1, 1);

    // TaskCompletionSource for waiting on Web Playback SDK device ready
    private TaskCompletionSource<string>? _webDeviceReadyTcs;

        // Private fields for properties
        private double _volume = 1.0; // Start at 100% volume
        private bool _volumeSetByUser = false; // Track if user has manually set volume
        private bool _isUpdatingVolumeFromState = false; // Prevent recursive volume updates

        // Top tracks collection
        private ObservableCollection<TrackModel> _topTracks = new();

        public PlayerViewModel(ISpotify spotify, IWebPlaybackBridge webPlaybackBridge, ILoggingService loggingService)
        {
            _instanceCount++;

            _spotify = spotify ?? throw new ArgumentNullException(nameof(spotify));
            _webPlaybackBridge = webPlaybackBridge ?? throw new ArgumentNullException(nameof(webPlaybackBridge));
            _loggingService = loggingService ?? throw new ArgumentNullException(nameof(loggingService));

            // Initialize TimerManager
            _timerManager = new Component.TimerManager(_loggingService);
            _timerManager.RegisterSeekThrottleHandler(OnSeekThrottleTimerTick);
            _timerManager.RegisterStatePollHandler(OnStatePollTimerTick);
            _timerManager.RegisterUiProgressHandler(OnUiProgressTick);

            // Initialize DeviceManager
            _deviceManager = new Component.DeviceManager(_spotify, _webPlaybackBridge, _loggingService);

            // Initialize PlaybackManager
            _playbackManager = new Component.PlaybackManager(_spotify, _webPlaybackBridge, _loggingService, _deviceManager, _timerManager);

            // Initialize MediaKeyManager for global hotkey support
            _mediaKeyManager = new Service.MediaKeyManager(this);

            // Initialize TrayIconManager for system tray support
            _trayIconManager = Service.TrayIconManager.GetInstance(this);

            // Initialize collections
            TopTracks = new ObservableCollection<TrackModel>();

            // Initialize commands
            PlayPauseCommand = new AsyncRelayCommand(ExecutePlayPauseAsync, CanExecutePlayPause);
            NextCommand = new AsyncRelayCommand(ExecuteNextAsync, CanExecuteNext);
            PrevCommand = new AsyncRelayCommand(ExecutePrevAsync, CanExecutePrev);
            SeekCommand = new AsyncRelayCommand<int>(ExecuteSeekAsync);
            SetVolumeCommand = new AsyncRelayCommand<double>(ExecuteSetVolumeAsync);
            SelectDeviceCommand = new AsyncRelayCommand<DeviceModel>(_deviceManager.SelectDeviceAsync);
            ClickTrackCommand = new AsyncRelayCommand<object>(ExecuteClickTrackAsync);
            ToggleShuffleCommand = new AsyncRelayCommand(ExecuteToggleShuffleAsync);
            CycleRepeatCommand = new AsyncRelayCommand(ExecuteCycleRepeatAsync);
            RefreshCommand = new AsyncRelayCommand(ExecuteRefreshAsync);

            // Subscribe to Web Playback events
            _webPlaybackBridge.OnPlayerStateChanged += OnPlayerStateChanged;
            _webPlaybackBridge.OnReadyDeviceId += _deviceManager.OnReadyDeviceId;
            _webDeviceReadyTcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

            // Start state polling timer
            _timerManager.StartStatePollTimer();

            // Start UI progress timer
            _timerManager.StartUiProgressTimer();

            // Don't load top tracks in constructor - will be loaded during initialization
        }

        #region Public Properties Access

        /// <summary>
        /// Access to the WebPlayback bridge for initialization
        /// </summary>
        public IWebPlaybackBridge WebPlaybackBridge => _webPlaybackBridge;

        /// <summary>
        /// Access to the TrayIconManager for window integration
        /// </summary>
        public Service.TrayIconManager TrayIconManager => _trayIconManager;

        #endregion

        #region Properties

        /// <summary>
        /// Currently playing track
        /// </summary>
        public TrackModel? CurrentTrack
        {
            get => _playbackManager.CurrentTrack;
            set
            {
                _playbackManager.CurrentTrack = value;
                RaisePropertyChanged();
                RaiseCommandsCanExecuteChanged();
            }
        }

        /// <summary>
        /// Whether playback is active
        /// </summary>
        public bool IsPlaying
        {
            get => _playbackManager.IsPlaying;
            set
            {
                _playbackManager.IsPlaying = value;
                RaisePropertyChanged();
                RaiseCommandsCanExecuteChanged();
            }
        }

        /// <summary>
        /// Current position in milliseconds
        /// </summary>
        public int PositionMs
        {
            get => _playbackManager.PositionMs;
            set
            {
                _playbackManager.PositionMs = value;
                RaisePropertyChanged();
            }
        }

        /// <summary>
        /// Volume level (0.0 - 1.0)
        /// </summary>
        public double Volume
        {
            get => _playbackManager.Volume;
            set
            {
                if (_isUpdatingVolumeFromState) return; // Prevent recursive updates
                
                _playbackManager.Volume = Math.Max(0.0, Math.Min(1.0, value));
                RaisePropertyChanged();

                // Apply volume immediately when bound value changes
                // Fire-and-forget with internal throttling to avoid flooding
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await _playbackManager.ExecuteSetVolumeAsync(_playbackManager.Volume, _deviceManager.Devices, _deviceManager.WebPlaybackDeviceId);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error setting volume: {ex.Message}");
                    }
                });
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
                _playbackManager.UpdateVolumeFromState(newVolume);
                RaisePropertyChanged(nameof(Volume));
            }
            finally
            {
                _isUpdatingVolumeFromState = false;
            }
        }

        /// <summary>
        /// Available playback devices
        /// </summary>
        public ObservableCollection<DeviceModel> Devices => _deviceManager.Devices;

        /// <summary>
        /// Currently selected device
        /// </summary>
        public DeviceModel? SelectedDevice
        {
            get => _deviceManager.SelectedDevice;
            set => _deviceManager.SelectedDevice = value;
        }

        /// <summary>
        /// Web Playback SDK device ID
        /// </summary>
        public string WebPlaybackDeviceId => _deviceManager.WebPlaybackDeviceId;

        /// <summary>
        /// Whether the user is currently dragging the position slider
        /// </summary>
        public bool IsDraggingSlider
        {
            get => _playbackManager.IsDraggingSlider;
            set => _playbackManager.IsDraggingSlider = value;
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
            get => _playbackManager.IsShuffled;
            set
            {
                _playbackManager.IsShuffled = value;
                RaisePropertyChanged();
            }
        }

        /// <summary>
        /// Repeat mode: 0 = off, 1 = context, 2 = track
        /// </summary>
        public int RepeatMode
        {
            get => _playbackManager.RepeatMode;
            set
            {
                _playbackManager.RepeatMode = value;
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
            await _playbackManager.ExecutePlayPauseAsync(_deviceManager.Devices, _deviceManager.WebPlaybackDeviceId);
            // Burst refresh to quickly populate UI after user action
            SchedulePostActionStateRefresh();
        }

        /// <summary>
        /// Execute next track command
        /// </summary>
        private async Task ExecuteNextAsync()
        {
            await _playbackManager.ExecuteNextAsync(_deviceManager.Devices);
        }

        /// <summary>
        /// Execute previous track command
        /// </summary>
        private async Task ExecutePrevAsync()
        {
            await _playbackManager.ExecutePrevAsync(_deviceManager.Devices);
        }

        /// <summary>
        /// Execute seek command with throttling
        /// </summary>
        private async Task ExecuteSeekAsync(int positionMs)
        {
            await _playbackManager.ExecuteSeekAsync(positionMs, _deviceManager.Devices, _deviceManager.WebPlaybackDeviceId);
            // Update UI immediately for responsiveness
            PositionMs = positionMs;
        }

        /// <summary>
        /// Execute set volume command
        /// </summary>
        private async Task ExecuteSetVolumeAsync(double volume)
        {
            await _playbackManager.ExecuteSetVolumeAsync(volume, _deviceManager.Devices, _deviceManager.WebPlaybackDeviceId);
        }

        /// <summary>
        /// Execute device selection command
        /// </summary>
        private async Task ExecuteToggleShuffleAsync()
        {
            await _playbackManager.ExecuteToggleShuffleAsync(_deviceManager.Devices);
        }

        private async Task ExecuteCycleRepeatAsync()
        {
            await _playbackManager.ExecuteCycleRepeatAsync(_deviceManager.Devices);
        }

        private async Task ExecuteRefreshAsync()
        {
            await _playbackManager.ExecuteRefreshAsync();
        }

        private async Task ExecuteSelectDeviceAsync(DeviceModel device)
        {
            await _playbackManager.ExecuteSelectDeviceAsync(device, _deviceManager.Devices, _deviceManager.WebPlaybackDeviceId);
        }

        /// <summary>
        /// Execute click track command (optimistic playback start)
        /// </summary>
    private async Task ExecuteClickTrackAsync(object trackObj)
        {
            await _playbackManager.ExecuteClickTrackAsync(trackObj, _deviceManager.Devices, _deviceManager.WebPlaybackDeviceId);
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
                // Schedule a delayed refresh and ensure any exceptions are observed
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await Task.Delay(d);
                        await _deviceManager.RefreshDevicesAsync();
                        await RefreshPlayerStateAsync();
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error in scheduled post-action refresh: {ex.Message}");
                    }
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
                _loggingService.LogError($"[STATE_UPDATE] ❌ OnPlayerStateChanged error: {ex.Message}", ex);
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
                if (wasPlaying && !IsPlaying)
                {
                    _loggingService.LogDebug($"[SMART_POLL] ▶️ Resuming API polling after playback stopped");
                    _timerManager.StartStatePollTimer();
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
                var timeSinceRepeatUpdate = DateTimeOffset.UtcNow - _playbackManager.LastRepeatUpdate;
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
                        if (trackChanged && CurrentTrack != null)
                        {
                            _loggingService.LogDebug($"[SMART_POLL] 🔄 Resuming API polling after track changed from '{CurrentTrack.Title}'");
                            _timerManager.StartStatePollTimer();
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
                    if (true)
                    {
                        _loggingService.LogDebug($"[SMART_POLL] ▶️ Resuming API polling after disconnection");
                        _timerManager.StartStatePollTimer();
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
                _deviceManager.WebPlaybackDeviceId = deviceId;
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

        /// <summary>
        /// Handle seek throttle timer tick
        /// </summary>
        private async void OnSeekThrottleTimerTick(object? sender, EventArgs e)
        {
            await Task.Run(() => _playbackManager.OnSeekThrottleTimerTick(sender, e, _deviceManager.Devices, _deviceManager.WebPlaybackDeviceId));
        }

        /// <summary>
        /// UI progress timer tick: advance slider locally when playing on any device
        /// </summary>
        private void OnUiProgressTick(object? sender, EventArgs e)
        {
            _playbackManager.OnUiProgressTick(sender, e);
        }

        /// <summary>
        /// Handle state poll timer tick
        /// </summary>
        private async void OnStatePollTimerTick(object? sender, EventArgs e)
        {
            await _playbackManager.OnStatePollTimerTickAsync(sender, e);
        }

        #endregion

        #region Private Orchestration

        // Fire-and-forget background orchestration for click-to-play to avoid blocking UI
        private async Task OrchestrateClickPlayAsync(TrackModel track, CancellationToken ct, string operationId = "")
        {
            LoggingService.LogToFile($"ORCHESTRATE === STARTING ORCHESTRATION for track: {track.Title} ===\n");

            try
            {
                if (ct.IsCancellationRequested)
                {
                    LoggingService.LogToFile("ORCHESTRATE ❌ Orchestration cancelled before start\n");
                    return;
                }

                // Try to get web device id quickly if missing
                if (string.IsNullOrEmpty(WebPlaybackDeviceId))
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
                var activeDevice = Devices.FirstOrDefault(d => d.IsActive);
                if (activeDevice != null && !string.IsNullOrEmpty(activeDevice.Id) && !string.IsNullOrEmpty(track.Id))
                {
                    var ok = await _spotify.PlayTrackOnDeviceAsync(activeDevice.Id, track.Id);
                    if (ok)
                    {
                        IsPlaying = true;
                        LoggingService.LogToFile("ORCHESTRATE ✅ Click-to-play started successfully on active device\n");
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
                    if (playback?.Device != null && !string.IsNullOrEmpty(playback.Device.Id) && !string.IsNullOrEmpty(track.Id))
                    {
                        var ok = await _spotify.PlayTrackOnDeviceAsync(playback.Device.Id, track.Id);
                        if (ok)
                        {
                            IsPlaying = true;
                            LoggingService.LogToFile("ORCHESTRATE ✅ Click-to-play started successfully on API playback device\n");
                            return;
                        }
                    }
                }
                catch (Exception apiEx)
                {
                    LoggingService.LogToFile($"ORCHESTRATE ❌ Error getting playback from API: {apiEx.Message}\n");
                }

                if (ct.IsCancellationRequested)
                {
                    LoggingService.LogToFile("ORCHESTRATE ❌ Orchestration cancelled after API device check\n");
                    return;
                }

                // 🎯 PRIORITY 3: Try Web Playback device transfer (original logic, but as fallback)
                if (!string.IsNullOrEmpty(WebPlaybackDeviceId) && !string.IsNullOrEmpty(track.Id))
                {

                    try
                    {
                        await _spotify.TransferPlaybackAsync(new[] { WebPlaybackDeviceId }, false);
                    }
                    catch (Exception txEx)
                    {
                        LoggingService.LogToFile($"ORCHESTRATE ⚠️ Transfer (play=false) failed: {txEx.Message}\n");
                    }

                    try { await Task.Delay(350, ct); } catch { }
                    
                    if (ct.IsCancellationRequested)
                    {
                        LoggingService.LogToFile("ORCHESTRATE ❌ Orchestration cancelled\n");
                        return;
                    }
                    
                    var ok = await _spotify.PlayTrackOnDeviceAsync(WebPlaybackDeviceId, track.Id);
                    if (ok)
                    {
                        IsPlaying = true;
                        LoggingService.LogToFile("ORCHESTRATE ✅ Click-to-play started successfully after Web device transfer\n");
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
                        await _spotify.TransferPlaybackAsync(new[] { WebPlaybackDeviceId }, true);
                        LoggingService.LogToFile("ORCHESTRATE ⏳ Waiting 400ms after retry transfer\n");
                        try { await Task.Delay(400, ct); } catch { }
                    }
                    catch (Exception txEx2)
                    {
                        LoggingService.LogToFile($"ORCHESTRATE ⚠️ Transfer retry (play=true) failed: {txEx2.Message}\n");
                    }

                    if (ct.IsCancellationRequested)
                    {
                        LoggingService.LogToFile("ORCHESTRATE ❌ Orchestration cancelled after retry transfer delay\n");
                        return;
                    }

                    LoggingService.LogToFile($"ORCHESTRATE � Retrying play on device {WebPlaybackDeviceId}\n");
                    ok = await _spotify.PlayTrackOnDeviceAsync(WebPlaybackDeviceId, track.Id);
                    if (ok)
                    {
                        IsPlaying = true;
                        LoggingService.LogToFile("ORCHESTRATE ✅ Click-to-play started successfully after retry transfer\n");
                        return;
                    }
                    else
                    {
                        LoggingService.LogToFile("ORCHESTRATE ❌ Retry PlayTrackOnDeviceAsync also returned false on Web device\n");
                    }
                }
                else
                {
                    LoggingService.LogToFile($"ORCHESTRATE ❌ Cannot use Web device transfer: WebPlaybackDeviceId='{WebPlaybackDeviceId}', TrackId='{track.Id}'\n");
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
                            await _webPlaybackBridge.PlayAsync(new[] { track.Uri });
                        });
                        IsPlaying = true;
                        LoggingService.LogToFile("ORCHESTRATE ✅ Click-to-play started via JS bridge\n");
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

        #region Public Methods

        /// <summary>
        /// Initialize the player with access token
        /// </summary>
        public async Task InitializeAsync(string accessToken, string playerHtmlPath)
        {
            try
            {
                LoggingService.LogToFile("PlayerViewModel: InitializeAsync called\n");
                
                LoggingService.LogToFile($"PlayerViewModel: About to call WebPlaybackBridge.InitializeAsync, bridge is null: {_webPlaybackBridge == null}\n");
                if (_webPlaybackBridge == null)
                {
                    throw new InvalidOperationException("WebPlaybackBridge is null");
                }
                await _webPlaybackBridge.InitializeAsync(accessToken, playerHtmlPath);
                LoggingService.LogToFile("PlayerViewModel: WebPlaybackBridge initialized\n");
                
                await _webPlaybackBridge.ConnectAsync();
                LoggingService.LogToFile("PlayerViewModel: WebPlaybackBridge connected\n");
                
                await _deviceManager.RefreshDevicesAsync();
                LoggingService.LogToFile("PlayerViewModel: Devices refreshed\n");
                
                // Load user's top tracks after successful initialization
                LoggingService.LogToFile($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] INIT: Starting top tracks loading in InitializeAsync\n");
                await LoadUserTopTracksAsync();
                LoggingService.LogToFile($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] INIT: LoadUserTopTracksAsync completed in InitializeAsync\n");
            }
            catch (Exception ex)
            {
                LoggingService.LogToFile($"PlayerViewModel.InitializeAsync error: {ex.Message}\n");
                LoggingService.LogToFile($"Stack trace: {ex.StackTrace}\n");
            }
        }

        /// <summary>
        /// Public method to reload user's top tracks
        /// </summary>
        public async Task LoadTopTracksAsync()
        {
            LoggingService.LogToFile($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] TOP_TRACKS: LoadTopTracksAsync called (manual reload)\n");
            await LoadUserTopTracksAsync();
            LoggingService.LogToFile($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] TOP_TRACKS: LoadTopTracksAsync completed (manual reload)\n");
        }
        
        /// <summary>
        /// Test method to load sample tracks for debugging
        /// </summary>
    public void LoadTestTracks()
        {
            LoggingService.LogToFile($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] TOP_TRACKS: LoadTestTracks called - loading test tracks for debugging\n");
            
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
            
            LoggingService.LogToFile($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] TOP_TRACKS: Added {TopTracks.Count} test tracks to collection\n");
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

                    // Quick follow-up refresh to sync UI (observe exceptions)
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await Task.Delay(250);
                            await RefreshPlayerStateAsync();
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"Error in quick follow-up refresh after web activation: {ex.Message}");
                        }
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
                    try { await _deviceManager.RefreshDevicesAsync(); activeDeviceId = SelectedDevice?.Id; } catch { }
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
                        var timeSinceRepeatUpdate = DateTimeOffset.UtcNow - _playbackManager.LastRepeatUpdate;
                        if (timeSinceRepeatUpdate <= TimeSpan.FromSeconds(2))
                        {
                            // Override the API's repeat mode with our optimistic value
                            ps.RepeatMode = _playbackManager.RepeatMode;
                            System.Diagnostics.Debug.WriteLine($"🔁 API polling: preserving optimistic repeat mode {_playbackManager.RepeatMode} ({timeSinceRepeatUpdate.TotalSeconds:F1}s ago)");
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
                LoggingService.LogToFile($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] TOP_TRACKS: LoadUserTopTracksAsync STARTED\n");
                LoggingService.LogToFile($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] TOP_TRACKS: Preparing to call Spotify API for top tracks (limit=20, offset=0, time_range=medium_term)\n");

                var topTracksDto = await _spotify.GetUserTopTracksAsync(20, 0, "medium_term");

                LoggingService.LogToFile($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] TOP_TRACKS: Spotify API call completed\n");
                LoggingService.LogToFile($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] TOP_TRACKS: API returned {topTracksDto?.Items?.Count ?? 0} tracks\n");

                // Clear existing tracks and add the real user data
                await App.Current.Dispatcher.InvokeAsync(() =>
                {
                    LoggingService.LogToFile($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] TOP_TRACKS: Clearing existing tracks collection\n");
                    TopTracks.Clear();

                    if (topTracksDto != null && topTracksDto.Items != null && topTracksDto.Items.Count > 0)
                    {
                        LoggingService.LogToFile($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] TOP_TRACKS: Processing {topTracksDto.Items.Count} tracks from API response\n");

                        int processedCount = 0;
                        foreach (var dto in topTracksDto.Items)
                        {
                            try
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
                                        LoggingService.LogToFile($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] TOP_TRACKS: Track '{track.Title}' has album art: {dto.AlbumImageUrl}\n");
                                    }
                                    catch (UriFormatException uriEx)
                                    {
                                        LoggingService.LogToFile($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] TOP_TRACKS: Invalid album art URI for track '{track.Title}': {dto.AlbumImageUrl} - {uriEx.Message}\n");
                                        track.AlbumArtUri = null;
                                    }
                                }
                                else
                                {
                                    LoggingService.LogToFile($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] TOP_TRACKS: Track '{track.Title}' has no album art\n");
                                }

                                TopTracks.Add(track);
                                processedCount++;

                                LoggingService.LogToFile($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] TOP_TRACKS: Added track {processedCount}/{topTracksDto.Items.Count}: '{track.Title}' by '{track.Artist}' (ID: {track.Id})\n");
                            }
                            catch (Exception trackEx)
                            {
                                LoggingService.LogToFile($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] TOP_TRACKS: ERROR processing track {processedCount + 1}: {trackEx.Message}\n");
                                LoggingService.LogToFile($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] TOP_TRACKS: Track data - ID: {dto.Id}, Name: {dto.Name}, Artists: {dto.Artists}\n");
                            }
                        }

                        LoggingService.LogToFile($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] TOP_TRACKS: Successfully added {TopTracks.Count} tracks to TopTracks collection\n");
                    }
                    else
                    {
                        LoggingService.LogToFile($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] TOP_TRACKS: No tracks returned from API or Items is null\n");
                    }
                });

                LoggingService.LogToFile($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] TOP_TRACKS: LoadUserTopTracksAsync COMPLETED SUCCESSFULLY\n");
            }
            catch (Exception ex)
            {
                LoggingService.LogToFile($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] TOP_TRACKS: ERROR in LoadUserTopTracksAsync: {ex.Message}\n");
                LoggingService.LogToFile($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] TOP_TRACKS: Exception type: {ex.GetType().FullName}\n");
                LoggingService.LogToFile($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] TOP_TRACKS: Stack trace: {ex.StackTrace}\n");

                // Don't clear tracks on error - instead load some fallback tracks so UI isn't empty
                await App.Current.Dispatcher.InvokeAsync(() =>
                {
                    if (TopTracks.Count == 0)
                    {
                        LoggingService.LogToFile($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] TOP_TRACKS: Loading fallback tracks due to API error\n");

                        try
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

                            LoggingService.LogToFile($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] TOP_TRACKS: Added {TopTracks.Count} fallback tracks to collection\n");
                        }
                        catch (Exception fallbackEx)
                        {
                            LoggingService.LogToFile($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] TOP_TRACKS: ERROR loading fallback tracks: {fallbackEx.Message}\n");
                        }
                    }
                    else
                    {
                        LoggingService.LogToFile($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] TOP_TRACKS: Keeping existing {TopTracks.Count} tracks in collection (not loading fallbacks)\n");
                    }
                });

                LoggingService.LogToFile($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] TOP_TRACKS: LoadUserTopTracksAsync COMPLETED WITH ERROR\n");
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
        /// Finalizer for safety net
        /// </summary>
        ~PlayerViewModel()
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
                // Dispose managed resources in reverse order of typical usage/allocation

                try
                {
                    // 1. Cancel and dispose any pending operations first
                    _clickCts?.Cancel();
                    _clickCts?.Dispose();

                    // 2. Cancel any pending TaskCompletionSource
                    if (_webDeviceReadyTcs != null && !_webDeviceReadyTcs.Task.IsCompleted)
                    {
                        _webDeviceReadyTcs.TrySetCanceled();
                    }
                }
                catch (Exception ex)
                {
                    _loggingService?.LogError($"Error cancelling operations in PlayerViewModel.Dispose: {ex.Message}", ex);
                }

                try
                {
                    // 3. Dispose TimerManager (which handles all timer disposal)
                    _timerManager?.Dispose();
                }
                catch (Exception ex)
                {
                    _loggingService?.LogError($"Error disposing TimerManager in PlayerViewModel.Dispose: {ex.Message}", ex);
                }

                try
                {
                    // 4. Dispose semaphore
                    _trackOperationSemaphore?.Dispose();
                }
                catch (Exception ex)
                {
                    _loggingService?.LogError($"Error disposing semaphore in PlayerViewModel.Dispose: {ex.Message}", ex);
                }

                try
                {
                    // 5. Dispose MediaKeyManager
                    _mediaKeyManager?.Dispose();
                }
                catch (Exception ex)
                {
                    _loggingService?.LogError($"Error disposing MediaKeyManager in PlayerViewModel.Dispose: {ex.Message}", ex);
                }

                try
                {
                    // 6. Dispose TrayIconManager
                    _trayIconManager?.Dispose();
                }
                catch (Exception ex)
                {
                    _loggingService?.LogError($"Error disposing TrayIconManager in PlayerViewModel.Dispose: {ex.Message}", ex);
                }

                try
                {
                    // 7. Dispose WebPlaybackBridge last (most complex resource)
                    if (_webPlaybackBridge != null && _webPlaybackBridge is IDisposable disposableBridge)
                    {
                        disposableBridge.Dispose();
                    }
                }
                catch (Exception ex)
                {
                    _loggingService?.LogError($"Error disposing WebPlaybackBridge in PlayerViewModel.Dispose: {ex.Message}", ex);
                }

                // 6. Clear references to prevent memory leaks
                _webDeviceReadyTcs = null;
                _clickCts = null;
                _pendingTrackId = null;
                _pendingPlayTrackId = null;
                _playbackManager.CurrentTrack = null;
            }

            _disposed = true;
        }

        #endregion
    }
}
