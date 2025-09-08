# SpotifyWPF

Unofficial WPF application that provides Spotify “power tools” (e.g., bulk playlist deletion, quick navigation, and extra metadata not shown in the official client).

Note: This repository is a fork of the original project by MrPnut: https://github.com/MrPnut/SpotifyWPF. Our fork lives at https://github.com/t0rzz/SpotifyWPF and continues development from there (starting at v1.1.0).

Recent highlights (v1.1.4):
- Faster playlist loading with Start/Stop controls and better pagination handling.
- Right‑click context menu on playlists (Open in Spotify, Copy playlist link, Unfollow/Delete) with clean visuals.
- Window title shows the app version (from assembly informational version).
- Optional MSIX packaging for a simpler install, plus portable single‑file EXE.
- New “Devices” menu: list available Spotify devices, see the active one, and transfer playback in one click.
- New “Help” menu: “About” dialog and “Logout” (clears local auth token and returns to the login screen).
- New “Play to” on track context menus (Playlists and Search): pick a device to play the selected track.
- “Play to” submenus show a right‑side chevron and correctly pass deviceId|trackId to commands.
- Active device checkmark and device list refresh on submenu open for both Playlists and Search pages.


## Requirements to build locally
- Windows 10/11 x64
- .NET SDK 8.0 or later
- Visual Studio 2022 (17.8+) with workloads:
  - .NET desktop development
  - optional: MSIX Packaging Tools / Desktop Bridge (if you want to generate an installer)
- Spotify account (required for OAuth authentication)


## Clone the repository
Open PowerShell and run:

```powershell
git clone https://github.com/t0rzz/SpotifyWPF.git
cd SpotifyWPF
```

If you are working with this repository locally, make sure you are in the root folder containing the `SpotifyWPF.sln` solution file.

## Build and run
You can use Visual Studio or the .NET CLI.

- Visual Studio:
  1. Open `SpotifyWPF.sln`.
  2. Set the startup project to `SpotifyWPF`.
## Features
- Playlists
  - Load all playlists with offset‑based pagination and logging.
  - Start/Stop controls to manage long operations.
  - Extra columns: “Created By”, “# Tracks”.
  - Bulk Unfollow/Delete with retries and 429 handling.
  - Context menu: Load Tracks, Open in Spotify, Copy link, Unfollow/Delete.
- Users/Artists
  - Load ALL followed artists with automatic cursor‑based pagination.
  - Parallel workers for speed; Stop to cancel in‑flight work.
  - Unfollow selected artists (batched) and per‑artist Unfollow.
  - Open in Spotify / Copy link from context menu.
  - Logs API page metadata similar to playlists (redacted as needed).
- Devices
  - List available devices and mark the active device.
  - Transfer playback to a selected device.
- Help
  - About dialog with version, author, and repository link.
  - Logout clears local auth token and returns to login.
  3. Select configuration `Debug` x `Any CPU`.
  4. Press F5 to run.
 On the first operation that requires the API, the app will open your browser for Spotify login and request the required permissions.
- .NET CLI:
  ```powershell
  dotnet restore
  dotnet build .\SpotifyWPF\SpotifyWPF.csproj -c Debug
  dotnet run --project .\SpotifyWPF\SpotifyWPF.csproj -c Debug
  ```
 For device listing and playback transfer, the app requests playback scopes (`user-read-playback-state`, `user-modify-playback-state`).
 For followed artists features, the app requests follow scopes (`user-follow-read`, `user-follow-modify`).
Target Framework: `net8.0-windows` with WPF enabled.


## Download and install
Head to the Releases page and pick one of the artifacts:
- MSIX installer: simplest install/uninstall experience on Windows 10/11.
- Portable EXE (single file): no install, just run. Good for testing.
- ZIP: the full build output if you prefer manual unpacking.


## Spotify authentication
On the first operation that requires the API, the app will open your browser for Spotify login and request the required permissions.

Tips:
- If you need to develop against the APIs with your own Spotify app, create one at https://developer.spotify.com/dashboard and configure a Redirect URI (e.g., `http://localhost:5000/callback`).
- This solution uses SpotifyAPI.Web/Auth. The login flow is automatic; you do not need to manually enter a Client Secret in the app for standard usage.
- If you encounter redirect issues, verify that the port/URI configured in your Spotify app matches the one used locally by the authentication flow.
- For device listing and playback transfer, the app requests playback scopes (`user-read-playback-state`, `user-modify-playback-state`). You’ll see these in the consent screen.


## Key features (excerpt)
- Playlist loading with pagination (max page size) and basic logging.
- Start/Stop buttons to start or interrupt long operations.
- Extra columns: “Created By” and “# Tracks”.
- Unfollow/Delete single or multiple playlists in parallel (with retries and 429 handling).
- Context menu on playlists: Open in Spotify, Copy playlist link, Unfollow/Delete.
- Devices menu: list available devices, highlight the active one, and switch playback target.
- Help menu: About dialog and one‑click Logout (clears local auth token and returns to login).


## Performance notes
- Bounded parallelism for network calls to stay fast without tripping rate limits.
- UI virtualization enabled in lists/grids to keep the UI responsive on large libraries.
- Cancelling in‑flight work when you press Stop to avoid blocking the UI.
- Reuses a single HTTP client under the hood and honors Retry‑After from the API.


## Packaging (optional)
The repository includes the `SpotifyWPF.MSIX` project for distribution as an installer.

- Open the solution in Visual Studio.
- Select the `SpotifyWPF.MSIX` project and generate the package (Store/SideLoad depending on configuration).
- For signed installations, use a trusted certificate or sign the package with your certificate.

CI builds attach ZIP, portable EXE, and (when the packaging toolchain is available) the MSIX to each release.




## Troubleshooting
- Rate limit (HTTP 429): the code implements backoff and respects Retry-After. Try again in a few minutes.
- Expired token: the app attempts automatic renewal. If the browser does not open, close and reopen the app.
- Build fails: make sure you have .NET 8 SDK installed and the correct Visual Studio workloads.
- Devices don’t appear: ensure Spotify is running on at least one device and your account is active there (the API only reports available devices when the client is active).

If the MSIX doesn’t appear in CI, ensure the Desktop Bridge toolchain is available on the runner (or build the MSIX locally from Visual Studio).


## Project structure (overview)
- `SpotifyWPF/` main WPF application.
- `SpotifyWPF.MSIX/` MSIX packaging project.
- `Service/` integration with Spotify API (wrappers and calls).
- `ViewModel/` presentation logic (MVVM).
- `View/` XAML views and components.


## License
This project is provided “as is.” Check the LICENSE file if present in the repository or add one before publishing.
