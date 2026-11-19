using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Lertaro.PluginSdk.Services;
using Lertaro.Plugins.ClipboardHistory.View;

namespace Lertaro.Plugins.ClipboardHistory.Tests.View;

[TestClass]
[DoNotParallelize]
public sealed class ClipboardPanelViewTests
{
    [STATestMethod]
    public void WheelMovesOneResultPerNotchAndSkipsGroupHeaders()
    {
        var panel = new ClipboardPanelView();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var newest = ClipboardInstantProviderTests.Entry(2, "new", true, now);
        var older = ClipboardInstantProviderTests.Entry(1, "old", false, now - 1000);
        panel.ApplySnapshot([newest, older]);

        panel.NavigateWheel(-60);
        Assert.AreEqual(2L, ((ClipboardPanelItem)panel.Lst.SelectedItem).Id);
        panel.NavigateWheel(-60);
        Assert.AreEqual(1L, ((ClipboardPanelItem)panel.Lst.SelectedItem).Id);
        panel.NavigateWheel(120);
        Assert.AreEqual(2L, ((ClipboardPanelItem)panel.Lst.SelectedItem).Id);
    }

    [STATestMethod]
    public void NarrowWindowsShowHistoryFirstAndSwitchToDetailsWithoutAnEmptyColumn()
    {
        var panel = new ClipboardPanelView();
        panel.ApplySnapshot([ClipboardInstantProviderTests.Entry(1, "First clip", false, 1)]);
        panel.ApplyPreviewLayout(440);
        Assert.AreEqual(1, Grid.GetRow(panel.ToolbarActions));
        Assert.AreEqual(2, Grid.GetColumnSpan(panel.TypeFilters));
        Assert.AreEqual(Visibility.Visible, panel.HistoryHost.Visibility);
        Assert.AreEqual(Visibility.Collapsed, panel.PreviewHost.Visibility);
        Assert.AreEqual(0, panel.PreviewColumn.Width.Value);
        Assert.AreEqual(Visibility.Collapsed, panel.PreviewSplitter.Visibility);

        panel.TogglePreview();
        Assert.AreEqual(Visibility.Collapsed, panel.HistoryHost.Visibility);
        Assert.AreEqual(Visibility.Visible, panel.PreviewHost.Visibility);
        Assert.AreEqual(0, Grid.GetColumn(panel.PreviewHost));
        Assert.IsNotNull(panel.NoteEditor);
        Assert.IsTrue(panel.PasteButton.IsEnabled);

        panel.TogglePreview();
        Assert.AreEqual(Visibility.Visible, panel.HistoryHost.Visibility);
        Assert.AreEqual(1L, ((ClipboardPanelItem)panel.Lst.SelectedItem).Id);

        panel.TogglePreview();
        panel.ApplyPreviewLayout(780);
        Assert.IsFalse(panel.IsCompactPreviewOpen);
        Assert.AreEqual(Visibility.Visible, panel.HistoryHost.Visibility);
        Assert.AreEqual(0, Grid.GetRow(panel.ToolbarActions));
        Assert.AreEqual(1, Grid.GetColumn(panel.ToolbarActions));
        Assert.AreEqual(0, Grid.GetRow(panel.PreviewHost));
        Assert.AreEqual(2, Grid.GetColumn(panel.PreviewHost));
        Assert.AreEqual(300, panel.PreviewColumn.Width.Value);
        Assert.AreEqual(Visibility.Visible, panel.PreviewSplitter.Visibility);

        panel.PreviewVisible = false;
        panel.ApplyPreviewLayout(780);
        Assert.AreEqual(Visibility.Collapsed, panel.PreviewHost.Visibility);
        Assert.AreEqual(Visibility.Collapsed, panel.PreviewSplitter.Visibility);
        Assert.AreEqual(0, panel.PreviewColumn.Width.Value);
    }

    [STATestMethod, DoNotParallelize]
    public void TypingPartOfAClipFindsAndSelectsItUsingTheHostMatcherContract()
    {
        var oldMatcher = FuzzyMatchService.IsMatchFunc;
        FuzzyMatchService.IsMatchFunc = static (pattern, text) => text.Contains(pattern, StringComparison.OrdinalIgnoreCase);
        try
        {
            var panel = new ClipboardPanelView();
            panel.ApplySnapshot([
                ClipboardInstantProviderTests.Entry(3, "unrelated clip", false, 3),
                ClipboardInstantProviderTests.Entry(2, "Release check results", false, 2),
            ]);
            panel.SearchBox.Text = "check";

            WaitForFilter(panel);

            Assert.AreEqual(2L, ((ClipboardPanelItem)panel.Lst.SelectedItem).Id);
            Assert.IsTrue(panel.PasteButton.IsEnabled);
        }
        finally { FuzzyMatchService.IsMatchFunc = oldMatcher; }
    }

    [STATestMethod]
    public void SnapshotRefreshKeepsMultiSelectionAndSearchHasItsOwnEmptyState()
    {
        var panel = new ClipboardPanelView();
        var entries = new[]
        {
            ClipboardInstantProviderTests.Entry(3, "latest", false, 3),
            ClipboardInstantProviderTests.Entry(2, "working note", false, 2),
            ClipboardInstantProviderTests.Entry(1, "older", false, 1),
        };
        panel.ApplySnapshot(entries);
        Assert.IsTrue(panel.PasteButton.IsEnabled);
        Assert.IsTrue(panel.CopyButton.IsEnabled);
        Assert.AreEqual(Visibility.Visible, panel.SearchWatermark.Visibility);

        panel.Lst.SelectedItems.Clear();
        panel.Lst.SelectedItems.Add(panel._items.Single(row => row.Id == 1));
        panel.Lst.SelectedItems.Add(panel._items.Single(row => row.Id == 2));
        panel.SelectContextRow(panel._items.Single(row => row.Id == 2));
        Assert.HasCount(2, panel.Lst.SelectedItems);
        panel.ApplySnapshot([ClipboardInstantProviderTests.Entry(4, "new clip", false, 4), .. entries]);
        var selected = panel.Lst.SelectedItems.Cast<ClipboardPanelItem>().Select(row => row.Id).ToArray();
        CollectionAssert.AreEquivalent(new long[] { 1, 2 }, selected);
        Assert.IsTrue(panel.PasteButton.IsEnabled);
        Assert.IsFalse(panel.CopyButton.IsEnabled);
        panel.SelectContextRow(panel._items.Single(row => row.Id == 3));
        Assert.HasCount(1, panel.Lst.SelectedItems);
        Assert.AreEqual(3L, ((ClipboardPanelItem)panel.Lst.SelectedItem).Id);

        panel.SearchBox.Text = "no-such-item";
        Assert.AreEqual(Visibility.Collapsed, panel.SearchWatermark.Visibility);
        Assert.IsFalse(panel.PasteButton.IsEnabled);
        Assert.IsTrue(panel.SearchPending);

        panel.SearchBox.Text = "another-no-match";
        WaitForFilter(panel);

        Assert.AreEqual(Visibility.Visible, panel.EmptyText.Visibility);
        Assert.IsFalse(panel.PasteButton.IsEnabled);
    }

    private static void WaitForFilter(ClipboardPanelView panel)
    {
        var frame = new DispatcherFrame();
        var deadline = DateTime.UtcNow.AddSeconds(3);
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(10) };
        timer.Tick += (_, _) =>
        {
            if (!panel.SearchPending || DateTime.UtcNow >= deadline)
                frame.Continue = false;
        };
        timer.Start();
        Dispatcher.PushFrame(frame);
        timer.Stop();

        Assert.IsFalse(panel.SearchPending);
    }
}
