namespace Lertaro.App.Services;

/// <summary>Runs a window's show callback on its owning dispatcher.</summary>
internal static class WindowPresentationHelper
{
    internal static Task ShowAsync(Views.Controls.AppWindow window, Action show, Func<bool>? isCurrent = null)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(show);

        void ShowIfCurrent()
        {
            if (isCurrent is null || isCurrent()) show();
        }

        if (window.Dispatcher.CheckAccess())
        {
            ShowIfCurrent();
            return Task.CompletedTask;
        }

        return window.Dispatcher.InvokeAsync(ShowIfCurrent).Task;
    }
}
