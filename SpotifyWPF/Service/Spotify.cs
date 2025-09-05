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

namespace SpotifyWPF.Service
{
    public class Spotify : ISpotify
    {
        private readonly ISettingsProvider _settingsProvider;

        private readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);

        private readonly EmbedIOAuthServer _server;

        private PrivateUser? _privateProfile;

        private Action? _loginSuccessAction;

        private readonly TokenStorage _tokenStorage = new TokenStorage();
        private TokenInfo? _currentToken;

        // Coordina una sola ri-autenticazione per volta tra più worker
        private readonly SemaphoreSlim _authSemaphore = new SemaphoreSlim(1, 1);
        private TaskCompletionSource<bool>? _reauthTcs;

        public ISpotifyClient? Api { get; private set; } = null;

        public Spotify(ISettingsProvider settingsProvider)
        {
            _settingsProvider = settingsProvider;

            _server = new EmbedIOAuthServer(
                new Uri($"http://localhost:{_settingsProvider.SpotifyRedirectPort}"),
                int.Parse(_settingsProvider.SpotifyRedirectPort));

            _server.AuthorizationCodeReceived += OnAuthorizationCodeReceived;
            _server.ErrorReceived += OnErrorReceived;

            // Esplicito per evitare CS0649 (il campo viene comunque impostato in EnsureAuthenticatedAsync quando serve)
            _reauthTcs = null;
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

        public async Task LoginAsync(Action? onSuccess)
        {
            _loginSuccessAction = onSuccess;

            // Prova token valido da storage
            var saved = _tokenStorage.Load();
            if (saved != null && !string.IsNullOrWhiteSpace(saved.AccessToken) && saved.ExpiresAtUtc > DateTime.UtcNow.AddMinutes(1))
            {
                _currentToken = saved;
                Api = new SpotifyClient(saved.AccessToken);
                InvokeOnUiThread(_loginSuccessAction);
                return;
            }

            // Token scaduto ma con refresh token: tenta refresh silenzioso
            if (saved != null && !string.IsNullOrWhiteSpace(saved.RefreshToken))
            {
                var refreshed = await TryRefreshAsync(saved.RefreshToken).ConfigureAwait(false);
                if (refreshed)
                {
                    InvokeOnUiThread(_loginSuccessAction);
                    return;
                }
            }

            // Avvia PKCE interactive login
            await _server.Start().ConfigureAwait(false);

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
                    Scopes.UserReadPrivate, Scopes.PlaylistModifyPrivate, Scopes.PlaylistModifyPublic,
                    Scopes.PlaylistReadCollaborative, Scopes.PlaylistReadPrivate
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
            await _server.Stop().ConfigureAwait(false);

            var oauth = new OAuthClient();
            var tokenResponse = await oauth.RequestToken(new PKCETokenRequest(
                _settingsProvider.SpotifyClientId,
                response.Code,
                _server.BaseUri,
                _pkceVerifier
            )).ConfigureAwait(false);

            Api = new SpotifyClient(tokenResponse.AccessToken);

            try
            {
                var expiresAt = DateTime.UtcNow.AddSeconds(tokenResponse.ExpiresIn).AddSeconds(-30);
                _currentToken = new TokenInfo
                {
                    AccessToken = tokenResponse.AccessToken,
                    RefreshToken = tokenResponse.RefreshToken,
                    ExpiresAtUtc = expiresAt
                };
                _tokenStorage.Save(_currentToken);
            }
            catch
            {
                // Ignora problemi di salvataggio
            }

            var tcs = _reauthTcs;
            if (tcs != null)
            {
                tcs.TrySetResult(true);
            }

            InvokeOnUiThread(_loginSuccessAction);
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

        // Metodo pubblico usato per verificare/ripristinare l'autenticazione basandosi sul token salvato
        public async Task<bool> EnsureAuthenticatedAsync()
        {
            // Usa cache in memoria se disponibile
            var token = _currentToken;
            if (token == null)
            {
                token = _tokenStorage.Load();
                _currentToken = token;
            }

            if (token == null)
            {
                return false;
            }

            // Se il token sta per scadere o è scaduto, prova refresh
            if (token.ExpiresAtUtc <= DateTime.UtcNow.AddSeconds(30))
            {
                if (!string.IsNullOrWhiteSpace(token.RefreshToken))
                {
                    var ok = await TryRefreshAsync(token.RefreshToken).ConfigureAwait(false);
                    if (!ok) return false;
                    token = _currentToken;
                    if (token == null) return false;
                }
                else
                {
                    return false;
                }
            }

            var access = token?.AccessToken;
            if (string.IsNullOrWhiteSpace(access))
            {
                return false;
            }

            if (Api == null)
            {
                Api = new SpotifyClient(access);
            }

            return true;
        }

        private async Task<bool> TryRefreshAsync(string refreshToken)
        {
            try
            {
                var oauth = new OAuthClient();
                var refreshed = await oauth.RequestToken(new PKCETokenRefreshRequest(
                    _settingsProvider.SpotifyClientId, refreshToken)).ConfigureAwait(false);

                Api = new SpotifyClient(refreshed.AccessToken);

                var expiresAt = DateTime.UtcNow.AddSeconds(refreshed.ExpiresIn).AddSeconds(-30);
                _currentToken = new TokenInfo
                {
                    AccessToken = refreshed.AccessToken,
                    RefreshToken = string.IsNullOrWhiteSpace(refreshed.RefreshToken) ? refreshToken : refreshed.RefreshToken,
                    ExpiresAtUtc = expiresAt
                };
                _tokenStorage.Save(_currentToken);

                return true;
            }
            catch
            {
                _tokenStorage.Clear();
                _currentToken = null;
                Api = null;
                return false;
            }
        }

        // Implementazione esplicita per garantire la conformità all'interfaccia anche in build temporanee
        async Task<bool> ISpotify.EnsureAuthenticatedAsync()
        {
            return await EnsureAuthenticatedAsync().ConfigureAwait(false);
        }

        private static readonly HttpClient _http = new HttpClient();

        public async Task<bool?> CheckIfCurrentUserFollowsPlaylistAsync(string playlistId)
        {
            if (string.IsNullOrWhiteSpace(playlistId)) return null;

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
                        TracksTotal = p.Tracks?.Total ?? 0,
                        SnapshotId = p.SnapshotId,
                        Href = p.Href
                    });
                }
            }
            result.Items = items;
            return result;
        }

        public async Task<PagingDto<PlaylistDto>> SearchPlaylistsPageAsync(string query, int offset, int limit)
        {
            var result = new PagingDto<PlaylistDto>();
            if (string.IsNullOrWhiteSpace(query) || !await EnsureAuthenticatedAsync().ConfigureAwait(false) || Api == null)
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

        public async Task<PagingDto<AlbumDto>> SearchAlbumsPageAsync(string query, int offset, int limit)
        {
            var result = new PagingDto<AlbumDto>();
            if (string.IsNullOrWhiteSpace(query) || !await EnsureAuthenticatedAsync().ConfigureAwait(false) || Api == null)
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
            var result = new PagingDto<ArtistDto>();
            if (string.IsNullOrWhiteSpace(query) || !await EnsureAuthenticatedAsync().ConfigureAwait(false) || Api == null)
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
            var result = new PagingDto<TrackDto>();
            if (string.IsNullOrWhiteSpace(query) || !await EnsureAuthenticatedAsync().ConfigureAwait(false) || Api == null)
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
                        Href = t.Href
                    });
                }
            }
            result.Items = items;
            return result;
        }

        public async Task UnfollowPlaylistAsync(string playlistId)
        {
            if (string.IsNullOrWhiteSpace(playlistId)) return;
            if (!await EnsureAuthenticatedAsync().ConfigureAwait(false) || Api == null) return;
            await Api.Follow.UnfollowPlaylist(playlistId).ConfigureAwait(false);
        }
    }
}
