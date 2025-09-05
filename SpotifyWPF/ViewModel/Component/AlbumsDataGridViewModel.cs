using System.Threading.Tasks;
using SpotifyWPF.Model.Dto;
using SpotifyWPF.Service;
using SpotifyWPF.ViewModel;

namespace SpotifyWPF.ViewModel.Component
{
    public class AlbumsDataGridViewModel : DataGridViewModelBaseDto<AlbumDto>
    {
        private readonly ISpotify _spotify;

        public AlbumsDataGridViewModel(ISpotify spotify)
        {
            _spotify = spotify;
        }

        private protected override async Task<PagingDto<AlbumDto>> FetchPageInternalAsync()
        {
            return await _spotify.SearchAlbumsPageAsync(Query, Items.Count, 20);
        }
    }
}
