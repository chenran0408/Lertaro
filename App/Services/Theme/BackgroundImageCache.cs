using System.IO;
using System.Windows.Media.Imaging;

namespace Lertaro.App.Services.Theme;

// Keep only the most recent image, decoded off the UI thread and bounded to a display-sized bitmap.
internal static class BackgroundImageCache
{
    private static readonly object Gate = new();
    private static string? _key;
    private static Task<BitmapSource?>? _pending;

    internal static async Task<BitmapSource?> LoadAsync(string path)
    {
        // File metadata can block too (especially disconnected shares); never probe it on the dispatcher.
        return await Task.Run(async () =>
        {
            string key;
            try
            {
                if (!Path.IsPathFullyQualified(path)) return null;
                var info = new FileInfo(path);
                if (!info.Exists || info.Length > 64 * 1024 * 1024) return null;
                key = $"{path}|{info.LastWriteTimeUtc.Ticks}|{info.Length}";
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            { return null; }
            Task<BitmapSource?> pending;
            lock (Gate)
            {
                if (_key == key && _pending != null) pending = _pending;
                else
                {
                    _key = key;
                    pending = _pending = DecodeAsync(path);
                }
            }
            return await pending;
        });
    }

    private static Task<BitmapSource?> DecodeAsync(string path) => Task.Run<BitmapSource?>(() =>
    {
        try
        {
            using var stream = File.OpenRead(path);
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
            var frame = decoder.Frames[0];
            int width = frame.PixelWidth, height = frame.PixelHeight;
            stream.Position = 0;
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            if (width >= height) image.DecodePixelWidth = Math.Min(width, 2048);
            else image.DecodePixelHeight = Math.Min(height, 2048);
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.Runtime.InteropServices.COMException)
        { return null; }
    });
}
