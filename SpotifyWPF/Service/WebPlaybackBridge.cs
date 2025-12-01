using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Globalization;
using System.Threading.Tasks;
using System.Text.Json;
using System.Windows;
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
        private readonly WebView2? _webView;
        private readonly ILoggingService _loggingService;
        private string _webPlaybackDeviceId = string.Empty;
        private bool _isInitialized = false;

        public WebPlaybackBridge(WebView2 webView, ILoggingService loggingService)
        {
            _webView = webView ?? throw new ArgumentNullException(nameof(webView));
            _loggingService = loggingService ?? throw new ArgumentNullException(nameof(loggingService));
            
            LoggingService.LogToFile("WebPlaybackBridge constructor called\n");
            LoggingService.LogToFile($"WebPlaybackBridge: WebView2 is null: {_webView == null}\n");
            LoggingService.LogToFile($"WebPlaybackBridge: WebView2.CoreWebView2 is null: {_webView?.CoreWebView2 == null}\n");
        }

        public event Action<PlayerState>? OnPlayerStateChanged;
        public event Action<string>? OnReadyDeviceId;
        public event Action<string>? OnAccountError;

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
            if (_isInitialized)
            {
                LoggingService.LogToFile("WebPlaybackBridge.InitializeAsync - Already initialized, updating token only\n");
                // If already initialized, just update the token on UI thread
                await Application.Current.Dispatcher.InvokeAsync(async () =>
                {
                    if (_webView != null && _webView.CoreWebView2 != null)
                    {
                        var initScript = $"window.initializePlayer('{accessToken}');";
                        var initResult = await _webView.CoreWebView2.ExecuteScriptAsync(initScript);
                        LoggingService.LogToFile($"WebPlaybackBridge.InitializeAsync - Token updated: {initResult}\n");
                    }
                });
                return;
            }

            LoggingService.LogToFile("WebPlaybackBridge.InitializeAsync - Starting\n");

            try
            {
                // Ensure all WebView2 operations are performed on the UI thread
                await Application.Current.Dispatcher.InvokeAsync(async () =>
                {
                    // Wait for CoreWebView2 to be available if it's not already
                    if (_webView == null || _webView.CoreWebView2 == null)
                    {
                        LoggingService.LogToFile("WebPlaybackBridge.InitializeAsync - CoreWebView2 is null, waiting...\n");
                        int attempts = 0;
                        while ((_webView == null || _webView.CoreWebView2 == null) && attempts < 100) // Max 10 seconds
                        {
                            await Task.Delay(100);
                            attempts++;
                            if (attempts % 10 == 0) // Log every second
                            {
                                LoggingService.LogToFile($"WebPlaybackBridge.InitializeAsync - Still waiting for CoreWebView2... attempt {attempts}\n");
                            }
                        }

                        if (_webView == null || _webView.CoreWebView2 == null)
                        {
                            throw new InvalidOperationException("CoreWebView2 is still null after waiting");
                        }
                        LoggingService.LogToFile("WebPlaybackBridge.InitializeAsync - CoreWebView2 is now available\n");
                    }
                    else
                    {
                        LoggingService.LogToFile("WebPlaybackBridge.InitializeAsync - CoreWebView2 already available\n");
                    }

                    // Check if WebView2 is already initialized at the control level
                    if (_webView != null && _webView.CoreWebView2 != null)
                    {
                        LoggingService.LogToFile("WebPlaybackBridge.InitializeAsync - WebView2 control already initialized by MainWindow, configuring for playback\n");
                    }
                    else
                    {
                        throw new InvalidOperationException("WebView2 should be initialized by MainWindow first");
                    }

                    LoggingService.LogToFile("WebPlaybackBridge.InitializeAsync - CoreWebView2 ensured\n");

                    // Configure WebView2 settings (only if not already configured)
                    if (_webView != null && _webView.CoreWebView2 != null)
                    {
                        // Subscribe to web messages from JavaScript (only if not already subscribed)
                        _webView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;

                        LoggingService.LogToFile("WebPlaybackBridge.InitializeAsync - WebMessageReceived handler attached\n");
                    }                    // Navigate to the player HTML file
                    LoggingService.LogToFile($"WebPlaybackBridge.InitializeAsync - Navigating to: {localHtmlPath}\n");
                    if (_webView != null && _webView.CoreWebView2 != null)
                    {
                        try
                        {
                            // Navigate directly to the file URL for now to avoid virtual host mapping issues
                            var assemblyDir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location)!;
                            var binDir = Path.GetDirectoryName(assemblyDir)!;
                            var debugDir = Path.GetDirectoryName(binDir)!;
                            var projectDir = Path.GetDirectoryName(debugDir)!;
                            var playerHtmlPath = Path.Combine(projectDir, "Assets", "player.html");
                            var fileUrl = $"file:///{playerHtmlPath.Replace("\\", "/")}";
                            LoggingService.LogToFile($"WebPlaybackBridge.InitializeAsync - Navigating to: {fileUrl}\n");
                            _webView.CoreWebView2.Navigate(fileUrl);
                        }
                        catch
                        {
                            // Fallback to file URL
                            _webView.CoreWebView2.Navigate(localHtmlPath);
                        }
                    }

                    // Wait for navigation to complete (or just delay for file URLs)
                    try
                    {
                        await WaitForNavigationComplete();
                        LoggingService.LogToFile("WebPlaybackBridge.InitializeAsync - Navigation complete\n");
                    }
                    catch (Exception navEx)
                    {
                        LoggingService.LogToFile($"WebPlaybackBridge.InitializeAsync - Navigation wait failed: {navEx.Message}, continuing anyway\n");
                        await Task.Delay(1000); // Give it a second to load
                    }

                    // Initialize the player with access token
                    // CRITICAL: Get fresh token to ensure it's valid for Web Playback SDK
                    LoggingService.LogToFile("🔑 Using provided access token for player initialization\n");
                    // Use JSON serialization to escape token safely for JavaScript injection
                    var initScript = $"window.initializePlayer({System.Text.Json.JsonSerializer.Serialize(accessToken)});";
                    LoggingService.LogToFile($"WebPlaybackBridge.InitializeAsync - Calling initializePlayer script\n");

                    if (_webView != null && _webView.CoreWebView2 != null)
                    {
                        var initResult = await _webView.CoreWebView2.ExecuteScriptAsync(initScript);
                        LoggingService.LogToFile($"WebPlaybackBridge.InitializeAsync - InitializePlayer returned: {initResult}\n");
                    }
                });

                _isInitialized = true;
            }
            catch (Exception ex)
            {
                LoggingService.LogToFile($"WebPlaybackBridge.InitializeAsync failed: {ex.Message}\n");
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
                await Application.Current.Dispatcher.InvokeAsync(async () =>
                {
                    if (_webView != null && _webView.CoreWebView2 != null)
                    {
                        var result = await _webView.CoreWebView2.ExecuteScriptAsync("window.spotifyBridge.connect()");
                        LoggingService.LogToFile($"WebPlaybackBridge.ConnectAsync - JavaScript returned: {result}\n");
                    }
                    else
                    {
                        throw new InvalidOperationException("WebView2 CoreWebView2 is null");
                    }
                });
            }
            catch (Exception ex)
            {
                LoggingService.LogToFile($"WebPlaybackBridge.ConnectAsync error: {ex.Message}\n");
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
                await Application.Current.Dispatcher.InvokeAsync(async () =>
                {
                    if (_webView != null && _webView.CoreWebView2 != null)
                    {
                        var script = $"window.spotifyBridge && window.spotifyBridge.updateToken && window.spotifyBridge.updateToken('{newAccessToken}')";
                        var result = await _webView.CoreWebView2.ExecuteScriptAsync(script);
                        System.Diagnostics.Debug.WriteLine($"🔑 Token update result: {result}");
                    }
                });
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
            try { LoggingService.LogToFile($"[WEB_PLAY_CALL] PlayAsync called with device={deviceId} uris=[{string.Join(',', uris)}]\n"); } catch { }
            if (!_isInitialized) throw new InvalidOperationException("Bridge not initialized");

            var urisArray = uris.ToArray();

            try
            {
                await Application.Current.Dispatcher.InvokeAsync(async () =>
                {
                    if (_webView != null && _webView.CoreWebView2 != null)
                    {
                        // CRITICAL: Enable audio context first for modern browsers
                        await _webView.CoreWebView2.ExecuteScriptAsync("window.enableSpotifyAudio && window.enableSpotifyAudio()");

                        try { LoggingService.LogToFile("[WEB_PLAY_CALL] Executing JS play (in WebPlaybackBridge.PlayAsync)\n"); } catch { }
                        var urisJson = JsonSerializer.Serialize(urisArray);
                        var escapedJson = urisJson.Replace("\\", "\\\\").Replace("'", "\\'");
                        var script = $"window.spotifyBridge.play({escapedJson})";

                        try { LoggingService.LogToFile("[WEB_PLAY_CALL] Called JS play script\n"); } catch { }
                        var result = await _webView.CoreWebView2.ExecuteScriptAsync(script);
                        System.Diagnostics.Debug.WriteLine($"🎵 Play command sent: {result}");
                    }
                });
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

            try
            {
                await Application.Current.Dispatcher.InvokeAsync(async () =>
                {
                    if (_webView != null && _webView.CoreWebView2 != null)
                    {
                        // Enable audio context for user interaction
                        await _webView.CoreWebView2.ExecuteScriptAsync("window.enableSpotifyAudio && window.enableSpotifyAudio()");

                        await _webView.CoreWebView2.ExecuteScriptAsync("window.spotifyBridge.pause()");
                    }
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ PauseAsync error: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Resume playback
        /// </summary>
        public async Task ResumeAsync()
        {
            if (!_isInitialized) throw new InvalidOperationException("Bridge not initialized");

            try
            {
                await Application.Current.Dispatcher.InvokeAsync(async () =>
                {
                    if (_webView != null && _webView.CoreWebView2 != null)
                    {
                        // Enable audio context for user interaction
                        await _webView.CoreWebView2.ExecuteScriptAsync("window.enableSpotifyAudio && window.enableSpotifyAudio()");

                        await _webView.CoreWebView2.ExecuteScriptAsync("window.spotifyBridge.resume()");
                    }
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ ResumeAsync error: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Seek to position
        /// </summary>
        public async Task SeekAsync(int positionMs)
        {
            if (!_isInitialized) throw new InvalidOperationException("Bridge not initialized");

            try
            {
                await Application.Current.Dispatcher.InvokeAsync(async () =>
                {
                    if (_webView != null && _webView.CoreWebView2 != null)
                    {
                        var script = $"window.spotifyBridge.seek({positionMs})";
                        await _webView.CoreWebView2.ExecuteScriptAsync(script);
                    }
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ SeekAsync error: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Set volume level
        /// </summary>
        public async Task SetVolumeAsync(double volume)
        {
            if (!_isInitialized) throw new InvalidOperationException("Bridge not initialized");

            try
            {
                await Application.Current.Dispatcher.InvokeAsync(async () =>
                {
                    if (_webView != null && _webView.CoreWebView2 != null)
                    {
                        // Use invariant culture and higher precision to avoid unintended zeroing at low volumes
                        var volString = volume.ToString("0.#####", CultureInfo.InvariantCulture);
                        var script = $"window.spotifyBridge.setVolume({volString})";
                        System.Diagnostics.Debug.WriteLine($"WebPlaybackBridge: setVolume({volString})");
                        await _webView.CoreWebView2.ExecuteScriptAsync(script);
                    }
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ SetVolumeAsync error: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Get current player state
        /// </summary>
        public async Task<PlayerState?> GetStateAsync()
        {
            if (!_isInitialized) throw new InvalidOperationException("Bridge not initialized");

            try
            {
                PlayerState? result = null;
                await Application.Current.Dispatcher.InvokeAsync(async () =>
                {
                    if (_webView != null && _webView.CoreWebView2 != null)
                    {
                        var scriptResult = await _webView.CoreWebView2.ExecuteScriptAsync("window.spotifyBridge.getState()");
                        if (string.IsNullOrWhiteSpace(scriptResult) || scriptResult == "null")
                            return;

                        // WebView2 returns a JSON representation as a string. If it's quoted, unescape first.
                        string json;
                        if (scriptResult.Length > 0 && scriptResult[0] == '"')
                        {
                            // Deserialize to string to unescape inner JSON
                            try
                            {
                                json = JsonSerializer.Deserialize<string>(scriptResult) ?? string.Empty;
                            }
                            catch
                            {
                                // Fallback: trim quotes if deserialization fails
                                json = scriptResult.Trim('"');
                            }
                        }
                        else
                        {
                            json = scriptResult;
                        }

                        if (string.IsNullOrWhiteSpace(json) || json == "null")
                            return;

                        result = JsonSerializer.Deserialize<PlayerState>(json);
                    }
                });
                return result;
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
                try { LoggingService.LogToFile($"[WEB_MSG] OnWebMessageReceived: {e.TryGetWebMessageAsString()}\n"); } catch { }
                LoggingService.LogToFile($"=== OnWebMessageReceived CALLED ===\n");
                var message = e.TryGetWebMessageAsString();
                LoggingService.LogToFile($"WebPlaybackBridge received message: {message}\n");

                if (string.IsNullOrEmpty(message))
                {
                    try { LoggingService.LogToFile($"[WEB_MSG] Bridged message empty or null: '{message}'\n"); } catch { }
                    LoggingService.LogToFile("Message is null or empty!\n");
                    return;
                }

                var messageJson = JsonSerializer.Deserialize<JsonElement>(message);
                
                if (messageJson.TryGetProperty("type", out var typeProperty))
                {
                    var messageType = typeProperty.GetString();
                    LoggingService.LogToFile($"=== WebPlaybackBridge message received ===\n");
                    LoggingService.LogToFile($"Message type: {messageType}\n");
                    
                    switch (messageType)
                    {
                        case "ready":
                            if (messageJson.TryGetProperty("device_id", out var deviceProperty))
                            {
                                var deviceId = deviceProperty.GetString() ?? string.Empty;
                                LoggingService.LogToFile($"🎵 WebPlaybackBridge received device ID: {deviceId}\n");
                                _webPlaybackDeviceId = deviceId;
                                OnReadyDeviceId?.Invoke(deviceId);
                            }
                            break;
                            
                        case "account_error":
                            if (messageJson.TryGetProperty("message", out var errorMessageProperty))
                            {
                                var errorMessage = errorMessageProperty.GetString() ?? string.Empty;
                                LoggingService.LogToFile($"🚨 WebPlaybackBridge received account error: {errorMessage}\n");
                                OnAccountError?.Invoke(errorMessage);
                            }
                            break;
                            
                        case "state":
                            if (messageJson.TryGetProperty("state", out var stateProperty))
                            {
                                LoggingService.LogToFile($"📡 WebPlaybackBridge received state update: {stateProperty.GetRawText()}\n");
                                
                                try
                                {
                                    var playerState = JsonSerializer.Deserialize<PlayerState>(stateProperty.GetRawText());
                                    if (playerState != null)
                                    {
                                        LoggingService.LogToFile($"🎵 Parsed player state - Track: {playerState.TrackName}, Playing: {playerState.IsPlaying}, Position: {playerState.PositionMs}ms\n");
                                        OnPlayerStateChanged?.Invoke(playerState);
                                    }
                                    else
                                    {
                                        LoggingService.LogToFile("⚠️ Player state is null\n");
                                    }
                                }
                                catch (JsonException jsonEx)
                                {
                                    LoggingService.LogToFile($"❌ Error deserializing player state: {jsonEx.Message}\n");
                                    LoggingService.LogToFile($"Raw JSON: {stateProperty.GetRawText()}\n");
                                }
                            }
                            break;
                            
                        case "state_changed": // Legacy support
                            if (messageJson.TryGetProperty("state", out var legacyStateProperty))
                            {
                                LoggingService.LogToFile($"📡 WebPlaybackBridge received legacy state_changed: {legacyStateProperty.GetRawText()}\n");
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
                LoggingService.LogToFile($"WebPlaybackBridge.OnWebMessageReceived error: {ex.Message}\n");
            }
        }

        private async Task WaitForNavigationComplete()
        {
            var completionSource = new TaskCompletionSource<bool>();
            
            void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
            {
                if (_webView != null && _webView.CoreWebView2 != null)
                {
                    _webView.CoreWebView2.NavigationCompleted -= OnNavigationCompleted;
                }
                completionSource.SetResult(e.IsSuccess);
            }
            
            if (_webView != null && _webView.CoreWebView2 != null)
            {
                _webView.CoreWebView2.NavigationCompleted += OnNavigationCompleted;
            }
            
            // Wait up to 10 seconds for navigation to complete
            var timeoutTask = Task.Delay(10000);
            var completedTask = await Task.WhenAny(completionSource.Task, timeoutTask);
            
            if (completedTask == timeoutTask)
            {
                if (_webView != null && _webView.CoreWebView2 != null)
                {
                    _webView.CoreWebView2.NavigationCompleted -= OnNavigationCompleted;
                }
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
        /// Finalizer for safety net (though not strictly necessary for managed resources)
        /// </summary>
        ~WebPlaybackBridge()
        {
            Dispose(false);
        }

        /// <summary>
        /// Dispose implementation with proper resource ordering
        /// </summary>
        protected virtual void Dispose(bool disposing)
        {
            if (_disposed)
                return;

            if (disposing)
            {
                // Dispose managed resources in reverse order of creation/allocation

                try
                {
                    // 1. First, clean up event handlers to prevent any further callbacks
                    if (_webView?.CoreWebView2 != null)
                    {
                        _webView.CoreWebView2.WebMessageReceived -= OnWebMessageReceived;
                        _webView.CoreWebView2.WebResourceRequested -= OnWebResourceRequested;
                    }
                }
                catch (Exception ex)
                {
                    LoggingService.LogToFile($"Error removing event handlers in WebPlaybackBridge.Dispose: {ex.Message}\n");
                }

                try
                {
                    // 2. Stop any ongoing WebView2 operations
                    if (_webView?.CoreWebView2 != null)
                    {
                        _webView.CoreWebView2.Stop();

                        // Navigate to blank page to stop any ongoing operations
                        _webView.CoreWebView2.Navigate("about:blank");
                    }
                }
                catch (Exception ex)
                {
                    LoggingService.LogToFile($"Error stopping WebView2 operations in WebPlaybackBridge.Dispose: {ex.Message}\n");
                }

                try
                {
                    // 3. Dispose the WebView2 control itself
                    _webView?.Dispose();
                }
                catch (Exception ex)
                {
                    LoggingService.LogToFile($"Error disposing WebView2 control in WebPlaybackBridge.Dispose: {ex.Message}\n");
                }

                // 4. Clear event handlers to prevent memory leaks
                OnPlayerStateChanged = null;
                OnReadyDeviceId = null;
                OnAccountError = null;
            }

            // 5. Reset state variables
            _webPlaybackDeviceId = string.Empty;
            _isInitialized = false;

            _disposed = true;
        }

        #endregion
    }
}
