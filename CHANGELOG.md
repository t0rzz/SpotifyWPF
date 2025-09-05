# Changelog

All notable changes to this project will be documented in this file.

The format is based on Keep a Changelog and this project adheres to Semantic Versioning.

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
- GitHub release: created automatically by CI when pushing a tag (v*); the release attaches the zipped Release binaries.
