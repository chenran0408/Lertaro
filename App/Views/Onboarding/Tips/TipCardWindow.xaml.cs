using System.Windows;
using Lertaro.App.Helpers.Visuals;
using Lertaro.App.Services;
using AppWindow = Lertaro.App.Views.Controls.AppWindow;
using Lertaro.App.ViewModels.Onboarding;
using Lertaro.Core;
using Lertaro.Core.Hook;

namespace Lertaro.App.Views.Onboarding.Tips;

/// <summary>
/// D1 point-of-use tip: a single-step card reusing the tour's visual language (AppWindow chrome,
/// settings buttons, placement), driven by a one-step OnboardingMachine so fold/timeout semantics
/// are identical to the tour. Shows once per tip id (SeenTips), never while the tour is active.
/// </summary>
public partial class TipCardWindow : AppWindow
{
    private readonly string _tipId;
    private readonly OnboardingMachine _machine = new(stepCount: 1);

    public TipCardWindow(string tipId, string title, string body)
    {
        InitializeComponent();
        _tipId = tipId;
        AltTabExcluder.Attach(this);
        SystemMenuBlocker.Attach(this, blockClose: false);
        TipTitle.Text = title;
        TipBody.Text = body;
    }

    public static void ShowTip(string tipId, IntPtr anchorHwnd)
    {
        // Tour takes priority: suppress all tips while it is on screen.
        if (OnboardingWindow.TourActive) return;
        var settings = UserSettings.Load();
        if (settings.SeenTips.Contains(tipId)) return;

        if (!TipRegistry.TryGetCopy(tipId, out var title, out var body)) return;
        var window = new TipCardWindow(tipId, title, body);
        window.Owner = Views.Controls.Dialogs.OwnedDialog.ResolveOwner(window);
        window.Show();
        window.PlaceNearAnchor(anchorHwnd);
    }

    private void PlaceNearAnchor(IntPtr anchorHwnd)
    {
        // Anchor to the dialog/panel that triggered us when we can, else bottom-right.
        if (anchorHwnd != IntPtr.Zero &&
            ExplorerNativeHooks.GetWindowRect(anchorHwnd, out var rect))
        {
            var scale = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
            Left = rect.Right / scale - 400;
            Top = rect.Top / scale + 8;
        }
        else
        {
            var area = SystemParameters.WorkArea;
            Left = area.Right - 400;
            Top = area.Bottom - 180;
        }
    }

    private void GotIt_Click(object sender, RoutedEventArgs e)
    {
        _machine.Handle(TourEvent.PracticeObserved); // one-step machine: completes
        var settings = UserSettings.Load();
        settings.SeenTips = [.. settings.SeenTips, _tipId];
        _ = settings.Save();
        Close();
    }
}
