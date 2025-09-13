namespace SpotifyWPF.Service
{
    public class SettingsProvider : ISettingsProvider
    {
        public string SpotifyClientId => SpotifyWPF.Properties.Settings.Default.SpotifyClientId;

        public string SpotifyRedirectPort => SpotifyWPF.Properties.Settings.Default.SpotifyRedirectPort;
    }
}
