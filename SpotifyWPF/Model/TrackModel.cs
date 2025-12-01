using System;

namespace SpotifyWPF.Model
{
    /// <summary>
    /// Enhanced track model for the player with all required properties
    /// </summary>
    public class TrackModel : System.ComponentModel.INotifyPropertyChanged
    {
        private string _id = string.Empty;
        private string _title = string.Empty;
        private string _artist = string.Empty;
        private Uri? _albumArtUri;
        private int _durationMs;
        private string _uri = string.Empty;
        private int _position;

        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

        public string Id
        {
            get => _id;
            set
            {
                if (_id != value)
                {
                    _id = value;
                    PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(Id)));
                }
            }
        }

        public string Title
        {
            get => _title;
            set
            {
                if (_title != value)
                {
                    _title = value;
                    PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(Title)));
                }
            }
        }

        public string Artist
        {
            get => _artist;
            set
            {
                if (_artist != value)
                {
                    _artist = value;
                    PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(Artist)));
                }
            }
        }

        public Uri? AlbumArtUri
        {
            get => _albumArtUri;
            set
            {
                if (_albumArtUri != value)
                {
                    _albumArtUri = value;
                    try { SpotifyWPF.Service.LoggingService.LogToFile($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] MODEL: TrackModel.AlbumArtUri changed to: {_albumArtUri}\n"); } catch { }
                    PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(AlbumArtUri)));
                }
            }
        }

        public int DurationMs
        {
            get => _durationMs;
            set
            {
                if (_durationMs != value)
                {
                    _durationMs = value;
                    PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(DurationMs)));
                }
            }
        }

        public string Uri
        {
            get => _uri;
            set
            {
                if (_uri != value)
                {
                    _uri = value;
                    PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(Uri)));
                }
            }
        }

        public int Position
        {
            get => _position;
            set
            {
                if (_position != value)
                {
                    _position = value;
                    PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(Position)));
                }
            }
        }
    }
}
