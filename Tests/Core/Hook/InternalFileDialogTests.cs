using System.Runtime.InteropServices;
using Lertaro.Core.Hook;
using Lertaro.Core.Hook.InlineSearch;
using Lertaro.PluginSdk.Abstractions.Plugins.WindowAdapters;
using Lertaro.PluginSdk.Registries;

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
    public void ClassifierClearsPreviousHostWhenUnrecognizedInternalDialogTakesFocus()
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
    public void ForegroundPollDoesNotReattachToUnrecognizedInternalDialog()
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

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void RecognizedOwnFilePicker_IsTrackedThroughClassifierAndForegroundPoll(bool usePoller)
    {
        var owner = Create("STATIC");
        var dialog = Create("#32770", owner);
        var adapter = new PickerAdapter { Target = dialog };
        FileDialogAdapterRegistry.Register(adapter);
        try
        {
            using var tracker = new ExplorerTracker(() => dialog) { AppProcessId = (uint)Environment.ProcessId };
            var activated = 0;
            tracker.OnExplorerActivated += (_, _, _, _) => activated++;
            var classifier = new ExplorerWindowClassifier(tracker, new FileDialogNavigationTracker());
            using var poller = new ExplorerActivePathPoller(tracker, classifier, () => dialog);
            if (usePoller) poller.Poll(tracker, ExplorerNativeHooks.EVENT_SYSTEM_FOREGROUND, dialog);
            else tracker.ReclassifyActiveWindow(dialog);
            Assert.AreEqual(dialog, tracker.ActiveHwnd, "Track the picker itself, not its settings owner.");
            Assert.IsTrue(tracker.IsActiveWindowDialog);
            Assert.AreSame(adapter, tracker.ActiveAdapter);
            Assert.AreEqual(adapter.Path, tracker.LastPath);
            Assert.AreEqual(1, activated);

            adapter.Path = @"C:\Pictures\Changed";
            tracker.RequestHostPathRead();
            poller.Poll(tracker, ExplorerNativeHooks.EVENT_SYSTEM_FOREGROUND, dialog);
            Assert.AreEqual(adapter.Path, tracker.LastPath, "An own picker must retain folder updates.");
            Assert.AreEqual(1, activated, "Polling must not recreate the search window.");

            DestroyWindow(dialog);
            tracker.ReclassifyActiveWindow(owner);
            Assert.IsFalse(tracker.IsActiveWindowDialog);
            Assert.AreEqual(IntPtr.Zero, tracker.ActiveHwnd);
        }
        finally { adapter.Target = 0; DestroyWindow(owner); }
    }

    [TestMethod]
    public void OwnPickerWhoseControlsAppearLate_IsRetriedWithoutAnotherForegroundChange()
    {
        var dialog = Create("#32770");
        var adapter = new PickerAdapter();
        FileDialogAdapterRegistry.Register(adapter);
        try
        {
            using var tracker = new ExplorerTracker(() => dialog);
            var classifier = new ExplorerWindowClassifier(tracker, new FileDialogNavigationTracker());
            using var poller = new ExplorerActivePathPoller(tracker, classifier, () => dialog);
            poller.Poll(tracker, ExplorerNativeHooks.EVENT_SYSTEM_FOREGROUND, dialog);
            Assert.IsFalse(tracker.IsActiveWindowDialog);
            adapter.Target = dialog;
            Assert.IsTrue(SpinWait.SpinUntil(() => tracker.IsActiveWindowDialog && tracker.LastPath == adapter.Path, 2000),
                "The ordinary dialog retry must also cover our own picker.");
            Assert.AreEqual(dialog, tracker.ActiveHwnd);
        }
        finally { adapter.Target = 0; DestroyWindow(dialog); }
    }

    [TestMethod]
    public void OwnPickerAllowsNavigationHotkeys_WhileOwnSearchAndSettingsKeepNormalInput()
    {
        using var tracker = new ExplorerTracker();
        var pid = (uint)Environment.ProcessId;
        tracker.ActiveHwnd = 10;
        tracker.IsActiveWindowDialog = true;
        Assert.IsFalse(KeyboardHookService.ShouldBypassOwnWindow(pid, 10, pid, tracker));
        Assert.IsTrue(KeyboardHookService.ShouldBypassOwnWindow(pid, 20, pid, tracker));
        tracker.IsActiveWindowDialog = false;
        Assert.IsTrue(KeyboardHookService.ShouldBypassOwnWindow(pid, 10, pid, tracker));
        Assert.IsFalse(KeyboardHookService.ShouldBypassOwnWindow(pid + 1, 10, pid, tracker));
    }

    private sealed class PickerAdapter : IFileDialogAdapter
    {
        internal nint Target;
        internal string Path = @"C:\Pictures";
        public string Name => "Internal picker regression adapter";
        public bool CanHandle(nint hwnd, string className, string processName) => Target != 0 && hwnd == Target && className == "#32770";
        public string? GetCurrentPath(nint hwnd) => Path;
        public bool NavigateTo(nint hwnd, string targetPath) => false;
        public bool RestoreFocus(nint hwnd) => false;
        public bool GetDockBounds(nint hwnd, out AdapterRect rect) { rect = default; return false; }
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
