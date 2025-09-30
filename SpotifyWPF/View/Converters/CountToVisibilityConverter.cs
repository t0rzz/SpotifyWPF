using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace SpotifyWPF.View.Converters
{
    /// <summary>
    /// Converter that converts an item count to Visibility
    /// </summary>
    public class CountToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int count && parameter is string mode)
            {
                switch (mode)
                {
                    case "single":
                        return count == 1 ? Visibility.Visible : Visibility.Collapsed;
                    case "multiple":
                        return count > 1 ? Visibility.Visible : Visibility.Collapsed;
                    default:
                        return Visibility.Collapsed;
                }
            }
            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}