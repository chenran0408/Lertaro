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

    public ThumbnailImageSourceConverter() : this(ResolveImagesDirectory()) { }

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string fileName || fileName.Length == 0) return null;
        var path = Path.Combine(imagesDirectory, fileName);
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
