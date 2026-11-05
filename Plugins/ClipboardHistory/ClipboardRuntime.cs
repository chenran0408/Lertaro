using System.Collections.Concurrent;
using System.IO;
using System.Windows;
using Lertaro.PluginSdk;
using Lertaro.PluginSdk.Services;
using Lertaro.Plugins.ClipboardHistory.Capture;

namespace Lertaro.Plugins.ClipboardHistory;

internal sealed class ClipboardRuntime : IDisposable
{
    private static readonly Lazy<ClipboardRuntime> Shared = new(() => new());
    internal static ClipboardRuntime Instance => Shared.Value;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _writeGate = new();
    private Task _pendingWrites = Task.CompletedTask;
    private readonly ConcurrentDictionary<long, SourceWindow> _sources = new();
    private ClipboardEntry[] _entries = [];
    private ClipboardStore? _store;
    private string? _imagesDirectory;
    private ClipboardMonitorService? _monitor;
    private PasteTargetTracker? _targets;
    private Task _ready = Task.CompletedTask;
    private bool _started;
    private volatile bool _stopping;
    private static long _pausedUntilMs;

    /// <summary>One-click capture pause from the panel: the monitor's options read this, so a
    /// paused recorder behaves exactly like a disabled one except no setting changes.</summary>
    internal static bool IsCapturePaused() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() < Volatile.Read(ref _pausedUntilMs);

    internal static void PauseCaptureFor(TimeSpan duration) =>
        Volatile.Write(ref _pausedUntilMs,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + (long)duration.TotalMilliseconds);

    internal IReadOnlyList<ClipboardEntry> Entries => Volatile.Read(ref _entries);

    internal void Start()
    {
        var app = Application.Current;
        if (app == null) return;
        if (!app.Dispatcher.CheckAccess())
        {
            app.Dispatcher.BeginInvoke(new Action(Start));
            return;
        }
        if (_started) return;
        _started = true;
        var directory = UserDataService.GetUserDataDirectory();
        if (string.IsNullOrEmpty(directory))
        {
            Logger.Log("[ClipboardHistory] User data directory is unavailable.", LogLevel.Error);
            return;
        }
        var root = Path.Combine(directory, "ClipboardHistory");
        _imagesDirectory = Path.Combine(root, "images");
        _store = new ClipboardStore(Path.Combine(root, "clipboard-history.db"), _imagesDirectory);
        _monitor = new ClipboardMonitorService(Captured, () => new ClipboardCaptureOptions(
            ClipboardSettings.Enabled && !_stopping && !IsCapturePaused(),
            ClipboardSettings.MaxTextLength, ClipboardSettings.ExcludedProcesses));
        try { _targets = new PasteTargetTracker(); }
        catch (Exception ex) { LogFailure(ex); }
        PluginSettingsService.SettingChanged += SettingChanged;
        app.Exit += OnAppExit;
        _ready = InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        try
        {
            Directory.CreateDirectory(_imagesDirectory!);
            await _store!.InitializeAsync().ConfigureAwait(false);
            await _store.TrimAsync(ClipboardSettings.MaxEntries, ClipboardSettings.RetentionDays).ConfigureAwait(false);
            Publish(await _store.GetEntriesAsync().ConfigureAwait(false));
            if (!_stopping && ClipboardSettings.Enabled) _monitor!.Start();
        }
        catch (Exception ex) { LogFailure(ex); PasteBackHelper.Notify("ClipboardHistory_StorageFailed"); }
    }

    private void SettingChanged(string pluginId, string key)
    {
        if (pluginId != ClipboardSettings.PluginId || _stopping) return;
        if (key == "Enabled")
        {
            if (ClipboardSettings.Enabled) _monitor?.Start();
            else _monitor?.Stop();
        }
        if (key is "MaxEntries" or "RetentionDays") _ = MutateAsync(async store =>
        {
            await store.TrimAsync(ClipboardSettings.MaxEntries, ClipboardSettings.RetentionDays).ConfigureAwait(false);
            Publish(await store.GetEntriesAsync().ConfigureAwait(false));
        });
    }

    private void Captured(ClipboardCapture capture)
    {
        if (_stopping || !ClipboardSettings.Enabled) return;
        _ = MutateAsync(async store =>
        {
            if (!ClipboardSettings.Enabled) return;
            var draft = await ClipboardImageIngest.BuildDraftAsync(capture, _imagesDirectory!).ConfigureAwait(false);
            if (draft == null) return;
            var entry = await store.AddAsync(draft, capture.SourceProcess, capture.SourceWindowTitle,
                ClipboardSettings.MaxEntries, ClipboardSettings.MaxTextLength).ConfigureAwait(false);
            if (entry == null) return;
            _sources[entry.Id] = new SourceWindow(capture.SourceWindow, capture.SourceProcessId, capture.SourceProcessStartedAtTicks);
            Publish(ClipEntries(Entries.Where(item => item.Id != entry.Id).Append(entry), ClipboardSettings.MaxEntries));
        });
    }


    // Mirror the store's unpinned cap without rereading every saved text on each clipboard update.
    internal static ClipboardEntry[] ClipEntries(IEnumerable<ClipboardEntry> entries, int maxEntries)
    {
        var ordered = entries.OrderByDescending(item => item.Pinned).ThenByDescending(item => item.LastSeenAtMs)
            .ThenByDescending(item => item.Id).ToArray();
        return ordered.Where(item => item.Pinned).Concat(ordered.Where(item => !item.Pinned).Take(maxEntries)).ToArray();
    }

    internal async Task<IReadOnlyList<ClipboardEntry>> GetEntriesAsync(CancellationToken token)
    {
        await _ready.WaitAsync(token).ConfigureAwait(false);
        if (_store == null || _stopping) return [];
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var entries = await _store.GetEntriesAsync(token).ConfigureAwait(false);
            Publish(entries);
            return entries;
        }
        finally { _gate.Release(); }
    }

    /// <summary>Filters the published snapshot in memory with the host's fuzzy matcher. The old
    /// path blocked on SQLite FTS from the sync query API, which stalled the search box; the
    /// snapshot is already in memory, capped by MaxEntries, and never touches disk.</summary>
    internal IReadOnlyList<ClipboardEntry> Search(string term)
    {
        var entries = Entries;
        if (entries.Count == 0 || !_ready.IsCompletedSuccessfully) return [];
        return entries.Where(entry => ClipboardSearchSupport.Matches(entry, term, FuzzyMatchService.IsMatch)).ToArray();
    }

    private void Publish(IReadOnlyList<ClipboardEntry> entries)
    {
        Volatile.Write(ref _entries, entries.ToArray());
        var ids = entries.Select(entry => entry.Id).ToHashSet();
        foreach (var id in _sources.Keys)
            if (!ids.Contains(id)) _sources.TryRemove(id, out _);
        SearchRefreshService.RefreshIfMatches(query => TriggerWord.TryMatch(query, ClipboardSettings.TriggerKeyword, out _));
    }

    internal bool Paste(long id) =>
        PasteEntries(Entries.Where(entry => entry.Id == id).Take(1).ToArray());

    /// <summary>Pastes one entry, or a merged payload built from several selected entries (the
    /// panel's multi-select). Returns false when there is nothing pasteable or the target refuses.
    /// Accepted pastes count toward the entry's paste tally -- the frequency sort means "used",
    /// and a capture-only tally never told the truth about that.</summary>
    internal bool PasteEntries(IReadOnlyList<ClipboardEntry> entries, bool keepFormat = true)
    {
        var merged = entries.Count == 1 ? entries[0] : ClipboardMergeSupport.MergeForPaste(entries);
        var target = _targets?.Target ?? PasteTargetTracker.ReadExternalForeground();
        try
        {
            if (merged == null || _monitor == null || !PasteBackHelper.CanPaste(target)) return false;
            CountPastes(entries);
            _ = PasteAsync(merged, target!, keepFormat);
            return true;
        }
        catch (Exception ex) { LogFailure(ex); PasteBackHelper.Notify("ClipboardHistory_PasteFailed"); return false; }
    }

    private void CountPastes(IReadOnlyList<ClipboardEntry> entries)
    {
        if (entries.Count == 0) return;
        _ = MutateAsync(async store =>
        {
            foreach (var entry in entries)
                await store.IncrementPasteCountAsync(entry.Id).ConfigureAwait(false);
            var ids = entries.Select(entry => entry.Id).ToHashSet();
            Publish(Entries.Select(item => ids.Contains(item.Id)
                ? item with { PasteCount = item.PasteCount + 1 }
                : item).ToArray());
        });
    }

    /// <summary>Writes an arbitrary text to the clipboard (path-format copies for file entries).</summary>
    internal async Task CopyTextAsync(string text)
    {
        if (_monitor == null) return;
        try { await _monitor.WriteTextAsync(text).ConfigureAwait(false); }
        catch (Exception ex) { LogFailure(ex); PasteBackHelper.Notify("ClipboardHistory_PasteFailed"); }
    }

    internal async Task CopyAsync(long id)
    {
        var entry = Entries.FirstOrDefault(entry => entry.Id == id);
        if (entry == null || _monitor == null) return;
        try
        {
            await WriteEntryAsync(entry, keepFormat: true).ConfigureAwait(false);
        }
        catch (Exception ex) { LogFailure(ex); PasteBackHelper.Notify("ClipboardHistory_PasteFailed"); }
    }

    private string ImageFilePath(ClipboardEntry entry) => Path.Combine(_imagesDirectory!, entry.ImagePath);

    /// <summary>Pastes into the tracked source window and reports whether the panel should close.
    /// The panel closes after pasting so the injected Ctrl+V lands in the window the user was
    /// typing in -- except for pinned entries: a pin is the user saying "keep this handy", so the
    /// panel stays up and the same entry can be pasted repeatedly (Win+V, click, Win+V, click)
    /// without resummoning it every time.</summary>
    internal bool PasteAndClose(long id)
    {
        var pasted = Paste(id);
        if (!pasted) return false;
        // Read the pin state after the paste: Publish() may have run during it, and the decision
        // belongs to the entry as it stands now, not as it stood when the row was clicked.
        return !Entries.FirstOrDefault(entry => entry.Id == id)?.Pinned ?? true;
    }

    private async Task PasteAsync(ClipboardEntry entry, SourceWindow target, bool keepFormat)
    {
        try
        {
            await PasteBackHelper.PasteAsync(target, () => WriteEntryAsync(entry, keepFormat)).ConfigureAwait(false);
        }
        catch (Exception ex) { LogFailure(ex); PasteBackHelper.Notify("ClipboardHistory_PasteFailed"); }
    }

    /// <summary>Writes one entry back to the clipboard. keepFormat sends the captured HTML flavor
    /// along with the text so rich targets keep their formatting; plain text strips it.</summary>
    private Task WriteEntryAsync(ClipboardEntry entry, bool keepFormat) => entry.Kind switch
    {
        ClipboardEntryKind.Image => _monitor!.WriteImageAsync(ImageFilePath(entry)),
        ClipboardEntryKind.FilePaths => _monitor!.WriteFilesAsync(entry.PathList),
        _ => keepFormat && entry.Html.Length > 0
            ? _monitor!.WriteTextWithHtmlAsync(entry.Content, entry.Html)
            : _monitor!.WriteTextAsync(entry.Content),
    };

    internal void Copy(long id) => _ = CopyAsync(id);

    internal void TogglePin(long id) => _ = MutateAsync(async store =>
    {
        if (Entries.FirstOrDefault(entry => entry.Id == id) is not { } entry) return;
        await store.SetPinnedAsync(id, !entry.Pinned).ConfigureAwait(false);
        await store.TrimAsync(ClipboardSettings.MaxEntries, ClipboardSettings.RetentionDays).ConfigureAwait(false);
        Publish(await store.GetEntriesAsync().ConfigureAwait(false));
    });

    /// <summary>Saves the user's note on an entry (CopyQ-style annotation). A targeted store update
    /// plus a local snapshot compose -- no full re-read, so the panel's open note editor keeps its
    /// focus while typing.</summary>
    internal void SetNote(long id, string note) => _ = MutateAsync(async store =>
    {
        await store.SetNoteAsync(id, note).ConfigureAwait(false);
        Publish(Entries.Select(entry => entry.Id == id ? entry with { Note = note } : entry).ToArray());
    });

    internal void Delete(long id) => _ = MutateAsync(async store =>
    {
        await store.DeleteAsync(id).ConfigureAwait(false);
        Publish(Entries.Where(entry => entry.Id != id).ToArray());
    });

    internal void ClearHistory() => _ = MutateAsync(async store =>
    {
        await store.ClearAsync().ConfigureAwait(false);
        Publish([]);
    });

    internal bool CanOpenSource(long id) => _sources.TryGetValue(id, out var source) && source.IsAlive;
    internal void OpenSource(long id)
    {
        if (_sources.TryGetValue(id, out var source) && source.IsAlive) SourceWindowFocus.Focus(source.Handle);
    }

    private Task MutateAsync(Func<ClipboardStore, Task> action)
    {
        lock (_writeGate)
        {
            if (_stopping) return Task.CompletedTask;
            return _pendingWrites = RunMutationAsync(_pendingWrites, action);
        }
    }

    private async Task RunMutationAsync(Task previous, Func<ClipboardStore, Task> action)
    {
        try
        {
            await previous.ConfigureAwait(false);
            await _ready.ConfigureAwait(false);
            if (_store == null) return;
            await _gate.WaitAsync().ConfigureAwait(false);
            try { await action(_store).ConfigureAwait(false); }
            finally { _gate.Release(); }
        }
        catch (Exception ex) { LogFailure(ex); PasteBackHelper.Notify("ClipboardHistory_StorageFailed"); }
    }

    internal async Task StopAsync()
    {
        Task pending;
        lock (_writeGate) { _stopping = true; pending = _pendingWrites; }
        _monitor?.Stop();
        await _ready.ConfigureAwait(false);
        await pending.ConfigureAwait(false);
    }

    private void OnAppExit(object sender, ExitEventArgs e) => Dispose();

    public void Dispose()
    {
        StopAsync().GetAwaiter().GetResult();
        PluginSettingsService.SettingChanged -= SettingChanged;
        if (Application.Current is { } app) app.Exit -= OnAppExit;
        _monitor?.Dispose();
        _targets?.Dispose();
    }

    // Exception messages can contain SQL values or paths. Keep clipboard data out of the host log.
    private static void LogFailure(Exception ex) => Logger.Log($"[ClipboardHistory] {ex.GetType().Name} (0x{ex.HResult:X8}).", LogLevel.Error);
}
