namespace SpotifyWPF
{
    /// <summary>
    /// Application-wide constants to eliminate magic numbers and improve maintainability
    /// </summary>
    public static class Constants
    {
        // Timing constants
        public const int DeviceRefreshIntervalSeconds = 30;
        public const int PortCheckTimeoutMs = 100;
        public const int SeekThrottleDelayMs = 120;
        public const int StatePollIntervalMs = 5000; // Increased from 800ms to 5 seconds to reduce API calls
        public const int UiProgressUpdateIntervalMs = 1000;

        // Network constants
        public const int DefaultConnectionLimit = 100;
        public const int TokenExpirationBufferMinutes = 5;

        // UI constants
        public const int DefaultWindowWidth = 1100;
        public const int DefaultWindowHeight = 800;
        public const int PlayButtonSize = 72;

        // Spotify API constants
        public const string SpotifyTrackUriPrefix = "spotify:track:";

        // File paths
        public const string AppDataFolderName = "SpotifyWPF";
        public const string WebView2FolderName = "WebView2";
        public const string TokenFileName = "token.json";
        public const string LogsFolderName = "logs";
    }
}