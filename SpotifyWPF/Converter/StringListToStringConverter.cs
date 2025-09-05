using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows.Data;

namespace SpotifyWPF.Converter
{
    public class StringListToStringConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is IList<string> list ? string.Join("\n", list) : Binding.DoNothing;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
