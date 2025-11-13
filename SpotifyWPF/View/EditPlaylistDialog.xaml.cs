using System.Windows;
using System.Windows.Controls;
using GalaSoft.MvvmLight;
using GalaSoft.MvvmLight.Command;

namespace SpotifyWPF.View
{
    public partial class EditPlaylistDialog : UserControl
    {
        public EditPlaylistDialog()
        {
            InitializeComponent();
        }
    }

    public class EditPlaylistDialogViewModel : ViewModelBase
    {
        private string _playlistName = string.Empty;
        private bool _isPublic;

        public string PlaylistName
        {
            get => _playlistName;
            set
            {
                _playlistName = value;
                RaisePropertyChanged();
                RaisePropertyChanged(nameof(CanSave));
            }
        }

        public bool IsPublic
        {
            get => _isPublic;
            set
            {
                _isPublic = value;
                RaisePropertyChanged();
            }
        }

        public bool CanSave => !string.IsNullOrWhiteSpace(PlaylistName);

        public RelayCommand SaveCommand { get; }
        public RelayCommand CancelCommand { get; }

        public Action? CloseAction { get; set; }

        public EditPlaylistDialogViewModel(Action? closeAction = null)
        {
            CloseAction = closeAction;
            SaveCommand = new RelayCommand(() =>
            {
                Result = true;
                CloseAction?.Invoke();
            });
            CancelCommand = new RelayCommand(() =>
            {
                Result = false;
                CloseAction?.Invoke();
            });
        }

        public bool? Result { get; private set; }
    }
}