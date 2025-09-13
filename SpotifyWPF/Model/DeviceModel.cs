namespace SpotifyWPF.Model
{
    /// <summary>
    /// Device model for Spotify playback devices
    /// </summary>
    public class DeviceModel
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public bool IsActive { get; set; }
    }
}
