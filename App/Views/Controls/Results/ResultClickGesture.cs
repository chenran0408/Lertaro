namespace Lertaro.App.Views.Controls.Results;

// A release redirected from a closing popup must not launch the row newly exposed underneath it.
internal sealed class ResultClickGesture
{
    private AppSearchResult? _pressed;
    internal void Press(AppSearchResult? result, bool allowed) => _pressed = allowed ? result : null;
    internal bool Release(AppSearchResult? result, bool allowed)
    {
        var pressed = _pressed;
        _pressed = null;
        return allowed && pressed != null && ReferenceEquals(pressed, result);
    }
    internal void Cancel() => _pressed = null;
}
