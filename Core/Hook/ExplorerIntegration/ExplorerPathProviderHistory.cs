using Lertaro.PluginSdk.Abstractions.Plugins.WindowAdapters;

namespace Lertaro.Core.Hook;

internal sealed class ExplorerPathProviderHistory
{
    private const int MaxProviders = 16;
    private readonly List<(IntPtr Hwnd, uint ProcessId, string Path)> _providers = [];

    internal (IInlineSearchAdapter Adapter, IntPtr Hwnd)? LastProvider { get; private set; }

    internal void Remember(IInlineSearchAdapter? adapter, IntPtr hwnd, uint processId, string? path = null)
    {
        if (adapter == null || hwnd == IntPtr.Zero || !adapter.IsFileExplorer) return;
        LastProvider = (adapter, hwnd);
        if (string.IsNullOrEmpty(path)) return;

        _providers.RemoveAll(entry => entry.Hwnd == hwnd);
        _providers.Insert(0, (hwnd, processId, path));
        if (_providers.Count > MaxProviders)
            _providers.RemoveRange(MaxProviders, _providers.Count - MaxProviders);
    }

    internal void Refresh(IntPtr hwnd, string path)
    {
        if (hwnd == IntPtr.Zero || string.IsNullOrEmpty(path)) return;
        var index = _providers.FindIndex(entry => entry.Hwnd == hwnd);
        if (index < 0) return;
        var entry = _providers[index];
        _providers.RemoveAt(index);
        _providers.Insert(0, (entry.Hwnd, entry.ProcessId, path));
    }

    internal string? Select(uint dialogProcessId, Func<IntPtr, bool> isWindowAlive, string? fallbackPath)
    {
        _providers.RemoveAll(entry => !isWindowAlive(entry.Hwnd));
        if (dialogProcessId != 0)
        {
            var sameProcess = _providers.FirstOrDefault(entry =>
                entry.ProcessId == dialogProcessId && !string.IsNullOrEmpty(entry.Path));
            if (!string.IsNullOrEmpty(sameProcess.Path)) return sameProcess.Path;
        }

        foreach (var entry in _providers)
            if (!string.IsNullOrEmpty(entry.Path)) return entry.Path;

        return fallbackPath;
    }

    internal void Clear()
    {
        LastProvider = null;
        _providers.Clear();
    }
}
