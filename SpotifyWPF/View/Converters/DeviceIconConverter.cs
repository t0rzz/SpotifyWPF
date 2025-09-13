using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace SpotifyWPF.View.Converters
{
    /// <summary>
    /// Converter that determines which device icon to show based on device type from Spotify API
    /// </summary>
    public class DeviceTypeToIconConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (parameter == null)
                return Visibility.Collapsed;

            string param = parameter.ToString() ?? "";

            // Special case: if parameter is "show", return visibility based on whether device exists
            if (param == "show")
            {
                return value != null ? Visibility.Visible : Visibility.Collapsed;
            }

            if (value == null)
                return Visibility.Collapsed;

            string deviceType = value.ToString()?.ToLower() ?? "";
            string iconType = param;

            // Determine if this icon should be visible based on device type
            bool shouldShow = ShouldShowIcon(deviceType, iconType);

            return shouldShow ? Visibility.Visible : Visibility.Collapsed;
        }

        private bool ShouldShowIcon(string deviceType, string iconType)
        {
            if (string.IsNullOrEmpty(deviceType))
                return iconType == "generic";

            switch (iconType.ToLower())
            {
                case "computer":
                    return deviceType == "computer";

                case "smartphone":
                    return deviceType == "smartphone";

                case "tablet":
                    return deviceType == "tablet";

                case "speaker":
                    return deviceType == "speaker";

                case "tv":
                    return deviceType == "tv";

                case "generic":
                    // Show generic icon if no specific match found
                    return deviceType != "computer" &&
                           deviceType != "smartphone" &&
                           deviceType != "tablet" &&
                           deviceType != "speaker" &&
                           deviceType != "tv";

                default:
                    return false;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}