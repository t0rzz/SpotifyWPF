using System.Windows;
using System.Windows.Controls;
using SpotifyWPF.ViewModel.Page;
using SpotifyWPF.Model;

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

            // Removed auto-load playlists on app start

            // Wire up submenu event for tracks context menu
            WireUpPlayToSubmenuEvent();
        }

        private void WireUpPlayToSubmenuEvent()
        {
            // The MenuItem is deep inside DataGrid row styles, we need to handle it differently
            // We'll handle this via the ContextMenu.Opened event instead
        }

        private void TracksDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var vm = DataContext as PlaylistsPageViewModel;
            if (vm != null)
            {
                vm.SelectedTracks.Clear();
                foreach (TrackModel item in ((DataGrid)sender).SelectedItems)
                {
                    vm.SelectedTracks.Add(item);
                }
            }
        }

        private void ArtistsDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Notify the ViewModel that the selection changed so it can update command states
            if (DataContext is PlaylistsPageViewModel viewModel && sender is DataGrid dataGrid)
            {
                viewModel.SelectedArtistsCount = dataGrid.SelectedItems.Count;
                viewModel.SelectedArtists = dataGrid.SelectedItems.Count > 0 ? dataGrid.SelectedItems : null;
                viewModel.UnfollowArtistsCommand?.RaiseCanExecuteChanged();
                viewModel.UnfollowArtistCommand?.RaiseCanExecuteChanged();
            }
        }
    }
}
