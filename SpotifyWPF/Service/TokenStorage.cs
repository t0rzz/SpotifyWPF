using System;
using System.IO;
using Newtonsoft.Json;

namespace SpotifyWPF.Service
{
    public class TokenInfo
    {
        public string? AccessToken { get; set; }
        public string? RefreshToken { get; set; }
        public DateTime ExpiresAtUtc { get; set; }
    }

    public class TokenStorage
    {
        private readonly string _folderPath;
        private readonly string _filePath;

        public TokenStorage()
        {
            _folderPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "SpotifyWPF");
            _filePath = Path.Combine(_folderPath, "token.json");
        }

        public TokenInfo? Load()
        {
            try
            {
                if (!File.Exists(_filePath)) return null;
                var json = File.ReadAllText(_filePath);
                var token = JsonConvert.DeserializeObject<TokenInfo>(json);
                if (token == null || string.IsNullOrWhiteSpace(token.AccessToken)) return null;
                return token;
            }
            catch
            {
                return null;
            }
        }

        public void Save(TokenInfo token)
        {
            try
            {
                if (!Directory.Exists(_folderPath))
                    Directory.CreateDirectory(_folderPath);

                var json = JsonConvert.SerializeObject(token, Formatting.Indented);
                File.WriteAllText(_filePath, json);
            }
            catch
            {
                // Ignora problemi di persistenza per non bloccare il flusso di login
            }
        }

        public void Clear()
        {
            try
            {
                if (File.Exists(_filePath))
                    File.Delete(_filePath);
            }
            catch
            {
                // Ignora
            }
        }
    }
}
