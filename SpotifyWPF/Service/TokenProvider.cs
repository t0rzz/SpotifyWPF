using System;
using System.Threading.Tasks;

namespace SpotifyWPF.Service
{
    public class TokenProvider : ITokenProvider
    {
        private readonly ILoggingService _loggingService;
        private string? _token;

        public TokenProvider(ILoggingService loggingService)
        {
            _loggingService = loggingService ?? throw new ArgumentNullException(nameof(loggingService));
        }

        public event Action<string?>? AccessTokenRefreshed;

        public void UpdateToken(string? newToken)
        {
            _token = newToken;
            try
            {
                _loggingService.LogDebug($"[TOKEN] UpdateToken called (HasToken={!string.IsNullOrEmpty(newToken)})");
                LoggingService.LogToFile($"[TOKEN] UpdateToken called (HasToken={!string.IsNullOrEmpty(newToken)})\n");
                AccessTokenRefreshed?.Invoke(newToken);
            }
            catch (Exception ex)
            {
                _loggingService.LogError($"[TOKEN] Error invoking AccessTokenRefreshed: {ex.Message}", ex);
            }
        }

        public Task<string?> GetCurrentTokenAsync()
        {
            _loggingService.LogDebug($"[TOKEN] GetCurrentTokenAsync called (HasToken={!string.IsNullOrEmpty(_token)})");
            LoggingService.LogToFile($"[TOKEN] GetCurrentTokenAsync called (HasToken={!string.IsNullOrEmpty(_token)})\n");
            return Task.FromResult(_token);
        }
    }
}