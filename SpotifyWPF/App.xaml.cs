using System;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using System.Diagnostics;

namespace SpotifyWPF
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private static System.Threading.Mutex? _mutex;

        public App()
        {
            // Check for single instance
            string appPath = System.Reflection.Assembly.GetExecutingAssembly().Location;
            string mutexName = "SpotifyWPF_SingleInstance_" + appPath.GetHashCode().ToString();
            _mutex = new System.Threading.Mutex(true, mutexName, out bool createdNew);
            if (!createdNew)
            {
                // Another instance is running
                MessageBox.Show("Spotify WPF Player is already running.", "Already Running", MessageBoxButton.OK, MessageBoxImage.Information);
                Shutdown();
                return;
            }

            // Hook global exception handlers as early as possible (before any XAML parses MainWindow)
            this.DispatcherUnhandledException += App_DispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
            TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;
            this.Exit += App_Exit;

            // Write a very early startup marker
            try { WriteMarker("App ctor reached."); } catch { /* ignore */ }

            // Set up debug logging to file
            try
            {
                var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                var logsFolder = Path.Combine(appData, Constants.AppDataFolderName, Constants.LogsFolderName);
                Directory.CreateDirectory(logsFolder);
                var debugLogPath = Path.Combine(logsFolder, "debug.log");
                
                string? deleteError = null;
                try
                {
                    if (File.Exists(debugLogPath))
                    {
                        File.Delete(debugLogPath);
                    }
                }
                catch (Exception deleteEx)
                {
                    deleteError = deleteEx.Message;
                }
                
                Trace.Listeners.Add(new TextWriterTraceListener(debugLogPath));
                Trace.AutoFlush = true;
                
                if (deleteError != null)
                {
                    Trace.WriteLine($"Failed to delete previous debug.log: {deleteError}");
                }
                else if (!File.Exists(debugLogPath))
                {
                    Trace.WriteLine("Previous debug.log deleted on startup");
                }
                
                Trace.WriteLine($"Debug logging started at {DateTime.UtcNow:o}");
            }
            catch (Exception setupEx)
            {
                // Try to log the setup failure to a fallback location
                try
                {
                    var fallbackPath = Path.Combine(Path.GetTempPath(), "SpotifyWPF_debug_setup_error.log");
                    File.WriteAllText(fallbackPath, $"Debug logging setup failed at {DateTime.UtcNow:o}: {setupEx.Message}");
                }
                catch { /* ignore */ }
            }
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
            try 
            { 
                WriteMarker("App Exit. Code=" + e.ApplicationExitCode);
                // Release the mutex
                _mutex?.ReleaseMutex();
                _mutex?.Dispose();
                _mutex = null;
                // Give a small delay to allow cleanup operations to complete
                System.Threading.Thread.Sleep(200);
            } 
            catch { /* ignore */ }
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
                // Filter out benign SocketException 995 that occurs during WebView2 shutdown
                var ex = e.Exception;
                if (ex is AggregateException aggEx && aggEx.InnerExceptions.Count == 1)
                {
                    var inner = aggEx.InnerExceptions[0];
                    if (inner is System.Net.Sockets.SocketException sockEx && sockEx.ErrorCode == 995)
                    {
                        // This is a benign exception that occurs when WebView2 network operations
                        // are cancelled during application shutdown. Set as observed and don't log.
                        e.SetObserved();
                        return;
                    }
                }

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
