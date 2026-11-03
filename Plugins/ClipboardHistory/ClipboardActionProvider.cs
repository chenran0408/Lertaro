using System.Globalization;
using Lertaro.PluginSdk.Abstractions;
using Lertaro.PluginSdk.Abstractions.Plugins;
using Lertaro.PluginSdk.Services;

namespace Lertaro.Plugins.ClipboardHistory;

public sealed class ClipboardActionProvider : IDynamicActionProvider
{
    /// <summary>Stable command ids: the menu is built from this table and dispatch reads the same
    /// constants, so adding or reordering an item can never silently shift what an id executes.</summary>
    internal enum MenuCommand
    {
        Paste = 101,
        Copy = 102,
        TogglePin = 103,
        Delete = 104,
        OpenSource = 105,
        ClearHistory = 106,
    }

    public string GroupName => TranslationService.Get("ClipboardHistory_MenuGroup");
    public bool CanProvideForInstantResults => true;
    public bool CanProvide(IReadOnlyList<ISearchResult> results) => TryGetId(results, out _);

    public IEnumerable<DynamicMenuItem> GetMenuItems(IReadOnlyList<ISearchResult> results, nint hMenu)
    {
        if (hMenu != 0 || !TryGetId(results, out var id)) yield break;
        var runtime = ClipboardRuntime.Instance;
        var entry = runtime.Entries.FirstOrDefault(entry => entry.Id == id);
        if (entry == null) yield break;

        var items = new (MenuCommand Command, string Key, string Shortcut)[]
        {
            (MenuCommand.Paste, "Paste", "P"),
            (MenuCommand.Copy, "Copy", "C"),
            (MenuCommand.TogglePin, entry.Pinned ? "Unpin" : "Pin", "T"),
            (MenuCommand.Delete, "Delete", "D"),
            (MenuCommand.OpenSource, "OpenSource", "O"),
            (MenuCommand.ClearHistory, "ClearHistory", "A"),
        };
        foreach (var (command, key, shortcut) in items)
        {
            yield return new DynamicMenuItem
            {
                CommandId = (uint)command,
                Text = TranslationService.Get("ClipboardHistory_" + key),
                ShortcutHint = shortcut,
                IsDisabled = command == MenuCommand.OpenSource && !runtime.CanOpenSource(id),
                OnExecute = () => Execute(id, command)
            };
        }
    }

    public void ExecuteCommand(IReadOnlyList<ISearchResult> results, uint commandId, nint ownerHwnd)
    {
        if (TryGetId(results, out var id) && Enum.IsDefined((MenuCommand)commandId))
            Execute(id, (MenuCommand)commandId);
    }

    public void ClearSession() { }

    private static void Execute(long id, MenuCommand command)
    {
        var runtime = ClipboardRuntime.Instance;
        switch (command)
        {
            case MenuCommand.Paste: runtime.Paste(id); break;
            case MenuCommand.Copy: runtime.Copy(id); break;
            case MenuCommand.TogglePin: runtime.TogglePin(id); break;
            case MenuCommand.Delete: runtime.Delete(id); break;
            case MenuCommand.OpenSource: runtime.OpenSource(id); break;
            case MenuCommand.ClearHistory: runtime.ClearHistory(); break;
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
