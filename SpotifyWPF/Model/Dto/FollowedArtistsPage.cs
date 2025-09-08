namespace SpotifyWPF.Model.Dto
{
    public class FollowedArtistsPage
    {
        public PagingDto<ArtistDto> Page { get; set; } = new PagingDto<ArtistDto>();
        public string? NextAfter { get; set; }
    }
}
