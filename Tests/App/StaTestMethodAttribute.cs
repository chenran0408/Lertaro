using System.Runtime.CompilerServices;
using System.Collections.Concurrent;

namespace Lertaro.App.Tests;

// WPF FrameworkElement construction (Grid, ContextMenu, MenuItem, ...) throws unless the calling thread
// is STA -- MSTest's own test threads are MTA by default. All STA tests share one dispatcher so WPF's
// process-wide Application and resource dictionaries keep a single thread owner.
public class StaTestMethodAttribute(
    [CallerFilePath] string callerFilePath = "",
    [CallerLineNumber] int callerLineNumber = 0)
    : TestMethodAttribute(callerFilePath, callerLineNumber)
{
    public override async Task<TestResult[]> ExecuteAsync(ITestMethod testMethod)
    {
        if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA)
            return await base.ExecuteAsync(testMethod);

        return await Runner.RunAsync(() => base.ExecuteAsync(testMethod));
    }

    private static readonly StaTestRunner Runner = new();
}

// MSTest also exposes an STA attribute with a different casing. Keep both spellings on the same
// dispatcher so tests that construct WPF application resources do not cross dispatcher boundaries.
public sealed class STATestMethodAttribute(
    [CallerFilePath] string callerFilePath = "",
    [CallerLineNumber] int callerLineNumber = 0)
    : StaTestMethodAttribute(callerFilePath, callerLineNumber)
{
}

internal sealed class StaTestRunner
{
    private readonly BlockingCollection<(Func<Task<TestResult[]>> Work, TaskCompletionSource<TestResult[]> Completion)> _queue = new();

    public StaTestRunner()
    {
        var thread = new Thread(Run) { IsBackground = true, Name = "Lertaro test STA" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }

    public Task<TestResult[]> RunAsync(Func<Task<TestResult[]>> work)
    {
        var completion = new TaskCompletionSource<TestResult[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        _queue.Add((work, completion));
        return completion.Task;
    }

    private void Run()
    {
        foreach (var (work, completion) in _queue.GetConsumingEnumerable())
        {
            try { completion.SetResult(work().GetAwaiter().GetResult()); }
            catch (Exception ex) { completion.SetException(ex); }
        }
    }
}
