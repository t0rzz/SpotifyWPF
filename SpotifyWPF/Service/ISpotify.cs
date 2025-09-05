using SpotifyAPI.Web;
using System;
using System.Threading.Tasks;
using SpotifyWPF.Model.Dto;

namespace SpotifyWPF.Service
{
    public interface ISpotify
    {
        Task LoginAsync(Action? onSuccess);

        Task<bool> EnsureAuthenticatedAsync();

        Task<PrivateUser?> GetPrivateProfileAsync();

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
    }
}
