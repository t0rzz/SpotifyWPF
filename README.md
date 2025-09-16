# SpofifyWPF - Cross-Platform Spotify Power Tools

> **Note**: This repository is a fork of the original project by MrPnut: https://github.com/MrPnut/SpotifyWPF. Our fork lives at https://github.com/t0rzz/SpotifyWPF.
>
> **🔔 Rebranding Notice**: As of v3.0.0, the project has been rebranded from "SpotifyWPF" to "SpofifyWPF" for consistent cross-platform branding. Both Windows and macOS applications now use the unified "SpofifyWPF" name.

Unofficial Spotify "power tools" for both Windows and macOS platforms. These apps offer advanced features like bulk playlist operations, device management, and enhanced metadata not available in the official Spotify clients.

## 🎯 Cross-Platform Overview

This repository contains **two different implementations** of the same core Spotify power tools:

### **SpofifyWPF** (Windows)
- **Platform**: Windows 10/11
- **Technology**: WPF (.NET 8) with WebView2
- **Distribution**: MSIX installer, portable EXE, or ZIP
- **UI**: Native Windows application with WebView2 for embedded web player

### **SpofifyWPF** (macOS)
- **Platform**: macOS 12+
- **Technology**: Swift with WKWebView (native macOS app)
- **Distribution**: DMG installer
- **UI**: Native macOS application with embedded WebView

### **🔄 Shared Core Features**
Despite different platforms and technologies, both apps provide identical core functionality:
- ✅ **Bulk playlist operations** (delete, unfollow, manage multiple playlists)
- ✅ **Device management** (list devices, transfer playback, "Play To" functionality)
- ✅ **Advanced playlist features** (pagination, sorting, context menus)
- ✅ **Artist management** (follow/unfollow, bulk operations)
- ✅ **OAuth authentication** with Spotify
- ✅ **Web Playback SDK integration** for local playback
- ✅ **Rate limiting and error handling**
- ✅ **Modern dark UI theme**

### 🎵 Playlist Management
- **Bulk Operations**: Delete, unfollow, or manage multiple playlists simultaneously
- **Advanced Sorting**: Sort by name, track count, owner, or creation date
- **Pagination**: Efficient loading of large playlist libraries
- **Context Menus**: Right-click playlists for quick actions (both platforms)
- **Search & Filter**: Find playlists quickly with real-time filtering

### 📱 Device Management
- **Device Discovery**: Automatically detect all available Spotify devices
- **Playback Transfer**: Switch playback between devices instantly
- **"Play To" Functionality**: Context menu integration for device selection
- **Active Device Tracking**: Visual indicators for current playback device
- **Volume Control**: Per-device volume management

### 🎨 Modern UI/UX
- **Dark Theme**: Consistent Spotify-branded dark interface
- **Responsive Design**: Optimized for different screen sizes
- **Web Playback SDK**: Local playback with high-quality streaming
- **Progress Tracking**: Smooth playback progress without network jitter
- **Loading States**: Clear feedback for all operations

### 🔐 Authentication & Security
- **OAuth 2.0**: Secure authentication with Spotify
- **Token Management**: Automatic refresh of access tokens
- **Rate Limiting**: Smart handling of API limits with retries
- **Error Handling**: Comprehensive error reporting and recovery

### 🎯 Platform-Specific Features

#### Windows (SpofifyWPF)
- **MSIX Packaging**: Modern Windows installer format
- **Portable EXE**: Single-file distribution option
- **WebView2 Integration**: Chromium-based embedded browser
- **Windows-specific UI**: Native Windows controls and styling

#### macOS (SpofifyWPF)
- **DMG Distribution**: Native macOS installer format
- **WKWebView Integration**: Safari-based embedded browser
- **Custom URL Schemes**: Native OAuth callback handling (`spofifywpf://callback`)
- **macOS-specific UI**: Native macOS controls and styling

## 📋 Recent Highlights (v2.0.0)

### Cross-Platform Features
- **Modern Player UI** using Web Playback SDK (local web player in both apps)
- **Top Tracks module** (Personalization API) with artwork
- **Unified device flow**: click-to-play promotes local player; "Play To" available from context menus
- **Smarter volume handling** for local vs remote devices
- **Smooth progress tracking** to avoid network jitter

### Windows-Specific (v1.1.4)
- **Right-click context menus** on playlists (Open in Spotify, Copy link, Unfollow/Delete)
- **MSIX packaging** for simpler Windows installation
- **Portable single-file EXE** option
- **Window title shows app version**

### macOS-Specific (v2.0.0)
- **Native macOS UI** with Swift and WKWebView
- **DMG distribution** for easy macOS installation
- **Custom URL scheme handling** for OAuth (`spofifywpf://callback`)
- **Native macOS context menus** with device submenus

### **SpotifyWPF** (Windows)
- **Platform**: Windows 10/11
- **Technology**: WPF (.NET 8) with WebView2
- **Distribution**: MSIX installer, portable EXE, or ZIP
- **UI**: Native Windows application with WebView2 for embedded web player

### **SpofifyWPF** (macOS)
- **Platform**: macOS 12+
- **Technology**: Swift with WKWebView (native macOS app)
- **Distribution**: DMG installer
- **UI**: Native macOS application with embedded WebView

### **🔄 Shared Core Features**
Despite different platforms and technologies, both apps provide identical core functionality:
- ✅ **Bulk playlist operations** (delete, unfollow, manage multiple playlists)
- ✅ **Device management** (list devices, transfer playback, "Play To" functionality)
- ✅ **Advanced playlist features** (pagination, sorting, context menus)
- ✅ **Artist management** (follow/unfollow, bulk operations)
- ✅ **OAuth authentication** with Spotify
- ✅ **Web Playback SDK integration** for local playback
- ✅ **Rate limiting and error handling**
- ✅ **Modern dark UI theme**

## 🚀 Quick Start

### Windows (SpofifyWPF)
```powershell
git clone https://github.com/t0rzz/SpotifyWPF.git
cd SpotifyWPF
dotnet restore
dotnet build .\SpotifyWPF\SpotifyWPF.csproj -c Release
dotnet run --project .\SpotifyWPF\SpotifyWPF.csproj
```

### macOS (SpofifyWPF)
```bash
git clone https://github.com/t0rzz/SpotifyWPF.git
cd SpotifyWPF/SpotifyWPF.macOS.native
chmod +x build_dmg.sh
./build_dmg.sh
# Install the generated .dmg file
```

## 📦 Downloads

Head to the [Releases page](https://github.com/t0rzz/SpotifyWPF/releases) and pick the appropriate artifact for your platform:

### Windows Downloads
- **MSIX installer**: Simplest install/uninstall experience on Windows 10/11
- **Portable EXE**: Single file, no installation required
- **ZIP**: Full build output for manual installation

### macOS Downloads
- **DMG installer**: Native macOS installer package
- Contains the complete SpofifyWPF application bundle

## 🔑 Spotify Authentication

Both applications use the same OAuth flow:
- Automatic browser-based authentication
- Secure token storage and refresh
- **Required scopes** (all are necessary for full functionality):
  - `user-read-private` - Read user profile information
  - `user-read-email` - Read user email address
    > **Note**: Both `user-read-private` and `user-read-email` are required for the same API endpoint (`GET /me` - Get Current User's Profile). Spotify's API design bundles email access with basic profile access for security reasons, even if your app doesn't display the email address.
  - `user-read-playback-state` - Read current playback state and devices
  - `user-modify-playback-state` - Control playback (play, pause, skip, volume)
  - `user-follow-read` - Read user's followed artists
  - `user-follow-modify` - Follow/unfollow artists
  - `user-library-read` - Read user's saved tracks and albums
  - `user-library-modify` - Save/remove tracks and albums
  - `user-top-read` - Read user's top tracks and artists
  - `playlist-read-private` - Read user's private playlists
  - `playlist-read-collaborative` - Read collaborative playlists
  - `playlist-modify-public` - Create and modify public playlists
  - `playlist-modify-private` - Create and modify private playlists
  - `streaming` - Stream music and control playback (required for Web Playback SDK)

**Note**: Create a Spotify app at https://developer.spotify.com/dashboard if you need custom configuration.

## 🛠️ Build Requirements

### Windows (SpofifyWPF)
- Windows 10/11 x64
- .NET SDK 8.0 or later
- Visual Studio 2022 (17.8+) with workloads:
  - .NET desktop development
  - Optional: MSIX Packaging Tools / Desktop Bridge
- Spotify account (required for OAuth authentication)

### macOS (SpofifyWPF)
- macOS 12.0 or later
- Xcode 14.0 or later
- Swift 5.7 or later
- Spotify account (required for OAuth authentication)

## 🏗️ Building from Source

### Windows Build
```powershell
git clone https://github.com/t0rzz/SpotifyWPF.git
cd SpotifyWPF
dotnet restore
dotnet build .\SpotifyWPF\SpotifyWPF.csproj -c Release
dotnet run --project .\SpotifyWPF\SpotifyWPF.csproj
```

### macOS Build
```bash
git clone https://github.com/t0rzz/SpotifyWPF.git
cd SpotifyWPF/SpotifyWPF.macOS.native
chmod +x build_dmg.sh
./build_dmg.sh
```

### CI/CD Builds
The repository includes GitHub Actions workflows that automatically build both platforms:
- **Windows**: WPF app, MSIX installer, portable EXE
- **macOS**: Native Swift app, DMG installer

All builds are triggered on pushes to `main`/`master` branches and releases are automatically created with cross-platform artifacts.

## 📋 Recent Highlights (v3.0.0)

### Cross-Platform Features
- **Modern Player UI** using Web Playback SDK (local web player in both apps)
- **Top Tracks module** (Personalization API) with artwork
- **Unified device flow**: click-to-play promotes local player; "Play To" available from context menus
- **Smarter volume handling** for local vs remote devices
- **Smooth progress tracking** to avoid network jitter

### Windows-Specific (v3.0.0)
- **Right-click context menus** on playlists (Open in Spotify, Copy link, Unfollow/Delete)
- **MSIX packaging** for simpler Windows installation
- **Portable single-file EXE** option
- **Window title shows app version**

### macOS-Specific (v3.0.0)
- **Native macOS UI** with Swift and WKWebView
- **DMG distribution** for easy macOS installation
- **Custom URL scheme handling** for OAuth (`spofifywpf://callback`)
- **Native macOS context menus** with device submenus

## 📁 Project Structure

### Windows (SpofifyWPF)
- `SpotifyWPF/` - Main WPF application (.NET 8)
- `SpotifyWPF.MSIX/` - MSIX packaging project
- `Service/` - Spotify API integration (wrappers and calls)
- `ViewModel/` - Presentation logic (MVVM pattern)
- `View/` - XAML views and components

### macOS (SpofifyWPF)
- `SpotifyWPF.macOS.native/` - Native macOS application
  - `SpotifyWPF/` - Swift application code
  - `SpotifyWPF.xcodeproj/` - Xcode project files
  - `WebApp/` - HTML/CSS/JavaScript web interface
    - `index.html` - Main web application
    - `css/styles.css` - Styling and themes
    - `js/` - JavaScript modules (app.js, player.js, etc.)
  - `build_dmg.sh` - DMG creation script

### Shared Components
- **Spotify API Integration**: Both apps use identical API calls and authentication
- **Web Playback SDK**: Same embedded player technology (WebView2/WKWebView)
- **OAuth Flow**: Consistent authentication experience
- **UI/UX Design**: Unified dark theme and interaction patterns

## 📋 Development Notes

### Cross-Platform Consistency
- **API Layer**: Both platforms use the same Spotify Web API endpoints
- **Authentication**: Identical OAuth 2.0 flow with PKCE
- **Features**: Core functionality is identical across platforms
- **UI Patterns**: Consistent interaction design and workflows

### Platform Differences
- **Build Process**: Windows uses .NET/MSBuild, macOS uses Xcode/Swift
- **Distribution**: MSIX vs DMG packaging
- **WebView**: WebView2 (Chromium) vs WKWebView (Safari)
- **Native Integration**: WPF controls vs SwiftUI/AppKit

## 🤝 Contributing

Both platforms welcome contributions! The core business logic is shared, so improvements to one platform often benefit both.

### Development Setup
1. **Windows**: Use Visual Studio 2022 with .NET desktop workload
2. **macOS**: Use Xcode 14+ with Swift 5.7+
3. **Cross-platform**: Web technologies (HTML/CSS/JS) work on both platforms

### Testing
- Test OAuth flows on both platforms
- Verify device management functionality
- Ensure playlist operations work consistently
- Check context menu behavior matches expectations

## 📄 License

This project is licensed under the GPL v3 License - see the [LICENSE](LICENSE) file for details.

## 🙏 Acknowledgments

- Original project by [MrPnut](https://github.com/MrPnut/SpotifyWPF)
- Spotify Web Playback SDK
- .NET and WPF communities
- Swift and macOS communities

---

*Last updated: September 16, 2025*
```

