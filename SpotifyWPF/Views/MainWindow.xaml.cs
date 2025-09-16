using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using SpotifyWPF.Service;
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

        public MainWindow()
        {
            // Get logging service first
            var locatorObj = Application.Current?.Resources["Locator"];
            _locator = locatorObj as SpotifyWPF.ViewModel.ViewModelLocator;
            if (_locator != null)
            {
                // Get logging service from the service locator
                _loggingService = GalaSoft.MvvmLight.Ioc.SimpleIoc.Default.GetInstance<ILoggingService>();
            }

            _loggingService?.LogInfo("MainWindow constructor called");
            InitializeComponent();
            
            // Set window title with version
            SetWindowTitleWithVersion();
            
            // Check for auto-login after the window is loaded
            this.Loaded += MainWindow_Loaded;
            this.Closed += MainWindow_Closed;
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
        /// Handle window loaded event to check for auto-login
        /// </summary>
        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
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

            // Dispose PlayerViewModel
            if (_playerViewModel is IDisposable disposablePlayer)
            {
                disposablePlayer.Dispose();
            }

            // Dispose WebPlaybackBridge
            if (_webPlaybackBridge is IDisposable disposableBridge)
            {
                disposableBridge.Dispose();
            }

            // Clear references
            _playerViewModel = null;
            _webPlaybackBridge = null;
            _locator = null;
            _loggingService = null;
        }

        /// <summary>
        /// Public method called by MainViewModel to initialize player after login
        /// </summary>
        public async Task InitializePlayerAfterLoginAsync()
        {
            if (_locator != null)
            {
                await InitializePlayerAsync(_locator);
            }
        }

        /// <summary>
        /// Initialize the player ViewModel and Web Playback bridge
        /// </summary>
        private async Task InitializePlayerAsync(ViewModelLocator locator)
        {
            _loggingService?.LogInfo($"InitializePlayerAsync called, _isInitialized={_isInitialized}");
            try
            {
                if (_isInitialized) 
                {
                    _loggingService?.LogInfo("InitializePlayerAsync: Already initialized, returning");
                    return;
                }

                _loggingService?.LogInfo("Starting Player Initialization");

                // Create WebPlaybackBridge with the WebView2 control (only if not already created)
                if (_webPlaybackBridge == null)
                {
                    _webPlaybackBridge = new WebPlaybackBridge(WebPlaybackWebView, _loggingService!);
                    _loggingService?.LogInfo("WebPlaybackBridge created");
                }
                else
                {
                    _loggingService?.LogInfo("Reusing existing WebPlaybackBridge");
                }
                
                // Create PlayerViewModel
                var spotify = locator.Main.GetSpotifyService();
                _playerViewModel = new PlayerViewModel(spotify, _webPlaybackBridge, _loggingService!);
                System.Diagnostics.Debug.WriteLine("PlayerViewModel created");
                
                // Set player on MainViewModel
                locator.Main.Player = _playerViewModel;
                System.Diagnostics.Debug.WriteLine("Player set on MainViewModel");
                
                // Get access token and initialize
                var accessToken = await GetAccessTokenAsync();
                if (!string.IsNullOrEmpty(accessToken))
                {
                    System.Diagnostics.Debug.WriteLine($"Access token obtained: {accessToken.Substring(0, Math.Min(20, accessToken.Length))}...");
                    var playerHtmlPath = GetPlayerHtmlPath();
                    System.Diagnostics.Debug.WriteLine($"Player HTML path: {playerHtmlPath}");
                    await _playerViewModel.InitializeAsync(accessToken, playerHtmlPath);
                    System.Diagnostics.Debug.WriteLine("PlayerViewModel initialized successfully");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("ERROR: No access token available");
                }
                
                _isInitialized = true;
                System.Diagnostics.Debug.WriteLine("=== Player Initialization Complete ===");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Player initialization error: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
            }
        }

        /// <summary>
        /// Get the path to the player.html file
        /// </summary>
        private string GetPlayerHtmlPath()
        {
            var appDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            var playerPath = Path.Combine(appDir!, "Assets", "player.html");
            
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
                if (this.DataContext is MainViewModel mainVm)
                {
                    var spotify = mainVm.GetSpotifyService();
                    var token = await spotify.GetCurrentAccessTokenAsync();
                    return token ?? string.Empty;
                }
                return string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        /// <summary>
        /// Handle device menu submenu opening
        /// </summary>
        private async void MenuItem_SubmenuOpened(object sender, RoutedEventArgs e)
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

        private async void MenuItem_MouseEnter(object sender, MouseEventArgs e)
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