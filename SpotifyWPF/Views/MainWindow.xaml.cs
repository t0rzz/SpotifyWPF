using System.Reflection;
using System.Windows;
using System.Windows.Controls;

namespace SpotifyWPF.Views
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            var version = Assembly.GetExecutingAssembly()
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            this.Title = $"SpotifyWPF v{version}";

            // Imposta il DataContext per i binding
            var locatorObj = Application.Current?.Resources["Locator"];
            var locator = locatorObj as SpotifyWPF.ViewModel.ViewModelLocator;
            if (locator != null)
            {
                this.DataContext = locator.Main;
            }
        }

        private async void MenuItem_SubmenuOpened(object sender, RoutedEventArgs e)
        {
            // Only handle top-level "Devices" when it opens
            if (e.OriginalSource is MenuItem mi && mi.Header is string header && header == "Devices")
            {
                if (this.DataContext is SpotifyWPF.ViewModel.MainViewModel vm)
                {
                    await vm.RefreshDevicesMenuAsync();
                }
            }
        }
    }
}