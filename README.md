# SpotifyWPF

Unofficial WPF application that provides Spotify “power tools” (e.g., bulk playlist deletion, viewing information not shown in the official client, etc.).

Note: This repository is a fork of the original project by MrPnut: https://github.com/MrPnut/SpotifyWPF. Our fork lives at https://github.com/t0rzz/SpotifyWPF and continues development from there. This fork starts at version 1.1.0.

This version includes improvements to playlist loading, Start/Stop buttons, and version metadata 1.1.0.


## Requirements to build locally
- Windows 10/11 x64
- .NET SDK 8.0 or later
- Visual Studio 2022 (17.8+) with workloads:
  - .NET desktop development
  - optional: MSIX Packaging Tools (if you want to generate an installer)
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
  3. Select configuration `Debug` x `Any CPU`.
  4. Press F5 to run.

- .NET CLI:
  ```powershell
  dotnet restore
  dotnet build .\SpotifyWPF\SpotifyWPF.csproj -c Debug
  dotnet run --project .\SpotifyWPF\SpotifyWPF.csproj -c Debug
  ```

Target Framework: `net8.0-windows` with WPF enabled.


## Spotify authentication
On the first operation that requires the API, the app will open your browser for Spotify login and request the required permissions.

Tips:
- If you need to develop against the APIs with your own Spotify app, create one at https://developer.spotify.com/dashboard and configure a Redirect URI (e.g., `http://localhost:5000/callback`).
- This solution uses SpotifyAPI.Web/Auth. The login flow is automatic; you do not need to manually enter a Client Secret in the app for standard usage.
- If you encounter redirect issues, verify that the port/URI configured in your Spotify app matches the one used locally by the authentication flow.


## Key features (excerpt)
- Playlist loading with pagination and logging.
- Start/Stop buttons to start/interrupt loading.
- Displays “Created By” and “# Tracks” fields.
- Unfollow (delete) multiple playlists in parallel (with retries and rate limiting handling).


## Packaging (optional)
The repository includes the `SpotifyWPF.MSIX` project for distribution as an installer.

- Open the solution in Visual Studio.
- Select the `SpotifyWPF.MSIX` project and generate the package (Store/SideLoad depending on configuration).
- For signed installations, use a trusted certificate or sign the package with your certificate.




## Troubleshooting
- Rate limit (HTTP 429): the code implements backoff and respects Retry-After. Try again in a few minutes.
- Expired token: the app attempts automatic renewal. If the browser does not open, close and reopen the app.
- Build fails: make sure you have .NET 8 SDK installed and the correct Visual Studio workloads.


## Project structure (overview)
- `SpotifyWPF/` main WPF application.
- `SpotifyWPF.MSIX/` MSIX packaging project.
- `Service/` integration with Spotify API (wrappers and calls).
- `ViewModel/` presentation logic (MVVM).
- `View/` XAML views and components.


## License
This project is provided “as is.” Check the LICENSE file if present in the repository or add one before publishing.
