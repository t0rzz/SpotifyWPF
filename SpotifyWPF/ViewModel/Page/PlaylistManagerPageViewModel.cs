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
        private readonly ISpotify _spotify;

        private Visibility _progressVisibility = Visibility.Hidden;
        private string _status = "Ready";

        private bool _isLoadingPlaylists;
        private bool _isSearching;
        private bool _isLoadingTracks;

        // Playlist Manager properties
        private string _newPlaylistName = string.Empty;
        private string _newPlaylistDescription = string.Empty;
        private bool _newPlaylistIsPublic = true;
        private bool _newPlaylistIsCollaborative = false;
        private string _selectedImagePath = string.Empty;
        private PlaylistDto? _selectedPlaylist;
        private ObservableCollection<SearchResultDto> _availableTracks = new ObservableCollection<SearchResultDto>();
        private ObservableCollection<SearchResultDto> _selectedTracks = new ObservableCollection<SearchResultDto>();

        // Search properties
        private string _searchQuery = string.Empty;
        private bool _searchTrack = true; // Default to track search
        private bool _searchArtist = false;
        private bool _searchAlbum = false;
        private bool _searchPlaylist = false;
        private bool _searchShow = false;
        private bool _searchEpisode = false;
        private bool _searchAudiobook = false;

        public PlaylistManagerPageViewModel(ISpotify spotify, IMapper mapper, IMessageBoxService messageBoxService, IConfirmationDialogService confirmationDialogService)
        {
            _spotify = spotify;
            _mapper = mapper;
            _messageBoxService = messageBoxService;
            _confirmationDialogService = confirmationDialogService;

            // Initialize commands
            CreatePlaylistCommand = new RelayCommand(async () => await CreatePlaylistAsync(), CanCreatePlaylist);
            SelectImageCommand = new RelayCommand(SelectImage);
            LoadAvailableTracksCommand = new RelayCommand(async () => await LoadAvailableTracksAsync());
            SearchTracksCommand = new RelayCommand(async () => await SearchTracksAsync(), CanSearch);
            AddTrackToSelectionCommand = new RelayCommand<SearchResultDto>(AddTrackToSelection, CanAddTrack);
            RemoveTrackFromSelectionCommand = new RelayCommand<SearchResultDto>(RemoveTrackFromSelection);
            AddSelectedTracksToPlaylistCommand = new RelayCommand(async () => await AddSelectedTracksToPlaylistAsync(), CanAddSelectedTracks);
            LoadUserPlaylistsCommand = new RelayCommand(async () => await LoadUserPlaylistsAsync());

            // Listen to SelectedTracks changes to update command availability
            _selectedTracks.CollectionChanged += (s, e) => AddSelectedTracksToPlaylistCommand.RaiseCanExecuteChanged();
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
                    _ = UpdateTracksInSelectedPlaylistAsync();
                }
            }
        }

        public ObservableCollection<SearchResultDto> AvailableTracks => _availableTracks;
        public ObservableCollection<SearchResultDto> SelectedTracks => _selectedTracks;
        public ObservableCollection<PlaylistDto> UserPlaylists { get; } = new ObservableCollection<PlaylistDto>();

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
                // Load up to 10,000 results by iterating through pages
                const int maxResults = 10000;
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

            while (totalLoaded < maxResults)
            {
                // Update progress status
                var typesString = string.Join(", ", types);
                Status = $"Loading {typesString}... ({totalLoaded}/{maxResults})";

                var trackResults = await _spotify.SearchItemsPageAsync(query, types, offset, pageSize);
                System.Diagnostics.Debug.WriteLine($"Loaded page with offset {offset}, returned {trackResults?.Items?.Count ?? 0} results");

                if (trackResults?.Items == null || trackResults.Items.Count == 0)
                {
                    // No more results
                    break;
                }

                foreach (var item in trackResults.Items)
                {
                    if (totalLoaded >= maxResults)
                        break;

                    System.Diagnostics.Debug.WriteLine($"Adding item: {item.Name} ({item.Type}), URI: {item.Uri}");
                    _availableTracks.Add(item);
                    totalLoaded++;
                }

                // Check if there are more pages
                if (string.IsNullOrEmpty(trackResults.Next) || totalLoaded >= maxResults)
                {
                    break;
                }

                offset += pageSize;

                // Small delay to avoid overwhelming the API
                await Task.Delay(100);
            }

            System.Diagnostics.Debug.WriteLine($"Total items loaded: {totalLoaded}");
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
                    }

                    offset += limit;

                    // Safety check to avoid infinite loops
                    if (page.Items == null || page.Items.Count == 0)
                        break;
                }

                // Now filter by ownership
                UserPlaylists.Clear();
                foreach (var playlist in allPlaylists)
                {
                    // Only include playlists owned by the current user
                    if (playlist.OwnerId == currentUser.Id)
                    {
                        UserPlaylists.Add(playlist);
                    }
                }

                Status = $"Loaded {UserPlaylists.Count} owned playlists";
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
                        throw new InvalidOperationException($"Image is too large even after compression. Size: {imageBytes.Length} bytes. Maximum allowed: 256KB.");
                    }
                }

                return imageBytes;
            }
        }

        private System.Drawing.Imaging.ImageCodecInfo GetEncoder(System.Drawing.Imaging.ImageFormat format)
        {
            var codecs = System.Drawing.Imaging.ImageCodecInfo.GetImageDecoders();
            foreach (var codec in codecs)
            {
                if (codec.FormatID == format.Guid)
                {
                    return codec;
                }
            }
            throw new InvalidOperationException("JPEG encoder not found on this system.");
        }

        private System.Drawing.Image ResizeImage(System.Drawing.Image image, int maxWidth, int maxHeight)
        {
            var ratioX = (double)maxWidth / image.Width;
            var ratioY = (double)maxHeight / image.Height;
            var ratio = Math.Min(ratioX, ratioY);

            // Only resize if image is larger than max dimensions
            if (ratio >= 1.0)
                return new System.Drawing.Bitmap(image); // Return copy if already small enough

            var newWidth = (int)(image.Width * ratio);
            var newHeight = (int)(image.Height * ratio);

            var newImage = new System.Drawing.Bitmap(newWidth, newHeight);
            using (var graphics = System.Drawing.Graphics.FromImage(newImage))
            {
                graphics.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;
                graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
                graphics.DrawImage(image, 0, 0, newWidth, newHeight);
            }

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
                const int limit = 100;

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
    }
}