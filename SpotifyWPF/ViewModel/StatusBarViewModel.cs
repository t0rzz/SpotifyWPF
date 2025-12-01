using System.ComponentModel;
using GalaSoft.MvvmLight;
using SpotifyWPF.Model;
using SpotifyWPF.Service;

namespace SpotifyWPF.ViewModel
{
    /// <summary>
    /// ViewModel for the status bar that displays current playback device information
    /// </summary>
    public class StatusBarViewModel : ViewModelBase
    {
        private readonly MainViewModel _mainViewModel;
        private readonly ISettingsProvider _settingsProvider;
        private DeviceModel? _currentDevice;
        private string _currentClientId = string.Empty;

        public StatusBarViewModel(MainViewModel mainViewModel)
        {
            _mainViewModel = mainViewModel ?? throw new ArgumentNullException(nameof(mainViewModel));
            _settingsProvider = new SettingsProvider(); // Create settings provider to access client_id

            // Subscribe to property changes on the MainViewModel to detect when Player is set
            _mainViewModel.PropertyChanged += OnMainViewModelPropertyChanged;

            // Subscribe to property changes on the PlayerViewModel to update current device
            if (_mainViewModel.Player != null)
            {
                _mainViewModel.Player.PropertyChanged += OnPlayerPropertyChanged;
            }

            // Initialize with current device and client_id
            UpdateCurrentDevice();
            UpdateCurrentClientId();
        }

        /// <summary>
        /// Gets or sets the current playback device
        /// </summary>
        public DeviceModel? CurrentDevice
        {
            get => _currentDevice;
            set
            {
                if (SetProperty(ref _currentDevice, value))
                {
                    RaisePropertyChanged(nameof(CurrentDeviceText));
                }
            }
        }

        /// <summary>
        /// Gets the display text for the current device
        /// </summary>
        public string CurrentDeviceText
        {
            get
            {
                if (CurrentDevice == null)
                    return "No device selected";

                return $"Playing on {CurrentDevice.Name}";
            }
        }

        /// <summary>
        /// Gets the current Spotify Client ID being used
        /// </summary>
        public string CurrentClientId
        {
            get => _currentClientId;
            private set => SetProperty(ref _currentClientId, value);
        }

        /// <summary>
        /// Gets the display text for the current client ID
        /// </summary>
        public string CurrentClientIdText
        {
            get
            {
                if (string.IsNullOrWhiteSpace(CurrentClientId))
                    return string.Empty;

                // Mask the client ID for security (show first 8 and last 4 characters)
                if (CurrentClientId.Length >= 12)
                {
                    return $"Client ID: {CurrentClientId.Substring(0, 8)}...{CurrentClientId.Substring(CurrentClientId.Length - 4)}";
                }
                else
                {
                    return $"Client ID: {CurrentClientId}";
                }
            }
        }

        private void OnMainViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MainViewModel.Player))
            {
                // Unsubscribe from old player if any
                if (_mainViewModel.Player != null)
                {
                    _mainViewModel.Player.PropertyChanged -= OnPlayerPropertyChanged;
                }

                // Subscribe to new player
                if (_mainViewModel.Player != null)
                {
                    _mainViewModel.Player.PropertyChanged += OnPlayerPropertyChanged;
                }

                // Update current device
                UpdateCurrentDevice();
            }
        }

        private void OnPlayerPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(PlayerViewModel.SelectedDevice))
            {
                UpdateCurrentDevice();
            }
        }

        private void UpdateCurrentDevice()
        {
            var player = _mainViewModel.Player;
            var selectedDevice = player?.SelectedDevice;
            LoggingService.LogToFile($"StatusBar: UpdateCurrentDevice called, Player is {(player!=null?"set":"null")}, SelectedDevice={( selectedDevice?.Name ?? "<null>")}\n");

            // Ensure UI updates happen on the UI thread
            if (System.Windows.Application.Current != null && !System.Windows.Application.Current.Dispatcher.CheckAccess())
            {
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    CurrentDevice = selectedDevice;
                    RaisePropertyChanged(nameof(CurrentDeviceText));
                });
            }
            else
            {
                CurrentDevice = selectedDevice;
                RaisePropertyChanged(nameof(CurrentDeviceText));
            }
        }

        private void UpdateCurrentClientId()
        {
            var clientId = _settingsProvider.SpotifyClientId;
            LoggingService.LogToFile($"StatusBar: UpdateCurrentClientId called, ClientId={(string.IsNullOrWhiteSpace(clientId) ? "<empty>" : clientId)}\n");

            // Ensure UI updates happen on the UI thread
            if (System.Windows.Application.Current != null && !System.Windows.Application.Current.Dispatcher.CheckAccess())
            {
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    CurrentClientId = clientId;
                    RaisePropertyChanged(nameof(CurrentClientIdText));
                });
            }
            else
            {
                CurrentClientId = clientId;
                RaisePropertyChanged(nameof(CurrentClientIdText));
            }
        }
    }
}