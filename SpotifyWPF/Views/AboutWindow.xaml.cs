using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Input;

namespace SpotifyWPF.Views
{
    /// <summary>
    /// Interaction logic for AboutWindow.xaml
    /// </summary>
    public partial class AboutWindow : Window
    {
        public AboutWindow()
        {
            InitializeComponent();
            LoadVersionInfo();
        }

        private void LoadVersionInfo()
        {
            try
            {
                var asm = Assembly.GetExecutingAssembly();
                var version = asm.GetName().Version?.ToString() ?? "1.0.0.0";
                // Remove the build and revision numbers for cleaner display
                var shortVersion = version.Split('.')[0] + "." + version.Split('.')[1] + "." + version.Split('.')[2];

                VersionTextBlock.Text = $"Version: {shortVersion}";
            }
            catch
            {
                VersionTextBlock.Text = "Version: 1.0.0";
            }
        }

        private void OKButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void RepositoryLink_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "https://github.com/t0rzz/SpotifyWPF",
                    UseShellExecute = true
                };
                Process.Start(psi);
            }
            catch
            {
                // Silently fail if we can't open the browser
            }
        }

        // Enable window dragging
        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }
    }
}