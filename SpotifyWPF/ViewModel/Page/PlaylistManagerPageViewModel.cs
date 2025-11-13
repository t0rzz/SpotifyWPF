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
// ReSharper disable AsyncVoidLambda

namespace SpotifyWPF.ViewModel.Page
{
    public class PlaylistManagerPageViewModel : ViewModelBase
    {
        private readonly IMapper _mapper;
        private readonly IMessageBoxService _messageBoxService;
        private readonly IConfirmationDialogService _confirmationDialogService;
        private readonly IEditPlaylistDialogService _editPlaylistDialogService;
        private readonly ISpotify _spotify;
        private readonly ISettingsProvider _settingsProvider;

        private Visibility _progressVisibility = Visibility.Hidden;
        private string _status = "Ready";

        private bool _isLoadingPlaylists;
        private bool _isSearching;
        private bool _isLoadingTracks;
        private bool _isGenerating;

        // Playlist Manager properties
        private string _newPlaylistName = string.Empty;
        private string _newPlaylistDescription = string.Empty;
        private bool _newPlaylistIsPublic = true;
        private bool _newPlaylistIsCollaborative = false;
        private string _selectedImagePath = string.Empty;
        private PlaylistDto? _selectedPlaylist;
        private ObservableCollection<SearchResultDto> _availableTracks = new ObservableCollection<SearchResultDto>();
        private ObservableCollection<SearchResultDto> _selectedTracks = new ObservableCollection<SearchResultDto>();

        // Followed playlists collection
        private ObservableCollection<PlaylistDto> _followedPlaylists = new();
        private PlaylistDto? _editingPlaylist;
        private ObservableCollection<TrackDto> _currentPlaylistTracks = new();

        // Viewing state
        private GeneratedPlaylistDto? _viewingGeneratedPlaylist;

        // Playlist Generator properties
        private ObservableCollection<GeneratedPlaylistDto> _generatedPlaylists = new ObservableCollection<GeneratedPlaylistDto>();
        private GeneratedPlaylistDto? _selectedGeneratedPlaylist;
        private int _selectedSongCount = 20;
        private string _generatorGenre = string.Empty;

        // Search properties
        private string _searchQuery = string.Empty;
        private bool _searchTrack = true; // Default to track search
        private bool _searchArtist = false;
        private bool _searchAlbum = false;
        private bool _searchPlaylist = false;
        private bool _searchShow = false;
        private bool _searchEpisode = false;
        private bool _searchAudiobook = false;

        public PlaylistManagerPageViewModel(ISpotify spotify, IMapper mapper, IMessageBoxService messageBoxService, IConfirmationDialogService confirmationDialogService, IEditPlaylistDialogService editPlaylistDialogService, ISettingsProvider settingsProvider)
        {
            _spotify = spotify;
            _mapper = mapper;
            _messageBoxService = messageBoxService;
            _confirmationDialogService = confirmationDialogService;
            _editPlaylistDialogService = editPlaylistDialogService;
            _settingsProvider = settingsProvider;

            System.Diagnostics.Debug.WriteLine("PlaylistManagerPageViewModel constructor called - ViewModel initialized successfully");

            // Set up collection change notifications for count properties
            _availableTracks.CollectionChanged += (s, e) => RaisePropertyChanged(nameof(AvailableTracksCount));
            _selectedTracks.CollectionChanged += (s, e) => RaisePropertyChanged(nameof(SelectedTracksCount));
            UserPlaylists.CollectionChanged += (s, e) => RaisePropertyChanged(nameof(UserPlaylistsCount));
            _currentPlaylistTracks.CollectionChanged += (s, e) => RaisePropertyChanged(nameof(PlaylistTracksHeader));
            _generatedPlaylists.CollectionChanged += (s, e) => RaisePropertyChanged(nameof(HasGeneratedPlaylists));
            _generatedPlaylists.CollectionChanged += (s, e) => RaisePropertyChanged(nameof(GeneratedPlaylistsCount));

            // Initialize commands
            CreatePlaylistCommand = new RelayCommand(async () => await CreatePlaylistAsync(), CanCreatePlaylist);
            SelectImageCommand = new RelayCommand(SelectImage);
            LoadAvailableTracksCommand = new RelayCommand(async () => await LoadAvailableTracksAsync());
            SearchTracksCommand = new RelayCommand(async () => await SearchTracksAsync(), CanSearch);
            AddTrackToSelectionCommand = new RelayCommand<SearchResultDto>(AddTrackToSelection, CanAddTrack);
            RemoveTrackFromSelectionCommand = new RelayCommand<SearchResultDto>(RemoveTrackFromSelection);
            AddSelectedTracksToPlaylistCommand = new RelayCommand(async () => await AddSelectedTracksToPlaylistAsync(), CanAddSelectedTracks);
            LoadUserPlaylistsCommand = new RelayCommand(async () => await LoadUserPlaylistsAsync());
            EditPlaylistCommand = new RelayCommand<PlaylistDto>(EditPlaylist, CanEditPlaylist);
            SavePlaylistChangesCommand = new RelayCommand(SavePlaylistChanges, CanSavePlaylistChanges);
            DeletePlaylistCommand = new RelayCommand<PlaylistDto>(DeletePlaylist);
            FollowPlaylistCommand = new RelayCommand<PlaylistDto>(FollowPlaylist);
            UnfollowPlaylistCommand = new RelayCommand<PlaylistDto>(UnfollowPlaylist);
            SelectPlaylistCommand = new RelayCommand<PlaylistDto>(SelectPlaylist, CanSelectPlaylist);
            RemoveTrackFromPlaylistCommand = new RelayCommand<TrackDto>(RemoveTrackFromPlaylist, CanRemoveTrack);
            MoveTrackUpCommand = new RelayCommand<TrackDto>(MoveTrackUp, CanMoveTrackUp);
            MoveTrackDownCommand = new RelayCommand<TrackDto>(MoveTrackDown, CanMoveTrackDown);
            LoadArtistTracksCommand = new RelayCommand<SearchResultDto>(LoadArtistTracks, CanLoadArtistTracks);

            // Playlist Generator commands
            GenerateRandomPlaylistCommand = new RelayCommand(async () => await GenerateRandomPlaylistAsync());
            SaveGeneratedPlaylistCommand = new RelayCommand<GeneratedPlaylistDto>(SaveGeneratedPlaylist);
            ViewGeneratedPlaylistCommand = new RelayCommand<GeneratedPlaylistDto>(ViewGeneratedPlaylist);
            RemoveTrackFromGeneratedPlaylistCommand = new RelayCommand<TrackDto>(RemoveTrackFromGeneratedPlaylist);
            DeleteGeneratedPlaylistCommand = new RelayCommand<GeneratedPlaylistDto>(DeleteGeneratedPlaylist);
        }

        // Properties
        public string NewPlaylistName
        {
            get => _newPlaylistName;
            set
            {
                if (_newPlaylistName != value)
                {
                    _newPlaylistName = value;
                    RaisePropertyChanged();
                    CreatePlaylistCommand?.RaiseCanExecuteChanged();
                }
            }
        }

        public string NewPlaylistDescription
        {
            get => _newPlaylistDescription;
            set
            {
                if (_newPlaylistDescription != value)
                {
                    _newPlaylistDescription = value;
                    RaisePropertyChanged();
                }
            }
        }

        public bool NewPlaylistIsPublic
        {
            get => _newPlaylistIsPublic;
            set
            {
                if (_newPlaylistIsPublic != value)
                {
                    _newPlaylistIsPublic = value;
                    RaisePropertyChanged();
                }
            }
        }

        public bool NewPlaylistIsCollaborative
        {
            get => _newPlaylistIsCollaborative;
            set
            {
                if (_newPlaylistIsCollaborative != value)
                {
                    _newPlaylistIsCollaborative = value;
                    RaisePropertyChanged();
                }
            }
        }

        public string SelectedImagePath
        {
            get => _selectedImagePath;
            set
            {
                if (_selectedImagePath != value)
                {
                    _selectedImagePath = value;
                    RaisePropertyChanged();
                }
            }
        }

        public PlaylistDto? SelectedPlaylist
        {
            get => _selectedPlaylist;
            set
            {
                if (_selectedPlaylist != value)
                {
                    _selectedPlaylist = value;
                    RaisePropertyChanged();
                    AddSelectedTracksToPlaylistCommand.RaiseCanExecuteChanged();
                    _ = UpdateTracksInSelectedPlaylistAsync().ContinueWith(t =>
                    {
                        if (t.IsFaulted)
                        {
                            System.Diagnostics.Debug.WriteLine($"Error updating tracks in selected playlist: {t.Exception?.GetBaseException()?.Message}");
                        }
                    }, TaskContinuationOptions.OnlyOnFaulted);
                }
            }
        }

        public ObservableCollection<SearchResultDto> AvailableTracks => _availableTracks;
        public ObservableCollection<SearchResultDto> SelectedTracks => _selectedTracks;
        public ObservableCollection<PlaylistDto> UserPlaylists { get; } = new ObservableCollection<PlaylistDto>();
        public ObservableCollection<TrackDto> CurrentPlaylistTracks => _currentPlaylistTracks;

        // Playlist Generator properties
        public ObservableCollection<GeneratedPlaylistDto> GeneratedPlaylists => _generatedPlaylists;
        public GeneratedPlaylistDto? SelectedGeneratedPlaylist
        {
            get => _selectedGeneratedPlaylist;
            set
            {
                if (_selectedGeneratedPlaylist != value)
                {
                    _selectedGeneratedPlaylist = value;
                    RaisePropertyChanged();
                    RaisePropertyChanged(nameof(IsGeneratedPlaylistPreviewVisible));
                }
            }
        }
        public List<int> SongCountOptions { get; } = new List<int> { 10, 20, 30, 50, 100 };
        public int SelectedSongCount
        {
            get => _selectedSongCount;
            set
            {
                if (_selectedSongCount != value)
                {
                    _selectedSongCount = value;
                    RaisePropertyChanged();
                }
            }
        }
        public string GeneratorGenre
        {
            get => _generatorGenre;
            set
            {
                if (_generatorGenre != value)
                {
                    _generatorGenre = value;
                    RaisePropertyChanged();
                }
            }
        }
        public bool HasGeneratedPlaylists => _generatedPlaylists.Count > 0;
        public bool IsGeneratedPlaylistPreviewVisible => _selectedGeneratedPlaylist != null;

        // Count of available tracks for display
        public int AvailableTracksCount => _availableTracks.Count;

        // Count properties for other collections
        public int SelectedTracksCount => _selectedTracks.Count;
        public int UserPlaylistsCount => UserPlaylists.Count;
        public int CurrentPlaylistTracksCount => _currentPlaylistTracks.Count;
        public int GeneratedPlaylistsCount => _generatedPlaylists.Count;

        // Selected item for context menu
        private SearchResultDto? _selectedAvailableTrack;
        public SearchResultDto? SelectedAvailableTrack
        {
            get => _selectedAvailableTrack;
            set
            {
                if (_selectedAvailableTrack != value)
                {
                    _selectedAvailableTrack = value;
                    System.Diagnostics.Debug.WriteLine($"SelectedAvailableTrack set to: {_selectedAvailableTrack?.Name} (Type: {_selectedAvailableTrack?.Type})");
                    RaisePropertyChanged();
                }
            }
        }

        // Dynamic header for playlist tracks section
        public string PlaylistTracksHeader
        {
            get
            {
                if (_viewingGeneratedPlaylist != null)
                {
                    return $"Generated Playlist: {_viewingGeneratedPlaylist.Name} ({_currentPlaylistTracks.Count})";
                }
                return _currentPlaylistTracks.Count > 0 
                    ? $"Tracks in Playlist ({_currentPlaylistTracks.Count})" 
                    : "Tracks in Playlist";
            }
        }

        public PlaylistDto? EditingPlaylist
        {
            get => _editingPlaylist;
            set
            {
                if (_editingPlaylist != value)
                {
                    _editingPlaylist = value;
                    RaisePropertyChanged();
                }
            }
        }

        // Search properties
        public string SearchQuery
        {
            get => _searchQuery;
            set
            {
                if (_searchQuery != value)
                {
                    _searchQuery = value;
                    RaisePropertyChanged();
                    SearchTracksCommand?.RaiseCanExecuteChanged();
                }
            }
        }

        public bool SearchTrack
        {
            get => _searchTrack;
            set
            {
                if (_searchTrack != value)
                {
                    _searchTrack = value;
                    RaisePropertyChanged();
                    SearchTracksCommand?.RaiseCanExecuteChanged();
                }
            }
        }

        public bool SearchArtist
        {
            get => _searchArtist;
            set
            {
                if (_searchArtist != value)
                {
                    _searchArtist = value;
                    RaisePropertyChanged();
                    SearchTracksCommand?.RaiseCanExecuteChanged();
                }
            }
        }

        public bool SearchAlbum
        {
            get => _searchAlbum;
            set
            {
                if (_searchAlbum != value)
                {
                    _searchAlbum = value;
                    RaisePropertyChanged();
                    SearchTracksCommand?.RaiseCanExecuteChanged();
                }
            }
        }

        public bool SearchPlaylist
        {
            get => _searchPlaylist;
            set
            {
                if (_searchPlaylist != value)
                {
                    _searchPlaylist = value;
                    RaisePropertyChanged();
                    SearchTracksCommand?.RaiseCanExecuteChanged();
                }
            }
        }

        public bool SearchShow
        {
            get => _searchShow;
            set
            {
                if (_searchShow != value)
                {
                    _searchShow = value;
                    RaisePropertyChanged();
                    SearchTracksCommand?.RaiseCanExecuteChanged();
                }
            }
        }

        public bool SearchEpisode
        {
            get => _searchEpisode;
            set
            {
                if (_searchEpisode != value)
                {
                    _searchEpisode = value;
                    RaisePropertyChanged();
                    SearchTracksCommand?.RaiseCanExecuteChanged();
                }
            }
        }

        public bool SearchAudiobook
        {
            get => _searchAudiobook;
            set
            {
                if (_searchAudiobook != value)
                {
                    _searchAudiobook = value;
                    RaisePropertyChanged();
                    SearchTracksCommand?.RaiseCanExecuteChanged();
                }
            }
        }

        // Commands
        public RelayCommand CreatePlaylistCommand { get; }
        public RelayCommand SelectImageCommand { get; }
        public RelayCommand LoadAvailableTracksCommand { get; }
        public RelayCommand SearchTracksCommand { get; }
        public RelayCommand<SearchResultDto> AddTrackToSelectionCommand { get; }
        public RelayCommand<SearchResultDto> RemoveTrackFromSelectionCommand { get; }
        public RelayCommand AddSelectedTracksToPlaylistCommand { get; }
        public RelayCommand LoadUserPlaylistsCommand { get; }
        public RelayCommand<PlaylistDto> EditPlaylistCommand { get; }
        public RelayCommand SavePlaylistChangesCommand { get; }
        public RelayCommand<PlaylistDto> DeletePlaylistCommand { get; }
        public RelayCommand<PlaylistDto> FollowPlaylistCommand { get; }
        public RelayCommand<PlaylistDto> UnfollowPlaylistCommand { get; }
        public RelayCommand<TrackDto> RemoveTrackFromPlaylistCommand { get; }
        public RelayCommand<TrackDto> MoveTrackUpCommand { get; }
        public RelayCommand<TrackDto> MoveTrackDownCommand { get; }
        public RelayCommand<SearchResultDto> LoadArtistTracksCommand { get; }

        private bool CanLoadArtistTracks(SearchResultDto searchResult)
        {
            bool canExecute = searchResult != null && searchResult.Type == "artist" && !string.IsNullOrEmpty(searchResult.Id);
            System.Diagnostics.Debug.WriteLine($"CanLoadArtistTracks: {canExecute} (searchResult: {searchResult?.Name}, Type: {searchResult?.Type})");
            return canExecute;
        }

        private bool CanSelectPlaylist(PlaylistDto playlist)
        {
            return playlist != null;
        }

        private bool CanEditPlaylist(PlaylistDto playlist)
        {
            return playlist != null;
        }

        public RelayCommand<PlaylistDto> SelectPlaylistCommand { get; }

        // Playlist Generator commands
        public RelayCommand GenerateRandomPlaylistCommand { get; }
        public RelayCommand<GeneratedPlaylistDto> SaveGeneratedPlaylistCommand { get; }
        public RelayCommand<GeneratedPlaylistDto> ViewGeneratedPlaylistCommand { get; }
        public RelayCommand<TrackDto> RemoveTrackFromGeneratedPlaylistCommand { get; }
        public RelayCommand<GeneratedPlaylistDto> DeleteGeneratedPlaylistCommand { get; }

        // UI Properties
        public Visibility ProgressVisibility
        {
            get => _progressVisibility;
            set
            {
                if (_progressVisibility != value)
                {
                    _progressVisibility = value;
                    RaisePropertyChanged();
                }
            }
        }

        public bool IsSearching
        {
            get => _isSearching;
            set
            {
                if (_isSearching != value)
                {
                    _isSearching = value;
                    RaisePropertyChanged();
                }
            }
        }

        public bool IsLoadingTracks
        {
            get => _isLoadingTracks;
            set
            {
                if (_isLoadingTracks != value)
                {
                    _isLoadingTracks = value;
                    System.Diagnostics.Debug.WriteLine($"IsLoadingTracks changed to: {_isLoadingTracks}");
                    RaisePropertyChanged();
                }
            }
        }

        public bool IsGenerating
        {
            get => _isGenerating;
            set
            {
                if (_isGenerating != value)
                {
                    _isGenerating = value;
                    RaisePropertyChanged();
                }
            }
        }

        public string Status
        {
            get => _status;
            set
            {
                if (_status != value)
                {
                    _status = value;
                    RaisePropertyChanged();
                }
            }
        }

        // Methods
        private bool CanCreatePlaylist()
        {
            return !string.IsNullOrWhiteSpace(_newPlaylistName) && !_isLoadingPlaylists;
        }

        private void SelectImage()
        {
            var openFileDialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Select Playlist Image",
                Filter = "Image files (*.jpg, *.jpeg, *.png)|*.jpg;*.jpeg;*.png",
                Multiselect = false
            };

            if (openFileDialog.ShowDialog() == true)
            {
                SelectedImagePath = openFileDialog.FileName;
            }
        }

        private async Task CreatePlaylistAsync()
        {
            if (string.IsNullOrWhiteSpace(_newPlaylistName))
                return;

            System.Diagnostics.Debug.WriteLine($"CreatePlaylistAsync called with name: '{_newPlaylistName}'");

            try
            {
                StartBusy($"Creating playlist '{_newPlaylistName}'...");

                var playlist = await _spotify.CreatePlaylistAsync(
                    _newPlaylistName,
                    _newPlaylistDescription,
                    _newPlaylistIsPublic,
                    _newPlaylistIsCollaborative);

                // Upload image if selected
                if (!string.IsNullOrWhiteSpace(_selectedImagePath))
                {
                    try
                    {
                        // Wait a bit for playlist to be fully created
                        await Task.Delay(1000);
                        System.Diagnostics.Debug.WriteLine($"Uploading image for playlist {playlist.Id}");
                        var imageData = ConvertImageToBytes(_selectedImagePath);
                        System.Diagnostics.Debug.WriteLine($"Image converted to JPEG, size: {imageData.Length} bytes");
                        var base64Image = Convert.ToBase64String(imageData);
                        System.Diagnostics.Debug.WriteLine($"Base64 length: {base64Image.Length} characters");
                        await _spotify.UploadPlaylistImageAsync(playlist.Id, base64Image);
                        System.Diagnostics.Debug.WriteLine("Image uploaded successfully");
                        Status = $"Playlist '{_newPlaylistName}' created with custom image!";
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Failed to upload playlist image: {ex.Message}");
                        Status = $"Playlist '{_newPlaylistName}' created (image upload failed)";
                    }
                }
                else
                {
                    Status = $"Playlist '{_newPlaylistName}' created successfully!";
                }

                // Add to user playlists
                UserPlaylists.Insert(0, playlist);
                SelectedPlaylist = playlist;

                // Reset form
                NewPlaylistName = string.Empty;
                NewPlaylistDescription = string.Empty;
                SelectedImagePath = string.Empty;
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("Not authenticated"))
            {
                // Authentication required - navigate to login page
                System.Diagnostics.Debug.WriteLine("Authentication required - navigating to login page");
                MessengerInstance.Send(new object(), MessageType.AuthenticationRequired);
                Status = "Please log in to continue";
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error creating playlist: {ex.Message}");
                Status = $"Failed to create playlist: {ex.Message}";
            }
            finally
            {
                EndBusy();
            }
        }

        private async Task LoadAvailableTracksAsync()
        {
            try
            {
                StartBusy("Loading available tracks...", LoadingType.LoadTracks);

                // Load user's top tracks
                var topTracks = await _spotify.GetUserTopTracksAsync(50);
                var savedTracks = topTracks; // For now, just use top tracks

                _availableTracks.Clear();

                // Add top tracks
                if (topTracks.Items != null)
                {
                    foreach (var track in topTracks.Items.OrderBy(t => t.Name))
                    {
                        var searchResult = new SearchResultDto
                        {
                            Id = track.Id,
                            Name = track.Name,
                            Type = "track",
                            Description = $"{track.Artists} - {track.AlbumName}",
                            ImageUrl = track.AlbumImageUrl,
                            Href = track.Href,
                            Uri = track.Uri,
                            CanAddToPlaylist = true,
                            IsInSelectedPlaylist = false, // Will be updated later
                            OriginalDto = track
                        };
                        _availableTracks.Add(searchResult);
                    }
                }

                // Add saved tracks (avoid duplicates) - using top tracks for now
                if (savedTracks.Items != null)
                {
                    foreach (var track in savedTracks.Items.OrderBy(t => t.Name))
                    {
                        if (!_availableTracks.Any(t => t.Id == track.Id))
                        {
                            var searchResult = new SearchResultDto
                            {
                                Id = track.Id,
                                Name = track.Name,
                                Type = "track",
                                Description = $"{track.Artists} - {track.AlbumName}",
                                ImageUrl = track.AlbumImageUrl,
                                Href = track.Href,
                                Uri = track.Uri,
                                CanAddToPlaylist = true,
                                IsInSelectedPlaylist = false, // Will be updated later
                                OriginalDto = track
                            };
                            _availableTracks.Add(searchResult);
                        }
                    }
                }

                Status = $"Loaded {_availableTracks.Count} available tracks";
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("Not authenticated"))
            {
                // Authentication required - navigate to login page
                System.Diagnostics.Debug.WriteLine("Authentication required - navigating to login page");
                MessengerInstance.Send(new object(), MessageType.AuthenticationRequired);
                Status = "Please log in to continue";
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading available tracks: {ex.Message}");
                Status = $"Failed to load tracks: {ex.Message}";
            }
            finally
            {
                EndBusy(LoadingType.LoadTracks);
            }
        }

        private bool CanSearch()
        {
            return !string.IsNullOrWhiteSpace(_searchQuery) &&
                   (_searchTrack || _searchArtist || _searchAlbum || _searchPlaylist || _searchShow || _searchEpisode || _searchAudiobook);
        }

        private async Task SearchTracksAsync()
        {
            if (string.IsNullOrWhiteSpace(_searchQuery))
                return;

            // Build list of selected search types
            var selectedTypes = new List<string>();
            if (_searchTrack) selectedTypes.Add("track");
            if (_searchArtist) selectedTypes.Add("artist");
            if (_searchAlbum) selectedTypes.Add("album");
            if (_searchPlaylist) selectedTypes.Add("playlist");
            if (_searchShow) selectedTypes.Add("show");
            if (_searchEpisode) selectedTypes.Add("episode");
            if (_searchAudiobook) selectedTypes.Add("audiobook");

            if (selectedTypes.Count == 0)
            {
                Status = "Please select at least one search type";
                return;
            }

            var typesString = string.Join(", ", selectedTypes);
            System.Diagnostics.Debug.WriteLine($"SearchTracksAsync called with query: '{_searchQuery}', types: '{typesString}'");

            try
            {
                // Load up to 1000 results (Spotify API offset limit) by iterating through pages
                const int maxResults = 1000; // Spotify API limit: maximum offset is 1000
                const int pageSize = 50;

                StartBusy($"Searching for {typesString} (loading up to {maxResults} results)...", LoadingType.Search);

                _availableTracks.Clear();

                int totalLoaded = 0;

                // Use the multi-type search API
                totalLoaded = await LoadAllItemsResults(_searchQuery, selectedTypes, maxResults, pageSize);

                // Update track status for selected playlist
                if (_selectedPlaylist != null)
                {
                    await UpdateTracksInSelectedPlaylistAsync();
                }

                var typeDescription = selectedTypes.Count == 1 ? $"{selectedTypes[0]}s" : "items";
                Status = $"Found {totalLoaded} {typeDescription} matching '{_searchQuery}'";
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("Not authenticated"))
            {
                // Authentication required - navigate to login page
                System.Diagnostics.Debug.WriteLine("Authentication required - navigating to login page");
                MessengerInstance.Send(new object(), MessageType.AuthenticationRequired);
                Status = "Please log in to continue";
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error searching: {ex.Message}");
                Status = $"Failed to search: {ex.Message}";
            }
            finally
            {
                EndBusy(LoadingType.Search);
            }
        }



        private async Task<int> LoadAllItemsResults(string query, List<string> types, int maxResults, int pageSize)
        {
            int totalLoaded = 0;
            int offset = 0;
            const int maxOffset = 1000; // Spotify API limit: maximum offset is 1000

            while (totalLoaded < maxResults && offset <= maxOffset)
            {
                // Update progress status
                var typesString = string.Join(", ", types);
                Status = $"Loading {typesString}... ({totalLoaded}/{maxResults})";

                var trackResults = await _spotify.SearchItemsPageAsync(query, types, offset, pageSize);

                if (trackResults?.Items == null || trackResults.Items.Count == 0)
                {
                    // No more results
                    break;
                }

                foreach (var item in trackResults.Items)
                {
                    if (totalLoaded >= maxResults)
                        break;

                    _availableTracks.Add(item);
                    totalLoaded++;
                }

                // Check if there are more pages
                if (string.IsNullOrEmpty(trackResults.Next) || totalLoaded >= maxResults || offset >= maxOffset)
                {
                    break;
                }

                offset += pageSize;

                // Small delay to avoid overwhelming the API
                await Task.Delay(100);
            }

            return totalLoaded;
        }

        private async Task LoadUserPlaylistsAsync()
        {
            try
            {
                StartBusy("Loading your playlists...", LoadingType.Playlists);

                // Get current user ID to filter only owned playlists
                var currentUser = await _spotify.GetPrivateProfileAsync();
                if (currentUser == null)
                {
                    Status = "Failed to get user profile";
                    return;
                }

                // Load ALL playlists first, then filter by ownership
                var allPlaylists = new List<PlaylistDto>();
                const int limit = 50;
                int offset = 0;
                int totalPlaylists = 0;

                // First, get the total count
                var firstPage = await _spotify.GetMyPlaylistsPageAsync(0, 1);
                totalPlaylists = firstPage.Total;

                if (totalPlaylists == 0)
                {
                    UserPlaylists.Clear();
                    Status = "Loaded 0 owned playlists";
                    return;
                }

        // Load all pages
        while (offset < totalPlaylists)
        {
            var page = await _spotify.GetMyPlaylistsPageAsync(offset, limit);
            if (page.Items != null)
            {
                allPlaylists.AddRange(page.Items);
                System.Diagnostics.Debug.WriteLine($"Loaded page with offset {offset}, got {page.Items.Count} playlists. Total so far: {allPlaylists.Count}");
            }

            offset += limit;

            // Safety check to avoid infinite loops
            if (page.Items == null || page.Items.Count == 0)
                break;
        }

        // Check for duplicates in allPlaylists
        var duplicateIds = allPlaylists.GroupBy(p => p.Id).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (duplicateIds.Any())
        {
            System.Diagnostics.Debug.WriteLine($"WARNING: Found duplicate playlist IDs in API response: {string.Join(", ", duplicateIds)}");
            foreach (var dupId in duplicateIds)
            {
                var duplicates = allPlaylists.Where(p => p.Id == dupId).ToList();
                System.Diagnostics.Debug.WriteLine($"Duplicate ID {dupId}: {string.Join(", ", duplicates.Select(p => p.Name))}");
            }
        }

        // Now include all playlists (owned and followed) - the Spotify API already distinguishes them by owner ID
        // No additional API calls needed since /me/playlists returns both owned and followed playlists
        System.Diagnostics.Debug.WriteLine($"Before filtering: UserPlaylists has {UserPlaylists.Count} items");
        UserPlaylists.Clear();
        System.Diagnostics.Debug.WriteLine($"After clearing: UserPlaylists has {UserPlaylists.Count} items");

        foreach (var playlist in allPlaylists)
        {
            // Include both owned and followed playlists - ownership is determined by playlist.OwnerId vs currentUser.Id
            // Check if we already have this playlist
            if (UserPlaylists.Any(p => p.Id == playlist.Id))
            {
                System.Diagnostics.Debug.WriteLine($"WARNING: Duplicate playlist found when filtering: {playlist.Name} (ID: {playlist.Id})");
                continue; // Skip duplicates
            }

            // Set ownership flag for UI display
            playlist.IsOwned = playlist.OwnerId == currentUser.Id;

            UserPlaylists.Add(playlist);
            System.Diagnostics.Debug.WriteLine($"Added playlist: {playlist.Name} (ID: {playlist.Id}, Owner: {playlist.OwnerId}, IsOwned: {playlist.IsOwned})");
        }

        System.Diagnostics.Debug.WriteLine($"Final UserPlaylists count: {UserPlaylists.Count}");
        var finalDuplicateIds = UserPlaylists.GroupBy(p => p.Id).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (finalDuplicateIds.Any())
        {
            System.Diagnostics.Debug.WriteLine($"CRITICAL: Final UserPlaylists still has duplicates: {string.Join(", ", finalDuplicateIds)}");
        }
        Status = $"Loaded {UserPlaylists.Count} playlists";
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("Not authenticated"))
            {
                // Authentication required - navigate to login page
                System.Diagnostics.Debug.WriteLine("Authentication required - navigating to login page");
                MessengerInstance.Send(new object(), MessageType.AuthenticationRequired);
                Status = "Please log in to continue";
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading user playlists: {ex.Message}");
                Status = $"Failed to load playlists: {ex.Message}";
            }
            finally
            {
                EndBusy(LoadingType.Playlists);
            }
        }

        private bool CanAddTrack(SearchResultDto track)
        {
            return track != null && !_selectedTracks.Contains(track) && !track.IsInSelectedPlaylist && track.CanAddToPlaylist;
        }

        private void AddTrackToSelection(SearchResultDto track)
        {
            if (track != null && !_selectedTracks.Contains(track))
            {
                _selectedTracks.Add(track);
            }
        }

        private void RemoveTrackFromSelection(SearchResultDto track)
        {
            if (track != null)
            {
                _selectedTracks.Remove(track);
            }
        }

        private bool CanAddSelectedTracks()
        {
            return _selectedPlaylist != null && _selectedTracks.Count > 0;
        }

        private async Task AddSelectedTracksToPlaylistAsync()
        {
            if (_selectedPlaylist == null || _selectedTracks.Count == 0)
                return;

            try
            {
                StartBusy($"Adding {_selectedTracks.Count} tracks to playlist...");

                var trackUris = _selectedTracks.Where(t => !string.IsNullOrEmpty(t.Uri)).Select(t => t.Uri!).ToList();
                await _spotify.AddTracksToPlaylistAsync(_selectedPlaylist.Id, trackUris);

                Status = $"Added {_selectedTracks.Count} tracks to '{_selectedPlaylist.Name}'";

                // Clear selection
                _selectedTracks.Clear();

                // Refresh playlist info
                await LoadUserPlaylistsAsync();

                // Update track status in selected playlist
                await UpdateTracksInSelectedPlaylistAsync();
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("Not authenticated"))
            {
                // Authentication required - navigate to login page
                System.Diagnostics.Debug.WriteLine("Authentication required - navigating to login page");
                MessengerInstance.Send(new object(), MessageType.AuthenticationRequired);
                Status = "Please log in to continue";
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error adding tracks to playlist: {ex.Message}");
                Status = $"Failed to add tracks: {ex.Message}";
            }
            finally
            {
                EndBusy();
            }
        }

        private void EditPlaylist(PlaylistDto playlist)
        {
            if (playlist == null) return;

            var (result, newName, newIsPublic) = _editPlaylistDialogService.ShowEditPlaylistDialog(playlist.Name ?? string.Empty, playlist.IsPublic ?? false);

            if (result == true)
            {
                // Set the editing playlist for SavePlaylistChanges to work with
                EditingPlaylist = playlist;
                // Update the playlist properties
                playlist.Name = newName;
                playlist.IsPublic = newIsPublic;

                // Save the changes
                SavePlaylistChanges();
            }
        }

        private bool CanSavePlaylistChanges()
        {
            return EditingPlaylist != null && !string.IsNullOrWhiteSpace(EditingPlaylist.Name);
        }

        private async void SavePlaylistChanges()
        {
            if (EditingPlaylist == null) return;

            try
            {
                StartBusy("Saving playlist changes...");

                await _spotify.UpdatePlaylistAsync(
                    EditingPlaylist.Id,
                    EditingPlaylist.Name ?? string.Empty,
                    EditingPlaylist.IsPublic ?? false);

                // Update the playlist in the collection
                var existingPlaylist = UserPlaylists.FirstOrDefault(p => p.Id == EditingPlaylist.Id);
                if (existingPlaylist != null)
                {
                    existingPlaylist.Name = EditingPlaylist.Name;
                    existingPlaylist.IsPublic = EditingPlaylist.IsPublic;
                }

                Status = "Playlist updated successfully";
                EditingPlaylist = null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error updating playlist: {ex.Message}");
                Status = $"Failed to update playlist: {ex.Message}";
            }
            finally
            {
                EndBusy();
            }
        }

        private async void DeletePlaylist(PlaylistDto playlist)
        {
            if (playlist == null) return;

            var result = _confirmationDialogService.ShowConfirmation(
                "Delete Playlist",
                $"Are you sure you want to delete the playlist '{playlist.Name}'?",
                "Delete",
                "Cancel");

            if (result != true) return;

            try
            {
                StartBusy("Deleting playlist...");

                await _spotify.DeletePlaylistAsync(playlist.Id);
                UserPlaylists.Remove(playlist);

                Status = "Playlist deleted successfully";
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error deleting playlist: {ex.Message}");
                Status = $"Failed to delete playlist: {ex.Message}";
            }
            finally
            {
                EndBusy();
            }
        }

        private void FollowPlaylist(PlaylistDto playlist)
        {
            if (playlist == null) return;

            try
            {
                StartBusy("Following playlist...");

                // Note: The ISpotify interface doesn't have a FollowPlaylistAsync method
                // This would need to be added to the service
                Status = "Follow playlist functionality not yet implemented in service";
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error following playlist: {ex.Message}");
                Status = $"Failed to follow playlist: {ex.Message}";
            }
            finally
            {
                EndBusy();
            }
        }

        private async void UnfollowPlaylist(PlaylistDto playlist)
        {
            if (playlist == null) return;

            try
            {
                StartBusy("Unfollowing playlist...");

                await _spotify.UnfollowPlaylistAsync(playlist.Id);

                Status = "Playlist unfollowed successfully";
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error unfollowing playlist: {ex.Message}");
                Status = $"Failed to unfollow playlist: {ex.Message}";
            }
            finally
            {
                EndBusy();
            }
        }

        private void SelectPlaylist(PlaylistDto playlist)
        {
            _selectedPlaylist = playlist;
            _viewingGeneratedPlaylist = null; // Clear generated playlist viewing state
            RaisePropertyChanged(nameof(SelectedPlaylist));
            _ = Task.Run(async () =>
            {
                try
                {
                    await LoadCurrentPlaylistTracksAsync();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error loading current playlist tracks: {ex.Message}");
                }
            });
        }

        private async Task LoadCurrentPlaylistTracksAsync()
        {
            if (_selectedPlaylist == null) return;

            try
            {
                StartBusy("Loading current playlist tracks...", LoadingType.LoadTracks);

                _currentPlaylistTracks.Clear();
                var offset = 0;
                const int limit = 50;

                while (true)
                {
                    var page = await _spotify.GetPlaylistTracksPageAsync(_selectedPlaylist.Id, offset, limit);
                    if (page.Items == null || page.Items.Count == 0)
                        break;

                    foreach (var track in page.Items)
                    {
                        _currentPlaylistTracks.Add(track);
                    }

                    if (page.Items.Count < limit)
                        break;

                    offset += limit;
                }

                Status = $"Loaded {_currentPlaylistTracks.Count} tracks";
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("Not authenticated"))
            {
                // Authentication required - navigate to login page
                System.Diagnostics.Debug.WriteLine("Authentication required - navigating to login page");
                MessengerInstance.Send(new object(), MessageType.AuthenticationRequired);
                Status = "Please log in to continue";
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading playlist tracks: {ex.Message}");
                Status = $"Failed to load tracks: {ex.Message}";
            }
            finally
            {
                EndBusy(LoadingType.LoadTracks);
            }
        }

        private byte[] ConvertImageToBytes(string imagePath)
        {
            using (var originalImage = System.Drawing.Image.FromFile(imagePath))
            using (var ms = new System.IO.MemoryStream())
            {
                // Resize to max 300x300 maintaining aspect ratio as recommended by Spotify
                var resizedImage = ResizeImage(originalImage, 300, 300);

                // Use high quality JPEG compression to stay under 256KB limit
                var encoderParameters = new System.Drawing.Imaging.EncoderParameters(1);
                encoderParameters.Param[0] = new System.Drawing.Imaging.EncoderParameter(
                    System.Drawing.Imaging.Encoder.Quality, 85L); // 85% quality

                var jpegEncoder = GetEncoder(System.Drawing.Imaging.ImageFormat.Jpeg);
                if (jpegEncoder == null)
                {
                    throw new InvalidOperationException("JPEG encoder not found on this system");
                }

                resizedImage.Save(ms, jpegEncoder, encoderParameters);

                var imageBytes = ms.ToArray();

                // Check size (256KB = 262144 bytes)
                if (imageBytes.Length > 262144)
                {
                    // If still too large, try lower quality
                    ms.SetLength(0);
                    encoderParameters.Param[0] = new System.Drawing.Imaging.EncoderParameter(
                        System.Drawing.Imaging.Encoder.Quality, 70L); // 70% quality
                    resizedImage.Save(ms, jpegEncoder, encoderParameters);
                    imageBytes = ms.ToArray();

                    if (imageBytes.Length > 262144)
                    {
                        // Try even lower quality
                        ms.SetLength(0);
                        encoderParameters.Param[0] = new System.Drawing.Imaging.EncoderParameter(
                            System.Drawing.Imaging.Encoder.Quality, 50L); // 50% quality
                        resizedImage.Save(ms, jpegEncoder, encoderParameters);
                        imageBytes = ms.ToArray();

                        if (imageBytes.Length > 262144)
                        {
                            throw new InvalidOperationException($"Image is too large even after maximum compression. Size: {imageBytes.Length} bytes. Maximum allowed: 256KB.");
                        }
                    }
                }

                // Verify the image data starts with JPEG header (0xFF 0xD8)
                if (imageBytes.Length < 2 || imageBytes[0] != 0xFF || imageBytes[1] != 0xD8)
                {
                    throw new InvalidOperationException("Generated image is not a valid JPEG file");
                }

                System.Diagnostics.Debug.WriteLine($"Image converted to JPEG successfully. Size: {imageBytes.Length} bytes");
                return imageBytes;
            }
        }

        private System.Drawing.Imaging.ImageCodecInfo GetEncoder(System.Drawing.Imaging.ImageFormat format)
        {
            var codecs = System.Drawing.Imaging.ImageCodecInfo.GetImageEncoders();
            foreach (var codec in codecs)
            {
                if (codec.FormatID == format.Guid)
                {
                    return codec;
                }
            }
            throw new InvalidOperationException($"JPEG encoder not found on this system. Required for playlist image upload.");
        }

        private System.Drawing.Image ResizeImage(System.Drawing.Image image, int maxWidth, int maxHeight)
        {
            if (image == null)
                throw new ArgumentNullException(nameof(image));

            // Calculate new size maintaining aspect ratio
            float ratioX = (float)maxWidth / image.Width;
            float ratioY = (float)maxHeight / image.Height;
            float ratio = Math.Min(ratioX, ratioY);

            int newWidth = (int)(image.Width * ratio);
            int newHeight = (int)(image.Height * ratio);

            // Resize image
            var newImage = new System.Drawing.Bitmap(image, newWidth, newHeight);

            return newImage;
        }

        private void StartBusy(string status, LoadingType loadingType = LoadingType.General)
        {
            Status = status;
            ProgressVisibility = Visibility.Visible;

            System.Diagnostics.Debug.WriteLine($"StartBusy called with type: {loadingType}");

            switch (loadingType)
            {
                case LoadingType.Search:
                    IsSearching = true;
                    break;
                case LoadingType.LoadTracks:
                    IsLoadingTracks = true;
                    break;
                case LoadingType.Playlists:
                    _isLoadingPlaylists = true;
                    break;
            }
        }

        private void EndBusy(LoadingType loadingType = LoadingType.General)
        {
            ProgressVisibility = Visibility.Hidden;

            System.Diagnostics.Debug.WriteLine($"EndBusy called with type: {loadingType}");

            switch (loadingType)
            {
                case LoadingType.Search:
                    IsSearching = false;
                    break;
                case LoadingType.LoadTracks:
                    IsLoadingTracks = false;
                    break;
                case LoadingType.Playlists:
                    _isLoadingPlaylists = false;
                    break;
            }
        }

        private enum LoadingType
        {
            General,
            Search,
            LoadTracks,
            Playlists
        }

        private async Task UpdateTracksInSelectedPlaylistAsync()
        {
            if (_selectedPlaylist == null || _availableTracks.Count == 0)
            {
                // Reset all tracks to not in playlist
                foreach (var item in _availableTracks.Where(i => i.CanAddToPlaylist))
                {
                    item.IsInSelectedPlaylist = false;
                }
                return;
            }

            try
            {
                // Get all tracks from the selected playlist
                var playlistTrackIds = new HashSet<string>();
                var offset = 0;
                const int limit = 50;

                while (true)
                {
                    var page = await _spotify.GetPlaylistTracksPageAsync(_selectedPlaylist.Id, offset, limit);
                    if (page.Items == null || page.Items.Count == 0)
                        break;

                    foreach (var track in page.Items)
                    {
                        if (!string.IsNullOrEmpty(track.Id))
                        {
                            playlistTrackIds.Add(track.Id);
                        }
                    }

                    if (page.Items.Count < limit)
                        break;

                    offset += limit;
                }

                // Update IsInSelectedPlaylist for tracks only
                foreach (var item in _availableTracks.Where(i => i.CanAddToPlaylist))
                {
                    item.IsInSelectedPlaylist = playlistTrackIds.Contains(item.Id);
                }
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("Not authenticated"))
            {
                // Authentication required - navigate to login page
                System.Diagnostics.Debug.WriteLine("Authentication required - navigating to login page");
                MessengerInstance.Send(new object(), MessageType.AuthenticationRequired);
                Status = "Please log in to continue";
            }
            catch (Exception ex)
            {
                // If we can't get playlist tracks, assume tracks are not in playlist
                foreach (var item in _availableTracks.Where(i => i.CanAddToPlaylist))
                {
                    item.IsInSelectedPlaylist = false;
                }
                Status = $"Error checking playlist tracks: {ex.Message}";
            }
        }

        private bool CanRemoveTrack(TrackDto track)
        {
            return track != null && _selectedPlaylist != null && _viewingGeneratedPlaylist == null;
        }

        private async void RemoveTrackFromPlaylist(TrackDto track)
        {
            if (track == null || _selectedPlaylist == null || string.IsNullOrEmpty(track.Uri)) return;

            try
            {
                StartBusy("Removing track from playlist...");

                await _spotify.RemoveTracksFromPlaylistAsync(_selectedPlaylist.Id, new[] { track.Uri });
                _currentPlaylistTracks.Remove(track);

                Status = "Track removed from playlist";
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("Not authenticated"))
            {
                // Authentication required - navigate to login page
                System.Diagnostics.Debug.WriteLine("Authentication required - navigating to login page");
                MessengerInstance.Send(new object(), MessageType.AuthenticationRequired);
                Status = "Please log in to continue";
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error removing track: {ex.Message}");
                Status = $"Failed to remove track: {ex.Message}";
            }
            finally
            {
                EndBusy();
            }
        }

        private bool CanMoveTrackUp(TrackDto track)
        {
            return track != null && _currentPlaylistTracks.IndexOf(track) > 0 && _viewingGeneratedPlaylist == null;
        }

        private bool CanMoveTrackDown(TrackDto track)
        {
            return track != null && _currentPlaylistTracks.IndexOf(track) < _currentPlaylistTracks.Count - 1 && _viewingGeneratedPlaylist == null;
        }

        private async void MoveTrackUp(TrackDto track)
        {
            if (track == null || _selectedPlaylist == null) return;

            var currentIndex = _currentPlaylistTracks.IndexOf(track);
            if (currentIndex <= 0) return;

            try
            {
                StartBusy("Moving track up...");

                // Call Spotify API to reorder (use original positions)
                await _spotify.ReorderPlaylistTracksAsync(_selectedPlaylist.Id, currentIndex, currentIndex - 1, 1);

                // Update local collection
                _currentPlaylistTracks.Move(currentIndex, currentIndex - 1);

                Status = "Track moved up";
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("Not authenticated"))
            {
                // Authentication required - navigate to login page
                System.Diagnostics.Debug.WriteLine("Authentication required - navigating to login page");
                MessengerInstance.Send(new object(), MessageType.AuthenticationRequired);
                Status = "Please log in to continue";
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error moving track up: {ex.Message}");
                Status = $"Failed to move track: {ex.Message}";
            }
            finally
            {
                EndBusy();
            }
        }

        private async void MoveTrackDown(TrackDto track)
        {
            if (track == null || _selectedPlaylist == null) return;

            var currentIndex = _currentPlaylistTracks.IndexOf(track);
            if (currentIndex < 0 || currentIndex >= _currentPlaylistTracks.Count - 1) return;

            try
            {
                StartBusy("Moving track down...");

                // Call Spotify API to reorder (use original positions)
                await _spotify.ReorderPlaylistTracksAsync(_selectedPlaylist.Id, currentIndex, currentIndex + 2, 1);

                // Update local collection
                _currentPlaylistTracks.Move(currentIndex, currentIndex + 1);

                Status = "Track moved down";
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("Not authenticated"))
            {
                // Authentication required - navigate to login page
                System.Diagnostics.Debug.WriteLine("Authentication required - navigating to login page");
                MessengerInstance.Send(new object(), MessageType.AuthenticationRequired);
                Status = "Please log in to continue";
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error moving track down: {ex.Message}");
                Status = $"Failed to move track: {ex.Message}";
            }
            finally
            {
                EndBusy();
            }
        }

        private async void LoadArtistTracks(SearchResultDto searchResult)
        {
            System.Diagnostics.Debug.WriteLine($"LoadArtistTracks called with searchResult: {searchResult?.Name} (Type: {searchResult?.Type}, ID: {searchResult?.Id})");

            if (searchResult == null)
            {
                System.Diagnostics.Debug.WriteLine("LoadArtistTracks: searchResult is null");
                return;
            }

            if (searchResult.Type != "artist")
            {
                System.Diagnostics.Debug.WriteLine($"LoadArtistTracks: searchResult.Type is '{searchResult.Type}', expected 'artist'");
                return;
            }

            if (string.IsNullOrEmpty(searchResult.Id))
            {
                System.Diagnostics.Debug.WriteLine("LoadArtistTracks: searchResult.Id is null or empty");
                return;
            }

            System.Diagnostics.Debug.WriteLine($"LoadArtistTracks: Processing artist '{searchResult.Name}' with ID '{searchResult.Id}'");

            try
            {
                StartBusy($"Loading top tracks for '{searchResult.Name}'...", LoadingType.Search);

                var artistTracks = await _spotify.GetArtistTopTracksAsync(searchResult.Id);

                System.Diagnostics.Debug.WriteLine($"LoadArtistTracks: Retrieved {artistTracks.Count} tracks for artist '{searchResult.Name}'");

                _availableTracks.Clear();

                foreach (var track in artistTracks.OrderBy(t => t.Name))
                {
                    var searchResultDto = new SearchResultDto
                    {
                        Id = track.Id,
                        Name = track.Name,
                        Type = "track",
                        Description = $"{track.Artists} - {track.AlbumName}",
                        ImageUrl = track.AlbumImageUrl,
                        Href = track.Href,
                        Uri = track.Uri,
                        CanAddToPlaylist = true,
                        IsInSelectedPlaylist = false,
                        OriginalDto = track
                    };
                    _availableTracks.Add(searchResultDto);
                }

                // Update track status for selected playlist
                if (_selectedPlaylist != null)
                {
                    await UpdateTracksInSelectedPlaylistAsync();
                }

                Status = $"Loaded {artistTracks.Count} top tracks for '{searchResult.Name}'";
                System.Diagnostics.Debug.WriteLine($"LoadArtistTracks: Successfully loaded {artistTracks.Count} tracks");
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("Not authenticated"))
            {
                // Authentication required - navigate to login page
                System.Diagnostics.Debug.WriteLine("Authentication required - navigating to login page");
                MessengerInstance.Send(new object(), MessageType.AuthenticationRequired);
                Status = "Please log in to continue";
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading artist tracks: {ex.Message}");
                Status = $"Failed to load artist tracks: {ex.Message}";
            }
            finally
            {
                EndBusy(LoadingType.Search);
            }
        }

        // Playlist Generator Methods
        private async Task GenerateRandomPlaylistAsync()
        {
            try
            {
                IsGenerating = true;
                StartBusy("Generating random playlist...");

                // Generate random name
                var randomNames = new[] { "Chill Vibes", "Party Mix", "Road Trip", "Workout Beats", "Study Session", "Late Night", "Morning Coffee", "Weekend Fun", "Random Hits", "Discovery Mode" };
                var randomAdjectives = new[] { "Awesome", "Epic", "Fantastic", "Groovy", "Amazing", "Super", "Cool", "Fresh", "Hot", "Wild" };
                var selectedBaseName = randomNames[new Random().Next(randomNames.Length)];
                var selectedAdjective = randomAdjectives[new Random().Next(randomAdjectives.Length)];
                var randomName = $"{selectedAdjective} {selectedBaseName}";

                // Generate description that matches the vibe
                var description = GenerateVibeDescription(selectedBaseName);

                // Get tracks from new releases instead of random search
                var tracks = new List<TrackDto>();
                var allTracks = new List<TrackDto>();
                
                // Get new releases (may need multiple pages)
                int albumOffset = 0;
                const int albumsPerPage = 20; // Get more albums to have variety
                int maxAlbums = Math.Min(albumsPerPage * 3, 100); // Limit to prevent too many API calls
                
                while (allTracks.Count < _selectedSongCount * 2 && albumOffset < maxAlbums)
                {
                    var newReleases = await _spotify.GetNewReleasesPageAsync(albumOffset, albumsPerPage);
                    if (newReleases?.Items == null || newReleases.Items.Count == 0)
                        break;

                    // For each album, get its tracks
                    foreach (var album in newReleases.Items)
                    {
                        if (album == null || string.IsNullOrEmpty(album.Id)) continue;
                        
                        var albumTracks = await _spotify.GetAlbumTracksAsync(album.Id);
                        if (albumTracks != null)
                        {
                            // Set album information for each track
                            foreach (var track in albumTracks)
                            {
                                track.AlbumName = album.Name;
                                track.AlbumImageUrl = album.ImageUrl;
                            }
                            allTracks.AddRange(albumTracks);
                        }
                        
                        // Break if we have enough tracks
                        if (allTracks.Count >= _selectedSongCount * 2)
                            break;
                    }
                    
                    albumOffset += albumsPerPage;
                    
                    // Break if no more pages
                    if (string.IsNullOrEmpty(newReleases.Next))
                        break;
                }

                // Randomly select the required number of tracks
                if (allTracks.Count > 0)
                {
                    var random = new Random();
                    tracks = allTracks.OrderBy(t => random.Next()).Take(_selectedSongCount).ToList();
                }

                // Get random artwork from a free image service (using Lorem Picsum)
                var randomImageId = new Random().Next(1, 1000);
                var imageUrl = $"https://picsum.photos/300/300?random={randomImageId}";

                // Create generated playlist
                var generatedPlaylist = new GeneratedPlaylistDto
                {
                    Name = randomName,
                    Genre = "New Releases", // Always new releases now
                    TrackCount = tracks.Count,
                    Tracks = new ObservableCollection<TrackDto>(tracks),
                    ImageUrl = imageUrl,
                    Description = description
                };

                // Set position numbers for tracks
                for (int i = 0; i < generatedPlaylist.Tracks.Count; i++)
                {
                    generatedPlaylist.Tracks[i].Position = i + 1;
                }

                _generatedPlaylists.Add(generatedPlaylist);
                Status = $"Generated playlist '{randomName}' with {tracks.Count} tracks";
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("Not authenticated"))
            {
                // Authentication required - navigate to login page
                System.Diagnostics.Debug.WriteLine("Authentication required - navigating to login page");
                MessengerInstance.Send(new object(), MessageType.AuthenticationRequired);
                Status = "Please log in to continue";
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error generating playlist: {ex.Message}");
                Status = $"Failed to generate playlist: {ex.Message}";
            }
            finally
            {
                IsGenerating = false;
                EndBusy();
            }
        }

        private string GenerateVibeDescription(string baseName)
        {
            return baseName switch
            {
                "Chill Vibes" => "Relax and unwind with these mellow, atmospheric tracks perfect for downtime",
                "Party Mix" => "Get the party started with high-energy beats and danceable rhythms",
                "Road Trip" => "Perfect soundtrack for your next adventure with upbeat and scenic vibes",
                "Workout Beats" => "Power through your workout with motivating rhythms and driving beats",
                "Study Session" => "Focus and concentrate with instrumental tracks and ambient sounds",
                "Late Night" => "Wind down your day with smooth, introspective melodies and chill vibes",
                "Morning Coffee" => "Start your day right with refreshing tunes and positive energy",
                "Weekend Fun" => "Make the most of your weekend with carefree and joyful melodies",
                "Random Hits" => "A eclectic mix of popular tracks spanning different genres and eras",
                "Discovery Mode" => "Explore new sounds and artists with fresh, exciting discoveries",
                _ => "A curated collection of tracks with great vibes"
            };
        }

        private async void SaveGeneratedPlaylist(GeneratedPlaylistDto playlist)
        {
            if (playlist == null) return;

            try
            {
                StartBusy($"Saving playlist '{playlist.Name}'...");

                // Create the playlist on Spotify
                var createdPlaylist = await _spotify.CreatePlaylistAsync(
                    playlist.Name,
                    playlist.Description,
                    true, // public
                    false // not collaborative
                );

                // Add tracks to the playlist
                if (playlist.Tracks.Count > 0)
                {
                    var trackUris = playlist.Tracks.Where(t => !string.IsNullOrEmpty(t.Uri)).Select(t => t.Uri!).ToList();
                    await _spotify.AddTracksToPlaylistAsync(createdPlaylist.Id, trackUris);
                }

                // Try to upload the random image
                try
                {
                    if (!string.IsNullOrWhiteSpace(playlist.ImageUrl))
                    {
                        // Download the image from the URL
                        using (var client = new System.Net.Http.HttpClient())
                        {
                            var imageBytes = await client.GetByteArrayAsync(playlist.ImageUrl);
                            var base64Image = Convert.ToBase64String(imageBytes);
                            await _spotify.UploadPlaylistImageAsync(createdPlaylist.Id, base64Image);
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to upload playlist image: {ex.Message}");
                    // Don't fail the whole operation if image upload fails
                }

                // Add to user playlists
                UserPlaylists.Insert(0, createdPlaylist);
                SelectedPlaylist = createdPlaylist;

                // Remove from generated playlists
                _generatedPlaylists.Remove(playlist);

                Status = $"Playlist '{playlist.Name}' saved to Spotify!";
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("Not authenticated"))
            {
                // Authentication required - navigate to login page
                System.Diagnostics.Debug.WriteLine("Authentication required - navigating to login page");
                MessengerInstance.Send(new object(), MessageType.AuthenticationRequired);
                Status = "Please log in to continue";
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error saving playlist: {ex.Message}");
                Status = $"Failed to save playlist: {ex.Message}";
            }
            finally
            {
                EndBusy();
            }
        }

        private void ViewGeneratedPlaylist(GeneratedPlaylistDto playlist)
        {
            if (playlist == null) return;

            // Clear current tracks and set viewing state
            _currentPlaylistTracks.Clear();
            _viewingGeneratedPlaylist = playlist;

            // Add all tracks from the generated playlist
            foreach (var track in playlist.Tracks)
            {
                _currentPlaylistTracks.Add(track);
            }

            // Update the header and status
            RaisePropertyChanged(nameof(PlaylistTracksHeader));
            Status = $"Viewing generated playlist '{playlist.Name}' with {playlist.Tracks.Count} tracks";
        }

        private void RemoveTrackFromGeneratedPlaylist(TrackDto track)
        {
            if (track == null || _selectedGeneratedPlaylist == null) return;

            _selectedGeneratedPlaylist.Tracks.Remove(track);
            
            // Update positions after removal
            for (int i = 0; i < _selectedGeneratedPlaylist.Tracks.Count; i++)
            {
                _selectedGeneratedPlaylist.Tracks[i].Position = i + 1;
            }
            
            Status = $"Removed '{track.Name}' from generated playlist";
        }

        private void DeleteGeneratedPlaylist(GeneratedPlaylistDto playlist)
        {
            if (playlist == null) return;

            var result = _confirmationDialogService.ShowConfirmation(
                "Delete Generated Playlist",
                $"Are you sure you want to delete the generated playlist '{playlist.Name}'?",
                "Delete",
                "Cancel");

            if (result == true)
            {
                _generatedPlaylists.Remove(playlist);
                if (_selectedGeneratedPlaylist == playlist)
                {
                    SelectedGeneratedPlaylist = null;
                }
                Status = $"Deleted generated playlist '{playlist.Name}'";
            }
        }
    }
}