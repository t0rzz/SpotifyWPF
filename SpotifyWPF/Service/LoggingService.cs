using System;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.ComponentModel;
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

        public ObservableCollection<LogEntry> LogEntries { get; } = new();

        public void LogDebug(string message, [CallerMemberName] string? caller = null)
        {
#if DEBUG
            AddLogEntry(LogLevel.Debug, message, caller);
            System.Diagnostics.Debug.WriteLine($"[{caller}] {message}");
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
                while (LogEntries.Count > 100)
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