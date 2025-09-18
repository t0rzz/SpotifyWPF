# Spofify Setup Guide

## 🚀 Quick Start

### 1. Get Spotify API Credentials

1. Go to [Spotify Developer Dashboard](https://developer.spotify.com/dashboard)
2. Create a new app (or use existing one)
3. Copy your **Client ID** (looks like: `1234567890abcdef1234567890abcdef`)

### 2. Configure the App

**Option A: Use Browser Console**
```javascript
// Open browser console (F12) and run:
setSpotifyClientId("your_client_id_here")
```

**Option B: Edit config.js**
```javascript
// Edit js/config.js and replace:
clientId: 'your_client_id_here',  // ← Replace with your actual Client ID
```

### 3. Set Up Redirect URI

In your Spotify app dashboard:
- Add redirect URI: `spofifywpf://callback`
- Save changes

### 4. Test Connection

1. Refresh the web app
2. Click "Connect to Spotify" button
3. Complete authorization in browser
4. App should connect successfully

## 🔧 Troubleshooting

### Button Not Working?
- Open browser console (F12)
- Run: `debugSpotifyConnection()`
- Check for error messages

### Still Having Issues?
- Run: `checkSpotifyConfig()`
- Make sure Client ID is not placeholder value
- Verify redirect URI matches exactly

### Console Commands
```javascript
checkSpotifyConfig()        // Check configuration
debugSpotifyConnection()    // Debug connection status
setSpotifyClientId("id")    // Set client ID
```

## 📋 What You Need

- ✅ Spotify Premium account (for playback)
- ✅ Spotify Developer App
- ✅ Client ID from dashboard
- ✅ Redirect URI: `spofifywpf://callback`

## 🎯 Next Steps

Once connected:
- Browse your playlists
- Control playback
- Manage your library
- Use device switching

---