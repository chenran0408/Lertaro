using Lertaro.Core;

namespace Lertaro.App.Services;

/// <summary>
/// Runs <see cref="IdleWorkingSetTrimGate"/>'s decision on a timer. See there for why the trim waits.
/// </summary>
internal static class IdleWorkingSetTrimmer
{
    // Keep pages resident between ordinary searches and summons, not just sub-second bursts.
    // A three-second pause is still interactive use; evicting then makes the next summon cold.
    // Match SearchEngine's quiet period while still returning memory after a longer idle interval.
    private const long IdleMs = 60_000;

    // How often the gate is polled once armed. Must stay below IdleMs or the poll interval, not the
    // threshold, becomes the real delay -- at the previous 5s poll a 3s threshold could not fire until
    // 5s. Each tick is a lock and a comparison, so polling this often costs nothing.
    private const int PollMs = 1_000;

    private static readonly IdleWorkingSetTrimGate Gate = new(IdleMs);
    private static System.Threading.Timer? _timer;
    private static readonly object StartLock = new();

    public static void WindowHidden()
    {
        Gate.WindowHidden(Environment.TickCount64);
        EnsureTimer();
    }

    /// <summary>Called at the very start of a summon, before anything touches the window.</summary>
    public static void WindowShowing() => Gate.WindowShowing();

    /// <summary>Requests a deferred trim after a plugin finishes a burst of background work.</summary>
    public static void RequestTrim()
    {
        Gate.RequestTrim(Environment.TickCount64);
        EnsureTimer();
    }

    /// <summary>Marks a CLI request routed through the App's search pipe as active.</summary>
    public static void BackgroundSearchStarted()
    {
        Gate.BackgroundSearchStarted();
        EnsureTimer();
    }

    /// <summary>Schedules a trim after the final CLI search completes and the App remains idle.</summary>
    public static void BackgroundSearchFinished() => Gate.BackgroundSearchFinished(Environment.TickCount64);

    private static void EnsureTimer()
    {
        lock (StartLock)
            _timer ??= new System.Threading.Timer(_ => Tick(), null, IdleMs, PollMs);
    }

    private static void Tick()
    {
        if (!Gate.ShouldTrim(Environment.TickCount64))
            return;

        try
        {
            Win32Api.TrimWorkingSet();
        }
        catch
        {
            // Best effort -- this is a courtesy to Task Manager, never something to fail over.
        }
    }
}
