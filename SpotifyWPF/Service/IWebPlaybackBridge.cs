using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SpotifyWPF.Model;

namespace SpotifyWPF.Service
{
    /// <summary>
    /// Interface for Web Playback SDK bridge using WebView2
    /// </summary>
    public interface IWebPlaybackBridge
    {
    Task UpdateTokenAsync(string newAccessToken);
        /// Initialize the WebView2 with the player HTML and access token
        /// </summary>
        /// <param name="accessToken">Spotify access token</param>
        /// <param name="localHtmlPath">Path to player.html</param>
        Task InitializeAsync(string accessToken, string localHtmlPath);

        /// <summary>
        /// Connect to Web Playback SDK (calls player.connect())
        /// </summary>
        Task ConnectAsync();

        /// <summary>
        /// Get the device ID reported by the Web Playback SDK
        /// </summary>
        Task<string> GetWebPlaybackDeviceIdAsync();

        /// <summary>
        /// Play tracks on specified device
        /// </summary>
        /// <param name="uris">Spotify track URIs</param>
        /// <param name="deviceId">Device ID (optional)</param>
        Task PlayAsync(IEnumerable<string> uris, string? deviceId = null);

        /// <summary>
        /// Pause playback
        /// </summary>
        Task PauseAsync();

        /// <summary>
        /// Resume playback
        /// </summary>
        Task ResumeAsync();

        /// <summary>
        /// Seek to position in milliseconds
        /// </summary>
        /// <param name="positionMs">Position in milliseconds</param>
        Task SeekAsync(int positionMs);

        /// <summary>
        /// Set volume (0.0 - 1.0)
        /// </summary>
        /// <param name="volume">Volume level</param>
        Task SetVolumeAsync(double volume);

        /// <summary>
        /// Get current player state
        /// </summary>
        Task<PlayerState?> GetStateAsync();

        /// <summary>
        /// Event fired when player state changes
        /// </summary>
        event Action<PlayerState>? OnPlayerStateChanged;

        /// <summary>
        /// Event fired when Web Playback SDK reports ready with device ID
        /// </summary>
        event Action<string>? OnReadyDeviceId;

        /// <summary>
        /// Event fired when Web Playback SDK reports account error (non-premium user)
        /// </summary>
        event Action<string>? OnAccountError;
    }
}
