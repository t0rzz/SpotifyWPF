using System.Threading.Tasks;
using SpotifyWPF.Model.Dto;
using SpotifyWPF.Service;
using SpotifyWPF.ViewModel;

namespace SpotifyWPF.ViewModel.Component
{
    public class ArtistsDataGridViewModel : DataGridViewModelBaseDto<ArtistDto>
    {
        private readonly ISpotify _spotify;

        public ArtistsDataGridViewModel(ISpotify spotify)
        {
            _spotify = spotify;
        }

        private protected override async Task<PagingDto<ArtistDto>> FetchPageInternalAsync()
        {
            return await _spotify.SearchArtistsPageAsync(Query, Items.Count, 20);
        }
    }
}
