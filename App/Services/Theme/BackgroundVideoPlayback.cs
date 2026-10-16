using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace Lertaro.App.Services.Theme;

// One decoder per UI dispatcher, shared by the window backgrounds and the settings preview.
internal sealed class BackgroundVideoPlayback
{
    private static readonly ConditionalWeakTable<Dispatcher, BackgroundVideoPlayback> Instances = new();
    internal static BackgroundVideoPlayback Current => Instances.GetValue(Dispatcher.CurrentDispatcher, _ => new());
    private readonly Dictionary<object, bool> _consumers = new();
    private MediaPlayer? _player;
    private string? _path;
    internal DrawingGroup Drawing { get; } = new();
    internal bool IsPlaying { get; private set; }
    internal string? FailedPath { get; private set; }
    internal MediaPlayer? Player => _player;
    internal event Action<string>? Failed;

    internal static bool IsVideoPath(string path) => string.Equals(Path.GetExtension(path), ".mp4", StringComparison.OrdinalIgnoreCase);
    internal static Task<bool> IsReadableAsync(string path) => Task.Run(() =>
    {
        try { return Path.IsPathFullyQualified(path) && IsVideoPath(path) && new FileInfo(path) is { Exists: true, Length: > 0 }; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { return false; }
    });

    internal void Acquire(object consumer, string path, bool active)
    {
        if (!string.Equals(_path, path, StringComparison.OrdinalIgnoreCase))
        {
            Close();
            _consumers.Clear();
            _path = path;
            FailedPath = null;
        }
        _consumers[consumer] = active;
        UpdatePlayback();
    }

    internal void SetActive(object consumer, bool active)
    {
        if (!_consumers.ContainsKey(consumer)) return;
        _consumers[consumer] = active;
        UpdatePlayback();
    }

    internal void Release(object consumer)
    {
        _consumers.Remove(consumer);
        if (_consumers.Count == 0)
        {
            Close();
            _path = null;
            FailedPath = null;
        }
        else UpdatePlayback();
    }

    private void UpdatePlayback()
    {
        try
        {
            var play = _consumers.Values.Any(active => active) && FailedPath == null;
            if (play && _player == null && _path != null)
            {
                var player = _player = new MediaPlayer { Volume = 0, IsMuted = true };
                player.MediaOpened += OnOpened;
                player.MediaEnded += OnEnded;
                player.MediaFailed += OnFailed;
                player.Open(new Uri(_path, UriKind.Absolute));
            }
            if (_player == null || play == IsPlaying) return;
            if (play) _player.Play();
            else _player.Pause();
            IsPlaying = play;
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or IOException or System.Runtime.InteropServices.COMException)
        { Fail(); }
    }

    private void OnOpened(object? sender, EventArgs args)
    {
        if (sender is not MediaPlayer player || player != _player) return;
        if (!player.HasVideo || player.NaturalVideoWidth <= 0 || player.NaturalVideoHeight <= 0) { Fail(); return; }
        Drawing.Children.Clear();
        Drawing.Children.Add(new VideoDrawing { Player = player, Rect = new Rect(0, 0, player.NaturalVideoWidth, player.NaturalVideoHeight) });
    }

    private void OnEnded(object? sender, EventArgs args)
    {
        if (sender is not MediaPlayer player || player != _player || !IsPlaying) return;
        try { player.Position = TimeSpan.Zero; player.Play(); }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException) { Fail(); }
    }

    private void OnFailed(object? sender, ExceptionEventArgs args)
    {
        if (sender is MediaPlayer player && player == _player) Fail();
    }

    private void Fail()
    {
        var path = _path;
        Close();
        FailedPath = path;
        if (path != null) Failed?.Invoke(path);
    }

    private void Close()
    {
        IsPlaying = false;
        Drawing.Children.Clear();
        var player = _player;
        _player = null;
        if (player == null) return;
        player.MediaOpened -= OnOpened;
        player.MediaEnded -= OnEnded;
        player.MediaFailed -= OnFailed;
        player.Close();
    }
}
