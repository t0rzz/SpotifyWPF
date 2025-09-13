using System.ComponentModel;

namespace SpotifyWPF.Model
{
    /// <summary>
    /// Legacy track model - consider migrating to TrackModel for consistency
    /// </summary>
    [Obsolete("Use TrackModel instead for better consistency and null safety")]
    public class Track
    {
        public string Id { get; set; } = string.Empty;
        public string Uri { get; set; } = string.Empty;
        public string TrackName { get; set; } = string.Empty;
        public string Artists { get; set; } = string.Empty;
        public string AlbumName { get; set; } = string.Empty;
        public int DurationMs { get; set; }
        public string ImageUrl { get; set; } = string.Empty;

        // Legacy compatibility properties - marked as obsolete
        [Obsolete("Use TrackName property instead")]
        public string Name => TrackName;

        [Obsolete("Use Artists property instead")]
        public string ArtistsNames => Artists;
    }
}
