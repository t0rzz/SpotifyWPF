using System;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace SpotifyWPF
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        public App()
        {
            // Hook global exception handlers as early as possible (before any XAML parses MainWindow)
            this.DispatcherUnhandledException += App_DispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
            TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;
            this.Exit += App_Exit;

            // Write a very early startup marker
            try { WriteMarker("App ctor reached."); } catch { /* ignore */ }
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            // Aumenta le connessioni per host per abilitare concorrenza reale su .NET Framework
            if (ServicePointManager.DefaultConnectionLimit < Constants.DefaultConnectionLimit)
            {
                ServicePointManager.DefaultConnectionLimit = Constants.DefaultConnectionLimit;
            }

            // Riduci latenza su richieste brevi
            ServicePointManager.Expect100Continue = false;
            ServicePointManager.UseNagleAlgorithm = false;

            try { WriteMarker("OnStartup reached."); } catch { /* ignore */ }

            base.OnStartup(e);
        }

        private void App_Exit(object sender, ExitEventArgs e)
        {
            try { WriteMarker("App Exit. Code=" + e.ApplicationExitCode); } catch { /* ignore */ }
        }

        private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            try
            {
                var path = LogException(e.Exception, "DispatcherUnhandledException");
                TryShowError(path, e.Exception);
            }
            catch
            {
                // ignore
            }
            finally
            {
                // Evita loop di crash: gestisci e chiudi
                e.Handled = true;
                Shutdown(1);
            }
        }

        private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            try
            {
                var ex = e.ExceptionObject as Exception ?? new Exception("UnhandledException (non-Exception object)");
                var path = LogException(ex, "AppDomain.UnhandledException");
                TryShowError(path, ex);
            }
            catch
            {
                // ignore
            }
        }

        private void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            try
            {
                LogException(e.Exception, "TaskScheduler.UnobservedTaskException");
                // Evita che termini il processo
                e.SetObserved();
            }
            catch
            {
                // ignore
            }
        }

        private static string? LogException(Exception ex, string source)
        {
            try
            {
                var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                var folder = Path.Combine(appData, Constants.AppDataFolderName, Constants.LogsFolderName);
                Directory.CreateDirectory(folder);

                var file = Path.Combine(folder, "error-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + ".log");
                using (var sw = new StreamWriter(file, false))
                {
                    sw.WriteLine("UTC: " + DateTime.UtcNow.ToString("o"));
                    sw.WriteLine("Source: " + source);
                    sw.WriteLine("OS: " + Environment.OSVersion);
                    sw.WriteLine(".NET: " + Environment.Version);
                    sw.WriteLine(new string('-', 60));
                    sw.WriteLine(ex.ToString());
                }
                return file;
            }
            catch
            {
                return null;
            }
        }

        private static void TryShowError(string? logPath, Exception ex)
        {
            try
            {
                var msg = "An unexpected error occurred and was logged.";
                if (!string.IsNullOrEmpty(logPath))
                    msg += Environment.NewLine + "Log: " + logPath;

                if (ex != null)
                    msg += Environment.NewLine + Environment.NewLine + ex.Message;

                MessageBox.Show(msg, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch
            {
                // ignore
            }
        }

        private static void WriteMarker(string text)
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var folder = Path.Combine(appData, Constants.AppDataFolderName, Constants.LogsFolderName);
            Directory.CreateDirectory(folder);
            var file = Path.Combine(folder, "startup.log");
            using (var sw = new StreamWriter(file, true))
            {
                sw.WriteLine(DateTime.UtcNow.ToString("o") + " - " + text);
            }
        }
    }
}
