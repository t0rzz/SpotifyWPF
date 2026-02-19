using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
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
        private const string EncryptedPrefix = "enc-v1:";
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("SpotifyWPF.TokenStorage.v1");

        private readonly string _folderPath;
        private readonly string _filePath;

        public TokenStorage()
        {
            _folderPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                Constants.AppDataFolderName);
            _filePath = Path.Combine(_folderPath, Constants.TokenFileName);
        }

        public TokenInfo? Load()
        {
            try
            {
                if (!File.Exists(_filePath)) return null;
                var payload = File.ReadAllText(_filePath);
                if (string.IsNullOrWhiteSpace(payload)) return null;

                var json = TryDecrypt(payload, out var decryptedJson) ? decryptedJson : payload;
                var token = JsonConvert.DeserializeObject<TokenInfo>(json);
                if (token == null || string.IsNullOrWhiteSpace(token.AccessToken)) return null;

                // Transparently migrate legacy plaintext token files to encrypted format.
                if (!payload.StartsWith(EncryptedPrefix, StringComparison.Ordinal))
                {
                    Save(token);
                }

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

                var json = JsonConvert.SerializeObject(token, Formatting.None);
                var encryptedPayload = Encrypt(json);

                var tempFilePath = _filePath + ".tmp";
                File.WriteAllText(tempFilePath, encryptedPayload, Encoding.UTF8);
                File.Move(tempFilePath, _filePath, true);
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

        private static string Encrypt(string plainTextJson)
        {
            var plainBytes = Encoding.UTF8.GetBytes(plainTextJson);
            var protectedBytes = ProtectedData.Protect(plainBytes, Entropy, DataProtectionScope.CurrentUser);
            return EncryptedPrefix + Convert.ToBase64String(protectedBytes);
        }

        private static bool TryDecrypt(string payload, out string json)
        {
            json = string.Empty;

            if (!payload.StartsWith(EncryptedPrefix, StringComparison.Ordinal))
            {
                return false;
            }

            var protectedBase64 = payload.Substring(EncryptedPrefix.Length);
            var protectedBytes = Convert.FromBase64String(protectedBase64);
            var plainBytes = ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.CurrentUser);
            json = Encoding.UTF8.GetString(plainBytes);
            return true;
        }
    }
}
