namespace Lertaro.App.ViewModels.Onboarding;

/// <summary>How the tour card presents itself right now.</summary>
public enum TourMode
{
    /// <summary>Full card beside the action.</summary>
    Expanded,
    /// <summary>Non-blocking strip: the practice window owns the screen (main search window up,
    /// or the step timed out and the card folded to a corner hint). Restorable.</summary>
    Minimized,
}

/// <summary>Where the tour is in its life; Completed/Skipped are terminal.</summary>
public enum TourOutcome { InProgress, Completed, Skipped }

/// <summary>Inputs the tour reacts to. Everything the drivers observe maps onto one of these.</summary>
public enum TourEvent
{
    /// <summary>The user did the step's real action (typed, selected, pressed, opened).</summary>
    PracticeObserved,
    /// <summary>The main search window appeared -- the card yields the screen.</summary>
    MainWindowShown,
    /// <summary>The main search window went away -- the card returns.</summary>
    MainWindowHidden,
    /// <summary>The step's practice budget elapsed; fold to a corner hint.</summary>
    Timeout,
    /// <summary>User dismissed the tour.</summary>
    SkipRequested,
    /// <summary>User clicked the folded hint back open.</summary>
    Restore,
}

/// <summary>
/// Pure state machine for the first-run tour: five practice steps, one real event each, fold and
/// restore on demand, two terminal outcomes. No WPF, no timers, no IO -- drivers observe the world
/// and feed events; the view renders whatever state comes back. Every transition is total, so a
/// late event after completion or skip can never revive the tour.
/// </summary>
public sealed class OnboardingMachine
{
    public const int StepCount = 5;

    /// <summary>Zero-based practice step the tour is waiting on.</summary>
    public int StepIndex { get; private set; }
    public TourMode Mode { get; private set; } = TourMode.Expanded;
    public TourOutcome Outcome { get; private set; } = TourOutcome.InProgress;

    /// <summary>How many steps the user actually finished -- for the summary line on completion.</summary>
    public int CompletedSteps { get; private set; }

    /// <summary>What the host should persist when the tour window closes. Completed marks the
    /// flag and arms the version (future redesigns re-arm via the version compare); Skipped also
    /// marks the flag -- one-shot semantics mean the tour never re-pops by itself -- and records
    /// which step was abandoned (0 = not skipped); InProgress (the process died mid-tour) writes
    /// nothing, so the next start replays it.</summary>
    public (bool MarkCompleted, int SkipStep, bool UpdateVersion) PersistencePlan(int currentVersion) =>
        Outcome switch
        {
            TourOutcome.Completed => (true, 0, true),
            TourOutcome.Skipped => (true, StepIndex + 1, false),
            _ => (false, 0, false),
        };

    public void Handle(TourEvent tourEvent)
    {
        if (Outcome != TourOutcome.InProgress) return; // terminal: nothing revives the tour
        switch (tourEvent)
        {
            case TourEvent.PracticeObserved:
                CompletedSteps++;
                if (StepIndex >= StepCount - 1) Outcome = TourOutcome.Completed;
                else StepIndex++;
                break;
            case TourEvent.MainWindowShown:
                Mode = TourMode.Minimized;
                break;
            case TourEvent.MainWindowHidden:
            case TourEvent.Restore:
                Mode = TourMode.Expanded;
                break;
            case TourEvent.Timeout:
                Mode = TourMode.Minimized;
                break;
            case TourEvent.SkipRequested:
                Outcome = TourOutcome.Skipped;
                break;
        }
    }
}
