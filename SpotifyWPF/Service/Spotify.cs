using SpotifyAPI.Web;
using SpotifyAPI.Web.Auth;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using System.Windows;
using SpotifyWPF.Model.Dto;
using System.Linq;
using System.IO;
using System.Security.Cryptography;
using SpotifyWPF.View;
using System.Text;

namespace SpotifyWPF.Service
{
    /// <summary>
    /// Exception thrown when Spotify API rate limits are exceeded
    /// </summary>
    public class RateLimitException : Exception
    {
        public RateLimitException(string message, Exception? innerException = null)
            : base(message, innerException)
        {
        }
    }

    /// <summary>
    /// Exception thrown when Spotify API returns 403 Forbidden, typically due to missing OAuth scopes.
    /// The user may need to log out and log back in to re-authorize with the required scopes.
    /// </summary>
    public class ForbiddenException : Exception
    {
        public string? RequiredScope { get; }

        public ForbiddenException(string message, string? requiredScope = null, Exception? innerException = null)
            : base(message, innerException)
        {
            RequiredScope = requiredScope;
        }
    }

    public class Spotify : ISpotify, IDisposable
    {
        private readonly ITokenProvider _tokenProvider;
        private readonly ISettingsProvider _settingsProvider;
        private readonly ILoggingService _loggingService;

        private readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);

        private readonly EmbedIOAuthServer _server;

        private PrivateUser? _privateProfile;

        private Action? _loginSuccessAction;

        private readonly TokenStorage _tokenStorage = new TokenStorage();
        private TokenInfo? _currentToken;
        private DateTime _lastAuthCheck = DateTime.MinValue;

        // Coordina una sola ri-autenticazione per volta tra più worker
        private readonly SemaphoreSlim _authSemaphore = new SemaphoreSlim(1, 1);
        private TaskCompletionSource<bool>? _reauthTcs;

        public ISpotifyClient? Api { get; private set; } = null;

        public PrivateUser? CurrentUser => _privateProfile;

        public Spotify(ISettingsProvider settingsProvider, ILoggingService loggingService, ITokenProvider tokenProvider)
        {
            _settingsProvider = settingsProvider ?? throw new ArgumentNullException(nameof(settingsProvider));
            _loggingService = loggingService ?? throw new ArgumentNullException(nameof(loggingService));
            _tokenProvider = tokenProvider ?? throw new ArgumentNullException(nameof(tokenProvider));

            // Check if port is already in use
            var port = int.Parse(_settingsProvider.SpotifyRedirectPort);
            if (IsPortInUse(port))
            {
                _loggingService.LogWarning($"OAuth port {port} is already in use - this may cause authorization issues");
                System.Diagnostics.Debug.WriteLine($"Warning: OAuth port {port} is already in use");
            }

            _server = new EmbedIOAuthServer(
                new Uri($"http://127.0.0.1:{_settingsProvider.SpotifyRedirectPort}"),
                int.Parse(_settingsProvider.SpotifyRedirectPort));

            _server.AuthorizationCodeReceived += OnAuthorizationCodeReceived;
            _server.ErrorReceived += OnErrorReceived;

            // Esplicito per evitare CS0649 (il campo viene comunque impostato in EnsureAuthenticatedAsync quando serve)
            _reauthTcs = null;
        }

        private static bool IsPortInUse(int port)
        {
            try
            {
                using (var client = new System.Net.Sockets.TcpClient())
                {
                    var result = client.BeginConnect("127.0.0.1", port, null, null);
                    var success = result.AsyncWaitHandle.WaitOne(Constants.PortCheckTimeoutMs); // 100ms timeout
                    if (success)
                    {
                        client.EndConnect(result);
                        return true;
                    }
                }
            }
            catch
            {
                // Port is available
            }
            return false;
        }

        private static void InvokeOnUiThread(Action? action)
        {
            if (action == null) return;
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher != null)
            {
                dispatcher.BeginInvoke((Action)(() => action()));
            }
            else
            {
                action();
            }
        }

        #region Input Validation Helpers

        private void ValidateSpotifyId(string id, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentException($"Parameter '{parameterName}' cannot be null, empty, or whitespace.", parameterName);

            // Spotify IDs are typically 22 characters long and contain only alphanumeric characters
            if (id.Length != 22 || !id.All(c => char.IsLetterOrDigit(c)))
                throw new ArgumentException($"Parameter '{parameterName}' must be a valid 22-character Spotify ID.", parameterName);
        }

        private void ValidateDeviceId(string deviceId, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(deviceId))
                throw new ArgumentException($"Parameter '{parameterName}' cannot be null, empty, or whitespace.", parameterName);

            // Device IDs can be longer than 22 chars and may contain special characters
            if (deviceId.Length < 10 || deviceId.Length > 100)
                throw new ArgumentException($"Parameter '{parameterName}' must be between 10 and 100 characters.", parameterName);
        }

        private void ValidatePaginationParameters(int offset, int limit, string methodName)
        {
            if (offset < 0)
                throw new ArgumentOutOfRangeException(nameof(offset), $"Parameter 'offset' in {methodName} cannot be negative.");

            if (limit < 1 || limit > 50)
                throw new ArgumentOutOfRangeException(nameof(limit), $"Parameter 'limit' in {methodName} must be between 1 and 50.");
        }

        private void ValidateVolumePercent(int volumePercent)
        {
            if (volumePercent < 0 || volumePercent > 100)
                throw new ArgumentOutOfRangeException(nameof(volumePercent), "Volume percent must be between 0 and 100.");
        }

        private void ValidatePositionMs(int positionMs)
        {
            if (positionMs < 0)
                throw new ArgumentOutOfRangeException(nameof(positionMs), "Position must be non-negative.");
        }

        private void ValidateCollection<T>(IEnumerable<T> collection, string parameterName)
        {
            if (collection == null)
                throw new ArgumentNullException(parameterName);

            var list = collection.ToList();
            if (list.Count == 0)
                throw new ArgumentException($"Parameter '{parameterName}' cannot be empty.", parameterName);

            if (list.Count > 50)
                throw new ArgumentException($"Parameter '{parameterName}' cannot contain more than 50 items.", parameterName);
        }

        private void ValidateTrackCollection(IEnumerable<string> trackUris, string parameterName)
        {
            if (trackUris == null)
                throw new ArgumentNullException(parameterName);

            var list = trackUris.ToList();
            if (list.Count == 0)
                throw new ArgumentException($"Parameter '{parameterName}' cannot be empty.", parameterName);

            // Note: Individual API calls are limited to 100 tracks, but batching is handled at the ViewModel level
            // So we allow more than 100 tracks here, but the method will process them in batches
        }

        private void ValidateRepeatState(string state)
        {
            if (string.IsNullOrWhiteSpace(state))
                throw new ArgumentException("Repeat state cannot be null, empty, or whitespace.", nameof(state));

            var normalizedState = state.ToLowerInvariant();
            if (normalizedState != "off" && normalizedState != "track" && normalizedState != "context")
                throw new ArgumentException("Repeat state must be one of: 'off', 'track', or 'context'.", nameof(state));
        }

        #endregion

        public async Task LoginAsync(Action? onSuccess)
        {
            _loginSuccessAction = onSuccess;

            _loggingService.LogInfo("Starting login process");

            // Prova token valido da storage
            var saved = _tokenStorage.Load();
            if (saved != null && !string.IsNullOrWhiteSpace(saved.AccessToken) && saved.ExpiresAtUtc > DateTime.UtcNow.AddMinutes(5))
            {
                var timeUntilExpiry = saved.ExpiresAtUtc - DateTime.UtcNow;
                _loggingService.LogInfo($"Using saved token (expires in {timeUntilExpiry.TotalHours:F1} hours)");
                _currentToken = saved;
                Api = new SpotifyClient(saved.AccessToken);
                InvokeOnUiThread(_loginSuccessAction);
                return;
            }

            // Token scaduto ma con refresh token: tenta refresh silenzioso
            if (saved != null && !string.IsNullOrWhiteSpace(saved.RefreshToken))
            {
                _loggingService.LogInfo("Attempting silent refresh with saved refresh token");
                var refreshed = await TryRefreshAsync(saved.RefreshToken).ConfigureAwait(false);
                if (refreshed)
                {
                    _loggingService.LogInfo("Silent refresh successful");
                    InvokeOnUiThread(_loginSuccessAction);
                    return;
                }
                _loggingService.LogWarning("Silent refresh failed");
            }

            _loggingService.LogInfo("Starting interactive OAuth login");
            // Avvia PKCE interactive login
            try
            {
                await _server.Start().ConfigureAwait(false);
                _loggingService.LogInfo($"OAuth server started on port {_settingsProvider.SpotifyRedirectPort}");
            }
            catch (Exception ex)
            {
                _loggingService.LogError($"Failed to start OAuth server: {ex.Message}", ex);
                System.Diagnostics.Debug.WriteLine($"Failed to start OAuth server: {ex.Message}");
                throw;
            }

            var codes = PKCEUtil.GenerateCodes();
            // GenerateCodes() restituisce (verifier, challenge)
            var verifier = codes.verifier;
            var challenge = codes.challenge;

            var request = new LoginRequest(_server.BaseUri, _settingsProvider.SpotifyClientId,
                LoginRequest.ResponseType.Code)
            {
                CodeChallenge = challenge,
                CodeChallengeMethod = "S256",
                Scope = new List<string>
                {
                    Scopes.UserReadPrivate,
                    Scopes.PlaylistModifyPrivate,
                    Scopes.PlaylistModifyPublic,
                    Scopes.PlaylistReadCollaborative,
                    Scopes.PlaylistReadPrivate,
                    // Required for playlist image upload
                    Scopes.UgcImageUpload,
                    // Required for devices and playback control
                    Scopes.UserReadPlaybackState,
                    Scopes.UserModifyPlaybackState,
                    // Required for followed artists read/modify
                    "user-follow-read",
                    "user-follow-modify",
                    // CRITICAL: Required for Web Playback SDK
                    Scopes.Streaming,
                    // Required for user identification in Web Playback SDK
                    Scopes.UserReadEmail,
                    // Required for top tracks and artists
                    Scopes.UserTopRead,
                    // Required for saved albums access
                    Scopes.UserLibraryRead,
                    Scopes.UserLibraryModify
                }
            };

            // Salviamo il verifier in memoria (usato al rientro)
            _currentToken = _currentToken ?? new TokenInfo();
            _currentToken.RefreshToken = null; // reset eventuale
            _pkceVerifier = verifier;

            BrowserUtil.Open(request.ToUri());
        }

        private async Task OnErrorReceived(object? sender, string error, string? state)
        {
            await _server.Stop().ConfigureAwait(false);
            var tcs = _reauthTcs;
            if (tcs != null)
            {
                tcs.TrySetResult(false);
            }
        }

        private string _pkceVerifier = string.Empty;

        private async Task OnAuthorizationCodeReceived(object sender, AuthorizationCodeResponse response)
        {
            _loggingService.LogInfo($"Received authorization code (state: {response.State})");

            await _server.Stop().ConfigureAwait(false);
            _loggingService.LogInfo("OAuth server stopped");

            var oauth = new OAuthClient();
            var tokenResponse = await oauth.RequestToken(new PKCETokenRequest(
                _settingsProvider.SpotifyClientId,
                response.Code,
                _server.BaseUri,
                _pkceVerifier
            )).ConfigureAwait(false);

            _loggingService.LogInfo($"Token request successful, expires in {tokenResponse.ExpiresIn} seconds");

            Api = new SpotifyClient(tokenResponse.AccessToken);

            try
            {
                var expiresAt = DateTime.UtcNow.AddSeconds(tokenResponse.ExpiresIn).AddMinutes(-5);
                _currentToken = new TokenInfo
                {
                    AccessToken = tokenResponse.AccessToken,
                    RefreshToken = tokenResponse.RefreshToken,
                    ExpiresAtUtc = expiresAt
                };
                _tokenStorage.Save(_currentToken);
                _loggingService.LogInfo($"Token saved to storage (expires: {expiresAt})");
            }
            catch (Exception ex)
            {
                _loggingService.LogError($"Token save failed: {ex.Message}", ex);
            }

            var tcs = _reauthTcs;
            if (tcs != null)
            {
                tcs.TrySetResult(true);
            }

            InvokeOnUiThread(_loginSuccessAction);
            _loggingService.LogInfo("Login process completed successfully");
        }

        public async Task<PrivateUser?> GetPrivateProfileAsync()
        {
            if (_privateProfile != null)
            {
                return _privateProfile;
            }

            await _semaphore.WaitAsync().ConfigureAwait(false);

            try
            {
                if (Api == null)
                {
                    return null;
                }

                if (_privateProfile != null)
                {
                    return _privateProfile;
                }

                _privateProfile = await Api.UserProfile.Current().ConfigureAwait(false);

                return _privateProfile;
            }
            finally
            {
                _semaphore.Release();
            }
        }

        // Returns the current user's display name if available (cached in memory once per session)
        public async Task<string?> GetUserDisplayNameAsync()
        {
            var me = await GetPrivateProfileAsync().ConfigureAwait(false);
            return me?.DisplayName ?? me?.Id;
        }

        // Returns the current user's subscription type ("premium" or "free") if available
        public async Task<string?> GetUserSubscriptionTypeAsync()
        {
            var me = await GetPrivateProfileAsync().ConfigureAwait(false);
            return me?.Product;
        }

        // Returns a local cached file path to the user's profile image. Downloads once and caches under LocalAppData/SpotifyWPF/cache
        public async Task<string?> GetProfileImageCachedPathAsync()
        {
            if (!await EnsureAuthenticatedAsync().ConfigureAwait(false)) return null;
            var me = await GetPrivateProfileAsync().ConfigureAwait(false);
            if (me == null) return null;

            var img = me.Images != null ? me.Images.OrderByDescending(i => (i?.Width ?? 0) * (i?.Height ?? 0)).FirstOrDefault() : null;
            var url = img?.Url;
            if (string.IsNullOrWhiteSpace(url)) return null;

            // Build cache path
            var cacheDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SpotifyWPF", "cache");
            Directory.CreateDirectory(cacheDir);

            // Hash URL to create a stable filename even if URL changes between sessions
            var hash = Sha1(url);
            var ext = ".jpg";
            try
            {
                var uri = new Uri(url);
                var lastSeg = Path.GetFileName(uri.AbsolutePath);
                var guessedExt = Path.GetExtension(lastSeg);
                if (!string.IsNullOrWhiteSpace(guessedExt)) ext = guessedExt;
            }
            catch { /* fallback to .jpg */ }

            var fileName = $"profile_{me.Id}_{hash}{ext}";
            var filePath = Path.Combine(cacheDir, fileName);

            if (!File.Exists(filePath))
            {
                // Download once
                try
                {
                    using var resp = await _http.GetAsync(url).ConfigureAwait(false);
                    resp.EnsureSuccessStatusCode();
                    await using var fs = File.Open(filePath, FileMode.Create, FileAccess.Write, FileShare.Read);
                    await resp.Content.CopyToAsync(fs).ConfigureAwait(false);
                }
                catch
                {
                    // On failure, don't throw; just return null
                    try { if (File.Exists(filePath)) File.Delete(filePath); } catch { }
                    return null;
                }
            }

            return filePath;
        }

        private static string Sha1(string input)
        {
            using var sha = SHA1.Create();
            var bytes = System.Text.Encoding.UTF8.GetBytes(input);
            var hash = sha.ComputeHash(bytes);
            return string.Concat(hash.Select(b => b.ToString("x2")));
        }

        private readonly object _apiLock = new object();

        // Metodo pubblico usato per verificare/ripristinare l'autenticazione basandosi sul token salvato
        public async Task<bool> EnsureAuthenticatedAsync()
        {
            // Cache authentication check for 10 seconds to avoid excessive API calls
            if ((DateTime.Now - _lastAuthCheck) < TimeSpan.FromSeconds(10) && Api != null)
            {
                return true;
            }

            _loggingService.LogDebug("Checking authentication status");
            _lastAuthCheck = DateTime.Now;

            // Usa cache in memoria se disponibile
            var token = _currentToken;
            if (token == null)
            {
                token = _tokenStorage.Load();
                _currentToken = token;
            }

            if (token == null)
            {
                _loggingService.LogWarning("No token found in storage");
                return false;
            }

            // Se il token sta per scadere o è scaduto, prova refresh
            if (token.ExpiresAtUtc <= DateTime.UtcNow.AddMinutes(5))
            {
                var timeUntilExpiry = token.ExpiresAtUtc - DateTime.UtcNow;
                _loggingService.LogInfo($"Token expiring soon: {timeUntilExpiry.TotalMinutes:F1} minutes remaining");
                
                if (!string.IsNullOrWhiteSpace(token.RefreshToken))
                {
                    var ok = await TryRefreshAsync(token.RefreshToken).ConfigureAwait(false);
                    if (!ok) 
                    {
                        _loggingService.LogWarning("Token refresh failed");
                        return false;
                    }
                    token = _currentToken;
                    if (token == null) 
                    {
                        _loggingService.LogWarning("No token after refresh");
                        return false;
                    }
                    _loggingService.LogInfo("Token refreshed successfully");
                }
                else
                {
                    _loggingService.LogWarning("No refresh token available");
                    return false;
                }
            }

            var access = token?.AccessToken;
            if (string.IsNullOrWhiteSpace(access))
            {
                _loggingService.LogWarning("Token has no access token");
                return false;
            }

            lock (_apiLock)
            {
                if (Api == null)
                {
                    Api = new SpotifyClient(access);
                    _loggingService.LogInfo("Created new Spotify API client");
                    try
                    {
                        _tokenProvider.UpdateToken(access);
                    }
                    catch { }
                }
            }

            _loggingService.LogInfo("Authentication verified successfully");
            return true;
        }

        private async Task<bool> TryRefreshAsync(string refreshToken, int attempt = 1)
        {
            _loggingService.LogInfo($"Attempting token refresh (attempt {attempt})");

            try
            {
                var oauth = new OAuthClient();
                var refreshed = await oauth.RequestToken(new PKCETokenRefreshRequest(
                    _settingsProvider.SpotifyClientId, refreshToken)).ConfigureAwait(false);

                _loggingService.LogInfo($"Token refresh successful, expires in {refreshed.ExpiresIn} seconds");

                Api = new SpotifyClient(refreshed.AccessToken);

                var expiresAt = DateTime.UtcNow.AddSeconds(refreshed.ExpiresIn).AddMinutes(-5); // 5-minute buffer instead of 30 seconds
                _currentToken = new TokenInfo
                {
                    AccessToken = refreshed.AccessToken,
                    RefreshToken = string.IsNullOrWhiteSpace(refreshed.RefreshToken) ? refreshToken : refreshed.RefreshToken,
                    ExpiresAtUtc = expiresAt
                };
                _tokenStorage.Save(_currentToken);
                // Notify subscribers that the access token was updated so they can update their SDKs
                try
                {
                    _tokenProvider.UpdateToken(refreshed.AccessToken);
                }
                catch { }

                _loggingService.LogInfo($"Refreshed token saved (expires: {expiresAt})");
                System.Diagnostics.Debug.WriteLine($"🔁 Refresh succeeded. New expiry: {expiresAt:o} (buffer: 5 minutes)");
                return true;
            }
            catch (SpotifyAPI.Web.APIException apiEx)
            {
                System.Net.HttpStatusCode? status = apiEx.Response?.StatusCode;
                var bodyStr = apiEx.Response?.Body as string ?? string.Empty;
                System.Diagnostics.Debug.WriteLine($"⚠️ Token refresh APIException (status={(status.HasValue ? ((int)status.Value).ToString() : "null")}): {apiEx.Message}\nBody: {bodyStr}");

                // Log more details for debugging
                _loggingService.LogError($"Token refresh failed: {apiEx.Message}", apiEx);

                // Retry once on transient server errors
                if (status.HasValue && ((int)status.Value >= 500 && (int)status.Value < 600) && attempt == 1)
                {
                    _loggingService.LogInfo($"Retrying refresh after server error ({status.Value})");
                    await Task.Delay(1000).ConfigureAwait(false); // Increased delay
                    return await TryRefreshAsync(refreshToken, attempt + 1).ConfigureAwait(false);
                }

                // Only clear persisted tokens on definite invalid_grant/unauthorized cases
                if ((status == System.Net.HttpStatusCode.BadRequest || status == System.Net.HttpStatusCode.Unauthorized) ||
                    bodyStr.IndexOf("invalid_grant", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    bodyStr.IndexOf("invalid refresh", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    bodyStr.IndexOf("invalid_client", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    System.Diagnostics.Debug.WriteLine("❌ Refresh token invalid or revoked. Clearing saved credentials.");
                    _loggingService.LogWarning("Refresh token invalid - clearing saved credentials");
                    _tokenStorage.Clear();
                    _currentToken = null;
                    Api = null;
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("⏳ Transient refresh failure. Keeping existing token; will retry later.");
                    _loggingService.LogWarning("Transient refresh failure - keeping existing token");
                }
                return false;
            }
            catch (HttpRequestException hre)
            {
                System.Diagnostics.Debug.WriteLine($"🌐 Network error during token refresh: {hre.Message}");
                _loggingService.LogError($"Network error during refresh: {hre.Message}", hre);
                if (attempt == 1)
                {
                    await Task.Delay(1000).ConfigureAwait(false);
                    return await TryRefreshAsync(refreshToken, attempt + 1).ConfigureAwait(false);
                }
                return false;
            }
            catch (TaskCanceledException tce)
            {
                System.Diagnostics.Debug.WriteLine($"⏱️ Timeout during token refresh: {tce.Message}");
                _loggingService.LogError($"Timeout during refresh: {tce.Message}", tce);
                if (attempt == 1)
                {
                    await Task.Delay(1000).ConfigureAwait(false);
                    return await TryRefreshAsync(refreshToken, attempt + 1).ConfigureAwait(false);
                }
                return false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Unexpected error during token refresh: {ex.Message}");
                _loggingService.LogError($"Unexpected error during refresh: {ex.Message}", ex);
                // Do not clear stored tokens on unknown errors; allow user to retry later
                return false;
            }
        }

        // Implementazione esplicita per garantire la conformità all'interfaccia anche in build temporanee
        async Task<bool> ISpotify.EnsureAuthenticatedAsync()
        {
            return await EnsureAuthenticatedAsync().ConfigureAwait(false);
        }

        public void Logout()
        {
            try
            {
                _tokenStorage.Clear();
                _currentToken = null;
                _privateProfile = null;
                Api = null;
            }
            catch { }
        }

        private static readonly HttpClient _http = new HttpClient();

        // Centralized 429 (Rate Limit) handling for Spotify Web API calls using the SpotifyAPI.Web client
        private static async Task<T> ExecuteApiWith429Async<T>(Func<Task<T>> action, int maxRetries = 0)
        {
            int attempt = 0;
            while (true)
            {
                try
                {
                    return await action().ConfigureAwait(false);
                }
                catch (APIException apiEx)
                {
                    // 429 handling with Retry-After
                    if (apiEx.Response?.StatusCode == System.Net.HttpStatusCode.TooManyRequests && attempt < maxRetries)
                    {
                        attempt++;
                        var retryAfter = GetRetryAfterSeconds(apiEx.Response?.Headers);
                        var delayMs = (int)(retryAfter * 1000) + Random.Shared.Next(100, 300);
                        System.Diagnostics.Debug.WriteLine($"⏳ 429 received. Backing off {delayMs} ms (attempt {attempt}/{maxRetries})");
                        await Task.Delay(delayMs).ConfigureAwait(false);
                        continue;
                    }
                    // If we exhausted retries or it's not a 429, check if it's a rate limit error
                    if (apiEx.Response?.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                    {
                        throw new RateLimitException($"Spotify API rate limit exceeded. Please wait before trying again.", apiEx);
                    }
                    throw; // rethrow others
                }
            }
        }

        private static async Task ExecuteApiWith429Async(Func<Task> action, int maxRetries = 0)
        {
            await ExecuteApiWith429Async<object>(async () => { await action().ConfigureAwait(false); return new object(); }, maxRetries).ConfigureAwait(false);
        }

        private static double GetRetryAfterSeconds(System.Net.Http.Headers.HttpResponseHeaders? headers)
        {
            if (headers == null) return 1.0; // default 1s
            if (headers.TryGetValues("Retry-After", out var vals))
            {
                var v = vals.FirstOrDefault();
                if (int.TryParse(v, out var secInt)) return Math.Max(0.5, secInt);
                if (double.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var secD))
                    return Math.Max(0.5, secD);
            }
            return 1.0;
        }

        private static double GetRetryAfterSeconds(System.Collections.Generic.IReadOnlyDictionary<string, string>? headers)
        {
            if (headers == null) return 1.0;
            foreach (var kv in headers)
            {
                if (string.Equals(kv.Key, "Retry-After", StringComparison.OrdinalIgnoreCase))
                {
                    var v = kv.Value;
                    if (int.TryParse(v, out var secInt)) return Math.Max(0.5, secInt);
                    if (double.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var secD)) return Math.Max(0.5, secD);
                }
            }
            return 1.0;
        }

        // Centralized sender for raw HTTP calls with 429 backoff and auth header
        private async Task<HttpResponseMessage> SendAsyncWithRetry(string url, HttpMethod method, string? jsonBody, bool includeAuthBearer, int maxRetries = 0)
        {
            int attempt = 0;
            while (true)
            {
                using var req = new HttpRequestMessage(method, url);
                if (includeAuthBearer && _currentToken?.AccessToken is string accessToken && !string.IsNullOrWhiteSpace(accessToken))
                {
                    req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                }
                if (jsonBody != null)
                {
                    req.Content = new StringContent(jsonBody);
                    req.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
                }

                var res = await _http.SendAsync(req).ConfigureAwait(false);
                if ((int)res.StatusCode == 429 && attempt < maxRetries)
                {
                    // Dispose and retry after backoff
                    res.Dispose();
                    attempt++;
                    // Try to read Retry-After header from response
                    double retryAfterSec = 1.0;
                    if (res.Headers.TryGetValues("Retry-After", out var vals))
                    {
                        var v = vals.FirstOrDefault();
                        if (int.TryParse(v, out var s)) retryAfterSec = Math.Max(0.5, s);
                        else if (double.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d)) retryAfterSec = Math.Max(0.5, d);
                    }
                    var delayMs = (int)(retryAfterSec * 1000) + Random.Shared.Next(100, 300);
                    System.Diagnostics.Debug.WriteLine($"⏳ 429 (raw). Backing off {delayMs} ms (attempt {attempt}/{maxRetries})");
                    await Task.Delay(delayMs).ConfigureAwait(false);
                    continue;
                }

                return res; // caller will dispose
            }
        }

        public async Task<bool?> CheckIfCurrentUserFollowsPlaylistAsync(string playlistId)
        {
            ValidateSpotifyId(playlistId, nameof(playlistId));

            var ok = await EnsureAuthenticatedAsync().ConfigureAwait(false);
            if (!ok) return null;

            // Precondizioni e acquisizione valori non-null (guardie esplicite -> locali non-null)
            var profile = await GetPrivateProfileAsync().ConfigureAwait(false);
            if (profile?.Id is not string meId || string.IsNullOrWhiteSpace(meId)) return null;

            if (_currentToken?.AccessToken is not string accessToken || string.IsNullOrWhiteSpace(accessToken)) return null;

            try
            {
                // https://api.spotify.com/v1/playlists/{playlist_id}/followers/contains?ids={user_id}
                var url = $"https://api.spotify.com/v1/playlists/{playlistId}/followers/contains?ids={Uri.EscapeDataString(meId)}";
                using (var req = new HttpRequestMessage(HttpMethod.Get, url))
                {
                    req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

                    var resp = await _http.SendAsync(req).ConfigureAwait(false);

                    if (resp.StatusCode == (HttpStatusCode)429)
                    {
                        // Rate limit: lascia che il chiamante gestisca eventuali retry/attese
                        return null;
                    }

                    resp.EnsureSuccessStatusCode();

                    var content = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                    // La risposta è un array JSON di booleani, es. [true] o [false]
                    var arr = JArray.Parse(content);
                    if (arr.Count > 0 && arr[0].Type == JTokenType.Boolean)
                    {
                        return arr[0].Value<bool>();
                    }

                    return null;
                }
            }
            catch
            {
                return null;
            }
        }

        // ====== DTO Methods (decoupling) ======
        public async Task<PagingDto<PlaylistDto>> GetMyPlaylistsPageAsync(int offset, int limit)
        {
            ValidatePaginationParameters(offset, limit, nameof(GetMyPlaylistsPageAsync));

            var result = new PagingDto<PlaylistDto>();
            if (!await EnsureAuthenticatedAsync().ConfigureAwait(false) || Api == null)
            {
                result.Items = new List<PlaylistDto>();
                result.Total = 0;
                result.Limit = limit;
                result.Offset = offset;
                return result;
            }

            var req = new PlaylistCurrentUsersRequest
            {
                Offset = offset,
                Limit = limit
            };

            try
            {
                var page = await Api.Playlists.CurrentUsers(req).ConfigureAwait(false);
                result.Href = page.Href;
                result.Limit = page.Limit ?? limit;
                result.Offset = page.Offset ?? offset;
                result.Total = page.Total ?? 0;
                result.Next = page.Next;
                result.Previous = page.Previous;
                var items = new List<PlaylistDto>();
                if (page.Items != null)
                {
                    foreach (var p in page.Items)
                    {
                        if (p == null || string.IsNullOrEmpty(p.Id)) continue;
                        items.Add(new PlaylistDto
                        {
                            Id = p.Id,
                            Name = p.Name,
                            OwnerName = p.Owner?.DisplayName ?? p.Owner?.Id,
                            OwnerId = p.Owner?.Id,
                            TracksTotal = p.Tracks?.Total ?? 0,
                            IsPublic = p.Public,
                            SnapshotId = p.SnapshotId,
                            Href = p.Href
                        });
                    }
                }
                result.Items = items;
            }
            catch (APIException apiEx)
            {
                if (apiEx.Response?.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                {
                    throw new RateLimitException($"Spotify API rate limit exceeded while getting playlists. Please wait before trying again.", apiEx);
                }
                if (apiEx.Response?.StatusCode == System.Net.HttpStatusCode.Forbidden)
                {
                    throw new ForbiddenException(
                        "Access denied: You may need to log out and log back in to grant permission to view playlists. " +
                        "This happens when the app requires new permissions that weren't requested during your initial login.",
                        "playlist-read-private",
                        apiEx);
                }
                if (apiEx.Response?.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                {
                    // Token might be invalid or expired - clear it to force re-authentication
                    _tokenStorage.Clear();
                    _currentToken = null;
                    Api = null;
                    throw new UnauthorizedAccessException(
                        "Your session has expired. Please log in again.", apiEx);
                }
                throw;
            }

            return result;
        }

        public async Task<PagingDto<PlaylistDto>> SearchPlaylistsPageAsync(string query, int offset, int limit)
        {
            if (string.IsNullOrWhiteSpace(query))
                throw new ArgumentException("Search query cannot be null, empty, or whitespace.", nameof(query));
            ValidatePaginationParameters(offset, limit, nameof(SearchPlaylistsPageAsync));

            var result = new PagingDto<PlaylistDto>();
            if (!await EnsureAuthenticatedAsync().ConfigureAwait(false) || Api == null)
            {
                result.Items = new List<PlaylistDto>();
                result.Total = 0;
                result.Limit = limit;
                result.Offset = offset;
                return result;
            }

            var req = new SearchRequest(SearchRequest.Types.Playlist, query)
            {
                Offset = offset,
                Limit = limit
            };
            var search = await Api.Search.Item(req).ConfigureAwait(false);
            var playlists = search.Playlists;

            result.Href = playlists?.Href;
            result.Limit = playlists?.Limit ?? limit;
            result.Offset = playlists?.Offset ?? offset;
            result.Total = playlists?.Total ?? 0;
            result.Next = playlists?.Next;
            result.Previous = playlists?.Previous;

            var items = new List<PlaylistDto>();
            if (playlists?.Items != null)
            {
                foreach (var p in playlists.Items)
                {
                    if (p == null || string.IsNullOrEmpty(p.Id)) continue;
                    items.Add(new PlaylistDto
                    {
                        Id = p.Id,
                        Name = p.Name,
                        OwnerName = p.Owner?.DisplayName ?? p.Owner?.Id,
                        TracksTotal = p.Tracks?.Total ?? 0,
                        SnapshotId = p.SnapshotId,
                        Href = p.Href
                    });
                }
            }
            result.Items = items;
            return result;
        }

        private IReadOnlyList<Device>? _cachedDevices;
        private DateTime _devicesLastFetched = DateTime.MinValue;
        private readonly object _devicesLock = new object();

        private CurrentlyPlayingContext? _cachedPlayback;
        private DateTime _playbackLastFetched = DateTime.MinValue;
        private readonly object _playbackLock = new object();

        // ====== Devices / Playback ======
        public async Task<IReadOnlyList<Device>> GetDevicesAsync()
        {
            // Cache devices for 30 seconds to avoid excessive API calls
            if ((DateTime.Now - _devicesLastFetched) < TimeSpan.FromSeconds(30) && _cachedDevices != null)
            {
                return _cachedDevices;
            }

            if (!await EnsureAuthenticatedAsync().ConfigureAwait(false) || Api == null)
            {
                return Array.Empty<Device>();
            }

            lock (_devicesLock)
            {
                // Double-check after lock
                if ((DateTime.Now - _devicesLastFetched) < TimeSpan.FromSeconds(30) && _cachedDevices != null)
                {
                    return _cachedDevices;
                }

                var devices = ExecuteApiWith429Async(() => Api.Player.GetAvailableDevices(), maxRetries: 3).ConfigureAwait(false).GetAwaiter().GetResult();
                _cachedDevices = devices?.Devices ?? new List<Device>();
                _devicesLastFetched = DateTime.Now;
                return _cachedDevices;
            }
        }

        public async Task<CurrentlyPlayingContext?> GetCurrentPlaybackAsync()
        {
            // Cache playback for 2 seconds to reduce API calls during rapid polling
            if ((DateTime.Now - _playbackLastFetched) < TimeSpan.FromSeconds(2) && _cachedPlayback != null)
            {
                return _cachedPlayback;
            }

            if (!await EnsureAuthenticatedAsync().ConfigureAwait(false) || Api == null)
            {
                return null;
            }

            lock (_playbackLock)
            {
                // Double-check after lock
                if ((DateTime.Now - _playbackLastFetched) < TimeSpan.FromSeconds(2) && _cachedPlayback != null)
                {
                    return _cachedPlayback;
                }

                var ctx = ExecuteApiWith429Async(() => Api.Player.GetCurrentPlayback(), maxRetries: 3).ConfigureAwait(false).GetAwaiter().GetResult();
                _cachedPlayback = ctx;
                _playbackLastFetched = DateTime.Now;
                return _cachedPlayback;
            }
        }



        public async Task TransferPlaybackAsync(IEnumerable<string> deviceIds, bool play)
        {
            ValidateCollection(deviceIds, nameof(deviceIds));

            if (!await EnsureAuthenticatedAsync().ConfigureAwait(false) || Api == null)
            {
                _loggingService.LogWarning("TransferPlaybackAsync: Not authenticated or API is null");
                return;
            }

            var ids = (deviceIds ?? Array.Empty<string>()).Where(id => !string.IsNullOrWhiteSpace(id)).Distinct().ToList();
            if (ids.Count == 0)
            {
                _loggingService.LogWarning("TransferPlaybackAsync: No valid device IDs provided");
                return;
            }

            _loggingService.LogInfo($"TransferPlaybackAsync: Transferring to devices: [{string.Join(", ", ids)}], play: {play}");

            var req = new PlayerTransferPlaybackRequest(ids) { Play = play };
            try
            {
                await ExecuteApiWith429Async(() => Api.Player.TransferPlayback(req), maxRetries: 3).ConfigureAwait(false);
                _loggingService.LogInfo("TransferPlaybackAsync: API call successful");
            }
            catch (APIException apiEx)
            {
                System.Diagnostics.Debug.WriteLine($"❌ TransferPlaybackAsync API error: {apiEx.Message}");
                System.Diagnostics.Debug.WriteLine($"❌ Response: {apiEx.Response?.Body}");
                // Rilancia: verrà gestito dal chiamante per mostrare un messaggio user-friendly
                throw;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ TransferPlaybackAsync general error: {ex.Message}");
                // Rilancia generica per caller handling
                throw;
            }
        }

        public async Task<bool> PlayTrackOnDeviceAsync(string deviceId, string trackId)
        {
            ValidateSpotifyId(trackId, nameof(trackId));
            if (!string.IsNullOrWhiteSpace(deviceId))
                ValidateDeviceId(deviceId, nameof(deviceId));

            if (!await EnsureAuthenticatedAsync().ConfigureAwait(false)) return false;

            var url = string.IsNullOrWhiteSpace(deviceId)
                ? "https://api.spotify.com/v1/me/player/play"
                : $"https://api.spotify.com/v1/me/player/play?device_id={Uri.EscapeDataString(deviceId)}";

            var payload = new
            {
                uris = new[] { $"spotify:track:{trackId}" }
            };
            var json = Newtonsoft.Json.JsonConvert.SerializeObject(payload);

            try
            {
                _loggingService.LogInfo($"PlayTrackOnDeviceAsync: Sending play for track {trackId} to device: {deviceId}\n");
            }
            catch { }

            using var res = await SendAsyncWithRetry(url, HttpMethod.Put, json, includeAuthBearer: true, maxRetries: 3).ConfigureAwait(false);
            if (res.StatusCode == HttpStatusCode.NoContent) { res.Dispose();
                try { _loggingService.LogInfo($"PlayTrackOnDeviceAsync: HTTP 204 success for track {trackId} on device {deviceId}\n"); } catch { }
                return true; }
            var ok = res.IsSuccessStatusCode;
            try { _loggingService.LogInfo($"PlayTrackOnDeviceAsync: response={res.StatusCode} ok={ok} for track {trackId} on device {deviceId}\n"); } catch { }
            if (!ok)
            {
                try
                {
                    var content = await res.Content.ReadAsStringAsync().ConfigureAwait(false);
                    try { _loggingService.LogInfo($"PlayTrackOnDeviceAsync: response body: {content}\n"); } catch { }
                }
                catch { }
            }
            res.Dispose();
            return ok;
        }

        public async Task<bool> SetVolumePercentOnDeviceAsync(string deviceId, int volumePercent)
        {
            ValidateDeviceId(deviceId, nameof(deviceId));
            ValidateVolumePercent(volumePercent);

            if (!await EnsureAuthenticatedAsync().ConfigureAwait(false)) return false;

            var url = $"https://api.spotify.com/v1/me/player/volume?volume_percent={volumePercent}&device_id={Uri.EscapeDataString(deviceId)}";
            using var res = await SendAsyncWithRetry(url, HttpMethod.Put, jsonBody: null, includeAuthBearer: true).ConfigureAwait(false);
            var ok = res.IsSuccessStatusCode || res.StatusCode == HttpStatusCode.NoContent;
            try
            {
                var body = await res.Content.ReadAsStringAsync().ConfigureAwait(false);
                LoggingService.LogToFile($"[RAW_HTTP] [SET_VOLUME] status={(int)res.StatusCode} ({res.StatusCode}) device={deviceId} volume={volumePercent} body={body}\n");
                _loggingService.LogDebug($"[RAW_HTTP] [SET_VOLUME] status={(int)res.StatusCode} device={deviceId} volume={volumePercent}");
            }
            catch { }
            res.Dispose();
            return ok;
        }

        public async Task<bool> PauseCurrentPlaybackAsync(string? deviceId = null)
        {
            if (deviceId != null)
                ValidateDeviceId(deviceId, nameof(deviceId));

            if (!await EnsureAuthenticatedAsync().ConfigureAwait(false)) return false;

            var url = "https://api.spotify.com/v1/me/player/pause";
            if (!string.IsNullOrWhiteSpace(deviceId)) url += $"?device_id={Uri.EscapeDataString(deviceId)}";
            using var res = await SendAsyncWithRetry(url, HttpMethod.Put, jsonBody: null, includeAuthBearer: true).ConfigureAwait(false);
            var ok = res.IsSuccessStatusCode || res.StatusCode == HttpStatusCode.NoContent;
            res.Dispose();
            return ok;
        }

        public async Task<bool> ResumeCurrentPlaybackAsync(string? deviceId = null)
        {
            if (deviceId != null)
                ValidateDeviceId(deviceId, nameof(deviceId));

            if (!await EnsureAuthenticatedAsync().ConfigureAwait(false)) return false;

            var url = "https://api.spotify.com/v1/me/player/play";
            if (!string.IsNullOrWhiteSpace(deviceId)) url += $"?device_id={Uri.EscapeDataString(deviceId)}";
            using var res = await SendAsyncWithRetry(url, HttpMethod.Put, jsonBody: "{}", includeAuthBearer: true).ConfigureAwait(false);
            var ok = res.IsSuccessStatusCode || res.StatusCode == HttpStatusCode.NoContent;
            res.Dispose();
            return ok;
        }

        public async Task<bool> SeekCurrentPlaybackAsync(int positionMs, string? deviceId = null)
        {
            ValidatePositionMs(positionMs);
            if (!string.IsNullOrWhiteSpace(deviceId))
                ValidateDeviceId(deviceId, nameof(deviceId));

            if (!await EnsureAuthenticatedAsync().ConfigureAwait(false)) return false;

            var url = $"https://api.spotify.com/v1/me/player/seek?position_ms={positionMs}";
            if (!string.IsNullOrWhiteSpace(deviceId)) url += $"&device_id={Uri.EscapeDataString(deviceId)}";
            using var res = await SendAsyncWithRetry(url, HttpMethod.Put, jsonBody: null, includeAuthBearer: true).ConfigureAwait(false);
            var ok = res.IsSuccessStatusCode || res.StatusCode == HttpStatusCode.NoContent;
            try
            {
                var body = await res.Content.ReadAsStringAsync().ConfigureAwait(false);
                LoggingService.LogToFile($"[RAW_HTTP] [SEEK] status={(int)res.StatusCode} ({res.StatusCode}) device={(deviceId ?? "current")} pos={positionMs} body={body}\n");
                _loggingService.LogDebug($"[RAW_HTTP] [SEEK] status={(int)res.StatusCode} device={(deviceId ?? "current")} pos={positionMs}");
            }
            catch { }
            res.Dispose();
            return ok;
        }



        public async Task<bool> SetShuffleAsync(bool state, string? deviceId = null)
        {
            if (deviceId != null)
                ValidateDeviceId(deviceId, nameof(deviceId));

            if (!await EnsureAuthenticatedAsync().ConfigureAwait(false)) return false;

            var url = $"https://api.spotify.com/v1/me/player/shuffle?state={(state ? "true" : "false")}";
            if (!string.IsNullOrWhiteSpace(deviceId)) url += $"&device_id={Uri.EscapeDataString(deviceId)}";
            using var res = await SendAsyncWithRetry(url, HttpMethod.Put, jsonBody: null, includeAuthBearer: true).ConfigureAwait(false);
            var ok = res.IsSuccessStatusCode || res.StatusCode == HttpStatusCode.NoContent;
            res.Dispose();
            return ok;
        }

        public async Task<bool> SetRepeatAsync(string state, string? deviceId = null)
        {
            ValidateRepeatState(state);
            if (!string.IsNullOrWhiteSpace(deviceId))
                ValidateDeviceId(deviceId, nameof(deviceId));

            if (!await EnsureAuthenticatedAsync().ConfigureAwait(false)) return false;

            var url = $"https://api.spotify.com/v1/me/player/repeat?state={Uri.EscapeDataString(state)}";
            if (!string.IsNullOrWhiteSpace(deviceId)) url += $"&device_id={Uri.EscapeDataString(deviceId)}";
            using var res = await SendAsyncWithRetry(url, HttpMethod.Put, jsonBody: null, includeAuthBearer: true).ConfigureAwait(false);
            var ok = res.IsSuccessStatusCode || res.StatusCode == HttpStatusCode.NoContent;
            res.Dispose();
            return ok;
        }

        public async Task<bool> SkipToNextAsync(string? deviceId = null)
        {
            if (deviceId != null)
                ValidateDeviceId(deviceId, nameof(deviceId));

            if (!await EnsureAuthenticatedAsync().ConfigureAwait(false)) return false;

            var url = "https://api.spotify.com/v1/me/player/next";
            if (!string.IsNullOrWhiteSpace(deviceId)) url += $"?device_id={Uri.EscapeDataString(deviceId)}";
            using var res = await SendAsyncWithRetry(url, HttpMethod.Post, jsonBody: null, includeAuthBearer: true).ConfigureAwait(false);
            var ok = res.IsSuccessStatusCode || res.StatusCode == HttpStatusCode.NoContent;
            res.Dispose();
            return ok;
        }

        public async Task<bool> SkipToPrevAsync(string? deviceId = null)
        {
            if (deviceId != null)
                ValidateDeviceId(deviceId, nameof(deviceId));

            if (!await EnsureAuthenticatedAsync().ConfigureAwait(false)) return false;

            var url = "https://api.spotify.com/v1/me/player/previous";
            if (!string.IsNullOrWhiteSpace(deviceId)) url += $"?device_id={Uri.EscapeDataString(deviceId)}";
            using var res = await SendAsyncWithRetry(url, HttpMethod.Post, jsonBody: null, includeAuthBearer: true).ConfigureAwait(false);
            var ok = res.IsSuccessStatusCode || res.StatusCode == HttpStatusCode.NoContent;
            res.Dispose();
            return ok;
        }

        public async Task<PlaylistDto> CreatePlaylistAsync(string name, string description, bool isPublic, bool isCollaborative)
        {
            ValidateCollection(new[] { name }, nameof(name));

            if (!await EnsureAuthenticatedAsync().ConfigureAwait(false) || Api == null)
            {
                throw new InvalidOperationException("Not authenticated");
            }

            var me = await GetPrivateProfileAsync().ConfigureAwait(false);
            if (me?.Id is not string userId || string.IsNullOrWhiteSpace(userId))
            {
                throw new InvalidOperationException("Unable to get user ID");
            }

            var request = new PlaylistCreateRequest(name)
            {
                Description = description,
                Public = isPublic,
                Collaborative = isCollaborative
            };

            var playlist = await Api.Playlists.Create(userId, request).ConfigureAwait(false);

            return new PlaylistDto
            {
                Id = playlist.Id ?? string.Empty,
                Name = playlist.Name,
                OwnerName = playlist.Owner?.DisplayName ?? playlist.Owner?.Id,
                OwnerId = playlist.Owner?.Id,
                TracksTotal = playlist.Tracks?.Total ?? 0,
                SnapshotId = playlist.SnapshotId,
                Href = playlist.Href
            };
        }

        public async Task UpdatePlaylistAsync(string playlistId, string name, bool isPublic)
        {
            ValidateSpotifyId(playlistId, nameof(playlistId));
            ValidateCollection(new[] { name }, nameof(name));

            if (!await EnsureAuthenticatedAsync().ConfigureAwait(false) || Api == null)
            {
                throw new InvalidOperationException("Not authenticated");
            }

            var request = new PlaylistChangeDetailsRequest
            {
                Name = name,
                Public = isPublic
            };

            await Api.Playlists.ChangeDetails(playlistId, request).ConfigureAwait(false);
        }

        public async Task<SpotifyWPF.Model.Dto.FollowedArtistsPage> GetFollowedArtistsPageAsync(string? after, int limit)
        {
            ValidatePaginationParameters(0, limit, nameof(GetFollowedArtistsPageAsync));

            var result = new SpotifyWPF.Model.Dto.FollowedArtistsPage();
            if (!await EnsureAuthenticatedAsync().ConfigureAwait(false)) return result;
            if (_currentToken?.AccessToken is not string accessToken || string.IsNullOrWhiteSpace(accessToken)) return result;

            var url = $"https://api.spotify.com/v1/me/following?type=artist&limit={limit}";
            if (!string.IsNullOrWhiteSpace(after)) url += $"&after={Uri.EscapeDataString(after)}";

            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
            using var res = await _http.SendAsync(req).ConfigureAwait(false);

            if (res.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
            {
                throw new RateLimitException($"Spotify API rate limit exceeded while getting followed artists. Please wait before trying again.");
            }

            if (res.StatusCode == System.Net.HttpStatusCode.Forbidden)
            {
                throw new ForbiddenException(
                    "Access denied: You may need to log out and log back in to grant permission to view followed artists. " +
                    "This happens when the app requires new permissions that weren't requested during your initial login.",
                    "user-follow-read");
            }

            if (res.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                // Token might be invalid or expired - clear it to force re-authentication
                _tokenStorage.Clear();
                _currentToken = null;
                Api = null;
                throw new UnauthorizedAccessException(
                    "Your session has expired. Please log in again.");
            }

            res.EnsureSuccessStatusCode();
            var json = await res.Content.ReadAsStringAsync().ConfigureAwait(false);

            var jo = Newtonsoft.Json.Linq.JObject.Parse(json);
            var artistsObj = jo["artists"] as Newtonsoft.Json.Linq.JObject;
            var page = new SpotifyWPF.Model.Dto.PagingDto<SpotifyWPF.Model.Dto.ArtistDto>();
            if (artistsObj != null)
            {
                page.Href = artistsObj.Value<string>("href");
                page.Limit = artistsObj.Value<int?>("limit") ?? limit;
                page.Offset = 0;
                page.Total = artistsObj.Value<int?>("total") ?? 0;
                page.Next = artistsObj.Value<string>("next");
                page.Previous = null;

                var items = artistsObj["items"] as Newtonsoft.Json.Linq.JArray;
                if (items != null)
                {
                    foreach (var item in items)
                    {
                        var id = item.Value<string>("id");
                        if (string.IsNullOrWhiteSpace(id)) continue;
                        var name = item.Value<string>("name");
                        var followers = item["followers"]?.Value<int?>("total") ?? 0;
                        var popularity = item.Value<int?>("popularity") ?? 0;
                        var href = item.Value<string>("href");
                        page.Items.Add(new SpotifyWPF.Model.Dto.ArtistDto
                        {
                            Id = id,
                            Name = name,
                            FollowersTotal = followers,
                            Popularity = popularity,
                            Href = href
                        });
                    }
                }

                var cursors = artistsObj["cursors"] as Newtonsoft.Json.Linq.JObject;
                result.NextAfter = cursors?.Value<string>("after");
            }

            result.Page = page;
            return result;
        }

        public async Task UnfollowArtistsAsync(IEnumerable<string> artistIds)
        {
            ValidateCollection(artistIds, nameof(artistIds));

            if (!await EnsureAuthenticatedAsync().ConfigureAwait(false) || Api == null)
            {
                return;
            }
            var ids = artistIds?.Where(id => !string.IsNullOrWhiteSpace(id)).ToList() ?? new List<string>();
            if (ids.Count == 0) return;

            // Spotify API allows up to 50 ids per request
            const int batchSize = 50;
            for (var i = 0; i < ids.Count; i += batchSize)
            {
                var chunk = ids.Skip(i).Take(batchSize).ToList();
                var request = new UnfollowRequest(UnfollowRequest.Type.Artist, chunk);
                await Api.Follow.Unfollow(request).ConfigureAwait(false);
            }
        }

        public async Task<PagingDto<AlbumDto>> GetMySavedAlbumsPageAsync(int offset, int limit)
        {
            ValidatePaginationParameters(offset, limit, nameof(GetMySavedAlbumsPageAsync));

            if (!await EnsureAuthenticatedAsync().ConfigureAwait(false) || Api == null)
            {
                return new PagingDto<AlbumDto> { Items = new List<AlbumDto>() };
            }

            try
            {
                var request = new LibraryAlbumsRequest
                {
                    Limit = limit,
                    Offset = offset
                };

                var response = await Api.Library.GetAlbums(request).ConfigureAwait(false);

                var result = new PagingDto<AlbumDto>
                {
                    Href = response.Href,
                    Limit = response.Limit ?? limit,
                    Offset = response.Offset ?? offset,
                    Total = response.Total ?? 0,
                    Next = response.Next,
                    Previous = response.Previous,
                    Items = new List<AlbumDto>()
                };

                if (response.Items != null)
                {
                    foreach (var item in response.Items)
                    {
                        if (item.Album != null)
                        {
                            // Get the best quality album image
                            var imageUrl = item.Album.Images?.OrderByDescending(img => (img?.Width ?? 0) * (img?.Height ?? 0)).FirstOrDefault()?.Url ?? string.Empty;

                            result.Items.Add(new AlbumDto
                            {
                                Id = item.Album.Id,
                                Name = item.Album.Name,
                                Artists = string.Join(", ", item.Album.Artists?.Select(a => a.Name) ?? Array.Empty<string>()),
                                ReleaseDate = item.Album.ReleaseDate,
                                TotalTracks = item.Album.TotalTracks,
                                Href = item.Album.Href,
                                ImageUrl = imageUrl,
                                AddedAt = item.AddedAt
                            });
                        }
                    }
                }

                return result;
            }
            catch (Exception ex)
            {
                _loggingService.LogError($"Error getting saved albums: {ex.Message}");
                return new PagingDto<AlbumDto> { Items = new List<AlbumDto>() };
            }
        }

        public async Task RemoveSavedAlbumsAsync(IEnumerable<string> albumIds)
        {
            ValidateCollection(albumIds, nameof(albumIds));

            if (!await EnsureAuthenticatedAsync().ConfigureAwait(false) || Api == null)
            {
                return;
            }

            var ids = albumIds?.Where(id => !string.IsNullOrWhiteSpace(id)).ToList() ?? new List<string>();
            if (ids.Count == 0) return;

            // Spotify API allows up to 50 ids per request for albums
            const int batchSize = 50;
            for (var i = 0; i < ids.Count; i += batchSize)
            {
                var chunk = ids.Skip(i).Take(batchSize).ToList();
                var request = new LibraryRemoveAlbumsRequest(chunk);
                await Api.Library.RemoveAlbums(request).ConfigureAwait(false);
            }
        }

        public async Task<PagingDto<AlbumDto>> SearchAlbumsPageAsync(string query, int offset, int limit)
        {
            if (string.IsNullOrWhiteSpace(query))
                throw new ArgumentException("Search query cannot be null, empty, or whitespace.", nameof(query));
            ValidatePaginationParameters(offset, limit, nameof(SearchAlbumsPageAsync));

            var result = new PagingDto<AlbumDto>();
            if (!await EnsureAuthenticatedAsync().ConfigureAwait(false) || Api == null)
            {
                result.Items = new List<AlbumDto>();
                result.Total = 0;
                result.Limit = limit;
                result.Offset = offset;
                return result;
            }

            var req = new SearchRequest(SearchRequest.Types.Album, query)
            {
                Offset = offset,
                Limit = limit
            };
            var search = await Api.Search.Item(req).ConfigureAwait(false);
            var albums = search.Albums;

            result.Href = albums?.Href;
            result.Limit = albums?.Limit ?? limit;
            result.Offset = albums?.Offset ?? offset;
            result.Total = albums?.Total ?? 0;
            result.Next = albums?.Next;
            result.Previous = albums?.Previous;

            var items = new List<AlbumDto>();
            if (albums?.Items != null)
            {
                foreach (var a in albums.Items)
                {
                    if (a == null || string.IsNullOrEmpty(a.Id)) continue;
                    var artistNames = a.Artists != null ? string.Join(", ", a.Artists.Select(ar => ar?.Name).Where(n => !string.IsNullOrWhiteSpace(n))) : null;
                    items.Add(new AlbumDto
                    {
                        Id = a.Id,
                        Name = a.Name,
                        Artists = artistNames,
                        ReleaseDate = a.ReleaseDate,
                        TotalTracks = a.TotalTracks,
                        Href = a.Href
                    });
                }
            }
            result.Items = items;
            return result;
        }

        public async Task<PagingDto<ArtistDto>> SearchArtistsPageAsync(string query, int offset, int limit)
        {
            if (string.IsNullOrWhiteSpace(query))
                throw new ArgumentException("Search query cannot be null, empty, or whitespace.", nameof(query));
            ValidatePaginationParameters(offset, limit, nameof(SearchArtistsPageAsync));

            var result = new PagingDto<ArtistDto>();
            if (!await EnsureAuthenticatedAsync().ConfigureAwait(false) || Api == null)
            {
                result.Items = new List<ArtistDto>();
                result.Total = 0;
                result.Limit = limit;
                result.Offset = offset;
                return result;
            }

            var req = new SearchRequest(SearchRequest.Types.Artist, query)
            {
                Offset = offset,
                Limit = limit
            };
            var search = await Api.Search.Item(req).ConfigureAwait(false);
            var artists = search.Artists;

            result.Href = artists?.Href;
            result.Limit = artists?.Limit ?? limit;
            result.Offset = artists?.Offset ?? offset;
            result.Total = artists?.Total ?? 0;
            result.Next = artists?.Next;
            result.Previous = artists?.Previous;

            var items = new List<ArtistDto>();
            if (artists?.Items != null)
            {
                foreach (var ar in artists.Items)
                {
                    if (ar == null || string.IsNullOrEmpty(ar.Id)) continue;
                    items.Add(new ArtistDto
                    {
                        Id = ar.Id,
                        Name = ar.Name,
                        FollowersTotal = ar.Followers?.Total ?? 0,
                        Popularity = ar.Popularity,
                        Href = ar.Href
                    });
                }
            }
            result.Items = items;
            return result;
        }

        public async Task<PagingDto<TrackDto>> SearchTracksPageAsync(string query, int offset, int limit)
        {
            if (string.IsNullOrWhiteSpace(query))
                throw new ArgumentException("Search query cannot be null, empty, or whitespace.", nameof(query));
            ValidatePaginationParameters(offset, limit, nameof(SearchTracksPageAsync));

            var result = new PagingDto<TrackDto>();
            if (!await EnsureAuthenticatedAsync().ConfigureAwait(false) || Api == null)
            {
                result.Items = new List<TrackDto>();
                result.Total = 0;
                result.Limit = limit;
                result.Offset = offset;
                return result;
            }

            var req = new SearchRequest(SearchRequest.Types.Track, query)
            {
                Offset = offset,
                Limit = limit
            };
            var search = await Api.Search.Item(req).ConfigureAwait(false);
            var tracks = search.Tracks;

            result.Href = tracks?.Href;
            result.Limit = tracks?.Limit ?? limit;
            result.Offset = tracks?.Offset ?? offset;
            result.Total = tracks?.Total ?? 0;
            result.Next = tracks?.Next;
            result.Previous = tracks?.Previous;

            var items = new List<TrackDto>();
            if (tracks?.Items != null)
            {
                foreach (var t in tracks.Items)
                {
                    if (t == null || string.IsNullOrEmpty(t.Id)) continue;
                    var artistNames = t.Artists != null ? string.Join(", ", t.Artists.Select(ar => ar?.Name).Where(n => !string.IsNullOrWhiteSpace(n))) : null;
                    items.Add(new TrackDto
                    {
                        Id = t.Id,
                        Name = t.Name,
                        Artists = artistNames,
                        AlbumName = t.Album?.Name,
                        DurationMs = t.DurationMs,
                        Href = t.Href,
                        Uri = t.Uri,
                        AlbumImageUrl = t.Album?.Images?.FirstOrDefault()?.Url
                    });
                }
            }
            result.Items = items;
            return result;
        }

        public async Task DeletePlaylistAsync(string playlistId)
        {
            System.Diagnostics.Debug.WriteLine($"[DeletePlaylistAsync] Attempting to delete playlist {playlistId}");
            await EnsureAuthenticatedAsync();

            // Spotify doesn't support direct playlist deletion via API
            // Instead, we need to remove all tracks and then unfollow the playlist
            // This effectively "deletes" the playlist from the user's perspective

            try
            {
                // First, get all tracks in the playlist (Spotify API limit is 50 per request)
                var tracksPage = await GetPlaylistTracksPageAsync(playlistId, 0, 50);
                if (tracksPage.Items != null && tracksPage.Items.Any())
                {
                    System.Diagnostics.Debug.WriteLine($"[DeletePlaylistAsync] Removing {tracksPage.Items.Count} tracks from playlist {playlistId}");

                    // Remove all tracks
                    var trackUris = tracksPage.Items.Select(t => t.Uri).Where(uri => !string.IsNullOrEmpty(uri)).ToList();
                    if (trackUris.Any())
                    {
                        await RemoveTracksFromPlaylistAsync(playlistId, trackUris);
                        System.Diagnostics.Debug.WriteLine($"[DeletePlaylistAsync] Successfully removed all tracks from playlist {playlistId}");
                    }

                    // If there are more tracks (pagination), continue removing
                    var totalTracks = tracksPage.Total;
                    for (int offset = 50; offset < totalTracks; offset += 50)
                    {
                        tracksPage = await GetPlaylistTracksPageAsync(playlistId, offset, 50);
                        if (tracksPage.Items != null && tracksPage.Items.Any())
                        {
                            trackUris = tracksPage.Items.Select(t => t.Uri).Where(uri => !string.IsNullOrEmpty(uri)).ToList();
                            if (trackUris.Any())
                            {
                                await RemoveTracksFromPlaylistAsync(playlistId, trackUris);
                                System.Diagnostics.Debug.WriteLine($"[DeletePlaylistAsync] Removed additional {tracksPage.Items.Count} tracks from playlist {playlistId}");
                            }
                        }
                    }
                }

                // Finally, unfollow the playlist (this removes it from the user's library)
                System.Diagnostics.Debug.WriteLine($"[DeletePlaylistAsync] Unfollowing playlist {playlistId}");
                await UnfollowPlaylistAsync(playlistId);
                System.Diagnostics.Debug.WriteLine($"[DeletePlaylistAsync] Successfully unfollowed (deleted) playlist {playlistId}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DeletePlaylistAsync] Error during playlist deletion process: {ex.Message}");
                throw;
            }
        }

        public async Task UnfollowPlaylistAsync(string playlistId)
        {
            await EnsureAuthenticatedAsync();

            var url = $"https://api.spotify.com/v1/playlists/{playlistId}/followers";
            await SendDeleteWithRetryAsync(url, $"unfollow playlist {playlistId}");
        }

        public async Task RemoveTracksFromPlaylistAsync(string playlistId, IEnumerable<string> trackUris)
        {
            ValidateSpotifyId(playlistId, nameof(playlistId));
            ValidateTrackCollection(trackUris, nameof(trackUris));

            var trackList = trackUris.ToList();
            if (trackList.Count > 100)
                throw new ArgumentException("Cannot remove more than 100 tracks at once.", nameof(trackUris));

            if (!await EnsureAuthenticatedAsync().ConfigureAwait(false) || Api == null)
                return;

            try
            {
                var request = new PlaylistRemoveItemsRequest
                {
                    Tracks = trackList.Select(uri => new PlaylistRemoveItemsRequest.Item { Uri = uri }).ToList()
                };

                await Api.Playlists.RemoveItems(playlistId, request).ConfigureAwait(false);
                _loggingService.LogInfo($"Successfully removed {trackList.Count} tracks from playlist {playlistId}");
            }
            catch (APIException apiEx)
            {
                _loggingService.LogError($"API error removing tracks from playlist: {apiEx.Message}");
                throw;
            }
            catch (Exception ex)
            {
                _loggingService.LogError($"Error removing tracks from playlist: {ex.Message}");
                throw;
            }
        }

        public async Task ReorderPlaylistTracksAsync(string playlistId, int rangeStart, int insertBefore, int rangeLength = 1, string? snapshotId = null)
        {
            ValidateSpotifyId(playlistId, nameof(playlistId));
            if (rangeStart < 0)
                throw new ArgumentException("Range start must be non-negative.", nameof(rangeStart));
            if (insertBefore < 0)
                throw new ArgumentException("Insert before must be non-negative.", nameof(insertBefore));
            if (rangeLength < 1)
                throw new ArgumentException("Range length must be at least 1.", nameof(rangeLength));

            if (!await EnsureAuthenticatedAsync().ConfigureAwait(false))
                return;

            try
            {
                var token = await GetCurrentAccessTokenAsync();
                if (string.IsNullOrEmpty(token))
                    throw new InvalidOperationException("No access token available");

                var url = $"https://api.spotify.com/v1/playlists/{playlistId}/tracks";

                using var httpClient = new HttpClient();
                httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
                httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                var requestBody = new
                {
                    range_start = rangeStart,
                    insert_before = insertBefore,
                    range_length = rangeLength,
                    snapshot_id = snapshotId
                };

                var jsonContent = System.Text.Json.JsonSerializer.Serialize(requestBody);
                var content = new StringContent(jsonContent, Encoding.UTF8, new MediaTypeHeaderValue("application/json"));

                var response = await httpClient.PutAsync(url, content).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();

                _loggingService.LogInfo($"Successfully reordered tracks in playlist {playlistId}: range_start={rangeStart}, insert_before={insertBefore}, range_length={rangeLength}");
            }
            catch (HttpRequestException ex)
            {
                _loggingService.LogError($"HTTP error reordering tracks in playlist: {ex.Message}");
                throw;
            }
            catch (Exception ex)
            {
                _loggingService.LogError($"Error reordering tracks in playlist: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Get current access token for Web Playback SDK
        /// </summary>
        public async Task<string?> GetCurrentAccessTokenAsync()
        {
            if (!await EnsureAuthenticatedAsync().ConfigureAwait(false))
            {
                return null;
            }
            return _currentToken?.AccessToken;
        }

        private async Task SendDeleteWithRetryAsync(string requestUrl, string operationDescription, CancellationToken cancellationToken = default)
        {
            if (_currentToken?.AccessToken == null)
            {
                throw new InvalidOperationException("Authorization token is not available.");
            }

            const int maxAttempts = 5;
            var attempt = 0;

            while (true)
            {
                attempt++;

                using var request = new HttpRequestMessage(HttpMethod.Delete, requestUrl);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _currentToken.AccessToken);

                using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);

                if (response.IsSuccessStatusCode)
                {
                    return;
                }

                var responseBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

                if (response.StatusCode == (HttpStatusCode)429 && attempt <= maxAttempts)
                {
                    // Do not retry on 429 - throw immediately to avoid flooding
                    var rateLimitMessage = $"Rate limit exceeded during {operationDescription}. Status {(int)response.StatusCode} {response.ReasonPhrase}. Body: {responseBody}";
                    _loggingService.LogError(rateLimitMessage);
                    throw new HttpRequestException(rateLimitMessage);
                }

                if (response.StatusCode == HttpStatusCode.Unauthorized && attempt <= maxAttempts)
                {
                    _loggingService.LogWarning($"Spotify unauthorized during {operationDescription}. Refreshing token (attempt {attempt}/{maxAttempts}).");
                    if (await EnsureAuthenticatedAsync().ConfigureAwait(false))
                    {
                        await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)), cancellationToken).ConfigureAwait(false);
                        continue;
                    }
                }

                var message = $"Failed to {operationDescription}. Status {(int)response.StatusCode} {response.ReasonPhrase}. Body: {responseBody}";
                _loggingService.LogError(message);
                throw new HttpRequestException(message);
            }
        }

        public async Task<PagingDto<SearchResultDto>> SearchItemsPageAsync(string query, List<string> types, int offset, int limit)
        {
            if (string.IsNullOrWhiteSpace(query))
                throw new ArgumentException("Search query cannot be null, empty, or whitespace.", nameof(query));
            ValidatePaginationParameters(offset, limit, nameof(SearchItemsPageAsync));

            var result = new PagingDto<SearchResultDto>();
            if (!await EnsureAuthenticatedAsync().ConfigureAwait(false) || Api == null)
            {
                result.Items = new List<SearchResultDto>();
                result.Total = 0;
                result.Limit = limit;
                result.Offset = offset;
                return result;
            }

            // For single type searches, use the specific search API for proper pagination
            if (types.Count == 1)
            {
                var type = types[0];
                switch (type)
                {
                    case "track":
                        var trackResult = await SearchTracksPageAsync(query, offset, limit);
                        result.Items = trackResult.Items?.Select(t => new SearchResultDto
                        {
                            Id = t.Id,
                            Name = t.Name,
                            Artists = t.Artists,
                            AlbumName = t.AlbumName,
                            DurationMs = t.DurationMs,
                            Href = t.Href,
                            Uri = t.Uri,
                            AlbumImageUrl = t.AlbumImageUrl,
                            Type = "track",
                            Description = $"{t.Artists} - {t.AlbumName}",
                            ImageUrl = t.AlbumImageUrl,
                            CanAddToPlaylist = true,
                            IsInSelectedPlaylist = false,
                            OriginalDto = t
                        }).ToList() ?? new List<SearchResultDto>();
                        result.Total = trackResult.Total;
                        result.Limit = trackResult.Limit;
                        result.Offset = trackResult.Offset;
                        result.Next = trackResult.Next;
                        result.Previous = trackResult.Previous;
                        return result;

                    case "artist":
                        var artistResult = await SearchArtistsPageAsync(query, offset, limit);
                        result.Items = (artistResult.Items ?? new List<ArtistDto>()).Where(ar => ar != null).Select(ar => new SearchResultDto
                        {
                            Id = ar.Id,
                            Name = ar.Name,
                            Artists = ar.Name,
                            AlbumName = null,
                            DurationMs = 0,
                            Href = ar.Href,
                            Uri = $"spotify:artist:{ar.Id}", // Construct URI from ID
                            AlbumImageUrl = null, // Artists don't have album images
                            Type = "artist",
                            Description = $"{ar.FollowersTotal} followers",
                            ImageUrl = null, // Artists don't have direct images in DTO
                            CanAddToPlaylist = false,
                            IsInSelectedPlaylist = false,
                            OriginalDto = ar
                        }).ToList();
                        result.Total = artistResult.Total;
                        result.Limit = artistResult.Limit;
                        result.Offset = artistResult.Offset;
                        result.Next = artistResult.Next;
                        result.Previous = artistResult.Previous;
                        return result;

                    case "album":
                        var albumResult = await SearchAlbumsPageAsync(query, offset, limit);
                        result.Items = albumResult.Items?.Select(a => new SearchResultDto
                        {
                            Id = a.Id,
                            Name = a.Name,
                            Artists = a.Artists,
                            AlbumName = a.Name,
                            DurationMs = 0,
                            Href = a.Href,
                            Uri = $"spotify:album:{a.Id}", // Construct URI from ID
                            AlbumImageUrl = a.ImageUrl,
                            Type = "album",
                            Description = $"{a.Artists}",
                            ImageUrl = a.ImageUrl,
                            CanAddToPlaylist = false,
                            IsInSelectedPlaylist = false,
                            OriginalDto = a
                        }).ToList() ?? new List<SearchResultDto>();
                        result.Total = albumResult.Total;
                        result.Limit = albumResult.Limit;
                        result.Offset = albumResult.Offset;
                        result.Next = albumResult.Next;
                        result.Previous = albumResult.Previous;
                        return result;

                    case "playlist":
                        var playlistResult = await SearchPlaylistsPageAsync(query, offset, limit);
                        result.Items = playlistResult.Items?.Select(p => new SearchResultDto
                        {
                            Id = p.Id,
                            Name = p.Name,
                            Artists = p.OwnerName,
                            AlbumName = null,
                            DurationMs = 0,
                            Href = p.Href,
                            Uri = $"spotify:playlist:{p.Id}", // Construct URI from ID
                            AlbumImageUrl = null, // Playlists don't have album images
                            Type = "playlist",
                            Description = $"{p.TracksTotal} tracks",
                            ImageUrl = null, // Playlists don't have direct images in DTO
                            CanAddToPlaylist = false,
                            IsInSelectedPlaylist = false,
                            OriginalDto = p
                        }).ToList() ?? new List<SearchResultDto>();
                        result.Total = playlistResult.Total;
                        result.Limit = playlistResult.Limit;
                        result.Offset = playlistResult.Offset;
                        result.Next = playlistResult.Next;
                        result.Previous = playlistResult.Previous;
                        return result;
                }
            }

            // For multi-type searches, use the All search and filter results
            var req = new SearchRequest(SearchRequest.Types.All, query)
            {
                Offset = offset,
                Limit = limit
            };
            var search = await Api.Search.Item(req).ConfigureAwait(false);

            // Combine results from different types, but only include requested types
            var items = new List<SearchResultDto>();

            // Tracks
            if (types.Contains("track") && search.Tracks?.Items != null)
            {
                foreach (var t in search.Tracks.Items)
                {
                    if (t == null || string.IsNullOrEmpty(t.Id)) continue;
                    var artistNames = t.Artists != null ? string.Join(", ", t.Artists.Select(ar => ar?.Name).Where(n => !string.IsNullOrWhiteSpace(n))) : null;
                    items.Add(new SearchResultDto
                    {
                        Id = t.Id,
                        Name = t.Name,
                        Artists = artistNames,
                        AlbumName = t.Album?.Name,
                        DurationMs = t.DurationMs,
                        Href = t.Href,
                        Uri = t.Uri,
                        AlbumImageUrl = t.Album?.Images?.FirstOrDefault()?.Url,
                        Type = "track",
                        Description = $"{artistNames} - {t.Album?.Name}",
                        ImageUrl = t.Album?.Images?.FirstOrDefault()?.Url,
                        CanAddToPlaylist = true,
                        IsInSelectedPlaylist = false,
                        OriginalDto = t
                    });
                }
            }

            // Albums
            if (types.Contains("album") && search.Albums?.Items != null)
            {
                foreach (var a in search.Albums.Items)
                {
                    if (a == null || string.IsNullOrEmpty(a.Id)) continue;
                    var artistNames = a.Artists != null ? string.Join(", ", a.Artists.Select(ar => ar?.Name).Where(n => !string.IsNullOrWhiteSpace(n))) : null;
                    items.Add(new SearchResultDto
                    {
                        Id = a.Id,
                        Name = a.Name,
                        Artists = artistNames,
                        AlbumName = a.Name,
                        DurationMs = 0,
                        Href = a.Href,
                        Uri = a.Uri,
                        AlbumImageUrl = a.Images?.FirstOrDefault()?.Url,
                        Type = "album",
                        Description = $"{artistNames}",
                        ImageUrl = a.Images?.FirstOrDefault()?.Url,
                        CanAddToPlaylist = false,
                        IsInSelectedPlaylist = false,
                        OriginalDto = a
                    });
                }
            }

            // Artists
            if (types.Contains("artist") && search.Artists?.Items != null)
            {
                foreach (var ar in search.Artists.Items)
                {
                    if (ar == null || string.IsNullOrEmpty(ar.Id)) continue;
                    items.Add(new SearchResultDto
                    {
                        Id = ar.Id,
                        Name = ar.Name,
                        Artists = ar.Name,
                        AlbumName = null,
                        DurationMs = 0,
                        Href = ar.Href,
                        Uri = ar.Uri,
                        AlbumImageUrl = ar.Images?.FirstOrDefault()?.Url,
                        Type = "artist",
                        Description = $"{ar.Followers?.Total ?? 0} followers",
                        ImageUrl = ar.Images?.FirstOrDefault()?.Url,
                        CanAddToPlaylist = false,
                        IsInSelectedPlaylist = false,
                        OriginalDto = ar
                    });
                }
            }

            // Playlists
            if (types.Contains("playlist") && search.Playlists?.Items != null)
            {
                foreach (var p in search.Playlists.Items)
                {
                    if (p == null || string.IsNullOrEmpty(p.Id)) continue;
                    items.Add(new SearchResultDto
                    {
                        Id = p.Id,
                        Name = p.Name,
                        Artists = p.Owner?.DisplayName ?? p.Owner?.Id,
                        AlbumName = null,
                        DurationMs = 0,
                        Href = p.Href,
                        Uri = p.Uri ?? string.Empty,
                        AlbumImageUrl = p.Images?.FirstOrDefault()?.Url,
                        Type = "playlist",
                        Description = $"{p.Tracks?.Total ?? 0} tracks",
                        ImageUrl = p.Images?.FirstOrDefault()?.Url,
                        CanAddToPlaylist = false,
                        IsInSelectedPlaylist = false,
                        OriginalDto = p
                    });
                }
            }

            result.Items = items;
            result.Total = items.Count; // For multi-type searches, we can't get accurate total
            result.Limit = limit;
            result.Offset = offset;

            // For multi-type searches, we need to determine if there are more pages
            // Check if any of the individual result sets have more pages
            var hasMorePages = (search.Tracks?.Next != null && types.Contains("track")) ||
                              (search.Albums?.Next != null && types.Contains("album")) ||
                              (search.Artists?.Next != null && types.Contains("artist")) ||
                              (search.Playlists?.Next != null && types.Contains("playlist"));

            if (hasMorePages)
            {
                result.Next = $"offset={offset + limit}"; // Simple next URL for pagination
            }

            return result;
        }

        public async Task AddTracksToPlaylistAsync(string playlistId, IEnumerable<string> trackUris)
        {
            ValidateSpotifyId(playlistId, nameof(playlistId));
            ValidateTrackCollection(trackUris, nameof(trackUris));

            var trackList = trackUris.ToList();
            if (trackList.Count > 100)
                throw new ArgumentException("Cannot add more than 100 tracks at once.", nameof(trackUris));

            if (!await EnsureAuthenticatedAsync().ConfigureAwait(false) || Api == null)
                return;

            var request = new PlaylistAddItemsRequest(trackList);
            await Api.Playlists.AddItems(playlistId, request).ConfigureAwait(false);
        }

        public async Task<PagingDto<TrackDto>> GetPlaylistTracksPageAsync(string playlistId, int offset, int limit)
        {
            ValidateSpotifyId(playlistId, nameof(playlistId));
            ValidatePaginationParameters(offset, limit, nameof(GetPlaylistTracksPageAsync));

            var result = new PagingDto<TrackDto>();
            if (!await EnsureAuthenticatedAsync().ConfigureAwait(false) || Api == null)
            {
                result.Items = new List<TrackDto>();
                result.Total = 0;
                result.Limit = limit;
                result.Offset = offset;
                return result;
            }

            var req = new PlaylistGetItemsRequest()
            {
                Offset = offset,
                Limit = limit
            };

            var page = await Api.Playlists.GetItems(playlistId, req).ConfigureAwait(false);
            result.Href = page.Href;
            result.Limit = page.Limit ?? limit;
            result.Offset = page.Offset ?? offset;
            result.Total = page.Total ?? 0;
            result.Next = page.Next;
            result.Previous = page.Previous;

            var items = new List<TrackDto>();
            if (page.Items != null)
            {
                foreach (var item in page.Items)
                {
                    if (item.Track is FullTrack fullTrack)
                    {
                        var artistNames = fullTrack.Artists != null ? string.Join(", ", fullTrack.Artists.Select(ar => ar?.Name).Where(n => !string.IsNullOrWhiteSpace(n))) : null;
                        // Map added_at from the playlist item (can be null). Spotify's SDK exposes AddedAt as DateTime?
                        System.DateTimeOffset? addedAt = null;
                        try
                        {
                            if (item.AddedAt.HasValue)
                            {
                                addedAt = new DateTimeOffset(item.AddedAt.Value);
                            }
                        }
                        catch
                        {
                            // If conversion fails, keep null
                            addedAt = null;
                        }

                        items.Add(new TrackDto
                        {
                            Id = fullTrack.Id,
                            Name = fullTrack.Name,
                            Artists = artistNames,
                            AlbumName = fullTrack.Album?.Name,
                            DurationMs = fullTrack.DurationMs,
                            Href = fullTrack.Href,
                            Uri = fullTrack.Uri,
                            AlbumImageUrl = fullTrack.Album?.Images?.FirstOrDefault()?.Url,
                            AddedAt = addedAt
                        });
                    }
                }
            }
            result.Items = items;
            return result;
        }

        /// <summary>
        /// Get user's top tracks from Spotify API
        /// </summary>
        /// <param name="limit">Number of tracks to return (1-50)</param>
        /// <param name="offset">Offset for pagination</param>
        /// <param name="timeRange">Time range: short_term, medium_term, long_term</param>
        public async Task<PagingDto<TrackDto>> GetUserTopTracksAsync(int limit = 20, int offset = 0, string timeRange = "medium_term")
        {
            ValidatePaginationParameters(offset, limit, nameof(GetUserTopTracksAsync));
            
            // Validate time range
            if (string.IsNullOrWhiteSpace(timeRange))
                throw new ArgumentException("Time range cannot be null, empty, or whitespace.", nameof(timeRange));
            
            var normalizedTimeRange = timeRange.ToLowerInvariant();
            if (normalizedTimeRange != "short_term" && normalizedTimeRange != "medium_term" && normalizedTimeRange != "long_term")
                throw new ArgumentException("Time range must be one of: 'short_term', 'medium_term', or 'long_term'.", nameof(timeRange));

            var result = new PagingDto<TrackDto> { Items = new List<TrackDto>() };
            
            if (!await EnsureAuthenticatedAsync().ConfigureAwait(false) || Api == null)
            {
                return result;
            }

            try
            {
                var request = new PersonalizationTopRequest
                {
                    Limit = limit,
                    Offset = offset,
                    TimeRangeParam = normalizedTimeRange switch
                    {
                        "short_term" => PersonalizationTopRequest.TimeRange.ShortTerm,
                        "long_term" => PersonalizationTopRequest.TimeRange.LongTerm,
                        _ => PersonalizationTopRequest.TimeRange.MediumTerm
                    }
                };
                var response = await Api.Personalization.GetTopTracks(request).ConfigureAwait(false);
                
                result.Total = response.Total ?? 0;
                result.Offset = response.Offset ?? 0;
                result.Limit = response.Limit ?? 0;

                var items = new List<TrackDto>();
                if (response.Items != null)
                {
                    foreach (var track in response.Items)
                    {
                        if (track == null || string.IsNullOrEmpty(track.Id)) continue;
                        
                        var artistNames = track.Artists != null ? 
                            string.Join(", ", track.Artists.Select(ar => ar?.Name).Where(n => !string.IsNullOrWhiteSpace(n))) : 
                            null;

                        items.Add(new TrackDto
                        {
                            Id = track.Id,
                            Name = track.Name,
                            Artists = artistNames,
                            AlbumName = track.Album?.Name,
                            DurationMs = track.DurationMs,
                            Href = track.Href,
                            Uri = track.Uri,
                            // Get the largest available image for album art
                            AlbumImageUrl = track.Album?.Images?.FirstOrDefault()?.Url
                        });
                    }
                }
                result.Items = items;
                
            }
            catch (Exception)
            {
                // Re-throw the exception so the caller can handle it properly
                throw;
            }

            return result;
        }

        /// <summary>
        /// Get artist's top tracks from Spotify API
        /// </summary>
        /// <param name="artistId">The Spotify ID of the artist</param>
        /// <param name="market">The market (country code) for which to retrieve top tracks</param>
        public async Task<List<TrackDto>> GetArtistTopTracksAsync(string artistId, string market = "US")
        {
            if (string.IsNullOrEmpty(artistId))
                throw new ArgumentNullException(nameof(artistId));
            if (string.IsNullOrEmpty(market))
                throw new ArgumentNullException(nameof(market));

            var result = new List<TrackDto>();
            
            if (!await EnsureAuthenticatedAsync().ConfigureAwait(false) || Api == null)
            {
                return result;
            }

            try
            {
                var request = new ArtistsTopTracksRequest(market);
                var response = await Api.Artists.GetTopTracks(artistId, request).ConfigureAwait(false);
                
                if (response != null && response.Tracks != null)
                {
                    foreach (var track in response.Tracks)
                    {
                        if (track == null || string.IsNullOrEmpty(track.Id)) continue;
                        
                        var artistNames = track.Artists != null ? 
                            string.Join(", ", track.Artists.Select(ar => ar?.Name).Where(n => !string.IsNullOrWhiteSpace(n))) : 
                            null;

                        result.Add(new TrackDto
                        {
                            Id = track.Id,
                            Name = track.Name,
                            Artists = artistNames,
                            AlbumName = track.Album?.Name,
                            DurationMs = track.DurationMs,
                            Href = track.Href,
                            Uri = track.Uri,
                            // Get the largest available image for album art
                            AlbumImageUrl = track.Album?.Images?.FirstOrDefault()?.Url
                        });
                    }
                }
                
            }
            catch (Exception)
            {
                // Re-throw the exception so the caller can handle it properly
                throw;
            }

            return result;
        }

        private readonly Dictionary<string, (List<TrackDto> Tracks, DateTime Fetched)> _albumTracksCache = new();
        private readonly object _albumTracksLock = new object();

        public async Task<List<TrackDto>> GetAlbumTracksAsync(string albumId)
        {
            ValidateSpotifyId(albumId, nameof(albumId));

            // Check cache first
            lock (_albumTracksLock)
            {
                if (_albumTracksCache.TryGetValue(albumId, out var cached) &&
                    (DateTime.Now - cached.Fetched) < TimeSpan.FromHours(1)) // Cache for 1 hour
                {
                    return cached.Tracks;
                }
            }

            if (!await EnsureAuthenticatedAsync().ConfigureAwait(false) || Api == null)
            {
                return new List<TrackDto>();
            }

            try
            {
                var request = new AlbumTracksRequest();
                var response = await Api.Albums.GetTracks(albumId, request).ConfigureAwait(false);

                var tracks = new List<TrackDto>();
                if (response.Items != null)
                {
                    foreach (var track in response.Items)
                    {
                        if (track == null || string.IsNullOrEmpty(track.Id)) continue;

                        var artistNames = track.Artists != null ? string.Join(", ", track.Artists.Select(ar => ar?.Name).Where(n => !string.IsNullOrWhiteSpace(n))) : null;

                        tracks.Add(new TrackDto
                        {
                            Id = track.Id,
                            Name = track.Name,
                            Artists = artistNames,
                            AlbumName = null, // Album name will be set from parent album context
                            DurationMs = track.DurationMs,
                            Href = track.Href,
                            Uri = track.Uri,
                            AlbumImageUrl = null // Will be set from album if needed
                        });
                    }
                }

                // Cache the result
                lock (_albumTracksLock)
                {
                    _albumTracksCache[albumId] = (tracks, DateTime.Now);
                }

                return tracks;
            }
            catch (Exception ex)
            {
                _loggingService.LogError($"Error getting album tracks for {albumId}: {ex.Message}");
                return new List<TrackDto>();
            }
        }

        private PagingDto<AlbumDto>? _cachedNewReleases;
        private DateTime _newReleasesLastFetched = DateTime.MinValue;
        private readonly object _newReleasesLock = new object();

        public async Task<PagingDto<AlbumDto>> GetNewReleasesPageAsync(int offset, int limit)
        {
            ValidatePaginationParameters(offset, limit, nameof(GetNewReleasesPageAsync));

            // For simplicity, only cache the first page (offset=0)
            if (offset == 0 && (DateTime.Now - _newReleasesLastFetched) < TimeSpan.FromMinutes(30))
            {
                lock (_newReleasesLock)
                {
                    if (_cachedNewReleases != null)
                    {
                        return _cachedNewReleases;
                    }
                }
            }

            var result = new PagingDto<AlbumDto>();
            if (!await EnsureAuthenticatedAsync().ConfigureAwait(false) || Api == null)
            {
                result.Items = new List<AlbumDto>();
                result.Total = 0;
                result.Limit = limit;
                result.Offset = offset;
                return result;
            }

            try
            {
                var request = new NewReleasesRequest
                {
                    Limit = limit,
                    Offset = offset
                };

                var response = await Api.Browse.GetNewReleases(request).ConfigureAwait(false);

                result.Href = response.Albums?.Href;
                result.Limit = response.Albums?.Limit ?? limit;
                result.Offset = response.Albums?.Offset ?? offset;
                result.Total = response.Albums?.Total ?? 0;
                result.Next = response.Albums?.Next;
                result.Previous = response.Albums?.Previous;

                var items = new List<AlbumDto>();
                if (response.Albums?.Items != null)
                {
                    foreach (var album in response.Albums.Items)
                    {
                        if (album == null || string.IsNullOrEmpty(album.Id)) continue;

                        var artistNames = album.Artists != null ? string.Join(", ", album.Artists.Select(ar => ar?.Name).Where(n => !string.IsNullOrWhiteSpace(n))) : null;

                        items.Add(new AlbumDto
                        {
                            Id = album.Id,
                            Name = album.Name,
                            Artists = artistNames,
                            ReleaseDate = album.ReleaseDate,
                            TotalTracks = album.TotalTracks,
                            Href = album.Href,
                            ImageUrl = album.Images?.FirstOrDefault()?.Url
                        });
                    }
                }
                result.Items = items;

                // Cache the first page
                if (offset == 0)
                {
                    lock (_newReleasesLock)
                    {
                        _cachedNewReleases = result;
                        _newReleasesLastFetched = DateTime.Now;
                    }
                }

                return result;
            }
            catch (Exception ex)
            {
                _loggingService.LogError($"Error getting new releases: {ex.Message}");
                return new PagingDto<AlbumDto> { Items = new List<AlbumDto>() };
            }
        }

        public async Task UploadPlaylistImageAsync(string playlistId, string base64Image)
        {
            if (string.IsNullOrEmpty(playlistId))
                throw new ArgumentNullException(nameof(playlistId));
            if (string.IsNullOrEmpty(base64Image))
                throw new ArgumentNullException(nameof(base64Image));

            await EnsureAuthenticatedAsync();

            if (Api == null)
                throw new InvalidOperationException("Spotify API client not initialized");

            try
            {
                // Get current access token
                var token = await GetCurrentAccessTokenAsync();
                if (string.IsNullOrEmpty(token))
                    throw new InvalidOperationException("No access token available");

                // Create HTTP client and request
                using (var httpClient = new HttpClient())
                {
                    httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
                    httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                    var url = $"https://api.spotify.com/v1/playlists/{playlistId}/images";
                    
                    // Create content with base64 image data
                    var content = new StringContent(base64Image);
                    content.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");

                    _loggingService.LogInfo($"Uploading playlist image to {url}, data length: {base64Image.Length} chars");

                    var response = await httpClient.PutAsync(url, content);
                    
                    if (!response.IsSuccessStatusCode)
                    {
                        var errorContent = await response.Content.ReadAsStringAsync();
                        _loggingService.LogError($"Playlist image upload failed: {response.StatusCode} - {errorContent}");
                        System.Diagnostics.Debug.WriteLine($"Playlist image upload failed: {response.StatusCode} - {errorContent}");
                        
                        // Check for specific error codes
                        if (response.StatusCode == HttpStatusCode.Unauthorized)
                            throw new UnauthorizedAccessException("Not authorized to upload playlist images. Check if you have the required scopes (ugc-image-upload, playlist-modify-public, playlist-modify-private).");
                        else if (response.StatusCode == HttpStatusCode.Forbidden)
                            throw new UnauthorizedAccessException("Forbidden: You don't have permission to modify this playlist.");
                        else if (response.StatusCode == HttpStatusCode.BadRequest)
                            throw new ArgumentException($"Bad request: {errorContent}");
                        else if (response.StatusCode == HttpStatusCode.UnsupportedMediaType)
                            throw new ArgumentException($"Unsupported media type. Make sure the image is a valid JPEG.");
                        else
                            throw new HttpRequestException($"HTTP {response.StatusCode}: {errorContent}");
                    }

                    _loggingService.LogInfo("Playlist image uploaded successfully");
                }
            }
            catch (Exception ex)
            {
                _loggingService.LogError($"Error uploading playlist image: {ex.Message}");
                throw;
            }
        }

    #region IDisposable Implementation

    private bool _disposed = false;

    /// <summary>
    /// Dispose resources used by the Spotify service
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Finalizer for safety net
    /// </summary>
    ~Spotify()
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

            // 1. Cancel any pending authentication operations
            try
            {
                if (_reauthTcs != null && !_reauthTcs.Task.IsCompleted)
                {
                    _reauthTcs.TrySetCanceled();
                }
            }
            catch (Exception ex)
            {
                _loggingService?.LogError($"Error cancelling reauth TCS in Spotify.Dispose: {ex.Message}");
            }

            // 2. Stop and dispose OAuth server
            try
            {
                _server?.Stop();
                _server?.Dispose();
            }
            catch (Exception ex)
            {
                _loggingService?.LogError($"Error disposing OAuth server: {ex.Message}", ex);
            }

            // 3. Dispose semaphores
            try
            {
                _semaphore?.Dispose();
            }
            catch (Exception ex)
            {
                _loggingService?.LogError($"Error disposing semaphore: {ex.Message}", ex);
            }

            try
            {
                _authSemaphore?.Dispose();
            }
            catch (Exception ex)
            {
                _loggingService?.LogError($"Error disposing auth semaphore: {ex.Message}", ex);
            }

            // Note: _http is static and shared across instances, so we don't dispose it here
            // In a production app, consider using IHttpClientFactory for proper lifecycle management

            // 4. Clear references to prevent memory leaks
            _reauthTcs = null;
            _privateProfile = null;
            _currentToken = null;
            Api = null;
        }

        _disposed = true;
    }

    #endregion
}
}


