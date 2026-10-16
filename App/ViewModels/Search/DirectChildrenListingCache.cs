namespace Lertaro.App.ViewModels.Search;

// Owns the one-level folder listings for one inline-search window, so the walk that scales with the
// folder's size happens once per folder instead of once per keystroke (and per streaming paint).
//
// A big folder was the reported case: with no cache, EVERY keystroke re-walked it, so the results for the
// first character only appeared after the whole folder had been enumerated. The walk is now started as
// soon as the window knows its folder -- which happens when Explorer is navigated to or a file dialog is
// activated, i.e. while the user has not typed yet -- and the first character then matches against a
// listing that is already there.
//
// Keyed by folder, not a single slot: navigating B and back to A is ordinary use, and a one-slot cache
// would re-walk A exactly when the user returns to it.
//
// Owned by InlineSearchManager for inline windows: a quick reopen may reuse a listing. A new session
// validates directory changes off the UI thread, and only reuses unchanged snapshots for five seconds.
internal sealed class DirectChildrenListingCache
{
    // Deliberately small. Each listing holds up to DirectChildrenLocator.MaxExaminedEntries names with
    // full paths, so a large folder is megabytes; this bounds the window's retention while still covering
    // the handful of folders a user steps back and forth through. Least-recently-added is evicted.
    private const int MaxFolders = 4;

    private readonly object _gate = new();
    private readonly Dictionary<string, Task<List<DirectChildrenLocator.Entry>>> _listings = new(StringComparer.OrdinalIgnoreCase);
    private readonly Queue<string> _insertionOrder = new();
    private readonly Dictionary<string, DateTime> _lastWrites = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, long> _readAt = new(StringComparer.OrdinalIgnoreCase);
    private readonly Func<long> _clock;
    internal DirectChildrenListingCache(Func<long>? clock = null) => _clock = clock ?? (() => Environment.TickCount64);

    /// <summary>
    /// The listing for <paramref name="directory"/>, walked at most once and shared by every caller.
    /// </summary>
    /// <remarks>
    /// Sharing the in-flight walk matters on the very first character: the search pump can ask for the
    /// snapshot while the walk it triggered is still running, and both must see the same one rather than
    /// starting a second. The task is deliberately not tied to a query's token -- a superseded keystroke
    /// must not abandon a walk the next one is waiting on; the bounded entry cap and the window's own
    /// lifetime bound it instead.
    /// </remarks>
    public Task<List<DirectChildrenLocator.Entry>> GetAsync(string directory)
    {
        lock (_gate)
        {
            if (_listings.TryGetValue(directory, out var existing))
                return existing;

            Task<List<DirectChildrenLocator.Entry>> started = null!;
            started = Task.Run(() =>
            {
                var stamp = ReadStamp(directory);
                var entries = DirectChildrenLocator.Enumerate(directory, CancellationToken.None);
                lock (_gate)
                {
                    if (_listings.TryGetValue(directory, out var current) && ReferenceEquals(current, started))
                    { _lastWrites[directory] = stamp; _readAt[directory] = _clock(); }
                }
                return entries;
            });
            _listings[directory] = started;
            _insertionOrder.Enqueue(directory);

            while (_insertionOrder.Count > MaxFolders)
            {
                var evicted = _insertionOrder.Dequeue();
                _listings.Remove(evicted); _lastWrites.Remove(evicted); _readAt.Remove(evicted);
            }

            return started;
        }
    }

    /// <summary>Starts the walk for a folder without waiting on it, so a later search finds it warm.</summary>
    public void Prewarm(string? directory)
    {
        if (!string.IsNullOrWhiteSpace(directory))
            _ = GetAsync(directory);
    }

    internal async Task<bool> RefreshForSessionAsync(string directory)
    {
        var original = GetAsync(directory);
        await original.ConfigureAwait(false);
        var stamp = await Task.Run(() => ReadStamp(directory)).ConfigureAwait(false);
        lock (_gate)
        {
            if (!_listings.TryGetValue(directory, out var current) || !ReferenceEquals(current, original)) return false;
            if (_lastWrites.TryGetValue(directory, out var previous) && previous == stamp &&
                _readAt.TryGetValue(directory, out var readAt) && _clock() - readAt < 5000) return false;
            _listings.Remove(directory); _lastWrites.Remove(directory); _readAt.Remove(directory);
            var retained = _insertionOrder.Where(path => !string.Equals(path, directory, StringComparison.OrdinalIgnoreCase)).ToArray();
            _insertionOrder.Clear();
            foreach (var path in retained) _insertionOrder.Enqueue(path);
            _ = GetAsync(directory);
            return true;
        }
    }

    private static DateTime ReadStamp(string directory)
    {
        try { return System.IO.Directory.GetLastWriteTimeUtc(directory); }
        catch { return DateTime.MinValue; }
    }
}
