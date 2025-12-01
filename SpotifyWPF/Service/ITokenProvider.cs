using System;
using System.Threading.Tasks;

namespace SpotifyWPF.Service
{
    public interface ITokenProvider
    {
        /// <summary>
        /// Event raised when the access token is refreshed or becomes available.
        /// </summary>
        event Action<string?>? AccessTokenRefreshed;

        /// <summary>
        /// Update the current token and notify subscribers.
        /// </summary>
        void UpdateToken(string? newToken);

        /// <summary>
        /// Helper asynchronous method to get the current token on demand.
        /// </summary>
        Task<string?> GetCurrentTokenAsync();
    }
}