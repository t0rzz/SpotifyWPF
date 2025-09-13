using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Globalization;
using System.Threading.Tasks;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using SpotifyWPF.Model;

namespace SpotifyWPF.Service
{
    /// <summary>
    /// WebView2-based Web Playback SDK bridge implementation
    /// </summary>
    public class WebPlaybackBridge : IWebPlaybackBridge, IDisposable
    {
        private readonly WebView2 _webView;
        private readonly ILoggingService _loggingService;
        private string _webPlaybackDeviceId = string.Empty;
        private bool _isInitialized = false;

        public WebPlaybackBridge(WebView2 webView, ILoggingService loggingService)
        {
            _webView = webView ?? throw new ArgumentNullException(nameof(webView));
            _loggingService = loggingService ?? throw new ArgumentNullException(nameof(loggingService));
        }

        public event Action<PlayerState>? OnPlayerStateChanged;
        public event Action<string>? OnReadyDeviceId;

        /// <summary>
        /// Handle web resource requests - allows loading external resources like Spotify SDK
        /// </summary>
        private void OnWebResourceRequested(object? sender, CoreWebView2WebResourceRequestedEventArgs e)
        {
            // Allow all requests - silent logging for working API calls
        }

        /// <summary>
        /// Initialize WebView2 with player HTML and access token
        /// </summary>
        public async Task InitializeAsync(string accessToken, string localHtmlPath)
        {
            if (_isInitialized) return;

            System.Diagnostics.Debug.WriteLine("WebPlaybackBridge.InitializeAsync - Starting");
            
            try
            {
                // Create a user data folder in a writable location to avoid access denied errors
                var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                var userDataFolder = Path.Combine(appDataPath, "SpotifyWPF", "WebView2");
                
                // Ensure the directory exists
                Directory.CreateDirectory(userDataFolder);
                
                System.Diagnostics.Debug.WriteLine($"WebPlaybackBridge.InitializeAsync - Using user data folder: {userDataFolder}");
                
                // Check if WebView2 is already initialized
                bool isFirstInit = _webView.CoreWebView2 == null;
                if (isFirstInit)
                {
                    System.Diagnostics.Debug.WriteLine("WebPlaybackBridge.InitializeAsync - Initializing WebView2 for first time");
                    // Initialize WebView2 with custom environment
                    var options = new CoreWebView2EnvironmentOptions("--autoplay-policy=no-user-gesture-required");
                    var environment = await CoreWebView2Environment.CreateAsync(null, userDataFolder, options);
                    await _webView.EnsureCoreWebView2Async(environment);
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("WebPlaybackBridge.InitializeAsync - WebView2 already initialized, skipping");
                }
                
                System.Diagnostics.Debug.WriteLine("WebPlaybackBridge.InitializeAsync - CoreWebView2 ensured");

                // Configure WebView2 settings (only if not already configured)
                if (_webView.CoreWebView2 != null)
                {
                    _webView.CoreWebView2.Settings.AreDefaultScriptDialogsEnabled = false;
                    // Disable DevTools for normal usage
                    _webView.CoreWebView2.Settings.AreDevToolsEnabled = false;
                    
                    // Ensure audio is allowed and not muted
                    _webView.CoreWebView2.IsMuted = false;
                
                    // **CRITICAL: Set Chrome User-Agent for Spotify compatibility**
                    _webView.CoreWebView2.Settings.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";
                    System.Diagnostics.Debug.WriteLine($"🌐 WebView2 User-Agent set to Chrome for Spotify compatibility");
                    
                    // **NEW: Enable external resource loading**
                    _webView.CoreWebView2.Settings.IsGeneralAutofillEnabled = true;
                    _webView.CoreWebView2.Settings.IsPasswordAutosaveEnabled = false;
                    _webView.CoreWebView2.Settings.IsSwipeNavigationEnabled = false;
                    
                    // Allow media playback as much as possible
                    _webView.CoreWebView2.Settings.IsStatusBarEnabled = false;
                    
                    // Grant media permissions
                    _webView.CoreWebView2.PermissionRequested += (s, e) =>
                    {
                        if (e.PermissionKind == CoreWebView2PermissionKind.Camera ||
                            e.PermissionKind == CoreWebView2PermissionKind.Microphone)
                        {
                            e.State = CoreWebView2PermissionState.Allow;
                        }
                    };
                    
                    // **NEW: Allow all web resource requests (including external CDN)**
                    _webView.CoreWebView2.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
                    _webView.CoreWebView2.WebResourceRequested += OnWebResourceRequested;

                    // Subscribe to web messages from JavaScript
                    _webView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;

                    // Try to clear cached data to avoid stale JS/HTML being used
                    try
                    {
                        // Use a version-tolerant approach: prefer "All" when available, otherwise OR known kinds by reflection
                        CoreWebView2BrowsingDataKinds kinds;
                        if (Enum.TryParse("All", out kinds))
                        {
                            _ = _webView.CoreWebView2.Profile.ClearBrowsingDataAsync(kinds);
                        }
                        else
                        {
                            // Build a combined mask of commonly used kinds if "All" is not present in this SDK version
                            ulong combined = 0UL;
                            string[] names = new[] { "Cookies", "CacheStorage", "DomStorage", "FileSystems", "IndexedDb", "ServiceWorkers", "WebSql", "AllSite" };
                            foreach (var name in names)
                            {
                                if (Enum.TryParse(typeof(CoreWebView2BrowsingDataKinds), name, out var valueObj) && valueObj is Enum)
                                {
                                    combined |= Convert.ToUInt64(valueObj);
                                }
                            }

                            if (combined != 0UL)
                            {
                                var combinedKinds = (CoreWebView2BrowsingDataKinds)combined;
                                _ = _webView.CoreWebView2.Profile.ClearBrowsingDataAsync(combinedKinds);
                            }
                        }

                        System.Diagnostics.Debug.WriteLine("🧹 Requested WebView2 cache clear (Profile.ClearBrowsingDataAsync)");
                    }
                    catch (Exception cacheEx)
                    {
                        System.Diagnostics.Debug.WriteLine($"⚠️ Could not clear WebView2 cache: {cacheEx.Message}");
                    }
                }

                // Navigate to the player HTML (only on first init)
                if (isFirstInit)
                {
                    System.Diagnostics.Debug.WriteLine($"WebPlaybackBridge.InitializeAsync - Navigating to: {localHtmlPath}");
                    if (_webView.CoreWebView2 != null)
                    {
                        try
                        {
                            // Map a virtual HTTPS host to the local app directory to satisfy EME/CORS if needed
                            var appDir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location)!;
                            _webView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                                "app",
                                appDir,
                                CoreWebView2HostResourceAccessKind.Allow);
                            // Use https://app for local assets
                            // Append a cache-busting query to avoid stale cached HTML/JS
                            var cacheBust = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                            var httpsUrl = $"https://app/Assets/player.html?v={cacheBust}";
                            _webView.CoreWebView2.Navigate(httpsUrl);
                        }
                        catch
                        {
                            // Fallback to file URL
                            _webView.CoreWebView2.Navigate(localHtmlPath);
                        }
                    }

                    // Wait for navigation to complete (only on first init)
                    await WaitForNavigationComplete();
                    System.Diagnostics.Debug.WriteLine("WebPlaybackBridge.InitializeAsync - Navigation complete");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("WebPlaybackBridge.InitializeAsync - Skipping navigation (already initialized)");
                }

                // Initialize the player with access token
                // CRITICAL: Get fresh token to ensure it's valid for Web Playback SDK
                System.Diagnostics.Debug.WriteLine("🔑 Using provided access token for player initialization");
                var initScript = $"window.initializePlayer('{accessToken}');";
                System.Diagnostics.Debug.WriteLine($"WebPlaybackBridge.InitializeAsync - Calling initializePlayer script");
                
                if (_webView.CoreWebView2 != null)
                {
                    var initResult = await _webView.CoreWebView2.ExecuteScriptAsync(initScript);
                    System.Diagnostics.Debug.WriteLine($"WebPlaybackBridge.InitializeAsync - InitializePlayer returned: {initResult}");
                }

                _isInitialized = true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"WebPlaybackBridge.InitializeAsync failed: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Connect to Web Playback SDK
        /// </summary>
        public async Task ConnectAsync()
        {
            if (!_isInitialized) throw new InvalidOperationException("Bridge not initialized");
            
            System.Diagnostics.Debug.WriteLine("WebPlaybackBridge.ConnectAsync - Calling JavaScript connect()");
            
            try
            {
                var result = await _webView.CoreWebView2.ExecuteScriptAsync("window.spotifyBridge.connect()");
                System.Diagnostics.Debug.WriteLine($"WebPlaybackBridge.ConnectAsync - JavaScript returned: {result}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"WebPlaybackBridge.ConnectAsync error: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Get the Web Playback device ID
        /// </summary>
        public Task<string> GetWebPlaybackDeviceIdAsync()
        {
            return Task.FromResult(_webPlaybackDeviceId);
        }

        /// <summary>
        /// Update access token for an already initialized player
        /// </summary>
        public async Task UpdateTokenAsync(string newAccessToken)
        {
            if (!_isInitialized) throw new InvalidOperationException("Bridge not initialized");
            
            System.Diagnostics.Debug.WriteLine("🔑 Updating player token...");
            
            try
            {
                var script = $"window.spotifyBridge && window.spotifyBridge.updateToken && window.spotifyBridge.updateToken('{newAccessToken}')";
                var result = await _webView.CoreWebView2.ExecuteScriptAsync(script);
                System.Diagnostics.Debug.WriteLine($"🔑 Token update result: {result}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ UpdateToken error: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Play tracks on specified device
        /// </summary>
        public async Task PlayAsync(IEnumerable<string> uris, string? deviceId = null)
        {
            if (!_isInitialized) throw new InvalidOperationException("Bridge not initialized");

            var urisArray = uris.ToArray();
            
            try
            {
                // CRITICAL: Enable audio context first for modern browsers
                await _webView.CoreWebView2.ExecuteScriptAsync("window.enableSpotifyAudio && window.enableSpotifyAudio()");
                
                var urisJson = JsonSerializer.Serialize(urisArray);
                var escapedJson = urisJson.Replace("\\", "\\\\").Replace("'", "\\'");
                var script = $"window.spotifyBridge.play({escapedJson})";
                
                var result = await _webView.CoreWebView2.ExecuteScriptAsync(script);
                System.Diagnostics.Debug.WriteLine($"🎵 Play command sent: {result}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ PlayAsync error: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Pause playback
        /// </summary>
        public async Task PauseAsync()
        {
            if (!_isInitialized) throw new InvalidOperationException("Bridge not initialized");
            
            // Enable audio context for user interaction
            await _webView.CoreWebView2.ExecuteScriptAsync("window.enableSpotifyAudio && window.enableSpotifyAudio()");
            
            await _webView.CoreWebView2.ExecuteScriptAsync("window.spotifyBridge.pause()");
        }

        /// <summary>
        /// Resume playback
        /// </summary>
        public async Task ResumeAsync()
        {
            if (!_isInitialized) throw new InvalidOperationException("Bridge not initialized");
            
            // Enable audio context for user interaction
            await _webView.CoreWebView2.ExecuteScriptAsync("window.enableSpotifyAudio && window.enableSpotifyAudio()");
            
            await _webView.CoreWebView2.ExecuteScriptAsync("window.spotifyBridge.resume()");
        }

        /// <summary>
        /// Seek to position
        /// </summary>
        public async Task SeekAsync(int positionMs)
        {
            if (!_isInitialized) throw new InvalidOperationException("Bridge not initialized");
            
            var script = $"window.spotifyBridge.seek({positionMs})";
            await _webView.CoreWebView2.ExecuteScriptAsync(script);
        }

        /// <summary>
        /// Set volume level
        /// </summary>
        public async Task SetVolumeAsync(double volume)
        {
            if (!_isInitialized) throw new InvalidOperationException("Bridge not initialized");
            
            // Use invariant culture and higher precision to avoid unintended zeroing at low volumes
            var volString = volume.ToString("0.#####", CultureInfo.InvariantCulture);
            var script = $"window.spotifyBridge.setVolume({volString})";
            System.Diagnostics.Debug.WriteLine($"WebPlaybackBridge: setVolume({volString})");
            await _webView.CoreWebView2.ExecuteScriptAsync(script);
        }

        /// <summary>
        /// Get current player state
        /// </summary>
        public async Task<PlayerState?> GetStateAsync()
        {
            if (!_isInitialized) throw new InvalidOperationException("Bridge not initialized");
            
            try
            {
                var result = await _webView.CoreWebView2.ExecuteScriptAsync("window.spotifyBridge.getState()");
                if (string.IsNullOrWhiteSpace(result) || result == "null")
                    return null;

                // WebView2 returns a JSON representation as a string. If it's quoted, unescape first.
                string json;
                if (result.Length > 0 && result[0] == '"')
                {
                    // Deserialize to string to unescape inner JSON
                    try
                    {
                        json = JsonSerializer.Deserialize<string>(result) ?? string.Empty;
                    }
                    catch
                    {
                        // Fallback: trim quotes if deserialization fails
                        json = result.Trim('"');
                    }
                }
                else
                {
                    json = result;
                }

                if (string.IsNullOrWhiteSpace(json) || json == "null")
                    return null;

                return JsonSerializer.Deserialize<PlayerState>(json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"WebPlaybackBridge.GetStateAsync error: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Handle web messages from JavaScript
        /// </summary>
        private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"=== OnWebMessageReceived CALLED ===");
                var message = e.TryGetWebMessageAsString();
                System.Diagnostics.Debug.WriteLine($"WebPlaybackBridge received message: {message}");

                if (string.IsNullOrEmpty(message)) 
                {
                    System.Diagnostics.Debug.WriteLine("Message is null or empty!");
                    return;
                }

                var messageJson = JsonSerializer.Deserialize<JsonElement>(message);
                
                if (messageJson.TryGetProperty("type", out var typeProperty))
                {
                    var messageType = typeProperty.GetString();
                    System.Diagnostics.Debug.WriteLine($"=== WebPlaybackBridge message received ===");
                    System.Diagnostics.Debug.WriteLine($"Message type: {messageType}");
                    
                    switch (messageType)
                    {
                        case "ready":
                            if (messageJson.TryGetProperty("device_id", out var deviceProperty))
                            {
                                var deviceId = deviceProperty.GetString() ?? string.Empty;
                                System.Diagnostics.Debug.WriteLine($"🎵 WebPlaybackBridge received device ID: {deviceId}");
                                _webPlaybackDeviceId = deviceId;
                                OnReadyDeviceId?.Invoke(deviceId);
                            }
                            break;
                            
                        case "state":
                            if (messageJson.TryGetProperty("state", out var stateProperty))
                            {
                                System.Diagnostics.Debug.WriteLine($"📡 WebPlaybackBridge received state update: {stateProperty.GetRawText()}");
                                
                                try
                                {
                                    var playerState = JsonSerializer.Deserialize<PlayerState>(stateProperty.GetRawText());
                                    if (playerState != null)
                                    {
                                        System.Diagnostics.Debug.WriteLine($"🎵 Parsed player state - Track: {playerState.TrackName}, Playing: {playerState.IsPlaying}, Position: {playerState.PositionMs}ms");
                                        OnPlayerStateChanged?.Invoke(playerState);
                                    }
                                    else
                                    {
                                        System.Diagnostics.Debug.WriteLine("⚠️ Player state is null");
                                    }
                                }
                                catch (JsonException jsonEx)
                                {
                                    System.Diagnostics.Debug.WriteLine($"❌ Error deserializing player state: {jsonEx.Message}");
                                    System.Diagnostics.Debug.WriteLine($"Raw JSON: {stateProperty.GetRawText()}");
                                }
                            }
                            break;
                            
                        case "state_changed": // Legacy support
                            if (messageJson.TryGetProperty("state", out var legacyStateProperty))
                            {
                                System.Diagnostics.Debug.WriteLine($"📡 WebPlaybackBridge received legacy state_changed: {legacyStateProperty.GetRawText()}");
                                var playerState = JsonSerializer.Deserialize<PlayerState>(legacyStateProperty.GetRawText());
                                if (playerState != null)
                                {
                                    OnPlayerStateChanged?.Invoke(playerState);
                                }
                            }
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"WebPlaybackBridge.OnWebMessageReceived error: {ex.Message}");
            }
        }

        /// <summary>
        /// Wait for navigation to complete
        /// </summary>
        private async Task WaitForNavigationComplete()
        {
            var completionSource = new TaskCompletionSource<bool>();
            
            void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
            {
                _webView.CoreWebView2.NavigationCompleted -= OnNavigationCompleted;
                completionSource.SetResult(e.IsSuccess);
            }
            
            _webView.CoreWebView2.NavigationCompleted += OnNavigationCompleted;
            
            // Wait up to 10 seconds for navigation to complete
            var timeoutTask = Task.Delay(10000);
            var completedTask = await Task.WhenAny(completionSource.Task, timeoutTask);
            
            if (completedTask == timeoutTask)
            {
                _webView.CoreWebView2.NavigationCompleted -= OnNavigationCompleted;
                throw new TimeoutException("Navigation timed out");
            }
            
            if (!completionSource.Task.Result)
            {
                throw new InvalidOperationException("Navigation failed");
            }
        }

        #region IDisposable Implementation

        private bool _disposed = false;

        /// <summary>
        /// Dispose WebView2 resources
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Dispose implementation
        /// </summary>
        protected virtual void Dispose(bool disposing)
        {
            if (_disposed)
                return;

            if (disposing)
            {
                // Dispose WebView2 resources
                try
                {
                    if (_webView?.CoreWebView2 != null)
                    {
                        // Remove event handlers
                        _webView.CoreWebView2.WebMessageReceived -= OnWebMessageReceived;
                        _webView.CoreWebView2.WebResourceRequested -= OnWebResourceRequested;
                    }
                }
                catch (Exception ex)
                {
                    _loggingService?.LogError($"Error disposing WebPlaybackBridge: {ex.Message}");
                }
            }

            _disposed = true;
        }

        #endregion
    }
}
