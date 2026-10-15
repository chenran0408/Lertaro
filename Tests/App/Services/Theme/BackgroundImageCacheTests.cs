using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Lertaro.App.Services.Theme;

namespace Lertaro.App.Tests.Services.Theme;

[TestClass, DoNotParallelize]
public sealed class BackgroundImageCacheTests
{
    [TestInitialize] public void Initialize() => ResetCache();
    [TestCleanup] public void Cleanup() => ResetCache();

    private static void ResetCache()
    {
        var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static;
        lock (typeof(BackgroundImageCache).GetField("Gate", flags)!.GetValue(null)!)
        {
            typeof(BackgroundImageCache).GetField("_key", flags)!.SetValue(null, null);
            typeof(BackgroundImageCache).GetField("_pending", flags)!.SetValue(null, null);
        }
    }

    [TestMethod]
    public async Task MissingRelativeAndCorruptFilesReturnFallback()
    {
        Assert.IsNull(await BackgroundImageCache.LoadAsync("relative.png"));
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".png");
        try
        {
            Assert.IsNull(await BackgroundImageCache.LoadAsync(path));
            await File.WriteAllTextAsync(path, "invalid image");
            Assert.IsNull(await BackgroundImageCache.LoadAsync(path));
        }
        finally { File.Delete(path); }
    }

    [StaTestMethod]
    public async Task PortraitIsBoundedAndImageDoesNotLockOriginalFile()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".png");
        try
        {
            var pixels = new byte[40 * 4000 * 4];
            var bitmap = BitmapSource.Create(40, 4000, 96, 96, PixelFormats.Bgra32, null, pixels, 40 * 4);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var stream = File.Create(path)) encoder.Save(stream);
            var image = await BackgroundImageCache.LoadAsync(path);
            Assert.IsNotNull(image);
            Assert.IsLessThanOrEqualTo(2048, image.PixelHeight);
            Assert.IsTrue(image.IsFrozen);
            File.Delete(path);
            Assert.IsFalse(File.Exists(path));
        }
        finally { File.Delete(path); }
    }
}
