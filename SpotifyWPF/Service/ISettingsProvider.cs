namespace SpotifyWPF.Service
{
    public interface ISettingsProvider
    {
        string SpotifyClientId { get; }

        string SpotifyRedirectPort { get; }

        string UserSpotifyClientId { get; }

        string UserSpotifyRedirectPort { get; }

        int MaxThreadsForOperations { get; }

        string DefaultMarket { get; }

        bool MinimizeToTrayOnClose { get; }
    }
}
