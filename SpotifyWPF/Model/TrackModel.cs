using System;

namespace SpotifyWPF.Model
{
    /// <summary>
    /// Enhanced track model for the player with all required properties
    /// </summary>
    public class TrackModel
    {
        public string Id { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Artist { get; set; } = string.Empty;
        public Uri? AlbumArtUri { get; set; }
        public int DurationMs { get; set; }
        public string Uri { get; set; } = string.Empty; // Spotify URI (spotify:track:...)
    }
}
