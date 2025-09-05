using System.Windows;
using SpotifyWPF.ViewModel;

namespace SpotifyWPF.View
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow
    {
        public MainWindow()
        {
            InitializeComponent();

            // Imposta il DataContext in modo sicuro, evitando StaticResource in XAML
            var locatorObj = Application.Current != null ? Application.Current.Resources["Locator"] : null;
            var locator = locatorObj as ViewModelLocator;
            if (locator != null)
            {
                this.DataContext = locator.Main;
            }
        }
    }
}
