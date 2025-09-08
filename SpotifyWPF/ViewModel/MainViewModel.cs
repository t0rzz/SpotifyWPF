using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Reflection;
using MessageBoxButton = SpotifyWPF.Service.MessageBoxes.MessageBoxButton;
using MessageBoxResult = SpotifyWPF.Service.MessageBoxes.MessageBoxResult;
using MessageBoxIcon = SpotifyWPF.Service.MessageBoxes.MessageBoxIcon;
using GalaSoft.MvvmLight;
using GalaSoft.MvvmLight.Command;
using SpotifyWPF.Service;
using SpotifyWPF.Service.MessageBoxes;
using SpotifyWPF.ViewModel.Component;
using SpotifyWPF.ViewModel.Page;

namespace SpotifyWPF.ViewModel
{
    public class MainViewModel : ViewModelBase
    {
    private readonly SearchPageViewModel _searchPageViewModel;
    private readonly ISpotify _spotify;
    private readonly IMessageBoxService _mb;
    private readonly LoginPageViewModel _loginPageViewModel;
        private readonly PlaylistsPageViewModel _playlistsPageViewModel;

        private ViewModelBase? _currentPage;

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
                new MenuItemViewModel("Devices", new RelayCommand(async () => await LoadDevicesMenuAsync()))
                {
                    MenuItems = new ObservableCollection<MenuItemViewModel>()
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
            _searchPageViewModel.TracksDataGridViewModel.SetLogAction(_playlistsPageViewModel.AddOutput);
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
                    _searchPageViewModel.TracksDataGridViewModel.SetLogAction(_playlistsPageViewModel.AddOutput);
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
            var asm = Assembly.GetExecutingAssembly();
            var infoVer = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                           ?? asm.GetName().Version?.ToString();

            var text =
                "SpotifyWPF – Unofficial power tools for Spotify\n\n" +
                $"Version: {infoVer}\n" +
                "Author: t0rzz (maintainer)\n" +
                "Original project: MrPnut/SpotifyWPF\n" +
                "Repository: https://github.com/t0rzz/SpotifyWPF\n\n" +
                "This is a community project and is not affiliated with Spotify.";

            _mb.ShowMessageBox(text, "About", MessageBoxButton.OK, MessageBoxIcon.Information);
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

            var devices = await _spotify.GetDevicesAsync().ConfigureAwait(false);
            var playback = await _spotify.GetCurrentPlaybackAsync().ConfigureAwait(false);
            var activeId = playback?.Device?.Id;

            // Update on UI thread
            Application.Current.Dispatcher.Invoke(() =>
            {
                root.MenuItems.Clear();
                foreach (var d in devices)
                {
                    var item = new MenuItemViewModel(string.IsNullOrWhiteSpace(d.Name) ? d.Id : d.Name,
                        new RelayCommand<MenuItemViewModel>(async _ => await OnPickDeviceAsync(d.Id)))
                    {
                        Id = d.Id,
                        IsChecked = !string.IsNullOrWhiteSpace(activeId) && string.Equals(d.Id, activeId)
                    };
                    root.MenuItems.Add(item);
                }
            });
        }

        // Public wrapper to refresh devices menu on demand (e.g., when submenu opens)
        public Task RefreshDevicesMenuAsync()
        {
            return LoadDevicesMenuAsync();
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
                    _playlistsPageViewModel?.AddOutput($"Playback transferred to device {deviceId} (confirmed)." );
                }
                else
                {
                    _playlistsPageViewModel?.AddOutput($"Playback transfer requested for device {deviceId}. State may take a second to update.");
                }
                // Refresh menu checks
                await LoadDevicesMenuAsync().ConfigureAwait(false);
            }
            catch (SpotifyAPI.Web.APIException apiEx)
            {
                // Common case: Not Found (device became unavailable) — show friendly note and log details
                var msg = $"Could not transfer playback: {apiEx.Message}. The target device may be offline or no longer available.";
                _playlistsPageViewModel?.AddOutput(msg);
                _mb.ShowMessageBox(msg, "Transfer Playback", MessageBoxButton.OK, MessageBoxIcon.Warning);
            }
            catch (System.Exception ex)
            {
                var msg = $"Unexpected error during transfer playback: {ex.Message}";
                _playlistsPageViewModel?.AddOutput(msg);
                _mb.ShowMessageBox(msg, "Transfer Playback", MessageBoxButton.OK, MessageBoxIcon.Error);
            }
        }

        // Poll the current playback until the expected device becomes active, or timeout.
        private async Task<bool> WaitForActiveDeviceAsync(string expectedDeviceId, int attempts = 5, int delayMs = 500)
        {
            for (var i = 0; i < attempts; i++)
            {
                var playback = await _spotify.GetCurrentPlaybackAsync().ConfigureAwait(false);
                var activeId = playback?.Device?.Id;
                if (!string.IsNullOrWhiteSpace(activeId) && string.Equals(activeId, expectedDeviceId))
                {
                    return true;
                }
                await Task.Delay(delayMs).ConfigureAwait(false);
            }
            return false;
        }
    }
}