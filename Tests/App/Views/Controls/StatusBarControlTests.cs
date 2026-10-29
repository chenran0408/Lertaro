using System.Diagnostics;
using System.Windows;
using System.Windows.Data;
using System.Windows.Threading;
using Lertaro.App.Tests;
using QuickSearchPopup = global::Lertaro.App.QuickSearchWindow;

namespace Lertaro.App.Tests.Views.Controls;

[TestClass, DoNotParallelize]
public sealed class StatusBarControlTests
{
    [StaTestMethod]
    public void QuickSearchStatusBar_ShowsServiceTextAndFitsBelowResults()
    {
        EnsureApplicationResources();
        var window = new QuickSearchPopup();

        try
        {
            window.Opacity = 0;
            window.Show();
            Assert.IsTrue(PumpUntil(window.Dispatcher, () => !window.IsVisible, TimeSpan.FromSeconds(5)),
                "The quick window should hide its first startup load.");

            window.Show();
            window.UpdateLayout();
            DrainDispatcher(window.Dispatcher);
            Assert.IsTrue(window.IsVisible);

            for (var i = 0; i < 6; i++)
                window.ViewModel.Results.Add(new AppSearchResult
                {
                    Name = $"status-row-{i}.txt",
                    FullPath = $@"C:\status-row-{i}.txt"
                });
            window.ViewModel.ResultsPanelVisibility = Visibility.Visible;
            window.ViewModel.ResultsSeparatorVisibility = Visibility.Visible;
            window.ViewModel.Monitor.StatusBarVisibility = Visibility.Visible;
            window.ViewModel.Monitor.StatusText = "Loading disk index · 6 results";
            window.ApplyResultsLayoutImmediate();
            window.UpdateLayout();
            DrainDispatcher(window.Dispatcher);

            var status = (StatusBarControl)window.StatusBar;
            Assert.AreEqual(6, window.LstResults.Items.Count);
            Assert.IsTrue(window.LstResults.ActualHeight > 0, "The result rows should be laid out above the status bar.");
            Assert.AreEqual(Visibility.Visible, status.Visibility);
            Assert.IsTrue(status.StatusBarBorder.ActualHeight > 0, "The status text must contribute height to the auto-sized window.");
            Assert.AreEqual("Loading disk index · 6 results", status.StatusInfoTextBlock.Text);

            window.ViewModel.Monitor.StatusText = "Building index · 42% · 6 results";
            DrainDispatcher(window.Dispatcher);
            Assert.AreEqual("Building index · 42% · 6 results", status.StatusInfoTextBlock.Text);

            window.ViewModel.Monitor.StatusText = "Index ready · 120 files";
            DrainDispatcher(window.Dispatcher);
            Assert.AreEqual("Index ready · 120 files", status.StatusInfoTextBlock.Text);

            window.ViewModel.Monitor.StatusBarVisibility = Visibility.Collapsed;
            window.UpdateLayout();
            Assert.IsFalse(status.IsVisible);
            Assert.AreEqual(0, status.ActualHeight);

            window.ViewModel.Monitor.StatusBarVisibility = Visibility.Visible;
            window.UpdateLayout();
            Assert.IsTrue(status.IsVisible);
            Assert.IsTrue(status.StatusBarBorder.ActualHeight > 0);

            var statusBounds = status.TransformToAncestor(window).TransformBounds(new Rect(status.RenderSize));
            Assert.IsTrue(statusBounds.Bottom <= window.ActualHeight + 1,
                $"The status bar ends at {statusBounds.Bottom:F1}, outside the window height {window.ActualHeight:F1}.");

            var narrowStatus = new StatusBarControl
            {
                Width = 320,
                DataContext = new
                {
                    StatusBarVisibility = Visibility.Visible,
                    StatusText = "Building the local disk index · 99% · 1,234,567 files and 123,456 folders have been scanned so far"
                }
            };
            narrowStatus.SetBinding(UIElement.VisibilityProperty, new Binding("StatusBarVisibility"));
            var narrowWindow = new Window
            {
                Content = narrowStatus,
                Width = 320,
                Height = 60,
                WindowStyle = WindowStyle.None,
                ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false
            };
            try
            {
                narrowWindow.Show();
                narrowWindow.UpdateLayout();
                var textBounds = narrowStatus.StatusInfoTextBlock.TransformToAncestor(narrowWindow)
                    .TransformBounds(new Rect(narrowStatus.StatusInfoTextBlock.RenderSize));
                Assert.AreEqual(TextTrimming.CharacterEllipsis, narrowStatus.StatusInfoTextBlock.TextTrimming);
                Assert.IsTrue(textBounds.Right <= narrowWindow.ActualWidth + 1,
                    $"The status text ends at {textBounds.Right:F1}, outside the narrow window width {narrowWindow.ActualWidth:F1}.");
            }
            finally
            {
                narrowWindow.Close();
            }
        }
        finally
        {
            window.Close();
        }
    }

    private static void EnsureApplicationResources()
    {
        var application = Application.Current;
        if (application == null)
            application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        AddApplicationDictionary(application, "Resources/Styles.xaml");
        AddApplicationDictionary(application, "Resources/DataTemplates.xaml");
    }

    private static void AddApplicationDictionary(Application application, string resourcePath)
    {
        var componentUri = new Uri($"pack://application:,,,/Lertaro.App;component/{resourcePath}", UriKind.Absolute);
        if (application.Resources.MergedDictionaries.Any(dictionary => dictionary.Source == componentUri))
            return;
        application.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = componentUri });
    }

    private static bool PumpUntil(Dispatcher dispatcher, Func<bool> predicate, TimeSpan timeout)
    {
        if (predicate()) return true;

        var frame = new DispatcherFrame();
        var stopwatch = Stopwatch.StartNew();
        var timer = new DispatcherTimer(TimeSpan.FromMilliseconds(10), DispatcherPriority.Background, (_, _) =>
        {
            if (predicate() || stopwatch.Elapsed >= timeout)
                frame.Continue = false;
        }, dispatcher);
        timer.Start();
        Dispatcher.PushFrame(frame);
        timer.Stop();
        return predicate();
    }

    private static void DrainDispatcher(Dispatcher dispatcher)
    {
        var frame = new DispatcherFrame();
        dispatcher.BeginInvoke(() => frame.Continue = false, DispatcherPriority.ApplicationIdle);
        Dispatcher.PushFrame(frame);
    }
}
