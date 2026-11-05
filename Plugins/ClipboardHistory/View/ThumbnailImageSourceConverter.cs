using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace Lertaro.Plugins.ClipboardHistory.View;

/// <summary>
/// Loads a stored PNG as a small decoded thumbnail: DecodePixelWidth caps the decode buffer, so a
/// 4K screenshot costs a few kilobytes of memory per visible row instead of its full size. The
/// images directory prefix comes in the constructor; the converter receives bare file names.
/// Returns null for non-image rows (the template hides the thumbnail box for them anyway).
/// </summary>
public sealed class ThumbnailImageSourceConverter(string imagesDirectory) : IValueConverter
{
    public const int DecodeWidth = 88; // 2x the 44-dip thumbnail box for HiDPI crispness

    // Decoded thumbnails are cached per full path: the store's PNG names are content-addressed
    // (SHA-256), so a decode can never go stale, and every rebuild re-binding the same visible
    // rows would otherwise re-read and re-decode the same files from disk. Cleared wholesale when
    // it grows past the cap -- re-decoding a few entries after a clear is merely slow, not wrong.
    private static readonly ConcurrentDictionary<string, BitmapSource> Cache = new();
    private const int CacheCap = 256;

    public ThumbnailImageSourceConverter() : this(ResolveImagesDirectory()) { }

    /// <summary>Decodes a stored PNG into the cache off the UI thread (no-op on cache hit or
    /// failure), so the first scroll through an image-heavy history finds warm entries instead of
    /// paying a synchronous disk read plus decode per row.</summary>
    public static void PrimeCache(string imagesDirectory, IReadOnlyList<string> fileNames)
    {
        foreach (var fileName in fileNames)
        {
            if (string.IsNullOrEmpty(fileName)) continue;
            var path = Path.Combine(imagesDirectory, fileName);
            if (Cache.ContainsKey(path)) continue;
            try
            {
                if (!File.Exists(path)) continue;
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.DecodePixelWidth = DecodeWidth;
                bitmap.UriSource = new Uri(path, UriKind.Absolute);
                bitmap.EndInit();
                bitmap.Freeze();
                if (Cache.Count >= CacheCap) Cache.Clear();
                Cache[path] = bitmap;
            }
            catch (Exception ex) when (ex is IOException or NotSupportedException or InvalidOperationException)
            {
                // The binding path already handles a failed decode by showing the row without a
                // thumbnail; the preheat must not turn that into a crash off the UI thread.
            }
        }
    }

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string fileName || fileName.Length == 0) return null;
        var path = Path.Combine(imagesDirectory, fileName);
        if (Cache.TryGetValue(path, out var cached)) return cached;
        if (!File.Exists(path)) return null;

        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.DecodePixelWidth = DecodeWidth;
            bitmap.UriSource = new Uri(path, UriKind.Absolute);
            bitmap.EndInit();
            bitmap.Freeze();
            if (Cache.Count >= CacheCap) Cache.Clear();
            Cache[path] = bitmap;
            return bitmap;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or InvalidOperationException)
        {
            return null;
        }
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    private static string ResolveImagesDirectory()
    {
        var directory = PluginSdk.Services.UserDataService.GetUserDataDirectory();
        return string.IsNullOrEmpty(directory)
            ? string.Empty
            : Path.Combine(directory, "ClipboardHistory", "images");
    }
}
