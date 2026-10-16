using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Lertaro.App.ViewModels.QuickPanel;
using Lertaro.App.Views.QuickPanel;
using Lertaro.Core;
using ContextMenu = System.Windows.Controls.ContextMenu;
using MenuItem = System.Windows.Controls.MenuItem;

namespace Lertaro.App.Tests.Views.QuickPanel;

[TestClass]
[DoNotParallelize]
public sealed class QuickPanelItemMenuTests
{
    [StaTestMethod]
    public void ReopeningReusesTheMenuAndReplacesItsActions()
    {
        var vm = new QuickPanelViewModel(() => new QuickPanelSettings(), (_, _) => Task.FromResult(new List<SearchResult>()), () => { });
        var item = new AppSearchResult { Name = "file.txt", FullPath = @"C:\Data\file.txt" };
        var group = new QuickPanelGroupViewModel("source", "Source", "", new() { (item, null) });
        var list = new ListBox { DataContext = group, ItemsSource = group.Items };
        var window = new QuickPanelWindow(vm) { Content = list, Width = 700, Height = 500 };
        var presenter = new QuickPanelItemMenu(window);
        var previousContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(window.Dispatcher));
        try
        {
            window.Show();
            Complete(presenter.ShowAsync(list, group, item, -1), window.Dispatcher);
            var first = list.ContextMenu;
            var count = first.Items.Count;
            presenter.Close();
            Complete(presenter.ShowAsync(list, group, item, -1), window.Dispatcher);
            Assert.AreSame(first, list.ContextMenu);
            Assert.AreEqual(count, first.Items.Count);
        }
        finally { presenter.Close(); window.Close(); SynchronizationContext.SetSynchronizationContext(previousContext); }
    }

    [StaTestMethod]
    public void ANewClickCancelsTheMenuStillBeingPrepared()
    {
        var vm = new QuickPanelViewModel(() => new QuickPanelSettings(), (_, _) => Task.FromResult(new List<SearchResult>()), () => { });
        var item = new AppSearchResult { Name = "file.txt", FullPath = @"C:\Data\file.txt" };
        var group = new QuickPanelGroupViewModel("source", "Source", "", new() { (item, null) });
        var list = new ListBox { DataContext = group, ItemsSource = group.Items, SelectedItem = item };
        var window = new QuickPanelWindow(vm) { Content = list, Width = 700, Height = 500 };
        var menu = new QuickPanelItemMenu(window);
        try
        {
            window.Show();
            var pending = menu.ShowAsync(list, group, item, -1);
            menu.ShowAsync(list, group, null, -1).GetAwaiter().GetResult();
            Complete(pending, window.Dispatcher);
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            Assert.IsFalse(menu.IsOpen);
            Assert.IsFalse(list.ContextMenu.IsOpen);
            Assert.IsTrue(window.IsVisible);
        }
        finally { menu.Close(); window.Close(); }
    }

    [StaTestMethod]
    public void ClosingThePanelCancelsAPendingMenu()
    {
        var vm = new QuickPanelViewModel(() => new QuickPanelSettings(), (_, _) => Task.FromResult(new List<SearchResult>()), () => { });
        var item = new AppSearchResult { Name = "file.txt", FullPath = @"C:\Data\file.txt" };
        var group = new QuickPanelGroupViewModel("source", "Source", "", new() { (item, null) });
        var list = new ListBox { DataContext = group, ItemsSource = group.Items };
        var window = new QuickPanelWindow(vm) { Content = list, Width = 700, Height = 500 };
        var menu = new QuickPanelItemMenu(window);
        try
        {
            window.Show();
            var pending = menu.ShowAsync(list, group, item, -1);
            window.Hide();
            Complete(pending, window.Dispatcher);
            Assert.IsFalse(menu.IsOpen);
            Assert.IsFalse(list.ContextMenu.IsOpen);
        }
        finally { menu.Close(); window.Close(); }
    }

    [StaTestMethod]
    public void EscapeClosesTheMenuWithoutClearingTheFilterOrClosingThePanel()
    {
        var vm = new QuickPanelViewModel(() => new QuickPanelSettings(), (_, _) => Task.FromResult(new List<SearchResult>()), () => { });
        var window = new QuickPanelWindow(vm) { Width = 700, Height = 500 };
        var button = Assert.IsInstanceOfType<QuickPanelSizeButton>(window.FindName("SizeButton"));
        var menu = new ContextMenu { PlacementTarget = button };
        menu.Items.Add(new MenuItem { Header = "Small" });
        button.ContextMenu = menu;
        try
        {
            window.Show();
            vm.SearchQuery = "query";
            menu.IsOpen = true;
            var key = new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice,
                PresentationSource.FromVisual(window), 0, System.Windows.Input.Key.Escape)
            { RoutedEvent = System.Windows.Input.Keyboard.PreviewKeyDownEvent };
            window.RaiseEvent(key);
            Assert.IsTrue(key.Handled);
            Assert.IsFalse(menu.IsOpen);
            Assert.IsTrue(window.IsVisible);
            Assert.AreEqual("query", vm.SearchQuery);
        }
        finally { menu.IsOpen = false; window.Close(); }
    }

    private static void Complete(Task task, Dispatcher dispatcher)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        timer.Tick += (_, _) => frame.Continue = false;
        task.ContinueWith(_ => dispatcher.BeginInvoke(new Action(() => frame.Continue = false)), TaskScheduler.Default);
        timer.Start();
        try { Dispatcher.PushFrame(frame); }
        finally { timer.Stop(); }
        Assert.IsTrue(task.IsCompleted, "The menu operation must complete while the dispatcher is running.");
        task.GetAwaiter().GetResult();
    }

    [StaTestMethod]
    public void BuiltInActionsAppearDirectlyBeforeNameToolsAndCloseBeforeExecuting()
    {
        var vm = new QuickPanelViewModel(() => new QuickPanelSettings(), (_, _) => Task.FromResult(new List<SearchResult>()), () => { });
        var window = new QuickPanelWindow(vm) { Width = 700, Height = 500 };
        var menu = new ContextMenu { PlacementTarget = window };
        var nameTool = new MenuItem { Header = "Rename label" };
        menu.Items.Add(nameTool);
        var executions = 0;
        var wasClosed = false;
        var action = new ActionMenuItem
        {
            Text = "Open", ShortcutHint = "Ctrl+O",
            OnExecute = () => { executions++; wasClosed = !menu.IsOpen; }
        };
        try
        {
            window.Show();
            QuickPanelItemMenu.PrependActions(menu, new[] { action, new ActionMenuItem { Text = "Unavailable", IsDisabled = true } },
                new[] { new AppSearchResult { Name = "file.txt", FullPath = @"C:\Data\file.txt" } }, window);
            menu.IsOpen = true;
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            var open = Assert.IsInstanceOfType<MenuItem>(menu.Items[0]);
            Assert.AreEqual("Open", open.Header);
            Assert.IsTrue(open.IsVisible);
            Assert.HasCount(0, open.Items);
            Assert.IsFalse(Assert.IsInstanceOfType<MenuItem>(menu.Items[1]).IsEnabled);
            Assert.IsInstanceOfType<Separator>(menu.Items[2]);
            Assert.AreSame(nameTool, menu.Items[3]);
            open.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, open));
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            Assert.AreEqual(1, executions);
            Assert.IsTrue(wasClosed);
        }
        finally { menu.IsOpen = false; window.Close(); }
    }
}
