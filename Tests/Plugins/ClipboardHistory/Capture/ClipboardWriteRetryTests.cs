using System.Runtime.InteropServices;
using Lertaro.Plugins.ClipboardHistory.Capture;

namespace Lertaro.Plugins.ClipboardHistory.Tests.Capture;

[TestClass]
public sealed class ClipboardWriteRetryTests
{
    [TestMethod]
    public void TransientBusyClipboard_IsRetriedUntilSuccess()
    {
        var attempts = 0;
        ClipboardWriteRetry.Execute(() =>
        {
            if (attempts++ < 2) throw new COMException("busy", unchecked((int)0x800401D0));
        });
        Assert.AreEqual(3, attempts);
    }

    [TestMethod]
    public void PersistentBusyClipboard_PropagatesAfterRetryBudget()
    {
        var attempts = 0;
        var ex = Assert.ThrowsExactly<COMException>(() => ClipboardWriteRetry.Execute(() =>
        {
            attempts++;
            throw new COMException("busy", unchecked((int)0x800401D0));
        }));
        Assert.AreEqual(ClipboardTextReader.MaxRetries + 1, attempts);
        Assert.AreEqual(unchecked((int)0x800401D0), ex.HResult);
    }

    [TestMethod]
    public void NonBusyClipboardFailure_IsNotRetried()
    {
        var attempts = 0;
        Assert.ThrowsExactly<COMException>(() => ClipboardWriteRetry.Execute(() =>
        {
            attempts++;
            throw new COMException("locked", unchecked((int)0x800401D3));
        }));
        Assert.AreEqual(1, attempts);
    }
}
