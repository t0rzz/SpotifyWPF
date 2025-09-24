// Spofify - Main Application Logic
class SpotifyMacOSApp {
    constructor() {
        this.currentSection = 'home';
        this.player = null;
        this.spotifyApi = window.spotifyApi; // Use global instance
        this.isConnected = false;
        this.deviceRefreshTimer = null;
        this.deviceRefreshInterval = 30000; // 30 seconds

        // Initialize playlist-related properties
        this.allPlaylists = [];
        this.filteredPlaylists = null;
        this.currentSort = { column: 'name', direction: 'asc' };

        this.init();
    }

    init() {
        console.log('🚀 Initializing Spofify MacOS App...');
        console.log('Current URL:', window.location.href);
        console.log('User Agent:', navigator.userAgent);

        this.bindEvents();
        this.initializeSpotify();
        this.restoreSectionFromURL();
        this.updateUI();

        console.log('✅ App initialization completed');
    }

    bindEvents() {
        // Navigation
        document.querySelectorAll('.nav-item').forEach(item => {
            item.addEventListener('click', (e) => {
                const section = e.currentTarget.dataset.section;
                this.switchSection(section);
            });
        });

        // Connect button
        document.getElementById('connect-btn').addEventListener('click', () => {
            console.log('🔘 Connect button clicked!');
            this.connectToSpotify();
        });

        // Disconnect button
        document.getElementById('disconnect-btn').addEventListener('click', () => {
            this.disconnect();
        });

        // Global context menu handler to prevent default browser menu
        document.addEventListener('contextmenu', (e) => {
            const target = e.target;
            const playlistRow = target.closest('.playlist-row');
            const trackRow = target.closest('.playlist-track-row');
            
            if (playlistRow) {
                e.preventDefault();
                const playlistId = playlistRow.dataset.id;
                this.showPlaylistContextMenu(e, playlistId);
            } else if (trackRow) {
                e.preventDefault();
                const trackUri = trackRow.dataset.trackUri;
                this.showTrackContextMenu(e, trackUri);
            }
        });

        // Search
        document.getElementById('search-input').addEventListener('keypress', (e) => {
            if (e.key === 'Enter') {
                this.performSearch();
            }
        });

        document.getElementById('search-btn').addEventListener('click', () => {
            this.performSearch();
        });

        // Playlist search
        const playlistSearchInput = document.getElementById('playlist-search');
        if (playlistSearchInput) {
            playlistSearchInput.addEventListener('input', (e) => {
                this.filterPlaylists(e.target.value);
            });
        }

        const clearSearchBtn = document.getElementById('clear-search-btn');
        if (clearSearchBtn) {
            clearSearchBtn.addEventListener('click', () => {
                this.clearPlaylistSearch();
            });
        }

        // Playlist tracks modal close button
        const closeModalBtn = document.getElementById('close-playlist-tracks');
        if (closeModalBtn) {
            closeModalBtn.addEventListener('click', () => {
                this.hidePlaylistTracksModal();
            });
        }

        // Network status monitoring
        window.addEventListener('online', () => {
            this.showSuccess('Internet connection restored');
            this.initializeSpotify(); // Try to reconnect
        });

        window.addEventListener('offline', () => {
            this.showError('Internet connection lost. Some features may not work.');
        });

        // Device management events
        this.bindDeviceEvents();

        // Search tab events
        this.bindSearchTabEvents();

        // Handle browser back/forward navigation
        window.addEventListener('hashchange', () => {
            this.restoreSectionFromURL();
        });

        // OAuth callback handler for native macOS app
        window.oauthCallback = (result) => {
            this.handleOAuthCallback(result);
        };

        // Handle callback URL from native macOS app
        window.handleCallback = (callbackUrl) => {
            console.log('Handling callback URL:', callbackUrl);

            try {
                // Parse the callback URL
                const url = new URL(callbackUrl);
                const searchParams = new URLSearchParams(url.search);

                const code = searchParams.get('code');
                const error = searchParams.get('error');
                const state = searchParams.get('state');

                console.log('Parsed callback parameters:', { code: code ? 'present' : 'missing', error, state });

                if (error) {
                    console.error('OAuth error in callback:', error);
                    this.showError(`Authorization failed: ${error}`);
                    return;
                }

                if (code) {
                    console.log('Authorization code received, processing...');
                    // Call the existing OAuth callback handler with the parsed result
                    this.handleOAuthCallback({ code, state });
                } else {
                    console.error('No authorization code or error in callback URL');
                    this.showError('Invalid authorization response');
                }
            } catch (error) {
                console.error('Failed to parse callback URL:', error);
                this.showError('Failed to process authorization response');
            }
        };

        // Add helper function for console configuration
        window.setSpotifyClientId = (clientId) => {
            if (!clientId || clientId.trim() === '') {
                console.error('❌ Please provide a valid Client ID');
                return false;
            }
            localStorage.setItem('spotify_client_id', clientId.trim());
            this.spotifyApi.setClientId(clientId.trim());
            console.log('✅ Spotify Client ID saved:', clientId.substring(0, 10) + '...');
            console.log('🔄 Please refresh the page to apply changes');
            return true;
        };

        // Add debugging helper
        window.debugSpotifyConnection = () => {
            console.log('=== SPOTIFY CONNECTION DEBUG ===');
            console.log('1. Client ID from config:', window.SPOTIFY_CONFIG?.clientId);
            console.log('2. Client ID from localStorage:', localStorage.getItem('spotify_client_id'));
            console.log('3. Access token present:', !!localStorage.getItem('spotify_access_token'));
            console.log('4. Refresh token present:', !!localStorage.getItem('spotify_refresh_token'));
            console.log('5. Token expiration:', localStorage.getItem('spotify_token_expires'));
            console.log('6. Is connected:', this.isConnected);
            console.log('7. Spotify API available:', !!this.spotifyApi);
            console.log('8. Connect button exists:', !!document.getElementById('connect-btn'));
            console.log('=== END DEBUG ===');
        };
    }

    async handleOAuthCallback(result) {
        console.log('OAuth callback received:', result);

        try {
            if (result.error) {
                console.error('OAuth error:', result.error);
                this.showError(`Authorization failed: ${result.error}`);
                return;
            }

            if (result.code) {
                console.log('Received authorization code, exchanging for tokens');
                this.showLoading('Exchanging authorization code...');

                // Exchange the authorization code for tokens
                const tokenData = await this.spotifyApi.exchangeCodeForToken(result.code);

                // Store the tokens
                localStorage.setItem('spotify_access_token', tokenData.access_token);
                this.spotifyApi.setAccessToken(tokenData.access_token);

                // Store refresh token if available
                if (tokenData.refresh_token) {
                    localStorage.setItem('spotify_refresh_token', tokenData.refresh_token);
                }

                // Calculate and store expiration time
                const expirationTime = Date.now() + (tokenData.expires_in * 1000);
                localStorage.setItem('spotify_token_expires', expirationTime);

                this.isConnected = true;
                this.hideLoading();
                this.updateUI();
                this.showSuccess('Successfully connected to Spotify!');

                // Initialize player after successful connection
                this.player = new SpotifyPlayer();
                // Bind event listeners after player is created
                if (this.player.bindEventListeners) {
                    this.player.bindEventListeners();
                }

                await this.loadUserData();
            }
        } catch (error) {
            console.error('OAuth callback processing failed:', error);
            this.hideLoading();
            this.showError('Failed to complete authorization. Please try again.');
        }
    }

    showError(message) {
        // Create and show error notification
        const notification = document.createElement('div');
        notification.className = 'notification error';
        notification.innerHTML = `
            <i class="fas fa-exclamation-triangle"></i>
            <span>${message}</span>
            <button class="notification-close" onclick="this.parentElement.remove()">
                <i class="fas fa-times"></i>
            </button>
        `;
        document.body.appendChild(notification);

        // Auto-remove after 5 seconds
        setTimeout(() => {
            if (notification.parentElement) {
                notification.remove();
            }
        }, 5000);

        console.error('Error:', message);
    }

    showSuccess(message) {
        // Create and show success notification
        const notification = document.createElement('div');
        notification.className = 'notification success';
        notification.innerHTML = `
            <i class="fas fa-check-circle"></i>
            <span>${message}</span>
            <button class="notification-close" onclick="this.parentElement.remove()">
                <i class="fas fa-times"></i>
            </button>
        `;
        document.body.appendChild(notification);

        // Auto-remove after 3 seconds
        setTimeout(() => {
            if (notification.parentElement) {
                notification.remove();
            }
        }, 3000);

        console.log('Success:', message);
    }

    showLoading(message = 'Loading...') {
        // Remove existing loading notification
        const existing = document.querySelector('.notification.loading');
        if (existing) {
            existing.remove();
        }

        // Create and show loading notification
        const notification = document.createElement('div');
        notification.className = 'notification loading';
        notification.innerHTML = `
            <i class="fas fa-spinner fa-spin"></i>
            <span>${message}</span>
        `;
        document.body.appendChild(notification);
    }

    hideLoading() {
        const loading = document.querySelector('.notification.loading');
        if (loading) {
            loading.remove();
        }
    }

    switchSection(sectionName) {
        console.log('Switching to section:', sectionName);

        // Update navigation
        document.querySelectorAll('.nav-item').forEach(item => {
            item.classList.remove('active');
        });
        document.querySelector(`[data-section="${sectionName}"]`).classList.add('active');

        // Update content
        document.querySelectorAll('.content-section').forEach(section => {
            section.classList.remove('active');
        });
        document.getElementById(`${sectionName}-section`).classList.add('active');

        // Update URL hash without triggering page reload
        const newHash = `#${sectionName}`;
        if (window.location.hash !== newHash) {
            console.log('Updating URL hash to:', newHash);
            window.history.replaceState(null, null, newHash);
        }

        // Handle device monitoring based on current section
        if (sectionName === 'devices' && this.isConnected) {
            // Refresh devices when entering devices section
            this.loadDevices();
        }

        this.currentSection = sectionName;
        this.updateUI();
    }

    restoreSectionFromURL() {
        // Get the current hash from URL (remove the #)
        const hash = window.location.hash.substring(1);

        console.log('Restoring section from URL:', hash, 'Current section:', this.currentSection);

        // If there's a valid section in the hash and it's different from current section, switch to it
        if (hash && ['home', 'search', 'devices', 'library', 'playlists'].includes(hash) && hash !== this.currentSection) {
            console.log('Switching to section from URL:', hash);
            this.switchSection(hash);
        } else if (!hash || !['home', 'search', 'devices', 'library', 'playlists'].includes(hash)) {
            // Default to home if no valid hash or empty hash, but only if we're not already on home
            if (this.currentSection !== 'home') {
                console.log('No valid hash found, defaulting to home');
                this.switchSection('home');
            }
        }
    }

    showSpotifySetup() {
        // Show setup instructions in the home section
        const homeSection = document.getElementById('home-section');
        homeSection.innerHTML = `
            <div class="setup-section">
                <h2>Connect to Spotify</h2>
                <div class="setup-step">
                    <div class="step-number">1</div>
                    <div class="step-content">
                        <h3>Register Your App</h3>
                        <p>Go to the <a href="https://developer.spotify.com/dashboard" target="_blank">Spotify Developer Dashboard</a> and create a new app.</p>
                        <div class="app-details">
                            <strong>App Name:</strong> Spofify<br>
                            <strong>App Description:</strong> A native macOS Spotify client<br>
                            <strong>Redirect URI:</strong> <code>${window.SPOTIFY_CONFIG?.redirectUri || 'spofifywpf://callback'}</code>
                        </div>
                        <button id="copy-redirect-uri" class="copy-button">
                            <i class="fas fa-copy"></i>
                            Copy Redirect URI
                        </button>
                    </div>
                </div>

                <div class="setup-step">
                    <div class="step-number">2</div>
                    <div class="step-content">
                        <h3>Enter Your Client ID</h3>
                        <p>After creating your app, copy the Client ID from the dashboard.</p>
                        <div class="client-id-input">
                            <input type="text" id="client-id-input" placeholder="Enter your Spotify Client ID" class="client-id-field">
                            <button id="save-client-id" class="save-button">
                                <i class="fas fa-save"></i>
                                Save & Connect
                            </button>
                        </div>
                    </div>
                </div>

                <div class="setup-step">
                    <div class="step-number">3</div>
                    <div class="step-content">
                        <h3>Authorize the App</h3>
                        <p>Click the button below to authorize Spofify to access your account.</p>
                        <button id="authorize-btn" class="authorize-button" disabled>
                            <i class="fab fa-spotify"></i>
                            Authorize Spotify
                        </button>
                    </div>
                </div>
            </div>
        `;

        // Bind setup events
        this.bindSetupEvents();
    }

    bindSetupEvents() {
        // Copy redirect URI
        document.getElementById('copy-redirect-uri').addEventListener('click', () => {
            const redirectUri = window.SPOTIFY_CONFIG?.redirectUri || 'spofifywpf://callback';
            navigator.clipboard.writeText(redirectUri).then(() => {
                this.showSuccess('Redirect URI copied to clipboard!');
            }).catch(() => {
                // Fallback for browsers that don't support clipboard API
                const textArea = document.createElement('textarea');
                textArea.value = redirectUri;
                document.body.appendChild(textArea);
                textArea.select();
                document.execCommand('copy');
                document.body.removeChild(textArea);
                this.showSuccess('Redirect URI copied to clipboard!');
            });
        });

        // Save client ID
        document.getElementById('save-client-id').addEventListener('click', () => {
            const clientId = document.getElementById('client-id-input').value.trim();
            if (clientId) {
                localStorage.setItem('spotify_client_id', clientId);
                this.spotifyApi.setClientId(clientId);
                document.getElementById('authorize-btn').disabled = false;
                this.showSuccess('Client ID saved! You can now authorize the app.');
            } else {
                this.showError('Please enter a valid Client ID.');
            }
        });

        // Authorize button
        document.getElementById('authorize-btn').addEventListener('click', () => {
            this.connectToSpotify();
        });
    }

    async initializeSpotify() {
        try {
            // Debug: Check localStorage state
            this.debugLocalStorage();

            // Check network connectivity first
            if (!this.spotifyApi.isOnline()) {
                this.showError('No internet connection. Please check your network and try again.');
                this.showSpotifySetup();
                return;
            }

            // Check if config is properly set up
            const configClientId = window.SPOTIFY_CONFIG?.clientId;
            const storedClientId = localStorage.getItem('spotify_client_id');

            // Use stored client ID if config has placeholder, otherwise use config
            const effectiveClientId = (configClientId && configClientId !== 'your_client_id_here') ? configClientId : storedClientId;

            if (!effectiveClientId) {
                console.warn('Spotify Client ID not configured. Showing setup flow.');
                this.showSpotifySetup();
                return;
            }

            console.log('Using client ID:', effectiveClientId);

            // Check if we have stored credentials
            const storedToken = localStorage.getItem('spotify_access_token');
            const storedExpiration = localStorage.getItem('spotify_token_expires');
            const storedRefreshToken = localStorage.getItem('spotify_refresh_token');

            console.log('Token status check:', {
                hasClientId: !!effectiveClientId,
                hasToken: !!storedToken,
                hasExpiration: !!storedExpiration,
                hasRefreshToken: !!storedRefreshToken,
                currentTime: Date.now(),
                expirationTime: storedExpiration ? parseInt(storedExpiration) : null,
                isExpired: storedExpiration ? Date.now() > parseInt(storedExpiration) : null
            });

            if (effectiveClientId && storedToken) {
                // Set the client ID and token
                this.spotifyApi.setClientId(effectiveClientId);
                this.spotifyApi.setAccessToken(storedToken);

                // Check if token is expired
                const currentTime = Date.now();
                const expirationTime = storedExpiration ? parseInt(storedExpiration) : 0;

                if (currentTime < expirationTime) {
                    // Token is not expired, validate it
                    console.log('Token not expired, validating...');
                    try {
                        const isTokenValid = await this.spotifyApi.validateToken();
                        if (isTokenValid) {
                            console.log('Token validation successful, loading user data...');
                            this.isConnected = true;
                            await this.loadUserData();
                            console.log('User data loaded successfully');

                            // Initialize player after successful token validation
                            console.log('Initializing Web Playback SDK player...');
                            this.player = new SpotifyPlayer();
                            // Bind event listeners after player is created
                            if (this.player.bindEventListeners) {
                                this.player.bindEventListeners();
                            }

                            this.updateUI(); // Update UI to show connected state
                            return;
                        } else {
                            console.log('Token validation failed');
                        }
                    } catch (validationError) {
                        console.error('Token validation threw exception:', validationError);
                        // Continue to refresh logic
                    }
                } else if (!this.spotifyApi.isTokenExpired()) {
                    // Double-check with the API method (should not happen, but safety check)
                    console.log('Token appears expired but API check says valid, validating...');
                    try {
                        const isTokenValid = await this.spotifyApi.validateToken();
                        if (isTokenValid) {
                            console.log('Token validation successful, loading user data...');
                            this.isConnected = true;
                            await this.loadUserData();
                            console.log('User data loaded successfully');

                            // Initialize player after successful token validation
                            console.log('Initializing Web Playback SDK player...');
                            this.player = new SpotifyPlayer();
                            // Bind event listeners after player is created
                            if (this.player.bindEventListeners) {
                                this.player.bindEventListeners();
                            }

                            this.updateUI(); // Update UI to show connected state
                            return;
                        } else {
                            console.log('Token validation failed');
                        }
                    } catch (validationError) {
                        console.error('Token validation threw exception:', validationError);
                        // Continue to refresh logic
                    }
                } else {
                    // Token is expired, try to refresh it
                    console.log('Token expired, attempting refresh...');
                    if (storedRefreshToken) {
                        try {
                            await this.spotifyApi.refreshAccessToken();
                            console.log('Token refresh successful, loading user data...');
                            this.isConnected = true;
                            await this.loadUserData();
                            console.log('User data loaded successfully');

                            // Initialize player after successful token refresh
                            console.log('Initializing Web Playback SDK player...');
                            this.player = new SpotifyPlayer();
                            // Bind event listeners after player is created
                            if (this.player.bindEventListeners) {
                                this.player.bindEventListeners();
                            }

                            this.updateUI(); // Update UI to show connected state
                            return;
                        } catch (refreshError) {
                            console.error('Failed to refresh token on startup:', refreshError);
                        }
                    }
                }

                // If we get here, token validation/refresh failed
                console.log('Token validation/refresh failed, showing setup');
                this.showSpotifySetup();
            } else {
                // No stored credentials, show setup flow
                console.log('No stored credentials found, showing setup');
                this.showSpotifySetup();
            }

            this.updateUI();
        } catch (error) {
            console.error('CRITICAL: Failed to initialize Spotify with unhandled exception:', error);
            console.error('Error details:', {
                message: error.message,
                stack: error.stack,
                name: error.name
            });
            this.showError('Failed to initialize Spotify. Please check your configuration.');
            this.showSpotifySetup();
        }
    }

    debugLocalStorage() {
        console.log('=== LOCALSTORAGE DEBUG ===');
        const keys = ['spotify_client_id', 'spotify_access_token', 'spotify_token_expires', 'spotify_refresh_token'];
        keys.forEach(key => {
            const value = localStorage.getItem(key);
            if (value) {
                if (key.includes('token')) {
                    console.log(`${key}: ${value.substring(0, 20)}... (length: ${value.length})`);
                } else if (key.includes('expires')) {
                    const expTime = parseInt(value);
                    const now = Date.now();
                    const timeLeft = expTime - now;
                    console.log(`${key}: ${new Date(expTime).toISOString()} (${timeLeft > 0 ? Math.floor(timeLeft / 1000 / 60) + ' minutes left' : 'EXPIRED'})`);
                } else {
                    console.log(`${key}: ${value}`);
                }
            } else {
                console.log(`${key}: NOT FOUND`);
            }
        });
        console.log('=== END LOCALSTORAGE DEBUG ===');
    }

    async connectToSpotify() {
        console.log('=== CONNECT TO SPOTIFY STARTED ===');
        console.log('Button clicked - connectToSpotify method called');

        try {
            console.log('Starting Spotify connection...');
            const clientId = localStorage.getItem('spotify_client_id') || window.SPOTIFY_CONFIG?.clientId;
            console.log('Client ID from localStorage:', localStorage.getItem('spotify_client_id') ? 'present' : 'missing');
            console.log('Client ID from config:', window.SPOTIFY_CONFIG?.clientId);
            console.log('Final client ID:', clientId ? 'present' : 'missing');

            if (!clientId) {
                console.error('❌ No client ID available');
                this.showError('Please configure your Spotify Client ID first.');
                return;
            }

            if (clientId === 'your_client_id_here') {
                console.error('❌ Client ID is still placeholder value');
                this.showError('Please configure your actual Spotify Client ID. The current value is just a placeholder.');
                return;
            }

            console.log('✅ Client ID validated, setting in API...');
            this.spotifyApi.setClientId(clientId);

            console.log('🔗 Generating authorization URL...');
            const authUrl = await this.spotifyApi.getAuthorizationUrl();
            console.log('Generated auth URL:', authUrl);

            // For native macOS app, redirect current page to authorization URL
            console.log('🌐 Redirecting to authorization URL for native app');
            window.location.href = authUrl;

            this.showLoading('Waiting for authorization...');
            this.showSuccess('Authorization page opened in your browser. Please complete the authorization there.');
            console.log('=== CONNECT TO SPOTIFY COMPLETED ===');

        } catch (error) {
            console.error('❌ Failed to connect to Spotify:', error);
            console.error('Error details:', {
                message: error.message,
                stack: error.stack,
                name: error.name
            });
            this.hideLoading();
            this.showError('Failed to start authorization process. Please try again.');
        }
    }

    async loadUserData() {
        try {
            console.log('Starting to load user data...');
            this.showLoading('Loading your Spotify data...');

            // Load user profile
            console.log('Loading user profile...');
            const profile = await this.spotifyApi.getCurrentUserProfile();
            document.getElementById('user-display-name').textContent = profile.display_name || 'User';
            console.log('User profile loaded:', profile.display_name);

            // Load playlists
            console.log('Loading playlists...');
            await this.loadPlaylists();
            console.log('Playlists loaded');

            // Load library
            console.log('Loading library...');
            await this.loadLibrary();
            console.log('Library loaded');

            // Load devices
            console.log('Loading devices...');
            await this.loadDevices();
            console.log('Devices loaded');

            // Start device monitoring
            console.log('Starting device monitoring...');
            this.startDeviceMonitoring();

            this.hideLoading();
            this.showSuccess('Data loaded successfully!');
            console.log('User data loading completed successfully');

        } catch (error) {
            this.hideLoading();
            console.error('Failed to load user data:', error);
            console.error('Error details:', {
                message: error.message,
                stack: error.stack,
                name: error.name
            });
            this.showError('Failed to load your Spotify data. Please try reconnecting.');
            // Re-throw to let caller handle it
            throw error;
        }
    }

    async loadPlaylists() {
        try {
            this.showLoading('Loading all playlists...');
            const playlists = await this.spotifyApi.getAllUserPlaylists();
            this.hideLoading();

            // Store playlists data for sorting
            this.allPlaylists = playlists.items || [];
            this.currentSort = this.currentSort || { column: 'name', direction: 'asc' };

            // Clear search when reloading playlists (now that allPlaylists is set)
            this.clearPlaylistSearch();

            this.renderPlaylistsTable();
        } catch (error) {
            console.error('Failed to load playlists:', error);
            const container = document.getElementById('playlists-content');
            container.innerHTML = '<p>Failed to load playlists. Please try again.</p>';
            this.showError('Failed to load playlists');
        }
    }

    renderPlaylistsTable() {
        const container = document.getElementById('playlists-content');
        const playlistsToShow = this.filteredPlaylists || this.allPlaylists;

        if (playlistsToShow && playlistsToShow.length > 0) {
            try {
                // Sort playlists based on current sort settings
                const sortedPlaylists = this.sortPlaylists(playlistsToShow);

                // Create table structure
                container.innerHTML = `
                    <div class="playlists-table-container">
                        <table class="playlists-table">
                            <thead>
                                <tr>
                                    <th class="checkbox-column">
                                        <input type="checkbox" id="select-all-playlists" class="playlist-checkbox">
                                    </th>
                                    <th class="image-column"></th>
                                    <th class="name-column sortable" data-sort="name">
                                        <span class="column-header">Name</span>
                                        <span class="sort-indicator"></span>
                                    </th>
                                    <th class="description-column">Description</th>
                                    <th class="tracks-column sortable" data-sort="tracks">
                                        <span class="column-header">Tracks</span>
                                        <span class="sort-indicator"></span>
                                    </th>
                                    <th class="owner-column sortable" data-sort="owner">
                                        <span class="column-header">Owner</span>
                                        <span class="sort-indicator"></span>
                                    </th>
                                    <th class="actions-column">Actions</th>
                                </tr>
                            </thead>
                            <tbody id="playlists-table-body">
                                ${sortedPlaylists.map(playlist => `
                                    <tr class="playlist-row" data-id="${playlist.id || ''}">
                                        <td class="checkbox-column">
                                            <input type="checkbox" class="playlist-checkbox" data-id="${playlist.id || ''}">
                                        </td>
                                        <td class="image-column">
                                            <img src="${playlist.images?.[0]?.url || ''}" alt="${playlist.name || ''}" class="playlist-table-image">
                                        </td>
                                        <td class="name-column">
                                            <div class="playlist-table-name">${playlist.name || 'Unknown'}</div>
                                        </td>
                                        <td class="description-column">
                                            <div class="playlist-table-description">${playlist.description || 'No description'}</div>
                                        </td>
                                        <td class="tracks-column">
                                            <span class="track-count">${playlist.tracks?.total || 0}</span>
                                        </td>
                                        <td class="owner-column">
                                            <span class="playlist-owner">${playlist.owner?.display_name || 'Unknown'}</span>
                                        </td>
                                        <td class="actions-column">
                                            <button class="playlist-action-btn" onclick="window.spotifyApp.playPlaylist('${playlist.id || ''}')" title="Play">
                                                <i class="fas fa-play"></i>
                                            </button>
                                            <button class="playlist-action-btn" onclick="window.spotifyApp.loadPlaylistTracks('${playlist.id || ''}')" title="View Tracks">
                                                <i class="fas fa-list"></i>
                                            </button>
                                        </td>
                                    </tr>
                                `).join('')}
                            </tbody>
                        </table>
                    </div>
                `;

                this.addPlaylistTableEventListeners();
                this.updateSortIndicators();
            } catch (error) {
                console.error('Error rendering playlists table:', error);
                container.innerHTML = '<p>Failed to render playlists table. Please try again.</p>';
            }
        } else {
            container.innerHTML = '<p>No playlists found.</p>';
        }
    }

    sortPlaylists(playlists) {
        if (!playlists || !Array.isArray(playlists)) return playlists;

        return [...playlists].sort((a, b) => {
            let aValue, bValue;

            try {
                switch (this.currentSort.column) {
                    case 'name':
                        aValue = (a.name || '').toLowerCase();
                        bValue = (b.name || '').toLowerCase();
                        break;
                    case 'tracks':
                        aValue = a.tracks?.total || 0;
                        bValue = b.tracks?.total || 0;
                        break;
                    case 'owner':
                        aValue = (a.owner?.display_name || '').toLowerCase();
                        bValue = (b.owner?.display_name || '').toLowerCase();
                        break;
                    default:
                        aValue = (a.name || '').toLowerCase();
                        bValue = (b.name || '').toLowerCase();
                }

                if (this.currentSort.direction === 'asc') {
                    return aValue < bValue ? -1 : aValue > bValue ? 1 : 0;
                } else {
                    return aValue > bValue ? -1 : aValue < bValue ? 1 : 0;
                }
            } catch (error) {
                console.error('Error sorting playlists:', error);
                return 0; // No change in order
            }
        });
    }

    handleColumnSort(column) {
        if (this.currentSort.column === column) {
            // Toggle direction if same column
            this.currentSort.direction = this.currentSort.direction === 'asc' ? 'desc' : 'asc';
        } else {
            // New column, default to ascending
            this.currentSort.column = column;
            this.currentSort.direction = 'asc';
        }

        // Re-render the table with new sort
        this.renderPlaylistsTable();
    }

    updateSortIndicators() {
        // Update all sort indicators
        document.querySelectorAll('#playlists-content .sortable').forEach(header => {
            const column = header.dataset.sort;

            if (column === this.currentSort.column) {
                // Active sort column
                header.classList.add(this.currentSort.direction === 'asc' ? 'sort-asc' : 'sort-desc');
                header.classList.remove(this.currentSort.direction === 'asc' ? 'sort-desc' : 'sort-asc');
            } else {
                // Inactive sort column
                header.classList.remove('sort-asc', 'sort-desc');
            }
        });
    }    updatePlaylistCount(total) {
        const header = document.querySelector('.playlists-header h2');
        if (header) {
            header.textContent = `Your Playlists (${total})`;
        }
    }

    addPlaylistTableEventListeners() {
        // Hide context menu when clicking elsewhere
        document.addEventListener('click', (e) => {
            if (!e.target.closest('.context-menu')) {
                this.hideContextMenu();
            }
        });

        // Select all checkbox
        const selectAllCheckbox = document.getElementById('select-all-playlists');
        if (selectAllCheckbox) {
            selectAllCheckbox.addEventListener('change', (e) => {
                const isChecked = e.target.checked;
                document.querySelectorAll('.playlist-checkbox').forEach(checkbox => {
                    if (checkbox !== selectAllCheckbox) {
                        checkbox.checked = isChecked;
                        const row = checkbox.closest('.playlist-row');
                        row.classList.toggle('selected', isChecked);
                    }
                });
                this.updateToolbarVisibility();
            });
        }

        // Individual checkbox change handlers
        document.querySelectorAll('.playlist-checkbox').forEach(checkbox => {
            if (checkbox.id !== 'select-all-playlists') {
                checkbox.addEventListener('change', (e) => {
                    const row = e.target.closest('.playlist-row');
                    row.classList.toggle('selected', e.target.checked);
                    this.updateSelectAllState();
                    this.updateToolbarVisibility();
                });
            }
        });

        // Row click handlers (avoid when clicking checkbox or buttons)
        document.querySelectorAll('.playlist-row').forEach(row => {
            row.addEventListener('click', (e) => {
                if (e.target.type !== 'checkbox' && !e.target.classList.contains('playlist-action-btn') && !e.target.closest('.playlist-action-btn')) {
                    const checkbox = row.querySelector('.playlist-checkbox');
                    checkbox.checked = !checkbox.checked;
                    row.classList.toggle('selected', checkbox.checked);
                    this.updateSelectAllState();
                    this.updateToolbarVisibility();
                }
            });
        });

        // Sorting event listeners for column headers
        document.querySelectorAll('#playlists-content .sortable').forEach(header => {
            header.addEventListener('click', (e) => {
                const column = e.currentTarget.dataset.sort;
                if (column) {
                    this.handleColumnSort(column);
                }
            });
            // Make headers look clickable
            header.style.cursor = 'pointer';
            header.style.userSelect = 'none';
        });
    }

    // Context Menu Methods
    async showPlaylistContextMenu(event, playlistId) {
        // Hide any existing context menu
        this.hideContextMenu();

        // Get available devices
        const devices = await this.getAvailableDevices();

        // Create context menu
        const menu = document.createElement('div');
        menu.className = 'context-menu';
        menu.style.left = `${event.pageX}px`;
        menu.style.top = `${event.pageY}px`;

        // Add menu items
        menu.innerHTML = `
            <div class="context-menu-item" onclick="window.spotifyApp.playPlaylist('${playlistId}')">
                <i class="fas fa-play"></i>
                Play
            </div>
            <div class="context-menu-item has-submenu">
                <i class="fas fa-external-link-alt"></i>
                Play To
                <div class="context-menu-submenu">
                    ${devices.map(device => `
                        <div class="context-menu-item" onclick="window.spotifyApp.playPlaylistOnDevice('${playlistId}', '${device.id}')">
                            <i class="fas fa-${this.getDeviceIcon(device.type)}"></i>
                            ${device.name}
                            ${device.is_active ? '<span style="color: var(--spotify-green); margin-left: auto;">●</span>' : ''}
                        </div>
                    `).join('')}
                    ${devices.length === 0 ? `
                        <div class="context-menu-item" style="color: var(--spotify-light-gray); cursor: default;">
                            <i class="fas fa-exclamation-triangle"></i>
                            No devices available
                        </div>
                    ` : ''}
                </div>
            </div>
            <div class="context-menu-separator"></div>
            <div class="context-menu-item" onclick="window.spotifyApp.loadPlaylistTracks('${playlistId}')">
                <i class="fas fa-list"></i>
                View Tracks
            </div>
        `;

        document.body.appendChild(menu);
        menu.style.display = 'block';

        // Prevent menu from going off-screen
        const rect = menu.getBoundingClientRect();
        if (rect.right > window.innerWidth) {
            menu.style.left = `${window.innerWidth - rect.width - 10}px`;
        }
        if (rect.bottom > window.innerHeight) {
            menu.style.top = `${window.innerHeight - rect.height - 10}px`;
        }
    }

    async showTrackContextMenu(event, trackUri) {
        // Hide any existing context menu
        this.hideContextMenu();

        // Get available devices
        const devices = await this.getAvailableDevices();

        // Create context menu
        const menu = document.createElement('div');
        menu.className = 'context-menu';
        menu.style.left = `${event.pageX}px`;
        menu.style.top = `${event.pageY}px`;

        // Add menu items
        menu.innerHTML = `
            <div class="context-menu-item" onclick="window.spotifyApp.playTrack('${trackUri}')">
                <i class="fas fa-play"></i>
                Play
            </div>
            <div class="context-menu-item has-submenu">
                <i class="fas fa-external-link-alt"></i>
                Play To
                <div class="context-menu-submenu">
                    ${devices.map(device => `
                        <div class="context-menu-item" onclick="window.spotifyApp.playTrackOnDevice('${trackUri}', '${device.id}')">
                            <i class="fas fa-${this.getDeviceIcon(device.type)}"></i>
                            ${device.name}
                            ${device.is_active ? '<span style="color: var(--spotify-green); margin-left: auto;">●</span>' : ''}
                        </div>
                    `).join('')}
                    ${devices.length === 0 ? `
                        <div class="context-menu-item" style="color: var(--spotify-light-gray); cursor: default;">
                            <i class="fas fa-exclamation-triangle"></i>
                            No devices available
                        </div>
                    ` : ''}
                </div>
            </div>
        `;

        document.body.appendChild(menu);
        menu.style.display = 'block';

        // Prevent menu from going off-screen
        const rect = menu.getBoundingClientRect();
        if (rect.right > window.innerWidth) {
            menu.style.left = `${window.innerWidth - rect.width - 10}px`;
        }
        if (rect.bottom > window.innerHeight) {
            menu.style.top = `${window.innerHeight - rect.height - 10}px`;
        }
    }

    hideContextMenu() {
        const existingMenu = document.querySelector('.context-menu');
        if (existingMenu) {
            existingMenu.remove();
        }
    }

    async getAvailableDevices() {
        try {
            const devicesResponse = await this.spotifyApi.getDevices();
            return devicesResponse.devices || [];
        } catch (error) {
            console.error('Failed to get devices:', error);
            return [];
        }
    }

    async playPlaylistOnDevice(playlistId, deviceId) {
        try {
            this.showLoading('Transferring playback and starting playlist...');

            // Transfer playback to the selected device
            await this.spotifyApi.transferPlayback(deviceId, true);

            // Wait a moment for the transfer to complete
            await new Promise(resolve => setTimeout(resolve, 1000));

            // Start the playlist on the new device
            const playlistUri = `spotify:playlist:${playlistId}`;
            await this.spotifyApi.startPlayback(playlistUri);

            this.hideLoading();
            this.showSuccess('Playlist started on selected device!');

        } catch (error) {
            this.hideLoading();
            console.error('Failed to play playlist on device:', error);
            this.showError('Failed to start playlist on selected device');
        }
    }

    async playTrackOnDevice(trackUri, deviceId) {
        try {
            this.showLoading('Transferring playback and starting track...');

            // Transfer playback to the selected device
            await this.spotifyApi.transferPlayback(deviceId, true);

            // Wait a moment for the transfer to complete
            await new Promise(resolve => setTimeout(resolve, 1000));

            // Start the track on the new device
            await this.spotifyApi.startPlayback(null, [trackUri]);

            this.hideLoading();
            this.showSuccess('Track started on selected device!');

        } catch (error) {
            this.hideLoading();
            console.error('Failed to play track on device:', error);
            this.showError('Failed to start track on selected device');
        }
    }

    updateSelectAllState() {
        const totalCheckboxes = document.querySelectorAll('.playlist-checkbox:not(#select-all-playlists)').length;
        const checkedCheckboxes = document.querySelectorAll('.playlist-checkbox:not(#select-all-playlists):checked').length;
        const selectAllCheckbox = document.getElementById('select-all-playlists');

        if (selectAllCheckbox) {
            selectAllCheckbox.checked = totalCheckboxes > 0 && checkedCheckboxes === totalCheckboxes;
            selectAllCheckbox.indeterminate = checkedCheckboxes > 0 && checkedCheckboxes < totalCheckboxes;
        }
    }

    updateToolbarVisibility() {
        const selectedCount = document.querySelectorAll('.playlist-checkbox:checked').length;
        const selectAllBtn = document.getElementById('select-all-btn');
        const deselectAllBtn = document.getElementById('deselect-all-btn');
        const deleteSelectedBtn = document.getElementById('delete-selected-btn');

        if (selectedCount > 0) {
            selectAllBtn.style.display = 'none';
            deselectAllBtn.style.display = 'flex';
            deleteSelectedBtn.style.display = 'flex';
        } else {
            selectAllBtn.style.display = 'flex';
            deselectAllBtn.style.display = 'none';
            deleteSelectedBtn.style.display = 'none';
        }
    }

    selectAllPlaylists() {
        document.querySelectorAll('.playlist-checkbox:not(#select-all-playlists)').forEach(checkbox => {
            checkbox.checked = true;
            const row = checkbox.closest('.playlist-row');
            row.classList.add('selected');
        });
        this.updateSelectAllState();
        this.updateToolbarVisibility();
    }

    deselectAllPlaylists() {
        document.querySelectorAll('.playlist-checkbox:not(#select-all-playlists)').forEach(checkbox => {
            checkbox.checked = false;
            const row = checkbox.closest('.playlist-row');
            row.classList.remove('selected');
        });
        this.updateSelectAllState();
        this.updateToolbarVisibility();
    }

    async deleteSelectedPlaylists() {
        const selectedCheckboxes = document.querySelectorAll('.playlist-checkbox:checked:not(#select-all-playlists)');
        const selectedIds = Array.from(selectedCheckboxes).map(cb => cb.dataset.id);
        const selectedNames = Array.from(selectedCheckboxes).map(cb => {
            const row = cb.closest('.playlist-row');
            return row.querySelector('.playlist-table-name').textContent;
        });

        if (selectedIds.length === 0) {
            this.showError('No playlists selected');
            return;
        }

        const confirmed = await this.showConfirmationDialog(
            `Delete ${selectedIds.length} playlist${selectedIds.length > 1 ? 's' : ''}?`,
            `This will permanently delete the following playlists:\n\n${selectedNames.join('\n')}\n\nThis action cannot be undone.`,
            'Delete',
            'Cancel'
        );

        if (!confirmed) return;

        try {
            this.showLoading('Deleting playlists...');

            // Use batched processing for better performance and rate limiting
            const results = await this.processBatchOperations(
                selectedIds,
                async (playlistId) => {
                    const result = await this.spotifyApi.deletePlaylist(playlistId);
                    console.log(`Deleted playlist: ${playlistId}`);
                    return result;
                },
                'Deleting playlists',
                8 // Max 8 concurrent threads
            );

            const successCount = results.filter(r => r.success).length;
            const failureCount = results.filter(r => !r.success).length;

            this.hideLoading();

            if (failureCount === 0) {
                this.showSuccess(`Successfully deleted ${successCount} playlist${successCount > 1 ? 's' : ''}`);
            } else if (successCount === 0) {
                this.showError(`Failed to delete any playlists. Please try again.`);
            } else {
                this.showSuccess(`Successfully deleted ${successCount} playlist${successCount > 1 ? 's' : ''}, but ${failureCount} failed.`);
            }

            // Refresh playlists
            await this.loadPlaylists();

        } catch (error) {
            console.error('Failed to delete playlists:', error);
            this.hideLoading();
            this.showError('Failed to delete playlists. Please try again.');
        }
    }

    /**
     * Process operations in batches with controlled concurrency and rate limit handling
     * @param {Array} items - Array of items to process
     * @param {Function} operation - Async function to execute for each item
     * @param {string} operationName - Name of the operation for progress updates
     * @param {number} maxConcurrency - Maximum number of concurrent operations (default: 8)
     * @param {number} batchDelay - Delay between batches in ms (default: 100)
     * @returns {Array} Array of results with success/failure status
     */
    async processBatchOperations(items, operation, operationName = 'Processing', maxConcurrency = 8, batchDelay = 100) {
        const results = [];
        const totalItems = items.length;

        console.log(`${operationName}: Processing ${totalItems} items with max ${maxConcurrency} concurrent threads`);

        // Rate limit tracking
        let rateLimitHits = 0;
        let lastRateLimitTime = 0;
        let currentBatchDelay = batchDelay;
        let consecutiveRateLimits = 0;

        // Process items in batches
        for (let i = 0; i < totalItems; i += maxConcurrency) {
            const batch = items.slice(i, i + maxConcurrency);
            const batchNumber = Math.floor(i / maxConcurrency) + 1;
            const totalBatches = Math.ceil(totalItems / maxConcurrency);

            console.log(`${operationName}: Processing batch ${batchNumber}/${totalBatches} (${batch.length} items)`);

            // Update loading message with progress
            const processedCount = Math.min(i + maxConcurrency, totalItems);
            this.updateLoadingMessage(`${operationName}... (${processedCount}/${totalItems})`);

            // Process current batch concurrently
            const batchPromises = batch.map(async (item, index) => {
                try {
                    const result = await operation(item);
                    results.push({ item, result, success: true, index: i + index });

                    // Reset consecutive rate limit counter on success
                    consecutiveRateLimits = 0;
                    return { success: true, index: i + index };
                } catch (error) {
                    console.error(`${operationName} failed for item ${i + index}:`, error);

                    // Handle rate limit errors specifically
                    if (error.isRateLimit || error.status === 429) {
                        rateLimitHits++;
                        lastRateLimitTime = Date.now();
                        consecutiveRateLimits++;

                        console.log(`Rate limit hit #${rateLimitHits}, consecutive: ${consecutiveRateLimits}`);

                        // If we hit multiple consecutive rate limits, slow down significantly
                        if (consecutiveRateLimits >= 3) {
                            currentBatchDelay = Math.min(currentBatchDelay * 2, 5000); // Double delay, max 5 seconds
                            maxConcurrency = Math.max(maxConcurrency - 1, 1); // Reduce concurrency
                            console.log(`Multiple rate limits detected. Reducing concurrency to ${maxConcurrency}, increasing delay to ${currentBatchDelay}ms`);
                        }

                        if (window.app) {
                            window.app.showError(`Rate limited during ${operationName.toLowerCase()}. Slowing down to avoid further limits...`);
                        }
                    }

                    results.push({ item, error, success: false, index: i + index });
                    return { success: false, index: i + index };
                }
            });

            // Wait for current batch to complete
            await Promise.allSettled(batchPromises);

            // Add delay between batches to avoid overwhelming the API
            if (i + maxConcurrency < totalItems) {
                // If we've had recent rate limits, use the increased delay
                const actualDelay = consecutiveRateLimits > 0 ? currentBatchDelay * 2 : currentBatchDelay;
                console.log(`Waiting ${actualDelay}ms before next batch (rate limit protection)`);
                await new Promise(resolve => setTimeout(resolve, actualDelay));
            }
        }

        const successCount = results.filter(r => r.success).length;
        const failureCount = results.filter(r => !r.success).length;

        console.log(`${operationName} completed: ${successCount} successful, ${failureCount} failed`);
        if (rateLimitHits > 0) {
            console.log(`Rate limit statistics: ${rateLimitHits} hits, final concurrency: ${maxConcurrency}, final delay: ${currentBatchDelay}ms`);
        }

        return results;
    }

    async unfollowSelectedPlaylists() {
        const selectedCheckboxes = document.querySelectorAll('.playlist-checkbox:checked:not(#select-all-playlists)');
        const selectedIds = Array.from(selectedCheckboxes).map(cb => cb.dataset.id);
        const selectedNames = Array.from(selectedCheckboxes).map(cb => {
            const row = cb.closest('.playlist-row');
            return row.querySelector('.playlist-table-name').textContent;
        });

        if (selectedIds.length === 0) {
            this.showError('No playlists selected');
            return;
        }

        const confirmed = await this.showConfirmationDialog(
            `Unfollow ${selectedIds.length} playlist${selectedIds.length > 1 ? 's' : ''}?`,
            `This will unfollow the following playlists:\n\n${selectedNames.join('\n')}\n\nYou can follow them again later.`,
            'Unfollow',
            'Cancel'
        );

        if (!confirmed) return;

        try {
            this.showLoading('Unfollowing playlists...');

            // Use batched processing for better performance
            const results = await this.processBatchOperations(
                selectedIds,
                async (playlistId) => {
                    const result = await this.spotifyApi.unfollowPlaylist(playlistId);
                    console.log(`Unfollowed playlist: ${playlistId}`);
                    return result;
                },
                'Unfollowing playlists',
                8 // Max 8 concurrent threads
            );

            const successCount = results.filter(r => r.success).length;
            const failureCount = results.filter(r => !r.success).length;

            this.hideLoading();

            if (failureCount === 0) {
                this.showSuccess(`Successfully unfollowed ${successCount} playlist${successCount > 1 ? 's' : ''}`);
            } else if (successCount === 0) {
                this.showError(`Failed to unfollow any playlists. Please try again.`);
            } else {
                this.showSuccess(`Successfully unfollowed ${successCount} playlist${successCount > 1 ? 's' : ''}, but ${failureCount} failed.`);
            }

            // Refresh playlists
            await this.loadPlaylists();

        } catch (error) {
            console.error('Failed to unfollow playlists:', error);
            this.hideLoading();
            this.showError('Failed to unfollow playlists. Please try again.');
        }
    }

    // Combined method that intelligently handles owned vs followed playlists
    async removeSelectedPlaylists() {
        const selectedCheckboxes = document.querySelectorAll('.playlist-checkbox:checked:not(#select-all-playlists)');
        const selectedItems = Array.from(selectedCheckboxes).map(cb => ({
            id: cb.dataset.id,
            row: cb.closest('.playlist-row'),
            name: cb.closest('.playlist-row').querySelector('.playlist-table-name').textContent,
            owner: cb.closest('.playlist-row').querySelector('.playlist-owner').textContent
        }));

        if (selectedItems.length === 0) {
            this.showError('No playlists selected');
            return;
        }

        // Check if current user owns any of the selected playlists
        const ownedPlaylists = selectedItems.filter(item => {
            // Simple check - if the owner name matches current user, consider it owned
            const currentUserDisplayName = document.querySelector('.user-info .user-name')?.textContent || '';
            return item.owner.toLowerCase() === currentUserDisplayName.toLowerCase();
        });

        const followedPlaylists = selectedItems.filter(item => !ownedPlaylists.includes(item));

        let actionType = '';
        let message = '';

        if (ownedPlaylists.length > 0 && followedPlaylists.length > 0) {
            actionType = 'Remove';
            message = `You are about to:\n\nDELETE ${ownedPlaylists.length} playlist${ownedPlaylists.length > 1 ? 's' : ''} you own:\n${ownedPlaylists.map(p => p.name).join('\n')}\n\nUNFOLLOW ${followedPlaylists.length} playlist${followedPlaylists.length > 1 ? 's' : ''}:\n${followedPlaylists.map(p => p.name).join('\n')}\n\nOwned playlists will be permanently deleted. Followed playlists can be followed again later.`;
        } else if (ownedPlaylists.length > 0) {
            actionType = 'Delete';
            message = `This will permanently delete the following playlists you own:\n\n${ownedPlaylists.map(p => p.name).join('\n')}\n\nThis action cannot be undone.`;
        } else {
            actionType = 'Unfollow';
            message = `This will unfollow the following playlists:\n\n${followedPlaylists.map(p => p.name).join('\n')}\n\nYou can follow them again later.`;
        }

        const confirmed = await this.showConfirmationDialog(
            `${actionType} ${selectedItems.length} playlist${selectedItems.length > 1 ? 's' : ''}?`,
            message,
            actionType,
            'Cancel'
        );

        if (!confirmed) return;

        try {
            this.showLoading(`${actionType.toLowerCase()}ing playlists...`);

            // Process owned playlists (delete) and followed playlists (unfollow) separately
            const allResults = [];

            if (ownedPlaylists.length > 0) {
                const deleteResults = await this.processBatchOperations(
                    ownedPlaylists.map(p => p.id),
                    async (playlistId) => {
                        const result = await this.spotifyApi.deletePlaylist(playlistId);
                        console.log(`Deleted owned playlist: ${playlistId}`);
                        return result;
                    },
                    'Deleting owned playlists',
                    8
                );
                allResults.push(...deleteResults);
            }

            if (followedPlaylists.length > 0) {
                const unfollowResults = await this.processBatchOperations(
                    followedPlaylists.map(p => p.id),
                    async (playlistId) => {
                        const result = await this.spotifyApi.unfollowPlaylist(playlistId);
                        console.log(`Unfollowed playlist: ${playlistId}`);
                        return result;
                    },
                    'Unfollowing playlists',
                    8
                );
                allResults.push(...unfollowResults);
            }

            const successCount = allResults.filter(r => r.success).length;
            const failureCount = allResults.filter(r => !r.success).length;

            this.hideLoading();

            if (failureCount === 0) {
                this.showSuccess(`Successfully ${actionType.toLowerCase()}d ${successCount} playlist${successCount > 1 ? 's' : ''}`);
            } else if (successCount === 0) {
                this.showError(`Failed to ${actionType.toLowerCase()} any playlists. Please try again.`);
            } else {
                this.showSuccess(`Successfully ${actionType.toLowerCase()}d ${successCount} playlist${successCount > 1 ? 's' : ''}, but ${failureCount} failed.`);
            }

            // Refresh playlists
            await this.loadPlaylists();

        } catch (error) {
            console.error(`Failed to ${actionType.toLowerCase()} playlists:`, error);
            this.hideLoading();
            this.showError(`Failed to ${actionType.toLowerCase()} playlists. Please try again.`);
        }
    }

    updateLoadingMessage(message) {
        const loadingElement = document.querySelector('.loading-message');
        if (loadingElement) {
            loadingElement.textContent = message;
        }
    }

    // Playlist search and filtering methods
    filterPlaylists(searchTerm) {
        const clearBtn = document.getElementById('clear-search-btn');

        if (searchTerm.trim() === '') {
            // Show all playlists when search is empty
            this.filteredPlaylists = null;
            clearBtn.style.display = 'none';
        } else {
            // Filter playlists based on search term
            if (this.allPlaylists && Array.isArray(this.allPlaylists)) {
                this.filteredPlaylists = this.allPlaylists.filter(playlist => {
                    const name = playlist.name.toLowerCase();
                    const description = (playlist.description || '').toLowerCase();
                    const owner = playlist.owner.display_name.toLowerCase();

                    return name.includes(searchTerm.toLowerCase()) ||
                           description.includes(searchTerm.toLowerCase()) ||
                           owner.includes(searchTerm.toLowerCase());
                });
            } else {
                this.filteredPlaylists = [];
            }
            clearBtn.style.display = 'flex';
        }

        // Re-render the table with filtered results
        this.renderPlaylistsTable();
        this.updateFilteredPlaylistCount(this.filteredPlaylists ? this.filteredPlaylists.length : (this.allPlaylists ? this.allPlaylists.length : 0));
    }

    clearPlaylistSearch() {
        const searchInput = document.getElementById('playlist-search');
        const clearBtn = document.getElementById('clear-search-btn');

        searchInput.value = '';
        clearBtn.style.display = 'none';
        this.filterPlaylists('');
    }

    updateFilteredPlaylistCount(visibleCount) {
        const header = document.querySelector('.playlists-header h2');
        const totalRows = document.querySelectorAll('.playlist-row').length;

        if (visibleCount === totalRows) {
            // Show total count when all are visible
            header.textContent = `Your Playlists (${totalRows})`;
        } else {
            // Show filtered count when searching
            header.textContent = `Your Playlists (${visibleCount} of ${totalRows})`;
        }
    }

    showConfirmationDialog(title, message, confirmText, cancelText) {
        return new Promise((resolve) => {
            const dialog = document.getElementById('confirmation-dialog');
            const titleEl = document.getElementById('confirmation-title');
            const messageEl = document.getElementById('confirmation-message');
            const confirmBtn = document.getElementById('confirmation-confirm');
            const cancelBtn = document.getElementById('confirmation-cancel');

            titleEl.textContent = title;
            messageEl.textContent = message;
            confirmBtn.textContent = confirmText;
            cancelBtn.textContent = cancelText;

            dialog.style.display = 'flex';

            const handleConfirm = () => {
                dialog.style.display = 'none';
                cleanup();
                resolve(true);
            };

            const handleCancel = () => {
                dialog.style.display = 'none';
                cleanup();
                resolve(false);
            };

            const handleEscape = (e) => {
                if (e.key === 'Escape') {
                    handleCancel();
                }
            };

            const cleanup = () => {
                confirmBtn.removeEventListener('click', handleConfirm);
                cancelBtn.removeEventListener('click', handleCancel);
                document.removeEventListener('keydown', handleEscape);
            };

            confirmBtn.addEventListener('click', handleConfirm);
            cancelBtn.addEventListener('click', handleCancel);
            document.addEventListener('keydown', handleEscape);
        });
    }

    async loadLibrary() {
        try {
            const tracks = await this.spotifyApi.getSavedTracks();
            const container = document.getElementById('library-content');

            if (tracks.items && tracks.items.length > 0) {
                container.innerHTML = tracks.items.map(item => {
                    const track = item.track;
                    return `
                        <div class="track-card" data-uri="${track.uri}">
                            <img src="${track.album.images[0]?.url || ''}" alt="${track.album.name}">
                            <h3>${track.name}</h3>
                            <p>${track.artists.map(artist => artist.name).join(', ')}</p>
                        </div>
                    `;
                }).join('');

                // Add click handlers for tracks
                container.querySelectorAll('.track-card').forEach(card => {
                    card.addEventListener('click', () => {
                        const trackUri = card.dataset.uri;
                        this.player.playTrack(trackUri);
                    });
                });
            } else {
                container.innerHTML = '<p>No saved tracks found.</p>';
            }
        } catch (error) {
            console.error('Failed to load library:', error);
            const container = document.getElementById('library-content');
            container.innerHTML = '<p>Failed to load library. Please try again.</p>';
            this.showError('Failed to load your saved tracks');
        }
    }

    async loadPlaylistTracks(playlistId) {
        try {
            // Show modal immediately with loading state
            this.showPlaylistTracksModalLoading();

            // Add minimum loading time to ensure loading state is visible
            const loadingStartTime = Date.now();
            const minLoadingTime = 800; // 800ms minimum

            const [tracks] = await Promise.all([
                this.spotifyApi.getAllPlaylistTracks(playlistId),
                new Promise(resolve => setTimeout(resolve, minLoadingTime))
            ]);

            // Ensure minimum loading time has passed
            const elapsedTime = Date.now() - loadingStartTime;
            if (elapsedTime < minLoadingTime) {
                await new Promise(resolve => setTimeout(resolve, minLoadingTime - elapsedTime));
            }

            // Get playlist name for the modal title
            const playlistName = await this.getPlaylistName(playlistId);

            // Display tracks in modal
            this.showPlaylistTracksModal(tracks, playlistName);

        } catch (error) {
            console.error('Failed to load playlist tracks:', error);
            this.hidePlaylistTracksModal();
            this.showError('Failed to load playlist tracks');
        }
    }

    showPlaylistTracksModalLoading() {
        const modal = document.getElementById('playlist-tracks-modal');
        const title = document.getElementById('playlist-tracks-title');
        const tracksList = document.getElementById('playlist-tracks-list');

        // Set modal title
        title.textContent = 'Loading Playlist';

        // Show loading state in the tracks list area
        tracksList.innerHTML = `
            <tr class="playlist-loading-state">
                <td colspan="5" style="text-align: center; padding: 60px 24px;">
                    <div class="loading-spinner">
                        <i class="fas fa-spinner fa-spin"></i>
                    </div>
                    <div class="loading-text">Loading playlist tracks...</div>
                </td>
            </tr>
        `;

        // Show modal
        modal.style.display = 'flex';
    }

    async playPlaylist(playlistId) {
        try {
            this.showLoading('Starting playlist playback...');

            // Check if there's an active device
            const playbackState = await this.spotifyApi.getCurrentPlaybackState();

            if (!playbackState || !playbackState.device) {
                // No active device found, try to activate Web Playback SDK player
                console.log('Player instance check:', {
                    playerExists: !!this.player,
                    playerPlayerExists: !!(this.player && this.player.player),
                    playerDeviceId: this.player ? this.player.deviceId : 'N/A'
                });

                if (this.player && this.player.player) {
                    console.log('Attempting to activate Web Playback SDK player...');

                    try {
                        // Check if player is already connected
                        const connected = await this.player.player.connected;
                        console.log('Player connected status:', connected);

                        if (!connected) {
                            console.log('Connecting player...');
                            await this.player.player.connect();
                            console.log('Player connect() called, waiting for ready event...');
                        }

                        // Wait for player to be ready (up to 5 seconds)
                        let attempts = 0;
                        while (!this.player.deviceId && attempts < 10) {
                            console.log(`Waiting for player ready... attempt ${attempts + 1}/10, deviceId: ${this.player.deviceId}`);
                            await new Promise(resolve => setTimeout(resolve, 500));
                            attempts++;
                        }

                        if (this.player.deviceId) {
                            console.log('Player ready with deviceId:', this.player.deviceId);
                            console.log('Transferring playback to device:', this.player.deviceId);
                            await this.spotifyApi.transferPlayback(this.player.deviceId, true);
                            console.log('Playback transfer successful');
                        } else {
                            console.error('Player failed to become ready - no deviceId after 5 seconds');
                            console.error('Player state:', {
                                player: !!this.player,
                                playerPlayer: !!(this.player && this.player.player),
                                connected: this.player && this.player.player ? await this.player.player.connected : 'N/A'
                            });
                            throw new Error('Web player failed to initialize');
                        }
                    } catch (playerError) {
                        console.error('Failed to activate Web player:', playerError);
                        this.hideLoading();
                        this.showError('Failed to activate Web Player. Please refresh the page and try again.');
                        return;
                    }
                } else {
                    console.error('No player instance available');
                    console.error('Player debug info:', {
                        player: this.player,
                        hasPlayer: !!this.player,
                        hasPlayerPlayer: !!(this.player && this.player.player),
                        spotifySdkLoaded: !!window.Spotify
                    });
                    this.hideLoading();
                    this.showError('Web Player not available. Please refresh the page.');
                    return;
                }
            }

            // Create the Spotify URI for the playlist
            const playlistUri = `spotify:playlist:${playlistId}`;

            // Start playback with the playlist context
            await this.spotifyApi.startPlayback(playlistUri);

            this.hideLoading();
            this.showSuccess('Playlist playback started!');

        } catch (error) {
            console.error('Failed to play playlist:', error);
            this.hideLoading();

            // Provide more specific error messages
            if (error.message && error.message.includes('No active device found')) {
                this.showError('No active Spotify device found. Please start Spotify on another device or ensure the Web Player is connected.');
            } else {
                this.showError('Failed to start playlist playback. Please try again.');
            }
        }
    }

    async playTrack(trackUri, buttonElement = null) {
        try {
            // Update button to loading state immediately
            if (buttonElement) {
                this.updateTrackButtonState(buttonElement, 'loading');
            }

            // Check if there's an active device
            const playbackState = await this.spotifyApi.getCurrentPlaybackState();

            if (!playbackState || !playbackState.device) {
                // No active device found, try to activate Web Playback SDK player
                console.log('Player instance check:', {
                    playerExists: !!this.player,
                    playerPlayerExists: !!(this.player && this.player.player),
                    playerDeviceId: this.player ? this.player.deviceId : 'N/A'
                });

                if (this.player && this.player.player) {
                    console.log('Attempting to activate Web Playback SDK player...');

                    try {
                        // Check if player is already connected
                        const connected = await this.player.player.connected;
                        console.log('Player connected status:', connected);

                        if (!connected) {
                            console.log('Connecting player...');
                            await this.player.player.connect();
                            console.log('Player connect() called, waiting for ready event...');
                        }

                        // Wait for player to be ready (up to 5 seconds)
                        let attempts = 0;
                        while (!this.player.deviceId && attempts < 10) {
                            console.log(`Waiting for player ready... attempt ${attempts + 1}/10, deviceId: ${this.player.deviceId}`);
                            await new Promise(resolve => setTimeout(resolve, 500));
                            attempts++;
                        }

                        if (this.player.deviceId) {
                            console.log('Player ready with deviceId:', this.player.deviceId);
                            console.log('Transferring playback to device:', this.player.deviceId);
                            await this.spotifyApi.transferPlayback(this.player.deviceId, true);
                            console.log('Playback transfer successful');
                        } else {
                            console.error('Player failed to become ready - no deviceId after 5 seconds');
                            console.error('Player state:', {
                                player: !!this.player,
                                playerPlayer: !!(this.player && this.player.player),
                                connected: this.player && this.player.player ? await this.player.player.connected : 'N/A'
                            });
                            throw new Error('Web player failed to initialize');
                        }
                    } catch (playerError) {
                        console.error('Failed to activate Web player:', playerError);
                        this.hideLoading();
                        this.showError('Failed to activate Web Player. Please refresh the page and try again.');
                        return;
                    }
                } else {
                    console.error('No player instance available');
                    console.error('Player debug info:', {
                        player: this.player,
                        hasPlayer: !!this.player,
                        hasPlayerPlayer: !!(this.player && this.player.player),
                        spotifySdkLoaded: !!window.Spotify
                    });
                    this.hideLoading();
                    this.showError('Web Player not available. Please refresh the page.');
                    return;
                }
            }

            // Start playback with the specific track URI
            await this.spotifyApi.startPlayback(null, [trackUri]);

            // Update button to pause state when playback starts
            if (buttonElement) {
                this.updateTrackButtonState(buttonElement, 'pause');
            }

            // Also update all other track buttons to reflect the new playing state
            if (window.spotifyApp?.player) {
                // Get current track info to update other buttons
                setTimeout(async () => {
                    try {
                        const playbackState = await this.spotifyApi.getCurrentPlaybackState();
                        if (playbackState && playbackState.item) {
                            window.spotifyApp.player.updateTrackButtonsState(playbackState.item, false);
                        }
                    } catch (error) {
                        console.error('Failed to update track buttons after playback start:', error);
                    }
                }, 500); // Small delay to ensure playback has started
            }

            this.showSuccess('Track playback started!');

        } catch (error) {
            console.error('Failed to play track:', error);
            // Reset button to play state on error
            if (buttonElement) {
                this.updateTrackButtonState(buttonElement, 'play');
            }

            // Provide more specific error messages
            if (error.message && error.message.includes('No active device found')) {
                this.showError('No active Spotify device found. Please start Spotify on another device or ensure the Web Player is connected.');
            } else {
                this.showError('Failed to start track playback. Please try again.');
            }
        }
    }

    updateTrackButtonState(buttonElement, state) {
        const icon = buttonElement.querySelector('i');
        if (!icon) return;

        // Remove all state classes
        buttonElement.classList.remove('loading', 'playing', 'paused');
        icon.className = '';

        switch (state) {
            case 'loading':
                buttonElement.classList.add('loading');
                icon.className = 'fas fa-spinner fa-spin';
                buttonElement.title = 'Loading...';
                break;
            case 'play':
                icon.className = 'fas fa-play';
                buttonElement.title = 'Play';
                break;
            case 'pause':
                buttonElement.classList.add('playing');
                icon.className = 'fas fa-pause';
                buttonElement.title = 'Pause';
                // Update onclick to pause functionality
                buttonElement.onclick = () => this.pauseTrack(buttonElement);
                break;
            default:
                icon.className = 'fas fa-play';
                buttonElement.title = 'Play';
        }
    }

    async pauseTrack(buttonElement) {
        try {
            // Update button to loading state
            this.updateTrackButtonState(buttonElement, 'loading');

            // Pause playback
            await this.spotifyApi.pausePlayback();

            // Update button back to play state
            this.updateTrackButtonState(buttonElement, 'play');

            // Also update all other track buttons to reflect the paused state
            if (window.spotifyApp?.player) {
                // Get current track info to update other buttons
                setTimeout(async () => {
                    try {
                        const playbackState = await this.spotifyApi.getCurrentPlaybackState();
                        if (playbackState && playbackState.item) {
                            window.spotifyApp.player.updateTrackButtonsState(playbackState.item, true);
                        }
                    } catch (error) {
                        console.error('Failed to update track buttons after pause:', error);
                    }
                }, 300); // Small delay to ensure pause has completed
            }

        } catch (error) {
            console.error('Failed to pause track:', error);
            // Reset to pause state on error
            this.updateTrackButtonState(buttonElement, 'pause');
            this.showError('Failed to pause track');
        }
    }

    async getPlaylistName(playlistId) {
        try {
            const playlist = await this.spotifyApi.getPlaylist(playlistId);
            return playlist.name;
        } catch (error) {
            console.error('Failed to get playlist name:', error);
            return 'Playlist Tracks';
        }
    }

    showPlaylistTracksModal(tracks, playlistName) {
        const modal = document.getElementById('playlist-tracks-modal');
        const title = document.getElementById('playlist-tracks-title');
        const tracksList = document.getElementById('playlist-tracks-list');
        const searchInput = document.getElementById('playlist-tracks-search');

        // Store original tracks for filtering
        this.originalTracks = tracks;
        this.filteredTracks = [...tracks];
        this.currentTracksSort = { field: null, direction: 'asc' };

        // Initialize virtual scrolling
        this.virtualScroll = {
            itemHeight: 60, // Approximate height of each track row
            containerHeight: 500,
            bufferSize: 10, // Extra items to render outside visible area
            startIndex: 0,
            endIndex: 0,
            scrollTop: 0,
            scrollHandler: null // Store reference to scroll handler for cleanup
        };

        // Set modal title
        title.textContent = `${playlistName} (${tracks.length} tracks)`;

        // Clear search input
        searchInput.value = '';

        // Setup search functionality
        searchInput.oninput = (e) => {
            this.filterPlaylistTracks(e.target.value);
        };

        // Setup sorting
        const sortableHeaders = modal.querySelectorAll('.sortable');
        sortableHeaders.forEach(header => {
            header.onclick = () => {
                const sortField = header.dataset.sort;
                this.sortPlaylistTracks(sortField);
            };
        });

        // Setup virtual scrolling
        this.setupVirtualScrolling();

        // Render tracks
        this.renderPlaylistTracks();

        // Show modal
        modal.style.display = 'flex';

        // Add event listener for close button
        const closeBtn = document.getElementById('close-playlist-tracks');
        closeBtn.onclick = () => this.hidePlaylistTracksModal();

        // Close modal when clicking outside
        modal.onclick = (e) => {
            if (e.target === modal) {
                this.hidePlaylistTracksModal();
            }
        };

        // Close modal on Escape key
        document.addEventListener('keydown', this.handleModalKeydown.bind(this));
    }

    setupVirtualScrolling() {
        const container = document.querySelector('.playlist-tracks-table-container');
        if (!container) return;

        // Clean up any existing scroll listener
        if (this.virtualScroll.scrollHandler) {
            container.removeEventListener('scroll', this.virtualScroll.scrollHandler);
        }

        // Update container height in virtual scroll config
        this.virtualScroll.containerHeight = container.clientHeight || 500;

        // Create scroll event handler with debouncing
        this.virtualScroll.scrollHandler = this.debounce((e) => {
            this.virtualScroll.scrollTop = e.target.scrollTop;
            this.updateVisibleRange();
            this.renderPlaylistTracks();
        }, 16); // ~60fps

        // Add scroll event listener
        container.addEventListener('scroll', this.virtualScroll.scrollHandler);

        // Initial visible range calculation
        this.updateVisibleRange();
    }

    updateVisibleRange() {
        const { itemHeight, containerHeight, bufferSize } = this.virtualScroll;
        const scrollTop = this.virtualScroll.scrollTop;

        // Calculate visible range with buffer
        const startIndex = Math.max(0, Math.floor(scrollTop / itemHeight) - bufferSize);
        const visibleCount = Math.ceil(containerHeight / itemHeight) + (bufferSize * 2);
        const endIndex = Math.min(this.filteredTracks.length, startIndex + visibleCount);

        this.virtualScroll.startIndex = startIndex;
        this.virtualScroll.endIndex = endIndex;
    }

    renderPlaylistTracks() {
        const tracksList = document.getElementById('playlist-tracks-list');
        const tracks = this.filteredTracks;
        const { startIndex, endIndex, itemHeight } = this.virtualScroll;

        // Clear previous tracks
        tracksList.innerHTML = '';

        // Add top spacer for virtual scrolling
        if (startIndex > 0) {
            const topSpacer = document.createElement('tr');
            topSpacer.innerHTML = `<td colspan="5" style="height: ${startIndex * itemHeight}px;"></td>`;
            tracksList.appendChild(topSpacer);
        }

        // Add visible tracks
        for (let i = startIndex; i < endIndex; i++) {
            const item = tracks[i];
            const track = item.track;
            if (!track) continue;

            const trackRow = document.createElement('tr');
            trackRow.className = 'playlist-track-row';
            trackRow.dataset.trackUri = track.uri;
            trackRow.innerHTML = `
                <td class="track-number">${i + 1}</td>
                <td class="track-name-cell">
                    <div class="track-name">
                        <img src="${track.album?.images?.[0]?.url || ''}" alt="${track.album?.name || 'Album'}" class="track-image">
                        <span>${track.name}</span>
                    </div>
                </td>
                <td class="track-author-cell">${track.artists?.map(artist => artist.name).join(', ') || 'Unknown Artist'}</td>
                <td class="track-duration">${this.formatDuration(track.duration_ms)}</td>
                <td class="track-play-cell">
                    <button class="track-play-btn" onclick="window.spotifyApp.playTrack('${track.uri}', this)" title="Play" data-track-uri="${track.uri}">
                        <i class="fas fa-play"></i>
                    </button>
                </td>
            `;

            tracksList.appendChild(trackRow);
        }

        // Add bottom spacer for virtual scrolling
        const remainingItems = tracks.length - endIndex;
        if (remainingItems > 0) {
            const bottomSpacer = document.createElement('tr');
            bottomSpacer.innerHTML = `<td colspan="5" style="height: ${remainingItems * itemHeight}px;"></td>`;
            tracksList.appendChild(bottomSpacer);
        }
    }

    filterPlaylistTracks(query) {
        if (!query.trim()) {
            this.filteredTracks = [...this.originalTracks];
        } else {
            const lowerQuery = query.toLowerCase();
            this.filteredTracks = this.originalTracks.filter(item => {
                const track = item.track;
                if (!track) return false;

                const nameMatch = track.name.toLowerCase().includes(lowerQuery);
                const artistMatch = track.artists?.some(artist =>
                    artist.name.toLowerCase().includes(lowerQuery)
                );

                return nameMatch || artistMatch;
            });
        }

        // Reset virtual scroll position when filtering
        this.virtualScroll.scrollTop = 0;
        this.virtualScroll.startIndex = 0;
        this.updateVisibleRange();

        // Re-apply current sort to filtered results
        if (this.currentTracksSort.field) {
            this.sortPlaylistTracks(this.currentTracksSort.field, false);
        } else {
            this.renderPlaylistTracks();
        }
    }

    sortPlaylistTracks(field, updateDirection = true) {
        if (updateDirection) {
            if (this.currentTracksSort.field === field) {
                this.currentTracksSort.direction = this.currentTracksSort.direction === 'asc' ? 'desc' : 'asc';
            } else {
                this.currentTracksSort.field = field;
                this.currentTracksSort.direction = 'asc';
            }
        }

        // Update sort icons
        const modal = document.getElementById('playlist-tracks-modal');
        const sortableHeaders = modal.querySelectorAll('.sortable');
        sortableHeaders.forEach(header => {
            header.classList.remove('sort-asc', 'sort-desc');
            if (header.dataset.sort === field) {
                header.classList.add(`sort-${this.currentTracksSort.direction}`);
            }
        });

        // Sort the filtered tracks array
        this.filteredTracks.sort((a, b) => {
            const trackA = a.track;
            const trackB = b.track;
            if (!trackA || !trackB) return 0;

            let valueA, valueB;

            switch (field) {
                case 'name':
                    valueA = trackA.name.toLowerCase();
                    valueB = trackB.name.toLowerCase();
                    break;
                case 'author':
                    valueA = trackA.artists?.[0]?.name.toLowerCase() || '';
                    valueB = trackB.artists?.[0]?.name.toLowerCase() || '';
                    break;
                default:
                    return 0;
            }

            if (valueA < valueB) return this.currentTracksSort.direction === 'asc' ? -1 : 1;
            if (valueA > valueB) return this.currentTracksSort.direction === 'asc' ? 1 : -1;
            return 0;
        });

        // Reset scroll position when sorting
        this.virtualScroll.scrollTop = 0;
        this.virtualScroll.startIndex = 0;
        this.updateVisibleRange();

        // Re-render with virtual scrolling
        this.renderPlaylistTracks();
    }

    reorderTableRows() {
        // With virtual scrolling, we just re-render the visible range
        this.renderPlaylistTracks();
    }

    hidePlaylistTracksModal() {
        const modal = document.getElementById('playlist-tracks-modal');
        const container = document.querySelector('.playlist-tracks-table-container');

        // Clean up scroll event listener
        if (container && this.virtualScroll.scrollHandler) {
            container.removeEventListener('scroll', this.virtualScroll.scrollHandler);
            this.virtualScroll.scrollHandler = null;
        }

        modal.style.display = 'none';
        document.removeEventListener('keydown', this.handleModalKeydown.bind(this));
    }

    handleModalKeydown(e) {
        if (e.key === 'Escape') {
            this.hidePlaylistTracksModal();
        }
    }

    formatDuration(ms) {
        const minutes = Math.floor(ms / 60000);
        const seconds = Math.floor((ms % 60000) / 1000);
        return `${minutes}:${seconds.toString().padStart(2, '0')}`;
    }

    debounce(func, wait) {
        let timeout;
        return function executedFunction(...args) {
            const later = () => {
                clearTimeout(timeout);
                func(...args);
            };
            clearTimeout(timeout);
            timeout = setTimeout(later, wait);
        };
    }

    async performSearch() {
        const query = document.getElementById('search-input').value.trim();
        if (!query) return;

        try {
            this.showLoading('Searching...');

            // Get selected search types from filters
            const searchTypes = [];
            if (document.getElementById('filter-tracks').checked) searchTypes.push('track');
            if (document.getElementById('filter-artists').checked) searchTypes.push('artist');
            if (document.getElementById('filter-albums').checked) searchTypes.push('album');
            if (document.getElementById('filter-playlists').checked) searchTypes.push('playlist');

            if (searchTypes.length === 0) {
                this.showError('Please select at least one search category.');
                this.hideLoading();
                return;
            }

            const results = await this.spotifyApi.search(query, searchTypes, 50);
            this.displaySearchResults(results);
            this.hideLoading();
        } catch (error) {
            this.hideLoading();
            console.error('Search failed:', error);
            this.showError('Search failed. Please try again.');
        }
    }

    displaySearchResults(results) {
        const container = document.getElementById('search-results');

        // Store results for tab switching
        this.searchResults = results;

        // Create tabbed content structure
        let html = '<div class="search-content">';

        // Add tab content containers
        html += '<div id="all-results" class="tab-content active">';
        html += this.renderAllResults(results);
        html += '</div>';

        if (results.tracks?.items?.length > 0) {
            html += '<div id="tracks-results" class="tab-content">';
            html += this.renderTracksResults(results.tracks.items);
            html += '</div>';
        }

        if (results.artists?.items?.length > 0) {
            html += '<div id="artists-results" class="tab-content">';
            html += this.renderArtistsResults(results.artists.items);
            html += '</div>';
        }

        if (results.albums?.items?.length > 0) {
            html += '<div id="albums-results" class="tab-content">';
            html += this.renderAlbumsResults(results.albums.items);
            html += '</div>';
        }

        if (results.playlists?.items?.length > 0) {
            html += '<div id="playlists-results" class="tab-content">';
            html += this.renderPlaylistsResults(results.playlists.items);
            html += '</div>';
        }

        html += '</div>';
        container.innerHTML = html;

        // Add click handlers for all result items
        this.addSearchResultHandlers();
    }

    renderAllResults(results) {
        let html = '<div class="search-results-grid">';

        // Show top results from each category
        if (results.tracks?.items?.length > 0) {
            html += results.tracks.items.slice(0, 4).map(track => `
                <div class="search-result-item" data-type="track" data-uri="${track.uri}">
                    <img src="${track.album.images[0]?.url || ''}" alt="${track.name}" class="result-image">
                    <div class="result-title">${track.name}</div>
                    <div class="result-subtitle">${track.artists.map(a => a.name).join(', ')}</div>
                    <span class="result-type">Track</span>
                </div>
            `).join('');
        }

        if (results.artists?.items?.length > 0) {
            html += results.artists.items.slice(0, 4).map(artist => `
                <div class="search-result-item" data-type="artist" data-uri="${artist.uri}">
                    <img src="${artist.images[0]?.url || ''}" alt="${artist.name}" class="result-image">
                    <div class="result-title">${artist.name}</div>
                    <div class="result-subtitle">Artist</div>
                    <span class="result-type">Artist</span>
                </div>
            `).join('');
        }

        if (results.albums?.items?.length > 0) {
            html += results.albums.items.slice(0, 4).map(album => `
                <div class="search-result-item" data-type="album" data-uri="${album.uri}">
                    <img src="${album.images[0]?.url || ''}" alt="${album.name}" class="result-image">
                    <div class="result-title">${album.name}</div>
                    <div class="result-subtitle">${album.artists.map(a => a.name).join(', ')}</div>
                    <span class="result-type">Album</span>
                </div>
            `).join('');
        }

        if (results.playlists?.items?.length > 0) {
            html += results.playlists.items.slice(0, 4).map(playlist => `
                <div class="search-result-item" data-type="playlist" data-uri="${playlist.uri}">
                    <img src="${playlist.images[0]?.url || ''}" alt="${playlist.name}" class="result-image">
                    <div class="result-title">${playlist.name}</div>
                    <div class="result-subtitle">By ${playlist.owner.display_name}</div>
                    <span class="result-type">Playlist</span>
                </div>
            `).join('');
        }

        html += '</div>';
        return html;
    }

    renderTracksResults(tracks) {
        return `
            <div class="search-results-grid">
                ${tracks.map(track => `
                    <div class="search-result-item" data-type="track" data-uri="${track.uri}">
                        <img src="${track.album.images[0]?.url || ''}" alt="${track.name}" class="result-image">
                        <div class="result-title">${track.name}</div>
                        <div class="result-subtitle">${track.artists.map(a => a.name).join(', ')}</div>
                        <span class="result-type">Track</span>
                    </div>
                `).join('')}
            </div>
        `;
    }

    renderArtistsResults(artists) {
        return `
            <div class="search-results-grid">
                ${artists.map(artist => `
                    <div class="search-result-item" data-type="artist" data-uri="${artist.uri}">
                        <img src="${artist.images[0]?.url || ''}" alt="${artist.name}" class="result-image">
                        <div class="result-title">${artist.name}</div>
                        <div class="result-subtitle">Artist</div>
                        <span class="result-type">Artist</span>
                    </div>
                `).join('')}
            </div>
        `;
    }

    renderAlbumsResults(albums) {
        return `
            <div class="search-results-grid">
                ${albums.map(album => `
                    <div class="search-result-item" data-type="album" data-uri="${album.uri}">
                        <img src="${album.images[0]?.url || ''}" alt="${album.name}" class="result-image">
                        <div class="result-title">${album.name}</div>
                        <div class="result-subtitle">${album.artists.map(a => a.name).join(', ')}</div>
                        <span class="result-type">Album</span>
                    </div>
                `).join('')}
            </div>
        `;
    }

    renderPlaylistsResults(playlists) {
        return `
            <div class="search-results-grid">
                ${playlists.map(playlist => `
                    <div class="search-result-item" data-type="playlist" data-uri="${playlist.uri}">
                        <img src="${playlist.images[0]?.url || ''}" alt="${playlist.name}" class="result-image">
                        <div class="result-title">${playlist.name}</div>
                        <div class="result-subtitle">By ${playlist.owner.display_name}</div>
                        <span class="result-type">Playlist</span>
                    </div>
                `).join('')}
            </div>
        `;
    }

    addSearchResultHandlers() {
        // Add click handlers for search result items
        document.querySelectorAll('.search-result-item').forEach(item => {
            item.addEventListener('click', (e) => {
                const type = item.dataset.type;
                const uri = item.dataset.uri;

                switch (type) {
                    case 'track':
                        this.player.playTrack(uri);
                        break;
                    case 'artist':
                        // Could navigate to artist page or play top tracks
                        this.showSuccess('Artist selected: ' + item.querySelector('.result-title').textContent);
                        break;
                    case 'album':
                        // Could navigate to album page or play album
                        this.showSuccess('Album selected: ' + item.querySelector('.result-title').textContent);
                        break;
                    case 'playlist':
                        // Could navigate to playlist page or play playlist
                        this.showSuccess('Playlist selected: ' + item.querySelector('.result-title').textContent);
                        break;
                }
            });
        });
    }

    updateUI() {
        const connectBtn = document.getElementById('connect-btn');
        const disconnectBtn = document.getElementById('disconnect-btn');
        const userDisplay = document.getElementById('user-display-name');

        if (this.isConnected) {
            // Show connected state
            if (connectBtn) connectBtn.style.display = 'none';
            if (disconnectBtn) disconnectBtn.style.display = 'flex';
            if (userDisplay) userDisplay.textContent = 'Connected';

            // Switch to home section if we're in setup
            if (this.currentSection === 'home') {
                this.showConnectedHome();
            }
        } else {
            // Show setup state
            if (connectBtn) connectBtn.style.display = 'flex';
            if (disconnectBtn) disconnectBtn.style.display = 'none';
            if (userDisplay) userDisplay.textContent = 'Not Connected';
        }
    }

    showConnectedHome() {
        const homeSection = document.getElementById('home-section');
        homeSection.innerHTML = `
            <div class="welcome-section">
                <h2>Welcome to Spofify</h2>
                <p>You're successfully connected! Browse your music, create playlists, and enjoy your favorite tracks.</p>
                <div class="quick-actions">
                    <button class="quick-action-btn" onclick="window.spotifyApp.switchSection('search')">
                        <i class="fas fa-search"></i>
                        Search Music
                    </button>
                    <button class="quick-action-btn" onclick="window.spotifyApp.switchSection('library')">
                        <i class="fas fa-music"></i>
                        Your Library
                    </button>
                    <button class="quick-action-btn" onclick="window.spotifyApp.switchSection('playlists')">
                        <i class="fas fa-list"></i>
                        Playlists
                    </button>
                </div>
            </div>
        `;
    }

    // Device Management Methods
    async loadDevices() {
        try {
            this.showLoading('Loading devices...');
            const devicesResponse = await this.spotifyApi.getDevices();
            this.hideLoading();

            const devicesList = document.getElementById('devices-list');
            if (devicesResponse.devices && devicesResponse.devices.length > 0) {
                devicesList.innerHTML = devicesResponse.devices.map(device => `
                    <div class="device-card ${device.is_active ? 'active' : ''}" data-id="${device.id}">
                        <div class="device-icon">
                            <i class="fas fa-${this.getDeviceIcon(device.type)}"></i>
                        </div>
                        <div class="device-info">
                            <div class="device-name">${device.name}</div>
                            <div class="device-type">${device.type}</div>
                            <div class="device-status">${device.is_active ? 'Active' : 'Inactive'}</div>
                            ${device.supports_volume ? `
                                <div class="device-volume">
                                    <i class="fas fa-volume-up volume-icon"></i>
                                    <input type="range" class="volume-slider" min="0" max="100"
                                           value="${device.volume_percent || 50}"
                                           data-device-id="${device.id}"
                                           ${!device.is_active ? 'disabled' : ''}>
                                </div>
                            ` : ''}
                        </div>
                        <div class="device-actions">
                            ${!device.is_active ? `
                                <button class="transfer-btn" onclick="window.spotifyApp.transferPlayback('${device.id}')">
                                    Transfer
                                </button>
                            ` : ''}
                        </div>
                    </div>
                `).join('');

                // Add volume slider event listeners
                devicesList.querySelectorAll('.volume-slider').forEach(slider => {
                    slider.addEventListener('input', (e) => {
                        const deviceId = e.target.dataset.deviceId;
                        const volume = e.target.value;

                        // Only attempt to set volume if slider is not disabled
                        if (!e.target.disabled) {
                            this.setDeviceVolume(deviceId, volume);
                        }
                    });
                });
            } else {
                devicesList.innerHTML = '<p>No devices found. Make sure Spotify is running on another device.</p>';
            }
        } catch (error) {
            this.hideLoading();
            console.error('Failed to load devices:', error);
            const devicesList = document.getElementById('devices-list');
            devicesList.innerHTML = '<p>Failed to load devices. Please try again.</p>';
            this.showError('Failed to load devices');
        }
    }

    getDeviceIcon(type) {
        const iconMap = {
            'Computer': 'desktop',
            'Smartphone': 'mobile-alt',
            'Speaker': 'volume-up',
            'TV': 'tv',
            'Tablet': 'tablet-alt',
            'GameConsole': 'gamepad',
            'CastVideo': 'cast',
            'CastAudio': 'cast',
            'Automobile': 'car',
            'Unknown': 'question-circle'
        };
        return iconMap[type] || 'question-circle';
    }

    async transferPlayback(deviceId) {
        try {
            this.showLoading('Transferring playback...');
            await this.spotifyApi.transferPlayback(deviceId, true);
            this.hideLoading();
            this.showSuccess('Playback transferred successfully');

            // Refresh devices list to show updated status
            setTimeout(() => {
                this.loadDevices();
            }, 1000);
        } catch (error) {
            this.hideLoading();
            console.error('Failed to transfer playback:', error);
            this.showError('Failed to transfer playback');
        }
    }

    async setDeviceVolume(deviceId, volumePercent) {
        try {
            // Check if this device is active
            const devicesResponse = await this.spotifyApi.getDevices();
            const device = devicesResponse.devices.find(d => d.id === deviceId);

            if (!device) {
                console.error('Device not found:', deviceId);
                return;
            }

            if (!device.is_active) {
                // For inactive devices, transfer playback first, then set volume
                console.log('Transferring playback to device before setting volume:', deviceId);
                await this.transferPlayback(deviceId);

                // Wait a moment for the transfer to complete
                await new Promise(resolve => setTimeout(resolve, 1000));

                // Now try to set the volume
                await this.spotifyApi.setVolume(volumePercent);
                console.log('Volume set successfully after transfer');
            } else {
                // Device is already active, just set volume
                await this.spotifyApi.setVolume(volumePercent);
            }
        } catch (error) {
            console.error('Failed to set device volume:', error);
            // Show a user-friendly error message
            if (error.message.includes('No active device')) {
                this.showError('Please start playback on a device before adjusting volume.');
            } else {
                this.showError('Failed to adjust volume. Please try again.');
            }
        }
    }

    // Add device refresh button handler
    bindDeviceEvents() {
        const refreshBtn = document.getElementById('refresh-devices-btn');
        if (refreshBtn) {
            refreshBtn.addEventListener('click', () => {
                this.loadDevices();
            });
        }
    }

    // Search tab events
    bindSearchTabEvents() {
        document.querySelectorAll('.search-tab').forEach(tab => {
            tab.addEventListener('click', (e) => {
                const tabName = e.target.dataset.tab;
                this.switchSearchTab(tabName);
            });
        });
    }

    switchSearchTab(tabName) {
        // Update tab active states
        document.querySelectorAll('.search-tab').forEach(tab => {
            tab.classList.remove('active');
        });
        document.querySelector(`[data-tab="${tabName}"]`).classList.add('active');

        // Update content visibility
        document.querySelectorAll('.tab-content').forEach(content => {
            content.classList.remove('active');
        });
        document.getElementById(`${tabName}-results`).classList.add('active');
    }

    // Device status monitoring
    startDeviceMonitoring() {
        if (this.deviceRefreshTimer) {
            clearInterval(this.deviceRefreshTimer);
        }

        this.deviceRefreshTimer = setInterval(() => {
            if (this.isConnected && this.currentSection === 'devices') {
                this.loadDevices();
            }
        }, this.deviceRefreshInterval);
    }

    stopDeviceMonitoring() {
        if (this.deviceRefreshTimer) {
            clearInterval(this.deviceRefreshTimer);
            this.deviceRefreshTimer = null;
        }
    }

    // Disconnect from Spotify
    disconnect() {
        this.isConnected = false;
        this.stopDeviceMonitoring();

        // Clear stored tokens
        localStorage.removeItem('spotify_access_token');
        localStorage.removeItem('spotify_token_expires');
        localStorage.removeItem('spotify_client_id');

        // Reset UI
        this.updateUI();

        // Clear content sections
        document.getElementById('playlists-content').innerHTML = '';
        document.getElementById('library-content').innerHTML = '';
        document.getElementById('devices-list').innerHTML = '<p>Connect to Spotify to view devices.</p>';

        // Switch to home section
        this.switchSection('home');
        this.showSpotifySetup();

        this.showSuccess('Disconnected from Spotify');
    }
}

// Initialize the app when DOM is loaded
document.addEventListener('DOMContentLoaded', () => {
    // Initialize Spotify API first
    window.spotifyApi = new SpotifyAPI();
    window.spotifyApp = new SpotifyMacOSApp();
});

// Global debug function for console access
window.debugSpotifyTokens = function() {
    console.log('=== SPOTIFY TOKEN DEBUG ===');
    const keys = ['spotify_client_id', 'spotify_access_token', 'spotify_token_expires', 'spotify_refresh_token'];
    keys.forEach(key => {
        const value = localStorage.getItem(key);
        if (value) {
            if (key.includes('token') && !key.includes('expires')) {
                console.log(`${key}: ${value.substring(0, 20)}... (length: ${value.length})`);
            } else if (key.includes('expires')) {
                const expTime = parseInt(value);
                const now = Date.now();
                const timeLeft = expTime - now;
                console.log(`${key}: ${new Date(expTime).toISOString()} (${timeLeft > 0 ? Math.floor(timeLeft / 1000 / 60) + ' minutes left' : 'EXPIRED'})`);
            } else {
                console.log(`${key}: ${value}`);
            }
        } else {
            console.log(`${key}: NOT FOUND`);
        }
    });
    console.log('=== END DEBUG ===');
};