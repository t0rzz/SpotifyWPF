using System.Windows.Controls;
using System.Windows.Input;

namespace SpotifyWPF.View.Page
{
    /// <summary>
    /// Interaction logic for PlaylistManagerPage.xaml
    /// </summary>
    public partial class PlaylistManagerPage : UserControl
    {
        private bool _isDeleteButtonClicked;

        public PlaylistManagerPage()
        {
            InitializeComponent();
        }

        private void GeneratedPlaylistsGrid_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Find the button that was clicked
            var originalSource = e.OriginalSource as System.Windows.DependencyObject;
            if (originalSource == null) return;

            var button = FindParent<Button>(originalSource);
            if (button != null && button.Content != null && button.Content.ToString() == "Delete")
            {
                // Mark that a delete button was clicked
                _isDeleteButtonClicked = true;
            }
            else
            {
                // Reset the flag for other clicks
                _isDeleteButtonClicked = false;
            }
        }

        private void GeneratedPlaylistsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isDeleteButtonClicked)
            {
                // Clear the selection if it was caused by clicking the delete button
                var dataGrid = sender as DataGrid;
                if (dataGrid != null)
                {
                    dataGrid.SelectedItem = null;
                }
                _isDeleteButtonClicked = false;
            }
        }

        private static T? FindParent<T>(System.Windows.DependencyObject child) where T : System.Windows.DependencyObject
        {
            if (child == null) return null;

            var parent = System.Windows.Media.VisualTreeHelper.GetParent(child);
            while (parent != null)
            {
                if (parent is T typedParent)
                {
                    return typedParent;
                }
                parent = System.Windows.Media.VisualTreeHelper.GetParent(parent);
            }
            return null;
        }
    }
}