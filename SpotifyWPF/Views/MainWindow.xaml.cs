using System.Windows.Data;
using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.ComponentModel;
using System.Windows.Media.Imaging;
using System.Windows.Input;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using SpotifyWPF.Service;
using SpotifyWPF.Service.MessageBoxes;
using SpotifyWPF.ViewModel;
using SpotifyWPF.View;
using GalaSoft.MvvmLight;
using GalaSoft.MvvmLight.Command;

namespace SpotifyWPF.Views
{
    public partial class MainWindow : Window
    {
        private PlayerViewModel? _playerViewModel;
        private bool _isInitialized = false;
        private ViewModelLocator? _locator;
        private WebPlaybackBridge? _webPlaybackBridge;
        private ILoggingService? _loggingService;
        private TaskCompletionSource<bool> _webView2Initialized = new TaskCompletionSource<bool>();

        public MainWindow()
        {
            LoggingService.LogToFile("=== APPLICATION START ===\n");

            // Get logging service first
            var locatorObj = Application.Current?.Resources["Locator"];
            _locator = locatorObj as SpotifyWPF.ViewModel.ViewModelLocator;
            if (_locator != null)
            {
                // Get logging service from the service locator
                _loggingService = GalaSoft.MvvmLight.Ioc.SimpleIoc.Default.GetInstance<ILoggingService>();
            }

            LoggingService.LogToFile("MainWindow constructor called\n");
            InitializeComponent();
            
            // Set window title with version
            SetWindowTitleWithVersion();
            
            // Initialize WebView2 after the window is loaded (when controls are ready)
            this.Loaded += MainWindow_Loaded;
            this.Closed += MainWindow_Closed;
            this.Closing += MainWindow_Closing;
            this.StateChanged += MainWindow_StateChanged;
        }

        /// <summary>
        /// Initialize WebView2 once for the lifetime of the application
        /// </summary>
        private async Task InitializeWebView2Async()
        {
            try
            {
                LoggingService.LogToFile("InitializeWebView2Async: Starting WebView2 initialization in Loaded event\n");

                // Ensure WebView2 operations are performed on the UI thread
                await Application.Current.Dispatcher.InvokeAsync(async () =>
                {
                    LoggingService.LogToFile("InitializeWebView2Async: WebView2 initialization: Checking if already initialized\n");
                    // Check if WebView2 is already initialized
                    if (WebPlaybackWebView.CoreWebView2 != null)
                    {
                        LoggingService.LogToFile("InitializeWebView2Async: WebView2 already initialized, skipping\n");
                        return;
                    }

                    LoggingService.LogToFile("InitializeWebView2Async: WebView2 initialization: Creating environment\n");

                    // Create a user data folder in a writable location to avoid access denied errors
                    var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                    var userDataFolder = Path.Combine(appDataPath, "SpotifyWPF", "WebView2");

                    // Ensure the directory exists
                    Directory.CreateDirectory(userDataFolder);
                    LoggingService.LogToFile($"InitializeWebView2Async: WebView2 initialization: User data folder: {userDataFolder}\n");

                    // Initialize WebView2 with custom environment
                    var options = new Microsoft.Web.WebView2.Core.CoreWebView2EnvironmentOptions("--autoplay-policy=no-user-gesture-required");
                    var environment = await Microsoft.Web.WebView2.Core.CoreWebView2Environment.CreateAsync(null, userDataFolder, options);
                    LoggingService.LogToFile("InitializeWebView2Async: WebView2 initialization: Environment created\n");

                    LoggingService.LogToFile("InitializeWebView2Async: WebView2 initialization: Calling EnsureCoreWebView2Async\n");
                    await WebPlaybackWebView.EnsureCoreWebView2Async(environment);
                    LoggingService.LogToFile("InitializeWebView2Async: WebView2 initialization: EnsureCoreWebView2Async completed\n");

                    // Wait until CoreWebView2 is actually available
                    LoggingService.LogToFile("InitializeWebView2Async: WebView2 initialization: Waiting for CoreWebView2 to be available\n");
                    int attempts = 0;
                    while (WebPlaybackWebView.CoreWebView2 == null && attempts < 50) // Max 5 seconds
                    {
                        await Task.Delay(100);
                        attempts++;
                        LoggingService.LogToFile($"InitializeWebView2Async: WebView2 initialization: Waiting for CoreWebView2... attempt {attempts}, CoreWebView2 is null: {WebPlaybackWebView.CoreWebView2 == null}\n");
                    }

                    if (WebPlaybackWebView.CoreWebView2 == null)
                    {
                        throw new InvalidOperationException("CoreWebView2 failed to initialize within timeout");
                    }

                    LoggingService.LogToFile("InitializeWebView2Async: WebView2 initialization: CoreWebView2 is now available\n");

                    // Configure WebView2 settings
                    if (WebPlaybackWebView.CoreWebView2 != null)
                    {
                        WebPlaybackWebView.CoreWebView2.Settings.AreDefaultScriptDialogsEnabled = false;
                        WebPlaybackWebView.CoreWebView2.Settings.AreDevToolsEnabled = false;
                        WebPlaybackWebView.CoreWebView2.IsMuted = false;

                        // Set Chrome User-Agent for Spotify compatibility
                        WebPlaybackWebView.CoreWebView2.Settings.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";

                        // Enable external resource loading
                        WebPlaybackWebView.CoreWebView2.Settings.IsGeneralAutofillEnabled = true;
                        WebPlaybackWebView.CoreWebView2.Settings.IsPasswordAutosaveEnabled = false;
                        WebPlaybackWebView.CoreWebView2.Settings.IsSwipeNavigationEnabled = false;
                        WebPlaybackWebView.CoreWebView2.Settings.IsStatusBarEnabled = false;

                        // Grant media permissions
                        WebPlaybackWebView.CoreWebView2.PermissionRequested += (s, e) =>
                        {
                            if (e.PermissionKind == Microsoft.Web.WebView2.Core.CoreWebView2PermissionKind.Camera ||
                                e.PermissionKind == Microsoft.Web.WebView2.Core.CoreWebView2PermissionKind.Microphone)
                            {
                                e.State = Microsoft.Web.WebView2.Core.CoreWebView2PermissionState.Allow;
                            }
                        };

                        // Allow all web resource requests
                        WebPlaybackWebView.CoreWebView2.AddWebResourceRequestedFilter("*", Microsoft.Web.WebView2.Core.CoreWebView2WebResourceContext.All);

                        LoggingService.LogToFile("InitializeWebView2Async: WebView2 initialization: Settings configured\n");
                    }
                });

                LoggingService.LogToFile("InitializeWebView2Async: WebView2 initialization: Setting TaskCompletionSource to success\n");
                // Signal that WebView2 initialization is complete
                _webView2Initialized.TrySetResult(true);
                LoggingService.LogToFile("InitializeWebView2Async: WebView2 initialization: COMPLETED SUCCESSFULLY\n");
            }
            catch (Exception ex)
            {
                LoggingService.LogToFile($"InitializeWebView2Async: WebView2 initialization failed: {ex.Message}\n");
                LoggingService.LogToFile($"InitializeWebView2Async: WebView2 initialization stack trace: {ex.StackTrace}\n");
                _webView2Initialized.TrySetException(ex);
            }
        }

        /// <summary>
        /// Set the window title with version information
        /// </summary>
        private void SetWindowTitleWithVersion()
        {
            try
            {
                var asm = Assembly.GetExecutingAssembly();
                var version = asm.GetName().Version?.ToString() ?? "1.0.0.0";
                // Remove the build and revision numbers for cleaner display
                var shortVersion = version.Split('.')[0] + "." + version.Split('.')[1] + "." + version.Split('.')[2];
                this.Title = $"SpofifyWPF v{shortVersion}";
            }
            catch
            {
                this.Title = "SpofifyWPF";
            }
        }

        /// <summary>
        /// Handle window loaded event to check for auto-login and initialize WebView2
        /// </summary>
        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            LoggingService.LogToFile("MainWindow_Loaded: Starting WebView2 initialization\n");

            // Initialize WebView2 first since controls are now loaded
            await InitializeWebView2Async();

            LoggingService.LogToFile("MainWindow_Loaded: WebView2 initialization completed\n");

            // Then check for auto-login
            if (_locator?.Main != null)
            {
                await _locator.Main.CheckAutoLoginAndStartTimerAsync();
            }
        }

        /// <summary>
        /// Handle window closed event to cleanup resources
        /// </summary>
        private void MainWindow_Closed(object? sender, EventArgs e)
        {
            _loggingService?.LogInfo("MainWindow closing, cleaning up resources");

            // Dispose WebPlaybackBridge first to stop WebView2 operations
            if (_webPlaybackBridge is IDisposable disposableBridge)
            {
                try
                {
                    disposableBridge.Dispose();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error disposing WebPlaybackBridge: {ex.Message}");
                }
            }

            // Stop local HTTP host for player
            try
            {
                WebPlaybackHost.Stop();
            }
            catch { }

            // Dispose PlayerViewModel
            if (_playerViewModel is IDisposable disposablePlayer)
            {
                try
                {
                    disposablePlayer.Dispose();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error disposing PlayerViewModel: {ex.Message}");
                }
            }

            // Clear references
            _playerViewModel = null;
            _webPlaybackBridge = null;
            _locator = null;
            _loggingService = null;
        }

        /// <summary>
        /// Handle window closing event to check minimize-to-tray setting
        /// </summary>
        private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            try
            {
                // Get settings provider from service locator
                var settingsProvider = GalaSoft.MvvmLight.Ioc.SimpleIoc.Default.GetInstance<ISettingsProvider>();
                if (settingsProvider != null && settingsProvider.MinimizeToTrayOnClose)
                {
                    // Minimize to tray instead of closing
                    if (_playerViewModel?.TrayIconManager != null)
                    {
                        _playerViewModel.TrayIconManager.HideToTray();
                    }
                    else
                    {
                        // Fallback if no tray manager
                        this.WindowState = WindowState.Minimized;
                        this.Hide();
                    }
                    e.Cancel = true;
                }
                else
                {
                    // Ensure tray icon is cleaned up so the app doesn't remain in background
                    try
                    {
                        if (_playerViewModel?.TrayIconManager != null)
                        {
                            // Dispose the tray manager explicitly before closing if user doesn't want minimize-to-tray
                            _playerViewModel.TrayIconManager.Dispose();
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error disposing tray manager during close: {ex.Message}");
                    }
                    // Ensure application exits: sometimes native tray or WebView threads keep the process alive
                    try
                    {
                        System.Windows.Application.Current.Shutdown();
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error handling window closing: {ex.Message}");
            }
        }

        /// <summary>
        /// Handle window state changes for tray integration
        /// </summary>
        private void MainWindow_StateChanged(object? sender, EventArgs e)
        {
            try
            {
                // Notify TrayIconManager of window state changes
                if (_playerViewModel?.TrayIconManager != null)
                {
                    _playerViewModel.TrayIconManager.HandleWindowStateChanged(this.WindowState);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error handling window state change: {ex.Message}");
            }
        }

        /// <summary>
        /// Public method called by MainViewModel to initialize player after login
        /// </summary>
        public async Task InitializePlayerAfterLoginAsync()
        {
            LoggingService.LogToFile($"MainWindow: InitializePlayerAfterLoginAsync called, _locator is {( _locator != null ? "not null" : "null")}\n");
            if (_locator != null)
            {
                LoggingService.LogToFile("MainWindow: Calling InitializePlayerAsync\n");
                await InitializePlayerAsync(_locator);
                LoggingService.LogToFile("MainWindow: InitializePlayerAsync completed\n");
            }
            else
            {
                LoggingService.LogToFile("MainWindow: _locator is null, cannot initialize player\n");
            }
        }

        /// <summary>
        /// Initialize the player ViewModel and Web Playback bridge
        /// </summary>
        private async Task InitializePlayerAsync(ViewModelLocator locator)
        {
            LoggingService.LogToFile($"=== NEW RUN STARTED AT {DateTime.Now} ===\n");
            LoggingService.LogToFile($"MainWindow: InitializePlayerAsync called, _isInitialized={_isInitialized}\n");
            try
            {
                if (_isInitialized) 
                {
                    LoggingService.LogToFile("MainWindow: InitializePlayerAsync: Already initialized, returning\n");
                    return;
                }

                LoggingService.LogToFile("MainWindow: Starting Player Initialization\n");

                // Wait for WebView2 to be initialized first
                LoggingService.LogToFile("MainWindow: Waiting for WebView2 initialization\n");
                await _webView2Initialized.Task;
                LoggingService.LogToFile("MainWindow: WebView2 initialization confirmed\n");

                // Create WebPlaybackBridge with the WebView2 control (only if not already created)
                if (_webPlaybackBridge == null)
                {
                    _webPlaybackBridge = new WebPlaybackBridge(WebPlaybackWebView, _loggingService!);
                    LoggingService.LogToFile("MainWindow: WebPlaybackBridge created\n");
                }
                else
                {
                    LoggingService.LogToFile("MainWindow: Reusing existing WebPlaybackBridge\n");
                }
                
                // Create PlayerViewModel
                    var spotify = locator.Main.GetSpotifyService();
                    var tokenProvider = GalaSoft.MvvmLight.Ioc.SimpleIoc.Default.GetInstance<ITokenProvider>();
                    var subscriptionDialogService = GalaSoft.MvvmLight.Ioc.SimpleIoc.Default.GetInstance<ISubscriptionDialogService>();
                    _playerViewModel = new PlayerViewModel(spotify, _webPlaybackBridge, _loggingService!, tokenProvider, subscriptionDialogService);
                LoggingService.LogToFile("MainWindow: PlayerViewModel created\n");
                
                // Set player on MainViewModel
                locator.Main.Player = _playerViewModel;
                LoggingService.LogToFile("MainWindow: Player set on MainViewModel\n");
                
                // Get access token and initialize
                var accessToken = await GetAccessTokenAsync();
                LoggingService.LogToFile($"MainWindow: Access token obtained: {!string.IsNullOrEmpty(accessToken)}\n");
                if (!string.IsNullOrEmpty(accessToken))
                {
                    // Start a lightweight local HTTP host for the player to ensure SDP/Web Playback SDK origin is trusted
                    try { WebPlaybackHost.Start(); } catch { }
                    var playerHtmlPath = GetPlayerHtmlPath();
                    LoggingService.LogToFile($"MainWindow: Player HTML path: {playerHtmlPath}\n");
                    await _playerViewModel.InitializeAsync(accessToken, playerHtmlPath);
                    LoggingService.LogToFile("MainWindow: PlayerViewModel initialized successfully\n");

                        // ✅ Hook album art UI events so we can log when the Image control receives the source
                        try
                        {
                            var albumImage = this.FindName("PlayerAlbumImage") as Image;
                            if (albumImage != null)
                            {
                                                        try
                                                        {
                                                            LoggingService.LogToFile($"[ALBUM_UI] Initial albumImage.Source={albumImage.Source?.GetType().FullName ?? "<null>"}\n");
                                                            var be = BindingOperations.GetBindingExpression(albumImage, Image.SourceProperty);
                                                            LoggingService.LogToFile($"[ALBUM_UI] Initial Binding: {be?.ParentBinding?.Path?.Path ?? "<none>"}\n");
                                                        }
                                                        catch (Exception ex)
                                                        {
                                                            LoggingService.LogToFile($"[ALBUM_UI] Error reading initial binding: {ex.Message}\n");
                                                        }
                                LoggingService.LogToFile($"[ALBUM_UI] Hooking album image events\n");

                                albumImage.ImageFailed += PlayerAlbumImage_ImageFailed;

                                var dpd = DependencyPropertyDescriptor.FromProperty(Image.SourceProperty, typeof(Image));
                                if (dpd != null)
                                {
                                    dpd.AddValueChanged(albumImage, PlayerAlbumImage_SourceChanged);
                                }
                                albumImage.TargetUpdated += PlayerAlbumImage_TargetUpdated;
                                // Listen to ViewModel property change for diagnostics
                                var vm = locator.Main?.Player;
                                if (vm != null)
                                {
                                    // Capture the image control in the closure so we can compare VM->UI
                                    var theImg = albumImage;
                                    vm.PropertyChanged += (s, evt) =>
                                    {
                                        if (evt.PropertyName == "CurrentAlbumArtImage")
                                        {
                                            var uiSource = theImg?.Source as BitmapImage;
                                            LoggingService.LogToFile($"[ALBUM_UI] PlayerViewModel.CurrentAlbumArtImage changed. VMHasImage={(vm.CurrentAlbumArtImage != null ? "true" : "false")}, UI Source={(uiSource?.UriSource?.ToString() ?? "<null>")}\n");
                                        }
                                    };
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            LoggingService.LogToFile($"[ALBUM_UI] Error hooking album image events: {ex.Message}\n");
                        }

                }
                else
                {
                    LoggingService.LogToFile("MainWindow: ERROR: No access token available\n");
                }
                
                _isInitialized = true;
                LoggingService.LogToFile("MainWindow: === Player Initialization Complete ===\n");
            }
            catch (Exception ex)
            {
                LoggingService.LogToFile($"MainWindow: Player initialization error: {ex.Message}\n");
                LoggingService.LogToFile($"MainWindow: Stack trace: {ex.StackTrace}\n");
            }
        }

        /// <summary>
        /// Get the path to the player.html file
        /// </summary>
        private string GetPlayerHtmlPath()
        {
            var appDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            var debugDir = Path.GetDirectoryName(appDir);
            var binDir = Path.GetDirectoryName(debugDir);
            var projectDir = Path.GetDirectoryName(binDir);
            var playerPath = Path.Combine(projectDir!, "Assets", "player.html");
            
            // Prefer local HTTP host if available so Web Playback SDK registers origin correctly
            if (!string.IsNullOrEmpty(WebPlaybackHost.Url))
            {
                return WebPlaybackHost.Url!;
            }

            if (File.Exists(playerPath))
            {
                return $"file:///{playerPath.Replace('\\', '/')}";
            }
            
            // Fallback: try relative path
            var relativePath = Path.Combine("Assets", "player.html");
            if (File.Exists(relativePath))
            {
                return $"file:///{Path.GetFullPath(relativePath).Replace('\\', '/')}";
            }
            
            throw new FileNotFoundException("player.html not found");
        }

        /// <summary>
        /// Get access token from Spotify service
        /// </summary>
        private async Task<string> GetAccessTokenAsync()
        {
            try
            {
                // Use the locator's Main property instead of DataContext
                // since DataContext might not be set yet during initialization
                var mainVm = _locator?.Main;
                if (mainVm != null)
                {
                    var spotify = mainVm.GetSpotifyService();
                    var token = await spotify.GetCurrentAccessTokenAsync();
                    return token ?? string.Empty;
                }
                return string.Empty;
            }
            catch (Exception ex)
            {
                LoggingService.LogToFile($"MainWindow: GetAccessTokenAsync error: {ex.Message}\n");
                return string.Empty;
            }
        }

        /// <summary>
        /// Handle device menu submenu opening
        /// </summary>
        private async void MenuItem_SubmenuOpened(object sender, RoutedEventArgs e)
        {
            try
            {
                // Only handle top-level "Devices" when it opens
                if (e.OriginalSource is MenuItem mi && mi.Header is string header && header == "Devices")
                {
                    System.Diagnostics.Debug.WriteLine("=== DEVICES MENU SUBMENU OPENED ===");
                    if (this.DataContext is SpotifyWPF.ViewModel.MainViewModel vm)
                    {
                        await vm.RefreshDevicesMenuAsync();
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"MenuItem_SubmenuOpened error: {ex.Message}");
            }
        }

        private async void MenuItem_MouseEnter(object sender, MouseEventArgs e)
        {
            try
            {
                // Only handle top-level "Devices" when mouse enters
                if (sender is MenuItem mi && mi.Header is string header && header == "Devices")
                {
                    if (this.DataContext is SpotifyWPF.ViewModel.MainViewModel vm)
                    {
                        await vm.RefreshDevicesMenuAsync();
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"MenuItem_MouseEnter error: {ex.Message}");
            }
        }

        /// <summary>
        /// Handle window size changes for responsive layout
        /// </summary>
        private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateResponsiveLayout(e.NewSize.Width);
        }

        /// <summary>
        /// Update layout based on window width for responsiveness
        /// </summary>
        private void UpdateResponsiveLayout(double width)
        {
            // Find controls by name for responsive adjustments
            var albumArtBorder = this.FindName("AlbumArtBorder") as FrameworkElement;
            var trackTitleText = this.FindName("TrackTitleText") as TextBlock;

            if (albumArtBorder == null || trackTitleText == null) return;

            if (width < 640)
            {
                // Narrow layout
                albumArtBorder.Width = 96;
                albumArtBorder.Height = 96;
                trackTitleText.FontSize = 20;
            }
            else if (width < 900)
            {
                // Medium layout
                albumArtBorder.Width = 140;
                albumArtBorder.Height = 140;
                trackTitleText.FontSize = 24;
            }
            else
            {
                // Wide layout
                albumArtBorder.Width = 180;
                albumArtBorder.Height = 180;
                trackTitleText.FontSize = 34;
            }
        }

        #region Slider Event Handlers

        /// <summary>
        /// Handle position slider mouse down - start dragging mode
        /// </summary>
        private void PositionSlider_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_playerViewModel == null) return;

            _playerViewModel.IsDraggingSlider = true;
            
            // Calculate clicked position and update immediately
            var slider = sender as Slider;
            if (slider != null)
            {
                var position = e.GetPosition(slider);
                var percentage = position.X / slider.ActualWidth;
                var newValue = (int)(percentage * slider.Maximum);
                
                // Update position optimistically
                _playerViewModel.PositionMs = newValue;
                
                // Send immediate seek command
                _playerViewModel.SeekCommand.Execute(newValue);
            }
        }

        /// <summary>
        /// Handle position slider mouse up - end dragging mode
        /// </summary>
        private void PositionSlider_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_playerViewModel == null) return;

            var slider = sender as Slider;
            if (slider != null)
            {
                // Send final seek command
                _playerViewModel.SeekCommand.Execute((int)slider.Value);
            }
            
            _playerViewModel.IsDraggingSlider = false;
        }

        /// <summary>
        /// Handle position slider value changes during dragging
        /// </summary>
        private void PositionSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_playerViewModel == null || !_playerViewModel.IsDraggingSlider) return;

            // Update ViewModel position (throttled seeking handled in ViewModel)
            _playerViewModel.PositionMs = (int)e.NewValue;
        }

        /// <summary>
        /// Log when the album image binding target changes so we can detect when UI actually shows an image
        /// </summary>
        private void PlayerAlbumImage_SourceChanged(object? sender, EventArgs e)
        {
            try
            {
                var img = sender as Image;
                var src = img?.Source;
                string srcStr = "<null>";
                if (src == null) srcStr = "<null>";
                else if (src is BitmapImage bi) srcStr = bi.UriSource?.ToString() ?? bi.GetType().Name;
                else srcStr = src.GetType().FullName ?? src.GetType().Name;
                var sourceLog = "[ALBUM_UI] PlayerAlbumImage.SourceChanged -> " + (srcStr ?? "<null>") + "\n";
                LoggingService.LogToFile(sourceLog);

                var vm = _locator?.Main?.Player as PlayerViewModel;
                if (vm != null)
                {
                    var hasVmImg = vm.CurrentAlbumArtImage != null ? "true" : "false";
                    var trackArt = vm.CurrentTrack?.AlbumArtUri?.ToString() ?? "<null>";
                    string logMessage = string.Format("[ALBUM_UI] VM State: CurrentAlbumArtImageSet={0}, CurrentTrack.AlbumArtUri={1}\n", hasVmImg, trackArt);
                    LoggingService.LogToFile(logMessage);
                }
            }
            catch (Exception ex)
            {
                var err = "[ALBUM_UI] PlayerAlbumImage_SourceChanged error: " + ex.Message + "\n";
                LoggingService.LogToFile(err);
            }
        }

        /// <summary>
        /// Log when the album art image fails to load
        /// </summary>
        private void PlayerAlbumImage_ImageFailed(object? sender, ExceptionRoutedEventArgs e)
        {
            try
            {
                var img = sender as Image;
                var src = img?.Source as BitmapImage;
                var srcUri = src?.UriSource?.ToString() ?? "<null>";
                string exceptionMsg = e.ErrorException?.Message ?? "<null>";
                string logMessage = string.Format("[ALBUM_UI] PlayerAlbumImage.ImageFailed: source={0} exception={1}\n", srcUri, exceptionMsg);
                LoggingService.LogToFile(logMessage);
            }
            catch (Exception ex)
            {
                var err = "[ALBUM_UI] ImageFailed handler error: " + ex.Message + "\n";
                LoggingService.LogToFile(err);
            }
        }

        private void PlayerAlbumImage_TargetUpdated(object? sender, DataTransferEventArgs e)
        {
            try
            {
                LoggingService.LogToFile($"[ALBUM_UI] PlayerAlbumImage.TargetUpdated event fired (Binding updated)\n");
            }
            catch (Exception ex)
            {
                var err = "[ALBUM_UI] PlayerAlbumImage_TargetUpdated error: " + ex.Message + "\n";
                LoggingService.LogToFile(err);
            }
        }

    // Volume slider now uses binding directly (Player.Volume with UpdateSourceTrigger=PropertyChanged)

        private void VolumeSlider_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            try
            {
                var slider = sender as Slider;
                if (slider == null) return;

                // If the click originated on the Thumb, allow default drag behavior
                var dep = e.OriginalSource as DependencyObject;
                while (dep != null)
                {
                    if (dep is Thumb) return;
                    dep = VisualTreeHelper.GetParent(dep);
                }

                var pt = e.GetPosition(slider);
                double percent = slider.ActualWidth > 0 ? pt.X / slider.ActualWidth : 0.0;
                if (percent < 0) percent = 0;
                if (percent > 1) percent = 1;

                var newValue = slider.Minimum + percent * (slider.Maximum - slider.Minimum);
                slider.Value = newValue;

                // Prevent default small-step behavior so the value jumps directly where clicked
                e.Handled = true;
            }
            catch
            {
                // ignore
            }
        }

        #endregion
    }
}