using System;

namespace SpotifyWPF.Model
{
    /// <summary>
    /// Model for saved albums with selection support for bulk operations
    /// </summary>
    public class Album
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Artist { get; set; } = string.Empty;
        public string ImageUrl { get; set; } = string.Empty;
        public int TotalTracks { get; set; }
        public DateTime AddedAt { get; set; }
    }
}