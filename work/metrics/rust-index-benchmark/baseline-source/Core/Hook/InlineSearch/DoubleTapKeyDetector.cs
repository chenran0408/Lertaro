namespace Lertaro.Core.Hook.InlineSearch;

/// <summary>Recognizes two press-release cycles of the same key within one time window.</summary>
internal sealed class DoubleTapKeyDetector(uint intervalMilliseconds = 320)
{
    private bool _armed, _keyDown;
    private int _key;
    private uint _firstTapTime;

    internal bool OnKeyDown(int key, uint time, bool repeated = false)
    {
        if (repeated || (_keyDown && key == _key)) return false;
        if (_keyDown && key != _key) _keyDown = false;
        _keyDown = true;
        if (_armed && key == _key && unchecked(time - _firstTapTime) <= intervalMilliseconds)
        {
            _armed = false;
            return true;
        }

        _key = key;
        _firstTapTime = time;
        _armed = true;
        return false;
    }

    internal void OnKeyUp(int key)
    {
        if (key == _key) _keyDown = false;
    }

    internal void ResetOnOtherKey(int key)
    {
        if (key == _key) return;
        _armed = false;
        _keyDown = false;
    }

    internal void Reset()
    {
        _armed = false;
        _keyDown = false;
        _key = 0;
        _firstTapTime = 0;
    }
}
