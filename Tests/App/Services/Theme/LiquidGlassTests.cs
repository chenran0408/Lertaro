using Lertaro.App.Services.Theme;
using Lertaro.App.Helpers.Visuals;
using Lertaro.Core;
namespace Lertaro.App.Tests.Services.Theme;

[TestClass, DoNotParallelize]
public sealed class LiquidGlassTests
{
    [TestMethod]
    public void ReservedLevelsKeepValidatedLightProfileUntilEnabled()
    {
        foreach (var requested in Enum.GetValues<GlassLevel>())
        {
            Assert.AreEqual(GlassLevel.LightRefraction, LiquidGlassProfile.EffectiveLevel(requested));
            Assert.AreEqual(new LiquidGlassProfile(9, 3, 30), LiquidGlassProfile.Resolve(requested));
        }
    }
    [TestMethod]
    public void CaptureBoundsHandleNegativeMonitorsAndFractionalDpiWithoutLargeAllocations()
    {
        Assert.AreEqual((-1953, -33, 866, 666), LiquidGlassSession.Region(-1920.5, -.5, -1119.5, 600.25));
        Assert.AreEqual(default, LiquidGlassSession.Region(0, 0, double.NaN, 100));
        Assert.AreEqual(default, LiquidGlassSession.Region(0, 0, 10000, 10000));
        Assert.AreEqual(default, LiquidGlassSession.Region(100, 100, 0, 0));
    }
    [StaTestMethod]
    public void EmbeddedShaderLoadsWithExpectedVersion()
    {
        var effect = new LiquidGlassEffect();
        Assert.IsNotNull(effect);
        var resource = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Lertaro.App;component/Resources/Shaders/LiquidGlass.ps"));
        using var reader = new System.IO.BinaryReader(resource.Stream);
        Assert.AreEqual(0xffff0300u, reader.ReadUInt32());
    }
    [StaTestMethod]
    public void DesktopCaptureUpdatesStopsWhenHiddenAndResumes()
    {
        if (!NativeMaterialHost.IsSupported || (System.Windows.Media.RenderCapability.Tier >> 16) < 2)
            Assert.Inconclusive("Requires supported desktop composition and hardware rendering.");
        var stage = new System.Windows.Window { Width = 600, Height = 400, Left = 60, Top = 60, Topmost = true, ShowActivated = false, Background = System.Windows.Media.Brushes.Red };
        var surface = new System.Windows.Controls.Grid();
        var paint = new System.Windows.Controls.Border(); surface.Children.Add(paint);
        var window = new System.Windows.Window { Width = 300, Height = 180, Left = 150, Top = 150, Content = surface, ShowActivated = false };
        stage.Show(); window.Owner = stage; window.Show();
        LiquidGlassSession? session = null;
        try
        {
            session = new LiquidGlassSession(surface, paint, window);
            WaitUntil(() => HasColor(paint, 0, 0, 255));
            stage.Background = System.Windows.Media.Brushes.Blue;
            WaitUntil(() => HasColor(paint, 255, 0, 0));
            window.Hide();
            Assert.IsFalse(session.IsCapturing);
            Assert.IsNull(paint.Effect);
            window.Show();
            WaitUntil(() => session.IsCapturing && HasColor(paint, 255, 0, 0));
            session.Dispose();
            Assert.IsFalse(session.IsCapturing);
            Assert.IsNull(paint.Effect);
        }
        finally { session?.Dispose(); window.Close(); stage.Close(); }
    }
    private static bool HasColor(System.Windows.Controls.Border paint, byte blue, byte green, byte red)
    {
        if (paint.Effect is not LiquidGlassEffect effect || effect.Input is not System.Windows.Media.ImageBrush brush
            || brush.ImageSource is not System.Windows.Media.Imaging.BitmapSource bitmap) return false;
        var pixel = new byte[4];
        bitmap.CopyPixels(new System.Windows.Int32Rect(bitmap.PixelWidth / 2, bitmap.PixelHeight / 2, 1, 1), pixel, 4, 0);
        return pixel[0] == blue && pixel[1] == green && pixel[2] == red;
    }
    private static void WaitUntil(Func<bool> condition)
    {
        var frame = new System.Windows.Threading.DispatcherFrame();
        var timeout = System.Diagnostics.Stopwatch.StartNew();
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) };
        timer.Tick += (_, _) => { if (condition() || timeout.Elapsed.TotalSeconds > 4) frame.Continue = false; };
        timer.Start();
        try { System.Windows.Threading.Dispatcher.PushFrame(frame); }
        finally { timer.Stop(); }
        Assert.IsTrue(condition(), "Expected desktop frame or lifecycle transition did not arrive.");
    }

}
