using System.Threading.Tasks;
using SpotifyWPF.Model.Dto;
using SpotifyWPF.Service;
using SpotifyWPF.ViewModel;

namespace SpotifyWPF.ViewModel.Component
{
    public class TracksDataGridViewModel : DataGridViewModelBaseDto<TrackDto>
    {
        private readonly ISpotify _spotify;

        public TracksDataGridViewModel(ISpotify spotify)
        {
            _spotify = spotify;
        }

        private protected override async Task<PagingDto<TrackDto>> FetchPageInternalAsync()
        {
            return await _spotify.SearchTracksPageAsync(Query, Items.Count, 20);
        }
    }
}
