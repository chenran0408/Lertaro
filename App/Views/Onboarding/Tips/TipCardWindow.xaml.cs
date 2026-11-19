using System.Windows;
using System.Windows.Interop;
using Lertaro.App.Helpers.Visuals;
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
    private static readonly HashSet<string> OpenTips = new(StringComparer.Ordinal);
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

        if (OpenTips.Contains(tipId) || !TipRegistry.TryGetCopy(tipId, out var title, out var body)) return;
        var window = new TipCardWindow(tipId, title, body);
        window.Owner = Controls.Dialogs.OwnedDialog.ResolveOwner(window);
        window.Closed += (_, _) => OpenTips.Remove(tipId);
        OpenTips.Add(tipId);
        window.Show();
        window.PlaceNearAnchor(anchorHwnd);
    }

    private void PlaceNearAnchor(IntPtr anchorHwnd)
    {
        var handle = anchorHwnd == IntPtr.Zero ? new WindowInteropHelper(this).Handle : anchorHwnd;
        var area = Screen.FromHandle(handle).WorkingArea;
        var transform = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformToDevice;
        var scaleX = transform?.M11 ?? 1.0;
        var scaleY = transform?.M22 ?? 1.0;
        var right = area.Right / scaleX;
        var bottom = area.Bottom / scaleY;
        var left = area.Left / scaleX;
        var top = area.Top / scaleY;

        var x = right - ActualWidth - 12;
        var y = bottom - ActualHeight - 12;
        if (anchorHwnd != IntPtr.Zero && ExplorerNativeHooks.GetWindowRect(anchorHwnd, out var rect))
        {
            x = rect.Right / scaleX - ActualWidth;
            y = rect.Top / scaleY + 8;
        }

        Left = Math.Clamp(x, left, Math.Max(left, right - ActualWidth));
        Top = Math.Clamp(y, top, Math.Max(top, bottom - ActualHeight));
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
