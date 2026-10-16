using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Lertaro.App.ViewModels.QuickPanel;
using Lertaro.App.Views.QuickPanel;
using Lertaro.Core;

namespace Lertaro.App.Tests.Views.QuickPanel;

[TestClass, DoNotParallelize]
public sealed class QuickPanelScrollSupportTests
{
    [StaTestMethod]
    public async Task ScrollingAcrossGroupsKeepsTheCorrectStickyHeaderAndAllRows()
    {
        var tab = new QuickPanelTab { Id = "workspace" };
        tab.Folders.Add(new() { Id = "first", Path = @"C:\first" });
        tab.Folders.Add(new() { Id = "second", Path = @"C:\second" });
        var settings = new QuickPanelSettings { Tabs = new() { tab } };
        var vm = new QuickPanelViewModel(() => settings, (source, _) => Task.FromResult(
            Enumerable.Range(0, 96).Select(i => new SearchResult
            {
                Name = $"file-{i:D3}.txt", Path = $@"{source.Path}\file-{i:D3}.txt"
            }).ToList()), () => { });
        await vm.RefreshAsync();
        Pump();
        Assert.HasCount(2, vm.Groups);
        foreach (var group in vm.Groups)
            while (group.LoadNextPage()) { }
        var window = new QuickPanelWindow(vm) { Width = 700, Height = 500, ShowActivated = false, IsStayOpen = true };
        try
        {
            window.Show(); Pump();
            var scroll = (ScrollViewer)window.FindName("GroupsScrollViewer");
            var sticky = (ContentControl)window.FindName("StickyGroupHeader");
            var groups = Children<Expander>(scroll).Where(e => e.DataContext is QuickPanelGroupViewModel).ToList();
            Assert.HasCount(2, groups);
            scroll.ScrollToVerticalOffset(groups[0].ActualHeight / 2); Pump();
            Assert.AreEqual(Visibility.Visible, sticky.Visibility);
            Assert.AreSame(vm.Groups[0], sticky.Content);
            scroll.ScrollToEnd(); Pump();
            Assert.AreSame(vm.Groups[1], sticky.Content);
            Assert.IsTrue(vm.Groups.All(g => g.Items.Count == 96));
            scroll.ScrollToTop(); Pump();
            Assert.AreEqual(Visibility.Collapsed, sticky.Visibility);
        }
        finally { window.Close(); }
    }

    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    private static IEnumerable<T> Children<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T typed) yield return typed;
            foreach (var nested in Children<T>(child)) yield return nested;
        }
    }
}
