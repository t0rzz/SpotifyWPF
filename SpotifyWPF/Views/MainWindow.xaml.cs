using System.Reflection;
using System.Windows;

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
    }
}