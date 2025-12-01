using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media.Imaging;
using System.Windows;

namespace SpotifyWPF.View.Converters
{
    /// <summary>
    /// Converter: Uri -> BitmapImage
    /// Creates a BitmapImage with IgnoreImageCache and OnLoad to avoid caching artifacts
    /// </summary>
    public class AlbumArtConverter : IValueConverter
    {
        // A simple cached placeholder image as a DrawingImage to show when no album art is available
        private static readonly System.Windows.Media.ImageSource _placeholder;

        static AlbumArtConverter()
        {
            var brush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(36, 36, 36));
            brush.Freeze();
            var rect = new System.Windows.Rect(0, 0, 160, 160);
            var drawing = new System.Windows.Media.GeometryDrawing(brush, null, new System.Windows.Media.RectangleGeometry(rect));
            var drawingImage = new System.Windows.Media.DrawingImage(drawing);
            drawingImage.Freeze();
            _placeholder = drawingImage;
        }
        public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            try
            {
                // Log that the converter was invoked (helps track when bindings run)
                if (value is Uri u)
                {
                    try { SpotifyWPF.Service.LoggingService.LogToFile($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] ALBUM_ART_CONVERTER: Invoked for {u}\n"); } catch { }
                }
            }
            catch { }

            if (value is Uri uri)
            {
                try
                {
                    var image = new BitmapImage();
                    image.BeginInit();
                    image.UriSource = uri;
                    image.CacheOption = BitmapCacheOption.OnLoad; // fully load and avoid keep-alive caching
                    image.CreateOptions = BitmapCreateOptions.IgnoreImageCache; // ignore WPF image cache
                    image.EndInit();
                    // Don't freeze — freezing remote-loaded images can throw in some cases
                    // (e.g. when the image stream isn't fully freezable on the UI thread)
                    // Freezing is optional and not required for data-binding on the UI thread.
                    // Log success when an image was actually created
                    try { SpotifyWPF.Service.LoggingService.LogToFile($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] ALBUM_ART_CONVERTER: Loaded image from {uri}\n"); } catch { }
                    return image;
                }
                catch (Exception ex)
                {
                    // Log conversion errors for debugging — converter executes on UI thread
                    try { SpotifyWPF.Service.LoggingService.LogToFile($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] ALBUM_ART_CONVERTER: Error loading image from {uri}: {ex.Message}\n"); } catch { }
                    // Fall through to clear value on error
                }
            }

            // When no Uri is available or an error occurred, return a neutral placeholder so items remain visible
            return _placeholder;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}