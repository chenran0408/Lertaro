using Lertaro.Core;

namespace Lertaro.App.ViewModels.QuickPanel;

// Panel-only labels never rename the underlying file or change its shortcut target.
public sealed class QuickPanelItemLabels : ViewModelBase
{
    private Dictionary<string, QuickPanelItemLabel> _values;
    public QuickPanelItemLabels(IReadOnlyDictionary<string, QuickPanelItemLabel>? values = null)
        => _values = values?.ToDictionary(p => p.Key, p => p.Value.Clone(), StringComparer.OrdinalIgnoreCase) ?? new(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyDictionary<string, QuickPanelItemLabel> Values => _values;
    public string Name(string path, string original, bool isDirectory = false)
        => DisplayName(path, original, isDirectory, _values.GetValueOrDefault(path));
    internal static string DisplayName(string path, string original, bool isDirectory, QuickPanelItemLabel? label = null)
    {
        if (!string.IsNullOrEmpty(label?.DisplayName)) return label.DisplayName;
        if (isDirectory) return original;
        var extension = System.IO.Path.GetExtension(string.IsNullOrEmpty(path) ? original : path);
        return extension.Length > 0 && original.Length > extension.Length && original.EndsWith(extension, StringComparison.OrdinalIgnoreCase)
            ? original[..^extension.Length] : original;
    }
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
