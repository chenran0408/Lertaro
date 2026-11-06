using System.Runtime.InteropServices;

namespace Lertaro.Core.Hook.InlineSearch;

// Keeps only the short prefix typed while the search box is acquiring focus. Replay is an explicit,
// generation-checked handshake; buffered input must never be delivered to another foreground app.
internal sealed class InlineInputHandoff
{
    internal readonly record struct Key(ushort VirtualKey, ushort ScanCode, bool Extended, bool Shift);
    private readonly object _gate = new();
    private readonly List<Key> _keys = [];
    private int _generation;
    private nint _owner;
    private nint _target;
    private long _started;
    internal bool IsPending { get { lock (_gate) return _owner != 0; } }

    internal int Begin(nint owner, Key key, long now)
    {
        lock (_gate)
        {
            _keys.Clear();
            _keys.Add(key);
            _owner = owner;
            _target = 0;
            _started = now;
            _generation = _generation == int.MaxValue ? 1 : _generation + 1;
            return _generation;
        }
    }

    internal void SetTarget(int generation, nint target)
    {
        lock (_gate) { if (_owner != 0 && generation == _generation) _target = target; }
    }

    internal bool TryAppend(nint foreground, Key key, long now)
    {
        lock (_gate)
        {
            if (_owner == 0) return false;
            if (now - _started > 2000 || (foreground != _owner && foreground != _target) || _keys.Count >= 64)
            {
                Cancel();
                return false;
            }
            _keys.Add(key);
            return true;
        }
    }

    internal Key[] Take(int generation, nint target, bool targetIsFocusedApp, long now)
    {
        lock (_gate)
        {
            if (generation != _generation) return [];
            var result = _owner != 0 && target != 0 && target == _target && targetIsFocusedApp && now - _started <= 2000 ? _keys.ToArray() : [];
            Cancel();
            return result;
        }
    }

    internal void Cancel() { lock (_gate) { _owner = 0; _target = 0; _keys.Clear(); } }

    internal static bool IsPrintableKey(int key) => key is >= 0x41 and <= 0x5A or >= 0x30 and <= 0x39
        or >= 0x60 and <= 0x6F or >= 0xBA and <= 0xC0 or >= 0xDB and <= 0xDF or 0xE2 or 0xE5;

    internal static void Replay(Key[] keys)
    {
        if (keys.Length == 0) return;
        var originalShift = (KeyboardNativeMethods.GetAsyncKeyState(0x10) & 0x8000) != 0;
        var shift = originalShift;
        var inputs = new List<Input>();
        foreach (var key in keys)
        {
            if (shift != key.Shift) { inputs.Add(Create(0x10, 0, key.Shift ? 0u : 2u)); shift = key.Shift; }
            // Scan codes preserve the IME's physical keyboard input, unlike Unicode text injection.
            var flags = (key.ScanCode != 0 ? 8u : 0u) | (key.Extended ? 1u : 0u);
            var vk = key.ScanCode != 0 ? (ushort)0 : key.VirtualKey;
            if (vk == 0xE5) continue; // A PROCESSKEY without its physical scan code cannot be replayed.
            inputs.Add(Create(vk, key.ScanCode, flags));
            inputs.Add(Create(vk, key.ScanCode, flags | 2));
        }
        if (shift != originalShift) inputs.Add(Create(0x10, 0, originalShift ? 0u : 2u));
        if (inputs.Count > 0 && SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf<Input>()) != (uint)inputs.Count)
            Logger.Log($"[InlineSearch] Input handoff was blocked or incomplete (error {Marshal.GetLastWin32Error()}).", LogLevel.Warn);
    }

    private static Input Create(ushort vk, ushort scan, uint flags) => new()
    { Type = 1, Data = new InputData { Keyboard = new KeyboardInput { VirtualKey = vk, ScanCode = scan, Flags = flags } } };
    [StructLayout(LayoutKind.Sequential)] private struct Input { public uint Type; public InputData Data; }
    [StructLayout(LayoutKind.Explicit)] private struct InputData
    {
        [FieldOffset(0)] public KeyboardInput Keyboard;
        [FieldOffset(0)] public MouseInput Mouse;
    }
    [StructLayout(LayoutKind.Sequential)] private struct KeyboardInput
    { public ushort VirtualKey, ScanCode; public uint Flags, Time; public nuint ExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] private struct MouseInput
    { public int X, Y; public uint Data, Flags, Time; public nuint ExtraInfo; }
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, Input[] inputs, int size);
}
