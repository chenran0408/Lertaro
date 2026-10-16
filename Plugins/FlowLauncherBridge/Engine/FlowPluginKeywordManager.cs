using Flow.Launcher.Plugin;

namespace Lertaro.Plugins.FlowLauncherBridge.Engine;

/// <summary>Maintains keyword registrations and publishes snapshots safe for concurrent queries.</summary>
public sealed class FlowPluginKeywordManager
{
    private readonly object _gate = new();
    private readonly Dictionary<string, List<PluginPair>> _keywordPlugins = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<PluginPair> _globalPlugins = [];

    public IReadOnlyList<PluginPair> GlobalPlugins { get { lock (_gate) return _globalPlugins.ToArray(); } }
    public IReadOnlyDictionary<string, List<PluginPair>> KeywordPlugins
    {
        get { lock (_gate) return _keywordPlugins.ToDictionary(entry => entry.Key, entry => entry.Value.ToList(), StringComparer.OrdinalIgnoreCase); }
    }

    public void RegisterPluginKeywords(PluginPair pair)
    {
        lock (_gate)
        {
            if (pair.Metadata.Disabled) return;
            var keywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(pair.Metadata.ActionKeyword)) keywords.Add(pair.Metadata.ActionKeyword);
            foreach (var keyword in pair.Metadata.ActionKeywords ?? [])
                if (!string.IsNullOrWhiteSpace(keyword)) keywords.Add(keyword);
            if ((keywords.Contains("*") || keywords.Count == 0) && !_globalPlugins.Contains(pair))
                _globalPlugins.Add(pair);
            foreach (var keyword in keywords)
            {
                if (keyword == "*") continue;
                if (!_keywordPlugins.TryGetValue(keyword, out var list)) _keywordPlugins[keyword] = list = [];
                if (!list.Contains(pair)) list.Add(pair);
            }
        }
    }

    public void UnregisterPluginKeywords(PluginPair pair)
    {
        lock (_gate)
        {
            _globalPlugins.Remove(pair);
            foreach (var (key, list) in _keywordPlugins.ToArray())
            {
                list.RemoveAll(p => string.Equals(p.Metadata.ID, pair.Metadata.ID, StringComparison.OrdinalIgnoreCase));
                if (list.Count == 0) _keywordPlugins.Remove(key);
            }
        }
    }

    public void AddActionKeyword(PluginPair pair, string newActionKeyword)
    {
        if (string.IsNullOrWhiteSpace(newActionKeyword)) return;
        lock (_gate)
        {
            UnregisterPluginKeywords(pair);
            if (!pair.Metadata.ActionKeywords.Contains(newActionKeyword, StringComparer.OrdinalIgnoreCase))
                pair.Metadata.ActionKeywords.Add(newActionKeyword);
            RegisterPluginKeywords(pair);
        }
    }

    public void RemoveActionKeyword(PluginPair pair, string oldActionKeyword)
    {
        if (string.IsNullOrWhiteSpace(oldActionKeyword)) return;
        lock (_gate)
        {
            UnregisterPluginKeywords(pair);
            pair.Metadata.ActionKeywords.RemoveAll(k => string.Equals(k, oldActionKeyword, StringComparison.OrdinalIgnoreCase));
            if (string.Equals(pair.Metadata.ActionKeyword, oldActionKeyword, StringComparison.OrdinalIgnoreCase)) pair.Metadata.ActionKeyword = string.Empty;
            RegisterPluginKeywords(pair);
        }
    }

    public bool ActionKeywordAssigned(string actionKeyword)
    {
        lock (_gate) return !string.IsNullOrWhiteSpace(actionKeyword) && _keywordPlugins.TryGetValue(actionKeyword, out var list) && list.Count > 0;
    }

    public void UpdateActionKeyword(PluginPair pair, string newActionKeyword)
    {
        if (string.IsNullOrWhiteSpace(newActionKeyword)) return;
        lock (_gate)
        {
            UnregisterPluginKeywords(pair);
            pair.Metadata.ActionKeyword = newActionKeyword;
            pair.Metadata.ActionKeywords.Clear();
            pair.Metadata.ActionKeywords.Add(newActionKeyword);
            RegisterPluginKeywords(pair);
        }
    }

    public void Clear()
    {
        lock (_gate) { _globalPlugins.Clear(); _keywordPlugins.Clear(); }
    }
}
