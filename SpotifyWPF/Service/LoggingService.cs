using System;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;

namespace SpotifyWPF.Service
{
    /// <summary>
    /// Centralized logging service to replace scattered debug logging
    /// </summary>
    public interface ILoggingService
    {
        void LogDebug(string message, [CallerMemberName] string? caller = null);
        void LogInfo(string message, [CallerMemberName] string? caller = null);
        void LogWarning(string message, [CallerMemberName] string? caller = null);
        void LogError(string message, Exception? ex = null, [CallerMemberName] string? caller = null);
    }

    public class LoggingService : ILoggingService
    {
        private readonly ConcurrentQueue<LogEntry> _logEntries = new();
        private const int MaxLogEntries = 1000;
        private const int MaxUiLogEntries = 100;
        private const long MaxLogFileSizeBytes = 10 * 1024 * 1024; // 10 MB
        private const int MaxArchivedLogFiles = 5;
        private static readonly object _fileLock = new object();
        private static readonly string _defaultLogPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Constants.AppDataFolderName,
            Constants.LogsFolderName,
            "debug.log");

        public ObservableCollection<LogEntry> LogEntries { get; } = new();

        public void LogDebug(string message, [CallerMemberName] string? caller = null)
        {
#if DEBUG
            AddLogEntry(LogLevel.Debug, message, caller);
            System.Diagnostics.Debug.WriteLine($"[{caller}] {message}");
            try
            {
                // Persist debug logs to file as well for troubleshooting — keep same filepath used by LogToFile
                var logLine = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] DEBUG [{caller}] {message}\n";
                LogToFile(logLine);
            }
            catch { }
#endif
        }

        public void LogInfo(string message, [CallerMemberName] string? caller = null)
        {
            AddLogEntry(LogLevel.Info, message, caller);
            System.Diagnostics.Debug.WriteLine($"[{caller}] {message}");
        }

        public void LogWarning(string message, [CallerMemberName] string? caller = null)
        {
            AddLogEntry(LogLevel.Warning, message, caller);
            System.Diagnostics.Debug.WriteLine($"[{caller}] WARNING: {message}");
        }

        public void LogError(string message, Exception? ex = null, [CallerMemberName] string? caller = null)
        {
            var fullMessage = ex != null ? $"{message}: {ex.Message}" : message;
            AddLogEntry(LogLevel.Error, fullMessage, caller);
            System.Diagnostics.Debug.WriteLine($"[{caller}] ERROR: {fullMessage}");
        }

        /// <summary>
        /// Thread-safe file logging to prevent concurrent access issues
        /// </summary>
        public static void LogToFile(string? message, string? filePath = null)
        {
            if (message == null) return;
            try
            {
                var targetPath = string.IsNullOrWhiteSpace(filePath) ? _defaultLogPath : filePath;
                lock (_fileLock)
                {
                    EnsureLogDirectoryAndRotateIfNeeded(targetPath);
                    File.AppendAllText(targetPath, message);
                }
            }
            catch (Exception ex)
            {
                // If file logging fails, fall back to debug output
                System.Diagnostics.Debug.WriteLine($"File logging failed: {ex.Message}");
            }
        }

        private static void EnsureLogDirectoryAndRotateIfNeeded(string logPath)
        {
            var logDirectory = Path.GetDirectoryName(logPath);
            if (string.IsNullOrWhiteSpace(logDirectory))
            {
                return;
            }

            Directory.CreateDirectory(logDirectory);

            if (!File.Exists(logPath))
            {
                return;
            }

            var fileInfo = new FileInfo(logPath);
            if (fileInfo.Length < MaxLogFileSizeBytes)
            {
                return;
            }

            var archivedFileName = $"debug-{DateTime.UtcNow:yyyyMMdd-HHmmss}.log";
            var archivedPath = Path.Combine(logDirectory, archivedFileName);
            File.Move(logPath, archivedPath, true);

            var oldArchives = new DirectoryInfo(logDirectory)
                .GetFiles("debug-*.log")
                .OrderByDescending(f => f.CreationTimeUtc)
                .Skip(MaxArchivedLogFiles);

            foreach (var oldFile in oldArchives)
            {
                try
                {
                    oldFile.Delete();
                }
                catch
                {
                    // Ignore cleanup failures; they should not block logging.
                }
            }
        }

        private void AddLogEntry(LogLevel level, string message, string? caller)
        {
            var entry = new LogEntry
            {
                Timestamp = DateTime.Now,
                Level = level,
                Message = message,
                Caller = caller ?? "Unknown"
            };

            _logEntries.Enqueue(entry);

            // Keep only the most recent entries
            while (_logEntries.Count > MaxLogEntries && _logEntries.TryDequeue(out _)) { }

            // Update UI collection on UI thread
            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
            {
                LogEntries.Add(entry);
                // Keep UI collection manageable
                while (LogEntries.Count > MaxUiLogEntries)
                {
                    LogEntries.RemoveAt(0);
                }
            });
        }
    }

    public class LogEntry
    {
        public DateTime Timestamp { get; set; }
        public LogLevel Level { get; set; }
        public string Message { get; set; } = string.Empty;
        public string Caller { get; set; } = string.Empty;
    }

    public enum LogLevel
    {
        Debug,
        Info,
        Warning,
        Error
    }
}
