namespace SpotifyWPF.Model.Dto
{
    public class ArtistDto
    {
        public string Id { get; set; } = string.Empty;
        public string? Name { get; set; }
        public int FollowersTotal { get; set; }
        public int Popularity { get; set; }
        public string? Href { get; set; }
    }
}
