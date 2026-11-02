using System.Globalization;
using Lertaro.PluginSdk.Abstractions;
using Lertaro.PluginSdk.Abstractions.Plugins;
using Lertaro.PluginSdk.Services;

namespace Lertaro.Plugins.ClipboardHistory;

public sealed class ClipboardActionProvider : IDynamicActionProvider
{
    public string GroupName => TranslationService.Get("ClipboardHistory_MenuGroup");
    public bool CanProvideForInstantResults => true;
    public bool CanProvide(IReadOnlyList<ISearchResult> results) => TryGetId(results, out _);

    public IEnumerable<DynamicMenuItem> GetMenuItems(IReadOnlyList<ISearchResult> results, nint hMenu)
    {
        if (hMenu != 0 || !TryGetId(results, out var id)) yield break;
        var runtime = ClipboardRuntime.Instance;
        var entry = runtime.Entries.FirstOrDefault(entry => entry.Id == id);
        if (entry == null) yield break;
        string[] keys = ["Paste", "Copy", entry.Pinned ? "Unpin" : "Pin", "Delete", "OpenSource", "ClearHistory"];
        string[] shortcuts = ["P", "C", "T", "D", "O", "A"];
        for (uint command = 1; command <= keys.Length; command++)
        {
            var captured = command;
            yield return new DynamicMenuItem
            {
                CommandId = command,
                Text = TranslationService.Get("ClipboardHistory_" + keys[command - 1]),
                ShortcutHint = shortcuts[command - 1],
                IsDisabled = command == 5 && !runtime.CanOpenSource(id),
                OnExecute = () => Execute(id, captured)
            };
        }
    }

    public void ExecuteCommand(IReadOnlyList<ISearchResult> results, uint commandId, nint ownerHwnd)
    {
        if (TryGetId(results, out var id)) Execute(id, commandId);
    }

    public void ClearSession() { }

    private static void Execute(long id, uint command)
    {
        var runtime = ClipboardRuntime.Instance;
        switch (command)
        {
            case 1: runtime.Paste(id); break;
            case 2: runtime.Copy(id); break;
            case 3: runtime.TogglePin(id); break;
            case 4: runtime.Delete(id); break;
            case 5: runtime.OpenSource(id); break;
            case 6: runtime.ClearHistory(); break;
        }
    }

    internal static bool TryGetId(IReadOnlyList<ISearchResult> results, out long id)
    {
        id = 0;
        return results.Count == 1 && results[0].InstantActionArgument is { } argument &&
            argument.StartsWith("clip:", StringComparison.Ordinal) &&
            long.TryParse(argument.AsSpan(5), NumberStyles.None, CultureInfo.InvariantCulture, out id) && id > 0;
    }
}
