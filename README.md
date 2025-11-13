# SpofifyWPF

> **Note**: This repository is a fork of the original project by MrPnut: https://github.com/MrPnut/SpotifyWPF. Our fork lives at https://github.com/t0rzz/SpotifyWPF.
>
> **🔔 Rebranding Notice**: As of v3.0.0, the project has been rebranded from "SpotifyWPF" to "SpofifyWPF" for consistent cross-platform branding. Both Windows and macOS applications now use the unified "SpofifyWPF" name.

A cross-platform Spotify management application with playlist management, device control, and playback features. Available for both Windows and macOS.

## 🎯 Features

This repository contains **two implementations** of the same core Spotify management toolkit:

### **SpofifyWPF** (Windows)
- **Platform**: Windows 10/11
- **Technology**: WPF (.NET 8) with WebView2
- **Distribution**: MSIX installer, portable EXE, or ZIP
- **UI**: Native Windows application with embedded web player

### **SpofifyWPF** (macOS)
- **Platform**: macOS 12+
- **Technology**: Swift with WKWebView
- **Distribution**: DMG installer
- **UI**: Native macOS application with embedded WebView

### **Core Features**
- ✅ **Bulk playlist operations** (delete, unfollow, manage multiple playlists)
- ✅ **Album management** (view saved albums, bulk deletion, sorting/filtering)
- ✅ **Playlist creation & management** (create custom playlists, add tracks, set images/descriptions)
- ✅ **Advanced search** (tracks, artists, albums, playlists with real-time results)
- ✅ **Device management** (list devices, transfer playback, "Play To" functionality)
- ✅ **Settings & configuration** (performance tuning, regional settings, window behavior)
- ✅ **Artist management** (follow/unfollow, bulk operations)
- ✅ **OAuth authentication** with Spotify
- ✅ **Web Playback SDK integration** for local playback
- ✅ **Rate limiting and error handling**
- ✅ **Modern dark UI theme**

### Playlist Management
- Mass playlist deletion with confirmation dialogs
- Bulk unfollow operations
- Advanced sorting by name, track count, owner, or creation date
- Pagination for large libraries
- Context menus for quick operations
- Real-time search and filtering

### Album Management
- View all saved albums in a sortable table
- Bulk album removal
- Sort by name, artist, track count, or release date
- Search and filter collection
- Direct playback from management interface

### Playlist Creation & Management
- Create custom playlists with names, descriptions, and cover images
- Advanced track search and selection across multiple content types
- Playlist generation based on genres with configurable parameters
- Track management within playlists (add, remove, reorder)
- Support for public, private, and collaborative playlists
- Generated playlist management and editing capabilities

### Search & Discovery
- Multi-type search across tracks, artists, albums, and playlists
- Real-time search results with pagination
- Context menu actions for search results
- Advanced filtering and sorting options

### Settings & Configuration
- Performance optimization (thread count configuration)
- Regional settings (default market/country)
- Window behavior (minimize-to-tray options)
- User preferences and customization

### Device Management & Playback
- Automatic device discovery
- Instant playback transfer between devices
- "Play To" functionality in context menus
- Visual indicators for active device
- Per-device volume control

### UI/UX
- Consistent Spotify-branded dark theme
- Responsive design for different screen sizes
- Web Playback SDK for high-quality local streaming
- Smooth progress tracking
- Clear loading states and feedback

### Security & Integration
- OAuth 2.0 authentication
- Automatic token refresh
- Smart API rate limiting with retries
- Comprehensive error handling

### Platform-Specific Features

#### Windows
- MSIX packaging for easy installation
- Portable EXE option
- WebView2 integration
- Native Windows UI controls

#### macOS
- DMG distribution
- WKWebView integration
- Custom URL scheme handling (`spofifywpf://callback`)
- Native macOS UI controls

## 🚀 Quick Start

### Windows
```powershell
git clone https://github.com/t0rzz/SpotifyWPF.git
cd SpotifyWPF
dotnet restore
dotnet build .\SpotifyWPF\SpotifyWPF.csproj -c Release
dotnet run --project .\SpotifyWPF\SpotifyWPF.csproj
```

### macOS
```bash
git clone https://github.com/t0rzz/SpotifyWPF.git
cd SpotifyWPF/SpotifyWPF.macOS.native
chmod +x build_dmg.sh
./build_dmg.sh
# Install the generated .dmg file
```

## 📦 Downloads

Head to the [Releases page](https://github.com/t0rzz/SpotifyWPF/releases) and download the appropriate artifact for your platform:

### Windows
- **MSIX installer**: Simplest install/uninstall experience on Windows 10/11
- **Portable EXE**: Single file, no installation required
- **ZIP**: Full build output for manual installation

### macOS
- **DMG installer**: Native macOS installer package
- Contains the complete SpofifyWPF application bundle

### Installing MSIX (developer build)

> These builds are signed with our **developer certificate** (`msix-signing.cer`).  
> Windows needs to trust this publisher once before installing.

**1) Download from the Release page**
- `SpofifyWPF-<version>.msix`
- `msix-signing.cer` (publisher certificate)

**2) Enable app sideloading (once)**
- Windows 10/11 → *Settings* → *Privacy & Security* → *For developers* → **Developer Mode** (or enable *Install apps from any source, including loose files*).

**3) Trust the certificate (Admin PowerShell)**
```powershell
# From the folder where you downloaded the files:
# Trust the publisher for app packages
Import-Certificate -FilePath .\msix-signing.cer -CertStoreLocation Cert:\LocalMachine\TrustedPeople

# Because it's self-signed, also trust as a root (often required)
Import-Certificate -FilePath .\msix-signing.cer -CertStoreLocation Cert:\LocalMachine\Root
```

## 🔑 Authentication

Both applications use OAuth 2.0 with the following required scopes:
- `user-read-private` - Read user profile information
- `user-read-email` - Read user email address
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

### Windows
- Windows 10/11 x64
- .NET SDK 8.0 or later
- Visual Studio 2022 (17.8+) with:
  - .NET desktop development workload
  - Optional: MSIX Packaging Tools
- Spotify account (required for OAuth)

### macOS
- macOS 12.0 or later
- Xcode 14.0 or later
- Swift 5.7 or later
- Spotify account (required for OAuth)

## 🏗️ Building from Source

### Windows
```powershell
git clone https://github.com/t0rzz/SpotifyWPF.git
cd SpotifyWPF
dotnet restore
dotnet build .\SpotifyWPF\SpotifyWPF.csproj -c Release
dotnet run --project .\SpotifyWPF\SpotifyWPF.csproj
```

### macOS
```bash
git clone https://github.com/t0rzz/SpotifyWPF.git
cd SpotifyWPF/SpotifyWPF.macOS.native
chmod +x build_dmg.sh
./build_dmg.sh
```

### CI/CD
The repository includes GitHub Actions workflows that automatically build both platforms. All builds are triggered on pushes to `main`/`master` branches and releases are automatically created with cross-platform artifacts.

## 📋 Recent Changes (v4.0.0)

### Cross-Platform Features
- **Enhanced Windows User Experience**: Global media key support, system tray functionality, and improved window management
- **System Integration**: Seamless system tray integration with playback controls and window state preservation
- **Global Media Keys**: System-wide hotkey support for play/pause, next track, previous track, and volume control
- **Personalized User Experience**: Consistent user greeting display across all views with proper user name integration
- **Playlist Manager**: Complete playlist creation and management system with advanced features
- **Settings & Configuration**: Comprehensive settings window for performance and behavior customization
- **Advanced Search**: Multi-type search functionality with real-time results and context menus

### Windows-Specific (v4.0.0)
- **Global Media Key Support**: Control playback from any application using system-wide hotkeys
- **System Tray Integration**: Minimize to tray with playback controls and window state preservation
- **Playlist Creation Tools**: Full-featured playlist manager with image upload and track selection
- **User Settings**: Configurable performance settings, regional preferences, and window behavior
- **Enhanced Albums View**: Personalized greeting with user name display instead of generic text

## 📁 Project Structure

### Windows
- `SpotifyWPF/` - Main WPF application (.NET 8)
- `SpotifyWPF.MSIX/` - MSIX packaging project
- `Service/` - Spotify API integration
- `ViewModel/` - Presentation logic (MVVM pattern)
- `View/` - XAML views and components

### macOS
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

## 📄 License

This project is licensed under the GPL v3 License - see the [LICENSE](LICENSE) file for details.

## 🙏 Acknowledgments

- Original project by [MrPnut](https://github.com/MrPnut/SpotifyWPF)
- Spotify Web Playback SDK
- .NET and WPF communities
- Swift and macOS communities

---

*Last updated: November 14, 2025*
