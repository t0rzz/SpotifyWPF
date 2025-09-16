// Spofyfy - Main Application Logic
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
        this.bindEvents();
        this.initializeSpotify();
        this.restoreSectionFromURL();
        this.updateUI();
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
            this.connectToSpotify();
        // Disconnect button
        document.getElementById('disconnect-btn').addEventListener('click', () => {
            this.disconnect();
        // Search
        document.getElementById('search-input').addEventListener('keypress', (e) => {
            if (e.key === 'Enter') {
                this.performSearch();
            }
        document.getElementById('search-btn').addEventListener('click', () => {
            this.performSearch();
        // Playlist search
        const playlistSearchInput = document.getElementById('playlist-search');
        if (playlistSearchInput) {
            playlistSearchInput.addEventListener('input', (e) => {
                this.filterPlaylists(e.target.value);
        }
        const clearSearchBtn = document.getElementById('clear-search-btn');
        if (clearSearchBtn) {
            clearSearchBtn.addEventListener('click', () => {
                this.clearPlaylistSearch();
        // Playlist tracks modal close button
        const closeModalBtn = document.getElementById('close-playlist-tracks');
        if (closeModalBtn) {
            closeModalBtn.addEventListener('click', () => {
                this.hidePlaylistTracksModal();
        // Network status monitoring
        window.addEventListener('online', () => {
            this.showSuccess('Internet connection restored');
            this.initializeSpotify(); // Try to reconnect
        window.addEventListener('offline', () => {
            this.showError('Internet connection lost. Some features may not work.');
        // Device management events
        this.bindDeviceEvents();
        // Search tab events
        this.bindSearchTabEvents();
        // Handle browser back/forward navigation
        window.addEventListener('hashchange', () => {
            this.restoreSectionFromURL();
        // OAuth callback handler for native macOS app
        window.oauthCallback = (result) => {
            this.handleOAuthCallback(result);
        };
    async handleOAuthCallback(result) {
        console.log('OAuth callback received:', result);
        try {
            if (result.error) {
                console.error('OAuth error:', result.error);
                this.showError(`Authorization failed: ${result.error}`);
                return;
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
                await this.loadUserData();
        } catch (error) {
            console.error('OAuth callback processing failed:', error);
            this.hideLoading();
            this.showError('Failed to complete authorization. Please try again.');
    showError(message) {
        this.showNotification(message, 'error', 'fas fa-exclamation-triangle', 5000);
    showSuccess(message) {
        this.showNotification(message, 'success', 'fas fa-check-circle', 3000);
    showLoading(message = 'Loading...') {
        // Remove existing loading notification first
        const existing = document.querySelector('.notification.loading');
        if (existing) {
            existing.remove();
        this.showNotification(message, 'loading', 'fas fa-spinner fa-spin', null, false);
    /**
     * Generic method to show notifications
     * @param {string} message - The notification message
     * @param {string} type - The notification type (error, success, loading)
     * @param {string} iconClass - The FontAwesome icon class
     * @param {number|null} autoRemoveMs - Auto-remove timeout in milliseconds, null for no auto-remove
     * @param {boolean} includeCloseButton - Whether to include a close button (default: true)
     */
    showNotification(message, type, iconClass, autoRemoveMs = null, includeCloseButton = true) {
        const notification = document.createElement('div');
        notification.className = `notification ${type}`;
        let closeButtonHtml = '';
        if (includeCloseButton) {
            closeButtonHtml = `
                <button class="notification-close" onclick="this.parentElement.remove()">
                    <i class="fas fa-times"></i>
                </button>
            `;
        notification.innerHTML = `
            <i class="${iconClass}"></i>
            <span>${message}</span>
            ${closeButtonHtml}
        `;
        document.body.appendChild(notification);
        // Auto-remove if specified
        if (autoRemoveMs) {
            setTimeout(() => {
                if (notification.parentElement) {
                    notification.remove();
            }, autoRemoveMs);
        // Log to console
        if (type === 'error') {
            console.error(`${type}:`, message);
        } else {
            console.log(`${type}:`, message);
    hideLoading() {
        const loading = document.querySelector('.notification.loading');
        if (loading) {
            loading.remove();
    switchSection(sectionName) {
        console.log('Switching to section:', sectionName);
        // Update navigation
            item.classList.remove('active');
        document.querySelector(`[data-section="${sectionName}"]`).classList.add('active');
        // Update content
        document.querySelectorAll('.content-section').forEach(section => {
            section.classList.remove('active');
        document.getElementById(`${sectionName}-section`).classList.add('active');
        // Update URL hash without triggering page reload
        const newHash = `#${sectionName}`;
        if (window.location.hash !== newHash) {
            console.log('Updating URL hash to:', newHash);
            window.history.replaceState(null, null, newHash);
        // Handle device monitoring based on current section
        if (sectionName === 'devices' && this.isConnected) {
            // Refresh devices when entering devices section
            this.loadDevices();
        this.currentSection = sectionName;
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
                            <strong>App Name:</strong> Spofyfy<br>
                            <strong>App Description:</strong> A native macOS Spotify client<br>
                            <strong>Redirect URI:</strong> <code>${window.SPOTIFY_CONFIG?.redirectUri || 'spotifywpf://callback'}</code>
                        </div>
                        <button id="copy-redirect-uri" class="copy-button">
                            <i class="fas fa-copy"></i>
                            Copy Redirect URI
                        </button>
                    </div>
                </div>
                    <div class="step-number">2</div>
                        <h3>Enter Your Client ID</h3>
                        <p>After creating your app, copy the Client ID from the dashboard.</p>
                        <div class="client-id-input">
                            <input type="text" id="client-id-input" placeholder="Enter your Spotify Client ID" class="client-id-field">
                            <button id="save-client-id" class="save-button">
                                <i class="fas fa-save"></i>
                                Save & Connect
                            </button>
                    <div class="step-number">3</div>
                        <h3>Authorize the App</h3>
                        <p>Click the button below to authorize Spofyfy to access your account.</p>
                        <button id="authorize-btn" class="authorize-button" disabled>
                            <i class="fab fa-spotify"></i>
                            Authorize Spotify
            </div>
        // Bind setup events
        this.bindSetupEvents();
    bindSetupEvents() {
        // Copy redirect URI
        document.getElementById('copy-redirect-uri').addEventListener('click', () => {
            const redirectUri = window.SPOTIFY_CONFIG?.redirectUri || 'spotifywpf://callback';
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
        // Authorize button
        document.getElementById('authorize-btn').addEventListener('click', () => {
    async initializeSpotify() {
            // Debug: Check localStorage state
            this.debugLocalStorage();
            // Check network connectivity first
            if (!this.spotifyApi.isOnline()) {
                this.showError('No internet connection. Please check your network and try again.');
                this.showSpotifySetup();
            // Check if config is properly set up
            const configClientId = window.SPOTIFY_CONFIG?.clientId;
            const storedClientId = localStorage.getItem('spotify_client_id');
            // Use stored client ID if config has placeholder, otherwise use config
            const effectiveClientId = (configClientId && configClientId !== 'your_client_id_here') ? configClientId : storedClientId;
            if (!effectiveClientId) {
                console.warn('Spotify Client ID not configured. Showing setup flow.');
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
                } else {
                    // Token is expired, try to refresh it
                    console.log('Token expired, attempting refresh...');
                    if (storedRefreshToken) {
                        try {
                            await this.spotifyApi.refreshAccessToken();
                            console.log('Token refresh successful, loading user data...');
                            // Initialize player after successful token refresh
                        } catch (refreshError) {
                            console.error('Failed to refresh token on startup:', refreshError);
                // If we get here, token validation/refresh failed
                console.log('Token validation/refresh failed, showing setup');
                // No stored credentials, show setup flow
                console.log('No stored credentials found, showing setup');
            this.updateUI();
            console.error('CRITICAL: Failed to initialize Spotify with unhandled exception:', error);
            console.error('Error details:', {
                message: error.message,
                stack: error.stack,
                name: error.name
            this.showError('Failed to initialize Spotify. Please check your configuration.');
            this.showSpotifySetup();
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
                    console.log(`${key}: ${value}`);
                console.log(`${key}: NOT FOUND`);
        console.log('=== END LOCALSTORAGE DEBUG ===');
    async connectToSpotify() {
            console.log('Starting Spotify connection...');
            const clientId = localStorage.getItem('spotify_client_id') || window.SPOTIFY_CONFIG?.clientId;
            console.log('Client ID:', clientId ? 'present' : 'missing');
            if (!clientId) {
                this.showError('Please configure your Spotify Client ID first.');
            this.spotifyApi.setClientId(clientId);
            // Generate authorization URL
            const authUrl = await this.spotifyApi.getAuthorizationUrl();
            console.log('Generated auth URL:', authUrl);
            // For native macOS app, open in external browser and let the app handle callback
            console.log('Opening authorization URL in external browser for native app');
            window.open(authUrl, '_blank');
            this.showLoading('Waiting for authorization...');
            this.showSuccess('Authorization page opened in your browser. Please complete the authorization there.');
            console.error('Failed to connect to Spotify:', error);
            this.showError('Failed to start authorization process. Please try again.');
    async loadUserData() {
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
            this.showSuccess('Data loaded successfully!');
            console.log('User data loading completed successfully');
            console.error('Failed to load user data:', error);
            this.showError('Failed to load your Spotify data. Please try reconnecting.');
            // Re-throw to let caller handle it
            throw error;
    async loadPlaylists() {
            this.showLoading('Loading all playlists...');
            const playlists = await this.spotifyApi.getAllUserPlaylists();
            // Store playlists data for sorting
            this.allPlaylists = playlists.items || [];
            this.currentSort = this.currentSort || { column: 'name', direction: 'asc' };
            // Clear search when reloading playlists (now that allPlaylists is set)
            this.clearPlaylistSearch();
            this.renderPlaylistsTable();
            console.error('Failed to load playlists:', error);
            const container = document.getElementById('playlists-content');
            container.innerHTML = '<p>Failed to load playlists. Please try again.</p>';
            this.showError('Failed to load playlists');
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
                                    <th class="description-column">Description</th>
                                    <th class="tracks-column sortable" data-sort="tracks">
                                        <span class="column-header">Tracks</span>
                                    <th class="owner-column sortable" data-sort="owner">
                                        <span class="column-header">Owner</span>
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
                                        <td class="name-column">
                                            <div class="playlist-table-name">${playlist.name || 'Unknown'}</div>
                                        <td class="description-column">
                                            <div class="playlist-table-description">${playlist.description || 'No description'}</div>
                                        <td class="tracks-column">
                                            <span class="track-count">${playlist.tracks?.total || 0}</span>
                                        <td class="owner-column">
                                            <span class="playlist-owner">${playlist.owner?.display_name || 'Unknown'}</span>
                                        <td class="actions-column">
                                            <button class="playlist-action-btn" onclick="window.spotifyApp.playPlaylist('${playlist.id || ''}')" title="Play">
                                                <i class="fas fa-play"></i>
                                            </button>
                                            <button class="playlist-action-btn" onclick="window.spotifyApp.loadPlaylistTracks('${playlist.id || ''}')" title="View Tracks">
                                                <i class="fas fa-list"></i>
                                    </tr>
                                `).join('')}
                            </tbody>
                        </table>
                `;
                this.addPlaylistTableEventListeners();
                this.updateSortIndicators();
            } catch (error) {
                console.error('Error rendering playlists table:', error);
                container.innerHTML = '<p>Failed to render playlists table. Please try again.</p>';
            container.innerHTML = '<p>No playlists found.</p>';
    sortPlaylists(playlists) {
        if (!playlists || !Array.isArray(playlists)) return playlists;
        return [...playlists].sort((a, b) => {
            let aValue, bValue;
                switch (this.currentSort.column) {
                    case 'name':
                        aValue = (a.name || '').toLowerCase();
                        bValue = (b.name || '').toLowerCase();
                        break;
                    case 'tracks':
                        aValue = a.tracks?.total || 0;
                        bValue = b.tracks?.total || 0;
                    case 'owner':
                        aValue = (a.owner?.display_name || '').toLowerCase();
                        bValue = (b.owner?.display_name || '').toLowerCase();
                    default:
                if (this.currentSort.direction === 'asc') {
                    return aValue < bValue ? -1 : aValue > bValue ? 1 : 0;
                    return aValue > bValue ? -1 : aValue < bValue ? 1 : 0;
                console.error('Error sorting playlists:', error);
                return 0; // No change in order
    handleColumnSort(column) {
        if (this.currentSort.column === column) {
            // Toggle direction if same column
            this.currentSort.direction = this.currentSort.direction === 'asc' ? 'desc' : 'asc';
            // New column, default to ascending
            this.currentSort.column = column;
            this.currentSort.direction = 'asc';
        // Re-render the table with new sort
        this.renderPlaylistsTable();
    updateSortIndicators() {
        // Update all sort indicators
        document.querySelectorAll('.sortable').forEach(header => {
            const column = header.dataset.sort;
            if (column === this.currentSort.column) {
                // Active sort column
                header.classList.add(this.currentSort.direction === 'asc' ? 'sort-asc' : 'sort-desc');
                header.classList.remove(this.currentSort.direction === 'asc' ? 'sort-desc' : 'sort-asc');
                // Inactive sort column
                header.classList.remove('sort-asc', 'sort-desc');
    }    updatePlaylistCount(total) {
        const header = document.querySelector('.playlists-header h2');
        if (header) {
            header.textContent = `Your Playlists (${total})`;
    addPlaylistTableEventListeners() {
        // Add right-click context menu to playlist rows
        document.querySelectorAll('.playlist-row').forEach(row => {
            row.addEventListener('contextmenu', (e) => {
                e.preventDefault();
                this.showPlaylistContextMenu(e, row.dataset.id);
        // Hide context menu when clicking elsewhere
        document.addEventListener('click', (e) => {
            if (!e.target.closest('.context-menu')) {
                this.hideContextMenu();
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
                });
                this.updateToolbarVisibility();
        // Individual checkbox change handlers
        document.querySelectorAll('.playlist-checkbox').forEach(checkbox => {
            if (checkbox.id !== 'select-all-playlists') {
                checkbox.addEventListener('change', (e) => {
                    const row = e.target.closest('.playlist-row');
                    row.classList.toggle('selected', e.target.checked);
                    this.updateSelectAllState();
                    this.updateToolbarVisibility();
        // Row click handlers (avoid when clicking checkbox or buttons)
            row.addEventListener('click', (e) => {
                if (e.target.type !== 'checkbox' && !e.target.classList.contains('playlist-action-btn') && !e.target.closest('.playlist-action-btn')) {
                    const checkbox = row.querySelector('.playlist-checkbox');
                    checkbox.checked = !checkbox.checked;
                    row.classList.toggle('selected', checkbox.checked);
        // Sorting event listeners for column headers
            header.addEventListener('click', (e) => {
                const column = e.currentTarget.dataset.sort;
                if (column) {
                    this.handleColumnSort(column);
            // Make headers look clickable
            header.style.cursor = 'pointer';
            header.style.userSelect = 'none';
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
            <div class="context-menu-item has-submenu">
                <i class="fas fa-external-link-alt"></i>
                Play To
                <div class="context-menu-submenu">
                    ${devices.map(device => `
                        <div class="context-menu-item" onclick="window.spotifyApp.playPlaylistOnDevice('${playlistId}', '${device.id}')">
                            <i class="fas fa-${this.getDeviceIcon(device.type)}"></i>
                            ${device.name}
                            ${device.is_active ? '<span style="color: var(--spotify-green); margin-left: auto;">●</span>' : ''}
                    `).join('')}
                    ${devices.length === 0 ? `
                        <div class="context-menu-item" style="color: var(--spotify-light-gray); cursor: default;">
                            <i class="fas fa-exclamation-triangle"></i>
                            No devices available
                    ` : ''}
            <div class="context-menu-separator"></div>
            <div class="context-menu-item" onclick="window.spotifyApp.loadPlaylistTracks('${playlistId}')">
                <i class="fas fa-list"></i>
                View Tracks
        document.body.appendChild(menu);
        menu.style.display = 'block';
        // Prevent menu from going off-screen
        const rect = menu.getBoundingClientRect();
        if (rect.right > window.innerWidth) {
            menu.style.left = `${window.innerWidth - rect.width - 10}px`;
        if (rect.bottom > window.innerHeight) {
            menu.style.top = `${window.innerHeight - rect.height - 10}px`;
    hideContextMenu() {
        const existingMenu = document.querySelector('.context-menu');
        if (existingMenu) {
            existingMenu.remove();
    async getAvailableDevices() {
            const devicesResponse = await this.spotifyApi.getDevices();
            return devicesResponse.devices || [];
            console.error('Failed to get devices:', error);
            return [];
    async playPlaylistOnDevice(playlistId, deviceId) {
            this.showLoading('Transferring playback and starting playlist...');
            // Transfer playback to the selected device
            await this.spotifyApi.transferPlayback(deviceId, true);
            // Wait a moment for the transfer to complete
            await new Promise(resolve => setTimeout(resolve, 1000));
            // Start the playlist on the new device
            const playlistUri = `spotify:playlist:${playlistId}`;
            await this.spotifyApi.startPlayback(playlistUri);
            this.showSuccess('Playlist started on selected device!');
            console.error('Failed to play playlist on device:', error);
            this.showError('Failed to start playlist on selected device');
    updateSelectAllState() {
        const totalCheckboxes = document.querySelectorAll('.playlist-checkbox:not(#select-all-playlists)').length;
        const checkedCheckboxes = document.querySelectorAll('.playlist-checkbox:not(#select-all-playlists):checked').length;
            selectAllCheckbox.checked = totalCheckboxes > 0 && checkedCheckboxes === totalCheckboxes;
            selectAllCheckbox.indeterminate = checkedCheckboxes > 0 && checkedCheckboxes < totalCheckboxes;
    updateToolbarVisibility() {
        const selectedCount = document.querySelectorAll('.playlist-checkbox:checked').length;
        const selectAllBtn = document.getElementById('select-all-btn');
        const deselectAllBtn = document.getElementById('deselect-all-btn');
        const deleteSelectedBtn = document.getElementById('delete-selected-btn');
        if (selectedCount > 0) {
            selectAllBtn.style.display = 'none';
            deselectAllBtn.style.display = 'flex';
            deleteSelectedBtn.style.display = 'flex';
            selectAllBtn.style.display = 'flex';
            deselectAllBtn.style.display = 'none';
            deleteSelectedBtn.style.display = 'none';
    selectAllPlaylists() {
        document.querySelectorAll('.playlist-checkbox:not(#select-all-playlists)').forEach(checkbox => {
            checkbox.checked = true;
            const row = checkbox.closest('.playlist-row');
            row.classList.add('selected');
        this.updateSelectAllState();
        this.updateToolbarVisibility();
    deselectAllPlaylists() {
            checkbox.checked = false;
            row.classList.remove('selected');
    async deleteSelectedPlaylists() {
        const selectedCheckboxes = document.querySelectorAll('.playlist-checkbox:checked:not(#select-all-playlists)');
        const selectedIds = Array.from(selectedCheckboxes).map(cb => cb.dataset.id);
        const selectedNames = Array.from(selectedCheckboxes).map(cb => {
            const row = cb.closest('.playlist-row');
            return row.querySelector('.playlist-table-name').textContent;
        if (selectedIds.length === 0) {
            this.showError('No playlists selected');
            return;
        const confirmed = await this.showConfirmationDialog(
            `Delete ${selectedIds.length} playlist${selectedIds.length > 1 ? 's' : ''}?`,
            `This will permanently delete the following playlists:\n\n${selectedNames.join('\n')}\n\nThis action cannot be undone.`,
            'Delete',
            'Cancel'
        );
        if (!confirmed) return;
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
            if (failureCount === 0) {
                this.showSuccess(`Successfully deleted ${successCount} playlist${successCount > 1 ? 's' : ''}`);
            } else if (successCount === 0) {
                this.showError(`Failed to delete any playlists. Please try again.`);
                this.showSuccess(`Successfully deleted ${successCount} playlist${successCount > 1 ? 's' : ''}, but ${failureCount} failed.`);
            // Refresh playlists
            console.error('Failed to delete playlists:', error);
            this.showError('Failed to delete playlists. Please try again.');
     * Process operations in batches with controlled concurrency and rate limit handling
     * @param {Array} items - Array of items to process
     * @param {Function} operation - Async function to execute for each item
     * @param {string} operationName - Name of the operation for progress updates
     * @param {number} maxConcurrency - Maximum number of concurrent operations (default: 8)
     * @param {number} batchDelay - Delay between batches in ms (default: 100)
     * @returns {Array} Array of results with success/failure status
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
                        if (window.app) {
                            window.app.showError(`Rate limited during ${operationName.toLowerCase()}. Slowing down to avoid further limits...`);
                    results.push({ item, error, success: false, index: i + index });
                    return { success: false, index: i + index };
            // Wait for current batch to complete
            await Promise.allSettled(batchPromises);
            // Add delay between batches to avoid overwhelming the API
            if (i + maxConcurrency < totalItems) {
                // If we've had recent rate limits, use the increased delay
                const actualDelay = consecutiveRateLimits > 0 ? currentBatchDelay * 2 : currentBatchDelay;
                console.log(`Waiting ${actualDelay}ms before next batch (rate limit protection)`);
                await new Promise(resolve => setTimeout(resolve, actualDelay));
        const successCount = results.filter(r => r.success).length;
        const failureCount = results.filter(r => !r.success).length;
        console.log(`${operationName} completed: ${successCount} successful, ${failureCount} failed`);
        if (rateLimitHits > 0) {
            console.log(`Rate limit statistics: ${rateLimitHits} hits, final concurrency: ${maxConcurrency}, final delay: ${currentBatchDelay}ms`);
        return results;
    async unfollowSelectedPlaylists() {
            `Unfollow ${selectedIds.length} playlist${selectedIds.length > 1 ? 's' : ''}?`,
            `This will unfollow the following playlists:\n\n${selectedNames.join('\n')}\n\nYou can follow them again later.`,
            'Unfollow',
            this.showLoading('Unfollowing playlists...');
            // Use batched processing for better performance
                    const result = await this.spotifyApi.unfollowPlaylist(playlistId);
                    console.log(`Unfollowed playlist: ${playlistId}`);
                'Unfollowing playlists',
                this.showSuccess(`Successfully unfollowed ${successCount} playlist${successCount > 1 ? 's' : ''}`);
                this.showError(`Failed to unfollow any playlists. Please try again.`);
                this.showSuccess(`Successfully unfollowed ${successCount} playlist${successCount > 1 ? 's' : ''}, but ${failureCount} failed.`);
            console.error('Failed to unfollow playlists:', error);
            this.showError('Failed to unfollow playlists. Please try again.');
    // Combined method that intelligently handles owned vs followed playlists
    async removeSelectedPlaylists() {
        const selectedItems = Array.from(selectedCheckboxes).map(cb => ({
            id: cb.dataset.id,
            row: cb.closest('.playlist-row'),
            name: cb.closest('.playlist-row').querySelector('.playlist-table-name').textContent,
            owner: cb.closest('.playlist-row').querySelector('.playlist-owner').textContent
        }));
        if (selectedItems.length === 0) {
        // Check if current user owns any of the selected playlists
        const ownedPlaylists = selectedItems.filter(item => {
            // Simple check - if the owner name matches current user, consider it owned
            const currentUserDisplayName = document.querySelector('.user-info .user-name')?.textContent || '';
            return item.owner.toLowerCase() === currentUserDisplayName.toLowerCase();
        const followedPlaylists = selectedItems.filter(item => !ownedPlaylists.includes(item));
        let actionType = '';
        let message = '';
        if (ownedPlaylists.length > 0 && followedPlaylists.length > 0) {
            actionType = 'Remove';
            message = `You are about to:\n\nDELETE ${ownedPlaylists.length} playlist${ownedPlaylists.length > 1 ? 's' : ''} you own:\n${ownedPlaylists.map(p => p.name).join('\n')}\n\nUNFOLLOW ${followedPlaylists.length} playlist${followedPlaylists.length > 1 ? 's' : ''}:\n${followedPlaylists.map(p => p.name).join('\n')}\n\nOwned playlists will be permanently deleted. Followed playlists can be followed again later.`;
        } else if (ownedPlaylists.length > 0) {
            actionType = 'Delete';
            message = `This will permanently delete the following playlists you own:\n\n${ownedPlaylists.map(p => p.name).join('\n')}\n\nThis action cannot be undone.`;
            actionType = 'Unfollow';
            message = `This will unfollow the following playlists:\n\n${followedPlaylists.map(p => p.name).join('\n')}\n\nYou can follow them again later.`;
            `${actionType} ${selectedItems.length} playlist${selectedItems.length > 1 ? 's' : ''}?`,
            message,
            actionType,
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
            if (followedPlaylists.length > 0) {
                const unfollowResults = await this.processBatchOperations(
                    followedPlaylists.map(p => p.id),
                        const result = await this.spotifyApi.unfollowPlaylist(playlistId);
                        console.log(`Unfollowed playlist: ${playlistId}`);
                    'Unfollowing playlists',
                allResults.push(...unfollowResults);
            const successCount = allResults.filter(r => r.success).length;
            const failureCount = allResults.filter(r => !r.success).length;
                this.showSuccess(`Successfully ${actionType.toLowerCase()}d ${successCount} playlist${successCount > 1 ? 's' : ''}`);
                this.showError(`Failed to ${actionType.toLowerCase()} any playlists. Please try again.`);
                this.showSuccess(`Successfully ${actionType.toLowerCase()}d ${successCount} playlist${successCount > 1 ? 's' : ''}, but ${failureCount} failed.`);
            console.error(`Failed to ${actionType.toLowerCase()} playlists:`, error);
            this.showError(`Failed to ${actionType.toLowerCase()} playlists. Please try again.`);
    updateLoadingMessage(message) {
        const loadingElement = document.querySelector('.loading-message');
        if (loadingElement) {
            loadingElement.textContent = message;
    // Playlist search and filtering methods
    filterPlaylists(searchTerm) {
        const clearBtn = document.getElementById('clear-search-btn');
        if (searchTerm.trim() === '') {
            // Show all playlists when search is empty
            this.filteredPlaylists = null;
            clearBtn.style.display = 'none';
            // Filter playlists based on search term
            if (this.allPlaylists && Array.isArray(this.allPlaylists)) {
                this.filteredPlaylists = this.allPlaylists.filter(playlist => {
                    const name = playlist.name.toLowerCase();
                    const description = (playlist.description || '').toLowerCase();
                    const owner = playlist.owner.display_name.toLowerCase();
                    return name.includes(searchTerm.toLowerCase()) ||
                           description.includes(searchTerm.toLowerCase()) ||
                           owner.includes(searchTerm.toLowerCase());
                this.filteredPlaylists = [];
            clearBtn.style.display = 'flex';
        // Re-render the table with filtered results
        this.updateFilteredPlaylistCount(this.filteredPlaylists ? this.filteredPlaylists.length : (this.allPlaylists ? this.allPlaylists.length : 0));
    clearPlaylistSearch() {
        const searchInput = document.getElementById('playlist-search');
        searchInput.value = '';
        clearBtn.style.display = 'none';
        this.filterPlaylists('');
    updateFilteredPlaylistCount(visibleCount) {
        const totalRows = document.querySelectorAll('.playlist-row').length;
        if (visibleCount === totalRows) {
            // Show total count when all are visible
            header.textContent = `Your Playlists (${totalRows})`;
            // Show filtered count when searching
            header.textContent = `Your Playlists (${visibleCount} of ${totalRows})`;
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
                resolve(false);
            const handleEscape = (e) => {
                if (e.key === 'Escape') {
                    handleCancel();
            const cleanup = () => {
                confirmBtn.removeEventListener('click', handleConfirm);
                cancelBtn.removeEventListener('click', handleCancel);
                document.removeEventListener('keydown', handleEscape);
            confirmBtn.addEventListener('click', handleConfirm);
            cancelBtn.addEventListener('click', handleCancel);
            document.addEventListener('keydown', handleEscape);
    async loadLibrary() {
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
                    `;
                }).join('');
                // Add click handlers for tracks
                container.querySelectorAll('.track-card').forEach(card => {
                    card.addEventListener('click', () => {
                        const trackUri = card.dataset.uri;
                        this.player.playTrack(trackUri);
                    });
                container.innerHTML = '<p>No saved tracks found.</p>';
            console.error('Failed to load library:', error);
            container.innerHTML = '<p>Failed to load library. Please try again.</p>';
            this.showError('Failed to load your saved tracks');
    async loadPlaylistTracks(playlistId) {
            // Show modal immediately with loading state
            this.showPlaylistTracksModalLoading();
            // Add minimum loading time to ensure loading state is visible
            const loadingStartTime = Date.now();
            const minLoadingTime = 800; // 800ms minimum
            const [tracks] = await Promise.all([
                this.spotifyApi.getPlaylistTracks(playlistId),
                new Promise(resolve => setTimeout(resolve, minLoadingTime))
            ]);
            // Ensure minimum loading time has passed
            const elapsedTime = Date.now() - loadingStartTime;
            if (elapsedTime < minLoadingTime) {
                await new Promise(resolve => setTimeout(resolve, minLoadingTime - elapsedTime));
            // Get playlist name for the modal title
            const playlistName = await this.getPlaylistName(playlistId);
            // Display tracks in modal
            this.showPlaylistTracksModal(tracks.items, playlistName);
            console.error('Failed to load playlist tracks:', error);
            this.hidePlaylistTracksModal();
            this.showError('Failed to load playlist tracks');
    showPlaylistTracksModalLoading() {
        const modal = document.getElementById('playlist-tracks-modal');
        const title = document.getElementById('playlist-tracks-title');
        const tracksList = document.getElementById('playlist-tracks-list');
        // Set modal title
        title.textContent = 'Loading Playlist';
        // Show loading state in the tracks list area
        tracksList.innerHTML = `
            <div class="playlist-loading-state">
                <div class="loading-spinner">
                    <i class="fas fa-spinner fa-spin"></i>
                <div class="loading-text">Loading playlist tracks...</div>
        // Show modal
        modal.style.display = 'flex';
    async playPlaylist(playlistId) {
            this.showLoading('Starting playlist playback...');
            await this.ensureActiveDevice();
            // Create the Spotify URI for the playlist
            // Start playback with the playlist context
            this.showSuccess('Playlist playback started!');
            console.error('Failed to play playlist:', error);
            // Provide more specific error messages
            if (error.message && error.message.includes('No active device found')) {
                this.showError('No active Spotify device found. Please start Spotify on another device or ensure the Web Player is connected.');
                this.showError('Failed to start playlist playback. Please try again.');
    async playTrack(trackUri, buttonElement = null) {
            // Update button to loading state immediately
            if (buttonElement) {
                this.updateTrackButtonState(buttonElement, 'loading');
            // Start playback with the specific track URI
            await this.spotifyApi.startPlayback(null, [trackUri]);
            // Update button to pause state when playback starts
                this.updateTrackButtonState(buttonElement, 'pause');
            // Also update all other track buttons to reflect the new playing state
            if (window.spotifyApp?.player) {
                // Get current track info to update other buttons
                setTimeout(async () => {
                        const playbackState = await this.spotifyApi.getCurrentPlaybackState();
                        if (playbackState && playbackState.item) {
                            window.spotifyApp.player.updateTrackButtonsState(playbackState.item, false);
                    } catch (error) {
                        console.error('Failed to update track buttons after playback start:', error);
                }, 500); // Small delay to ensure playback has started
            this.showSuccess('Track playback started!');
            console.error('Failed to play track:', error);
            // Reset button to play state on error
                this.updateTrackButtonState(buttonElement, 'play');
                this.showError('Failed to start track playback. Please try again.');
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
            case 'pause':
                buttonElement.classList.add('playing');
                icon.className = 'fas fa-pause';
                buttonElement.title = 'Pause';
                // Update onclick to pause functionality
                buttonElement.onclick = () => this.pauseTrack(buttonElement);
            default:
    async pauseTrack(buttonElement) {
            // Update button to loading state
            this.updateTrackButtonState(buttonElement, 'loading');
            // Pause playback
            await this.spotifyApi.pausePlayback();
            // Update button back to play state
            this.updateTrackButtonState(buttonElement, 'play');
            // Also update all other track buttons to reflect the paused state
                            window.spotifyApp.player.updateTrackButtonsState(playbackState.item, true);
                        console.error('Failed to update track buttons after pause:', error);
                }, 300); // Small delay to ensure pause has completed
            console.error('Failed to pause track:', error);
            // Reset to pause state on error
            this.updateTrackButtonState(buttonElement, 'pause');
            this.showError('Failed to pause track');
    async getPlaylistName(playlistId) {
            const playlist = await this.spotifyApi.getPlaylist(playlistId);
            return playlist.name;
            console.error('Failed to get playlist name:', error);
            return 'Playlist Tracks';
    showPlaylistTracksModal(tracks, playlistName) {
        title.textContent = `${playlistName} (${tracks.length} tracks)`;
        // Clear previous tracks
        tracksList.innerHTML = '';
        // Add tracks to the modal
        tracks.forEach((item, index) => {
            const track = item.track;
            if (!track) return; // Skip if track is null
            const trackElement = document.createElement('div');
            trackElement.className = 'playlist-track-item';
            trackElement.innerHTML = `
                <span class="track-number">${index + 1}</span>
                <img src="${track.album?.images?.[0]?.url || ''}" alt="${track.album?.name || 'Album'}" class="track-image">
                <div class="track-info">
                    <div class="track-name">${track.name}</div>
                    <div class="track-artist">${track.artists?.map(artist => artist.name).join(', ') || 'Unknown Artist'}</div>
                <span class="track-duration">${this.formatDuration(track.duration_ms)}</span>
                <button class="track-play-btn" onclick="window.spotifyApp.playTrack('${track.uri}', this)" title="Play" data-track-uri="${track.uri}">
                    <i class="fas fa-play"></i>
            tracksList.appendChild(trackElement);
        // Add event listener for close button
        const closeBtn = document.getElementById('close-playlist-tracks');
        closeBtn.onclick = () => this.hidePlaylistTracksModal();
        // Close modal when clicking outside
        modal.onclick = (e) => {
            if (e.target === modal) {
        // Close modal on Escape key
        document.addEventListener('keydown', this.handleModalKeydown.bind(this));
    hidePlaylistTracksModal() {
        modal.style.display = 'none';
        document.removeEventListener('keydown', this.handleModalKeydown.bind(this));
    handleModalKeydown(e) {
        if (e.key === 'Escape') {
    formatDuration(ms) {
        const minutes = Math.floor(ms / 60000);
        const seconds = Math.floor((ms % 60000) / 1000);
        return `${minutes}:${seconds.toString().padStart(2, '0')}`;
    async performSearch() {
        const query = document.getElementById('search-input').value.trim();
        if (!query) return;
            this.showLoading('Searching...');
            // Get selected search types from filters
            const searchTypes = [];
            if (document.getElementById('filter-tracks').checked) searchTypes.push('track');
            if (document.getElementById('filter-artists').checked) searchTypes.push('artist');
            if (document.getElementById('filter-albums').checked) searchTypes.push('album');
            if (document.getElementById('filter-playlists').checked) searchTypes.push('playlist');
            if (searchTypes.length === 0) {
                this.showError('Please select at least one search category.');
            const results = await this.spotifyApi.search(query, searchTypes, 50);
            this.displaySearchResults(results);
            console.error('Search failed:', error);
            this.showError('Search failed. Please try again.');
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
        if (results.artists?.items?.length > 0) {
            html += '<div id="artists-results" class="tab-content">';
            html += this.renderArtistsResults(results.artists.items);
        if (results.albums?.items?.length > 0) {
            html += '<div id="albums-results" class="tab-content">';
            html += this.renderAlbumsResults(results.albums.items);
        if (results.playlists?.items?.length > 0) {
            html += '<div id="playlists-results" class="tab-content">';
            html += this.renderPlaylistsResults(results.playlists.items);
        container.innerHTML = html;
        // Add click handlers for all result items
        this.addSearchResultHandlers();
    renderAllResults(results) {
        let html = '<div class="search-results-grid">';
        // Show top results from each category
            html += results.tracks.items.slice(0, 4).map(track => `
                <div class="search-result-item" data-type="track" data-uri="${track.uri}">
                    <img src="${track.album.images[0]?.url || ''}" alt="${track.name}" class="result-image">
                    <div class="result-title">${track.name}</div>
                    <div class="result-subtitle">${track.artists.map(a => a.name).join(', ')}</div>
                    <span class="result-type">Track</span>
            `).join('');
            html += results.artists.items.slice(0, 4).map(artist => `
                <div class="search-result-item" data-type="artist" data-uri="${artist.uri}">
                    <img src="${artist.images[0]?.url || ''}" alt="${artist.name}" class="result-image">
                    <div class="result-title">${artist.name}</div>
                    <div class="result-subtitle">Artist</div>
                    <span class="result-type">Artist</span>
            html += results.albums.items.slice(0, 4).map(album => `
                <div class="search-result-item" data-type="album" data-uri="${album.uri}">
                    <img src="${album.images[0]?.url || ''}" alt="${album.name}" class="result-image">
                    <div class="result-title">${album.name}</div>
                    <div class="result-subtitle">${album.artists.map(a => a.name).join(', ')}</div>
                    <span class="result-type">Album</span>
            html += results.playlists.items.slice(0, 4).map(playlist => `
                <div class="search-result-item" data-type="playlist" data-uri="${playlist.uri}">
                    <img src="${playlist.images[0]?.url || ''}" alt="${playlist.name}" class="result-image">
                    <div class="result-title">${playlist.name}</div>
                    <div class="result-subtitle">By ${playlist.owner.display_name}</div>
                    <span class="result-type">Playlist</span>
        return html;
    renderTracksResults(tracks) {
        return this.renderSearchResults(tracks, 'track', (track) => track.artists.map(a => a.name).join(', '));
    renderArtistsResults(artists) {
        return this.renderSearchResults(artists, 'artist', () => 'Artist');
    renderAlbumsResults(albums) {
        return this.renderSearchResults(albums, 'album', (album) => album.artists.map(a => a.name).join(', '));
    renderPlaylistsResults(playlists) {
        return this.renderSearchResults(playlists, 'playlist', (playlist) => `By ${playlist.owner.display_name}`);
     * Generic method to render search results for any type
     * @param {Array} items - Array of items to render
     * @param {string} type - The type of items (track, artist, album, playlist)
     * @param {Function} getSubtitle - Function to get the subtitle text for an item
     * @returns {string} HTML string for the search results
    renderSearchResults(items, type, getSubtitle) {
        return `
            <div class="search-results-grid">
                ${items.map(item => `
                    <div class="search-result-item" data-type="${type}" data-uri="${item.uri}">
                        <img src="${item.images?.[0]?.url || ''}" alt="${item.name}" class="result-image">
                        <div class="result-title">${item.name}</div>
                        <div class="result-subtitle">${getSubtitle(item)}</div>
                        <span class="result-type">${type.charAt(0).toUpperCase() + type.slice(1)}</span>
                `).join('')}
    addSearchResultHandlers() {
        // Add click handlers for search result items
        document.querySelectorAll('.search-result-item').forEach(item => {
                const type = item.dataset.type;
                const uri = item.dataset.uri;
                switch (type) {
                    case 'track':
                        this.player.playTrack(uri);
                    case 'artist':
                        // Could navigate to artist page or play top tracks
                        this.showSuccess('Artist selected: ' + item.querySelector('.result-title').textContent);
                    case 'album':
                        // Could navigate to album page or play album
                        this.showSuccess('Album selected: ' + item.querySelector('.result-title').textContent);
                    case 'playlist':
                        // Could navigate to playlist page or play playlist
                        this.showSuccess('Playlist selected: ' + item.querySelector('.result-title').textContent);
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
            // Show setup state
            if (connectBtn) connectBtn.style.display = 'flex';
            if (disconnectBtn) disconnectBtn.style.display = 'none';
            if (userDisplay) userDisplay.textContent = 'Not Connected';
    showConnectedHome() {
            <div class="welcome-section">
                <h2>Welcome to Spofyfy</h2>
                <p>You're successfully connected! Browse your music, create playlists, and enjoy your favorite tracks.</p>
                <div class="quick-actions">
                    <button class="quick-action-btn" onclick="window.spotifyApp.switchSection('search')">
                        <i class="fas fa-search"></i>
                        Search Music
                    </button>
                    <button class="quick-action-btn" onclick="window.spotifyApp.switchSection('library')">
                        <i class="fas fa-music"></i>
                        Your Library
                    <button class="quick-action-btn" onclick="window.spotifyApp.switchSection('playlists')">
                        <i class="fas fa-list"></i>
                        Playlists
        // Create and show error notification
        notification.className = 'error';
        notification.textContent = message;
        setTimeout(() => {
            notification.remove();
        }, 5000);
        // Create and show success notification
        notification.className = 'success';
       
        }, 3000);
        // Remove existing loading notification
        // Create and show loading notification
        notification.className = 'notification loading';
            <i class="fas fa-spinner fa-spin"></i>
        }    }
}
