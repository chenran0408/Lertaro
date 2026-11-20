using Lertaro.PluginSdk.Helpers;
using System.Runtime.InteropServices;
using System.Text;
using Lertaro.Core.SearchIndex.Fzf;

using Lertaro.Core.IndexV2.Delta;

using Lertaro.Core.IndexV2.Persistence;
using Lertaro.Core.SearchIndex;

namespace Lertaro.Core.IndexV2.Search;

// Order-free "a term may be satisfied by an ancestor folder instead of the file name" pass, run by
// NameSearch to top up a page the names alone did not fill. That trigger is what keeps this cheap: a
// query that already answers in full never reaches it. Everything found here is appended after the
// name hits rather than merged with them, so the ranking can reuse the matched term's own sort key
// untouched -- no sort-key surgery, and no risk of pushing a genuine name match down.
//
// Distinct from PathSearch, which models a POSITIONAL "dir\subdir\file" query: there the query's
// segments must appear in ancestors in that same order. Here the terms carry no position at all, so
// each ancestor is offered every still-unsatisfied term (fzf terms are already order-free against a
// name; this extends the same property across the path).
//
// Deliberate v1 restriction: at least one term must match the FILE NAME. Without it the candidate set
// becomes "every row under any folder matching any term", which one common folder name turns
// into most of a drive -- and that set cannot be enumerated from phase A's per-term unique hits, which
// is exactly what keeps the row walk here bounded to the same order of work as an ordinary search.
internal static class PathTermFallback
{
    /// <summary>What one unique name contributed: which terms its own name satisfied, and the best
    /// match among them for a matching row to rank by.</summary>
    internal struct NameHit
    {
        public int Mask;
        public int Score;
        public ulong SortKey;
    }

    // The two tables below hold one entry per unique name and one per folder walked, which on a broad
    // query is tens of thousands each. Built and thrown away per search, they were most of what this
    // pass allocated; rented and cleared, the buckets survive and only the first search of a size pays
    // for them. Pooled the way SearchMatcher pools its workers and hit lists, because searches on
    // different drives run at the same time and a single shared instance would be a race.
    private sealed class Scratch : IDisposable
    {
        public readonly Dictionary<int, NameHit> NameHits = new();
        public readonly Dictionary<int, int> AncestorMemo = new();
        public readonly List<int> AncestorChain = new(64);

        public void Dispose() => ScratchPool.Add(this);

        public void Reset()
        {
            // Trimmed rather than merely cleared: both dictionaries scale with the search (one entry per
            // matched name, one per directory walked), and Clear keeps the buckets, so a whole-drive
            // query would leave this pooled scratch sized for it forever. See SearchScratchPolicy.
            SearchScratchPolicy.ClearAndTrim(NameHits);
            SearchScratchPolicy.ClearAndTrim(AncestorMemo);
            AncestorChain.Clear();
        }
    }

    private static readonly System.Collections.Concurrent.ConcurrentBag<Scratch> ScratchPool = new();

    private static Scratch RentScratch()
    {
        var scratch = ScratchPool.TryTake(out var pooled) ? pooled : new Scratch();
        scratch.Reset();
        return scratch;
    }

    // The satisfied-term set is carried as a bitmask, so a query with more terms than bits simply
    // opts out rather than silently matching on a truncated set.
    private const int MaxTerms = 16;

    public static void SearchStreaming(Snapshot snapshot, DeltaOverlay delta, FzfPattern pattern, int limit,
        Action<SearchResult> onResult, CancellationToken token, string? directoryFilterLower, SearchCategoryFilter category = SearchCategoryFilter.All)
    {
        if (pattern.OrGroups != null)
        {
            SearchAndFirstBranches(snapshot, delta, pattern, limit, onResult, token, directoryFilterLower, category);
            return;
        }

        var termCount = pattern.TermSets.Length;
        // One term has nowhere to split: the "at least one term matches the name" rule would make this
        // identical to the name search that just came up empty.
        if (termCount < 2 || termCount > MaxTerms)
            return;

        var directoryContext = NameSearch.ResolveDirectoryContext(snapshot, delta, directoryFilterLower);
        if (directoryContext.Excluded)
            return;

        foreach (var rank in CollectPathRanks(snapshot, delta, pattern, directoryContext, limit, token, category))
        {
            token.ThrowIfCancellationRequested();
            onResult(ResultBuilder.ToResult(snapshot, delta, rank));
        }
    }

    private static List<FzfRank> CollectPathRanks(Snapshot snapshot, DeltaOverlay delta, FzfPattern pattern,
        NameSearch.DirectoryContext directoryContext, int limit, CancellationToken token, SearchCategoryFilter category = SearchCategoryFilter.All)
    {
        var termCount = pattern.TermSets.Length;
        var termPatterns = new FzfPattern[termCount];
        var termBytePatterns = new FzfBytePattern[termCount];
        for (var i = 0; i < termCount; i++)
        {
            termPatterns[i] = FzfPattern.ForTermSet(pattern, i);
            termBytePatterns[i] = FzfBytePattern.From(termPatterns[i]);
        }

        // Phase A once per term instead of once per query: which unique names satisfy term i, and with
        // what sort key (kept so an emitted row can rank by the term that actually hit its name).
        //
        // One entry per unique rather than a mask table and a rank table side by side. Both were keyed
        // the same way and written in the same breath, so every hit paid four hash lookups where it
        // needs one, and the row walk below then looked the rank up a second time. A broad term set can
        // reach tens of thousands of uniques per query.
        using var scratch = RentScratch();
        var nameHits = scratch.NameHits;
        var fullMask = (1 << termCount) - 1;
        for (var i = 0; i < termCount; i++)
        {
            token.ThrowIfCancellationRequested();
            var bit = 1 << i;
            if (PathTermMatchCache.TryGet(snapshot, termPatterns[i], out var saved, out var remember))
            {
                foreach (var hit in saved)
                    AddNameHit(nameHits, hit.Uid, hit.Score, hit.SortKey, bit);
                continue;
            }

            var hits = SearchMatcher.RentHitList();
            try
            {
                SearchMatcher.MatchUniques(snapshot, termPatterns[i], hits, token);
                foreach (var hit in hits)
                    AddNameHit(nameHits, hit.Uid, hit.Match.Score, hit.SortKey, bit);
                remember?.Invoke(hits);
            }
            finally
            {
                SearchMatcher.ReturnHitList(hits);
            }
        }

        if (nameHits.Count == 0) return [];

        // The source root's segments are the same for every row in the query, but the ancestor walk
        // below consulted them on each of its (many) memo misses -- re-splitting the root string and
        // re-matching every term against it tens of thousands of times per search. Computed once.
        var rootWorker = SearchMatcher.RentWorker();
        var rootMask = PathTermFallbackAncestorHelper.MaskFromSegments(snapshot.SourceRoot.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries), termPatterns, rootWorker, 0);
        SearchMatcher.ReturnWorker(rootWorker);

        var ancestorMemo = scratch.AncestorMemo;
        var ancestorChain = scratch.AncestorChain;
        var membership = directoryContext.FilterLower != null ? new Dictionary<int, bool>() : null;
        var worker = SearchMatcher.RentWorker();
        // Bounded by the index rather than by the caller's limit, which is no longer capped: the
        // multiply overflows int for a large enough limit, and FzfTopN reserves twice its capacity.
        var keep = (int)Math.Min((long)Math.Max(limit, 8) * 8, snapshot.Count + delta.Added.Count);
        // Every occurrence of a unique name has the same upper bound on its rank. Once a small
        // page is full, skip inferior names before materialising their rows or walking their parents.
        var bounded = limit < nameHits.Count / 4;
        var topN = bounded ? null : new FzfTopN(keep);
        var page = bounded ? new PriorityQueue<FzfRank, FzfRank>(WorstRankFirst) : null;
        try
        {
            foreach (var (uid, hit) in nameHits)
            {
                token.ThrowIfCancellationRequested();
                // A name satisfying every term on its own is exactly what the name search matches, so
                // it has already been reported (or was cut by its limit, which this pass must not
                // undo by re-reporting it further down). Skipping is what keeps the two passes from
                // double-reporting now that this one runs alongside a non-empty result set.
                if (page != null && page.Count == limit && hit.SortKey > page.Peek().SortKey)
                    continue;
                var nameMask = hit.Mask;
                if (nameMask == fullMask)
                    continue;

                // An `!term` is satisfied by any segment that lacks it, so the mask above can be filled by
                // an ANCESTOR while this candidate's own name carries the very text the user excluded. The
                // veto belongs on the name -- see FzfPatternExclusionExtensions.ExclusionsPass.
                if (pattern.HasExclusions && !NamePassesExclusions(snapshot, uid, pattern, worker))
                    continue;

                foreach (var row in snapshot.RowsForUid(uid))
                {
                    if (snapshot.IsDeleted(row) || delta.IsSuperseded(row) || !NameSearch.CategoryAdmits(category, snapshot, delta, row))
                        continue;
                    if (membership != null && !NameSearch.RowMatchesFilter(snapshot, delta, row, directoryContext, membership))
                        continue;

                    var rank = new FzfRank(row, hit.Score, hit.SortKey);
                    if (page != null && page.Count == limit && FzfResultRank.Compare(rank, page.Peek()) >= 0)
                        continue;
                    var parent = snapshot.ParentIndexes[row];
                    if (parent == row || parent < 0)
                        continue;
                    if ((nameMask | PathTermFallbackAncestorHelper.AncestorMask(snapshot, delta, parent, termPatterns, termBytePatterns, worker, ancestorMemo, fullMask, rootMask, ancestorChain, nameHits)) != fullMask)
                        continue;

                    if (page == null)
                        topN!.Add(rank);
                    else if (page.Count < limit)
                        page.Enqueue(rank, rank);
                    else
                        page.DequeueEnqueue(rank, rank);
                }
            }
        }
        finally
        {
            SearchMatcher.ReturnWorker(worker);
        }

        if (page == null) return topN!.Finish(limit);
        var ranks = page.UnorderedItems.Select(pair => pair.Element).ToList();
        FzfRankRadixSorter.Sort(ranks);
        return ranks;
    }

    private static readonly IComparer<FzfRank> WorstRankFirst =
        Comparer<FzfRank>.Create((left, right) => FzfResultRank.Compare(right, left));

    private static void AddNameHit(Dictionary<int, NameHit> hits, int uid, int score, ulong sortKey, int bit)
    {
        ref var entry = ref CollectionsMarshal.GetValueRefOrAddDefault(hits, uid, out var existed);
        entry.Mask |= bit;
        if (!existed || score > entry.Score)
        {
            entry.Score = score;
            entry.SortKey = sortKey;
        }
    }

    private static void SearchAndFirstBranches(Snapshot snapshot, DeltaOverlay delta, FzfPattern pattern, int limit,
        Action<SearchResult> onResult, CancellationToken token, string? directoryFilterLower, SearchCategoryFilter category)
    {
        var emitted = 0;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in pattern.OrGroups!)
        {
            if (emitted >= limit)
                break;

            // The existing fallback algorithm operates on an AND list of term sets. Reusing it per DNF
            // branch preserves its bounded name/ancestor walk while the outer callback deduplicates rows
            // that satisfy more than one OR branch.
            var branch = new FzfPattern(pattern.TargetDrive, group.Sets);
            SearchStreaming(snapshot, delta, branch, limit, result =>
            {
                if (emitted >= limit || !seen.Add(result.Path))
                    return;
                emitted++;
                onResult(result);
            }, token, directoryFilterLower, category);
        }
    }

    // Decodes one unique name into the worker's scratch and applies the query's exclusions to it. Only
    // the verdict survives: the same scratch is reused by the ancestor walk this is called from.
    private static bool NamePassesExclusions(Snapshot snapshot, int uid, FzfPattern pattern, SearchMatcher.Worker worker)
    {
        var utf8 = snapshot.UniqueNameUtf8(uid);
        if (worker.Scratch.Length < utf8.Length)
            worker.Scratch = new char[Math.Max(utf8.Length, worker.Scratch.Length * 2)];
        var written = Encoding.UTF8.GetChars(utf8, worker.Scratch);
        return pattern.ExclusionsPass(worker.Scratch.AsSpan(0, written));
    }
}
