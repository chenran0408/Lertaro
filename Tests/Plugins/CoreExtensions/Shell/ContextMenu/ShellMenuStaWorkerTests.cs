using System.Windows.Threading;
using Lertaro.Plugins.CoreExtensions.Shell.ContextMenu;

namespace Lertaro.Plugins.CoreExtensions.Tests.Shell.ContextMenu;

[TestClass]
public sealed class ShellMenuStaWorkerTests
{
    [TestMethod]
    public async Task PostCleanup_ReturnsWhileOwnerIsBlockedAndReleasesOnOwner()
    {
        var ready = new TaskCompletionSource<Dispatcher>(TaskCreationOptions.RunContinuationsAsynchronously);
        var blocked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cleaned = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            ready.SetResult(Dispatcher.CurrentDispatcher);
            Dispatcher.Run();
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        var owner = await ready.Task.WaitAsync(TimeSpan.FromSeconds(5));
        _ = owner.InvokeAsync(() =>
        {
            blocked.SetResult();
            release.Task.GetAwaiter().GetResult();
        });
        Task? posting = null;
        try
        {
            await blocked.Task.WaitAsync(TimeSpan.FromSeconds(5));
            posting = Task.Run(() => ShellMenuStaWorker.PostCleanup(owner,
                () => cleaned.SetResult(Environment.CurrentManagedThreadId)));
            await posting.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.IsFalse(cleaned.Task.IsCompleted, "Cleanup must be queued behind the owner's native call.");
            release.SetResult();
            Assert.AreEqual(thread.ManagedThreadId, await cleaned.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        }
        finally
        {
            release.TrySetResult();
            if (posting != null) await posting.WaitAsync(TimeSpan.FromSeconds(5));
            owner.BeginInvokeShutdown(DispatcherPriority.Background);
            Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(5)));
        }
    }

    [TestMethod]
    public void PostCleanup_OnOwnerRunsImmediatelyAndContainsErrors()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var owner = Dispatcher.CurrentDispatcher;
                var released = false;
                ShellMenuStaWorker.PostCleanup(owner, () => released = true);
                Assert.IsTrue(released);
                ShellMenuStaWorker.PostCleanup(owner, () => throw new InvalidOperationException("cleanup failure"));
                owner.InvokeShutdown();
            }
            catch (Exception ex) { failure = ex; }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(5)));
        Assert.IsNull(failure);
    }
}
