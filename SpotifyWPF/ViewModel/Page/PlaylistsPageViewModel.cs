#pragma warning disable CS8602 // Dereference of a possibly null reference.

using System;
using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using AutoMapper;
using GalaSoft.MvvmLight;
using GalaSoft.MvvmLight.Command;
using SpotifyAPI.Web;
using SpotifyWPF.Model;
using SpotifyWPF.Model.Dto;
using SpotifyWPF.Service;
using SpotifyWPF.Service.MessageBoxes;
using Newtonsoft.Json.Linq;
using MessageBoxButton = SpotifyWPF.Service.MessageBoxes.MessageBoxButton;
using MessageBoxResult = SpotifyWPF.Service.MessageBoxes.MessageBoxResult;

namespace SpotifyWPF.ViewModel.Page
{
    public class PlaylistsPageViewModel : ViewModelBase
    {
        private readonly IMapper _mapper;

        private readonly IMessageBoxService _messageBoxService;
        private readonly IConfirmationDialogService _confirmationDialogService;
        private readonly ISubscriptionDialogService _subscriptionDialogService;
        private readonly ISpotify _spotify;

        private Visibility _progressVisibility = Visibility.Hidden;

        private string _status = "Ready";

    private bool _isLoadingPlaylists;
    private bool _isLoadingArtists;
    private int _selectedTabIndex;
    private int _selectedArtistsCount;
    private IList? _selectedArtists;

        public RelayCommand<IList> RemoveTracksFromPlaylistCommand { get; private set; }

        public PlaylistsPageViewModel(ISpotify spotify, IMapper mapper, IMessageBoxService messageBoxService, IConfirmationDialogService confirmationDialogService, ISubscriptionDialogService subscriptionDialogService)
        {
            _spotify = spotify;
            _mapper = mapper;
            _messageBoxService = messageBoxService;
            _confirmationDialogService = confirmationDialogService;
            _subscriptionDialogService = subscriptionDialogService;

            LoadPlaylistsCommand = new RelayCommand(
                async () => await LoadPlaylistsAsync(),
                () => _busyCount == 0 && !_isLoadingPlaylists
            );
            LoadTracksCommand = new RelayCommand<PlaylistDto>(
                async playlist => await LoadTracksAsync(playlist),
                playlist => playlist != null
            );
            DeletePlaylistsCommand = new RelayCommand<IList>(
                async playlists =>
                {
                    if (_isDeletingPlaylists) return;
                    _cancelRequested = false;
                    _isDeletingPlaylists = true;
                    UpdateLoadingUiState();
                    try
                    {
                        await DeletePlaylistsAsync(playlists);
                    }
                    finally
                    {
                        _isDeletingPlaylists = false;
                        UpdateLoadingUiState();
                    }
                },
                playlists => playlists != null && playlists.Count > 0
            );

            RemoveTracksFromPlaylistCommand = new RelayCommand<IList>(
                async items =>
                {
                    if (items == null || items.Count == 0 || CurrentPlaylist == null) return;
                    var tracks = items.Cast<TrackModel>().ToList();
                    await RemoveTracksFromPlaylistAsync(tracks);
                },
                items => items != null && items.Count > 0 && CurrentPlaylist != null && IsCurrentPlaylistOwnedByUser
            );

            StartLoadPlaylistsCommand = new RelayCommand(
                async () =>
                {
                    if (_isLoadingPlaylists) return;
                    _cancelRequested = false; // Reset cancellation flag for fresh start
                    _loadPlaylistsCts = new CancellationTokenSource();
                    UpdateLoadingUiState();
                    try
                    {
                        await LoadPlaylistsAsync(_loadPlaylistsCts.Token);
                    }
                    finally
                    {
                        _loadPlaylistsCts?.Dispose();
                        _loadPlaylistsCts = null;
                        UpdateLoadingUiState();
                    }
                },
                () => CanStart
            );

            StopLoadPlaylistsCommand = new RelayCommand(
                () =>
                {
                    System.Diagnostics.Debug.WriteLine("StopLoadPlaylistsCommand executed");
                    _cancelRequested = true; // cancel deletions
                    _loadPlaylistsCts?.Cancel(); // cancel loading
                    System.Diagnostics.Debug.WriteLine($"Cancellation requested. CTS state: {_loadPlaylistsCts?.IsCancellationRequested}");
                    
                    // Immediately stop the loading state for UI responsiveness
                    _isLoadingPlaylists = false;
                    _isDeletingPlaylists = false;
                    UpdateLoadingUiState();
                },
                () => CanStop
            );

            // F5 refresh current tab
            RefreshCurrentTabCommand = new RelayCommand(
                async () =>
                {
                    // 0 = Playlists tab, 1 = Users/Artists tab
                    if (SelectedTabIndex == 0)
                    {
                        await LoadPlaylistsAsync();
                    }
                    else
                    {
                        await StartLoadFollowedArtistsAsync();
                    }
                },
                () => _busyCount == 0 && !_isLoadingPlaylists && !_isLoadingArtists && !_isDeletingPlaylists
            );

            // Context menu commands
            OpenInSpotifyCommand = new RelayCommand<PlaylistDto>(
                p =>
                {
                    var url = BuildPlaylistWebUrl(p);
                    if (!string.IsNullOrWhiteSpace(url))
                    {
                        TryOpenUrl(url);
                    }
                },
                p => p != null && !string.IsNullOrWhiteSpace(p.Id) && !IsBusyAny
            );

            CopyPlaylistLinkCommand = new RelayCommand<PlaylistDto>(
                p =>
                {
                    var url = BuildPlaylistWebUrl(p);
                    if (!string.IsNullOrWhiteSpace(url))
                    {
                        TryCopyToClipboard(url);
                    }
                },
                p => p != null && !string.IsNullOrWhiteSpace(p.Id)
            );

            UnfollowPlaylistCommand = new RelayCommand<object>(
                async param =>
                {
                    if (param == null) return;
                    if (_isDeletingPlaylists) return;
                    _cancelRequested = false;
                    _isDeletingPlaylists = true;
                    UpdateLoadingUiState();
                    try
                    {
                        IList playlistsToDelete;
                        if (param is PlaylistDto singlePlaylist)
                        {
                            playlistsToDelete = new[] { singlePlaylist };
                        }
                        else if (param is IList list)
                        {
                            playlistsToDelete = list;
                        }
                        else
                        {
                            return; // Invalid parameter
                        }
                        await DeletePlaylistsAsync(playlistsToDelete);
                    }
                    finally
                    {
                        _isDeletingPlaylists = false;
                        UpdateLoadingUiState();
                    }
                },
                param =>
                {
                    if (param == null) return false;
                    if (_isDeletingPlaylists) return false;
                    
                    if (param is PlaylistDto playlist)
                    {
                        return !string.IsNullOrWhiteSpace(playlist.Id);
                    }
                    else if (param is IList list)
                    {
                        return list.Count > 0;
                    }
                    return false;
                }
            );

            // Users/Artists tab
            LoadFollowedArtistsCommand = new RelayCommand(async () => await StartLoadFollowedArtistsAsync(), () => !_isLoadingArtists);
            StopLoadFollowedArtistsCommand = new RelayCommand(() => _loadArtistsCts?.Cancel(), () => _isLoadingArtists);
            UnfollowArtistsCommand = new RelayCommand<IList>(
                async items => await UnfollowArtistsAsync(items),
                items => _selectedArtistsCount > 0
            );
            OpenInSpotifyArtistCommand = new RelayCommand<ArtistDto>(
                a => { var url = BuildArtistWebUrl(a); if (!string.IsNullOrWhiteSpace(url)) TryOpenUrl(url); },
                a => a != null && !string.IsNullOrWhiteSpace(a.Id));
            CopyArtistLinkCommand = new RelayCommand<ArtistDto>(
                a => { var url = BuildArtistWebUrl(a); if (!string.IsNullOrWhiteSpace(url)) TryCopyToClipboard(url); },
                a => a != null && !string.IsNullOrWhiteSpace(a.Id));
            UnfollowArtistCommand = new RelayCommand<ArtistDto>(
                async a => {
                    if (a == null || string.IsNullOrWhiteSpace(a.Id)) return;
                    
                    // If multiple artists are selected, unfollow all selected artists
                    // Otherwise, unfollow just the clicked artist
                    IList artistsToUnfollow;
                    if (_selectedArtistsCount > 1 && _selectedArtists != null)
                    {
                        artistsToUnfollow = _selectedArtists;
                    }
                    else
                    {
                        artistsToUnfollow = new[] { a };
                    }
                    
                    await UnfollowArtistsAsync(artistsToUnfollow);
                },
                a => a != null && !string.IsNullOrWhiteSpace(a.Id));

            // Initialize filtering
            InitializeFiltering();
        }

        // Set up collection change handlers for filtering
        private void InitializeFiltering()
        {
            Playlists.CollectionChanged += (s, e) => ApplyPlaylistsFilter();
            Tracks.CollectionChanged += (s, e) => ApplyTracksFilter();
            FollowedArtists.CollectionChanged += (s, e) => ApplyArtistsFilter();
        }

        private void ApplyPlaylistsFilter()
        {
            FilteredPlaylists.Clear();

            var filteredItems = Playlists.Where(item =>
            {
                // Text filter
                bool matchesText = string.IsNullOrWhiteSpace(_playlistsFilterText) ||
                    (item.Name?.Contains(_playlistsFilterText, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    (item.OwnerName?.Contains(_playlistsFilterText, StringComparison.OrdinalIgnoreCase) ?? false);

                // Owned playlists filter
                bool includeItem = matchesText;
                if (_excludeOwnedPlaylists && !string.IsNullOrWhiteSpace(_currentUserId))
                {
                    includeItem = includeItem && item.OwnerId != _currentUserId;
                }
                if (_onlyShowOwnedPlaylists && !string.IsNullOrWhiteSpace(_currentUserId))
                {
                    includeItem = includeItem && item.OwnerId == _currentUserId;
                }

                return includeItem;
            });

            foreach (var item in filteredItems)
            {
                FilteredPlaylists.Add(item);
            }

            RaisePropertyChanged(nameof(PlaylistsHeader));
        }

        private void ApplyTracksFilter()
        {
            FilteredTracks.Clear();

            var filteredItems = string.IsNullOrWhiteSpace(_tracksFilterText)
                ? Tracks
                : Tracks.Where(item =>
                    (item.Title?.Contains(_tracksFilterText, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    (item.Artist?.Contains(_tracksFilterText, StringComparison.OrdinalIgnoreCase) ?? false));

            foreach (var item in filteredItems)
            {
                FilteredTracks.Add(item);
            }

            RaisePropertyChanged(nameof(TracksHeader));
        }

        private void ApplyArtistsFilter()
        {
            FilteredFollowedArtists.Clear();

            var filteredItems = string.IsNullOrWhiteSpace(_artistsFilterText)
                ? FollowedArtists
                : FollowedArtists.Where(item =>
                    item.Name?.Contains(_artistsFilterText, StringComparison.OrdinalIgnoreCase) ?? false);

            foreach (var item in filteredItems)
            {
                FilteredFollowedArtists.Add(item);
            }

            RaisePropertyChanged(nameof(ArtistsHeader));
        }

        public ObservableCollection<PlaylistDto> Playlists { get; } = new ObservableCollection<PlaylistDto>();

        public ObservableCollection<TrackModel> Tracks { get; } = new ObservableCollection<TrackModel>();

        // Filtered collections for search functionality
        public ObservableCollection<PlaylistDto> FilteredPlaylists { get; } = new ObservableCollection<PlaylistDto>();
        public ObservableCollection<TrackModel> FilteredTracks { get; } = new ObservableCollection<TrackModel>();
        public ObservableCollection<ArtistDto> FilteredFollowedArtists { get; } = new ObservableCollection<ArtistDto>();

        // Current playlist for track operations
        private PlaylistDto? _currentPlaylist;
        public PlaylistDto? CurrentPlaylist
        {
            get => _currentPlaylist;
            set
            {
                if (_currentPlaylist != value)
                {
                    _currentPlaylist = value;
                    RaisePropertyChanged();
                    RaisePropertyChanged(nameof(IsCurrentPlaylistOwnedByUser));
                }
            }
        }

        // Check if current playlist is owned by the current user
        public bool IsCurrentPlaylistOwnedByUser => CurrentPlaylist != null && 
                                                   CurrentPlaylist.OwnerId == CurrentUserId;

        // Dynamic headers with counts
        public string PlaylistsHeader => FilteredPlaylists.Count > 0 ? $"Playlists ({FilteredPlaylists.Count})" : "Playlists";
        public string TracksHeader => FilteredTracks.Count > 0 ? $"Tracks ({FilteredTracks.Count})" : "Tracks";
        public string ArtistsHeader => FilteredFollowedArtists.Count > 0 ? $"Users/Artists ({FilteredFollowedArtists.Count})" : "Users/Artists";

        // Search filter properties
        private string _playlistsFilterText = string.Empty;
        public string PlaylistsFilterText
        {
            get => _playlistsFilterText;
            set
            {
                if (_playlistsFilterText != value)
                {
                    _playlistsFilterText = value;
                    RaisePropertyChanged();
                    ApplyPlaylistsFilter();
                }
            }
        }

        private string _tracksFilterText = string.Empty;
        public string TracksFilterText
        {
            get => _tracksFilterText;
            set
            {
                if (_tracksFilterText != value)
                {
                    _tracksFilterText = value;
                    RaisePropertyChanged();
                    ApplyTracksFilter();
                }
            }
        }

        private string _artistsFilterText = string.Empty;
        public string ArtistsFilterText
        {
            get => _artistsFilterText;
            set
            {
                if (_artistsFilterText != value)
                {
                    _artistsFilterText = value;
                    RaisePropertyChanged();
                    ApplyArtistsFilter();
                }
            }
        }

        // Filter to exclude owned playlists
        private bool _excludeOwnedPlaylists;
        public bool ExcludeOwnedPlaylists
        {
            get => _excludeOwnedPlaylists;
            set
            {
                if (_excludeOwnedPlaylists != value)
                {
                    _excludeOwnedPlaylists = value;
                    RaisePropertyChanged();
                    // If enabling exclude, disable only show owned
                    if (value && _onlyShowOwnedPlaylists)
                    {
                        OnlyShowOwnedPlaylists = false;
                    }
                    ApplyPlaylistsFilter();
                }
            }
        }

        private bool _onlyShowOwnedPlaylists;
        public bool OnlyShowOwnedPlaylists
        {
            get => _onlyShowOwnedPlaylists;
            set
            {
                if (_onlyShowOwnedPlaylists != value)
                {
                    _onlyShowOwnedPlaylists = value;
                    RaisePropertyChanged();
                    // If enabling only show owned, disable exclude
                    if (value && _excludeOwnedPlaylists)
                    {
                        ExcludeOwnedPlaylists = false;
                    }
                    ApplyPlaylistsFilter();
                }
            }
        }

        // Current user ID for filtering owned playlists
        private string? _currentUserId;
        public string? CurrentUserId
        {
            get => _currentUserId;
            set
            {
                if (_currentUserId != value)
                {
                    _currentUserId = value;
                    RaisePropertyChanged();
                    RaisePropertyChanged(nameof(IsCurrentPlaylistOwnedByUser));
                    // Re-apply filter when user ID changes
                    if (_excludeOwnedPlaylists || _onlyShowOwnedPlaylists)
                    {
                        ApplyPlaylistsFilter();
                    }
                }
            }
        }

        public int SelectedArtistsCount
        {
            get => _selectedArtistsCount;
            internal set
            {
                if (_selectedArtistsCount != value)
                {
                    _selectedArtistsCount = value;
                    RaisePropertyChanged();
                }
            }
        }

        public IList? SelectedArtists
        {
            get => _selectedArtists;
            internal set
            {
                _selectedArtists = value;
                RaisePropertyChanged();
            }
        }

    // Users/Artists tab collections
    public ObservableCollection<ArtistDto> FollowedArtists { get; } = new ObservableCollection<ArtistDto>();

        // Greeting and profile
        private string _greetingText = string.Empty;
        public string GreetingText
        {
            get => _greetingText;
            set { _greetingText = value; RaisePropertyChanged(); }
        }

        private bool _isFreeUser;
        public bool IsFreeUser
        {
            get => _isFreeUser;
            set { _isFreeUser = value; RaisePropertyChanged(); }
        }

        private string? _profileImagePath;
        public string? ProfileImagePath
        {
            get => _profileImagePath;
            set { _profileImagePath = value; RaisePropertyChanged(); }
        }

        public async Task LoadGreetingAsync()
        {
            try
            {
                var name = await _spotify.GetUserDisplayNameAsync().ConfigureAwait(false);
                var imgPath = await _spotify.GetProfileImageCachedPathAsync().ConfigureAwait(false);
                var profile = await _spotify.GetPrivateProfileAsync().ConfigureAwait(false);
                var subscriptionType = await _spotify.GetUserSubscriptionTypeAsync().ConfigureAwait(false);
                
                Application.Current.Dispatcher.Invoke(() =>
                {
                    var greeting = string.IsNullOrWhiteSpace(name) ? "Hey there" : $"Hey {name}";
                    GreetingText = greeting;
                    ProfileImagePath = imgPath;
                    CurrentUserId = profile?.Id;
                    IsFreeUser = subscriptionType?.ToLower() != "premium";
                });
            }
            catch { }
        }

        // Tracciamo gli ID già caricati per evitare duplicati tra tentativi
        private readonly HashSet<string> _playlistIds = new HashSet<string>();

        public string Status
        {
            get => _status;

            set
            {
                _status = value;
                RaisePropertyChanged();
            }
        }

        public Visibility ProgressVisibility
        {
            get => _progressVisibility;

            set
            {
                _progressVisibility = value;
                RaisePropertyChanged();
            }
        }

        // Devices for "Play to" context menus
        public ObservableCollection<SpotifyAPI.Web.Device> Devices { get; } = new ObservableCollection<SpotifyAPI.Web.Device>();
        public RelayCommand<string> PlayToDeviceCommand => new RelayCommand<string>(async param => 
        {
            if (IsFreeUser)
            {
                System.Diagnostics.Debug.WriteLine($"[PlaylistsPage] PlayToDeviceCommand: User does not have premium subscription (IsFreeUser={IsFreeUser}), showing modal and blocking access");
                _subscriptionDialogService.ShowSubscriptionDialog("Premium Feature Required", "Playing music on specific devices requires a Spotify Premium subscription. Upgrade to access this feature.", "Play to Device", "Transfer playback to any of your connected devices");
                return;
            }
            await PlayToDeviceAsync(param);
        });

    // Removed obsolete Submenu track-id plumbing (now done via BindingProxy+MultiBinding in XAML)

        public RelayCommand<PlaylistDto> LoadTracksCommand { get; }

        public RelayCommand<IList> DeletePlaylistsCommand { get; }

        public RelayCommand LoadPlaylistsCommand { get; }

        public RelayCommand StartLoadPlaylistsCommand { get; }

        public RelayCommand StopLoadPlaylistsCommand { get; }

    // F5: refresh according to selected tab
    public RelayCommand RefreshCurrentTabCommand { get; }

    // Users/Artists commands
        public RelayCommand LoadFollowedArtistsCommand { get; }
        public RelayCommand StopLoadFollowedArtistsCommand { get; }
    public RelayCommand<IList> UnfollowArtistsCommand { get; }
        public RelayCommand<ArtistDto> OpenInSpotifyArtistCommand { get; }
        public RelayCommand<ArtistDto> CopyArtistLinkCommand { get; }
        public RelayCommand<ArtistDto> UnfollowArtistCommand { get; }

        // Context menu commands
        public RelayCommand<PlaylistDto> OpenInSpotifyCommand { get; }
        public RelayCommand<PlaylistDto> CopyPlaylistLinkCommand { get; }
        public RelayCommand<object> UnfollowPlaylistCommand { get; }

        private CancellationTokenSource? _loadPlaylistsCts;
        private volatile bool _cancelRequested;
        private bool _isDeletingPlaylists;

        public bool CanStart => !_isLoadingPlaylists && _busyCount == 0 && !_isDeletingPlaylists;

        public bool CanStop => _isLoadingPlaylists || _isDeletingPlaylists;

        private int _busyCount = 0;

        private bool IsBusyAny => _isLoadingPlaylists || _isDeletingPlaylists || _busyCount > 0;

        private void UpdateLoadingUiState()
        {
            LoadPlaylistsCommand?.RaiseCanExecuteChanged();
            LoadFollowedArtistsCommand?.RaiseCanExecuteChanged();
            StopLoadFollowedArtistsCommand?.RaiseCanExecuteChanged();
            StartLoadPlaylistsCommand?.RaiseCanExecuteChanged();
            StopLoadPlaylistsCommand?.RaiseCanExecuteChanged();
            OpenInSpotifyCommand?.RaiseCanExecuteChanged();
            CopyPlaylistLinkCommand?.RaiseCanExecuteChanged();
            UnfollowPlaylistCommand?.RaiseCanExecuteChanged();
            UnfollowArtistsCommand?.RaiseCanExecuteChanged();
            RefreshCurrentTabCommand?.RaiseCanExecuteChanged();
            RaisePropertyChanged(nameof(CanStart));
            RaisePropertyChanged(nameof(CanStop));
        }

        public int SelectedTabIndex
        {
            get => _selectedTabIndex;
            set
            {
                if (_selectedTabIndex == value) return;
                _selectedTabIndex = value;
                RaisePropertyChanged();
                RefreshCurrentTabCommand?.RaiseCanExecuteChanged();
            }
        }

        private void StartBusy(String initialStatus)
        {
            System.Threading.Interlocked.Increment(ref _busyCount);
            Application.Current?.Dispatcher.BeginInvoke((Action)(() =>
            {
                if (!string.IsNullOrEmpty(initialStatus))
                {
                    Status = initialStatus;
                }
                ProgressVisibility = Visibility.Visible;
                // Aggiorna lo stato dei comandi
                UpdateLoadingUiState();
            }));
        }

        private void EndBusy()
        {
            var left = System.Threading.Interlocked.Decrement(ref _busyCount);
            if (left <= 0)
            {
                // Normalizza a zero
                System.Threading.Interlocked.Exchange(ref _busyCount, 0);
                Application.Current?.Dispatcher.BeginInvoke((Action)(() =>
                {
                    Status = "Ready";
                    ProgressVisibility = Visibility.Hidden;
                    // Aggiorna lo stato dei comandi
                    UpdateLoadingUiState();
                }));
            }
            else
            {
                // Aggiorna comunque lo stato del comando in caso vari il conteggio
                Application.Current?.Dispatcher.BeginInvoke((Action)(() =>
                {
                    UpdateLoadingUiState();
                }));
            }
        }

        public async Task RefreshDevicesForTracksMenuAsync()
        {
            try
            {
                var subscriptionType = await _spotify.GetUserSubscriptionTypeAsync().ConfigureAwait(false);
                if (subscriptionType?.ToLower() == "premium")
                {
                    var list = await _spotify.GetDevicesAsync().ConfigureAwait(false);
                    await Application.Current.Dispatcher.BeginInvoke((Action)(() =>
                    {
                        Devices.Clear();
                        foreach (var d in list)
                        {
                            if (d != null && !string.IsNullOrWhiteSpace(d.Id)) 
                            {
                                Devices.Add(d);
                                System.Diagnostics.Debug.WriteLine($"[PlaylistsPage] Device: {d.Name} (ID: {d.Id}) - Active: {d.IsActive}");
                            }
                        }
                        System.Diagnostics.Debug.WriteLine($"[PlaylistsPage] Refreshed {Devices.Count} devices in Play To submenu");
                    }));
                }
                else
                {
                    // Clear devices for free users
                    await Application.Current.Dispatcher.BeginInvoke((Action)(() =>
                    {
                        Devices.Clear();
                        System.Diagnostics.Debug.WriteLine("[PlaylistsPage] Cleared devices for free user");
                    }));
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[PlaylistsPage] Error refreshing devices: {ex.Message}");
            }
        }

        private async Task PlayToDeviceAsync(string? param)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"[PlaylistsPage] PlayToDeviceAsync called with param: {param ?? "NULL"}");
                
                // Parse the parameter: deviceId|trackId
                System.Diagnostics.Debug.WriteLine($"[PlaylistsPage] DEBUG: MultiBinding expects deviceId|trackId format");
                
                if (string.IsNullOrWhiteSpace(param)) 
                {
                    System.Diagnostics.Debug.WriteLine($"[PlaylistsPage] PlayToDeviceAsync: param is null or empty, returning");
                    System.Diagnostics.Debug.WriteLine($"[PlaylistsPage] HINT: Check if MultiBinding is working - deviceId from Device.Id and trackId from PlacementTarget.DataContext.Id");
                    return;
                }
                
                var parts = param.Split('|');
                if (parts.Length != 2) 
                {
                    System.Diagnostics.Debug.WriteLine($"[PlaylistsPage] PlayToDeviceAsync: param does not contain exactly 2 parts, found {parts.Length}");
                    return;
                }
                
                var deviceId = parts[0];
                var trackId = parts[1];
                if (string.IsNullOrWhiteSpace(deviceId) || string.IsNullOrWhiteSpace(trackId)) 
                {
                    System.Diagnostics.Debug.WriteLine($"[PlaylistsPage] PlayToDeviceAsync: deviceId or trackId is null/empty. Device: '{deviceId}', Track: '{trackId}'");
                    return;
                }
                
                System.Diagnostics.Debug.WriteLine($"[PlaylistsPage] Playing track {trackId} on device {deviceId}");
                // Ensure the device is active by transferring playback there first (play=true), then try playing
                try
                {
                    await _spotify.TransferPlaybackAsync(new[] { deviceId }, true);
                    await Task.Delay(400);
                }
                catch { }
                var ok = await _spotify.PlayTrackOnDeviceAsync(deviceId, trackId);
                System.Diagnostics.Debug.WriteLine(ok
                    ? $"Playing track {trackId} on device {deviceId}."
                    : $"Failed to start playback on device {deviceId}.");
                
                // Wait a moment for Spotify to update the active device state
                System.Diagnostics.Debug.WriteLine($"[PlaylistsPage] Waiting for Spotify to update device state...");
                await Task.Delay(1000);
                
                // Refresh devices list to update active device checkmarks
                System.Diagnostics.Debug.WriteLine($"[PlaylistsPage] Refreshing devices list after Play To command");
                await RefreshDevicesForTracksMenuAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error starting playback: {ex.Message}");
            }
        }

        public async Task DeletePlaylistsAsync(IList items)
        {
            System.Diagnostics.Debug.WriteLine($"[PlaylistDelete] DeletePlaylistsAsync called with {items?.Count ?? 0} items");

            if (items == null || items.Count == 0) 
            {
                System.Diagnostics.Debug.WriteLine("[PlaylistDelete] No items provided, returning");
                return;
            }

            var playlists = items.Cast<PlaylistDto>().ToList();
            System.Diagnostics.Debug.WriteLine($"[PlaylistDelete] Cast to {playlists.Count} playlists");

            if (!playlists.Any()) 
            {
                System.Diagnostics.Debug.WriteLine("[PlaylistDelete] No valid playlists, returning");
                return;
            }

            // Determine operation type and show appropriate confirmation
            var ownedPlaylists = playlists.Where(p => p.OwnerId == _spotify.CurrentUser?.Id).ToList();
            var notOwnedPlaylists = playlists.Except(ownedPlaylists).ToList();

            System.Diagnostics.Debug.WriteLine($"[PlaylistDelete] Current user ID: {_spotify.CurrentUser?.Id}");
            foreach (var p in playlists)
            {
                System.Diagnostics.Debug.WriteLine($"[PlaylistDelete] Playlist '{p.Name}' - OwnerId: {p.OwnerId}, IsOwned: {p.OwnerId == _spotify.CurrentUser?.Id}");
            }

            string title, message, confirmButtonText;

            if (ownedPlaylists.Count == playlists.Count)
            {
                // All playlists are owned - delete operation
                title = "Confirm Deletion";
                message = playlists.Count == 1
                    ? $"Are you sure you want to delete the playlist '{playlists[0].Name}'?"
                    : $"Are you sure you want to delete these {playlists.Count} playlists?";
                confirmButtonText = "Delete";
            }
            else if (ownedPlaylists.Count == 0)
            {
                // No playlists are owned - unfollow operation
                title = "Confirm Unfollow";
                message = playlists.Count == 1
                    ? $"Are you sure you want to unfollow the playlist '{playlists[0].Name}'?"
                    : $"Are you sure you want to unfollow these {playlists.Count} playlists?";
                confirmButtonText = "Unfollow";
            }
            else
            {
                // Mixed ownership - delete and unfollow operation
                title = "Confirm Delete/Unfollow";
                var deleteNames = ownedPlaylists.Select(p => p.Name);
                var unfollowNames = notOwnedPlaylists.Select(p => p.Name);
                message = $"The following playlists will be deleted:\n{string.Join("\n", deleteNames)}\n\n" +
                         $"The following playlists will be unfollowed:\n{string.Join("\n", unfollowNames)}";
                confirmButtonText = "Continue";
            }

            System.Diagnostics.Debug.WriteLine($"[PlaylistDelete] Showing confirmation dialog: {title}");

            var result = _confirmationDialogService.ShowConfirmation(
                title,
                message,
                confirmButtonText,
                "Cancel"
            );

            System.Diagnostics.Debug.WriteLine($"[PlaylistDelete] Confirmation result: {result}");

            if (result != true) 
            {
                System.Diagnostics.Debug.WriteLine("[PlaylistDelete] User cancelled, returning");
                return;
            }

            System.Diagnostics.Debug.WriteLine($"[PlaylistDelete] Starting deletion of {playlists.Count} playlists");

            StartBusy($"Deleting {playlists.Count} playlist(s)...");

            try
            {
                // Use parallel processing for better performance, with conservative rate limiting
                const int maxWorkers = 6;
                const int delayBetweenRequestsMs = 1000;
                var workersCount = Math.Min(maxWorkers, playlists.Count);
                var playlistsQueue = new ConcurrentQueue<PlaylistDto>(playlists);
                var workers = new List<Task>();

                System.Diagnostics.Debug.WriteLine($"[PlaylistDelete] Starting {workersCount} workers for {playlists.Count} playlists");

                for (var w = 0; w < workersCount; w++)
                {
                    var workerId = w; // Capture for logging
                    workers.Add(Task.Run(async () =>
                    {
                        System.Diagnostics.Debug.WriteLine($"[PlaylistDelete] Worker {workerId} started");
                        var processedCount = 0;
                        
                        while (playlistsQueue.TryDequeue(out var playlist))
                        {
                            System.Diagnostics.Debug.WriteLine($"[PlaylistDelete] Worker {workerId} processing playlist '{playlist.Name}' (ID: {playlist.Id})");

                            if (_cancelRequested) 
                            {
                                System.Diagnostics.Debug.WriteLine($"[PlaylistDelete] Worker {workerId} stopped: cancellation requested (processed {processedCount} playlists)");
                                break;
                            }

                            // Retry logic for individual playlist operations
                            const int maxPlaylistAttempts = 3;
                            var playlistSuccess = false;
                            
                            for (var attempt = 1; attempt <= maxPlaylistAttempts && !playlistSuccess; attempt++)
                            {
                                try
                                {
                                    System.Diagnostics.Debug.WriteLine($"[PlaylistDelete] Worker {workerId} attempt {attempt} for playlist '{playlist.Name}'");

                                    if (ownedPlaylists.Contains(playlist))
                                    {
                                        // Delete owned playlist
                                        System.Diagnostics.Debug.WriteLine($"[PlaylistDelete] Worker {workerId} deleting owned playlist '{playlist.Name}'");
                                        await _spotify.DeletePlaylistAsync(playlist.Id!);
                                        System.Diagnostics.Debug.WriteLine($"[PlaylistDelete] Worker {workerId} successfully deleted playlist '{playlist.Name}'");
                                    }
                                    else
                                    {
                                        // Unfollow not owned playlist
                                        System.Diagnostics.Debug.WriteLine($"[PlaylistDelete] Worker {workerId} unfollowing playlist '{playlist.Name}'");
                                        await _spotify.UnfollowPlaylistAsync(playlist.Id!);
                                        System.Diagnostics.Debug.WriteLine($"[PlaylistDelete] Worker {workerId} successfully unfollowed playlist '{playlist.Name}'");
                                    }

                                    processedCount++;
                                    System.Diagnostics.Debug.WriteLine($"[PlaylistDelete] Worker {workerId} successfully processed playlist '{playlist.Name}' ({processedCount} total)");

                                    // Remove from UI
                                    System.Diagnostics.Debug.WriteLine($"[PlaylistDelete] Worker {workerId} removing playlist '{playlist.Name}' from UI");
                                    await Application.Current.Dispatcher.BeginInvoke((Action)(() =>
                                    {
                                        var wasRemoved = Playlists.Remove(playlist);
                                        var idRemoved = _playlistIds.Remove(playlist.Id!);
                                        RaisePropertyChanged(nameof(PlaylistsHeader));
                                        System.Diagnostics.Debug.WriteLine($"[PlaylistDelete] UI removal: playlist removed={wasRemoved}, id removed={idRemoved}, remaining playlists={Playlists.Count}");
                                    }));

                                    playlistSuccess = true;

                                    // Slow down subsequent calls to respect rate limits
                                    var delay = delayBetweenRequestsMs + Random.Shared.Next(0, 250);
                                    System.Diagnostics.Debug.WriteLine($"[PlaylistDelete] Worker {workerId} delaying for {delay}ms");
                                    await Task.Delay(delay);
                                }
                                catch (InvalidOperationException ex) when (ex.Message.Contains("Not authenticated"))
                                {
                                    // Authentication required - navigate to login page
                                    System.Diagnostics.Debug.WriteLine($"[PlaylistDelete] Authentication required for playlist '{playlist.Name}': {ex.Message}");
                                    await Application.Current.Dispatcher.BeginInvoke((Action)(() =>
                                    {
                                        MessengerInstance.Send(new object(), MessageType.AuthenticationRequired);
                                        System.Diagnostics.Debug.WriteLine("[PlaylistDelete] Sent AuthenticationRequired message");
                                    }));
                                    break; // Stop processing this playlist
                                }
                                catch (Exception ex)
                                {
                                    var isRetryableError = ex.Message.Contains("502") || 
                                                          ex.Message.Contains("500") || 
                                                          ex.Message.Contains("503") || 
                                                          ex.Message.Contains("504") ||
                                                          ex.Message.Contains("Bad Gateway") ||
                                                          ex.Message.Contains("Internal Server Error") ||
                                                          ex.Message.Contains("Service Unavailable") ||
                                                          ex.Message.Contains("Gateway Timeout");
                                    
                                    System.Diagnostics.Debug.WriteLine($"[PlaylistDelete] Worker {workerId} exception on playlist '{playlist.Name}' attempt {attempt}: {ex.Message} (retryable: {isRetryableError})");

                                    if (isRetryableError && attempt < maxPlaylistAttempts)
                                    {
                                        var backoffDelay = (int)(Math.Pow(2, attempt - 1) * 1000); // 1s, 2s, 4s
                                        System.Diagnostics.Debug.WriteLine($"[PlaylistDelete] Worker {workerId} retrying playlist '{playlist.Name}' in {backoffDelay}ms (attempt {attempt}/{maxPlaylistAttempts}): {ex.Message}");
                                        await Task.Delay(backoffDelay);
                                    }
                                    else
                                    {
                                        if (isRetryableError)
                                        {
                                            System.Diagnostics.Debug.WriteLine($"[PlaylistDelete] Worker {workerId} failed to delete/unfollow playlist '{playlist.Name}' after {maxPlaylistAttempts} attempts: {ex.Message}");
                                        }
                                        else
                                        {
                                            System.Diagnostics.Debug.WriteLine($"[PlaylistDelete] Worker {workerId} failed to delete/unfollow playlist '{playlist.Name}' (non-retryable error): {ex.Message}");
                                        }
                                        break; // Don't retry non-retryable errors or max attempts reached
                                    }
                                }
                            }
                        }
                        
                        if (!_cancelRequested)
                        {
                            System.Diagnostics.Debug.WriteLine($"[PlaylistDelete] Worker {workerId} stopped: queue empty (processed {processedCount} playlists)");
                        }
                    }));
                }

                System.Diagnostics.Debug.WriteLine("[PlaylistDelete] Waiting for all workers to complete");

                // Wait for all workers to complete
                await Task.WhenAll(workers);

                System.Diagnostics.Debug.WriteLine($"[PlaylistDelete] All workers completed. Successfully deleted/unfollowed {playlists.Count} playlist(s)");
            }
            catch (Exception ex)
            {
                var errorMessage = $"Failed to delete playlists. Error: {ex.Message}";
                System.Diagnostics.Debug.WriteLine($"[PlaylistDelete] Outer exception: {errorMessage}");

                // Show error dialog
                _confirmationDialogService.ShowConfirmation(
                    "Error",
                    errorMessage,
                    "OK",
                    "",
                    false
                );
            }
            finally
            {
                System.Diagnostics.Debug.WriteLine("[PlaylistDelete] Ending busy state");
                EndBusy();
            }
        }

        public async Task RemoveTracksFromPlaylistAsync(IList<TrackModel> tracks)
        {
            if (tracks == null || tracks.Count == 0 || CurrentPlaylist == null) return;

            if (!tracks.Any()) return;

            var message = tracks.Count == 1
                ? $"Are you sure you want to remove track '{tracks.ElementAt(0).Title}' from playlist '{CurrentPlaylist.Name}'?"
                : $"Are you sure you want to remove these {tracks.Count} tracks from playlist '{CurrentPlaylist.Name}'?";

            var result = _confirmationDialogService.ShowConfirmation(
                "Confirm Removal",
                message,
                "Remove",
                "Cancel"
            );

            if (result != true) return;

            LoggingService.LogToFile($"[{DateTime.Now:o}] User requested to remove {tracks.Count} tracks from playlist '{CurrentPlaylist.Name}'");

            if (_spotify.Api == null)
            {
                System.Diagnostics.Debug.WriteLine("You must be logged in to remove tracks from playlists.");
                return;
            }

            StartBusy($"Removing {tracks.Count} track(s) from playlist...");

            try
            {
                var trackUris = tracks.Select(t => t.Uri).Where(uri => !string.IsNullOrEmpty(uri)).ToList();
                if (trackUris.Any())
                {
                    LoggingService.LogToFile($"[{DateTime.Now:o}] Starting API calls to remove tracks from playlist");
                    // Process tracks in batches of 100 (Spotify API limit)
                    const int batchSize = 100;
                    for (int i = 0; i < trackUris.Count; i += batchSize)
                    {
                        var batch = trackUris.Skip(i).Take(batchSize).ToList();
                        await _spotify.RemoveTracksFromPlaylistAsync(CurrentPlaylist.Id!, batch);
                        
                        // Update progress message for large batches
                        if (trackUris.Count > batchSize)
                        {
                            var processed = Math.Min(i + batchSize, trackUris.Count);
                            Status = $"Removing tracks from playlist... ({processed}/{trackUris.Count})";
                        }
                    }

                    // Remove tracks from UI
                    await Application.Current.Dispatcher.BeginInvoke((Action)(() =>
                    {
                        foreach (var track in tracks)
                        {
                            Tracks.Remove(track);
                        }
                        RaisePropertyChanged(nameof(TracksHeader));
                    }));
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error removing tracks from playlist: {ex.Message}");
                await Application.Current.Dispatcher.BeginInvoke((Action)(() =>
                {
                    _messageBoxService.ShowMessageBox(
                        $"Failed to remove tracks from playlist. Error: {ex.Message}",
                        "Removal Failed",
                        MessageBoxButton.OK,
                        MessageBoxIcon.Error
                    );
                }));
            }
            finally
            {
                EndBusy();
                LoggingService.LogToFile($"[{DateTime.Now:o}] UI spinner stopped after removing tracks");
            }
        }

        public async Task LoadPlaylistsAsync(CancellationToken cancellationToken = default)
        {
            if (_spotify.Api == null)
            {
                System.Diagnostics.Debug.WriteLine("You must be logged in to load playlists.");
                return;
            }

            if (_isLoadingPlaylists)
            {
                System.Diagnostics.Debug.WriteLine("A playlists load is already in progress. Skipping.");
                return;
            }
            _isLoadingPlaylists = true;
            UpdateLoadingUiState();

            try
            {
                StartBusy("Loading playlists...");
                System.Diagnostics.Debug.WriteLine("Starting playlists load (offset-based, keeping existing items in UI).");

                // Per un nuovo fetch/refresh, svuotiamo UI e set ID per evitare dedup da run precedenti
                await Application.Current.Dispatcher.BeginInvoke((Action)(() =>
                {
                    Playlists.Clear();
                    _playlistIds.Clear();
                }));

                const int limit = 50;
                int offset = 0;
                int? expectedTotal = null;

                // 1) Ottieni la prima pagina valida con retry (per ricavare expectedTotal)
                {
                    const int pageMaxAttempts = 20;
                    bool pageLoaded = false;
                    PagingDto<PlaylistDto>? page = null;

                    for (var attempt = 1; attempt <= pageMaxAttempts && !pageLoaded && !cancellationToken.IsCancellationRequested; attempt++)
                    {
                        try
                        {
                            await _spotify.EnsureAuthenticatedAsync();
                            page = await _spotify.GetMyPlaylistsPageAsync(offset, limit);

                            // Log dell'output dell'API (redacted)
                            try
                            {
                                var json = BuildPlaylistsPageApiOutput(page);
                                System.Diagnostics.Debug.WriteLine($"Spotify API response (playlists page offset {offset}) [redacted]:");
                                foreach (var line in json.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
                                {
                                    System.Diagnostics.Debug.WriteLine("  " + line);
                                }
                            }
                            catch { /* ignore logging issues */ }

                            // Validazione coerenza (anche per la prima pagina)
                            ValidatePlaylistsPageConsistency(page, expectedTotal, 0);

                            // Prima pagina valida: fissa il totale atteso
                            if (page != null && page.Total > 0)
                            {
                                expectedTotal = page.Total;
                                System.Diagnostics.Debug.WriteLine($"Detected expected total playlists from API: {expectedTotal.Value}");
                            }

                            // Aggiunge elementi (deduplica)
                            var countToAdd = page?.Items?.Count ?? 0;
                            if (countToAdd > 0)
                            {
                                var toAdd = page;
                                var disp = Application.Current?.Dispatcher;
                                if (disp != null)
                                {
                                    await disp.BeginInvoke((Action)(() => { AddPlaylists(toAdd); }));
                                }
                                else
                                {
                                    AddPlaylists(toAdd);
                                }
                            }

                            pageLoaded = true;
                        }
                        catch (APIException apiEx)
                        {
                            System.Diagnostics.Debug.WriteLine($"Page fetch attempt {attempt} at offset {offset} failed: {apiEx.Message}");
                            if (apiEx.Message != null && apiEx.Message.IndexOf("access token expired", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                var ok = await _spotify.EnsureAuthenticatedAsync();
                                if (!ok) System.Diagnostics.Debug.WriteLine("Automatic token refresh failed.");
                            }
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"Page fetch attempt {attempt} at offset {offset} failed: {ex.Message}");
                            if (ex is SpotifyWPF.Service.RateLimitException rateEx)
                            {
                                // Show user-friendly message for rate limit
                                int? retryAfter = null;
                                if (rateEx.InnerException is APIException apiEx)
                                {
                                    retryAfter = GetRetryAfterSeconds(apiEx.Response?.Headers);
                                }
                                var message = retryAfter.HasValue ?
                                    $"Spotify API rate limit exceeded. Please wait {retryAfter.Value} seconds before trying to load playlists again." :
                                    "Spotify API rate limit exceeded. Please wait a moment before trying to load playlists again.";
                                await Application.Current.Dispatcher.InvokeAsync(() =>
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

                    if (!pageLoaded)
                    {
                        System.Diagnostics.Debug.WriteLine("Stopping load: could not load first page after 20 attempts.");
                        return;
                    }

                    if (cancellationToken.IsCancellationRequested || _cancelRequested)
                    {
                        System.Diagnostics.Debug.WriteLine("Load cancelled during first page phase.");
                        return;
                    }
                }

                // Se non abbiamo potuto stabilire un totale atteso, fermiamoci
                if (!expectedTotal.HasValue || expectedTotal.Value <= 0)
                {
                    System.Diagnostics.Debug.WriteLine("Expected total could not be determined. Stopping.");
                    return;
                }

                // 2) Strategia di paginazione: solo offset-based in parallelo
                var total = expectedTotal.Value;

                var nextStart = limit; // abbiamo già aggiunto offset 0 (se aveva items)
                var offsets = new System.Collections.Concurrent.ConcurrentQueue<int>();
                for (var off = nextStart; off < total; off += limit)
                {
                    offsets.Enqueue(off);
                }

                // Calcolo dinamico dei worker (conservativo) per ridurre i 429
                var pagesRemaining = (int)Math.Ceiling((total - nextStart) / (double)limit);
                var computedWorkers = Math.Max(1, Math.Min(pagesRemaining, ComputeWorkerCount(total, 2000)));
                System.Diagnostics.Debug.WriteLine($"Starting concurrent page fetch with {computedWorkers} worker(s). Total expected={total}, page size={limit}");

                var workers = new List<Task>();
                var allLoaded = false;

                for (var w = 0; w < computedWorkers; w++)
                {
                    workers.Add(Task.Run(async () =>
                    {
                        while (!allLoaded && !cancellationToken.IsCancellationRequested && !_cancelRequested && offsets.TryDequeue(out var off))
                        {
                            System.Diagnostics.Debug.WriteLine($"Worker processing offset {off}, cancellation requested: {cancellationToken.IsCancellationRequested}");
                            // Retry per pagina (senza delay), reset per ogni offset
                            const int pageMaxAttempts = 20;
                            bool pageLoaded = false;

                            for (var attempt = 1; attempt <= pageMaxAttempts && !pageLoaded; attempt++)
                            {
                                try
                                {
                                    await _spotify.EnsureAuthenticatedAsync();
                                    var page = await _spotify.GetMyPlaylistsPageAsync(off, limit);

                                    // Log dell'output (redacted)
                                    try
                                    {
                                        if (page != null)
                                        {
                                            var p = page;
                                            var json = BuildPlaylistsPageApiOutput(p);
                                            System.Diagnostics.Debug.WriteLine($"Spotify API response (playlists page offset {off}) [redacted]:");
                                            foreach (var line in json.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
                                            {
                                                System.Diagnostics.Debug.WriteLine("  " + line);
                                            }
                                        }
                                    }
                                    catch { }

                                    if (page != null)
                                    {
                                        ValidatePlaylistsPageConsistency(page, total, off / limit);
                                    }

                                    var countToAdd = page?.Items?.Count ?? 0;
                                    if (countToAdd > 0)
                                    {
                                        var toAdd = page;
                                        var disp = Application.Current?.Dispatcher;
                                        if (disp != null)
                                        {
                                            await disp.BeginInvoke((Action)(() => { AddPlaylists(toAdd); }));
                                        }
                                        else
                                        {
                                            AddPlaylists(toAdd);
                                        }
                                    }

                                    pageLoaded = true;

                                    var uiCount = GetLoadedPlaylistsCount();
                                    if (uiCount >= total)
                                    {
                                        allLoaded = true;
                                        break;
                                    }
                                }
                                catch (APIException apiEx)
                                {
                                    System.Diagnostics.Debug.WriteLine($"Page fetch attempt {attempt} at offset {off} failed: {apiEx.Message}");
                                    if (apiEx.Message != null && apiEx.Message.IndexOf("access token expired", StringComparison.OrdinalIgnoreCase) >= 0)
                                    {
                                        var ok = await _spotify.EnsureAuthenticatedAsync();
                                        if (!ok) System.Diagnostics.Debug.WriteLine("Automatic token refresh failed.");
                                    }
                                }
                                catch (Exception ex)
                                {
                                    System.Diagnostics.Debug.WriteLine($"Page fetch attempt {attempt} at offset {off} failed: {ex.Message}");
                                    if (ex is SpotifyWPF.Service.RateLimitException rateEx)
                                    {
                                        // Show user-friendly message for rate limit
                                        int? retryAfter = null;
                                        if (rateEx.InnerException is APIException apiEx)
                                        {
                                            retryAfter = GetRetryAfterSeconds(apiEx.Response?.Headers);
                                        }
                                        var message = retryAfter.HasValue ?
                                            $"Spotify API rate limit exceeded. Please wait {retryAfter.Value} seconds before trying to load playlists again." :
                                            "Spotify API rate limit exceeded. Please wait a moment before trying to load playlists again.";
                                        await Application.Current.Dispatcher.InvokeAsync(() =>
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

                            if (!pageLoaded)
                            {
                                System.Diagnostics.Debug.WriteLine($"Skipping offset {off}: failed after 20 attempts.");
                            }

                            // Controllo completamento anche se la pagina è stata saltata
                            if (GetLoadedPlaylistsCount() >= total)
                            {
                                allLoaded = true;
                                break;
                            }
                        }
                    }));
                }

                await Task.WhenAll(workers);
                System.Diagnostics.Debug.WriteLine($"All workers completed. Cancellation requested: {cancellationToken.IsCancellationRequested}");

                var finalCount = GetLoadedPlaylistsCount();
                if (finalCount >= total)
                {
                    System.Diagnostics.Debug.WriteLine($"All playlist pages loaded. Total loaded in UI: {finalCount}");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"Completed concurrent fetchers. Loaded {finalCount}/{total} playlists.");
                }
            }
            finally
            {
                _isLoadingPlaylists = false;
                UpdateLoadingUiState();
                EndBusy();
            }
        }

        // ===== Users/Artists (followed) =====
        private string? _artistsCursorAfter;
        private CancellationTokenSource? _loadArtistsCts;

        private async Task StartLoadFollowedArtistsAsync()
        {
            if (_isLoadingArtists) return;
            _loadArtistsCts = new CancellationTokenSource();
            _isLoadingArtists = true;
            UpdateLoadingUiState();
            try
            {
                StartBusy("Loading followed artists...");
                FollowedArtists.Clear();
                await LoadFollowedArtistsAsync(_loadArtistsCts.Token);
            }
            finally
            {
                _loadArtistsCts?.Dispose();
                _loadArtistsCts = null;
                _isLoadingArtists = false;
                UpdateLoadingUiState();
                EndBusy();
            }
        }

        private async Task LoadFollowedArtistsAsync(CancellationToken cancellationToken)
        {
            const int limit = 50;
            _artistsCursorAfter = null;
            var total = 0;

            // Primo fetch (per total e primo blocco)
            try
            {
                var first = await _spotify.GetFollowedArtistsPageAsync(_artistsCursorAfter, limit);
                // Log dell'output dell'API (redacted)
                try
                {
                    if (first?.Page != null)
                    {
                        var p = first.Page;
                        var json = BuildArtistsPageApiOutput(p!);
                        System.Diagnostics.Debug.WriteLine("Spotify API response (followed artists first page) [redacted]:");
                        foreach (var line in json.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
                        {
                            System.Diagnostics.Debug.WriteLine("  " + line);
                        }
                    }
                }
                catch { /* ignore logging issues */ }
                if (first?.Page?.Items != null)
                {
                    foreach (var a in first.Page.Items) FollowedArtists.Add(a);
                    total = first.Page.Total;
                }
                _artistsCursorAfter = first?.NextAfter;
            }
            catch (RateLimitException ex)
            {
                // Show user-friendly message for rate limit
                int? retryAfter = null;
                if (ex.InnerException is APIException apiEx)
                {
                    retryAfter = GetRetryAfterSeconds(apiEx.Response?.Headers);
                }
                var message = retryAfter.HasValue ?
                    $"Spotify API rate limit exceeded. Please wait {retryAfter.Value} seconds before trying to load artists again." :
                    "Spotify API rate limit exceeded. Please wait a moment before trying to load artists again.";
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    System.Windows.MessageBox.Show(
                        message,
                        "Rate Limit Exceeded",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Warning);
                });
                return;
            }

            if (cancellationToken.IsCancellationRequested) return;
            if (total <= 0 || FollowedArtists.Count >= total) return;

            // Cache cursori
            var cursors = new System.Collections.Concurrent.ConcurrentQueue<string>();
            try
            {
                while (!string.IsNullOrWhiteSpace(_artistsCursorAfter))
                {
                    cursors.Enqueue(_artistsCursorAfter);
                    var next = await _spotify.GetFollowedArtistsPageAsync(_artistsCursorAfter, limit);
                    _artistsCursorAfter = next?.NextAfter;
                    if (cancellationToken.IsCancellationRequested) return;
                    if (string.IsNullOrWhiteSpace(_artistsCursorAfter)) break;
                }
            }
            catch (RateLimitException ex)
            {
                // Show user-friendly message for rate limit
                int? retryAfter = null;
                if (ex.InnerException is APIException apiEx)
                {
                    retryAfter = GetRetryAfterSeconds(apiEx.Response?.Headers);
                }
                var message = retryAfter.HasValue ?
                    $"Spotify API rate limit exceeded. Please wait {retryAfter.Value} seconds before trying to load artists again." :
                    "Spotify API rate limit exceeded. Please wait a moment before trying to load artists again.";
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    System.Windows.MessageBox.Show(
                        message,
                        "Rate Limit Exceeded",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Warning);
                });
                return;
            }

            var workersCount = ComputeWorkerCount(total, 2000);
            var workers = new List<Task>();
            var dispatcher = Application.Current?.Dispatcher;

            for (var w = 0; w < workersCount; w++)
            {
                workers.Add(Task.Run(async () =>
                {
                    while (!cancellationToken.IsCancellationRequested && cursors.TryDequeue(out var after))
                    {
                        try
                        {
                            var page = await _spotify.GetFollowedArtistsPageAsync(after, limit);
                            // Log dell'output (redacted)
                            try
                            {
                                if (page?.Page != null)
                                {
                                    var p = page.Page;
                                    var json = BuildArtistsPageApiOutput(p!);
                                    System.Diagnostics.Debug.WriteLine($"Spotify API response (followed artists after '{after}') [redacted]:");
                                    foreach (var line in json.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
                                    {
                                        System.Diagnostics.Debug.WriteLine("  " + line);
                                    }
                                }
                            }
                            catch { }
                            if (page?.Page?.Items != null)
                            {
                                if (dispatcher != null)
                                {
                                    await dispatcher.BeginInvoke((Action)(() =>
                                    {
                                        foreach (var a in page.Page.Items)
                                        {
                                            if (a == null) continue;
                                            FollowedArtists.Add(a);
                                        }
                                    }));
                                }
                                else
                                {
                                    foreach (var a in page.Page.Items)
                                    {
                                        if (a == null) continue;
                                        FollowedArtists.Add(a);
                                    }
                                }
                            }
                        }
                        catch (APIException apiEx)
                        {
                            System.Diagnostics.Debug.WriteLine($"Followed artists page fetch failed: {apiEx.Message}");
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"Followed artists page fetch failed: {ex.Message}");
                            if (ex is SpotifyWPF.Service.RateLimitException rateEx)
                            {
                                // Show user-friendly message for rate limit
                                int? retryAfter = null;
                                if (rateEx.InnerException is APIException apiEx)
                                {
                                    retryAfter = GetRetryAfterSeconds(apiEx.Response?.Headers);
                                }
                                var message = retryAfter.HasValue ?
                                    $"Spotify API rate limit exceeded. Please wait {retryAfter.Value} seconds before trying to load artists again." :
                                    "Spotify API rate limit exceeded. Please wait a moment before trying to load artists again.";
                                await Application.Current.Dispatcher.InvokeAsync(() =>
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
                }));
            }

            await Task.WhenAll(workers);
            Status = $"Loaded {FollowedArtists.Count} followed artist(s).";
        }

        private async Task UnfollowArtistsAsync(IList items)
        {
            if (items == null) return;
            var artists = items.Cast<ArtistDto>().ToList();
            if (!artists.Any()) return;

            var msg = artists.Count == 1
                ? $"Are you sure you want to delete artist '{artists[0].Name}'? (Unfollow)"
                : $"Are you sure you want to delete these {artists.Count} artists? (Unfollow)";
            var res = _confirmationDialogService.ShowConfirmation("Confirm", msg, "Unfollow", "Cancel");
            if (res != true) return;

            try
            {
                StartBusy($"Unfollowing {artists.Count} artist(s)...");
                await _spotify.UnfollowArtistsAsync(artists.Select(a => a.Id));
                foreach (var a in artists)
                {
                    FollowedArtists.Remove(a);
                }
                System.Diagnostics.Debug.WriteLine($"Unfollowed {artists.Count} artist(s).");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error unfollowing artists: {ex.Message}");
            }
            finally
            {
                EndBusy();
            }
        }

        private async Task<T> ExecuteWithRetryAsync<T>(Func<Task<T>> action, string operationName, int maxAttempts = 5)
        {
            for (var attempt = 1; attempt <= maxAttempts; attempt++)
            {
                try
                {
                    return await action();
                }
                catch (APIException apiEx)
                {
                    System.Diagnostics.Debug.WriteLine($"Attempt {attempt} to {operationName} failed: {apiEx.Message}");
                    if (attempt == maxAttempts) throw;

                    var delayMs = (int)(Math.Pow(2, attempt - 1) * 500); // 0.5s,1s,2s,4s...
                    await Task.Delay(delayMs);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Attempt {attempt} to {operationName} failed: {ex.Message}");
                    if (attempt == maxAttempts) throw;

                    var delayMs = (int)(Math.Pow(2, attempt - 1) * 500);
                    await Task.Delay(delayMs);
                }
            }

            // Non raggiungibile, ma richiesto dal compilatore
            return default!;
        }

        private string BuildPlaylistsPageApiOutput(PagingDto<PlaylistDto> page)
        {
            ArgumentNullException.ThrowIfNull(page);

            string? href = page.Href;
            int limit = page.Limit;
            int offset = page.Offset;
            int total = page.Total;
            string? next = page.Next;
            string? previous = page.Previous;
            int itemsCount = page.Items?.Count ?? 0;

            var jo = new JObject
            {
                ["href"] = href,
                ["limit"] = limit,
                ["offset"] = offset,
                ["total"] = total,
                ["next"] = next,
                ["previous"] = previous,
                // Non includiamo l'array items per evitare i nomi delle playlist
                ["itemsCount"] = itemsCount
            };

            return jo.ToString(Newtonsoft.Json.Formatting.Indented);
        }

        private string BuildArtistsPageApiOutput(PagingDto<ArtistDto> page)
        {
            ArgumentNullException.ThrowIfNull(page);

            string? href = page.Href;
            int limit = page.Limit;
            int offset = page.Offset;
            int total = page.Total;
            string? next = page.Next;
            string? previous = page.Previous;
            int itemsCount = page.Items?.Count ?? 0;

            var jo = new JObject
            {
                ["href"] = href,
                ["limit"] = limit,
                ["offset"] = offset,
                ["total"] = total,
                ["next"] = next,
                ["previous"] = previous,
                // Non includiamo l'array items per evitare i nomi
                ["itemsCount"] = itemsCount
            };

            return jo.ToString(Newtonsoft.Json.Formatting.Indented);
        }

        private int GetLoadedPlaylistsCount()
        {
            var count = 0;
            try
            {
                Application.Current?.Dispatcher?.Invoke((Action)(() =>
                {
                    count = Playlists.Count;
                }));
            }
            catch
            {
                // In caso di problemi con il dispatcher, restituiamo la stima basata sul set
                count = _playlistIds.Count;
            }

            return count;
        }

        private int? TryExtractRetryAfterSeconds(string message)
        {
            if (string.IsNullOrEmpty(message)) return null;

            // Cerca "Retry-After" e poi la prima sequenza di cifre (secondi)
            var idx = message.IndexOf("Retry-After", StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return null;

            // Salta fino alla prima cifra dopo "Retry-After"
            var i = idx;
            while (i < message.Length && !char.IsDigit(message[i])) i++;
            if (i >= message.Length) return null;

            var start = i;
            while (i < message.Length && char.IsDigit(message[i])) i++;

            var numberText = message.Substring(start, i - start);
            int seconds;
            if (int.TryParse(numberText, out seconds))
            {
                return seconds;
            }

            return null;
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

        // Calcolo dinamico dei worker: 1 worker ogni 'unitSize' elementi (minimo 1), rispettando il limite massimo dalle impostazioni
        private int ComputeWorkerCount(int totalCount, int unitSize)
        {
            if (totalCount <= 0) return 1;
            if (unitSize <= 0) unitSize = 1;
            
            // Get max threads from settings, default to 3 if not available
            int maxThreads = 3; // Default fallback
            try
            {
                maxThreads = (int)Properties.Settings.Default["MaxThreadsForOperations"];
                if (maxThreads < 1) maxThreads = 1;
            }
            catch
            {
                // If settings access fails, use default
                maxThreads = 3;
            }
            
            var workers = (int)Math.Ceiling(totalCount / (double)unitSize);
            return Math.Min(workers, maxThreads);
        }

        private void ValidatePlaylistsPageConsistency(PagingDto<PlaylistDto> page, int? expectedTotal, int pageIndex)
        {
            ArgumentNullException.ThrowIfNull(page);

            int items = page.Items?.Count ?? 0;
            int limit = page.Limit;
            int offset = page.Offset;
            int total = page.Total;
            string? next = page.Next;
            bool hasNext = !string.IsNullOrWhiteSpace(next);

            // Regole generali
            if (items < 0 || limit < 0 || offset < 0)
                throw new Exception($"Invalid paging values (offset={offset}, limit={limit}, items={items}).");

            if (items > limit)
                throw new Exception($"Items count {items} exceeds limit {limit} at offset {offset}.");

            if (expectedTotal.HasValue && total == 0)
                throw new Exception($"Inconsistent total=0 after expected total={expectedTotal} at offset {offset}.");

            // Se sappiamo che mancano ancora elementi, una pagina vuota è incoerente
            if (expectedTotal.HasValue && (offset + limit) < expectedTotal.Value && items == 0)
                throw new Exception($"Empty page at offset {offset} while expectedTotal={expectedTotal} indicates more items.");

            // Regole specifiche per la prima pagina
            if (pageIndex == 0)
            {
                // Caso problematico: prima pagina completamente vuota e senza next -> transiente, va ritentata
                if (total == 0 && items == 0 && !hasNext)
                    throw new Exception("First page returned empty (total=0, items=0, next=null). Treating as transient failure.");

                // Se total > 0, ci aspettiamo items > 0
                if (total > 0 && items == 0)
                    throw new Exception("First page returned total>0 but items=0.");

                // Se total > limit ma next è null, è incoerente (terminazione anticipata)
                if (total > limit && !hasNext)
                    throw new Exception($"First page indicates more data (total={total}, limit={limit}) but next=null.");
            }
        }

        private void AddPlaylists(PagingDto<PlaylistDto>? playlists)
        {
            if (playlists?.Items == null)
            {
                return;
            }

            foreach (var playlist in playlists.Items)
            {
                if (playlist == null) continue;
                if (string.IsNullOrEmpty(playlist.Id))
                {
                    // A volte item nulli/incompleti: ignora
                    continue;
                }

                if (_playlistIds.Contains(playlist.Id))
                {
                    // Già presente in UI, salta
                    continue;
                }

                Playlists.Add(playlist);
                _playlistIds.Add(playlist.Id);
                RaisePropertyChanged(nameof(PlaylistsHeader));
            }
        }

        public async Task LoadTracksAsync(PlaylistDto playlist)
        {
            if (playlist == null)
            {
                return;
            }

            CurrentPlaylist = playlist;

            StartBusy("Loading tracks...");

            Tracks.Clear();
            RaisePropertyChanged(nameof(TracksHeader));

            try
            {
                const int limit = 100;
                int offset = 0;
                int? expectedTotal = null;

                // 1) Get first page to determine total
                var firstPage = await GetPlaylistTracksAsync(playlist.Id!, offset);
                if (firstPage != null && firstPage.Total > 0)
                {
                    expectedTotal = Convert.ToInt32(firstPage.Total);
                    System.Diagnostics.Debug.WriteLine($"Detected expected total tracks: {expectedTotal.Value}");

                    // Add first page tracks
                    await Application.Current.Dispatcher.BeginInvoke((Action)(() => { AddTracks(firstPage, 0); }));
                }

                if (!expectedTotal.HasValue || expectedTotal.Value <= limit)
                {
                    // If only one page or no tracks, we're done
                    return;
                }

                // 2) Prepare offsets for remaining pages
                var nextStart = limit;
                var offsets = new System.Collections.Concurrent.ConcurrentQueue<int>();
                for (var off = nextStart; off < expectedTotal.Value; off += limit)
                {
                    offsets.Enqueue(off);
                }

                // Get max threads from settings
                int maxThreads = 3; // Default fallback
                try
                {
                    maxThreads = (int)Properties.Settings.Default["MaxThreadsForOperations"];
                    if (maxThreads < 1) maxThreads = 1;
                }
                catch
                {
                    maxThreads = 3;
                }

                var pagesRemaining = (int)Math.Ceiling((expectedTotal.Value - nextStart) / (double)limit);
                var computedWorkers = Math.Min(pagesRemaining, maxThreads);
                System.Diagnostics.Debug.WriteLine($"Starting concurrent track fetch with {computedWorkers} worker(s). Total expected={expectedTotal.Value}, page size={limit}");

                var workers = new List<Task>();
                var allLoaded = false;

                for (var w = 0; w < computedWorkers; w++)
                {
                    workers.Add(Task.Run(async () =>
                    {
                        while (!allLoaded && offsets.TryDequeue(out var off))
                        {
                            try
                            {
                                var page = await GetPlaylistTracksAsync(playlist.Id!, off);
                                if (page?.Items != null && page.Items.Count > 0)
                                {
                                    var tracksToLoad = page;
                                    var disp = Application.Current?.Dispatcher;
                                    if (disp != null)
                                    {
                                        await disp.BeginInvoke((Action)(() => { AddTracks(tracksToLoad, off); }));
                                    }
                                    else
                                    {
                                        AddTracks(tracksToLoad, off);
                                    }
                                }

                                var uiCount = Tracks.Count;
                                if (uiCount >= expectedTotal.Value)
                                {
                                    allLoaded = true;
                                    break;
                                }
                            }
                            catch (Exception ex)
                            {
                                System.Diagnostics.Debug.WriteLine($"Error loading tracks page at offset {off}: {ex.Message}");
                            }
                        }
                    }));
                }

                await Task.WhenAll(workers);
                System.Diagnostics.Debug.WriteLine($"Track loading completed. Loaded {Tracks.Count} tracks");
            }
            catch (SpotifyWPF.Service.RateLimitException ex)
            {
                // Show user-friendly message for rate limit
                int? retryAfter = null;
                if (ex.InnerException is APIException apiEx)
                {
                    retryAfter = GetRetryAfterSeconds(apiEx.Response?.Headers);
                }
                var message = retryAfter.HasValue ?
                    $"Spotify API rate limit exceeded. Please wait {retryAfter.Value} seconds before trying to load playlist tracks again." :
                    "Spotify API rate limit exceeded. Please wait a moment before trying to load playlist tracks again.";
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    System.Windows.MessageBox.Show(
                        message,
                        "Rate Limit Exceeded",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Warning);
                });
                // Clear tracks since loading failed
                Tracks.Clear();
                RaisePropertyChanged(nameof(TracksHeader));
            }
            finally
            {
                EndBusy();
            }
        }

        private async Task<Paging<PlaylistTrack<IPlayableItem>>> GetPlaylistTracksAsync(string playlistId, int offset)
        {
            var req = new PlaylistGetItemsRequest()
            {
                Offset = offset,
                Limit = 100
            };

            var ok = await _spotify.EnsureAuthenticatedAsync();
            if (!ok || _spotify.Api == null)
            {
                System.Diagnostics.Debug.WriteLine("You must be logged in to load playlist tracks.");
                // Show a user-friendly message on the UI
                try
                {
                    await Application.Current.Dispatcher.BeginInvoke((Action)(() =>
                    {
                        _messageBoxService.ShowMessageBox(
                            "You must be logged in to view playlist tracks.",
                            "Authentication Required",
                            MessageBoxButton.OK,
                            MessageBoxIcon.Warning
                        );
                    }));
                }
                catch { /* Best-effort message, don't crash the caller */ }

                // Return an empty page to avoid NullReferenceException in the caller
                var empty = new Paging<PlaylistTrack<IPlayableItem>>
                {
                    Items = new System.Collections.Generic.List<PlaylistTrack<IPlayableItem>>(),
                    Limit = 0,
                    Offset = offset,
                    Total = 0
                };
                return empty;
            }
            try
            {
                return await _spotify.Api!.Playlists.GetItems(playlistId, req);
            }
            catch (APIException apiEx)
            {
                if (apiEx.Response?.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                {
                    throw new SpotifyWPF.Service.RateLimitException($"Spotify API rate limit exceeded while getting playlist tracks. Please wait before trying again.", apiEx);
                }
                throw;
            }
        }

        private void AddTracks(IPaginatable<PlaylistTrack<IPlayableItem>> tracks, int offset)
        {
            if (tracks.Items == null)
            {
                return;
            }

            for (int i = 0; i < tracks.Items.Count; i++)
            {
                var track = tracks.Items[i];
                if (track == null) continue;
                var trackModel = _mapper.Map<TrackModel>(track);
                trackModel.Position = offset + i + 1; // 1-based position
                Tracks.Add(trackModel);
            }
        }

        private string BuildPlaylistWebUrl(PlaylistDto? playlist)
        {
            if (playlist == null) return string.Empty;
            var id = playlist.Id;
            if (string.IsNullOrWhiteSpace(id)) return string.Empty;
            return $"https://open.spotify.com/playlist/{id}";
        }

        private void TryOpenUrl(string? url)
        {
            if (string.IsNullOrWhiteSpace(url)) return;
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                };
                Process.Start(psi);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to open URL: {ex.Message}");
            }
        }

        private void TryCopyToClipboard(string? text)
        {
            if (string.IsNullOrEmpty(text)) return;
            try
            {
                Clipboard.SetText(text);
                System.Diagnostics.Debug.WriteLine("Link copied to clipboard.");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to copy to clipboard: {ex.Message}");
            }
        }

        private string BuildArtistWebUrl(ArtistDto? artist)
        {
            if (artist == null) return string.Empty;
            var id = artist.Id;
            if (string.IsNullOrWhiteSpace(id)) return string.Empty;
            return $"https://open.spotify.com/artist/{id}";
        }

        public ObservableCollection<TrackModel> SelectedTracks { get; } = new ObservableCollection<TrackModel>();
    }
#pragma warning restore CS8602 // Dereference of a possibly null reference.
}
