using System;
using System.Globalization;
using System.Windows.Data;

namespace SpotifyWPF.Converter
{
    public class RadioButtonStringConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return parameter is string p && value is string s && p == s;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is bool b && b ? parameter! : Binding.DoNothing;
        }
    }
}
