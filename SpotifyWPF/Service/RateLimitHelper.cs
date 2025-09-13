using System;
using System.Text.RegularExpressions;

namespace SpotifyWPF.Service
{
    public static class RateLimitHelper
    {
        // Estrae il valore intero dei secondi dal testo contenente "Retry-After"
        // Esempi: "Retry-After: 3", "retry-after 10"
        public static int? TryExtractRetryAfterSeconds(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;

            var match = Regex.Match(text, @"retry-?after[:\s]+(?<s>\d+)", RegexOptions.IgnoreCase);
            if (match.Success && int.TryParse(match.Groups["s"].Value, out var seconds))
            {
                return seconds;
            }

            return (int?)null;
        }

        // Controlla se il messaggio indica scadenza dell'access token
        public static bool IsAccessTokenExpiredMessage(string text)
        {
            return !string.IsNullOrWhiteSpace(text) &&
                   text.IndexOf("access token expired", StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
