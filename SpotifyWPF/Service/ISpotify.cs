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

        ISpotifyClient? Api { get; }

    // Devices / Playback
    Task<IReadOnlyList<Device>> GetDevicesAsync();
    Task<CurrentlyPlayingContext?> GetCurrentPlaybackAsync();
    Task TransferPlaybackAsync(IEnumerable<string> deviceIds, bool play);
    Task<bool> PlayTrackOnDeviceAsync(string deviceId, string trackId);

    // Logout clears tokens and current client
    void Logout();

        // Followed artists (cursor paging)
        Task<SpotifyWPF.Model.Dto.FollowedArtistsPage> GetFollowedArtistsPageAsync(string? after, int limit);
        Task UnfollowArtistsAsync(IEnumerable<string> artistIds);
    }
}
