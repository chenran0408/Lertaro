using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Data;
using System.Windows.Threading;
using Lertaro.App.ViewModels.Search.Mapping;
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
            window.Show();
            window.UpdateLayout();
            DrainDispatcher(window.Dispatcher);
            Assert.IsTrue(window.IsVisible, "The first summon should leave the quick window visible.");

            // The results layout is driven by fed rows rather than a live Service search: the file
            // pipeline needs the LertaroService process and its index, which a dev machine has but
            // CI never does -- asserting on it here kept this test green locally and red on every
            // CI run. The layout contract under test (rows -> ListBox items + measured height) is
            // identical either way.
            window.ViewModel.ResultsPanelVisibility = Visibility.Visible;
            var firstResult = SearchResultMapper.CreateWebSearchResult("pcl");
            window.ViewModel.Results.Add(firstResult);
            window.ViewModel.Results.Add(SearchResultMapper.CreateWebSearchResult("pcl file"));
            Assert.IsTrue(PumpUntil(window.Dispatcher,
                () => window.LstResults.Items.Count == window.ViewModel.Results.Count && window.LstResults.Height > 0,
                TimeSpan.FromSeconds(5)),
                "Results.CollectionChanged should update ListBox items and its measured row height.");
            Assert.AreEqual(Visibility.Visible, window.ViewModel.ResultsPanelVisibility);
            Assert.AreEqual(Visibility.Visible, window.ResultsPanel.Visibility);
            Assert.AreSame(firstResult, window.LstResults.Items[0]);
            Assert.AreEqual(window.ViewModel.Results.Take(global::Lertaro.App.Services.UiMetrics.QuickSearchMaxVisibleResultRows)
                .Sum(result => result.ScaledItemHeight), window.LstResults.Height);

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
