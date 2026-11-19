using System.Reflection;
using System.Windows.Interop;
using System.Windows.Input;
using Lertaro.App.Views.Controls;

namespace Lertaro.App.Tests.Views.Controls;

[TestClass]
public sealed class HotkeyRecorderControlTests
{
    [STATestMethod]
    public void RecorderAcceptsTheClipboardDefaultAndRejectsWindowsClipboardShortcut()
    {
        var recorder = new HotkeyRecorderControl { Value = Lertaro.Core.HotkeyPageSettings.DefaultClipboardPanelHotkey };
        Assert.AreEqual("Alt+V", recorder.Value);
        recorder.Value = "Win+V";
        Assert.AreEqual(string.Empty, recorder.Value);
    }

    [STATestMethod]
    public void EscapeCancelsPendingCaptureWithoutErasingExistingBinding()
    {
        using var source = new HwndSource(new HwndSourceParameters("HotkeyRecorderCancelTest"));
        var recorder = new HotkeyRecorderControl { Value = "Ctrl+G" };
        var down = typeof(HotkeyRecorderControl).GetMethod("HotkeyBox_PreviewKeyDown",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        var commit = typeof(HotkeyRecorderControl).GetMethod("CommitPendingSingleKey",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        KeyEventArgs Press(Key key) => new(Keyboard.PrimaryDevice, source, Environment.TickCount, key)
        {
            RoutedEvent = Keyboard.PreviewKeyDownEvent
        };
        down.Invoke(recorder, [null, Press(Key.F8)]);
        down.Invoke(recorder, [null, Press(Key.Escape)]);
        commit.Invoke(recorder, []);

        Assert.AreEqual("Ctrl+G", recorder.Value);
    }

    [STATestMethod]
    public void TwoBareKeyPressesAreRecordedAsExplicitDoubleTap()
    {
        using var source = new HwndSource(new HwndSourceParameters("HotkeyRecorderDoubleTapTest"));
        var recorder = new HotkeyRecorderControl();
        var previewKeyDown = typeof(HotkeyRecorderControl).GetMethod("HotkeyBox_PreviewKeyDown",
            BindingFlags.Instance | BindingFlags.NonPublic)!;

        KeyEventArgs Press() => new(Keyboard.PrimaryDevice, source, Environment.TickCount, Key.F8) { RoutedEvent = Keyboard.PreviewKeyDownEvent };

        previewKeyDown.Invoke(recorder, [null, Press()]);
        Assert.AreEqual(string.Empty, recorder.Value, "The recorder waits briefly to distinguish a single key from a double tap.");

        previewKeyDown.Invoke(recorder, [null, Press()]);

        Assert.AreEqual("DoubleTap:F8", recorder.Value);
    }

    [STATestMethod]
    public void RequireModifierRecorderStillAcceptsTwoBareKeyPressesAsDoubleTap()
    {
        using var source = new HwndSource(new HwndSourceParameters("RequiredModifierDoubleTapTest"));
        var recorder = new HotkeyRecorderControl { RequireModifier = true };
        var previewKeyDown = typeof(HotkeyRecorderControl).GetMethod("HotkeyBox_PreviewKeyDown",
            BindingFlags.Instance | BindingFlags.NonPublic)!;

        KeyEventArgs Press() => new(Keyboard.PrimaryDevice, source, Environment.TickCount, Key.F8) { RoutedEvent = Keyboard.PreviewKeyDownEvent };

        previewKeyDown.Invoke(recorder, [null, Press()]);
        previewKeyDown.Invoke(recorder, [null, Press()]);

        Assert.AreEqual("DoubleTap:F8", recorder.Value);
    }
}

