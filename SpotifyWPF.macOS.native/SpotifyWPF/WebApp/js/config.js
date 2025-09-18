// Spofify Configuration
// IMPORTANT: Replace these placeholder values with your actual Spotify Developer App credentials
// Get these from: https://developer.spotify.com/dashboard

const SPOTIFY_CONFIG = {
    // 🔑 Your Spotify App Client ID (required)
    // Get this from your Spotify Developer App dashboard
    // It looks like: '1234567890abcdef1234567890abcdef'
    clientId: 'your_client_id_here',

    // 🔐 Your Spotify App Client Secret (NOT needed for desktop apps with PKCE)
    // This can be left as placeholder for desktop apps
    clientSecret: 'your_client_secret_here',

    // 🎯 Redirect URI (must match your Spotify Developer App exactly)
    // For native macOS app: use 'spofifywpf://callback'
    // ✅ CONFIRMED: Custom URL schemes are explicitly supported by Spotify
    // For web testing: use 'http://127.0.0.1:8080/callback.html'
    redirectUri: 'spofifywpf://callback', // ✅ CORRECT for desktop app

    // 📋 Spotify API permissions (scopes) needed for the app
    scopes: [
        'streaming',              // Play music and control playback
        'user-read-email',        // Read user email address
        'user-read-private',      // Read user profile information
        'user-library-read',      // Read user's saved tracks and albums
        'user-library-modify',    // Save/remove tracks and albums
        'user-read-playback-state',    // Read current playback state and devices
        'user-modify-playback-state',  // Control playback (play, pause, skip, volume)
        'playlist-read-private',       // Read user's private playlists
        'playlist-read-collaborative', // Read collaborative playlists
        'playlist-modify-public',      // Create and modify public playlists
        'playlist-modify-private'      // Create and modify private playlists
    ]
};

// Make config available globally
window.SPOTIFY_CONFIG = SPOTIFY_CONFIG;

// Quick setup instructions
console.log('🎵 Spofify Setup Instructions:');
console.log('1. Go to https://developer.spotify.com/dashboard');
console.log('2. Create a new app or use existing one');
console.log('3. Copy your Client ID');
console.log('4. In browser console, run: setSpotifyClientId("your_client_id_here")');
console.log('5. Refresh the page');
console.log('6. Click "Connect to Spotify" button');
console.log('');
console.log('🔧 Debug commands:');
console.log('- checkSpotifyConfig() - Check configuration');
console.log('- debugSpotifyConnection() - Debug connection status');
console.log('- setSpotifyClientId("your_id") - Set client ID');

// Development helper: Check if config is properly set up
window.checkSpotifyConfig = function() {
    const issues = [];

    if (SPOTIFY_CONFIG.clientId === 'your_client_id_here') {
        issues.push('❌ Client ID not configured - Get this from https://developer.spotify.com/dashboard');
    }

    if (SPOTIFY_CONFIG.clientSecret === 'your_client_secret_here') {
        issues.push('⚠️ Client Secret not configured (optional for desktop apps)');
    }

    if (issues.length === 0) {
        console.log('✅ Spotify configuration looks good!');
        console.log('Client ID:', SPOTIFY_CONFIG.clientId.substring(0, 10) + '...');
        console.log('Redirect URI:', SPOTIFY_CONFIG.redirectUri);
        console.log('Scopes:', SPOTIFY_CONFIG.scopes.length);
        return true;
    } else {
        console.warn('⚠️ Spotify configuration issues:');
        issues.forEach(issue => console.warn('  ' + issue));
        console.log('💡 To fix: Go to https://developer.spotify.com/dashboard and create an app');
        return false;
    }
};