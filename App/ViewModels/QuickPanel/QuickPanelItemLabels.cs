using Lertaro.Core;

namespace Lertaro.App.ViewModels.QuickPanel;

// Panel-only labels never rename the underlying file or change its shortcut target.
public sealed class QuickPanelItemLabels : ViewModelBase
{
    private Dictionary<string, QuickPanelItemLabel> _values;
    public QuickPanelItemLabels(IReadOnlyDictionary<string, QuickPanelItemLabel>? values = null)
        => _values = values?.ToDictionary(p => p.Key, p => p.Value.Clone(), StringComparer.OrdinalIgnoreCase) ?? new(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyDictionary<string, QuickPanelItemLabel> Values => _values;
    public string Name(string path, string original)
        => _values.TryGetValue(path, out var value) && value.DisplayName.Length > 0 ? value.DisplayName : original;
    public void Set(string path, string name, bool hidden)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        _values = new Dictionary<string, QuickPanelItemLabel>(_values, StringComparer.OrdinalIgnoreCase);
        name = name.Trim();
        if (name.Length == 0 && !hidden) _values.Remove(path);
        else _values[path] = new QuickPanelItemLabel { DisplayName = name, HideName = hidden };
        OnPropertyChanged(nameof(Values));
    }
}
