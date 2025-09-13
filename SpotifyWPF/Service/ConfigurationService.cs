using System;
using System.IO;

namespace SpotifyWPF.Service
{
    /// <summary>
    /// Centralized configuration management
    /// </summary>
    public interface IConfigurationService
    {
        string GetLogDirectory();
        string GetTokenFilePath();
        string GetWebView2DataDirectory();
        string GetStartupLogPath();
    }

    public class ConfigurationService : IConfigurationService
    {
        private readonly string _appDataPath;

        public ConfigurationService()
        {
            _appDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                Constants.AppDataFolderName);
        }

        public string GetLogDirectory()
        {
            return Path.Combine(_appDataPath, Constants.LogsFolderName);
        }

        public string GetTokenFilePath()
        {
            return Path.Combine(_appDataPath, Constants.TokenFileName);
        }

        public string GetWebView2DataDirectory()
        {
            return Path.Combine(_appDataPath, Constants.WebView2FolderName);
        }

        public string GetStartupLogPath()
        {
            return Path.Combine(GetLogDirectory(), "startup.log");
        }
    }
}