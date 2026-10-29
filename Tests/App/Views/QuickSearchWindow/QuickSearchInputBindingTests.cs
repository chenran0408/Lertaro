using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Data;
using System.Windows.Threading;
using QuickSearchPopup = global::Lertaro.App.QuickSearchWindow;

namespace Lertaro.App.Tests.Views.QuickSearchWindow;

[TestClass, DoNotParallelize]
public sealed class QuickSearchInputBindingTests
{
    [STATestMethod]
    public void QuickSearchWindow_TypedQueryReachesSearchAndResultsLayout()
    {
        EnsureApplicationResources();
        var window = new QuickSearchPopup();

        try
        {
            // The first load intentionally hides the startup seed. Show it once, pump Loaded, then show
            // it again as the normal user summon. This does not run App.OnStartup or its process mutex.
            window.Opacity = 0;
            window.Show();
            Assert.IsTrue(PumpUntil(window.Dispatcher, () => !window.IsVisible, TimeSpan.FromSeconds(5)),
                "The first load should hide the startup seed after the Loaded handler runs.");

            window.Show();
            window.UpdateLayout();
            DrainDispatcher(window.Dispatcher);
            Assert.IsTrue(window.IsVisible, "The second summon should leave the quick window visible.");

            window.SearchTextBox.Text = "pcl";
            Assert.IsTrue(PumpUntil(window.Dispatcher, () => window.ViewModel.SearchQuery == "pcl", TimeSpan.FromSeconds(2)),
                "Typing should immediately update QuickSearchViewModel.SearchQuery.");

            var searchBox = (SearchBoxControl)typeof(QuickSearchPopup)
                .GetField("SearchBox", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(window)!;
            var innerBinding = BindingOperations.GetBindingExpression(window.SearchTextBox, System.Windows.Controls.TextBox.TextProperty);
            var outerBinding = BindingOperations.GetBindingExpression(searchBox, SearchBoxControl.SearchTextProperty);
            Assert.AreEqual(BindingStatus.Active, innerBinding?.Status);
            Assert.AreSame(searchBox, innerBinding?.ResolvedSource);
            Assert.AreEqual(nameof(SearchBoxControl.SearchText), innerBinding?.ResolvedSourcePropertyName);
            Assert.AreEqual(BindingStatus.Active, outerBinding?.Status);
            Assert.AreSame(window.ViewModel, outerBinding?.ResolvedSource);
            Assert.AreEqual(nameof(window.ViewModel.SearchQuery), outerBinding?.ResolvedSourcePropertyName);

            Assert.IsTrue(PumpUntil(window.Dispatcher, () => window.ViewModel.Results.Count > 0, TimeSpan.FromSeconds(15)),
                "The active Service search should populate QuickSearchViewModel.Results.");

            var firstResult = window.ViewModel.Results[0];
            Assert.AreEqual(Visibility.Visible, window.ViewModel.ResultsPanelVisibility);
            Assert.AreEqual(Visibility.Visible, window.ResultsPanel.Visibility);
            Assert.IsTrue(PumpUntil(window.Dispatcher,
                () => window.LstResults.Items.Count == window.ViewModel.Results.Count && window.LstResults.Height > 0,
                TimeSpan.FromSeconds(5)),
                "Results.CollectionChanged should update ListBox items and its measured row height.");
            Assert.AreSame(firstResult, window.LstResults.Items[0]);
            Assert.AreEqual(firstResult.ScaledItemHeight, window.LstResults.Height);
            Assert.IsTrue(window.Height > 0);
        }
        finally
        {
            window.Close();
        }
    }

    private static void EnsureApplicationResources()
    {
        var application = Application.Current ?? new Application();
        application.ShutdownMode = ShutdownMode.OnExplicitShutdown;
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
        if (predicate())
            return true;

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
