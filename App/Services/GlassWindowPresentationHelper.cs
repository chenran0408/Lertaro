using System.Runtime.CompilerServices;
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

    internal static async Task ShowWhenPreparedAsync(
        GlassWindow window,
        Action show,
        Func<bool>? isCurrent = null)
    {
        if (window.IsVisible)
        {
            show();
            return;
        }

        var preparation = Preparations.GetValue(window, static w => new Preparation(w));
        var task = preparation.Task;
        var completed = await Task.WhenAny(task, Task.Delay(PreShowBudget)) == task;
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
            preparation.ObserveLateCompletionOnce();
        }

        // Async preparation can finish after the summon that requested it has been superseded.
        // The owner supplies its existing operation/version check where that distinction matters.
        if (isCurrent != null && !isCurrent()) return;

        show();
        if (result is { } prepared)
            Logger.Log($"[GlassWindow] First-display material source: {prepared.Source} (optical ready: {prepared.IsOpticalMaterialReady}).", LogLevel.Debug);
    }

    private static async Task ObserveLateCompletionAsync(Task<FirstDisplayPreparationResult> task)
    {
        try
        {
            var result = await task;
            Logger.Log($"[GlassWindow] Late first-display material source: {result.Source} (optical ready: {result.IsOpticalMaterialReady}).", LogLevel.Debug);
        }
        catch (Exception ex)
        {
            Logger.Log($"[GlassWindow] Late first-display preparation failed: {ex.Message}", LogLevel.Warn);
        }
    }

    private sealed class Preparation(GlassWindow window)
    {
        internal Task<FirstDisplayPreparationResult> Task { get; } = window.PrepareFirstDisplayAsync();

        private bool _lateObserverStarted;

        internal void ObserveLateCompletionOnce()
        {
            if (_lateObserverStarted) return;
            _lateObserverStarted = true;
            _ = ObserveLateCompletionAsync(Task);
        }
    }
}
