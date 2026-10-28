using System.Runtime.CompilerServices;
using System.Diagnostics;
using System.Windows;
using GlassDesk.UI.Controls;
using Lertaro.Core;

namespace Lertaro.App.Services;

/// <summary>
/// Keeps an App-owned GlassWindow hidden until its first-frame preparation has completed.
/// Callers finish loading content, applying size, and positioning before invoking this helper;
/// their show callback then performs visibility and any focus or input-hook handoff.
/// </summary>
internal static class GlassWindowPresentationHelper
{
    private static readonly TimeSpan PreShowBudget = TimeSpan.FromMilliseconds(250);
    private static readonly ConditionalWeakTable<GlassWindow, Preparation> Preparations = new();

    internal static Task ShowWhenPreparedAsync(
        GlassWindow window,
        Action show,
        Func<bool>? isCurrent = null)
        => ShowWhenPreparedAsyncCore(window, show, isCurrent, static w => w.PrepareFirstDisplayAsync());

    private static async Task ShowWhenPreparedAsyncCore(
        GlassWindow window,
        Action show,
        Func<bool>? isCurrent,
        Func<GlassWindow, Task<FirstDisplayPreparationResult>> prepareFirstDisplay)
    {
        if (window.IsVisible)
        {
            show();
            return;
        }

        var startedAt = Stopwatch.GetTimestamp();
        Preparation preparation;
        Task<FirstDisplayPreparationResult> task;
        bool completed;
        while (true)
        {
            preparation = Preparations.GetValue(window, w => CreatePreparation(w, prepareFirstDisplay));
            task = preparation.Task;
            var remaining = PreShowBudget - Stopwatch.GetElapsedTime(startedAt);
            completed = task.IsCompleted || (remaining > TimeSpan.Zero
                && await Task.WhenAny(task, Task.Delay(remaining)) == task);
            if (!preparation.IsInvalidated) break;

            // A hide releases the surface backing, so a late preparation cannot serve the next reveal.
            // Wait only within the caller's pre-show budget; never overlap layout/capture work.
            if (!completed) break;
            try
            {
                await task;
            }
            catch (Exception ex)
            {
                Logger.Log($"[GlassWindow] Invalidated first-display preparation failed: {ex.Message}", LogLevel.Warn);
            }
            preparation.RemoveIfHidden();
        }

        FirstDisplayPreparationResult? result = null;
        if (completed)
        {
            try
            {
                result = await task;
            }
            catch (Exception ex)
            {
                Logger.Log($"[GlassWindow] First-display preparation failed; showing neutral material: {ex.Message}", LogLevel.Warn);
            }
        }
        if (!completed)
        {
            Logger.Log($"[GlassWindow] First-display preparation exceeded {PreShowBudget.TotalMilliseconds:0}ms; showing neutral material while it continues.", LogLevel.Debug);
            preparation.ObserveLateCompletionOnce(isCurrent);
        }

        // Async preparation can finish after the summon that requested it has been superseded.
        // The owner supplies its existing operation/version check where that distinction matters.
        if (isCurrent != null && !isCurrent())
        {
            // Preparation wakes surfaces before capture. If this summon was superseded,
            // release that backing only while the window is still hidden; another
            // concurrent reveal may already have made it visible.
            preparation.InvalidateAndSleepIfHidden();
            return;
        }

        if (!completed)
            window.WakeGlassSurfacesForDisplay();
        show();
        if (result is { } prepared)
            Logger.Log($"[GlassWindow] First-display material source: {prepared.Source} (optical ready: {prepared.IsOpticalMaterialReady}).", LogLevel.Debug);
    }

    internal static Task ShowWhenPreparedAsync(
        GlassWindow window,
        Action show,
        Func<bool>? isCurrent,
        Func<GlassWindow, Task<FirstDisplayPreparationResult>> prepareFirstDisplay)
    {
        ArgumentNullException.ThrowIfNull(prepareFirstDisplay);
        return ShowWhenPreparedAsyncCore(window, show, isCurrent, prepareFirstDisplay);
    }

    private static Preparation CreatePreparation(
        GlassWindow window,
        Func<GlassWindow, Task<FirstDisplayPreparationResult>> prepareFirstDisplay)
    {
        var preparation = new Preparation(window, prepareFirstDisplay);
        window.IsVisibleChanged += preparation.OnVisibilityChanged;
        return preparation;
    }

    private static async Task ObserveLateCompletionAsync(
        Preparation preparation,
        Task<FirstDisplayPreparationResult> task,
        Func<bool>? isCurrent)
    {
        try
        {
            var result = await task;
            Logger.Log($"[GlassWindow] Late first-display material source: {result.Source} (optical ready: {result.IsOpticalMaterialReady}).", LogLevel.Debug);
            if (isCurrent != null && !isCurrent())
                preparation.InvalidateAndSleepIfHidden();
        }
        catch (Exception ex)
        {
            Logger.Log($"[GlassWindow] Late first-display preparation failed: {ex.Message}", LogLevel.Warn);
        }
    }

    private sealed class Preparation
    {
        private readonly GlassWindow _window;
        internal Task<FirstDisplayPreparationResult> Task { get; }

        private bool _lateObserverStarted;
        private volatile bool _hidden;
        internal bool IsInvalidated => _hidden;

        internal Preparation(GlassWindow window, Func<GlassWindow, Task<FirstDisplayPreparationResult>> prepareFirstDisplay)
        {
            _window = window;
            Task = prepareFirstDisplay(window);
            _ = Task.ContinueWith(
                static (_, state) => ((Preparation)state!).RemoveIfHidden(),
                this,
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        internal void ObserveLateCompletionOnce(Func<bool>? isCurrent)
        {
            if (_lateObserverStarted) return;
            _lateObserverStarted = true;
            _ = ObserveLateCompletionAsync(this, Task, isCurrent);
        }

        internal void InvalidateAndSleepIfHidden()
        {
            _window.Dispatcher.VerifyAccess();
            if (_window.IsVisible) return;
            if (!Preparations.TryGetValue(_window, out var current) || !ReferenceEquals(current, this)) return;

            // Other summons may be awaiting this same task. Prevent them from treating
            // its now-cleared backing as a valid prepared result.
            _hidden = true;
            _window.SleepGlassSurfacesIfHidden();
            RemoveIfHidden();
        }

        internal void OnVisibilityChanged(object? sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.NewValue is true) return;
            _hidden = true;
            RemoveIfHidden();
        }

        internal void RemoveIfHidden()
        {
            if (!_hidden || !Task.IsCompleted) return;
            if (!_window.Dispatcher.CheckAccess())
            {
                _window.Dispatcher.BeginInvoke(RemoveIfHidden);
                return;
            }
            _window.IsVisibleChanged -= OnVisibilityChanged;
            if (Preparations.TryGetValue(_window, out var current) && ReferenceEquals(current, this))
                Preparations.Remove(_window);
        }
    }
}
