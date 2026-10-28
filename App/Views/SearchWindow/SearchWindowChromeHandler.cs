using System.Windows;
using Lertaro.Core;
using System.Windows.Input;
using Lertaro.App.Helpers.Visuals;
using TextBox = System.Windows.Controls.TextBox;

namespace Lertaro.App.Views.SearchWindow;

public class SearchWindowChromeHandler
{
    private readonly Lertaro.App.SearchWindow _window;

    public SearchWindowChromeHandler(Lertaro.App.SearchWindow window) => _window = window;

    public void HandleHeaderMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is TextBox)
        {
            return;
        }

        if (e.LeftButton == MouseButtonState.Pressed)
        {
            if (e.ClickCount == 2)
            {
                ToggleMaximize();
            }
            else
            {
                WindowMaximizedDragHelper.DragMoveOrRestore(_window, e);
            }
        }
    }

    public void HandleStateChanged()
    {
        _window.BtnMaximize?.Content = _window.WindowState == WindowState.Maximized ? "\uE923" : "\uE922";

    }

    public void Minimize() => _window.WindowState = WindowState.Minimized;

    public void ToggleMaximize() => _window.WindowState = _window.WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;

    public void Close()
    {
        Logger.Log("[AppLifecycle] Full search close button clicked.");
        _window.Close();
    }
}
