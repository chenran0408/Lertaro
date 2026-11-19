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
    private readonly SemaphoreSlim _clipboardGate = new(1, 1);
    private readonly object _writeGate = new();
    private Task _pendingWrites = Task.CompletedTask;
    private readonly ConcurrentDictionary<long, SourceWindow> _sources = new();
    private ClipboardEntry[] _entries = [];
    private ClipboardStore? _store;
    private string? _root;
    private string? _imagesDirectory;
    private string? _databasePath;
    private ClipboardMonitorService? _monitor;
    private PasteTargetTracker? _targets;
    private Task _ready = Task.CompletedTask;
    private readonly CancellationTokenSource _retentionStop = new();
    private Task _retentionTask = Task.CompletedTask;
    private bool _disposed;
    private bool _started;
    private volatile bool _stopping;
    private volatile bool _storageFailed;
    private static long _pausedUntilMs;

    /// <summary>One-click capture pause from the panel: the monitor's options read this, so a
    /// paused recorder behaves exactly like a disabled one except no setting changes.</summary>
    internal static bool IsCapturePaused() => CapturePausedUntil() != null;

    internal static DateTimeOffset? CapturePausedUntil()
    {
        var end = Volatile.Read(ref _pausedUntilMs);
        return end > DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() ? DateTimeOffset.FromUnixTimeMilliseconds(end) : null;
    }

    internal static void PauseCaptureFor(TimeSpan duration) =>
        Volatile.Write(ref _pausedUntilMs,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + (long)duration.TotalMilliseconds);

    internal IReadOnlyList<ClipboardEntry> Entries => Volatile.Read(ref _entries);
    internal bool StorageFailed => _storageFailed;
    internal event Action? EntriesChanged;

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
            _storageFailed = true;
            Logger.Log("[ClipboardHistory] User data directory is unavailable.", LogLevel.Error);
            return;
        }
        var root = Path.Combine(directory, "ClipboardHistory");
        _root = root;
        _imagesDirectory = Path.Combine(root, "images");
        _databasePath = Path.Combine(root, "clipboard-history.db");
        _store = new ClipboardStore(_databasePath, _imagesDirectory);
        _monitor = new ClipboardMonitorService(Captured, () => new ClipboardCaptureOptions(
            ClipboardSettings.Enabled && !_stopping && !IsCapturePaused(),
            ClipboardSettings.MaxTextLength, ClipboardSettings.ExcludedProcesses));
        try { _targets = new PasteTargetTracker(); }
        catch (Exception ex) { LogFailure(ex); }
        PluginSettingsService.SettingChanged += SettingChanged;
        app.Exit += OnAppExit;
        _ready = InitializeAsync();
        _retentionTask = MaintainRetentionAsync(_retentionStop.Token);
    }

    private async Task InitializeAsync()
    {
        try
        {
            Directory.CreateDirectory(_imagesDirectory!);
            await _store!.InitializeAsync().ConfigureAwait(false);
            await _store.TrimAsync(ClipboardSettings.MaxEntries, ClipboardSettings.RetentionDays, ClipboardSettings.ImageRetentionDays).ConfigureAwait(false);
            Publish(await _store.GetEntriesAsync(includeHtml: false).ConfigureAwait(false));
            if (!_stopping && ClipboardSettings.Enabled) _monitor!.Start();
        }
        catch (Exception ex)
        {
            _storageFailed = true;
            EntriesChanged?.Invoke();
            LogFailure(ex);
            PasteBackHelper.Notify("ClipboardHistory_StorageFailed");
        }
    }

    private async Task MaintainRetentionAsync(CancellationToken token)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(30));
        try
        {
            while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false))
            {
                var retentionDays = ClipboardSettings.RetentionDays;
                var imageDays = ClipboardSettings.ImageRetentionDays;
                if (!HasExpiredEntries(Entries, DateTimeOffset.UtcNow, retentionDays, imageDays)) continue;

                await MutateAsync(async store =>
                {
                    await store.TrimAsync(ClipboardSettings.MaxEntries, ClipboardSettings.RetentionDays,
                        ClipboardSettings.ImageRetentionDays).ConfigureAwait(false);
                    Publish(await store.GetEntriesAsync(includeHtml: false).ConfigureAwait(false));
                }).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }

    internal static bool HasExpiredEntries(IReadOnlyList<ClipboardEntry> entries, DateTimeOffset now, int retentionDays, int imageRetentionDays) =>
        entries.Any(entry =>
        {
            var days = entry.Kind == ClipboardEntryKind.Image ? imageRetentionDays : retentionDays;
            return !entry.Pinned && days > 0 && entry.LastSeenAtMs < now.AddDays(-days).ToUnixTimeMilliseconds();
        });

    private void SettingChanged(string pluginId, string key)
    {
        if (pluginId != ClipboardSettings.PluginId || _stopping) return;
        if (key == "Enabled")
        {
            if (ClipboardSettings.Enabled) _monitor?.Start();
            else _monitor?.Stop();
        }
        if (key is "MaxEntries" or "RetentionDays" or "ImageRetentionDays") _ = MutateAsync(async store =>
        {
            await store.TrimAsync(ClipboardSettings.MaxEntries, ClipboardSettings.RetentionDays, ClipboardSettings.ImageRetentionDays).ConfigureAwait(false);
            Publish(await store.GetEntriesAsync(includeHtml: false).ConfigureAwait(false));
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
            Publish(ClipEntries(Entries.Where(item => item.Id != entry.Id).Append(entry with { Html = string.Empty }), ClipboardSettings.MaxEntries));
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
        return _stopping ? [] : Entries;
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
        EntriesChanged?.Invoke();
        SearchRefreshService.RefreshIfMatches(query => TriggerWord.TryMatch(query, ClipboardSettings.TriggerKeyword, out _));
    }

    internal SourceWindow? CapturePasteTarget() => PasteTargetTracker.ReadExternalForeground(_targets?.Target);

    internal bool Paste(long id) =>
        StartPaste(Entries.Where(entry => entry.Id == id).Take(1).ToArray(), true, CapturePasteTarget()) != null;

    /// <summary>Global quick paste (Ctrl+Win+N): the Nth entry of the panel's canonical order
    /// (recency or the frequency toggle, matching what the panel shows) pasted straight into the
    /// active window. Out of range is a silent no-op -- a hotkey must never surface an error.</summary>
    internal bool PasteSlot(int oneBased)
    {
        var ordered = View.ClipboardPanelRows.Sort(Entries, View.ClipboardPanelViewSettings.SortByFrequency());
        if (oneBased < 1 || oneBased > ordered.Count) return false;
        return StartPaste([ordered[oneBased - 1]], true, PasteTargetTracker.ReadExternalForeground()) != null;
    }

    internal Task<bool>? StartPaste(IReadOnlyList<ClipboardEntry> entries, bool keepFormat, SourceWindow? target)
    {
        var merged = entries.Count == 1 ? entries[0] : ClipboardMergeSupport.MergeForPaste(entries);
        try
        {
            if (_stopping || merged == null || _monitor == null || !PasteBackHelper.CanPaste(target)) return null;
            var pastedIds = entries.Where(entry => entries.Count == 1 || entry.Kind != ClipboardEntryKind.Image)
                .Select(entry => entry.Id).ToArray();
            return PasteAndCountAsync(target!, merged, keepFormat, pastedIds);
        }
        catch (Exception ex) { LogFailure(ex); PasteBackHelper.Notify("ClipboardHistory_PasteFailed"); return null; }
    }

    private async Task<bool> PasteAndCountAsync(SourceWindow target, ClipboardEntry entry, bool keepFormat, long[] ids)
    {
        var sent = false;
        await RunClipboardActionAsync(async () =>
        {
            sent = await PasteBackHelper.PasteAsync(target, () => WriteEntryAsync(entry, keepFormat)).ConfigureAwait(false);
            if (sent) CountPastes(ids);
        }).ConfigureAwait(false);
        return sent;
    }

    private void CountPastes(long[] ids)
    {
        _ = MutateAsync(async store =>
        {
            await store.IncrementPasteCountsAsync(ids).ConfigureAwait(false);
            var pastedIds = ids.ToHashSet();
            Publish(Entries.Select(item => pastedIds.Contains(item.Id)
                ? item with { PasteCount = item.PasteCount + 1 }
                : item).ToArray());
        });
    }

    internal Task CopyTextAsync(string text) =>
        _monitor == null ? Task.CompletedTask : RunClipboardActionAsync(() => _monitor.WriteTextAsync(text));

    internal Task CopyAsync(long id)
    {
        var entry = Entries.FirstOrDefault(entry => entry.Id == id);
        return entry == null || _monitor == null
            ? Task.CompletedTask
            : RunClipboardActionAsync(() => WriteEntryAsync(entry, keepFormat: true));
    }

    private async Task RunClipboardActionAsync(Func<Task> action)
    {
        await _clipboardGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!_stopping) await action().ConfigureAwait(false);
        }
        catch (Exception ex) { LogFailure(ex); PasteBackHelper.Notify("ClipboardHistory_PasteFailed"); }
        finally { _clipboardGate.Release(); }
    }

    private string ImageFilePath(ClipboardEntry entry) => Path.Combine(_imagesDirectory!, entry.ImagePath);

    /// <summary>Writes one entry back to the clipboard. keepFormat sends the captured HTML flavor
    /// along with the text so rich targets keep their formatting; plain text strips it.</summary>
    private Task WriteEntryAsync(ClipboardEntry entry, bool keepFormat) => entry.Kind switch
    {
        ClipboardEntryKind.Image => _monitor!.WriteImageAsync(ImageFilePath(entry)),
        ClipboardEntryKind.FilePaths => _monitor!.WriteFilesAsync(entry.PathList),
        _ => WriteTextEntryAsync(entry, keepFormat),
    };

    private async Task WriteTextEntryAsync(ClipboardEntry entry, bool keepFormat)
    {
        var html = string.Empty;
        if (keepFormat && entry.Id > 0 && _store != null)
        {
            await _gate.WaitAsync().ConfigureAwait(false);
            try { html = await _store.GetHtmlAsync(entry).ConfigureAwait(false); }
            finally { _gate.Release(); }
        }
        if (html.Length > 0)
            await _monitor!.WriteTextWithHtmlAsync(entry.Content, html).ConfigureAwait(false);
        else
            await _monitor!.WriteTextAsync(entry.Content).ConfigureAwait(false);
    }

    internal void Copy(long id) => _ = CopyAsync(id);

    internal void TogglePin(long id) => _ = MutateAsync(async store =>
    {
        if (Entries.FirstOrDefault(entry => entry.Id == id) is not { } entry) return;
        await store.SetPinnedAsync(id, !entry.Pinned).ConfigureAwait(false);
        await store.TrimAsync(ClipboardSettings.MaxEntries, ClipboardSettings.RetentionDays, ClipboardSettings.ImageRetentionDays).ConfigureAwait(false);
        Publish(await store.GetEntriesAsync(includeHtml: false).ConfigureAwait(false));
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

    /// <summary>Clears the panel's clear-history action: unpinned rows only. Pinned entries
    /// survive a bulk clear -- the pin's whole promise is surviving trims, clears included;
    /// removing a pinned entry is a deliberate per-row decision.</summary>
    internal void ClearHistory() => _ = MutateAsync(async store =>
    {
        await store.ClearUnpinnedAsync().ConfigureAwait(false);
        Publish(Entries.Where(entry => entry.Pinned).ToArray());
    });

    /// <summary>Exports the whole history (database snapshot + images) into one zip file.</summary>
    internal async Task<bool> ExportAsync(string destinationZip)
    {
        return await MutateAsync(store => store.ExportAsync(destinationZip)).ConfigureAwait(false);
    }

    /// <summary>Validates and prepares a backup before replacing any live history.</summary>
    internal async Task<bool> ImportFromAsync(string zipPath)
    {
        if (_store == null || _root == null) return false;
        var staging = Path.Combine(_root, $".import-{Guid.NewGuid():N}");
        try
        {
            var (database, images) = await Task.Run(() => ClipboardBackupSupport.Extract(zipPath, staging)).ConfigureAwait(false);
            var candidate = new ClipboardStore(database, images);
            await candidate.InitializeImportAsync().ConfigureAwait(false);
            var entries = await candidate.GetEntriesAsync(includeHtml: false).ConfigureAwait(false);
            if (entries.Any(entry => entry.Kind == ClipboardEntryKind.Image
                && (!ClipboardStoreSupport.IsImageFileName(entry.ImagePath) || !File.Exists(Path.Combine(images, entry.ImagePath)))))
                throw new InvalidDataException("Backup has invalid or missing image files.");

            return await MutateAsync(async _ =>
            {
                await candidate.TrimAsync(ClipboardSettings.MaxEntries, ClipboardSettings.RetentionDays,
                    ClipboardSettings.ImageRetentionDays).ConfigureAwait(false);
                var retained = await candidate.GetEntriesAsync(includeHtml: false).ConfigureAwait(false);
                ClipboardBackupSupport.ReplaceLiveFiles(_databasePath!, _imagesDirectory!, database, images);
                _sources.Clear();
                Publish(retained);
            }).ConfigureAwait(false);
        }
        catch (Exception ex) { LogFailure(ex); PasteBackHelper.Notify("ClipboardHistory_StorageFailed"); return false; }
        finally { ClipboardBackupSupport.DeleteStagingDirectory(staging); }
    }

    internal bool CanOpenSource(long id) => _sources.TryGetValue(id, out var source) && source.IsAlive;
    internal void OpenSource(long id)
    {
        if (_sources.TryGetValue(id, out var source) && source.IsAlive) SourceWindowFocus.Focus(source.Handle);
    }

    private Task<bool> MutateAsync(Func<ClipboardStore, Task> action)
    {
        lock (_writeGate)
        {
            if (_stopping) return Task.FromResult(false);
            var mutation = RunMutationAsync(_pendingWrites, action);
            _pendingWrites = mutation;
            return mutation;
        }
    }

    private async Task<bool> RunMutationAsync(Task previous, Func<ClipboardStore, Task> action)
    {
        try
        {
            await previous.ConfigureAwait(false);
            await _ready.ConfigureAwait(false);
            if (_store == null) return false;
            await _gate.WaitAsync().ConfigureAwait(false);
            try
            {
                await action(_store).ConfigureAwait(false);
                return true;
            }
            finally { _gate.Release(); }
        }
        catch (Exception ex)
        {
            LogFailure(ex);
            PasteBackHelper.Notify("ClipboardHistory_StorageFailed");
            return false;
        }
    }

    internal async Task StopAsync()
    {
        Task pending;
        lock (_writeGate)
        {
            if (!_stopping)
            {
                _stopping = true;
                _retentionStop.Cancel();
            }
            pending = _pendingWrites;
        }
        _monitor?.Stop();
        await _retentionTask.ConfigureAwait(false);
        await _ready.ConfigureAwait(false);
        await pending.ConfigureAwait(false);
        await _clipboardGate.WaitAsync().ConfigureAwait(false);
        _clipboardGate.Release();
    }

    private void OnAppExit(object sender, ExitEventArgs e) => Dispose();

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        StopAsync().GetAwaiter().GetResult();
        _retentionStop.Dispose();
        PluginSettingsService.SettingChanged -= SettingChanged;
        if (Application.Current is { } app) app.Exit -= OnAppExit;
        _monitor?.Dispose();
        _targets?.Dispose();
    }

    // Exception messages can contain SQL values or paths. Keep clipboard data out of the host log.
    private static void LogFailure(Exception ex) => Logger.Log($"[ClipboardHistory] {ex.GetType().Name} (0x{ex.HResult:X8}).", LogLevel.Error);
}
