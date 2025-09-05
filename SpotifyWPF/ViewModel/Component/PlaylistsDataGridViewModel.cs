using System.Threading.Tasks;
using SpotifyWPF.Model.Dto;
using SpotifyWPF.Service;
using SpotifyWPF.ViewModel;

namespace SpotifyWPF.ViewModel.Component
{
    public class PlaylistsDataGridViewModel : DataGridViewModelBaseDto<PlaylistDto>
    {
        private readonly ISpotify _spotify;

        public PlaylistsDataGridViewModel(ISpotify spotify)
        {
            _spotify = spotify;
        }

        private protected override async Task<PagingDto<PlaylistDto>> FetchPageInternalAsync()
        {
            // Usa il service decoupled per effettuare la ricerca e tornare DTO
            // Query è gestita dal base DTO
            return await _spotify.SearchPlaylistsPageAsync(Query, Items.Count, 20);
        }
    }
}
