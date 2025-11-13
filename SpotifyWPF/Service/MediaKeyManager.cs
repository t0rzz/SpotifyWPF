using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using SpotifyWPF.ViewModel;

namespace SpotifyWPF.Service
{
    /// <summary>
    /// Global media key manager for enhanced user experience
    /// Handles system-wide media keys (play/pause, next, previous, volume)
    /// </summary>
    public class MediaKeyManager : IDisposable
    {
        private readonly PlayerViewModel _playerViewModel;
        private bool _disposed = false;
        private HwndSource? _hwndSource;
        private IntPtr _hwnd;

        // Windows API constants for media keys
        private const int WM_HOTKEY = 0x0312;
        private const int VK_MEDIA_PLAY_PAUSE = 0xB3;
        private const int VK_MEDIA_NEXT_TRACK = 0xB0;
        private const int VK_MEDIA_PREV_TRACK = 0xB1;
        private const int VK_VOLUME_UP = 0xAF;
        private const int VK_VOLUME_DOWN = 0xAE;
        private const int VK_VOLUME_MUTE = 0xAD;

        // Hotkey IDs
        private const int HOTKEY_PLAY_PAUSE = 1;
        private const int HOTKEY_NEXT_TRACK = 2;
        private const int HOTKEY_PREV_TRACK = 3;
        private const int HOTKEY_VOLUME_UP = 4;
        private const int HOTKEY_VOLUME_DOWN = 5;
        private const int HOTKEY_VOLUME_MUTE = 6;

        // Windows API imports
        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        public MediaKeyManager(PlayerViewModel playerViewModel)
        {
            _playerViewModel = playerViewModel ?? throw new ArgumentNullException(nameof(playerViewModel));

            // Create a hidden window to receive hotkey messages
            InitializeHotkeys();

            System.Diagnostics.Debug.WriteLine("MediaKeyManager initialized with global hotkey support");
        }

        private void InitializeHotkeys()
        {
            try
            {
                // Create a hidden window for receiving hotkey messages
                var window = new Window
                {
                    Width = 0,
                    Height = 0,
                    WindowStyle = WindowStyle.None,
                    ShowInTaskbar = false,
                    Visibility = Visibility.Hidden
                };

                // Show the window briefly to get a handle, then hide it
                window.Show();
                window.Hide();

                _hwndSource = HwndSource.FromHwnd(new WindowInteropHelper(window).Handle);
                _hwnd = _hwndSource.Handle;

                if (_hwndSource != null)
                {
                    _hwndSource.AddHook(WndProc);
                }

                // Register global media hotkeys
                RegisterHotKey(_hwnd, HOTKEY_PLAY_PAUSE, 0, VK_MEDIA_PLAY_PAUSE);
                RegisterHotKey(_hwnd, HOTKEY_NEXT_TRACK, 0, VK_MEDIA_NEXT_TRACK);
                RegisterHotKey(_hwnd, HOTKEY_PREV_TRACK, 0, VK_MEDIA_PREV_TRACK);
                RegisterHotKey(_hwnd, HOTKEY_VOLUME_UP, 0, VK_VOLUME_UP);
                RegisterHotKey(_hwnd, HOTKEY_VOLUME_DOWN, 0, VK_VOLUME_DOWN);
                RegisterHotKey(_hwnd, HOTKEY_VOLUME_MUTE, 0, VK_VOLUME_MUTE);

                System.Diagnostics.Debug.WriteLine("Global media hotkeys registered successfully");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to initialize media hotkeys: {ex.Message}");
            }
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_HOTKEY)
            {
                int hotkeyId = wParam.ToInt32();
                HandleMediaKey(hotkeyId);
                handled = true;
            }

            return IntPtr.Zero;
        }

        private void HandleMediaKey(int hotkeyId)
        {
            try
            {
                switch (hotkeyId)
                {
                    case HOTKEY_PLAY_PAUSE:
                        System.Diagnostics.Debug.WriteLine("🎵 Global hotkey: Play/Pause");
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            _playerViewModel.PlayPauseCommand.Execute(null);
                        });
                        break;

                    case HOTKEY_NEXT_TRACK:
                        System.Diagnostics.Debug.WriteLine("🎵 Global hotkey: Next Track");
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            _playerViewModel.NextCommand.Execute(null);
                        });
                        break;

                    case HOTKEY_PREV_TRACK:
                        System.Diagnostics.Debug.WriteLine("🎵 Global hotkey: Previous Track");
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            _playerViewModel.PrevCommand.Execute(null);
                        });
                        break;

                    case HOTKEY_VOLUME_UP:
                        System.Diagnostics.Debug.WriteLine("🎵 Global hotkey: Volume Up");
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            // Increase volume by 10%
                            var newVolume = Math.Min(1.0, _playerViewModel.Volume + 0.1);
                            _playerViewModel.Volume = newVolume;
                        });
                        break;

                    case HOTKEY_VOLUME_DOWN:
                        System.Diagnostics.Debug.WriteLine("🎵 Global hotkey: Volume Down");
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            // Decrease volume by 10%
                            var newVolume = Math.Max(0.0, _playerViewModel.Volume - 0.1);
                            _playerViewModel.Volume = newVolume;
                        });
                        break;

                    case HOTKEY_VOLUME_MUTE:
                        System.Diagnostics.Debug.WriteLine("🎵 Global hotkey: Volume Mute/Unmute");
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            // Toggle mute by setting volume to 0 or restoring previous volume
                            if (_playerViewModel.Volume > 0)
                            {
                                _lastVolumeBeforeMute = _playerViewModel.Volume;
                                _playerViewModel.Volume = 0.0;
                            }
                            else
                            {
                                _playerViewModel.Volume = _lastVolumeBeforeMute > 0 ? _lastVolumeBeforeMute : 0.5;
                            }
                        });
                        break;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error handling media key {hotkeyId}: {ex.Message}");
            }
        }

        private double _lastVolumeBeforeMute = 0.5;

        /// <summary>
        /// Dispose resources used by the MediaKeyManager
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Finalizer for safety net
        /// </summary>
        ~MediaKeyManager()
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
                    // Unregister all hotkeys
                    if (_hwnd != IntPtr.Zero)
                    {
                        UnregisterHotKey(_hwnd, HOTKEY_PLAY_PAUSE);
                        UnregisterHotKey(_hwnd, HOTKEY_NEXT_TRACK);
                        UnregisterHotKey(_hwnd, HOTKEY_PREV_TRACK);
                        UnregisterHotKey(_hwnd, HOTKEY_VOLUME_UP);
                        UnregisterHotKey(_hwnd, HOTKEY_VOLUME_DOWN);
                        UnregisterHotKey(_hwnd, HOTKEY_VOLUME_MUTE);
                    }

                    // Remove the hook and dispose the window
                    if (_hwndSource != null)
                    {
                        _hwndSource.RemoveHook(WndProc);
                        _hwndSource.Dispose();
                        _hwndSource = null;
                    }

                    System.Diagnostics.Debug.WriteLine("MediaKeyManager disposed - hotkeys unregistered");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error disposing MediaKeyManager: {ex.Message}");
                }
            }

            _disposed = true;
        }
    }

    public enum MediaKey
    {
        PlayPause,
        NextTrack,
        PreviousTrack,
        VolumeUp,
        VolumeDown,
        VolumeMute
    }
}
