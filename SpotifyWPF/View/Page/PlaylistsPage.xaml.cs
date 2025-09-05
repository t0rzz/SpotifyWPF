using System.Windows;
using System.Windows.Controls;
using SpotifyWPF.ViewModel.Page;

namespace SpotifyWPF.View.Page
{
    /// <summary>
    /// Interaction logic for PlaylistsPage.xaml
    /// </summary>
    public partial class PlaylistsPage : UserControl
    {
        private bool _loadedOnce;

        public PlaylistsPage()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (_loadedOnce) return;
            _loadedOnce = true;

            var vm = DataContext as PlaylistsPageViewModel;
            if (vm != null && vm.LoadPlaylistsCommand != null && vm.Playlists.Count == 0 && vm.LoadPlaylistsCommand.CanExecute(null))
            {
                vm.LoadPlaylistsCommand.Execute(null);
            }
        }
    }
}
