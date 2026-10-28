using System.Windows;
using GlassDesk.UI.Controls;

namespace Lertaro.App.Tests.GlassDesk;

[TestClass]
public sealed class LiquidGlassOpticsBufferReuseTests
{
    [TestMethod]
    public void Render_ReusedWorkBuffersMatchFreshOpticsBitForBit()
    {
        const int width = 48;
        const int height = 36;
        var source = new byte[width * height * 4];
        for (var pixel = 0; pixel < width * height; pixel++)
        {
            source[pixel * 4] = (byte)(pixel * 17);
            source[pixel * 4 + 1] = (byte)(pixel * 29);
            source[pixel * 4 + 2] = (byte)(pixel * 43);
            source[pixel * 4 + 3] = 255;
        }

        var reused = new LiquidGlassOptics();
        var first = reused.Render(source, width, height, new Size(width, height), 9);
        var firstSnapshot = (byte[])first.Clone();
        var second = reused.Render(source, width, height, new Size(width, height), 9);
        var fresh = new LiquidGlassOptics().Render(source, width, height, new Size(width, height), 9);

        CollectionAssert.AreEqual(firstSnapshot, first, "A later frame must not mutate a returned frame.");
        CollectionAssert.AreEqual(firstSnapshot, second, "Reused scratch buffers must preserve output bytes.");
        CollectionAssert.AreEqual(second, fresh, "Fresh and warmed optics must render identical bytes.");
    }
}
