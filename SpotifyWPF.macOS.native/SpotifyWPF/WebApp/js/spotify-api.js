// Spofify API - Web API Integration
class SpotifyAPI {
    constructor() {
        this.baseUrl = 'https://api.spotify.com/v1';
        this.accessToken = null;
        this.clientId = window.SPOTIFY_CONFIG?.clientId || null;
        this.redirectUri = window.SPOTIFY_CONFIG?.redirectUri || 'spofifywpf://callback';

        // Rate limit tracking
        this.rateLimitState = {
            limit: null,
            remaining: null,
            reset: null,
            retryAfter: null,
            isRateLimited: false,
            lastRateLimitTime: null,
            lastResponseTime: null
        };
    }

    setClientId(clientId) {
        this.clientId = clientId;
    }

    setAccessToken(token) {
        this.accessToken = token;
    }

    isOnline() {
        return navigator.onLine;
    }

    async validateToken() {
        console.log('=== TOKEN VALIDATION START ===');
        if (!this.accessToken) {
            console.log('No access token available for validation');
            return false;
        }

        console.log('Validating token with Spotify API...');
        try {
            // Make a lightweight request to validate the token
            const response = await this.makeRequest('/me', { method: 'HEAD' });
            console.log('Token validation successful');
            return true;
        } catch (error) {
            console.error('Token validation failed:', error);
            console.log('Error details:', {
                message: error.message,
                status: error.status,
                statusText: error.statusText,
                url: error.url
            });

            if (error.message.includes('expired') || error.message.includes('401') || error.status === 401) {
                // Try to refresh the token
                console.log('Token appears expired (401), attempting to refresh...');
                try {
                    await this.refreshAccessToken();
                    console.log('Token refresh successful');
                    return true;
                } catch (refreshError) {
                    console.error('Failed to refresh token:', refreshError);
                    console.log('Clearing stored tokens due to refresh failure');
                    this.accessToken = null;
                    localStorage.removeItem('spotify_access_token');
                    localStorage.removeItem('spotify_refresh_token');
                    localStorage.removeItem('spotify_token_expires');
                    return false;
                }
            }
            // For other errors, assume token is still valid
            console.log('Non-401 error, assuming token is still valid:', error.message);
            return true;
        } finally {
            console.log('=== TOKEN VALIDATION END ===');
        }
    }

    isTokenExpired() {
        const expirationTime = localStorage.getItem('spotify_token_expires');
        if (!expirationTime) {
            return true; // No expiration time stored, consider expired
        }

        const currentTime = Date.now();
        const expTime = parseInt(expirationTime);

        // Add 5 minute buffer to prevent edge cases
        const isExpired = currentTime >= (expTime - (5 * 60 * 1000));

        console.log('Token expiration check:', {
            currentTime: new Date(currentTime),
            expirationTime: new Date(expTime),
            isExpired: isExpired,
            timeUntilExpiry: Math.max(0, expTime - currentTime) / 1000 / 60, // minutes
        });

        return isExpired;
    }

    async getAuthorizationUrl() {
        if (!this.clientId) {
            throw new Error('Client ID not set. Please configure your Spotify app first.');
        }

        // Generate PKCE code verifier and challenge
        const codeVerifier = this.generateCodeVerifier();
        const codeChallenge = await this.generateCodeChallenge(codeVerifier);

        // Store code verifier for later use
        sessionStorage.setItem('spotify_code_verifier', codeVerifier);

        const scopes = window.SPOTIFY_CONFIG?.scopes || [
            'streaming',
            'user-read-email',
            'user-read-private',
            'user-library-read',
            'user-library-modify',
            'user-read-playback-state',
            'user-modify-playback-state',
            'playlist-read-private',
            'playlist-read-collaborative',
            'playlist-modify-public',
            'playlist-modify-private'
        ];

        const params = new URLSearchParams({
            client_id: this.clientId,
            response_type: 'code', // Changed from 'token' to 'code'
            redirect_uri: this.redirectUri,
            scope: scopes.join(' '),
            code_challenge_method: 'S256',
            code_challenge: codeChallenge,
            show_dialog: 'true'
        });

        return `https://accounts.spotify.com/authorize?${params.toString()}`;
    }

    generateCodeVerifier() {
        const array = new Uint8Array(32);
        crypto.getRandomValues(array);
        return this.base64URLEncode(array);
    }

    async generateCodeChallenge(codeVerifier) {
        const encoder = new TextEncoder();
        const data = encoder.encode(codeVerifier);
        const digest = await crypto.subtle.digest('SHA-256', data);
        return this.base64URLEncode(new Uint8Array(digest));
    }

    base64URLEncode(array) {
        const base64 = btoa(String.fromCharCode.apply(null, array));
        return base64.replace(/\+/g, '-').replace(/\//g, '_').replace(/=/g, '');
    }

    async exchangeCodeForToken(code) {
        const codeVerifier = sessionStorage.getItem('spotify_code_verifier');
        if (!codeVerifier) {
            throw new Error('Code verifier not found');
        }

        const response = await fetch('https://accounts.spotify.com/api/token', {
            method: 'POST',
            headers: {
                'Content-Type': 'application/x-www-form-urlencoded',
            },
            body: new URLSearchParams({
                client_id: this.clientId,
                grant_type: 'authorization_code',
                code: code,
                redirect_uri: this.redirectUri,
                code_verifier: codeVerifier,
            }),
        });

        if (!response.ok) {
            const errorData = await response.json();
            throw new Error(`Token exchange failed: ${errorData.error_description || errorData.error}`);
        }

        const data = await response.json();

        // Clear the code verifier from storage
        sessionStorage.removeItem('spotify_code_verifier');

        return {
            access_token: data.access_token,
            token_type: data.token_type,
            expires_in: data.expires_in,
            refresh_token: data.refresh_token,
        };
    }

    async refreshAccessToken() {
        const refreshToken = localStorage.getItem('spotify_refresh_token');
        if (!refreshToken) {
            throw new Error('No refresh token available');
        }

        console.log('Attempting to refresh access token...');

        const response = await fetch('https://accounts.spotify.com/api/token', {
            method: 'POST',
            headers: {
                'Content-Type': 'application/x-www-form-urlencoded',
            },
            body: new URLSearchParams({
                client_id: this.clientId,
                grant_type: 'refresh_token',
                refresh_token: refreshToken,
            }),
        });

        if (!response.ok) {
            const errorData = await response.json();
            console.error('Token refresh failed:', errorData);
            throw new Error(`Token refresh failed: ${errorData.error_description || errorData.error}`);
        }

        const data = await response.json();
        console.log('Token refresh successful');

        // Update stored tokens
        localStorage.setItem('spotify_access_token', data.access_token);
        this.accessToken = data.access_token;

        // Update refresh token if a new one is provided
        if (data.refresh_token) {
            localStorage.setItem('spotify_refresh_token', data.refresh_token);
        }

        // Update expiration time
        const expirationTime = Date.now() + (data.expires_in * 1000);
        localStorage.setItem('spotify_token_expires', expirationTime);

        console.log('Token refresh completed, new expiration:', new Date(expirationTime));

        return {
            access_token: data.access_token,
            token_type: data.token_type,
            expires_in: data.expires_in,
            refresh_token: data.refresh_token,
        };
    }

    async makeRequest(endpoint, options = {}, retryCount = 0) {
        console.log(`Making API request: ${endpoint} (attempt ${retryCount + 1})`);

        // Check rate limit status before making request
        if (!this.isSafeToRequest() && retryCount === 0) {
            const status = this.getRateLimitStatus();
            console.warn('Rate limit check failed:', status);

            if (status.isRateLimited) {
                const waitTime = status.retryAfter * 1000;
                console.log(`Rate limited, waiting ${status.retryAfter} seconds before request`);

                if (window.app) {
                    window.app.showError(`Rate limited. Waiting ${status.retryAfter} seconds...`);
                }

                await new Promise(resolve => setTimeout(resolve, waitTime));
            }
        }

        if (!this.accessToken) {
            console.error('No access token available for request');
            throw new Error('No access token available');
        }

        const url = `${this.baseUrl}${endpoint}`;
        const defaultOptions = {
            headers: {
                'Authorization': `Bearer ${this.accessToken}`,
                'Content-Type': 'application/json'
            }
        };

        try {
            const response = await fetch(url, { ...defaultOptions, ...options });
            console.log(`API response status: ${response.status} for ${endpoint}`);

            // Parse and log rate limit headers for monitoring
            const rateLimitInfo = this.parseRateLimitHeaders(response);
            if (rateLimitInfo.limit || rateLimitInfo.remaining) {
                console.log('Rate limit info:', {
                    limit: rateLimitInfo.limit,
                    remaining: rateLimitInfo.remaining,
                    reset: rateLimitInfo.reset ? new Date(parseInt(rateLimitInfo.reset) * 1000) : null,
                    retryAfter: rateLimitInfo.retryAfter
                });
            }

            if (!response.ok) {
                if (response.status === 401) {
                    // Token expired
                    console.log('401 Unauthorized - token expired');
                    localStorage.removeItem('spotify_access_token');
                    if (window.app) {
                        window.app.showError('Your session has expired. Please reconnect to Spotify.');
                    }
                    const error = new Error('Access token expired');
                    error.status = 401;
                    throw error;
                }

                if (response.status === 429) {
                    // Rate limited - enhanced handling for batch operations
                    const retryAfter = parseInt(response.headers.get('Retry-After')) || 30;
                    const retryDelay = Math.min(retryAfter * 1000, 60000); // Cap at 60 seconds

                    console.log(`Rate limited (429). Retry-After: ${retryAfter}s, will retry in ${retryDelay/1000}s (attempt ${retryCount + 1}/3)`);

                    // Log detailed rate limit information
                    const rateLimitStatus = this.getRateLimitStatus();
                    console.log('Current rate limit status:', {
                        isRateLimited: rateLimitStatus.isRateLimited,
                        remaining: rateLimitStatus.remaining,
                        limit: rateLimitStatus.limit,
                        timeUntilReset: Math.ceil(rateLimitStatus.timeUntilReset / 1000),
                        lastRateLimitTime: rateLimitStatus.lastRateLimitTime ? new Date(rateLimitStatus.lastRateLimitTime) : null
                    });

                    if (window.app) {
                        window.app.showError(`Rate limited. Waiting ${retryDelay/1000} seconds before retry...`);
                    }

                    if (retryCount < 3) {
                        // Exponential backoff with jitter for batch operations
                        const jitter = Math.random() * 1000; // Add up to 1 second of jitter
                        const backoffDelay = retryDelay + (retryCount * 2000) + jitter;

                        console.log(`Applying exponential backoff: ${backoffDelay/1000}s total delay`);
                        await new Promise(resolve => setTimeout(resolve, backoffDelay));

                        return this.makeRequest(endpoint, options, retryCount + 1);
                    }

                    // After max retries, throw a more informative error
                    const rateLimitError = new Error(`Rate limit exceeded after ${retryCount + 1} attempts. Please wait before trying again.`);
                    rateLimitError.status = 429;
                    rateLimitError.retryAfter = retryAfter;
                    rateLimitError.isRateLimit = true;
                    throw rateLimitError;
                }

                if (response.status >= 500) {
                    // Server error - retry once
                    console.log(`Server error ${response.status}, retrying`);
                    if (retryCount < 1) {
                        await new Promise(resolve => setTimeout(resolve, 1000));
                        return this.makeRequest(endpoint, options, retryCount + 1);
                    }
                }

                let errorMessage = `API request failed: ${response.status} ${response.statusText}`;
                try {
                    const errorData = await response.json();
                    if (errorData.error?.message) {
                        errorMessage = errorData.error.message;
                    }
                } catch (e) {
                    // Ignore JSON parsing errors
                }

                console.error(`API request failed: ${errorMessage}`);
                const error = new Error(errorMessage);
                error.status = response.status;
                error.statusText = response.statusText;
                error.url = url;
                throw error;
            }

            console.log(`API request successful: ${endpoint}`);
            // For HEAD requests, don't try to parse JSON since there's no response body
            if (options && options.method === 'HEAD') {
                return response;
            }
            // For 204 No Content responses, return null instead of trying to parse JSON
            if (response.status === 204) {
                return null;
            }
            return response.json();
        } catch (error) {
            if (error.name === 'TypeError' && error.message.includes('fetch')) {
                // Network error
                console.error('Network error during API request:', error);
                if (window.app) {
                    window.app.showError('Network connection error. Please check your internet connection.');
                }
                throw new Error('Network connection error');
            }

            // Re-throw other errors
            console.error('API request error:', error);
            throw error;
        }
    }

    // User Profile
    async getCurrentUserProfile() {
        return this.makeRequest('/me');
    }

    // Search
    async search(query, types = ['track', 'artist', 'album', 'playlist'], limit = 20) {
        const params = new URLSearchParams({
            q: query,
            type: types.join(','),
            limit: limit.toString()
        });

        return this.makeRequest(`/search?${params.toString()}`);
    }

    // Library
    async getSavedTracks(limit = 50, offset = 0) {
        const params = new URLSearchParams({
            limit: limit.toString(),
            offset: offset.toString()
        });

        return this.makeRequest(`/me/tracks?${params.toString()}`);
    }

    async getSavedAlbums(limit = 50, offset = 0) {
        const params = new URLSearchParams({
            limit: limit.toString(),
            offset: offset.toString()
        });

        return this.makeRequest(`/me/albums?${params.toString()}`);
    }

    async saveTracks(trackIds) {
        return this.makeRequest('/me/tracks', {
            method: 'PUT',
            body: JSON.stringify({ ids: trackIds })
        });
    }

    async removeSavedTracks(trackIds) {
        return this.makeRequest('/me/tracks', {
            method: 'DELETE',
            body: JSON.stringify({ ids: trackIds })
        });
    }

    // Playlists
    async getUserPlaylists(limit = 50, offset = 0) {
        const params = new URLSearchParams({
            limit: limit.toString(),
            offset: offset.toString()
        });

        return this.makeRequest(`/me/playlists?${params.toString()}`);
    }

    async getAllUserPlaylists() {
        try {
            // First, get the total count
            const firstBatch = await this.getUserPlaylists(1, 0);
            const total = firstBatch.total;

            if (total <= 50) {
                // If 50 or fewer playlists, just get them all at once
                return await this.getUserPlaylists(50, 0);
            }

            // Calculate how many batches we need (using 50 per batch for efficiency)
            const batchSize = 50;
            const numBatches = Math.ceil(total / batchSize);

            // Create batches with offsets
            const batches = [];
            for (let i = 0; i < numBatches; i++) {
                const offset = i * batchSize;
                const limit = Math.min(batchSize, total - offset);
                batches.push({ offset, limit });
            }

            // Execute all requests in parallel
            const batchPromises = batches.map(batch =>
                this.getUserPlaylists(batch.limit, batch.offset)
            );

            const results = await Promise.all(batchPromises);

            // Combine all results
            const allItems = results.flatMap(result => result.items);
            const combinedResult = {
                ...results[0], // Copy metadata from first result
                items: allItems,
                total: total,
                limit: allItems.length,
                offset: 0
            };

            return combinedResult;

        } catch (error) {
            console.error('Failed to load all playlists:', error);
            throw error;
        }
    }

    async getPlaylist(playlistId) {
        return this.makeRequest(`/playlists/${playlistId}`);
    }

    async getPlaylistTracks(playlistId, limit = 100, offset = 0) {
        const params = new URLSearchParams({
            limit: limit.toString(),
            offset: offset.toString()
        });

        return this.makeRequest(`/playlists/${playlistId}/tracks?${params.toString()}`);
    }

    /**
     * Get all tracks from a playlist with automatic pagination
     * @param {string} playlistId - The playlist ID
     * @returns {Promise<Array>} Array of all track objects
     */
    async getAllPlaylistTracks(playlistId) {
        const allTracks = [];
        let offset = 0;
        const limit = 100;

        while (true) {
            const response = await this.getPlaylistTracks(playlistId, limit, offset);
            const tracks = response.items || [];

            allTracks.push(...tracks);
            offset += limit;

            // Check if we've reached the end
            if (tracks.length < limit || offset >= response.total) {
                break;
            }
        }

        return allTracks;
    }

    async createPlaylist(name, description = '', isPublic = false) {
        const profile = await this.getCurrentUserProfile();
        return this.makeRequest(`/users/${profile.id}/playlists`, {
            method: 'POST',
            body: JSON.stringify({
                name: name,
                description: description,
                public: isPublic
            })
        });
    }

    async addTracksToPlaylist(playlistId, trackUris, position = 0) {
        return this.makeRequest(`/playlists/${playlistId}/tracks`, {
            method: 'POST',
            body: JSON.stringify({
                uris: trackUris,
                position: position
            })
        });
    }

    // Playback

    async startPlayback(contextUri = null, uris = null, offset = null, positionMs = 0) {
        const body = {
            position_ms: positionMs
        };

        if (contextUri) {
            body.context_uri = contextUri;
        }

        if (uris) {
            body.uris = uris;
        }

        if (offset) {
            body.offset = offset;
        }

        return this.makeRequest('/me/player/play', {
            method: 'PUT',
            body: JSON.stringify(body)
        });
    }

    async pausePlayback() {
        return this.makeRequest('/me/player/pause', {
            method: 'PUT'
        });
    }

    async resumePlayback() {
        return this.makeRequest('/me/player/play', {
            method: 'PUT'
        });
    }

    async skipToNext() {
        return this.makeRequest('/me/player/next', {
            method: 'POST'
        });
    }

    async skipToPrevious() {
        return this.makeRequest('/me/player/previous', {
            method: 'POST'
        });
    }

    // Recently Played
    async getRecentlyPlayedTracks(limit = 50, before = null, after = null) {
        const params = new URLSearchParams({
            limit: limit.toString()
        });

        if (before) params.append('before', before);
        if (after) params.append('after', after);

        return this.makeRequest(`/me/player/recently-played?${params.toString()}`);
    }

    // Top Artists/Tracks
    async getTopArtists(timeRange = 'medium_term', limit = 20, offset = 0) {
        const params = new URLSearchParams({
            time_range: timeRange,
            limit: limit.toString(),
            offset: offset.toString()
        });

        return this.makeRequest(`/me/top/artists?${params.toString()}`);
    }

    async getTopTracks(timeRange = 'medium_term', limit = 20, offset = 0) {
        const params = new URLSearchParams({
            time_range: timeRange,
            limit: limit.toString(),
            offset: offset.toString()
        });

        return this.makeRequest(`/me/top/tracks?${params.toString()}`);
    }

    // Browse
    async getFeaturedPlaylists(limit = 20, offset = 0) {
        const params = new URLSearchParams({
            limit: limit.toString(),
            offset: offset.toString()
        });

        return this.makeRequest(`/browse/featured-playlists?${params.toString()}`);
    }

    async getNewReleases(limit = 20, offset = 0) {
        const params = new URLSearchParams({
            limit: limit.toString(),
            offset: offset.toString()
        });

        return this.makeRequest(`/browse/new-releases?${params.toString()}`);
    }

    async getCategories(limit = 20, offset = 0) {
        const params = new URLSearchParams({
            limit: limit.toString(),
            offset: offset.toString()
        });

        return this.makeRequest(`/browse/categories?${params.toString()}`);
    }

    async getCategoryPlaylists(categoryId, limit = 20, offset = 0) {
        const params = new URLSearchParams({
            limit: limit.toString(),
            offset: offset.toString()
        });

        return this.makeRequest(`/browse/categories/${categoryId}/playlists?${params.toString()}`);
    }

    // Utility methods
    extractTrackId(uri) {
        // spotify:track:4iV5W9uYEdYUVa79Axb7Rh -> 4iV5W9uYEdYUVa79Axb7Rh
        const match = uri.match(/spotify:track:([a-zA-Z0-9]+)/);
        return match ? match[1] : null;
    }

    extractArtistId(uri) {
        const match = uri.match(/spotify:artist:([a-zA-Z0-9]+)/);
        return match ? match[1] : null;
    }

    extractAlbumId(uri) {
        const match = uri.match(/spotify:album:([a-zA-Z0-9]+)/);
        return match ? match[1] : null;
    }

    extractPlaylistId(uri) {
        const match = uri.match(/spotify:playlist:([a-zA-Z0-9]+)/);
        return match ? match[1] : null;
    }

    // Device Management
    async getDevices() {
        return this.makeRequest('/me/player/devices');
    }

    async transferPlayback(deviceId, play = true) {
        return this.makeRequest('/me/player', {
            method: 'PUT',
            body: JSON.stringify({
                device_ids: [deviceId],
                play: play
            })
        });
    }

    async getCurrentPlaybackState() {
        return this.makeRequest('/me/player');
    }

    async setVolume(volumePercent) {
        const params = new URLSearchParams({
            volume_percent: Math.round(volumePercent).toString()
        });
        return this.makeRequest(`/me/player/volume?${params.toString()}`, {
            method: 'PUT'
        });
    }

    async setRepeatMode(state) {
        // state can be 'track', 'context', or 'off'
        const params = new URLSearchParams({
            state: state
        });
        return this.makeRequest(`/me/player/repeat?${params.toString()}`, {
            method: 'PUT'
        });
    }

    async setShuffleMode(state) {
        // state is boolean
        const params = new URLSearchParams({
            state: state.toString()
        });
        return this.makeRequest(`/me/player/shuffle?${params.toString()}`, {
            method: 'PUT'
        });
    }

    async seekToPosition(positionMs) {
        const params = new URLSearchParams({
            position_ms: positionMs.toString()
        });
        return this.makeRequest(`/me/player/seek?${params.toString()}`, {
            method: 'PUT'
        });
    }

    async deletePlaylist(playlistId) {
        return this.makeRequest(`/playlists/${playlistId}/followers`, {
            method: 'DELETE'
        });
    }

    async unfollowPlaylist(playlistId) {
        // Same API endpoint as delete, but conceptually different for followed playlists
        return this.makeRequest(`/playlists/${playlistId}/followers`, {
            method: 'DELETE'
        });
    }

    /**
     * Check if we're currently rate limited and get estimated wait time
     * @returns {Object} Rate limit status
     */
    getRateLimitStatus() {
        const now = Date.now();
        const resetTime = this.rateLimitState.reset ? this.rateLimitState.reset * 1000 : null;

        // Check if rate limit has expired
        if (resetTime && now >= resetTime) {
            this.rateLimitState.isRateLimited = false;
            this.rateLimitState.remaining = this.rateLimitState.limit; // Reset remaining to limit
        }

        // Calculate time until reset
        let timeUntilReset = null;
        if (resetTime) {
            timeUntilReset = Math.max(0, resetTime - now);
        }

        return {
            isRateLimited: this.rateLimitState.isRateLimited,
            retryAfter: this.rateLimitState.retryAfter || (timeUntilReset ? Math.ceil(timeUntilReset / 1000) : 0),
            lastRateLimitTime: this.rateLimitState.lastRateLimitTime,
            limit: this.rateLimitState.limit,
            remaining: this.rateLimitState.remaining,
            resetTime: resetTime,
            timeUntilReset: timeUntilReset,
            lastResponseTime: this.rateLimitState.lastResponseTime
        };
    }

    /**
     * Get rate limit headers from a response for monitoring
     * @param {Response} response - Fetch response object
     * @returns {Object} Rate limit information
     */
    parseRateLimitHeaders(response) {
        const rateLimitInfo = {
            limit: response.headers.get('X-RateLimit-Limit'),
            remaining: response.headers.get('X-RateLimit-Remaining'),
            reset: response.headers.get('X-RateLimit-Reset'),
            retryAfter: response.headers.get('Retry-After')
        };

        // Update internal rate limit state
        if (rateLimitInfo.limit !== null) {
            this.rateLimitState.limit = parseInt(rateLimitInfo.limit);
        }
        if (rateLimitInfo.remaining !== null) {
            this.rateLimitState.remaining = parseInt(rateLimitInfo.remaining);
        }
        if (rateLimitInfo.reset !== null) {
            this.rateLimitState.reset = parseInt(rateLimitInfo.reset);
        }
        if (rateLimitInfo.retryAfter !== null) {
            this.rateLimitState.retryAfter = parseInt(rateLimitInfo.retryAfter);
        }

        // Update rate limited status
        this.rateLimitState.isRateLimited = response.status === 429 ||
            (this.rateLimitState.remaining !== null && this.rateLimitState.remaining <= 0);

        // Track timing
        this.rateLimitState.lastResponseTime = Date.now();

        if (this.rateLimitState.isRateLimited) {
            this.rateLimitState.lastRateLimitTime = Date.now();
        }

        return rateLimitInfo;
    }

    /**
     * Check if it's safe to make an API request
     * @returns {boolean} True if safe to make request
     */
    isSafeToRequest() {
        const status = this.getRateLimitStatus();
        return !status.isRateLimited && (status.remaining === null || status.remaining > 0);
    }

    /**
     * Reset rate limit state (useful for testing or manual override)
     */
    resetRateLimitState() {
        this.rateLimitState = {
            limit: null,
            remaining: null,
            reset: null,
            retryAfter: null,
            isRateLimited: false,
            lastRateLimitTime: null,
            lastResponseTime: null
        };
        console.log('Rate limit state reset');
    }
}