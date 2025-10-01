using System.Collections.Generic;

namespace SpotifyWPF.Model.Dto
{
    public class AlbumDto
    {
        public string Id { get; set; } = string.Empty;
        public string? Name { get; set; }
        public string? Artists { get; set; }
        public string? ReleaseDate { get; set; }
        public int TotalTracks { get; set; }
        public string? Href { get; set; }
        public string? ImageUrl { get; set; }
        public DateTime? AddedAt { get; set; }
    }
}
