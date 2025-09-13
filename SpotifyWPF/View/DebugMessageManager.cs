using System;
using System.Collections.Concurrent;
using System.Text;

namespace SpotifyWPF.View
{
    /// <summary>
    /// Event arguments for debug messages
    /// </summary>
    public class DebugMessageEventArgs : EventArgs
    {
        public string Message { get; }
        public DateTime Timestamp { get; }

        public DebugMessageEventArgs(string message)
        {
            Message = message;
            Timestamp = DateTime.Now;
        }
    }

    /// <summary>
    /// Singleton manager for debug messages across the application
    /// </summary>
    public class DebugMessageManager
    {
        private static readonly Lazy<DebugMessageManager> _instance = new Lazy<DebugMessageManager>(() => new DebugMessageManager());
        private readonly ConcurrentQueue<string> _messages = new ConcurrentQueue<string>();
        private readonly int _maxMessages = 1000; // Keep last 1000 messages

        public static DebugMessageManager Instance => _instance.Value;

        public event EventHandler<DebugMessageEventArgs>? DebugMessageReceived;

        private DebugMessageManager()
        {
            // Private constructor for singleton
        }

        /// <summary>
        /// Add a debug message
        /// </summary>
        public void AddMessage(string message)
        {
            var timestampedMessage = $"[{DateTime.Now:HH:mm:ss.fff}] {message}";

            _messages.Enqueue(timestampedMessage);

            // Remove old messages if we exceed the limit
            while (_messages.Count > _maxMessages)
            {
                _messages.TryDequeue(out _);
            }

            // Notify listeners
            DebugMessageReceived?.Invoke(this, new DebugMessageEventArgs(timestampedMessage));
        }

        /// <summary>
        /// Get all messages as a single string
        /// </summary>
        public string GetAllMessages()
        {
            return string.Join(Environment.NewLine, _messages);
        }

        /// <summary>
        /// Get the current message count
        /// </summary>
        public int MessageCount => _messages.Count;

        /// <summary>
        /// Clear all messages
        /// </summary>
        public void Clear()
        {
            while (_messages.TryDequeue(out _)) { }
        }

        /// <summary>
        /// Add a formatted debug message with category
        /// </summary>
        public void AddMessage(string category, string message)
        {
            AddMessage($"[{category}] {message}");
        }

        /// <summary>
        /// Add an exception message
        /// </summary>
        public void AddException(string context, Exception ex)
        {
            AddMessage("ERROR", $"{context}: {ex.Message}");
            if (ex.InnerException != null)
            {
                AddMessage("ERROR", $"Inner exception: {ex.InnerException.Message}");
            }
        }
    }
}