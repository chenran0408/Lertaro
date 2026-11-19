using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Threading;
using Lertaro.App.Converters;
using Lertaro.App.Views.InlineSearchWindow.Helpers;

namespace Lertaro.App.Tests.Views.InlineSearchWindow.Helpers;

[TestClass]
public sealed class InlinePathDisplayBehaviorTests
{
    [STATestMethod]
    public void RecycledOpenedFolderRowShowsTheNewFileName()
    {
        var style = new Style(typeof(TextBlock));
        style.Setters.Add(new Setter(TextHighlighter.TextProperty, new Binding(nameof(AppSearchResult.Name))));
        style.Setters.Add(new Setter(TextBlock.TextTrimmingProperty, TextTrimming.WordEllipsis));
        var openedFolder = new DataTrigger
        {
            Binding = new Binding(nameof(AppSearchResult.ResultKind)),
            Value = "OpenedFolder"
        };
        openedFolder.Setters.Add(new Setter(InlinePathDisplayBehavior.IsEnabledProperty, true));
        style.Triggers.Add(openedFolder);

        var text = new TextBlock
        {
            Style = style,
            DataContext = new AppSearchResult { Name = @"D:\downloads", ResultKind = "OpenedFolder" }
        };
        var host = new Grid();
        host.Children.Add(text);
        using var source = new HwndSource(new HwndSourceParameters("InlineResultTest") { Width = 500, Height = 40 });
        source.RootVisual = host;
        host.Measure(new Size(500, 40));
        host.Arrange(new Rect(0, 0, 500, 40));
        text.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
        text.Dispatcher.Invoke(static () => { }, DispatcherPriority.ApplicationIdle);
        Assert.AreEqual(@"D:\downloads", text.Text);
        Assert.AreEqual(TextTrimming.None, text.TextTrimming);

        text.DataContext = new AppSearchResult { Name = "report.txt", ResultKind = "File" };
        text.Dispatcher.Invoke(static () => { }, DispatcherPriority.ApplicationIdle);

        Assert.AreEqual("report.txt", text.Text);
        Assert.AreEqual(TextTrimming.WordEllipsis, text.TextTrimming);
    }
}
