using System.Windows.Controls;

namespace SpotifyWPF.View.Page
{
    public partial class AlbumsPage : UserControl
    {
        public AlbumsPage()
        {
            InitializeComponent();
        }

        private void AlbumsDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Notify the ViewModel that the selection changed so it can update command states
            if (DataContext is ViewModel.Page.AlbumsPageViewModel viewModel && sender is DataGrid dataGrid)
            {
                viewModel.IsMultipleAlbumsSelected = dataGrid.SelectedItems.Count > 1;
                viewModel.DeleteSelectedAlbumsCommand?.RaiseCanExecuteChanged();
            }
        }
    }
}