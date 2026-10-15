using System.Runtime.InteropServices;
using Lertaro.Core.Hook;

namespace Lertaro.Core.Tests.Hook;

[TestClass, DoNotParallelize]
public sealed class InternalFileDialogTests
{
    [TestMethod]
    public void OwnedDialogAndChildAreExcludedButSettingsWindowIsNotADialog()
    {
        var owner = Create("STATIC");
        var dialog = Create("#32770", owner);
        var child = Create("EDIT", dialog, child: true);
        try
        {
            Assert.IsTrue(ExplorerFocusChangeFilter.IsInternalDialog(dialog, 0));
            Assert.IsTrue(ExplorerFocusChangeFilter.IsInternalDialog(child, (uint)Environment.ProcessId));
            Assert.IsFalse(ExplorerFocusChangeFilter.IsInternalDialog(owner, (uint)Environment.ProcessId));
            Assert.IsFalse(ExplorerFocusChangeFilter.IsInternalDialog(IntPtr.Zero, 0));
        }
        finally { DestroyWindow(owner); }
    }

    [TestMethod]
    public void ClassifierClearsPreviousHostWhenInternalPickerTakesFocus()
    {
        var dialog = Create("#32770");
        try
        {
            using var tracker = new ExplorerTracker(() => dialog)
            {
                ActiveHwnd = new IntPtr(0x1234),
                IsExplorerOrDesktopActive = true,
                IsActiveWindowDialog = true,
                LastPath = @"C:\Previous"
            };
            var deactivated = 0;
            tracker.OnExplorerDeactivated += () => deactivated++;
            tracker.ReclassifyActiveWindow(dialog);
            Assert.AreEqual(IntPtr.Zero, tracker.ActiveHwnd);
            Assert.IsFalse(tracker.IsActiveWindowDialog);
            Assert.IsFalse(tracker.IsExplorerOrDesktopActive);
            Assert.IsNull(tracker.LastPath);
            Assert.AreEqual(1, deactivated);
        }
        finally { DestroyWindow(dialog); }
    }

    [TestMethod]
    public void ForegroundPollDoesNotReattachToInternalPicker()
    {
        var dialog = Create("#32770");
        try
        {
            using var tracker = new ExplorerTracker(() => dialog);
            var classifier = new ExplorerWindowClassifier(tracker, new FileDialogNavigationTracker());
            using var poller = new ExplorerActivePathPoller(tracker, classifier, () => dialog);
            var activated = 0;
            tracker.OnExplorerActivated += (_, _, _, _) => activated++;
            poller.Poll(tracker, ExplorerNativeHooks.EVENT_SYSTEM_FOREGROUND, dialog);
            Assert.AreEqual(0, activated);
            Assert.AreEqual(IntPtr.Zero, tracker.ActiveHwnd);
            Assert.IsFalse(tracker.IsActiveWindowDialog);
        }
        finally { DestroyWindow(dialog); }
    }

    private static IntPtr Create(string className, IntPtr parent = default, bool child = false)
    {
        var hwnd = CreateWindowEx(0, className, "Internal picker regression test",
            child ? 0x40000000u : 0x80000000u, 0, 0, 100, 100, parent, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        Assert.AreNotEqual(IntPtr.Zero, hwnd, $"CreateWindowEx failed: {Marshal.GetLastWin32Error()}");
        return hwnd;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowEx(uint exStyle, string className, string title, uint style,
        int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr hwnd);
}
