using System.Collections.ObjectModel;

namespace SpotifyWPF.Model.Dto
{
    public class GeneratedPlaylistDto
    {
        public string Name { get; set; } = string.Empty;
        public string Genre { get; set; } = string.Empty;
        public int TrackCount { get; set; }
        public ObservableCollection<TrackDto> Tracks { get; set; } = new ObservableCollection<TrackDto>();
        public string? ImageUrl { get; set; }
        public string Description { get; set; } = string.Empty;
    }
}