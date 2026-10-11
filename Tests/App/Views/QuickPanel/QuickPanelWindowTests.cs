using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Lertaro.App.ViewModels.QuickPanel;
using Lertaro.Core;

namespace Lertaro.App.Tests.Views.QuickPanel;

// The same guard the settings page carries, for the same two bugs. A StaticResource that does not exist
// compiles perfectly happily and only throws when the window is first opened, and a binding whose path
// does not exist throws nothing at all and simply renders nothing -- a group heading that stays blank,
// a tab strip that never appears. Both have shipped from this window's neighbourhood before.
// [DoNotParallelize] because the binding check listens to PresentationTraceSources, which is
// process-wide: run alongside any other test that builds WPF elements and it collects that test's
// binding errors as if they were this window's.
[TestClass]
[DoNotParallelize]
public sealed class QuickPanelWindowTests
{
    // Two workspaces, each with one source that answers with one entry, so the strip, the group header
    // and both row templates all have something real to bind against.
    private static QuickPanelViewModel BuildViewModel()
    {
        var settings = new QuickPanelSettings { Tabs = new List<QuickPanelTab>() };
        foreach (var (id, path) in new[] { ("w1", @"C:\a"), ("w2", @"C:\b") })
        {
            var tab = new QuickPanelTab { Id = id, Name = id };
            tab.Folders.Add(new QuickPanelFolderSource { Id = id + "s", Path = path });
            settings.Tabs.Add(tab);
        }

        return new QuickPanelViewModel(
            () => settings,
            (source, _) => Task.FromResult(new List<SearchResult>
            {
                new()
                {
                    Name = "file.txt",
                    Path = System.IO.Path.Combine(source.Path, "file.txt"),
                    Metadata = new PluginSdk.Abstractions.FileMetadata(0, DateTime.Now, DateTime.Now, DateTime.Now),
                },
            }));
    }

    // Measured through the window's own content rather than the window: laying out a Window that was
    // never shown is not something WPF supports, and every binding under test hangs off the content.
    private static FrameworkElement BuildContent(QuickPanelViewModel viewModel)
    {
        var window = new Lertaro.App.Views.QuickPanel.QuickPanelWindow(viewModel);
        return (FrameworkElement)window.Content;
    }

    [StaTestMethod]
    public async Task EmptyFolderHasAVisibleDropArea()
    {
        var tab = new QuickPanelTab { Id = "empty" };
        tab.Folders.Add(QuickPanelFolderSource.For(System.IO.Path.GetTempPath(), QuickPanelSourceKind.All));
        var settings = new QuickPanelSettings { Tabs = new() { tab } };
        var vm = new QuickPanelViewModel(() => settings, (_, _) => Task.FromResult(new List<SearchResult>()), () => { });
        await vm.RefreshAsync();
        var window = new Lertaro.App.Views.QuickPanel.QuickPanelWindow(vm) { Width = 700, Height = 500 };
        try
        {
            window.Show();
            window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            var list = Find<System.Windows.Controls.ListBox>(window);
            Assert.IsNotNull(list);
            Assert.IsTrue(list.IsVisible);
            Assert.IsGreaterThanOrEqualTo(50.0, list.ActualHeight);
            Assert.AreEqual(0, list.Items.Count);
        }
        finally { window.Close(); }
    }

    [StaTestMethod]
    public async Task DraggingDisablesSelectionBandAndSwitchingSortKeepsGridPositions()
    {
        var vm = BuildViewModel();
        await vm.RefreshAsync();
        var window = new Lertaro.App.Views.QuickPanel.QuickPanelWindow(vm) { Width = 700, Height = 500 };
        try
        {
            window.Show();
            window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            var list = Find<System.Windows.Controls.ListBox>(window);
            Assert.IsNotNull(list);
            var group = Assert.IsInstanceOfType<QuickPanelGroupViewModel>(list.DataContext);
            var picker = Find<System.Windows.Controls.ComboBox>(window);
            Assert.IsNotNull(picker);
            Assert.HasCount(3, picker.Items);
            group.IsReordering = true;
            Assert.IsTrue(window.IsStayOpen);
            window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            var panel = Find<Lertaro.App.Views.QuickPanel.QuickPanelNumberedWrapPanel>(list);
            Assert.IsNotNull(panel);
            Assert.IsFalse(panel.UsesGridSlots, "Starting a drag must not change the saved sort.");
            Assert.IsGreaterThan(0.0, panel.ActualHeight);
            var path = group.Items[0].FullPath;
            group.PlaceInGrid(new[] { path }, 7);
            window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            var container = Assert.IsInstanceOfType<System.Windows.Controls.ListBoxItem>(list.ItemContainerGenerator.ContainerFromItem(group.Items.First(item => item.FullPath == path)));
            var borders = new List<System.Windows.Controls.Border>();
            Collect(container, borders);
            var iconBox = borders.Single(border => border.Name == "TileIcon");
            Assert.AreEqual(Lertaro.App.Converters.QuickPanelTileMetrics.IconWidthFor(container.ActualWidth), iconBox.ActualWidth, 0.01);
            Assert.AreEqual(Lertaro.App.Converters.QuickPanelTileMetrics.IconHeightFor(container.ActualWidth), iconBox.ActualHeight, 0.01);
            Assert.AreEqual(Lertaro.App.Converters.QuickPanelTileMetrics.CellHeightFor(container.ActualWidth), container.ActualHeight, 0.01);
            var previousSize = Lertaro.App.Converters.QuickPanelTileMetrics.IconSize;
            var previousPosition = (window.Left, window.Top);
            try
            {
                Lertaro.App.Views.QuickPanel.QuickPanelSizeButton.ApplySize(window, QuickPanelThumbnailSize.Small);
                window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                Assert.AreEqual(32.0, iconBox.ActualWidth, 0.01);
                Assert.AreEqual(56.0, container.ActualWidth, 0.01);
                Assert.AreEqual(62.0, container.ActualHeight, 0.01);
                Assert.AreEqual(previousPosition, (window.Left, window.Top));
                Assert.AreEqual(7, group.GridSlots[path]);
            }
            finally
            {
                Lertaro.App.Views.QuickPanel.QuickPanelSizeButton.ApplySize(window, previousSize);
                window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            }
            group.ItemLabels.Set(path, "Short label", true);
            window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            var texts = new List<System.Windows.Controls.TextBlock>();
            Collect(container, texts);
            var caption = texts.Single(text => text.Text == "Short label");
            Assert.AreEqual(Visibility.Collapsed, caption.Visibility);
            group.ItemLabels.Set(path, "", false);
            window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            Assert.AreEqual(Visibility.Visible, caption.Visibility);
            var restoredItem = group.Items.First(item => item.FullPath == path);
            Assert.AreEqual("file.txt", restoredItem.Name);
            Assert.AreEqual("file", caption.Text);
            var point = container.TranslatePoint(new Point(container.ActualWidth / 2, container.ActualHeight / 2), panel);
            Assert.AreEqual(7, panel.SlotAt(point));
            group.SortMode = QuickPanelSortMode.NameAscending;
            Assert.IsFalse(group.UsesGridSlots);
            group.SortMode = QuickPanelSortMode.Custom;
            Assert.AreEqual(7, group.GridSlots[path]);
            group.IsReordering = true;
            Assert.IsTrue(window.IsStayOpen);
            window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            Assert.IsFalse(Lertaro.App.Helpers.Visuals.RubberBandSelection.GetIsEnabled(list));
            Lertaro.App.Views.QuickPanel.QuickPanelItemReordering.Cancel(window);
            Assert.IsFalse(group.IsReordering);
            Assert.IsFalse(window.IsStayOpen);
            window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            Assert.IsTrue(Lertaro.App.Helpers.Visuals.RubberBandSelection.GetIsEnabled(list));
        }
        finally { window.Close(); }
    }

    [StaTestMethod]
    public async Task SortingAShownPanelKeepsItsPositionAndSize()
    {
        var vm = BuildViewModel();
        await vm.RefreshAsync();
        vm.Groups[0].Replace(new()
        {
            (new AppSearchResult { Name = "z.txt", FullPath = @"C:\a\z.txt" }, new DateTime(2026, 1, 2)),
            (new AppSearchResult { Name = "a.txt", FullPath = @"C:\a\a.txt" }, new DateTime(2026, 1, 1))
        });
        var window = new Lertaro.App.Views.QuickPanel.QuickPanelWindow(vm)
        {
            Left = 120, Top = 80, Width = 700, Height = 500, HasUserPosition = true
        };
        try
        {
            window.Show();
            window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            var before = new Rect(window.Left, window.Top, window.ActualWidth, window.ActualHeight);
            vm.Groups[0].ToggleSort();
            Assert.AreEqual("a.txt", vm.Groups[0].Items[0].Name);
            window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            Assert.AreEqual(before, new Rect(window.Left, window.Top, window.ActualWidth, window.ActualHeight));
        }
        finally { window.Close(); }
    }

    [StaTestMethod]
    public async Task Window_BuildsWithEveryResourceItReferences()
    {
        var viewModel = BuildViewModel();
        await viewModel.RefreshAsync();

        Assert.IsNotNull(BuildContent(viewModel));
    }

    [StaTestMethod]
    public async Task Window_LaysOutWithNoBrokenBindings()
    {
        var viewModel = BuildViewModel();
        await viewModel.RefreshAsync();

        var errors = new BindingErrorListener();
        var previousLevel = PresentationTraceSources.DataBindingSource.Switch.Level;
        PresentationTraceSources.Refresh();
        PresentationTraceSources.DataBindingSource.Listeners.Add(errors);
        PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Error;
        try
        {
            var content = BuildContent(viewModel);
            content.Measure(new Size(330, 300));
            content.Arrange(new Rect(0, 0, 330, 300));
            content.UpdateLayout();
        }
        finally
        {
            PresentationTraceSources.DataBindingSource.Listeners.Remove(errors);
            PresentationTraceSources.DataBindingSource.Switch.Level = previousLevel;
        }

        Assert.IsEmpty(errors.Messages, string.Join(Environment.NewLine, errors.Messages));
    }

    // Clicking blank space drops the selection; clicking a row or a header control does not. Driven
    // through the two decisions the handler makes rather than through a synthesized click, which WPF
    // gives no way to aim at a particular pixel of a laid-out tree.
    [StaTestMethod]
    public async Task ClickOnNothing_IsAnythingThatIsNotARowOrAControl()
    {
        var viewModel = BuildViewModel();
        await viewModel.RefreshAsync();
        var content = Laid(viewModel);

        var list = Find<System.Windows.Controls.ListBox>(content)!;
        var row = Find<System.Windows.Controls.ListBoxItem>(content)!;
        var button = Find<System.Windows.Controls.Primitives.ButtonBase>(content)!;

        Assert.IsTrue(Lertaro.App.Views.QuickPanel.QuickPanelWindow.IsClickOnNothing(list),
            "the list's own background is the blank space this exists for");
        Assert.IsFalse(Lertaro.App.Views.QuickPanel.QuickPanelWindow.IsClickOnNothing(row));
        Assert.IsFalse(Lertaro.App.Views.QuickPanel.QuickPanelWindow.IsClickOnNothing(button),
            "collapsing a group or switching its order is not a click on nothing");
    }

    // Every group, not just the one under the pointer: each renders its own list, so a selection left
    // in another group would keep a row looking selected that a keystroke no longer acts on.
    [StaTestMethod]
    public async Task ClearSelection_EmptiesEveryGroupsList()
    {
        var viewModel = BuildViewModel();
        await viewModel.RefreshAsync();
        var content = Laid(viewModel);

        var lists = new List<System.Windows.Controls.ListBox>();
        Collect(content, lists);
        Assert.IsNotEmpty(lists);
        foreach (var list in lists)
            list.SelectedIndex = 0;

        Lertaro.App.Views.QuickPanel.QuickPanelWindow.ClearSelection(content);

        Assert.IsTrue(lists.TrueForAll(list => list.SelectedItems.Count == 0));
    }

    // Hold the jump-to-Nth-result modifier and press a number. The combo has to be spelled the way the
    // hotkey recorder spells one -- "Ctrl+D3", not "Ctrl+3" -- because a bare digit parses as the raw
    // Key ordinal (3 is Key.Tab) and matches nothing at all, which is how this shipped doing nothing.
    [TestMethod]
    [DataRow(Key.D3, ModifierKeys.Control, "Ctrl", 3)]
    [DataRow(Key.D1, ModifierKeys.Control, "Ctrl", 1)]
    [DataRow(Key.D9, ModifierKeys.Control, "Ctrl", 9)]
    [DataRow(Key.NumPad3, ModifierKeys.Control, "Ctrl", 3)]
    [DataRow(Key.D2, ModifierKeys.Alt, "Alt", 2)]
    public void WorkspaceIndex_IsTheDigitHeldWithTheConfiguredModifier(
        Key key, ModifierKeys modifiers, string jumpModifier, int expected)
        => Assert.AreEqual(expected,
            Lertaro.App.Views.QuickPanel.QuickPanelWindow.WorkspaceIndexFor(key, modifiers, jumpModifier));

    [TestMethod]
    [DataRow(Key.D3, ModifierKeys.None, "Ctrl", "a bare digit is a plain keystroke, not this shortcut")]
    [DataRow(Key.D3, ModifierKeys.Control | ModifierKeys.Shift, "Ctrl", "the modifiers must match exactly")]
    [DataRow(Key.D3, ModifierKeys.Alt, "Ctrl", "held with the wrong modifier")]
    [DataRow(Key.A, ModifierKeys.Control, "Ctrl", "not a number at all")]
    [DataRow(Key.D0, ModifierKeys.Control, "Ctrl", "the strip is 1-9; there is no zeroth workspace")]
    [DataRow(Key.D3, ModifierKeys.Control, "", "no modifier configured means no shortcut")]
    public void WorkspaceIndex_IsZeroForAnythingElse(Key key, ModifierKeys modifiers, string jumpModifier, string because)
        => Assert.AreEqual(0,
            Lertaro.App.Views.QuickPanel.QuickPanelWindow.WorkspaceIndexFor(key, modifiers, jumpModifier), because);

    private static FrameworkElement Laid(QuickPanelViewModel viewModel)
    {
        var content = BuildContent(viewModel);
        content.Measure(new Size(330, 300));
        content.Arrange(new Rect(0, 0, 330, 300));
        content.UpdateLayout();
        return content;
    }

    private static T? Find<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T match) return match;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            if (Find<T>(VisualTreeHelper.GetChild(root, i)) is { } found) return found;
        }
        return null;
    }

    private static void Collect<T>(DependencyObject root, List<T> into) where T : DependencyObject
    {
        if (root is T match) into.Add(match);
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            Collect(VisualTreeHelper.GetChild(root, i), into);
    }

    private sealed class BindingErrorListener : TraceListener
    {
        public List<string> Messages { get; } = new();

        public override void Write(string? message) { }

        public override void WriteLine(string? message)
        {
            if (!string.IsNullOrEmpty(message))
                Messages.Add(message);
        }
    }
}
