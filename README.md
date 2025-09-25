# SpofifyWPF - Advanced Spotify Playlist Management & Bulk Operations Tool

> **Note**: This repository is a fork of the original project by MrPnut: https://github.com/MrPnut/SpotifyWPF. Our fork lives at https://github.com/t0rzz/SpotifyWPF.
>
> **🔔 Rebranding Notice**: As of v3.0.0, the project has been rebranded from "SpotifyWPF" to "SpofifyWPF" for consistent cross-platform branding. Both Windows and macOS applications now use the unified "SpofifyWPF" name.

**Powerful Spotify playlist management tool** for bulk playlist operations, mass playlist deletion, and advanced Spotify automation. Clean up your Spotify library with efficient playlist management features including bulk unfollow, mass playlist operations, and comprehensive playlist organization tools for both Windows and macOS.

## 🎯 Advanced Spotify Playlist Management Features

This repository contains **two powerful implementations** of the same core Spotify playlist management and bulk operations toolkit:

### **SpofifyWPF** (Windows) - Spotify Playlist Organizer
- **Platform**: Windows 10/11
- **Technology**: WPF (.NET 8) with WebView2
- **Distribution**: MSIX installer, portable EXE, or ZIP
- **UI**: Native Windows application with WebView2 for embedded web player

### **SpofifyWPF** (macOS) - macOS Spotify Power Tools
- **Platform**: macOS 12+
- **Technology**: Swift with WKWebView (native macOS app)
- **Distribution**: DMG installer
- **UI**: Native macOS application with embedded WebView

### **🔄 Core Spotify Bulk Operations & Playlist Management**
Despite different platforms and technologies, both apps provide identical advanced Spotify playlist management functionality:
- ✅ **Bulk playlist deletion** and mass playlist operations (delete, unfollow, manage multiple playlists)
- ✅ **Spotify playlist cleanup** tools with advanced filtering and sorting
- ✅ **Device management** (list devices, transfer playback, "Play To" functionality)
- ✅ **Advanced playlist features** (pagination, sorting, context menus)
- ✅ **Artist management** (follow/unfollow, bulk operations)
- ✅ **OAuth authentication** with Spotify
- ✅ **Web Playback SDK integration** for local playback
- ✅ **Rate limiting and error handling**
- ✅ **Modern dark UI theme**

### 🎵 Spotify Playlist Management & Bulk Operations
- **Mass playlist deletion**: Delete multiple playlists simultaneously with confirmation dialogs
- **Bulk playlist operations**: Unfollow, delete, or manage multiple playlists at once
- **Advanced sorting**: Sort playlists by name, track count, owner, or creation date
- **Pagination**: Efficient loading of large playlist libraries for playlist cleanup
- **Context menus**: Right-click playlists for quick bulk operations (both platforms)
- **Search & filter**: Find playlists quickly with real-time filtering for playlist organization

### 📱 Spotify Device Management & Playback Control
- **Device discovery**: Automatically detect all available Spotify devices for playback transfer
- **Playback transfer**: Switch playback between devices instantly during playlist management
- **"Play To" functionality**: Context menu integration for device selection in bulk operations
- **Active device tracking**: Visual indicators for current playback device
- **Volume control**: Per-device volume management during Spotify automation

### 🎨 Modern UI/UX for Spotify Power Tools
- **Dark theme**: Consistent Spotify-branded dark interface for playlist management
- **Responsive design**: Optimized for different screen sizes during bulk operations
- **Web Playback SDK**: Local playback with high-quality streaming
- **Progress tracking**: Smooth playback progress without network jitter
- **Loading states**: Clear feedback for all playlist operations and bulk tasks

### 🔐 Secure Spotify Authentication & API Integration
- **OAuth 2.0**: Secure authentication with Spotify for playlist management
- **Token management**: Automatic refresh of access tokens for uninterrupted bulk operations
- **Rate limiting**: Smart handling of API limits with retries during mass playlist operations
- **Error handling**: Comprehensive error reporting and recovery for Spotify automation

### 🎯 Platform-Specific Spotify Playlist Tools

#### Windows (SpofifyWPF) - Windows Spotify Power Tools
- **MSIX packaging**: Modern Windows installer format for easy playlist management setup
- **Portable EXE**: Single-file distribution option for bulk playlist operations
- **WebView2 integration**: Chromium-based embedded browser for Spotify automation
- **Windows-specific UI**: Native Windows controls and styling for playlist cleanup

#### macOS (SpofifyWPF) - macOS Spotify Playlist Organizer
- **DMG distribution**: Native macOS installer format for playlist management
- **WKWebView integration**: Safari-based embedded browser for Spotify power tools
- **Custom URL schemes**: Native OAuth callback handling (`spofifywpf://callback`)
- **macOS-specific UI**: Native macOS controls and styling for bulk operations

##  Quick Start - Spotify Playlist Management Setup

### Windows (SpofifyWPF) - Windows Spotify Bulk Operations
```powershell
git clone https://github.com/t0rzz/SpotifyWPF.git
cd SpotifyWPF
dotnet restore
dotnet build .\SpotifyWPF\SpotifyWPF.csproj -c Release
dotnet run --project .\SpotifyWPF\SpotifyWPF.csproj
```

### macOS (SpofifyWPF) - macOS Spotify Playlist Organizer
```bash
git clone https://github.com/t0rzz/SpotifyWPF.git
cd SpotifyWPF/SpotifyWPF.macOS.native
chmod +x build_dmg.sh
./build_dmg.sh
# Install the generated .dmg file for advanced playlist management
```

## 📦 Downloads - Spotify Power Tools Installation

Head to the [Releases page](https://github.com/t0rzz/SpotifyWPF/releases) and pick the appropriate artifact for your platform to start bulk playlist operations:

### Windows Downloads - Windows Spotify Playlist Management
- **MSIX installer**: Simplest install/uninstall experience on Windows 10/11 for playlist cleanup
- **Portable EXE**: Single file, no installation required for bulk operations
- **ZIP**: Full build output for manual installation of Spotify automation tools

### macOS Downloads - macOS Spotify Power Tools
- **DMG installer**: Native macOS installer package for advanced playlist management
- Contains the complete SpofifyWPF application bundle for mass playlist deletion

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

## 🛠️ Build Requirements - Spotify Playlist Management Development

### Windows (SpofifyWPF) - Windows Spotify Power Tools Development
- Windows 10/11 x64
- .NET SDK 8.0 or later
- Visual Studio 2022 (17.8+) with workloads:
  - .NET desktop development
  - Optional: MSIX Packaging Tools / Desktop Bridge
- Spotify account (required for OAuth authentication in bulk operations)

### macOS (SpofifyWPF) - macOS Spotify Playlist Organizer Development
- macOS 12.0 or later
- Xcode 14.0 or later
- Swift 5.7 or later
- Spotify account (required for OAuth authentication in playlist management)

## 🏗️ Building from Source - Spotify Bulk Operations Development

### Windows Build - Windows Spotify Playlist Tools
```powershell
git clone https://github.com/t0rzz/SpotifyWPF.git
cd SpotifyWPF
dotnet restore
dotnet build .\SpotifyWPF\SpotifyWPF.csproj -c Release
dotnet run --project .\SpotifyWPF\SpotifyWPF.csproj
```

### macOS Build - macOS Spotify Power Tools
```bash
git clone https://github.com/t0rzz/SpotifyWPF.git
cd SpotifyWPF/SpotifyWPF.macOS.native
chmod +x build_dmg.sh
./build_dmg.sh
```

### CI/CD Builds - Automated Spotify Playlist Management
The repository includes GitHub Actions workflows that automatically build both platforms for playlist cleanup:
- **Windows**: WPF app, MSIX installer, portable EXE for bulk operations
- **macOS**: Native Swift app, DMG installer for mass playlist deletion

All builds are triggered on pushes to `main`/`master` branches and releases are automatically created with cross-platform Spotify automation artifacts.

## 📋 Recent Highlights (v3.0.4)

### Cross-Platform Features
- **Modern Player UI** using Web Playback SDK (local web player in both apps for playlist management)
- **Top Tracks module** (Personalization API) with artwork for playlist organization
- **Unified device flow**: click-to-play promotes local player; "Play To" available from context menus
- **Smarter volume handling** for local vs remote devices during bulk playlist operations
- **Smooth progress tracking** to avoid network jitter in Spotify automation
- **Context menu auto-close**: Context menus now automatically close after selection for better UX

### Windows-Specific (v3.0.4)
- **Right-click context menus** on playlists (Open in Spotify, Copy link, mass playlist deletion)
- **MSIX packaging** for simpler Windows installation of playlist management tools
- **Portable single-file EXE** option for bulk playlist operations
- **Window title shows app version** for Spotify playlist organizer

### macOS-Specific (v3.0.4)
- **Native macOS UI** with Swift and WKWebView for advanced playlist management
- **DMG distribution** for easy macOS installation of Spotify power tools
- **Custom URL scheme handling** for OAuth (`spofifywpf://callback`) in bulk operations
- **Native macOS context menus** with device submenus for playlist automation
- **HTTP-based OAuth callbacks** for improved authorization flow reliability

## �📁 Project Structure - Spotify Playlist Management Architecture

### Windows (SpofifyWPF) - Windows Spotify Bulk Operations
- `SpotifyWPF/` - Main WPF application (.NET 8) for playlist management
- `SpotifyWPF.MSIX/` - MSIX packaging project for bulk operations
- `Service/` - Spotify API integration (wrappers and calls for playlist cleanup)
- `ViewModel/` - Presentation logic (MVVM pattern for Spotify automation)
- `View/` - XAML views and components for playlist organizer

### macOS (SpofifyWPF) - macOS Spotify Power Tools
- `SpotifyWPF.macOS.native/` - Native macOS application for mass playlist deletion
  - `SpotifyWPF/` - Swift application code for playlist management
  - `SpotifyWPF.xcodeproj/` - Xcode project files for bulk operations
  - `WebApp/` - HTML/CSS/JavaScript web interface for Spotify automation
    - `index.html` - Main web application for playlist cleanup
    - `css/styles.css` - Styling and themes for Spotify power tools
    - `js/` - JavaScript modules (app.js, player.js, etc.) for bulk operations
  - `build_dmg.sh` - DMG creation script for macOS playlist organizer

### Shared Components - Cross-Platform Spotify Playlist Management
- **Spotify API Integration**: Both apps use identical API calls and authentication for bulk operations
- **Web Playback SDK**: Same embedded player technology (WebView2/WKWebView) for playlist management
- **OAuth Flow**: Consistent authentication experience for Spotify automation
- **UI/UX Design**: Unified dark theme and interaction patterns for mass playlist deletion

## 📋 Development Notes - Spotify Bulk Operations Development

### Cross-Platform Consistency - Unified Playlist Management
- **API Layer**: Both platforms use the same Spotify Web API endpoints for bulk operations
- **Authentication**: Identical OAuth 2.0 flow with PKCE for playlist cleanup
- **Features**: Core functionality is identical across platforms for Spotify automation
- **UI Patterns**: Consistent interaction design and workflows for mass playlist operations

### Platform Differences - Spotify Power Tools Architecture
- **Build Process**: Windows uses .NET/MSBuild, macOS uses Xcode/Swift for playlist management
- **Distribution**: MSIX vs DMG packaging for bulk operations
- **WebView**: WebView2 (Chromium) vs WKWebView (Safari) for Spotify automation
- **Native Integration**: WPF controls vs SwiftUI/AppKit for playlist organizer

## 🤝 Contributing - Spotify Playlist Management Development

Both platforms welcome contributions! The core business logic is shared, so improvements to one platform often benefit both in bulk playlist operations.

### Development Setup - Spotify Power Tools Development
1. **Windows**: Use Visual Studio 2022 with .NET desktop workload for playlist cleanup
2. **macOS**: Use Xcode 14+ with Swift 5.7+ for mass playlist deletion
3. **Cross-platform**: Web technologies (HTML/CSS/JS) work on both platforms for Spotify automation

### Testing - Spotify Bulk Operations Testing
- Test OAuth flows on both platforms for playlist management
- Verify device management functionality in bulk operations
- Ensure playlist operations work consistently for mass playlist deletion
- Check context menu behavior matches expectations for Spotify automation

## 📄 License - Spotify Playlist Management Tool License

This project is licensed under the GPL v3 License - see the [LICENSE](LICENSE) file for details.

## 🙏 Acknowledgments - Spotify Power Tools Acknowledgments

- Original project by [MrPnut](https://github.com/MrPnut/SpotifyWPF) for pioneering Spotify playlist management
- Spotify Web Playback SDK for enabling advanced playlist operations
- .NET and WPF communities for Windows bulk operations framework
- Swift and macOS communities for macOS playlist cleanup tools

---

*Last updated: September 23, 2025*

**Keywords**: Spotify playlist management, bulk playlist operations, mass playlist deletion, Spotify power tools, playlist cleanup, Spotify automation, playlist organizer, bulk unfollow playlists, Spotify bulk operations, playlist management tool
```
