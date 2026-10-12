namespace Lertaro.Plugins.QuickLookBridge;

// Separates cached availability from pipe I/O so a menu never waits for another application.
internal sealed class QuickLookAvailability(Func<bool> probe, Action onUnavailable)
{
    private long _lastRefreshStartedTicks;
    private int _refreshInFlight;
    private int _hasCheckedOnce;
    private volatile bool _cachedAvailable;

    internal bool Read(bool waitForInitial)
    {
        if (waitForInitial && Interlocked.CompareExchange(ref _hasCheckedOnce, 1, 0) == 0)
        {
            SetAvailable(probe());
            Interlocked.Exchange(ref _lastRefreshStartedTicks, DateTime.UtcNow.Ticks);
            return _cachedAvailable;
        }
        var now = DateTime.UtcNow.Ticks;
        if (now - Interlocked.Read(ref _lastRefreshStartedTicks) >= TimeSpan.TicksPerSecond
            && Interlocked.CompareExchange(ref _refreshInFlight, 1, 0) == 0)
        {
            Interlocked.Exchange(ref _lastRefreshStartedTicks, now);
            Task.Run(() =>
            {
                try
                {
                    var available = probe();
                    Interlocked.Exchange(ref _hasCheckedOnce, 1);
                    SetAvailable(available);
                }
                finally
                {
                    Interlocked.Exchange(ref _refreshInFlight, 0);
                }
            });
        }
        return _cachedAvailable;
    }

    private void SetAvailable(bool available)
    {
        if (_cachedAvailable && !available) onUnavailable();
        _cachedAvailable = available;
    }
}
