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
