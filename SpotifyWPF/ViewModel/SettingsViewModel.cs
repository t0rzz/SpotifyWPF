using GalaSoft.MvvmLight;
using System;
using System.ComponentModel;
using SpotifyWPF.Service;

namespace SpotifyWPF.ViewModel
{
    public class SettingsViewModel : ViewModelBase
    {
        private int _maxThreadsForOperations;
        private string _defaultMarket = string.Empty;
        private bool _minimizeToTrayOnClose;
        private readonly ISettingsProvider _settingsProvider;

        public SettingsViewModel(ISettingsProvider? settingsProvider = null)
        {
            _settingsProvider = settingsProvider ?? new SettingsProvider();
            LoadSettings();
        }

        public int MaxThreadsForOperations
        {
            get => _maxThreadsForOperations;
            set
            {
                _maxThreadsForOperations = value;
                RaisePropertyChanged();
                RaisePropertyChanged(nameof(IsMaxThreadsValid));
            }
        }

        public string DefaultMarket
        {
            get => _defaultMarket;
            set
            {
                _defaultMarket = value;
                RaisePropertyChanged();
            }
        }

        public bool MinimizeToTrayOnClose
        {
            get => _minimizeToTrayOnClose;
            set
            {
                _minimizeToTrayOnClose = value;
                RaisePropertyChanged();
            }
        }

        public bool IsMaxThreadsValid => _maxThreadsForOperations >= 1 && _maxThreadsForOperations <= 10;

        public void LoadSettings()
        {
            // Load from application settings, default to 3 threads
            _maxThreadsForOperations = (int)Properties.Settings.Default["MaxThreadsForOperations"];
            if (_maxThreadsForOperations < 1 || _maxThreadsForOperations > 10)
            {
                _maxThreadsForOperations = 3; // Default value
            }
            RaisePropertyChanged(nameof(MaxThreadsForOperations));

            // Load DefaultMarket setting
            _defaultMarket = Properties.Settings.Default.DefaultMarket ?? string.Empty;
            RaisePropertyChanged(nameof(DefaultMarket));

            // Load MinimizeToTrayOnClose setting
            _minimizeToTrayOnClose = Properties.Settings.Default.MinimizeToTrayOnClose;
            RaisePropertyChanged(nameof(MinimizeToTrayOnClose));
        }

        public bool SaveSettings()
        {
            try
            {
                // Validate and correct input
                if (_maxThreadsForOperations < 1 || _maxThreadsForOperations > 10)
                {
                    _maxThreadsForOperations = Math.Max(1, Math.Min(10, _maxThreadsForOperations));
                    RaisePropertyChanged(nameof(MaxThreadsForOperations));
                }

                // Save to application settings
                Properties.Settings.Default["MaxThreadsForOperations"] = _maxThreadsForOperations;
                Properties.Settings.Default["DefaultMarket"] = _defaultMarket ?? string.Empty;
                Properties.Settings.Default["MinimizeToTrayOnClose"] = _minimizeToTrayOnClose;
                Properties.Settings.Default.Save();

                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}