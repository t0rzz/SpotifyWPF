using System.Text.Json.Serialization;

namespace SpotifyWPF.Model
{
    /// <summary>
    /// Enhanced player state from Web Playback SDK with external control support
    /// </summary>
    public class PlayerState
    {
        [JsonPropertyName("trackId")]
        public string? TrackId { get; set; }
        
        [JsonPropertyName("trackName")]
        public string? TrackName { get; set; }
        
        [JsonPropertyName("artists")]
        public string? Artists { get; set; }
        
        [JsonPropertyName("album")]
        public string? Album { get; set; }
        
        [JsonPropertyName("imageUrl")]
        public string? ImageUrl { get; set; }
        
        [JsonPropertyName("position_ms")]
        public int PositionMs { get; set; }
        
        [JsonPropertyName("paused")]
        public bool Paused { get; set; }
        
        [JsonPropertyName("duration_ms")]
        public int DurationMs { get; set; }
        
        [JsonPropertyName("volume")]
        public double Volume { get; set; }
        
        [JsonPropertyName("shuffled")]
        public bool Shuffled { get; set; }
        
        [JsonPropertyName("repeat_mode")]
        public int RepeatMode { get; set; } // 0=off, 1=context, 2=track
        
        [JsonPropertyName("isPlaying")]
        public bool IsPlaying { get; set; }
        
        [JsonPropertyName("hasNextTrack")]
        public bool HasNextTrack { get; set; }
        
        [JsonPropertyName("hasPreviousTrack")]
        public bool HasPreviousTrack { get; set; }
        
        [JsonPropertyName("timestamp")]
        public long Timestamp { get; set; }
    }
}
