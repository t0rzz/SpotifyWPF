using System.ComponentModel;
using GalaSoft.MvvmLight;
using SpotifyWPF.Model;

namespace SpotifyWPF.ViewModel
{
    /// <summary>
    /// ViewModel for the status bar that displays current playback device information
    /// </summary>
    public class StatusBarViewModel : ViewModelBase
    {
        private readonly MainViewModel _mainViewModel;
        private DeviceModel? _currentDevice;

        public StatusBarViewModel(MainViewModel mainViewModel)
        {
            _mainViewModel = mainViewModel ?? throw new ArgumentNullException(nameof(mainViewModel));

            // Subscribe to property changes on the MainViewModel to detect when Player is set
            _mainViewModel.PropertyChanged += OnMainViewModelPropertyChanged;

            // Subscribe to property changes on the PlayerViewModel to update current device
            if (_mainViewModel.Player != null)
            {
                _mainViewModel.Player.PropertyChanged += OnPlayerPropertyChanged;
            }

            // Initialize with current device
            UpdateCurrentDevice();
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
            CurrentDevice = _mainViewModel.Player?.SelectedDevice;
            RaisePropertyChanged(nameof(CurrentDeviceText));
        }
    }
}