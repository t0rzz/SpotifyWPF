using System;
using System.Globalization;
using System.Linq;
using System.Windows.Data;

namespace SpotifyWPF.View.Converters
{
    public class MultiJoinConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            var sep = parameter as string ?? "|";
            if (values == null || values.Length == 0) return null!;
            var parts = values.Select(v => v?.ToString() ?? string.Empty).ToArray();
            if (parts.Any(string.IsNullOrWhiteSpace)) return null!;
            return string.Join(sep, parts);
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
