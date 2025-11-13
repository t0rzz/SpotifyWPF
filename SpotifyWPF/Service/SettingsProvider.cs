namespace SpotifyWPF.Service
{
    public class SettingsProvider : ISettingsProvider
    {
        public string SpotifyClientId => SpotifyWPF.Properties.Settings.Default.SpotifyClientId;

        public string SpotifyRedirectPort => SpotifyWPF.Properties.Settings.Default.SpotifyRedirectPort;

        public int MaxThreadsForOperations => SpotifyWPF.Properties.Settings.Default.MaxThreadsForOperations;

        public string DefaultMarket => SpotifyWPF.Properties.Settings.Default.DefaultMarket;

        public bool MinimizeToTrayOnClose => SpotifyWPF.Properties.Settings.Default.MinimizeToTrayOnClose;
    }
}
