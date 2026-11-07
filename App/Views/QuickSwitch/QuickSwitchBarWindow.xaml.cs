using System.Windows;
using System.Windows.Interop;
using Lertaro.App.Helpers.Visuals;
using Lertaro.App.Services;
using Lertaro.Core;
using Lertaro.Core.Hook;
using Lertaro.Core.Wire;

namespace Lertaro.App.Views.QuickSwitch;

/// <summary>
/// C1: the Quick Switch discoverability skin. When a file dialog is foreground and the tracker has
/// a suggested folder behind it, a small clickable bar floats at the dialog's top edge; clicking it
/// sends the same NavigateDialog IPC the hotkey path drives. Never activates, never enters
/// Alt-Tab, dismisses when the dialog goes away. Shows once per dialog handle -- teach, then leave.
/// </summary>
public partial class QuickSwitchBarWindow : Window
{
    private readonly IntPtr _dialogHwnd;
    private readonly string _path;

    public QuickSwitchBarWindow(IntPtr dialogHwnd, string suggestedPath)
    {
        InitializeComponent();
        _dialogHwnd = dialogHwnd;
        _path = suggestedPath;
        SuggestionText.Text = string.Format(TranslationManager.Instance["QuickSwitch_BarSuggestion"], suggestedPath);
        AltTabExcluder.Attach(this);
        SourceInitialized += (_, _) =>
        {
            if (PresentationSource.FromVisual(this) is HwndSource source)
                source.AddHook((_, msg, _, _, ref handled) =>
                {
                    // Dismiss when the dialog we serve dies (WM_DESTROY observed indirectly via the
                    // bar being deactivated by a dead window is unreliable; the manager hides us).
                    return IntPtr.Zero;
                });
        };
    }

    public IntPtr DialogHwnd => _dialogHwnd;

    public void PlaceAbove(IntPtr dialogHwnd)
    {
        if (new WindowInteropHelper(this).Handle == IntPtr.Zero) return;
        // Physical pixels -> DIPs (the bar sits on the dialog's own monitor, which is not
        // necessarily the primary one the WPF scale would report).
        if (ExplorerNativeHooks.GetWindowRect(dialogHwnd, out var rect))
        {
            var scale = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
            var widthDip = Width > 0 ? Width : 420;
            Left = rect.Left / scale + ((rect.Right - rect.Left) / scale - widthDip) / 2;
            Top = rect.Top / scale + 6;
        }
    }

    private void Go_Click(object sender, RoutedEventArgs e)
    {
        App.HookClient?.SendMessage(new IpcMessage
        {
            Id = IpcMessageId.NavigateDialog,
            Hwnd = _dialogHwnd.ToInt64(),
            StringVal1 = _path,
        });
        Hide();
    }
}
