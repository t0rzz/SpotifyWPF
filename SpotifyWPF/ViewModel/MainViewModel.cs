using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Reflection;
using System.Windows.Threading;
using System;
using MessageBoxButton = SpotifyWPF.Service.MessageBoxes.MessageBoxButton;
using MessageBoxResult = SpotifyWPF.Service.MessageBoxes.MessageBoxResult;
using MessageBoxIcon = SpotifyWPF.Service.MessageBoxes.MessageBoxIcon;
using GalaSoft.MvvmLight;
using GalaSoft.MvvmLight.Command;
using SpotifyWPF.Service;
using SpotifyWPF.Service.MessageBoxes;
using SpotifyWPF.ViewModel.Component;
using SpotifyWPF.ViewModel.Page;
using SpotifyWPF.Views;

namespace SpotifyWPF.ViewModel
{
    public class MainViewModel : ViewModelBase
    {
    private readonly SearchPageViewModel _searchPageViewModel;
    private readonly ISpotify _spotify;
    private readonly IMessageBoxService _mb;
    private readonly LoginPageViewModel _loginPageViewModel;
        private readonly PlaylistsPageViewModel _playlistsPageViewModel;
        private readonly DispatcherTimer _devicesRefreshTimer;
        private bool _isRefreshingDevicesMenu;

        private ViewModelBase? _currentPage;
        private PlayerViewModel? _player;

        public MainViewModel(LoginPageViewModel loginPageViewModel,
            PlaylistsPageViewModel playlistsPageViewModel,
            SearchPageViewModel searchPageViewModel,
            ISpotify spotify,
            IMessageBoxService messageBoxService)
        {
            _loginPageViewModel = loginPageViewModel;
            _playlistsPageViewModel = playlistsPageViewModel;
            _searchPageViewModel = searchPageViewModel;
            _spotify = spotify;
            _mb = messageBoxService;

            CurrentPage = loginPageViewModel;

            // Initialize devices refresh timer (every 30 seconds to avoid rate limiting)
            _devicesRefreshTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(Constants.DeviceRefreshIntervalSeconds)
            };
            _devicesRefreshTimer.Tick += OnDevicesRefreshTimerTick;

            MessengerInstance.Register<object>(this, MessageType.LoginSuccessful, LoginSuccessful);

            MenuItems = new ObservableCollection<MenuItemViewModel>
            {
                new MenuItemViewModel("File")
                {
                    MenuItems = new ObservableCollection<MenuItemViewModel>
                    {
                        new MenuItemViewModel("Exit", new RelayCommand(Exit))
                    }
                },
                new MenuItemViewModel("View", new RelayCommand<MenuItemViewModel>(SwitchViewFromMenuItem))
                {
                    MenuItems = new ObservableCollection<MenuItemViewModel>
                    {
                        new MenuItemViewModel("Search",
                            new RelayCommand<MenuItemViewModel>(SwitchViewFromMenuItem)),
                        new MenuItemViewModel("Playlists",
                            new RelayCommand<MenuItemViewModel>(SwitchViewFromMenuItem)) {IsChecked = true},
                    }
                },
                new MenuItemViewModel("Devices")
                {
                    MenuItems = new ObservableCollection<MenuItemViewModel>()
                },
                new MenuItemViewModel("Debug", new RelayCommand(ShowDebugWindow))
                {
                    MenuItems = new ObservableCollection<MenuItemViewModel>
                    {
                        new MenuItemViewModel("Show Debug Console", new RelayCommand(ShowDebugWindow))
                    }
                }
            };

            // Help menu with About and Logout
            MenuItems.Add(new MenuItemViewModel("Help")
            {
                MenuItems = new ObservableCollection<MenuItemViewModel>
                {
                    new MenuItemViewModel("About", new RelayCommand(ShowAbout)),
                    new MenuItemViewModel("Logout", new RelayCommand(DoLogout))
                }
            });
        }

        public ObservableCollection<MenuItemViewModel> MenuItems { get; set; }

        public ViewModelBase? CurrentPage
        {
            get => _currentPage;
            set
            {
                _currentPage = value;
                RaisePropertyChanged();
            }
        }

        /// <summary>
        /// Player ViewModel for modern UI
        /// </summary>
        public PlayerViewModel? Player
        {
            get => _player;
            set
            {
                _player = value;
                RaisePropertyChanged();
            }
        }

        /// <summary>
        /// Get the Spotify service instance
        /// </summary>
        public ISpotify GetSpotifyService() => _spotify;

        /// <summary>
        /// Check if user is already logged in and start devices timer if needed
        /// Should be called after MainWindow initialization
        /// </summary>
        public async Task CheckAutoLoginAndStartTimerAsync()
        {
            try
            {
                // Check if we can make an authenticated request
                bool isAuthenticated = await _spotify.EnsureAuthenticatedAsync().ConfigureAwait(false);
                if (isAuthenticated)
                {
                    System.Diagnostics.Debug.WriteLine("User already logged in, starting devices timer and initializing...");
                    
                    // Switch to playlists page if we're still on login page
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        if (CurrentPage == _loginPageViewModel)
                        {
                            CurrentPage = _playlistsPageViewModel;
                        }
                        
                        // Start the devices refresh timer
                        if (!_devicesRefreshTimer.IsEnabled)
                        {
                            _devicesRefreshTimer.Start();
                            System.Diagnostics.Debug.WriteLine("Started periodic devices refresh timer (auto-login detected)");
                        }
                    });
                    
                    // Initialize devices and other data
                    _ = LoadDevicesMenuAsync();
                    _ = _playlistsPageViewModel.LoadGreetingAsync();
                    _ = _playlistsPageViewModel.RefreshDevicesForTracksMenuAsync();
                    
                    // Set up logging for TracksDataGridViewModel in SearchPage
                    _searchPageViewModel.TracksDataGridViewModel.SetLogAction(msg => System.Diagnostics.Debug.WriteLine(msg));
                    
                    // Initialize the player
                    _ = InitializePlayerAfterLoginAsync();
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("User not authenticated, staying on login page");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Auto-login check failed: {ex.Message}");
                // User is not logged in, stay on login page
            }
        }

        private void LoginSuccessful(object o)
        {
            CurrentPage = _playlistsPageViewModel;
            // Populate devices after login
            _ = LoadDevicesMenuAsync();
            // Load greeting/profile once logged in
            _ = _playlistsPageViewModel.LoadGreetingAsync();
            // Also refresh devices collection used by Play To menu under Tracks grid
            _ = _playlistsPageViewModel.RefreshDevicesForTracksMenuAsync();
            
            // Set up logging for TracksDataGridViewModel in SearchPage
            _searchPageViewModel.TracksDataGridViewModel.SetLogAction(msg => System.Diagnostics.Debug.WriteLine(msg));
            
            // Start periodic devices refresh timer (every 30 seconds)
            _devicesRefreshTimer.Start();
            System.Diagnostics.Debug.WriteLine("Started periodic devices refresh timer (30 seconds interval)");
            
            // Initialize the player after login
            _ = InitializePlayerAfterLoginAsync();
        }

        /// <summary>
        /// Initialize the player by calling the MainWindow
        /// </summary>
        private async Task InitializePlayerAfterLoginAsync()
        {
            try
            {
                // Find the MainWindow and initialize the player
                var mainWindow = Application.Current.MainWindow as SpotifyWPF.Views.MainWindow;
                if (mainWindow != null)
                {
                    await mainWindow.InitializePlayerAfterLoginAsync();

                    // Ensure Player is set and proactively load Top Tracks
                    if (Player != null)
                    {
                        System.Diagnostics.Debug.WriteLine("MainViewModel: Loading user's top tracks after player init...");
                        await Player.LoadTopTracksAsync();
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to initialize player: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Player initialization error: {ex}");
            }
        }

        private void SwitchViewFromMenuItem(MenuItemViewModel menuItem)
        {
            switch (menuItem.Header)
            {
                case "Playlists":
                    CurrentPage = _playlistsPageViewModel;
                    _ = _playlistsPageViewModel.LoadGreetingAsync();
                    _ = _playlistsPageViewModel.RefreshDevicesForTracksMenuAsync();
                    break;
                case "Search":
                    CurrentPage = _searchPageViewModel;
                    // Ensure logging is set up for TracksDataGridViewModel
                    _searchPageViewModel.TracksDataGridViewModel.SetLogAction(msg => System.Diagnostics.Debug.WriteLine(msg));
                    break;
                default:
                    return;
            }

            MenuItems.First(item => item.Header == "View")
                .MenuItems.ToList().ForEach(item => item.IsChecked = false);

            menuItem.IsChecked = true;
        }

        private static void Exit()
        {
            Application.Current.MainWindow?.Close();
        }

        private void ShowAbout()
        {
            var aboutWindow = new AboutWindow
            {
                Owner = Application.Current.MainWindow
            };
            aboutWindow.ShowDialog();
        }

        private void DoLogout()
        {
            var res = _mb.ShowMessageBox(
                "Do you want to log out? Your local authorization token will be cleared.",
                "Logout",
                MessageBoxButton.YesNo,
                MessageBoxIcon.Question);
            if (res != MessageBoxResult.Yes) return;

            _spotify.Logout();
            // Back to login page
            CurrentPage = _loginPageViewModel;
        }

        private MenuItemViewModel? GetDevicesRoot()
        {
            return MenuItems.FirstOrDefault(mi => mi.Header == "Devices");
        }

        private async Task LoadDevicesMenuAsync()
        {
            var root = GetDevicesRoot();
            if (root == null) return;

            System.Diagnostics.Debug.WriteLine("=== LoadDevicesMenuAsync STARTED ===");

            try
            {
                // Silent device loading
                System.Diagnostics.Debug.WriteLine("LoadDevicesMenuAsync: Fetching devices from Spotify API...");
                
                var devices = await _spotify.GetDevicesAsync().ConfigureAwait(false);
                System.Diagnostics.Debug.WriteLine($"LoadDevicesMenuAsync: Retrieved {devices?.Count ?? 0} devices");
                
                var activeId = devices?.FirstOrDefault(d => d.IsActive)?.Id;

                var activeName = devices?.FirstOrDefault(d => d.IsActive)?.Name;
                System.Diagnostics.Debug.WriteLine($"Current playback device ID: {activeId ?? "NULL"}");
                System.Diagnostics.Debug.WriteLine($"Current playback device name: {activeName ?? "NULL"}");

                // Update on UI thread
                Application.Current.Dispatcher.Invoke(() =>
                {
                    // Instead of clearing and rebuilding, update existing items
                    if (devices != null)
                    {
                        // Get current device IDs
                        var newDeviceIds = devices.Select(d => d.Id).ToHashSet();
                        var existingItems = root.MenuItems.ToList();
                        
                        // Remove devices that are no longer available
                        for (int i = existingItems.Count - 1; i >= 0; i--)
                        {
                            var item = existingItems[i];
                            if (!string.IsNullOrEmpty(item.Id) && !newDeviceIds.Contains(item.Id))
                            {
                                root.MenuItems.RemoveAt(i);
                            }
                        }
                        
                        // Add new devices and update existing ones
                        foreach (var d in devices)
                        {
                            var existingItem = root.MenuItems.FirstOrDefault(item => item.Id == d.Id);
                            var isActive = !string.IsNullOrWhiteSpace(activeId) && string.Equals(d.Id, activeId);
                            var deviceName = string.IsNullOrWhiteSpace(d.Name) ? d.Id : d.Name;

                            System.Diagnostics.Debug.WriteLine($"LoadDevicesMenuAsync: Processing device '{deviceName}' (ID: {d.Id}, Active: {isActive})");

                            // Determine an icon glyph based on device type/name
                            string? glyph = null;
                            try
                            {
                                var type = (d.Type ?? string.Empty).ToLowerInvariant();
                                var nameLower = (d.Name ?? string.Empty).ToLowerInvariant();
                                // Web detection: explicit name contains "web" or "web player"
                                if (nameLower.Contains("web"))
                                {
                                    glyph = "\uE774"; // Globe (web)
                                }
                                else if (type == "computer")
                                {
                                    glyph = "\uE7F8"; // PC
                                }
                                else if (type == "smartphone" || type == "tablet")
                                {
                                    glyph = "\uE8EA"; // Phone
                                }
                                else
                                {
                                    // Leave null or use a generic device/speaker icon (optional)
                                    glyph = null; // "\uE71B";
                                }
                            }
                            catch { }
                            
                            if (existingItem != null)
                            {
                                // Update existing item
                                existingItem.Header = deviceName;
                                existingItem.IsChecked = isActive;
                                existingItem.IconGlyph = glyph;
                                System.Diagnostics.Debug.WriteLine($"LoadDevicesMenuAsync: Updated existing device '{deviceName}'");
                            }
                            else
                            {
                                // Add new device
                                var newItem = new MenuItemViewModel(deviceName,
                                    new RelayCommand<MenuItemViewModel>(async _ => await OnPickDeviceAsync(d.Id)))
                                {
                                    Id = d.Id,
                                    IsChecked = isActive,
                                    IconGlyph = glyph
                                };
                                root.MenuItems.Add(newItem);
                                System.Diagnostics.Debug.WriteLine($"LoadDevicesMenuAsync: Added new device '{deviceName}' to menu");
                            }
                        }
                    }
                    else
                    {
                        // Clear menu only if no devices at all
                        if (root.MenuItems.Count > 0)
                        {
                            root.MenuItems.Clear();
                            System.Diagnostics.Debug.WriteLine("Cleared devices menu - no devices available");
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"!!! ERROR in LoadDevicesMenuAsync: {ex.Message} !!!");
                System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
            }
        }

        // Public wrapper to refresh both the Devices menu and the "Play to" submenu devices
        public async Task RefreshDevicesMenuAsync()
        {
            if (_isRefreshingDevicesMenu) return;
            _isRefreshingDevicesMenu = true;
            try
            {
                // Refresh main Devices menu
                await LoadDevicesMenuAsync();
                // Also refresh the devices collection used by the Play To submenu (right-click menus)
                if (_playlistsPageViewModel != null)
                {
                    await _playlistsPageViewModel.RefreshDevicesForTracksMenuAsync();
                }
            }
            finally
            {
                _isRefreshingDevicesMenu = false;
            }
        }

        /// <summary>
        /// Handle periodic devices refresh timer tick
        /// </summary>
        private async void OnDevicesRefreshTimerTick(object? sender, EventArgs e)
        {
            try
            {
                // Refresh both the top menu and the Play To submenu in real-time
                await RefreshDevicesMenuAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in periodic devices refresh: {ex.Message}");
            }
        }

        private async Task OnPickDeviceAsync(string? deviceId)
        {
            if (string.IsNullOrWhiteSpace(deviceId)) return;
            try
            {
                await _spotify.TransferPlaybackAsync(new[] { deviceId }, true).ConfigureAwait(false);
                // Try to confirm active device with a short poll, as Spotify may update state asynchronously
                var becameActive = await WaitForActiveDeviceAsync(deviceId, attempts: 6, delayMs: 500).ConfigureAwait(false);
                if (becameActive)
                {
                    System.Diagnostics.Debug.WriteLine($"Playback transferred to device {deviceId} (confirmed)." );
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"Playback transfer requested for device {deviceId}. State may take a second to update.");
                }
                // Refresh menu checks in both locations (Devices menu + Play To submenu)
                await RefreshDevicesMenuAsync().ConfigureAwait(false);
            }
            catch (SpotifyAPI.Web.APIException apiEx)
            {
                // Common case: Not Found (device became unavailable) — show friendly note and log details
                var msg = $"Could not transfer playback: {apiEx.Message}. The target device may be offline or no longer available.";
                System.Diagnostics.Debug.WriteLine(msg);
                _mb.ShowMessageBox(msg, "Transfer Playback", MessageBoxButton.OK, MessageBoxIcon.Warning);
            }
            catch (System.Exception ex)
            {
                var msg = $"Unexpected error during transfer playback: {ex.Message}";
                System.Diagnostics.Debug.WriteLine(msg);
                _mb.ShowMessageBox(msg, "Transfer Playback", MessageBoxButton.OK, MessageBoxIcon.Error);
            }
        }

        // Poll the current playback until the expected device becomes active, or timeout.
        private async Task<bool> WaitForActiveDeviceAsync(string expectedDeviceId, int attempts = 5, int delayMs = 500)
        {
            for (var i = 0; i < attempts; i++)
            {
                var devices = await _spotify.GetDevicesAsync().ConfigureAwait(false);
                var activeId = devices?.FirstOrDefault(d => d.IsActive)?.Id;
                if (!string.IsNullOrWhiteSpace(activeId) && string.Equals(activeId, expectedDeviceId))
                {
                    return true;
                }
                await Task.Delay(delayMs).ConfigureAwait(false);
            }
            return false;
        }

        private void ShowDebugWindow()
        {
            try
            {
                var debugWindow = new SpotifyWPF.View.DebugWindow();
                debugWindow.Show();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to show debug window: {ex.Message}");
                _mb.ShowMessageBox($"Failed to open debug window: {ex.Message}", "Debug Window Error", MessageBoxButton.OK, MessageBoxIcon.Error);
            }
        }
    }
}
