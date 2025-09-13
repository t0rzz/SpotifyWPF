namespace SpotifyWPF.Model.Dto
{
    public class TrackDto
    {
        public string Id { get; set; } = string.Empty;
        public string? Name { get; set; }
        public string? Artists { get; set; }
        public string? AlbumName { get; set; }
        public int DurationMs { get; set; }
        public string? Href { get; set; }
        public string Uri { get; set; } = string.Empty;
        public string? AlbumImageUrl { get; set; }
    }
}
