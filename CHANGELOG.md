# Changelog

All notable changes to this project will be documented in this file.

The format is based on Keep a Changelog and this project adheres to Semantic Versioning.

## [3.0.0] - 2025-09-16
### 🎯 Major Release: Cross-Platform Spotify Power Tools

**Breaking Change**: This release transforms the project from a Windows-only application to a **cross-platform Spotify power tools suite** with native implementations for both Windows and macOS.

### ✨ Added
- **🎵 Spofify (macOS)**: Complete native macOS application with identical feature set to Windows version
  - Native Swift application with WKWebView for embedded web interface
  - Custom URL scheme handling (`spofifywpf://callback`) for OAuth authentication
  - DMG distribution package for easy macOS installation
  - Native macOS UI with AppKit integration
- **🔄 Cross-Platform CI/CD**: GitHub Actions workflow now builds both platforms simultaneously
  - Windows: WPF app, MSIX installer, portable EXE
  - macOS: Native Swift app, DMG installer
  - Unified release process with cross-platform artifacts
- **📱 Unified Device Management**: Identical device discovery and "Play To" functionality across platforms
  - Dynamic device detection and status tracking
  - Seamless playback transfer between devices
  - Context menu integration for device selection
- **🎨 Consistent UI/UX**: Unified dark theme and interaction patterns across both platforms
  - Spotify-branded dark interface
  - Consistent playlist management and bulk operations
  - Identical OAuth authentication flow

### 🔧 Changed
- **📦 Distribution Strategy**: Dual-platform releases with platform-specific installers
  - Windows: MSIX + portable EXE + ZIP
  - macOS: DMG installer
- **🏗️ Build System**: Cross-platform build pipeline supporting both .NET and Xcode
- **📚 Documentation**: Unified README covering both Windows and macOS implementations
- **🎯 Feature Parity**: All core features now available on both platforms
  - Bulk playlist operations (delete, unfollow, manage)
  - Advanced device management and playback transfer
  - Web Playback SDK integration
  - Rate limiting and error handling
- **🏷️ Rebranding**: Project renamed from "SpotifyWPF" to "SpofifyWPF" for consistent cross-platform branding
  - Windows app: "SpotifyWPF" → "SpofifyWPF"
  - macOS app: "Spofify" → "SpofifyWPF" (unified naming)
  - All references updated throughout codebase and documentation

### 🎵 Core Features (Cross-Platform)
Both SpotifyWPF (Windows) and Spofify (macOS) now provide identical functionality:
- **Playlist Management**: Bulk operations, sorting, pagination, context menus
- **Device Integration**: "Play To" functionality, playback transfer, volume control
- **Authentication**: OAuth 2.0 with secure token management
- **Web Playback**: Local playback with Spotify Web Playback SDK
- **Artist Management**: Follow/unfollow operations with bulk support
- **Search**: Multi-category search with filtering
- **Modern UI**: Dark theme with responsive design

### 📋 Platform-Specific Implementations
- **Windows (SpotifyWPF)**: WPF (.NET 8) + WebView2 + MSIX packaging
- **macOS (Spofify)**: Swift + WKWebView + DMG packaging

### 🚀 Migration Notes
- **Version Reset**: Both platforms start at v3.0.0 for unified versioning
- **Feature Parity**: All existing Windows features now available on macOS
- **Backward Compatibility**: Windows users can continue using existing workflows
- **New Users**: macOS users now have full access to Spotify power tools

---

## [2.0.0] - 2025-09-12
### Highlights
- Modern Player UI based on WebView2 hosting the Spotify Web Playback SDK.
- "Top Tracks" module powered by Spotify's Personalization API (with album artwork).
- Unified device experience: click-to-play promotes the local Web Player when no active device is present; "Play to" remains available from context menus.
- Smarter volume handling that distinguishes between the local Web Player and remote devices.
- Smoother, latency-resistant progress tracking for local playback.

Note: Internal development fixes made during the 2.0 cycle are not itemized here.

## [1.1.4] - 2025-09-08
### Added
- Added “Play to” context menu on track rows (Playlists and Search) to play a selected track on a selected device.
- Active device shows a checkmark in device submenus; device list refreshes on submenu open.
- Consistent right-side chevrons and padding for submenu items (Windows Explorer style).

### Changed
- Unified the binding approach for context menus using a BindingProxy (Freezable) and a MultiJoinConverter for robust CommandParameter construction.

### Fixed
- Resolved null CommandParameter issue in “Play to” actions caused by ContextMenu/Popup data context boundaries.
- Fixed XAML issues (namespace prefixes, RelativeSource markup) and stabilized ContextMenu resource scopes.

### Packaging/Versioning
- Bumped app version to v1.1.4 (AssemblyVersion 1.1.4.0, FileVersion 1.1.4.0, MSIX 1.1.4.0).
- AppInstaller version aligned for consistency.

## [1.1.3] - 2025-09-08
### Added
- Devices menu: list devices, highlight active, transfer playback.
- Help menu: About dialog (with version/author/repo) and Logout.
- Users/Artists tab: load all followed artists with cursor-based pagination and parallel workers; unfollow selected; context menu (Open/Copy/Unfollow); detailed API logging.
- F5 refresh: reloads the active tab (Playlists vs Users/Artists).

### Changed
- Global Button template: enforce dark text color on green/red buttons for readability.
- TabItem visuals: more rounded, pill-like headers with subtle border on selection.

### Fixed
- Context menus operate on full selection:
	- Playlists “Unfollow (Delete)” applies to all selected items; confirmation shows name for single or count for multiple.
	- Artists “Unfollow” applies to all selected items; confirmation shows correct count.

### Packaging/Versioning
- Bumped app version to v1.1.3 (AssemblyVersion 1.1.3.0, FileVersion 1.1.3.0, MSIX 1.1.3.0).
- AppInstaller version aligned for consistency.

## [1.1.2] - 2025-09-07
### Added
- Devices menu: discover devices, show active device, transfer playback.
- Help menu with About and Logout.
- About dialog enriched with app version (AssemblyInformationalVersion), author, and repository link.

### Changed
- Window title shows the app version from AssemblyInformationalVersion.
- Devices submenu refreshes on open for on-demand discovery.
- README updated with Devices/Help documentation.
- Simplified MSIX packaging approach (minimal WAP), clarified release artifacts.

### Fixed
- Logout navigation no longer depends on a service locator; uses constructor injection.
- Disambiguated MessageBox enum usage in view models.

## [1.1.0] - 2025-09-05
### Changed
- Repository marked as a fork of the original project (MrPnut/SpotifyWPF); links to both repositories added in README.
- All repository links updated to point to https://github.com/t0rzz/SpotifyWPF where applicable.
- Project version bumped to 1.1.0.
- Unified context menu wording to a single entry: “Unfollow (Delete)”.
- Improved context menu styling (colors, padding, hover) to match ModernTheme.

### Added
- Right-click context menu on Playlists with actions: Load Tracks, Open in Spotify, Copy Link, Unfollow (Delete).

### Fixed
- Fixed startup crash due to duplicate ContextMenu style definitions in ModernTheme.
- Fixed TabItem template in ModernTheme so headers render correctly as tabs (previously showed grid headers instead of tabs).
- Search page cleaned up and restored with four tabs: Tracks, Artists, Albums, Playlists.
- Unified DataGrid styling under ModernTheme; removed hardcoded AlternatingRowBackground to improve readability and reduce eye strain.
- Updated component grids to use decoupled DTO fields (e.g., Artists, AlbumName, FollowersTotal, OwnerName, TracksTotal) for consistent display.
- Context menu "Unfollow (Delete)" now applies to all selected playlists; confirmation dialog shows the playlist name for single selection or the number of playlists for multiple selections.

### Documentation
- README updated to reflect fork origin and version start at 1.1.0.

## [1.0.0] - 2025-09-05
### Added
- Guide in English for building locally and preparing a GitHub release (README.md).
- Start/Stop buttons for playlist loading on the Playlists page.
- Batched logging with size limitation to avoid UI slowdowns during long operations.

### Fixed
- Population of the “Created By” and “# Tracks” columns in the playlists DataGrid using decoupled DTOs.
- Improved retry handling and rate limiting (HTTP 429) during playlist loading.

### Changed
- Consolidated version metadata in `SpotifyWPF.csproj`: `Version=1.0.0`, `AssemblyVersion=1.0.0.0`, `FileVersion=1.0.0.0`.

---

Release notes:
- Build: see sections "Requirements to build locally" and "Build and run" in the README.
- GitHub release: created automatically by CI when pushing an update; the release attaches the zipped Release binaries.
