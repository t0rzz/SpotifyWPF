namespace SpotifyWPF.Service
{
    public class SettingsProvider : ISettingsProvider
    {
        public string SpotifyClientId => !string.IsNullOrWhiteSpace(Properties.Settings.Default.UserSpotifyClientId) 
            ? Properties.Settings.Default.UserSpotifyClientId 
            : Properties.Settings.Default.SpotifyClientId;

        public string SpotifyRedirectPort => !string.IsNullOrWhiteSpace(Properties.Settings.Default.UserSpotifyRedirectPort) 
            ? Properties.Settings.Default.UserSpotifyRedirectPort 
            : Properties.Settings.Default.SpotifyRedirectPort;

        public string UserSpotifyClientId => Properties.Settings.Default.UserSpotifyClientId;

        public string UserSpotifyRedirectPort => Properties.Settings.Default.UserSpotifyRedirectPort;

        public int MaxThreadsForOperations => Properties.Settings.Default.MaxThreadsForOperations;

        public string DefaultMarket => Properties.Settings.Default.DefaultMarket;

        public bool MinimizeToTrayOnClose => Properties.Settings.Default.MinimizeToTrayOnClose;
    }
}
