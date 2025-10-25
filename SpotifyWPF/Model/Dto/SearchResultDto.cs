namespace SpotifyWPF.Model.Dto
{
    public class SearchResultDto
    {
        public string Id { get; set; } = string.Empty;
        public string? Name { get; set; }
        public string? Artists { get; set; }
        public string? AlbumName { get; set; }
        public int DurationMs { get; set; }
        public string? Href { get; set; }
        public string Uri { get; set; } = string.Empty;
        public string? AlbumImageUrl { get; set; }
        public string? Type { get; set; }
        public string? Description { get; set; }
        public string? ImageUrl { get; set; }
        public bool CanAddToPlaylist { get; set; }
        public bool IsInSelectedPlaylist { get; set; }
        public object? OriginalDto { get; set; }
    }
}