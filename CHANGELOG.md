# Changelog

All notable changes to this project will be documented in this file.

The format is based on Keep a Changelog and this project adheres to Semantic Versioning.

## [3.0.5] - 2025-09-30

### Added

**Album Management Feature**
- Complete album library management with saved albums viewing and bulk operations
- Sortable album table with columns for name, artist, track count, and release date
- Bulk album deletion with confirmation dialogs and progress feedback
- Real-time album search and filtering functionality
- Album playback integration with Spotify Web Playback SDK
- User profile avatar display in header when available
- Enhanced UI consistency between playlists and albums sections
- Fixed DELETE API response handling for empty response bodies

### Enhanced

**User Interface Improvements**
- User profile name now displays instead of generic "Connected" text
- Profile avatar shows in header when user has profile picture
- Improved responsive design for mobile devices
- Better error handling for broken or missing images

## [3.0.4] - 2025-09-24

### Added

**macOS Context Menu UX Enhancement**
- Context menus now automatically close after selecting "Play" or any device from "Play To" submenu
- Improved user experience following standard macOS application behavior
- Cleaner interface with no lingering context menus after actions

**OAuth Callback Handling Improvements**
- Improved HTTP-based callback handling for more reliable Spotify authorization
- Better cross-frame communication between callback page and main application
- Enhanced error handling and user feedback during OAuth flow

## [3.0.3] - 2025-09-23

### Fixed

**macOS App Branding and Naming**
- Corrected Xcode project PRODUCT_NAME from "$(TARGET_NAME)" to "Spofify" in both Debug and Release configurations
- Ensured future builds produce "Spofify.app" instead of "SpotifyWPF.app"
- Verified app displays with correct name in Applications folder, Dock, and system interfaces
- Maintained consistent branding across all macOS app instances

## [3.0.2] - 2025-09-22

### Fixed

**CI/CD Pipeline Fixes**
- Corrected scheme name from "SpofifyWPF" to "SpotifyWPF" in build_dmg.sh
- Updated DMG creation and verification to use consistent "SpotifyWPF" naming
- Fixed CI workflow to properly rename DMG artifact for release

## [3.0.1] - 2025-09-18

### Fixed

**macOS App Window Visibility**
- Added `NSApp.activate(ignoringOtherApps: true)` to properly activate the application
- Added `applicationDidBecomeActive` handler to ensure window visibility when app becomes active
- Enhanced `loadWebApp()` method with comprehensive debugging output
- Fixed window activation timing to prevent hidden app issues

**Cross-Platform Version Synchronization**
- Synchronized version numbers across all platforms
- Windows (.csproj): AssemblyVersion, FileVersion, Version → 3.0.1
- macOS (Xcode): MARKETING_VERSION → 3.0.1
- MSIX Package: Version → 3.0.1.0
- Info.plist: CFBundleShortVersionString → 3.0.1
- WebApp HTML: Title updated to v3.0.1

## [3.0.0] - 2025-09-16

### Added

**Breaking Change**: This release transforms the project from a Windows-only application to a **cross-platform Spotify power tools suite** with native implementations for both Windows and macOS.

- **Spofify (macOS)**: Complete native macOS application with identical feature set to Windows version
  - Native Swift application with WKWebView for embedded web interface
  - Custom URL scheme handling (`spofifywpf://callback`) for OAuth authentication
  - DMG distribution package for easy macOS installation
  - Native macOS UI with AppKit integration
- **Cross-Platform CI/CD**: GitHub Actions workflow now builds both platforms simultaneously
  - Windows: WPF app, MSIX installer, portable EXE
  - macOS: Native Swift app, DMG installer
  - Unified release process with cross-platform artifacts
- **Unified Device Management**: Identical device discovery and "Play To" functionality across platforms
  - Dynamic device detection and status tracking
  - Seamless playback transfer between devices
  - Context menu integration for device selection
- **Consistent UI/UX**: Unified dark theme and interaction patterns across both platforms
  - Spotify-branded dark interface
  - Consistent playlist management and bulk operations
  - Identical OAuth authentication flow

### Changed

- **Distribution Strategy**: Dual-platform releases with platform-specific installers
  - Windows: MSIX + portable EXE + ZIP
  - macOS: DMG installer
- **Build System**: Cross-platform build pipeline supporting both .NET and Xcode
- **Documentation**: Unified README covering both Windows and macOS implementations
- **Feature Parity**: All core features now available on both platforms
  - Bulk playlist operations (delete, unfollow, manage)
  - Advanced device management and playback transfer
  - Web Playback SDK integration
  - Rate limiting and error handling
- **Rebranding**: Project renamed from "SpotifyWPF" to "SpofifyWPF" for consistent cross-platform branding
  - Windows app: "SpotifyWPF" → "SpofifyWPF"
  - macOS app: "Spofify" → "SpofifyWPF" (unified naming)
  - All references updated throughout codebase and documentation
- **Version Reset**: Both platforms start at v3.0.0 for unified versioning
- **Migration Notes**: All existing Windows features now available on macOS; Windows users can continue using existing workflows; macOS users now have full access to Spotify power tools

---

## [2.0.0] - 2025-09-12

### Added

- Modern Player UI based on WebView2 hosting the Spotify Web Playback SDK
- "Top Tracks" module powered by Spotify's Personalization API (with album artwork)
- Unified device experience: click-to-play promotes the local Web Player when no active device is present; "Play to" remains available from context menus
- Smarter volume handling that distinguishes between the local Web Player and remote devices
- Smoother, latency-resistant progress tracking for local playback

Note: Internal development fixes made during the 2.0 cycle are not itemized here.

## [1.1.4] - 2025-09-08
### Added
- Added “Play to” context menu on track rows (Playlists and Search) to play a selected track on a selected device.
- Active device shows a checkmark in device submenus; device list refreshes on submenu open.
- Consistent right-side chevrons and padding for submenu items (Windows Explorer style).

### Changed

- Unified the binding approach for context menus using a BindingProxy (Freezable) and a MultiJoinConverter for robust CommandParameter construction.
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

- README updated to reflect fork origin and version start at 1.1.0.

### Added

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
