using System.Text.Json;
using Lertaro.PluginSdk.Services;

namespace Lertaro.Plugins.WebSearch;

internal static class WebSearchSuggestionFetcher
{
    private static readonly TimeSpan SuggestionDebounce = TimeSpan.FromMilliseconds(200);

    private static readonly HttpClient SuggestionHttpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(4) };
    internal sealed record Request(string Keyword, string Endpoint, string Term, string Culture)
    {
        internal bool Matches(string query) => TriggerWord.TryMatchInvoked(query, Keyword, out var term)
            && string.Equals(term, Term, StringComparison.OrdinalIgnoreCase);
    }

    private const int CacheCapacity = 128;
    private static readonly HashSet<Request> Pending = [];
    private static readonly Dictionary<Request, List<string>> Cache = [];
    private static readonly Queue<Request> CacheOrder = new();
    private static Request? _latest;

    static WebSearchSuggestionFetcher()
    {
        try
        {
            // Some suggestion endpoints (e.g. Wikipedia's) reject requests with no User-Agent header.
            SuggestionHttpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/125.0.0.0 Safari/537.36 Edg/125.0.0.0");
        }
        catch { }
    }

    public static IReadOnlyList<string> GetOrFetch(Request key)
    {
        lock (Cache)
        {
            _latest = key;
            if (Cache.TryGetValue(key, out var suggestions)) return suggestions;
            if (Pending.Add(key)) _ = FetchAndRefreshAsync(key);
        }
        return [];
    }

    private static async Task FetchAndRefreshAsync(Request key)
    {
        try
        {
            await Task.Delay(SuggestionDebounce).ConfigureAwait(false);
            lock (Cache)
            {
                if (_latest != key) return;
            }

            var suggestions = await FetchSuggestionsAsync(key).ConfigureAwait(false);
            lock (Cache)
            {
                if (Cache.Count == CacheCapacity) Cache.Remove(CacheOrder.Dequeue());
                Cache.Add(key, suggestions);
                CacheOrder.Enqueue(key);
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or JsonException
            or FormatException or InvalidOperationException or NotSupportedException)
        {
            // The main search result still works. The next user query can retry this endpoint.
            return;
        }
        finally
        {
            lock (Cache) Pending.Remove(key);
        }
        SearchRefreshService.RefreshIfMatches(key.Matches);
    }

    private static async Task<List<string>> FetchSuggestionsAsync(Request key)
    {
        var url = WebSearchInstantProvider.BuildUrl(key.Endpoint, key.Term);
        using var request = new HttpRequestMessage(HttpMethod.Get, url);

        var cultureName = key.Culture;
        if (!string.IsNullOrWhiteSpace(cultureName))
        {
            try
            {
                request.Headers.AcceptLanguage.Add(new System.Net.Http.Headers.StringWithQualityHeaderValue(cultureName));
            }
            catch { }
        }

        using var response = await SuggestionHttpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync();
        return ParseOpenSearchSuggestions(json, key.Term);
    }

    internal static List<string> ParseOpenSearchSuggestions(string json, string term)
    {
        var result = new List<string>();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() < 2)
            return result;

        var suggestionsElement = root[1];
        if (suggestionsElement.ValueKind != JsonValueKind.Array)
            return result;

        foreach (var item in suggestionsElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
                continue;

            var text = item.GetString();
            if (string.IsNullOrWhiteSpace(text) || string.Equals(term, text, StringComparison.OrdinalIgnoreCase)
                || result.Contains(text, StringComparer.OrdinalIgnoreCase)) continue;
            result.Add(text);
            if (result.Count == 5) break;
        }
        return result;
    }
}
