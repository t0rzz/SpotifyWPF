using System;
using System.Globalization;
using System.Windows.Data;

namespace SpotifyWPF.View.Converters
{
    /// <summary>
    /// Converts milliseconds to a human-readable time format (m:ss or h:mm:ss).
    /// </summary>
    public class MillisecondsToTimeConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            try
            {
                long ms = value switch
                {
                    long l => l,
                    int i => i,
                    double d => (long)d,
                    _ => 0
                };

                var timeSpan = TimeSpan.FromMilliseconds(ms);

                if (timeSpan.TotalHours >= 1)
                {
                    // Format: h:mm:ss
                    return $"{(int)timeSpan.TotalHours}:{timeSpan.Minutes:D2}:{timeSpan.Seconds:D2}";
                }
                else
                {
                    // Format: m:ss
                    return $"{timeSpan.Minutes}:{timeSpan.Seconds:D2}";
                }
            }
            catch
            {
                return "0:00";
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
