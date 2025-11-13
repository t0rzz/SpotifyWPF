using System;
using System.Drawing;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Media.Imaging;
using SpotifyWPF.ViewModel;

namespace SpotifyWPF.Service
{
    /// <summary>
    /// System tray icon manager for background playback and window management
    /// Provides minimize-to-tray functionality with playback controls
    /// </summary>
    public class TrayIconManager : IDisposable
    {
        private static TrayIconManager? _instance;
        private static int _instanceCount = 0;
        private readonly PlayerViewModel _playerViewModel;
        private bool _disposed = false;
        private NotifyIcon? _notifyIcon;
        private ContextMenuStrip? _contextMenu;
        private bool _isMinimizedToTray = false;
        private WindowState _previousWindowState = WindowState.Normal;

        // Menu items for easy access
        private ToolStripMenuItem? _showHideMenuItem;
        private ToolStripMenuItem? _playPauseMenuItem;
        private ToolStripMenuItem? _nextMenuItem;
        private ToolStripMenuItem? _previousMenuItem;
        private ToolStripMenuItem? _exitMenuItem;

        private TrayIconManager(PlayerViewModel playerViewModel)
        {
            _instanceCount++;

            _playerViewModel = playerViewModel ?? throw new ArgumentNullException(nameof(playerViewModel));

            InitializeTrayIcon();

        }

        public static TrayIconManager GetInstance(PlayerViewModel playerViewModel)
        {
            if (_instance == null)
            {
                _instance = new TrayIconManager(playerViewModel);
            }
            return _instance;
        }

        private void InitializeTrayIcon()
        {
            try
            {
                // Create the notify icon
                _notifyIcon = new NotifyIcon
                {
                    Text = $"Spotify WPF Player (PID {System.Diagnostics.Process.GetCurrentProcess().Id})",
                    Visible = true,
                    Icon = GetApplicationIcon()
                };

                // Create context menu
                CreateContextMenu();

                // Set up event handlers
                _notifyIcon.DoubleClick += OnTrayIconDoubleClick;
                _notifyIcon.ContextMenuStrip = _contextMenu;

                // Subscribe to player state changes to update menu
                _playerViewModel.PropertyChanged += OnPlayerViewModelPropertyChanged;

            }
            catch
            {
                // Don't throw - tray icon is not critical functionality
            }
        }

        private void CreateContextMenu()
        {
            _contextMenu = new ContextMenuStrip();

            // Show/Hide menu item
            _showHideMenuItem = new ToolStripMenuItem("Show", null, OnShowHideClick);
            _contextMenu.Items.Add(_showHideMenuItem);

            _contextMenu.Items.Add(new ToolStripSeparator());

            // Playback controls
            _playPauseMenuItem = new ToolStripMenuItem("Play", null, OnPlayPauseClick);
            _contextMenu.Items.Add(_playPauseMenuItem);

            _nextMenuItem = new ToolStripMenuItem("Next", null, OnNextClick);
            _contextMenu.Items.Add(_nextMenuItem);

            _previousMenuItem = new ToolStripMenuItem("Previous", null, OnPreviousClick);
            _contextMenu.Items.Add(_previousMenuItem);

            _contextMenu.Items.Add(new ToolStripSeparator());

            // Exit menu item
            _exitMenuItem = new ToolStripMenuItem("Exit", null, OnExitClick);
            _contextMenu.Items.Add(_exitMenuItem);

            // Update initial menu state
            UpdateMenuState();
        }

        private Icon GetApplicationIcon()
        {
            try
            {
                // Try to get the application icon from the main window
                var mainWindow = System.Windows.Application.Current.MainWindow;
                if (mainWindow != null && mainWindow.Icon != null)
                {
                    // Convert WPF BitmapSource to Windows Forms Icon
                    var bitmapSource = mainWindow.Icon as BitmapSource;
                    if (bitmapSource != null)
                    {
                        return Icon.FromHandle(GetIconHandle(bitmapSource));
                    }
                }

                // Fallback: try to load from application resources
                var iconUri = new Uri("pack://application:,,,/icon.ico", UriKind.Absolute);
                var iconStream = System.Windows.Application.GetResourceStream(iconUri)?.Stream;
                if (iconStream != null)
                {
                    return new Icon(iconStream);
                }

                // Final fallback: use system default icon
                return SystemIcons.Application;
            }
            catch
            {
                return SystemIcons.Application;
            }
        }

        private IntPtr GetIconHandle(BitmapSource bitmapSource)
        {
            try
            {
                // Convert BitmapSource to Bitmap
                var bitmap = new Bitmap(bitmapSource.PixelWidth, bitmapSource.PixelHeight);
                var bitmapData = bitmap.LockBits(
                    new Rectangle(0, 0, bitmap.Width, bitmap.Height),
                    System.Drawing.Imaging.ImageLockMode.WriteOnly,
                    System.Drawing.Imaging.PixelFormat.Format32bppPArgb);

                bitmapSource.CopyPixels(
                    Int32Rect.Empty,
                    bitmapData.Scan0,
                    bitmapData.Height * bitmapData.Stride,
                    bitmapData.Stride);

                bitmap.UnlockBits(bitmapData);

                // Convert to icon
                return bitmap.GetHicon();
            }
            catch
            {
                return IntPtr.Zero;
            }
        }

        private void UpdateMenuState()
        {
            if (_showHideMenuItem == null || _playPauseMenuItem == null) return;

            try
            {
                // Update Show/Hide text based on window state
                var mainWindow = System.Windows.Application.Current.MainWindow;
                if (mainWindow != null)
                {
                    bool isVisible = mainWindow.IsVisible && mainWindow.WindowState != WindowState.Minimized;
                    _showHideMenuItem.Text = isVisible ? "Hide" : "Show";
                }

                // Update Play/Pause text based on playback state
                _playPauseMenuItem.Text = _playerViewModel.IsPlaying ? "Pause" : "Play";

                // Update tray icon tooltip with current track info
                if (_notifyIcon != null)
                {
                    string tooltip = "Spotify WPF Player";
                    if (_playerViewModel.CurrentTrack != null)
                    {
                        tooltip = $"{_playerViewModel.CurrentTrack.Title} - {_playerViewModel.CurrentTrack.Artist}";
                        if (tooltip.Length > 63) // Windows tooltip limit
                        {
                            tooltip = tooltip.Substring(0, 60) + "...";
                        }
                    }
                    _notifyIcon.Text = tooltip;
                }
            }
            catch
            {
            }
        }

        #region Event Handlers

        private void OnTrayIconDoubleClick(object? sender, EventArgs e)
        {
            ShowFromTray();
        }

        private void OnShowHideClick(object? sender, EventArgs e)
        {
            var mainWindow = System.Windows.Application.Current.MainWindow;
            if (mainWindow != null)
            {
                if (mainWindow.IsVisible && mainWindow.WindowState != WindowState.Minimized)
                {
                    HideToTray();
                }
                else
                {
                    ShowFromTray();
                }
            }
        }

        private void OnPlayPauseClick(object? sender, EventArgs e)
        {
            try
            {
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    _playerViewModel.PlayPauseCommand.Execute(null);
                });
            }
            catch
            {
            }
        }

        private void OnNextClick(object? sender, EventArgs e)
        {
            try
            {
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    _playerViewModel.NextCommand.Execute(null);
                });
            }
            catch
            {
            }
        }

        private void OnPreviousClick(object? sender, EventArgs e)
        {
            try
            {
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    _playerViewModel.PrevCommand.Execute(null);
                });
            }
            catch
            {
            }
        }

        private void OnExitClick(object? sender, EventArgs e)
        {
            try
            {
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    System.Windows.Application.Current.Shutdown();
                });
            }
            catch
            {
                // Force exit as fallback
                Environment.Exit(0);
            }
        }

        private void OnPlayerViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            // Update menu state when playback state changes
            if (e.PropertyName == nameof(_playerViewModel.IsPlaying) ||
                e.PropertyName == nameof(_playerViewModel.CurrentTrack))
            {
                UpdateMenuState();
            }
        }

        #endregion

        /// <summary>
        /// Hide the main window to system tray
        /// </summary>
        public void HideToTray()
        {
            if (_disposed) return;

            try
            {
                var mainWindow = System.Windows.Application.Current.MainWindow;
                if (mainWindow != null)
                {
                    // Save the current window state before hiding
                    _previousWindowState = mainWindow.WindowState;
                    
                    mainWindow.Hide();
                    _isMinimizedToTray = true;

                    // Show balloon tip on first minimize to tray
                    if (_notifyIcon != null && !_notifyIcon.Visible)
                    {
                        _notifyIcon.Visible = true;
                        _notifyIcon.ShowBalloonTip(2000, "Spotify WPF", "Application minimized to tray", ToolTipIcon.Info);
                    }

                    UpdateMenuState();
                }
            }
            catch
            {
            }
        }

        /// <summary>
        /// Show the main window from system tray
        /// </summary>
        public void ShowFromTray()
        {
            if (_disposed) return;

            try
            {
                var mainWindow = System.Windows.Application.Current.MainWindow;
                if (mainWindow != null)
                {
                    mainWindow.Show();
                    // Restore the previous window state (could be Normal or Maximized)
                    mainWindow.WindowState = _previousWindowState;
                    mainWindow.Activate();
                    _isMinimizedToTray = false;

                    UpdateMenuState();
                }
            }
            catch
            {
            }
        }

        /// <summary>
        /// Handle window state changes (integrate with main window)
        /// </summary>
        public void HandleWindowStateChanged(WindowState newState)
        {
            if (_disposed) return;

            // Auto-hide to tray when minimized if user preference allows
            if (newState == WindowState.Minimized && !_isMinimizedToTray)
            {
                // Optional: could add a setting to control auto-minimize behavior
                // For now, just update menu state
                UpdateMenuState();
            }
        }

        /// <summary>
        /// Dispose resources used by the TrayIconManager
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Finalizer for safety net
        /// </summary>
        ~TrayIconManager()
        {
            Dispose(false);
        }

        /// <summary>
        /// Dispose implementation
        /// </summary>
        protected virtual void Dispose(bool disposing)
        {
            if (_disposed)
                return;

            if (disposing)
            {
                try
                {
                    // Unsubscribe from events
                    if (_playerViewModel != null)
                    {
                        _playerViewModel.PropertyChanged -= OnPlayerViewModelPropertyChanged;
                    }

                    // Dispose context menu
                    if (_contextMenu != null)
                    {
                        _contextMenu.Dispose();
                        _contextMenu = null;
                    }

                    // Dispose notify icon
                    if (_notifyIcon != null)
                    {
                        _notifyIcon.Visible = false;
                        _notifyIcon.Dispose();
                        _notifyIcon = null;
                    }

                }
                catch
                {
                }
            }

            _disposed = true;
        }
    }
}
