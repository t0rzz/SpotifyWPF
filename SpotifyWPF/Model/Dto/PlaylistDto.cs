namespace SpotifyWPF.Model.Dto
{
    public class PlaylistDto
    {
        public string Id { get; set; } = string.Empty;
        public string? Name { get; set; }
        public string? OwnerName { get; set; }
        public int TracksTotal { get; set; }
        public string? SnapshotId { get; set; }
        public string? Href { get; set; }
    }
}
