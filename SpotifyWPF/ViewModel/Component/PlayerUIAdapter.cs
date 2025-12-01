using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SpotifyWPF.Service;

namespace SpotifyWPF.ViewModel.Component
{
    /// <summary>
    /// Adapter that performs UI-specific transformations of player state.
    /// Responsibilities:
    /// - Preload album art into a BitmapImage and cache it
    /// - Provide events when an ImageSource is ready for binding
    /// - Centralize UI resource handling for tests and refactors
    /// </summary>
    public class PlayerUIAdapter : IDisposable
    {
        private readonly ILoggingService _loggingService;

        private readonly ConcurrentDictionary<string, ImageSource> _imageCache = new();

        public PlayerUIAdapter(ILoggingService loggingService)
        {
            _loggingService = loggingService ?? throw new ArgumentNullException(nameof(loggingService));
        }

        /// <summary>
        /// Event invoked when an album art image has been loaded and is ready.
        /// </summary>
        public event Action<ImageSource?>? AlbumArtReady;

        /// <summary>
        /// Preload album art for a given URL (returns quickly; loads image in background).
        /// </summary>
        public void PreloadAlbumArt(string? url)
        {
            if (string.IsNullOrWhiteSpace(url)) return;

            if (_imageCache.ContainsKey(url))
            {
                try
                {
                    if (Application.Current != null && !Application.Current.Dispatcher.CheckAccess())
                    {
                        Application.Current.Dispatcher.Invoke(() => AlbumArtReady?.Invoke(_imageCache[url]));
                    }
                    else
                    {
                        AlbumArtReady?.Invoke(_imageCache[url]);
                    }
                }
                catch { }
                return;
            }

            _ = Task.Run(async () =>
            {
                try
                {
                    var bmp = new BitmapImage();
                    // Create/initialize on UI thread to avoid cross-thread Freezable issues
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        try
                        {
                            bmp.BeginInit();
                            bmp.UriSource = new Uri(url);
                            bmp.CacheOption = BitmapCacheOption.OnLoad;
                            bmp.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
                            bmp.EndInit();
                        }
                        catch (Exception ex)
                        {
                            _loggingService.LogDebug($"[UI_ADAPTER] PreloadAlbumArt initialization failed: {ex.Message}");
                        }
                    });

                    // Do not freeze — we want to avoid Freeze-specific exceptions during cross-thread use
                    // Cache and raise event on UI thread to avoid cross-thread binding issues
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        try
                        {
                            // Freeze the BitmapImage to make it cross-thread safe if ever needed elsewhere
                            if (bmp.CanFreeze)
                            {
                                try { bmp.Freeze(); } catch { /* ignore freeze errors */ }
                            }

                            _imageCache[url] = bmp;
                            _loggingService.LogDebug($"[UI_ADAPTER] Cached album art for {url}");
                        }
                        catch (Exception ex)
                        {
                            _loggingService.LogDebug($"[UI_ADAPTER] Error caching album art on UI thread: {ex.Message}");
                        }

                        try
                        {
                            AlbumArtReady?.Invoke(bmp);
                        }
                        catch (Exception ex)
                        {
                            _loggingService.LogDebug($"[UI_ADAPTER] AlbumArtReady handler threw: {ex.Message}");
                        }
                    });
                }
                catch (Exception ex)
                {
                    _loggingService.LogDebug($"[UI_ADAPTER] PreloadAlbumArt failed: {ex.Message}");
                    try
                    {
                        if (Application.Current != null && !Application.Current.Dispatcher.CheckAccess())
                        {
                            Application.Current.Dispatcher.Invoke(() => AlbumArtReady?.Invoke(null));
                        }
                        else
                        {
                            AlbumArtReady?.Invoke(null);
                        }
                    }
                    catch { }
                }
            });
        }

        /// <summary>
        /// Synchronously get a cached image if available
        /// </summary>
        public ImageSource? GetCachedAlbumArt(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return null;
            return _imageCache.TryGetValue(url, out var src) ? src : null;
        }

        public void Dispose()
        {
            _imageCache.Clear();
            try
            {
                _loggingService.LogDebug("[UI_ADAPTER] Dispose called - cache cleared");
            }
            catch { }
        }
    }
}