namespace SpotifyWPF.Service
{
    public class SettingsProvider : ISettingsProvider
    {
        public string SpotifyClientId => !string.IsNullOrWhiteSpace(Properties.Settings.Default.UserSpotifyClientId)
            ? Properties.Settings.Default.UserSpotifyClientId
            : Properties.Settings.Default.SpotifyClientId;

        public string SpotifyRedirectPort
        {
            get
            {
                if (TryNormalizePort(Properties.Settings.Default.UserSpotifyRedirectPort, out var userPort))
                {
                    return userPort;
                }

                if (TryNormalizePort(Properties.Settings.Default.SpotifyRedirectPort, out var defaultPort))
                {
                    return defaultPort;
                }

                // Hard fallback for corrupted settings files.
                return "4002";
            }
        }

        public string UserSpotifyClientId => Properties.Settings.Default.UserSpotifyClientId;

        public string UserSpotifyRedirectPort => Properties.Settings.Default.UserSpotifyRedirectPort;

        public int MaxThreadsForOperations => Properties.Settings.Default.MaxThreadsForOperations;

        public string DefaultMarket => Properties.Settings.Default.DefaultMarket;

        public bool MinimizeToTrayOnClose => Properties.Settings.Default.MinimizeToTrayOnClose;

        private static bool TryNormalizePort(string? value, out string normalizedPort)
        {
            normalizedPort = string.Empty;
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            if (!int.TryParse(value.Trim(), out var parsedPort))
            {
                return false;
            }

            if (parsedPort < 1 || parsedPort > 65535)
            {
                return false;
            }

            normalizedPort = parsedPort.ToString();
            return true;
        }
    }
}
