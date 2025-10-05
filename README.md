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
- ✅ **Device management** (list devices, transfer playback, "Play To" functionality)
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

## 📋 Recent Changes (v3.0.7)

### Cross-Platform Features
- **Album Management Feature**: Complete album library management with saved albums viewing and bulk operations
- **Enhanced UI Consistency**: Unified styling between playlists and albums sections with proper table layouts
- **User Profile Integration**: Display user profile name and avatar in header when available
- **Improved Error Handling**: Better handling of DELETE API responses and broken/missing images
- **Responsive Design**: Enhanced mobile device support and layout improvements

### Windows-Specific (v3.0.7)
- **Bulk Album Operations**: Delete multiple albums with confirmation dialogs and progress feedback
- **Sortable Album Table**: Columns for name, artist, track count, and release date
- **Real-time Search**: Album filtering and search functionality
- **Album Playback**: Direct playback integration with Spotify Web Playback SDK

### macOS-Specific (v3.0.7)
- **Album Management UI**: Consistent table layouts and styling for album management
- **Profile Display**: User avatar and name integration in macOS interface
- **Enhanced Error Handling**: Improved API response handling for album operations

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

*Last updated: October 1, 2025*
