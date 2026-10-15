namespace Lertaro.Core.Hook.InlineSearch;

// Gesture state only; native input, timer ownership and process policy stay in the mouse hook.
internal sealed class QuickPanelMouseGestureDetector
{
    private bool _pendingHold, _suppressRelease;
    private int _holdX, _holdY, _holdDelay;
    private uint _holdTime;
    private double _holdTolerance;
    internal bool HasPendingHold => _pendingHold;

    internal void BeginHold(int x, int y, uint time, int delay, double tolerance)
    {
        _holdX = x; _holdY = y; _holdTime = time; _holdDelay = Math.Clamp(delay, 300, 2000);
        _holdTolerance = tolerance; _pendingHold = true; _suppressRelease = false; ResetCircle();
    }
    internal void Move(int x, int y)
    {
        if (!_pendingHold) return;
        if (Math.Abs((double)x - _holdX) > _holdTolerance || Math.Abs((double)y - _holdY) > _holdTolerance) _pendingHold = false;
    }
    internal bool TryFireHold(uint time)
    {
        var elapsed = unchecked(time - _holdTime);
        if (!_pendingHold || elapsed < _holdDelay || elapsed > int.MaxValue) return false;
        _pendingHold = false; _suppressRelease = true; return true;
    }
    internal bool Release()
    {
        var suppress = _suppressRelease; _pendingHold = _suppressRelease = false; return suppress;
    }
    internal void CancelPendingHold() => _pendingHold = false;

    private readonly record struct Sample(int X, int Y, uint Time);
    // ponytail: the last 128 sampled points, scanned once per sample. No unbounded path or idle timer.
    // Increase the buffer and time window together if longer gestures are added.
    private readonly Sample[] _points = new Sample[128];
    private int _head, _count;
    private uint _lastCircleTime;
    private bool _hasTriggeredCircle;
    internal void ResetCircle() => _head = _count = 0;

    internal bool AddCirclePoint(int x, int y, uint time, double scale = 1)
    {
        if (_hasTriggeredCircle && unchecked(time - _lastCircleTime) < 1200) return false;
        var newest = new Sample(x, y, time);
        if (_count > 0)
        {
            var last = _points[(_head + 127) % 128];
            if (double.Hypot(x - (double)last.X, y - (double)last.Y) < 3 * scale) return false;
        }
        _points[_head] = newest; _head = (_head + 1) % 128; _count = Math.Min(128, _count + 1);
        double minX = x, maxX = x, minY = y, maxY = y, turn = 0, absoluteTurn = 0, length = 0, lastDx = 0, lastDy = 0;
        var previous = newest;
        for (var age = 1; age < _count; age++)
        {
            var older = _points[(_head - 1 - age + 256) % 128];
            if (unchecked(time - older.Time) > 2200) break;
            var dx = previous.X - (double)older.X; var dy = previous.Y - (double)older.Y;
            length += double.Hypot(dx, dy);
            if (age > 1)
            {
                var angle = Math.Atan2(dx * lastDy - dy * lastDx, dx * lastDx + dy * lastDy);
                turn += angle; absoluteTurn += Math.Abs(angle);
            }
            lastDx = dx; lastDy = dy; previous = older;
            minX = Math.Min(minX, older.X); maxX = Math.Max(maxX, older.X);
            minY = Math.Min(minY, older.Y); maxY = Math.Max(maxY, older.Y);
            var width = maxX - minX; var height = maxY - minY;
            if (Math.Max(width, height) > 500 * scale) break;
            if (Math.Abs(turn) < Math.Tau * 1.8 || absoluteTurn == 0 || Math.Abs(turn) / absoluteTurn < .8
                || Math.Min(width, height) < 48 * scale || width / height is < .5 or > 2
                || length < Math.Max(width, height) * 3
                || double.Hypot(older.X - (double)x, older.Y - (double)y) > Math.Min(width, height) * .35) continue;
            _lastCircleTime = time; _hasTriggeredCircle = true; ResetCircle(); return true;
        }
        return false;
    }
}
