using System.Windows.Controls;
using System.Windows;
using SpotifyWPF.ViewModel.Component;

namespace SpotifyWPF.View.Component
{
    /// <summary>
    /// Interaction logic for TracksDataGrid.xaml
    /// </summary>
    public partial class TracksDataGrid : UserControl
    {
        public TracksDataGrid()
        {
            InitializeComponent();
            Loaded += TracksDataGrid_Loaded;
        }

        private void TracksDataGrid_Loaded(object sender, RoutedEventArgs e)
        {
            // Find the MenuItem and wire up the event in code-behind
            if (FindName("PlayToMenuItem") is MenuItem playToMenuItem)
            {
                playToMenuItem.SubmenuOpened += async (s, args) =>
                {
                    if (DataContext is TracksDataGridViewModel vm)
                    {
                        await vm.RefreshDevicesAsync();
                    }
                };
            }
        }
    }
}
