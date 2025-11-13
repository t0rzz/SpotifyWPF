namespace SpotifyWPF.Model.Dto
{
    public class PlaylistDto
    {
        public string Id { get; set; } = string.Empty;
        public string? Name { get; set; }
        public string? OwnerName { get; set; }
        public string? OwnerId { get; set; }
        public int TracksTotal { get; set; }
        public bool? IsPublic { get; set; }
        public string? SnapshotId { get; set; }
        public string? Href { get; set; }

        // Computed property for UI display
        public string OwnerDisplayName => string.IsNullOrEmpty(OwnerId) ? "Unknown" :
                                         IsOwned ? "You" : (OwnerName ?? "Unknown");

        // Set this when loading playlists to determine ownership
        public bool IsOwned { get; set; }
    }
}
