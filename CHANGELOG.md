# Changelog

All notable changes to this project will be documented in this file.

The format is based on Keep a Changelog and this project adheres to Semantic Versioning.

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
