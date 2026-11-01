using Lertaro.Core.Hook;
using Lertaro.PluginSdk.Abstractions.Plugins.WindowAdapters;

namespace Lertaro.Core.Tests.Hook.ExplorerIntegration;

[TestClass]
public sealed class ExplorerDialogBoundsTests
{
    private sealed class BoundsAdapter : IFileDialogAdapter
    {
        internal bool HasTargetBounds { get; init; }
        internal AdapterRect TargetBounds { get; init; }
        internal bool HasFileListBounds { get; init; }
        internal AdapterRect FileListBounds { get; init; }

        public bool CanHandle(IntPtr hwnd, string className, string processName) => false;
        public string? GetCurrentPath(IntPtr hwnd) => null;
        public bool NavigateTo(IntPtr hwnd, string targetPath) => false;
        public bool GetDockBounds(IntPtr hwnd, out AdapterRect rect) { rect = default; return false; }
        public bool RestoreFocus(IntPtr hwnd) => false;
        public bool TryGetTargetFieldBounds(IntPtr hwnd, out AdapterRect bounds)
        {
            bounds = TargetBounds;
            return HasTargetBounds;
        }
        public bool TryGetFileListBounds(IntPtr hwnd, out AdapterRect bounds)
        {
            bounds = FileListBounds;
            return HasFileListBounds;
        }
    }

    [TestMethod]
    public void TryGetTargetFieldRect_RejectsMissingWindowAdapterAndEmptyBounds()
    {
        var adapter = new BoundsAdapter
        {
            HasTargetBounds = true,
            TargetBounds = new AdapterRect { Left = 4, Top = 5, Right = 4, Bottom = 20 }
        };

        Assert.IsFalse(ExplorerDialogBounds.TryGetTargetFieldRect(IntPtr.Zero, adapter, out _));
        Assert.IsFalse(ExplorerDialogBounds.TryGetTargetFieldRect(new IntPtr(1), null, out _));
        Assert.IsFalse(ExplorerDialogBounds.TryGetTargetFieldRect(new IntPtr(1), new BoundsAdapter(), out _));
        Assert.IsFalse(ExplorerDialogBounds.TryGetTargetFieldRect(new IntPtr(1), adapter, out _));
    }

    [TestMethod]
    public void TryGetTargetFieldRect_ConvertsUsableBounds()
    {
        var adapter = new BoundsAdapter
        {
            HasTargetBounds = true,
            TargetBounds = new AdapterRect { Left = -20, Top = 5, Right = 40, Bottom = 25 }
        };

        Assert.IsTrue(ExplorerDialogBounds.TryGetTargetFieldRect(new IntPtr(1), adapter, out var rect));
        Assert.AreEqual(-20, rect.Left);
        Assert.AreEqual(5, rect.Top);
        Assert.AreEqual(40, rect.Right);
        Assert.AreEqual(25, rect.Bottom);
    }

    [TestMethod]
    public void TryGetFileListRect_RejectsUnavailableAndConvertsUsableBounds()
    {
        var unavailable = new BoundsAdapter();
        Assert.IsFalse(ExplorerDialogBounds.TryGetFileListRect(new IntPtr(1), unavailable, out _));

        var adapter = new BoundsAdapter
        {
            HasFileListBounds = true,
            FileListBounds = new AdapterRect { Left = 1, Top = 2, Right = 11, Bottom = 22 }
        };
        Assert.IsTrue(ExplorerDialogBounds.TryGetFileListRect(new IntPtr(1), adapter, out var rect));
        Assert.AreEqual(1, rect.Left);
        Assert.AreEqual(2, rect.Top);
        Assert.AreEqual(11, rect.Right);
        Assert.AreEqual(22, rect.Bottom);
    }
}
