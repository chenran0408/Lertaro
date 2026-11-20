using System.Runtime.CompilerServices;
using Lertaro.Core.IndexV2.Persistence;
using Lertaro.Core.SearchIndex;
using Lertaro.Core.SearchIndex.Fzf;

namespace Lertaro.Core.IndexV2.Search;

// A user refining the last word of a filename repeatedly asks the same immutable name table
// about the preceding words. Cache only that scan. Live rows, paths and their ranks are rebuilt
// on every request. Replacing the snapshot also replaces its cache.
internal sealed class PathTermMatchCache
{
    private static readonly ConditionalWeakTable<Snapshot, PathTermMatchCache> BySnapshot = new();
    private readonly record struct Key(FzfTerm Term, string DisabledProviders, int ProviderVersion);
    internal readonly record struct TermHit(int Uid, int Score, ulong SortKey);
    // Sixteen bytes per retained hit. A byte budget lets common words share the same 8 MiB
    // allowance as rare words without excluding an entire drive on a moderately broad query.
    private const int MaximumCachedHits = 512 * 1024;
    private const int MaximumEntries = 8;
    private int _retainedHits;
    private readonly Dictionary<Key, TermHit[]> _entries = new();
    private readonly Queue<Key> _insertionOrder = new();

    internal static bool TryGet(Snapshot snapshot, FzfPattern pattern, out TermHit[] hits, out Action<List<UniqueMatch>>? remember)
    {
        hits = [];
        remember = null;
        var terms = pattern.TermSets[0].Terms;
        // Only cache ordinary, short ASCII words. Mixed-script terms can consult a live plugin.
        if (terms.Length != 1 || terms[0].Inverse || terms[0].Text.Length is < 2 or > 64 ||
            !terms[0].Text.All(char.IsAsciiLetterOrDigit) || MixedQueryMatcher.TrySegmentPattern(pattern) != null)
            return false;

        var disabled = SearchContext.DisabledAliasIds;
        var key = new Key(terms[0], disabled is { Count: > 0 }
            ? Convert.ToHexString(disabled.Order().ToArray()) : string.Empty, AliasProviderRegistry.RegistrationVersion);
        var cache = BySnapshot.GetValue(snapshot, _ => new PathTermMatchCache());
        lock (cache._entries)
        {
            if (cache._entries.TryGetValue(key, out var stored))
            {
                hits = stored;
                return true;
            }
        }
        remember = matches => cache.Store(key, matches);
        return false;
    }

    private void Store(Key key, List<UniqueMatch> matches)
    {
        if (matches.Count > MaximumCachedHits / 2) return;
        lock (_entries)
        {
            if (_entries.ContainsKey(key)) return;
            while (_entries.Count >= MaximumEntries || _retainedHits + matches.Count > MaximumCachedHits)
            {
                _entries.Remove(_insertionOrder.Dequeue(), out var evicted);
                _retainedHits -= evicted!.Length;
            }

            var hits = new TermHit[matches.Count];
            for (var i = 0; i < hits.Length; i++)
            {
                var match = matches[i];
                hits[i] = new TermHit(match.Uid, match.Match.Score, match.SortKey);
            }
            _entries.Add(key, hits);
            _insertionOrder.Enqueue(key);
            _retainedHits += hits.Length;
        }
    }
}
