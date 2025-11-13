namespace SpotifyWPF.Model.Dto
{
    public class TrackDto
    {
        public string Id { get; set; } = string.Empty;
        public string? Name { get; set; }
        public string? Artists { get; set; }
        public string? AlbumName { get; set; }
        // When a track comes from a playlist item, Spotify returns an "added_at" timestamp.
        // Store it here as nullable DateTimeOffset. It will be null for tracks that are not associated
        // with a playlist item (e.g., search results).
        public System.DateTimeOffset? AddedAt { get; set; }
        public int DurationMs { get; set; }
        public string? Href { get; set; }
        public string Uri { get; set; } = string.Empty;
        public string? AlbumImageUrl { get; set; }
        public int Position { get; set; } // Position in playlist (1-based)

        public string DurationFormatted
        {
            get
            {
                var totalSeconds = DurationMs / 1000;
                var minutes = totalSeconds / 60;
                var seconds = totalSeconds % 60;
                return $"{minutes}:{seconds:D2}";
            }
        }
    }
}
