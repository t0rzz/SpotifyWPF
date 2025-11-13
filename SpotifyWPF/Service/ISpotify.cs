using SpotifyAPI.Web;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SpotifyWPF.Model.Dto;

namespace SpotifyWPF.Service
{
    public interface ISpotify
    {
        Task LoginAsync(Action? onSuccess);

        Task<bool> EnsureAuthenticatedAsync();

        Task<PrivateUser?> GetPrivateProfileAsync();
    Task<string?> GetUserDisplayNameAsync();
    Task<string?> GetProfileImageCachedPathAsync();

        // Ritorna true se l'utente segue la playlist, false se non la segue, null in caso di errore/transiente
        Task<bool?> CheckIfCurrentUserFollowsPlaylistAsync(string playlistId);

        // Nuovi metodi DTO (decoupling dal package)
        Task<PagingDto<PlaylistDto>> GetMyPlaylistsPageAsync(int offset, int limit);
        Task<PagingDto<PlaylistDto>> SearchPlaylistsPageAsync(string query, int offset, int limit);
        Task<PagingDto<AlbumDto>> SearchAlbumsPageAsync(string query, int offset, int limit);
        Task<PagingDto<ArtistDto>> SearchArtistsPageAsync(string query, int offset, int limit);
        Task<PagingDto<TrackDto>> SearchTracksPageAsync(string query, int offset, int limit);
        Task UnfollowPlaylistAsync(string playlistId);
        Task DeletePlaylistAsync(string playlistId);
        // Playlist management
        Task<PlaylistDto> CreatePlaylistAsync(string name, string description, bool isPublic, bool isCollaborative);
        Task UpdatePlaylistAsync(string playlistId, string name, bool isPublic);
        Task UploadPlaylistImageAsync(string playlistId, string base64Image);
        Task<PagingDto<SearchResultDto>> SearchItemsPageAsync(string query, List<string> types, int offset, int limit);
        Task AddTracksToPlaylistAsync(string playlistId, IEnumerable<string> trackUris);
        Task<PagingDto<TrackDto>> GetPlaylistTracksPageAsync(string playlistId, int offset, int limit);

        // Playlist tracks management
        Task RemoveTracksFromPlaylistAsync(string playlistId, IEnumerable<string> trackUris);
        Task ReorderPlaylistTracksAsync(string playlistId, int rangeStart, int insertBefore, int rangeLength = 1, string? snapshotId = null);

        // Saved albums management
        Task<PagingDto<AlbumDto>> GetMySavedAlbumsPageAsync(int offset, int limit);
        Task RemoveSavedAlbumsAsync(IEnumerable<string> albumIds);

        ISpotifyClient? Api { get; }

        PrivateUser? CurrentUser { get; }

    // Devices / Playback
    Task<IReadOnlyList<Device>> GetDevicesAsync();
    Task<CurrentlyPlayingContext?> GetCurrentPlaybackAsync();
    Task TransferPlaybackAsync(IEnumerable<string> deviceIds, bool play);
    Task<bool> PlayTrackOnDeviceAsync(string deviceId, string trackId);
    Task<bool> SetVolumePercentOnDeviceAsync(string deviceId, int volumePercent);
    Task<bool> PauseCurrentPlaybackAsync(string? deviceId = null);
    Task<bool> ResumeCurrentPlaybackAsync(string? deviceId = null);
    Task<bool> SeekCurrentPlaybackAsync(int positionMs, string? deviceId = null);
    Task<bool> SetShuffleAsync(bool state, string? deviceId = null);
    Task<bool> SetRepeatAsync(string state, string? deviceId = null);
    Task<bool> SkipToNextAsync(string? deviceId = null);
    Task<bool> SkipToPrevAsync(string? deviceId = null);

    // Logout clears tokens and current client
    void Logout();

    // Get current access token for Web Playback SDK
    Task<string?> GetCurrentAccessTokenAsync();

        // Get user's top tracks from Spotify API
    Task<PagingDto<TrackDto>> GetUserTopTracksAsync(int limit = 20, int offset = 0, string timeRange = "medium_term");

        // Followed artists (cursor paging)
        Task<SpotifyWPF.Model.Dto.FollowedArtistsPage> GetFollowedArtistsPageAsync(string? after, int limit);
        Task UnfollowArtistsAsync(IEnumerable<string> artistIds);

        // Get artist's top tracks
        Task<List<TrackDto>> GetArtistTopTracksAsync(string artistId, string market = "US");

        // Browse new releases
        Task<PagingDto<AlbumDto>> GetNewReleasesPageAsync(int offset, int limit);

        // Get album tracks
        Task<List<TrackDto>> GetAlbumTracksAsync(string albumId);
    }
}
