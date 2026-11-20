using System.Windows;
using System.Windows.Interop;
using System.Windows.Shell;
using Lertaro.App.Views.Controls;

namespace Lertaro.App.Tests.Views.Controls;

[TestClass, DoNotParallelize]
public sealed class AppWindowFrameTests
{
    [STATestMethod]
    [DataRow(WindowStyle.None, ResizeMode.CanResizeWithGrip, false, true)]
    [DataRow(WindowStyle.None, ResizeMode.CanResize, false, true)]
    [DataRow(WindowStyle.None, ResizeMode.NoResize, false, false)]
    [DataRow(WindowStyle.None, ResizeMode.CanResize, true, false)]
    [DataRow(WindowStyle.SingleBorderWindow, ResizeMode.CanResize, false, false)]
    public void OnlyOpaqueResizableWindowsWithCustomTitleBarsExtendTheirClientArea(
        WindowStyle style, ResizeMode resizing, bool transparent, bool expected)
    {
        var window = new AppWindow { WindowStyle = style, ResizeMode = resizing, AllowsTransparency = transparent };
        try
        {
            new WindowInteropHelper(window).EnsureHandle();
            var chrome = WindowChrome.GetWindowChrome(window);
            Assert.AreEqual(expected, chrome != null);
            if (chrome != null)
            {
                Assert.AreEqual(new Thickness(0), chrome.GlassFrameThickness);
                Assert.AreEqual(0d, chrome.CaptionHeight);
                Assert.IsFalse(chrome.UseAeroCaptionButtons);
            }
        }
        finally { window.Close(); }
    }
}
