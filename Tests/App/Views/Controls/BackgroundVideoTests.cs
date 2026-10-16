using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Lertaro.App.Services.Theme;
using Lertaro.App.ViewModels.Settings.General;
using Lertaro.App.Views.Controls;
using Lertaro.Core;

namespace Lertaro.App.Tests.Views.Controls;

[TestClass, DoNotParallelize]
public sealed class BackgroundVideoTests
{
    private static string Fixture => Path.Combine(AppContext.BaseDirectory, "Fixtures", "background-loop.mp4");

    [StaTestMethod]
    public void TwoWindowsShareOneSilentLoopAndOnlyPauseWhenEverySurfaceIsHidden()
    {
        var first = new BackgroundSurface();
        var second = new BackgroundSurface();
        var one = CreateWindow(first);
        var two = CreateWindow(second);
        var playback = BackgroundVideoPlayback.Current;
        try
        {
            BackgroundManager.Preview(new() { Kind = BackgroundKind.Image, ImagePath = Fixture, Opacity = .4 });
            one.Show(); two.Show();
            PumpUntil(() => playback.Drawing.Children.Count == 1);
            var player = playback.Player!;
            var loops = 0;
            player.MediaEnded += (_, _) => loops++;
            Assert.AreSame(Brush(first).Drawing, Brush(second).Drawing);
            Assert.IsTrue(player.IsMuted); Assert.AreEqual(0d, player.Volume);
            Assert.IsFalse(first.IsHitTestVisible);
            Assert.AreEqual(.4, Brush(first).Opacity);
            PumpUntil(() => player.Position.TotalMilliseconds > 150);
            var initialFrame = RenderVideo(first);
            PumpFor(250);
            Assert.IsFalse(initialFrame.SequenceEqual(RenderVideo(first)), "The displayed video frames must advance.");
            PumpUntil(() => loops > 0);
            Assert.IsTrue(playback.IsPlaying);
            one.Hide(); Pump();
            Assert.IsTrue(playback.IsPlaying, "The other visible window still needs the video.");
            two.Hide(); Pump();
            Assert.IsFalse(playback.IsPlaying);
            var paused = player.Position;
            PumpFor(250);
            Assert.IsLessThan(80d, Math.Abs((player.Position - paused).TotalMilliseconds));
            two.Show(); Pump();
            Assert.AreSame(player, playback.Player);
            Assert.IsTrue(playback.IsPlaying);
            BackgroundManager.Preview(new() { Kind = BackgroundKind.Image, ImagePath = Fixture, Opacity = 0, FitImage = true });
            Assert.IsFalse(playback.IsPlaying);
            Assert.AreSame(player, playback.Player);
            Assert.AreEqual(Stretch.Uniform, Brush(second).Stretch);
            BackgroundManager.Preview(new() { Kind = BackgroundKind.Image, ImagePath = Fixture, Opacity = .8 });
            Assert.IsTrue(playback.IsPlaying);
            Assert.AreSame(player, playback.Player, "Opacity changes must not reopen the decoder.");
            two.WindowState = WindowState.Minimized; Pump();
            Assert.IsFalse(playback.IsPlaying);
            two.WindowState = WindowState.Normal; Pump();
            Assert.IsTrue(playback.IsPlaying);
            one.Close(); two.Close(); Pump();
            Assert.IsNull(playback.Player);
            Assert.IsEmpty(playback.Drawing.Children);
            Assert.IsFalse(playback.IsPlaying);
        }
        finally { one.Close(); two.Close(); BackgroundManager.EndPreview(); }
    }

    [StaTestMethod]
    public void CorruptVideoFallsBackAndReportsTheFailureThenAnotherSelectionCanPlay()
    {
        var invalid = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".mp4");
        File.WriteAllText(invalid, "not a video");
        var vm = new BackgroundSettingsViewModel(new UserSettings());
        var surface = new BackgroundSurface();
        surface.Resources["ContentBg"] = Brushes.Black;
        var window = CreateWindow(surface);
        try
        {
            vm.ImagePath = invalid; vm.UseImage = true; window.Show();
            PumpUntil(() => BackgroundVideoPlayback.Current.FailedPath == invalid);
            Assert.IsFalse(BackgroundVideoPlayback.Current.IsPlaying);
            Assert.IsEmpty(BackgroundVideoPlayback.Current.Drawing.Children);
            Assert.IsFalse(string.IsNullOrEmpty(vm.Status));
            Assert.AreSame(Brushes.Black, surface.Background);
            vm.ImagePath = Fixture;
            PumpUntil(() => BackgroundVideoPlayback.Current.Drawing.Children.Count == 1);
            PumpUntil(() => vm.Status.Length == 0);
            vm.ResetCommand.Execute(null); Pump();
            Assert.IsNull(BackgroundVideoPlayback.Current.Player);
            Assert.AreEqual(Visibility.Collapsed, surface.Visibility);
        }
        finally { window.Close(); vm.Cleanup(); File.Delete(invalid); }
    }

    [StaTestMethod]
    public void VideoChosenForAHiddenWindowDoesNotStartUntilItIsShown()
    {
        var surface = new BackgroundSurface();
        var window = CreateWindow(surface);
        try
        {
            BackgroundManager.Preview(new() { Kind = BackgroundKind.Image, ImagePath = Fixture });
            window.Show(); window.Hide();
            PumpUntil(() => ((Border)surface.Children[0]).Background is DrawingBrush);
            Assert.IsNull(BackgroundVideoPlayback.Current.Player);
            window.Show();
            PumpUntil(() => BackgroundVideoPlayback.Current.Player?.HasVideo == true);
        }
        finally { window.Close(); BackgroundManager.EndPreview(); }
    }

    [TestMethod]
    [DataRow("relative.mp4")]
    [DataRow("https://example.com/background.mp4")]
    public async Task VideoSourceMustBeAnExistingLocalFile(string path) =>
        Assert.IsFalse(await BackgroundVideoPlayback.IsReadableAsync(path));

    [StaTestMethod]
    public void RapidCancelBeforeVideoValidationCompletesDoesNotReopenAStaleDecoder()
    {
        var surface = new BackgroundSurface();
        var window = CreateWindow(surface);
        try
        {
            BackgroundManager.Preview(new() { Kind = BackgroundKind.Image, ImagePath = Fixture });
            window.Show();
            BackgroundManager.Preview(new());
            PumpFor(200);
            Assert.IsNull(BackgroundVideoPlayback.Current.Player);
            Assert.AreEqual(Visibility.Collapsed, surface.Visibility);
        }
        finally { window.Close(); BackgroundManager.EndPreview(); }
    }

    [StaTestMethod]
    public void SwitchingToImageOrCancelingVideoPreviewReleasesTheDecoderAndPreservesSettings()
    {
        var settings = new UserSettings();
        var vm = new BackgroundSettingsViewModel(settings) { ImagePath = Fixture, UseImage = true };
        var surface = new BackgroundSurface();
        var window = CreateWindow(surface);
        try
        {
            window.Show(); PumpUntil(() => BackgroundVideoPlayback.Current.Player?.HasVideo == true);
            vm.CancelCommand.Execute(null); Pump();
            Assert.AreEqual(BackgroundKind.Theme, settings.Background.Kind);
            Assert.IsNull(BackgroundVideoPlayback.Current.Player);
            vm.UseImage = true; vm.ImagePath = Fixture;
            PumpUntil(() => BackgroundVideoPlayback.Current.Player?.HasVideo == true);
            vm.ImagePath = Fixture + ".png";
            PumpUntil(() => BackgroundVideoPlayback.Current.Player == null);
            Assert.IsNull(((Border)surface.Children[0]).Background);
        }
        finally { window.Close(); vm.Cleanup(); }
    }

    private static Window CreateWindow(BackgroundSurface surface) => new()
    { Content = surface, Width = 200, Height = 150, ShowActivated = false, ShowInTaskbar = false, Left = 50, Top = 50 };
    private static DrawingBrush Brush(BackgroundSurface surface) => Assert.IsInstanceOfType<DrawingBrush>(((Border)surface.Children[0]).Background);
    private static byte[] RenderVideo(FrameworkElement surface)
    {
        surface.UpdateLayout();
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)surface.ActualWidth, (int)surface.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(surface);
        var bytes = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
        bitmap.CopyPixels(bytes, bitmap.PixelWidth * 4, 0);
        Assert.IsTrue(Enumerable.Range(0, bytes.Length / 4).Any(i => bytes[i * 4] != bytes[i * 4 + 1] || bytes[i * 4 + 1] != bytes[i * 4 + 2]),
            "The background must contain rendered color video pixels, not just a running clock.");
        return bytes;
    }
    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    private static void PumpFor(int milliseconds)
    {
        var deadline = Environment.TickCount64 + milliseconds;
        PumpUntil(() => Environment.TickCount64 >= deadline);
    }
    private static void PumpUntil(Func<bool> condition)
    {
        var frame = new DispatcherFrame(); var deadline = Environment.TickCount64 + 10000;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(10) };
        timer.Tick += (_, _) => { if (condition() || Environment.TickCount64 >= deadline) frame.Continue = false; };
        timer.Start(); try { Dispatcher.PushFrame(frame); } finally { timer.Stop(); }
        var playback = BackgroundVideoPlayback.Current;
        Assert.IsTrue(condition(), $"Media did not finish: playing={playback.IsPlaying}, " +
            $"video={playback.Player?.HasVideo}, position={playback.Player?.Position}, " +
            $"failed={playback.FailedPath}, renderTier={RenderCapability.Tier}.");
    }
}
