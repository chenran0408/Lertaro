using System.Diagnostics.Metrics;
using Lertaro.App.ViewModels.Search;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Lertaro.App.Tests.ViewModels.Search;

// What a search gets to show while a provider is being reloaded. The rebuild itself is unavoidable --
// a change under a watched folder has to be picked up -- but it must never be something the user can
// see, and taking the entries away for its duration is exactly that: every application drops out of
// the results until each shortcut has been resolved through COM and each icon re-extracted.
//
// It matters more than it looks, because the rebuild is not always warranted: a change the index cannot
// pin to a directory is reported against the whole drive, so a busy C: still produces the occasional
// refresh for something that never touched these folders.
[TestClass]
[DoNotParallelize]
public sealed class SearchableItemCacheStaleTests
{
    private static string NewProviderId() => "TestProvider_" + Guid.NewGuid().ToString("N");

    [TestMethod]
    public void InvalidatingKeepsServingWhatItAlreadyHad()
    {
        var id = NewProviderId();
        SearchableItemCache.Seed(id, new List<SearchableItemCache.CacheEntry>
        {
            new(new PluginSdk.Abstractions.Plugins.SearchableItem { Title = "Notepad" }, new List<string>(), null),
        });

        SearchableItemCache.Invalidate(id);

        Assert.IsTrue(SearchableItemCache.TryGetEntries(id, out var entries), "the provider dropped out of results while it reloaded");
        Assert.HasCount(1, entries);
    }

    // Marked stale, so the next EnsureLoaded rebuilds rather than deciding it is already cached.
    [TestMethod]
    public void InvalidatingStillMarksItForReload()
    {
        var id = NewProviderId();
        SearchableItemCache.Seed(id, new List<SearchableItemCache.CacheEntry>());

        SearchableItemCache.Invalidate(id);

        Assert.IsTrue(SearchableItemCache.IsStale(id));
    }

    // Nothing cached yet means nothing to serve; there is no state worth carrying and the first load
    // has not run.
    [TestMethod]
    public void InvalidatingSomethingNeverLoadedIsHarmless()
    {
        var id = NewProviderId();

        SearchableItemCache.Invalidate(id);

        Assert.IsFalse(SearchableItemCache.TryGetEntries(id, out _));
        Assert.IsFalse(SearchableItemCache.IsStale(id));
    }

    [TestMethod]
    public void ProviderCacheGaugesReportTaggedCountsAndUniqueBitmapBytesAndTrackChanges()
    {
        var firstId = NewProviderId();
        var secondId = NewProviderId();
        var shared = Bitmap(2, 3);
        SearchableItemCache.Seed(firstId,
        [
            Entry("one", shared),
            Entry("two", shared),
        ]);
        SearchableItemCache.Seed(secondId, [Entry("three", Bitmap(1, 1))]);

        var measurements = new List<(string Name, long Value, string? ProviderId)>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == "Lertaro.App.Caches" &&
                instrument.Name is "lertaro.cache.searchable_items.provider_entries" or "lertaro.cache.searchable_items.provider_bitmap_bytes")
                meterListener.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
        {
            var providerId = tags.ToArray().FirstOrDefault(tag => tag.Key == "providerId").Value as string;
            measurements.Add((instrument.Name, value, providerId));
        });
        listener.Start();

        void Record() => listener.RecordObservableInstruments();
        long Value(string metric, string providerId) => measurements.Single(item => item.Name == metric && item.ProviderId == providerId).Value;

        Record();
        Assert.AreEqual(2L, Value("lertaro.cache.searchable_items.provider_entries", firstId));
        Assert.AreEqual(1L, Value("lertaro.cache.searchable_items.provider_entries", secondId));
        Assert.AreEqual(24L, Value("lertaro.cache.searchable_items.provider_bitmap_bytes", firstId));
        Assert.AreEqual(4L, Value("lertaro.cache.searchable_items.provider_bitmap_bytes", secondId));

        measurements.Clear();
        SearchableItemCache.Seed(firstId, [Entry("replacement", null)]);
        Record();
        Assert.AreEqual(1L, Value("lertaro.cache.searchable_items.provider_entries", firstId));
        Assert.AreEqual(0L, Value("lertaro.cache.searchable_items.provider_bitmap_bytes", firstId));

        measurements.Clear();
        SearchableItemCache.ClearForTests();
        Record();
        Assert.IsFalse(measurements.Any(item => item.ProviderId == firstId || item.ProviderId == secondId));
    }

    private static SearchableItemCache.CacheEntry Entry(string title, ImageSource? icon) =>
        new(new PluginSdk.Abstractions.Plugins.SearchableItem { Title = title }, [], icon);

    private static BitmapSource Bitmap(int width, int height)
    {
        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null,
            new byte[width * height * 4], width * 4);
        bitmap.Freeze();
        return bitmap;
    }
}
