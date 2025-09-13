using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using GalaSoft.MvvmLight;
using GalaSoft.MvvmLight.Command;

namespace SpotifyWPF.View
{
    /// <summary>
    /// Interaction logic for DebugWindow.xaml
    /// </summary>
    public partial class DebugWindow : Window, INotifyPropertyChanged
    {
        private readonly StringBuilder _debugBuffer = new StringBuilder();
        private readonly DispatcherTimer _uiUpdateTimer;
        private string _debugText = string.Empty;
        private string _statusText = "Ready";
        private int _messageCount = 0;
        private bool _autoScrollEnabled = true;

        public DebugWindow()
        {
            InitializeComponent();
            DataContext = this;

            // Initialize commands
            ClearCommand = new RelayCommand(Clear);
            CopyAllCommand = new RelayCommand(CopyAll);

            // Set up UI update timer to batch updates
            _uiUpdateTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(100)
            };
            _uiUpdateTimer.Tick += OnUiUpdateTimerTick;

            // Subscribe to debug messages
            DebugMessageManager.Instance.DebugMessageReceived += OnDebugMessageReceived;

            // Load any existing messages
            DebugText = DebugMessageManager.Instance.GetAllMessages();
            MessageCount = DebugMessageManager.Instance.MessageCount;
        }

        public RelayCommand ClearCommand { get; }
        public RelayCommand CopyAllCommand { get; }

        public string DebugText
        {
            get => _debugText;
            set
            {
                _debugText = value;
                OnPropertyChanged();
            }
        }

        public string StatusText
        {
            get => _statusText;
            set
            {
                _statusText = value;
                OnPropertyChanged();
            }
        }

        public int MessageCount
        {
            get => _messageCount;
            set
            {
                _messageCount = value;
                OnPropertyChanged();
            }
        }

        public bool AutoScrollEnabled
        {
            get => _autoScrollEnabled;
            set
            {
                _autoScrollEnabled = value;
                OnPropertyChanged();
            }
        }

        private void OnDebugMessageReceived(object? sender, DebugMessageEventArgs e)
        {
            // Update UI on dispatcher thread
            Dispatcher.Invoke(() =>
            {
                DebugText = DebugMessageManager.Instance.GetAllMessages();
                MessageCount = DebugMessageManager.Instance.MessageCount;

                if (AutoScrollEnabled)
                {
                    ScrollViewer.ScrollToEnd();
                }
            });
        }

        private void OnUiUpdateTimerTick(object? sender, EventArgs e)
        {
            // Batch UI updates
            DebugText = DebugMessageManager.Instance.GetAllMessages();
            MessageCount = DebugMessageManager.Instance.MessageCount;

            if (AutoScrollEnabled)
            {
                ScrollViewer.ScrollToEnd();
            }
        }

        private void Clear()
        {
            DebugMessageManager.Instance.Clear();
            DebugText = string.Empty;
            MessageCount = 0;
            StatusText = "Debug log cleared";
        }

        private void CopyAll()
        {
            try
            {
                Clipboard.SetText(DebugText);
                StatusText = "Debug log copied to clipboard";
            }
            catch (Exception ex)
            {
                StatusText = $"Failed to copy: {ex.Message}";
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);

            // Unsubscribe from events
            DebugMessageManager.Instance.DebugMessageReceived -= OnDebugMessageReceived;
            _uiUpdateTimer.Stop();
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}