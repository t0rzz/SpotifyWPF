using System;
using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using AutoMapper;
using GalaSoft.MvvmLight;
using GalaSoft.MvvmLight.Command;
using SpotifyWPF.Model;
using SpotifyWPF.Model.Dto;
using SpotifyWPF.Service;
using SpotifyWPF.Service.MessageBoxes;
using MessageBoxButton = SpotifyWPF.Service.MessageBoxes.MessageBoxButton;
using MessageBoxResult = SpotifyWPF.Service.MessageBoxes.MessageBoxResult;

namespace SpotifyWPF.ViewModel.Page
{
    public class AlbumsPageViewModel : ViewModelBase
    {
        private readonly IMapper _mapper;
        private readonly IMessageBoxService _messageBoxService;
        private readonly ISpotify _spotify;

        private Visibility _progressVisibility = Visibility.Hidden;
        private string _status = "Ready";
        private bool _isLoadingAlbums;
        private readonly HashSet<string> _albumIds = new HashSet<string>();

        // Search filter properties
        private string _albumsFilterText = string.Empty;
        private bool _isMultipleAlbumsSelected;
        private IList _selectedAlbums = new List<object>();

        public bool IsMultipleAlbumsSelected
        {
            get => _isMultipleAlbumsSelected;
            set
            {
                if (_isMultipleAlbumsSelected != value)
                {
                    _isMultipleAlbumsSelected = value;
                    RaisePropertyChanged();
                }
            }
        }

        public IList SelectedAlbums
        {
            get => _selectedAlbums;
            set
            {
                _selectedAlbums = value;
                RaisePropertyChanged();
            }
        }

        // Commands
        public RelayCommand LoadAlbumsCommand { get; private set; }
        public RelayCommand<IList> DeleteSelectedAlbumsCommand { get; private set; }
        public RelayCommand DeleteSelectedAlbumsFromContextMenuCommand { get; private set; }

        // Context menu commands
        public RelayCommand<Album> OpenInSpotifyCommand { get; private set; }
        public RelayCommand<Album> CopyAlbumLinkCommand { get; private set; }
        public RelayCommand<Album> RemoveAlbumCommand { get; private set; }

        public AlbumsPageViewModel(ISpotify spotify, IMapper mapper, IMessageBoxService messageBoxService)
        {
            _spotify = spotify;
            _mapper = mapper;
            _messageBoxService = messageBoxService;

            Albums = new ObservableCollection<Album>();
            FilteredAlbums = new ObservableCollection<Album>();

            // Initialize filtering
            InitializeFiltering();

            LoadAlbumsCommand = new RelayCommand(
                async () => await LoadAlbumsAsync(),
                () => !_isLoadingAlbums
            );

            DeleteSelectedAlbumsCommand = new RelayCommand<IList>(
                async albums => await DeleteSelectedAlbumsAsync(albums),
                albums => albums != null && albums.Count > 0
            );

            DeleteSelectedAlbumsFromContextMenuCommand = new RelayCommand(
                async () => await DeleteSelectedAlbumsAsync(_selectedAlbums),
                () => _selectedAlbums != null && _selectedAlbums.Count > 0
            );

            // Context menu commands
            OpenInSpotifyCommand = new RelayCommand<Album>(
                album =>
                {
                    var url = BuildAlbumWebUrl(album);
                    if (!string.IsNullOrWhiteSpace(url))
                    {
                        TryOpenUrl(url);
                    }
                },
                album => album != null && !string.IsNullOrWhiteSpace(album.Id)
            );

            CopyAlbumLinkCommand = new RelayCommand<Album>(
                album =>
                {
                    var url = BuildAlbumWebUrl(album);
                    if (!string.IsNullOrWhiteSpace(url))
                    {
                        TryCopyToClipboard(url);
                    }
                },
                album => album != null && !string.IsNullOrWhiteSpace(album.Id)
            );

            RemoveAlbumCommand = new RelayCommand<Album>(
                async album =>
                {
                    if (album == null) return;
                    await DeleteSelectedAlbumsAsync(new[] { album });
                },
                album => album != null && !string.IsNullOrWhiteSpace(album.Id)
            );

            // Load user greeting and profile image
            _ = Task.Run(async () =>
            {
                try
                {
                    await LoadGreetingAsync();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error loading greeting: {ex.Message}");
                }
            });
        }

        // Set up collection change handlers for filtering
        private void InitializeFiltering()
        {
            Albums.CollectionChanged += (s, e) => ApplyAlbumsFilter();
        }

        private void ApplyAlbumsFilter()
        {
            FilteredAlbums.Clear();

            var filteredItems = string.IsNullOrWhiteSpace(_albumsFilterText)
                ? Albums
                : Albums.Where(item =>
                    (item.Name?.Contains(_albumsFilterText, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    (item.Artist?.Contains(_albumsFilterText, StringComparison.OrdinalIgnoreCase) ?? false));

            foreach (var item in filteredItems)
            {
                FilteredAlbums.Add(item);
            }
        }

        public ObservableCollection<Album> Albums { get; }

        // Filtered collection for search functionality
        public ObservableCollection<Album> FilteredAlbums { get; }

        public Visibility ProgressVisibility
        {
            get => _progressVisibility;
            set
            {
                _progressVisibility = value;
                RaisePropertyChanged();
            }
        }

        public string Status
        {
            get => _status;
            set
            {
                _status = value;
                RaisePropertyChanged();
            }
        }

        public bool IsLoadingAlbums
        {
            get => _isLoadingAlbums;
            set
            {
                _isLoadingAlbums = value;
                RaisePropertyChanged();
                LoadAlbumsCommand.RaiseCanExecuteChanged();
            }
        }

        // Context menu commands
        public string GreetingText { get; private set; } = "Hey there";
        public string? ProfileImagePath { get; private set; }

        private async Task LoadAlbumsAsync()
        {
            if (_spotify.Api == null)
            {
                System.Diagnostics.Debug.WriteLine("You must be logged in to load albums.");
                Status = "Please log in to load albums";
                return;
            }

            if (_isLoadingAlbums)
            {
                System.Diagnostics.Debug.WriteLine("An albums load is already in progress. Skipping.");
                return;
            }

            _isLoadingAlbums = true;
            UpdateLoadingState();

            try
            {
                Status = "Loading albums...";
                ProgressVisibility = Visibility.Visible;

                // Clear existing albums
                await Application.Current.Dispatcher.BeginInvoke((Action)(() =>
                {
                    Albums.Clear();
                    _albumIds.Clear();
                }));

                const int limit = 20;
                int offset = 0;
                int? expectedTotal = null;

                // 1) Get the first page to determine total
                {
                    const int maxAttempts = 5;
                    bool pageLoaded = false;
                    PagingDto<AlbumDto>? page = null;

                    for (var attempt = 1; attempt <= maxAttempts && !pageLoaded; attempt++)
                    {
                        try
                        {
                            await _spotify.EnsureAuthenticatedAsync();
                            page = await _spotify.GetMySavedAlbumsPageAsync(offset, limit);

                            // First page valid: set expected total
                            if (page != null && page.Total > 0)
                            {
                                expectedTotal = page.Total;
                                System.Diagnostics.Debug.WriteLine($"Detected expected total albums from API: {expectedTotal.Value}");
                            }

                            // Add items from first page
                            var countToAdd = page?.Items?.Count ?? 0;
                            if (countToAdd > 0)
                            {
                                var toAdd = page;
                                var disp = Application.Current?.Dispatcher;
                                if (disp != null)
                                {
                                    await disp.BeginInvoke((Action)(() => { AddAlbums(toAdd); }));
                                }
                                else
                                {
                                    AddAlbums(toAdd);
                                }
                            }

                            pageLoaded = true;
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"First page fetch attempt {attempt} failed: {ex.Message}");
                        }
                    }

                    if (!pageLoaded)
                    {
                        Status = "Failed to load albums";
                        return;
                    }
                }

                // If we couldn't determine total, stop
                if (!expectedTotal.HasValue || expectedTotal.Value <= 0)
                {
                    Status = $"Loaded {Albums.Count} albums";
                    return;
                }

                // 2) Load remaining pages concurrently
                var total = expectedTotal.Value;
                var nextStart = limit; // we already added offset 0
                var offsets = new System.Collections.Concurrent.ConcurrentQueue<int>();
                for (var off = nextStart; off < total; off += limit)
                {
                    offsets.Enqueue(off);
                }

                // Calculate workers (respecting max threads setting)
                var pagesRemaining = (int)Math.Ceiling((total - nextStart) / (double)limit);
                
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
                
                var computedWorkers = Math.Min(pagesRemaining, maxThreads);
                System.Diagnostics.Debug.WriteLine($"Starting concurrent album fetch with {computedWorkers} worker(s). Total expected={total}, page size={limit}");

                var workers = new List<Task>();
                var allLoaded = false;

                for (var w = 0; w < computedWorkers; w++)
                {
                    workers.Add(Task.Run(async () =>
                    {
                        while (!allLoaded && offsets.TryDequeue(out var off))
                        {
                            const int maxAttempts = 5;
                            bool pageLoaded = false;

                            for (var attempt = 1; attempt <= maxAttempts && !pageLoaded; attempt++)
                            {
                                try
                                {
                                    await _spotify.EnsureAuthenticatedAsync();
                                    var page = await _spotify.GetMySavedAlbumsPageAsync(off, limit);

                                    var countToAdd = page?.Items?.Count ?? 0;
                                    if (countToAdd > 0)
                                    {
                                        var toAdd = page;
                                        var disp = Application.Current?.Dispatcher;
                                        if (disp != null)
                                        {
                                            await disp.BeginInvoke((Action)(() => { AddAlbums(toAdd); }));
                                        }
                                        else
                                        {
                                            AddAlbums(toAdd);
                                        }
                                    }

                                    pageLoaded = true;

                                    var uiCount = Albums.Count;
                                    if (uiCount >= total)
                                    {
                                        allLoaded = true;
                                        break;
                                    }
                                }
                                catch (Exception ex)
                                {
                                    System.Diagnostics.Debug.WriteLine($"Page fetch attempt {attempt} at offset {off} failed: {ex.Message}");
                                }
                            }
                        }
                    }));
                }

                // Wait for all workers to complete
                await Task.WhenAll(workers);

                Status = $"Loaded {Albums.Count} albums";
            }
            catch (Exception ex)
            {
                Status = $"Error loading albums: {ex.Message}";
                _messageBoxService.ShowMessageBox(
                    $"Error loading albums: {ex.Message}",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxIcon.Error
                );
            }
            finally
            {
                _isLoadingAlbums = false;
                UpdateLoadingState();
                ProgressVisibility = Visibility.Hidden;
            }
        }

        private async Task DeleteSelectedAlbumsAsync(IList selectedAlbums)
        {
            if (selectedAlbums == null || selectedAlbums.Count == 0)
                return;

            var result = _messageBoxService.ShowMessageBox(
                $"Are you sure you want to remove {selectedAlbums.Count} album(s) from your library?",
                "Confirm Removal",
                MessageBoxButton.YesNo,
                MessageBoxIcon.Question
            );

            if (result != MessageBoxResult.Yes)
                return;

            IsLoadingAlbums = true;
            Status = "Removing albums...";
            ProgressVisibility = Visibility.Visible;

            try
            {
                var albumIds = selectedAlbums.Cast<Album>().Select(a => a.Id).ToList();
                await _spotify.RemoveSavedAlbumsAsync(albumIds);

                // Remove from local collection - create a copy to avoid enumeration issues
                var albumsToRemove = selectedAlbums.Cast<Album>().ToList();
                foreach (Album album in albumsToRemove)
                {
                    Albums.Remove(album);
                }

                Status = $"Removed {selectedAlbums.Count} album(s)";
            }
            catch (Exception ex)
            {
                Status = $"Error removing albums: {ex.Message}";
                _messageBoxService.ShowMessageBox(
                    $"Error removing albums: {ex.Message}",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxIcon.Error
                );
            }
            finally
            {
                IsLoadingAlbums = false;
                ProgressVisibility = Visibility.Hidden;
            }
        }

        public async Task LoadGreetingAsync()
        {
            try
            {
                var name = await _spotify.GetUserDisplayNameAsync().ConfigureAwait(false);
                var imgPath = await _spotify.GetProfileImageCachedPathAsync().ConfigureAwait(false);
                Application.Current.Dispatcher.Invoke(() =>
                {
                    GreetingText = string.IsNullOrWhiteSpace(name) ? "Hey there" : $"Hey {name}";
                    ProfileImagePath = imgPath ?? string.Empty;
                });
            }
            catch { }
        }

        private string BuildAlbumWebUrl(Album? album)
        {
            if (album == null) return string.Empty;
            var id = album.Id;
            if (string.IsNullOrWhiteSpace(id)) return string.Empty;
            return $"https://open.spotify.com/album/{id}";
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
                System.Windows.Clipboard.SetText(text);
                System.Diagnostics.Debug.WriteLine("Link copied to clipboard.");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to copy to clipboard: {ex.Message}");
            }
        }

        private void UpdateLoadingState()
        {
            LoadAlbumsCommand?.RaiseCanExecuteChanged();
            DeleteSelectedAlbumsCommand?.RaiseCanExecuteChanged();
            OpenInSpotifyCommand?.RaiseCanExecuteChanged();
            CopyAlbumLinkCommand?.RaiseCanExecuteChanged();
            RemoveAlbumCommand?.RaiseCanExecuteChanged();
        }

        private void AddAlbums(PagingDto<AlbumDto>? albumsPage)
        {
            if (albumsPage?.Items == null)
            {
                return;
            }

            foreach (var albumDto in albumsPage.Items)
            {
                if (albumDto == null) continue;
                if (string.IsNullOrEmpty(albumDto.Id))
                {
                    continue;
                }

                if (_albumIds.Contains(albumDto.Id))
                {
                    // Already present in UI, skip
                    continue;
                }

                var album = _mapper.Map<Album>(albumDto);
                if (album != null)
                {
                    Albums.Add(album);
                    _albumIds.Add(albumDto.Id);
                }
            }
        }
    }
}