using System;
using System.Windows.Input;
using SpotifyWPF.ViewModel;

namespace SpotifyWPF.Service
{
    /// <summary>
    /// Example extension for media key support (placeholder implementation)
    /// In a full implementation, this would use Windows APIs to capture global media keys
    /// </summary>
    public class MediaKeyManager : IDisposable
    {
        private readonly PlayerViewModel _playerViewModel;
        private bool _disposed = false;

        public MediaKeyManager(PlayerViewModel playerViewModel)
        {
            _playerViewModel = playerViewModel ?? throw new ArgumentNullException(nameof(playerViewModel));
            
            // In a full implementation, you would:
            // 1. Register for global hotkey events using Windows APIs
            // 2. Handle VK_MEDIA_PLAY_PAUSE, VK_MEDIA_NEXT_TRACK, VK_MEDIA_PREV_TRACK
            // 3. Execute corresponding player commands

            System.Diagnostics.Debug.WriteLine("MediaKeyManager initialized (placeholder)");
        }

        /// <summary>
        /// Handle media key press (example implementation)
        /// </summary>
        public void HandleMediaKey(MediaKey key)
        {
            switch (key)
            {
                case MediaKey.PlayPause:
                    _playerViewModel.PlayPauseCommand.Execute(null);
                    break;
                case MediaKey.NextTrack:
                    _playerViewModel.NextCommand.Execute(null);
                    break;
                case MediaKey.PreviousTrack:
                    _playerViewModel.PrevCommand.Execute(null);
                    break;
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                // Unregister global hotkeys
                _disposed = true;
            }
        }
    }

    public enum MediaKey
    {
        PlayPause,
        NextTrack,
        PreviousTrack,
        VolumeUp,
        VolumeDown
    }
}
