using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace SpotifyWPF.View.Converters
{
    /// <summary>
    /// Converter that converts a boolean to Visibility, with inverted logic for hiding when true
    /// </summary>
    public class BooleanToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool boolValue)
            {
                // Return Collapsed when true (hide when multiple selected), Visible when false (show when single selected)
                return boolValue ? Visibility.Collapsed : Visibility.Visible;
            }
            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}