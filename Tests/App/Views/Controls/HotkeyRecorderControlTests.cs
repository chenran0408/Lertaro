using System.Reflection;
using System.Windows.Interop;
using System.Windows.Input;
using Lertaro.App.Views.Controls;

namespace Lertaro.App.Tests.Views.Controls;

[TestClass]
public sealed class HotkeyRecorderControlTests
{
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

