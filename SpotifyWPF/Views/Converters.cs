using System;
using System.Globalization;
using System.Windows.Data;

namespace SpotifyWPF.Views
{
    /// <summary>
    /// Converts milliseconds to MM:SS format
    /// </summary>
    public class TimeConverter : IValueConverter
    {
        public static readonly TimeConverter Instance = new TimeConverter();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int milliseconds)
            {
                var timeSpan = TimeSpan.FromMilliseconds(milliseconds);
                return $"{(int)timeSpan.TotalMinutes:D2}:{timeSpan.Seconds:D2}";
            }
            return "0:00";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// Converts IsPlaying boolean to play/pause icon
    /// </summary>
    public class PlayPauseIconConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool isPlaying)
            {
                return isPlaying ? "\uE769" : "\uE768"; // Pause : Play icons
            }
            return "\uE768"; // Default to play
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// Converts IsPlaying boolean to accessibility text
    /// </summary>
    public class PlayPauseAccessibilityConverter : IValueConverter
    {
        public static readonly PlayPauseAccessibilityConverter Instance = new PlayPauseAccessibilityConverter();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool isPlaying)
            {
                return isPlaying ? "Pause" : "Play";
            }
            return "Play";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
