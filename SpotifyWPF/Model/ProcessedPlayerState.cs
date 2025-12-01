using System;

namespace SpotifyWPF.Model
{
    /// <summary>
    /// A processed view of an incoming PlayerState describing higher level events
    /// like track changes, device transfers, or position wrap.
    /// </summary>
    public class ProcessedPlayerState
    {
        public PlayerState RawState { get; set; } = new PlayerState();

        // Flags set by the processor
        public bool TrackChanged { get; set; }
        public bool DeviceTransferDetected { get; set; }
        public bool PositionWrapped { get; set; }
        public bool WasDuplicate { get; set; }

        // Optional details
        public string Reason { get; set; } = string.Empty;
    }
}