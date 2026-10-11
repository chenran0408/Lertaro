namespace Lertaro.App.Views.QuickPanel;

internal sealed class QuickPanelArrangePinState
{
    private bool? _previous;
    internal bool Update(bool arranging, bool pinned)
    {
        if (arranging) { _previous ??= pinned; return true; }
        var restored = _previous ?? pinned;
        _previous = null;
        return restored;
    }
}
