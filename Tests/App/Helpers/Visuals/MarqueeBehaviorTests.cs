using Lertaro.App.Helpers.Visuals;

namespace Lertaro.App.Tests.Helpers.Visuals;

[TestClass, DoNotParallelize]
public sealed class MarqueeBehaviorTests
{
    [StaTestMethod]
    [DataRow(32)]
    [DataRow(100)]
    [DataRow(300)]
    public void RemovingLabelsReleasesTheirSizeHandlersWhileTheParentRemainsOpen(int count)
    {
        var parent = new System.Windows.Controls.Grid();
        var window = new System.Windows.Window { Content = parent, Width = 300, Height = 150, ShowActivated = false };
        try
        {
            window.Show();
            var labels = Enumerable.Range(0, count).Select(_ => AddAndRemove(parent)).ToArray();
            Pump();
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            Assert.AreEqual(0, labels.Count(label => label.IsAlive), "Removed labels must not remain rooted by the live parent's SizeChanged event.");
            GC.KeepAlive(parent);
        }
        finally { window.Close(); }
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static WeakReference AddAndRemove(System.Windows.Controls.Grid parent)
    {
        var text = new System.Windows.Controls.TextBlock { Text = "A long result name", Width = 500 };
        MarqueeBehavior.SetEnableMarquee(text, true);
        parent.Children.Add(text); parent.UpdateLayout(); Pump();
        parent.Children.Remove(text); Pump();
        return new WeakReference(text);
    }

    private static void Pump() => System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);

    [TestMethod]
    public void CalculateMarqueeSpeed_UsesBaseSpeedForShortOverflow()
    {
        Assert.AreEqual(40d, MarqueeBehavior.CalculateMarqueeSpeed(0));
        Assert.AreEqual(40d, MarqueeBehavior.CalculateMarqueeSpeed(-20));
    }

    [TestMethod]
    public void CalculateMarqueeSpeed_IncreasesForLongerOverflow() => Assert.IsGreaterThan(
            MarqueeBehavior.CalculateMarqueeSpeed(100),
            MarqueeBehavior.CalculateMarqueeSpeed(500));

    [TestMethod]
    public void CalculateMarqueeSpeed_IsCapped() => Assert.AreEqual(240d, MarqueeBehavior.CalculateMarqueeSpeed(2000));
}
