namespace Lertaro.App.ViewModels.Search.Dispatch;

// Runs one asynchronous item at a time and retains only the newest item queued behind it.
internal sealed class LatestAsyncWork<T>(Func<T, Task> process)
{
    private readonly object _gate = new();
    private T? _pending;
    private bool _hasPending;
    private bool _running;

    public void Queue(T item)
    {
        var start = false;
        lock (_gate)
        {
            _pending = item;
            _hasPending = true;
            if (!_running)
            {
                _running = true;
                start = true;
            }
        }
        if (start)
            _ = DrainAsync();
    }

    public void ClearPending()
    {
        lock (_gate)
        {
            _pending = default;
            _hasPending = false;
        }
    }

    private async Task DrainAsync()
    {
        while (true)
        {
            T item;
            lock (_gate)
            {
                if (!_hasPending)
                {
                    _running = false;
                    return;
                }
                item = _pending!;
                _pending = default;
                _hasPending = false;
            }
            try
            {
                await process(item);
            }
            catch
            {
                var restart = false;
                lock (_gate)
                {
                    if (_hasPending)
                        restart = true;
                    else
                        _running = false;
                }
                if (restart)
                    _ = DrainAsync();
                throw;
            }
        }
    }
}
