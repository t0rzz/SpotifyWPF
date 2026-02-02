using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace SpotifyWPF.View.Converters
{
    /// <summary>
    /// Converts an image URL to a BitmapImage with in-memory caching.
    /// Uses async loading to avoid blocking and cross-thread exceptions.
    /// </summary>
    public class ImageUrlToBitmapConverter : IValueConverter
    {
        // Cache loaded images to avoid repeated downloads
        private static readonly ConcurrentDictionary<string, BitmapImage> _cache = new();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            try
            {
                var url = value as string;
                if (string.IsNullOrWhiteSpace(url)) return DependencyProperty.UnsetValue;

                // Normalize URL
                if (url.StartsWith("//"))
                    url = "https:" + url;

                if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
                    return DependencyProperty.UnsetValue;

                // Return cached image if available
                if (_cache.TryGetValue(url, out var cached))
                    return cached;

                // Create BitmapImage with async loading (avoids blocking and thread issues)
                var bi = new BitmapImage();
                bi.BeginInit();
                bi.CacheOption = BitmapCacheOption.OnLoad;  // Cache in memory once loaded
                bi.CreateOptions = BitmapCreateOptions.IgnoreImageCache; // Don't use disk cache (we have our own)
                bi.UriSource = uri;
                bi.EndInit();

                // If already downloaded (from WPF's internal cache), freeze and cache it
                if (bi.IsDownloading == false)
                {
                    bi.Freeze();
                    _cache.TryAdd(url, bi);
                    return bi;
                }

                // Still downloading - set up completion handler
                var urlForClosure = url;
                bi.DownloadCompleted += (s, e) =>
                {
                    try
                    {
                        if (s is BitmapImage img && img.CanFreeze)
                        {
                            img.Freeze();
                            _cache.TryAdd(urlForClosure, img);
                        }
                    }
                    catch { /* ignore */ }
                };

                bi.DownloadFailed += (s, e) =>
                {
                    System.Diagnostics.Debug.WriteLine($"[ImageConverter] Download failed: {urlForClosure} - {e.ErrorException?.Message}");
                };

                return bi;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ImageConverter] Error: {ex.Message}");
                return DependencyProperty.UnsetValue;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
