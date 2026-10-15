using System.Runtime.InteropServices;
namespace Lertaro.App.Services.Theme;

// Owns native resources on the capture worker; only a bounded window region is copied.
internal sealed class GlassBackdropCapture : IDisposable
{
    private nint _screen, _memory, _bitmap, _previous, _pixels;
    private int _width, _height;
    private byte[] _buffer = [];
    internal byte[] Read(int left, int top, int width, int height)
    {
        if (width <= 0 || height <= 0 || (long)width * height > 5_000_000) throw new ArgumentOutOfRangeException(nameof(width));
        if (_screen == 0) { _screen = GetDC(0); _memory = CreateCompatibleDC(_screen); }
        if (_screen == 0 || _memory == 0) throw new InvalidOperationException("Desktop capture DC unavailable.");
        if (_width != width || _height != height)
        {
            ReleaseBitmap();
            var info = new BitmapInfo { Size = 40, Width = width, Height = -height, Planes = 1, Bits = 32 };
            _bitmap = CreateDIBSection(_screen, ref info, 0, out _pixels, 0, 0);
            if (_bitmap == 0 || _pixels == 0) throw new InvalidOperationException("Desktop capture bitmap unavailable.");
            _previous = SelectObject(_memory, _bitmap);
            _width = width; _height = height; _buffer = new byte[checked(width * height * 4)];
        }
        if (!BitBlt(_memory, 0, 0, width, height, _screen, left, top, 0x00CC0020 | 0x40000000))
            throw new InvalidOperationException("Desktop capture failed.");
        GdiFlush(); Marshal.Copy(_pixels, _buffer, 0, _buffer.Length); return _buffer;
    }
    private void ReleaseBitmap()
    {
        if (_previous != 0 && _memory != 0) SelectObject(_memory, _previous);
        if (_bitmap != 0) DeleteObject(_bitmap);
        _bitmap = _previous = _pixels = 0;
    }
    public void Dispose()
    {
        ReleaseBitmap();
        if (_memory != 0) DeleteDC(_memory);
        if (_screen != 0) ReleaseDC(0, _screen);
        _memory = _screen = 0;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfo
    { internal uint Size; internal int Width, Height; internal ushort Planes, Bits; internal uint Compression, ImageSize; internal int XPels, YPels; internal uint Used, Important; internal uint Color; }
    [DllImport("user32.dll")] private static extern nint GetDC(nint window);
    [DllImport("user32.dll")] private static extern int ReleaseDC(nint window, nint dc);
    [DllImport("gdi32.dll")] private static extern nint CreateCompatibleDC(nint dc);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(nint dc);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(nint obj);
    [DllImport("gdi32.dll")] private static extern nint SelectObject(nint dc, nint obj);
    [DllImport("gdi32.dll")] private static extern nint CreateDIBSection(nint dc, ref BitmapInfo info, uint usage, out nint bits, nint section, uint offset);
    [DllImport("gdi32.dll")] private static extern bool BitBlt(nint dest, int x, int y, int width, int height, nint source, int sourceX, int sourceY, uint operation);
    [DllImport("gdi32.dll")] private static extern bool GdiFlush();
}
