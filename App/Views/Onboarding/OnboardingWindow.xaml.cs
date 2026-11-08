using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Shapes;
using System.Windows.Threading;
using Lertaro.App.Helpers.Visuals;
using Lertaro.App.Services;
using Lertaro.App.Services.Favorites;
using Lertaro.App.Services.Notifications;
using Lertaro.App.ViewModels.Search;
using Lertaro.App.ViewModels.Onboarding;
using Lertaro.App.Views.Controls;
using Lertaro.App.Views.Controls.Dialogs;
using Lertaro.Core;
using FullscreenHelper = Lertaro.Core.Hook.FullscreenHelper;
using ThemedWindowIconHelper = Lertaro.App.Services.Theme.ThemedWindowIconHelper;
using TextBox = System.Windows.Controls.TextBox;
using ListBox = System.Windows.Controls.ListBox;
using Application = System.Windows.Application;
using MainSearchWindow = Lertaro.App.QuickSearchWindow;
using Brush = System.Windows.Media.Brush;
using TranslateTransform = System.Windows.Media.TranslateTransform;

namespace Lertaro.App.Views.Onboarding;

/// <summary>
/// The first-run tour card: each step demos its keycap animation (Phase 3) and then waits for the
/// user to actually do it -- the card listens to the real surfaces (main window visibility, the
/// search box, the results list) and never touches the wake path itself. Listary-style, the card
/// never folds: while the search window is up it docks beside it (same top edge, whichever side
/// has room) so the instruction and the practice stay on screen together, and it returns to its
/// resting spot when the window closes. Esc anywhere ends the tour. Every exit marks
/// OnboardingCompleted, so the card shows itself exactly once -- the tray replays it on demand.
/// </summary>
public partial class OnboardingWindow : AppWindow
{
    private static readonly (string TitleKey, string BodyKey, string HintKey, string? Keycaps)[] Steps =
    [
        ("Tour1_Title", "Tour1_Body", "Tour1_Hint", "Ctrl\u2009+\u2009Ctrl"),
        ("Tour2_Title", "Tour2_Body", "Tour2_Hint", null),
        ("Tour3_Title", "Tour3_Body", "Tour3_Hint", "\u2191\u2193"),
        ("Tour4_Title", "Tour4_Body", "Tour4_Hint", "Enter"),
        ("Tour5_Title", "Tour5_Body", "Tour5_Hint", "Ctrl\u2009+\u2009O"),
    ];

    private const double CardWidth = 420;
    private const double EstimatedCardHeight = 320;

    private readonly OnboardingMachine _machine = new();
    // Runs once, after the completed card has been up for its linger delay: then the tour closes
    // itself. There is no per-step budget -- an ignored card stays put (Listary does the same)
    // instead of folding away mid-practice.
    private readonly DispatcherTimer _lingerTimer = new() { Interval = TimeSpan.FromSeconds(2.5) };
    private bool _indexingVariant;
    private PropertyChangedEventHandler? _indexReadyHandler;
    private TextBox? _hookedSearchTextBox;
    private ListBox? _hookedResults;
    private SearchBoxControl? _hookedSearchBox;
    private bool _completedShown;

    public OnboardingWindow()
    {
        InitializeComponent();
        AltTabExcluder.Attach(this);
        SystemMenuBlocker.Attach(this, blockClose: false);
        ThemedWindowIconHelper.Apply(this);
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) Close();
        };
        _lingerTimer.Tick += (_, _) => Close();
        RebuildDots();
        HookMainWindow();
        HookPracticeSurfaces();
        Render();
    }

    /// <summary>First run and tray replay both come through here. A foreground fullscreen app
    /// (video, game) delays the popup a few seconds instead of popping over it.</summary>
    /// <summary>True when the configured wake hotkey is a chord and RegisterHotKey says someone
    /// else owns it -- step one then teaches the tray path instead. Bare-modifier wakes (the
    /// default double-ctrl) cannot be probed: RegisterHotKey has no representation for them, and
    /// such a wake cannot collide with any registered hotkey. ponytail: a true double-tap
    /// conflict (another app swallowing the chord) is undetectable here; the upgrade is watching
    /// for the detector's miss at runtime, not a bigger probe.</summary>
    private static bool _teachTrayPath;

    /// <summary>True while a tour card is on screen -- D1 tips suppress themselves during it.</summary>
    public static bool TourActive { get; private set; }

    public static void ShowTour()
    {
        _teachTrayPath = false;
        if (FavoriteHotkeyFormat.TryBuild(UserSettings.Load().Hotkeys.ToggleWindowHotkey,
                out var virtualKey, out var modifiers) && HotkeyConflictProbe.IsTaken(modifiers, virtualKey))
            _teachTrayPath = true;
        var window = new OnboardingWindow();
        // Startup summons this before the main window has ever been shown; assigning that as Owner
        // throws. ResolveOwner only hands back windows with a live handle, else none -- the card
        // then centres on screen, which is exactly the first-run layout anyway.
        if (OwnedDialog.ResolveOwner(window) is { } owner)
            window.Owner = owner;
        if (FullscreenHelper.IsForegroundWindowFullScreen())
        {
            var retries = 0;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                if (++retries >= 12 || !FullscreenHelper.IsForegroundWindowFullScreen())
                    window.ShowCard();
                else
                    timer.Start();
            };
            timer.Start();
            return;
        }
        window.ShowCard();
    }

    private void ShowCard()
    {
        Place();
        Show();
        TourActive = true;
    }

    // Resting spot: right edge of the foreground window's monitor, vertically centered, inside
    // that monitor's work area (per-monitor DPI via the shared placement resolver). While the
    // search window is up, the card docks beside it at the same top edge -- the side with room
    // wins -- so tour and practice read as one panel; if neither side fits (small screen, wide
    // window) the resting spot stands. ponytail: main.Left/Top are DIPs in the search window's
    // own monitor's scale; the resolver returns the foreground monitor's. They agree in the
    // normal first-run shape (summon on the foreground monitor) and the clamps below keep a
    // cross-DPI surprise on-screen rather than off it.
    private void Place()
    {
        var area = NotificationPlacement.Resolve().WorkAreaDip;
        var left = area.Right - CardWidth - 20;
        var top = area.Top + Math.Max(area.Height - EstimatedCardHeight, 0) / 2;

        if (Application.Current?.MainWindow is MainSearchWindow { IsVisible: true } main)
        {
            const double gap = 14;
            if (main.Left + main.ActualWidth + gap + CardWidth <= area.Right)
            {
                left = main.Left + main.ActualWidth + gap;
                top = main.Top;
            }
            else if (main.Left - gap - CardWidth >= area.Left)
            {
                left = main.Left - gap - CardWidth;
                top = main.Top;
            }
        }

        Left = Math.Clamp(left, area.Left, Math.Max(area.Left, area.Right - CardWidth));
        Top = Math.Clamp(top, area.Top, Math.Max(area.Top, area.Bottom - EstimatedCardHeight));
    }

    private void HookMainWindow()
    {
        if (Application.Current?.MainWindow is not MainSearchWindow main) return;
        main.IsVisibleChanged += (_, args) =>
        {
            var visible = (bool)args.NewValue;
            // The window's visibility IS the practice signal on two steps: appearing completes the
            // wake step, disappearing completes the Enter step (executing a result closes the
            // window by design). Any other visibility change is just the user coming and going.
            if (visible && _machine.StepIndex == 0)
                _machine.Handle(TourEvent.PracticeObserved);
            else if (!visible && _machine.StepIndex == 3)
                _machine.Handle(TourEvent.PracticeObserved);
            Render();
            // Queued at Loaded priority so the SizeToContent search window has laid out before the
            // dock math reads its ActualWidth -- on first summon it is still zero at this point.
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, Place);
        };
    }

    // Wire the real surfaces once and guard on the current step -- unsubscribing per step would
    // fight the flat driver design for no gain, and the guards keep steps from cross-firing.
    private void HookPracticeSurfaces()
    {
        if (Application.Current?.MainWindow is not MainSearchWindow main) return;
        _hookedSearchTextBox = main.SearchTextBox;
        if (main.DataContext is QuickSearchViewModel quickVm)
        {
            _indexReadyHandler = (_, args) =>
            {
                if (args.PropertyName == nameof(QuickSearchViewModel.IsIndexReady)
                    && _machine.StepIndex == 1 && _indexingVariant)
                    Render(); // readiness arrived: back to the real step-two copy
            };
            quickVm.PropertyChanged += _indexReadyHandler;
        }
        _hookedSearchTextBox.TextChanged += (_, _) =>
        {
            if (_machine.StepIndex == 1 && _hookedSearchTextBox.Text.Length > 0) Observe();
        };
        _hookedResults = main.LstResults;
        _hookedResults.SelectionChanged += (_, _) =>
        {
            if (_machine.StepIndex == 2) Observe();
        };
        _hookedSearchBox = main.SearchBox;
        DependencyPropertyDescriptor
            .FromProperty(SearchBoxControl.IsInActionsModeProperty, typeof(SearchBoxControl))
            .AddValueChanged(_hookedSearchBox, (_, _) =>
            {
                if (_machine.StepIndex == 4 && _hookedSearchBox.IsInActionsMode) Observe();
            });
    }

    private void Observe()
    {
        _machine.Handle(TourEvent.PracticeObserved);
        Render();
    }

    private void Render()
    {
        if (_machine.Outcome != TourOutcome.InProgress)
        {
            if (_machine.Outcome == TourOutcome.Skipped) { Close(); return; }
            ShowCompletedCard();
            return;
        }

        var step = Steps[Math.Min(_machine.StepIndex, _machine.StepCount - 1)];
        // W1: step two is the only step whose practice depends on the index -- while it builds,
        // the copy switches to the indexing variant (and back) instead of teaching a search that
        // cannot return file results yet.
        _indexingVariant = _machine.StepIndex == 1 && !IsIndexReady();
        StepCounter.Text = $"{_machine.StepIndex + 1} / {_machine.StepCount}";
        StepTitle.Text = T(_indexingVariant ? "Tour2_IndexingTitle" : step.TitleKey);
        StepBody.Text = T(_indexingVariant ? "Tour2_IndexingBody" : step.BodyKey);
        PracticeHint.Text = T(_indexingVariant ? "Tour2_IndexingHint" : step.HintKey);
        BuildKeycaps(_indexingVariant ? null : step.Keycaps);
        UpdateDots(_machine.StepIndex);

        if (_machine.StepIndex == 0 && _teachTrayPath)
        {
            StepTitle.Text = T("Tour1_TrayTitle");
            StepBody.Text = T("Tour1_TrayBody");
            PracticeHint.Text = T("Tour1_TrayHint");
            KeycapPanel.Children.Clear();
        }
    }

    private void ShowCompletedCard()
    {
        if (_completedShown) return;
        _completedShown = true;
        StepCounter.Text = string.Empty;
        StepTitle.Text = T("Tour_DoneTitle");
        StepBody.Text = string.Format(T("Tour_DoneBody"), _machine.CompletedSteps);
        PracticeHint.Text = string.Empty;
        KeycapPanel.Children.Clear();
        UpdateDots(-1);
        _lingerTimer.Stop();
        _lingerTimer.Start();
    }

    private void Skip_Click(object sender, RoutedEventArgs e)
    {
        _machine.Handle(TourEvent.SkipRequested);
        Close();
    }

    // The demo driver: each keycap floats slightly raised and translucent, then sinks onto its
    // resting position like a pressed key -- staggered left to right, the whole chord repeating
    // twice. Only Motion tokens; no effects, so the loop never costs a frame when idle.
    private void BuildKeycaps(string? combo)
    {
        KeycapPanel.Children.Clear();
        if (string.IsNullOrEmpty(combo)) return;

        var chips = combo.Split('\u2009');
        var sink = (Duration)FindResource("DurationHover");
        var fadeIn = (Duration)FindResource("DurationWindowFadeIn");
        var ease = (System.Windows.Media.Animation.CubicEase)FindResource("EaseOutCubic");
        var storyboard = new System.Windows.Media.Animation.Storyboard
        {
            RepeatBehavior = new System.Windows.Media.Animation.RepeatBehavior(2),
        };

        for (var index = 0; index < chips.Length; index++)
        {
            var chip = new Border
            {
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8, 3, 8, 3),
                Margin = new Thickness(0, 0, 6, 0),
                Opacity = 0.25,
                RenderTransform = new TranslateTransform(0, -4),
                Background = (Brush)FindResource("ControlBackground"),
                BorderBrush = (Brush)FindResource("ControlBorderBrush"),
                BorderThickness = new Thickness(1),
                Child = new TextBlock
                {
                    Text = chips[index],
                    FontSize = 12.5,
                    Foreground = (Brush)FindResource("TextPrimary"),
                },
            };
            KeycapPanel.Children.Add(chip);

            var begin = TimeSpan.FromMilliseconds(index * 150);
            var opacity = new System.Windows.Media.Animation.DoubleAnimation
            {
                From = 0.25, To = 1, Duration = fadeIn, BeginTime = begin, EasingFunction = ease,
            };
            System.Windows.Media.Animation.Storyboard.SetTarget(opacity, chip);
            System.Windows.Media.Animation.Storyboard.SetTargetProperty(opacity, new PropertyPath("Opacity"));
            storyboard.Children.Add(opacity);

            var press = new System.Windows.Media.Animation.DoubleAnimation
            {
                From = -4, To = 0, Duration = sink, BeginTime = begin, EasingFunction = ease,
            };
            System.Windows.Media.Animation.Storyboard.SetTarget(press, chip);
            System.Windows.Media.Animation.Storyboard.SetTargetProperty(press, new PropertyPath("RenderTransform.Y"));
            storyboard.Children.Add(press);
        }

        storyboard.Begin(KeycapPanel);
    }

    private void RebuildDots()
    {
        for (var index = 0; index < _machine.StepCount; index++)
            Dots.Children.Add(new Ellipse
            {
                Width = 8,
                Height = 8,
                Margin = new Thickness(4, 0, 4, 0),
                Opacity = 0.35,
            });
    }

    private void UpdateDots(int active)
    {
        for (var index = 0; index < Dots.Children.Count; index++)
        {
            var dot = (Ellipse)Dots.Children[index];
            dot.Fill = (Brush)FindResource(index == active ? "AccentBlue" : "TextSecondary");
            dot.Opacity = index == active ? 1 : 0.35;
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        TourActive = false;
        // The machine decides what this exit means: Completed marks + arms the version, Skipped
        // marks with the abandoned step (one-shot holds -- the tour never re-pops by itself),
        // InProgress (the process died) writes nothing so the next start replays it.
        var (markCompleted, skipStep, updateVersion) = _machine.PersistencePlan(UserSettings.CurrentOnboardingVersion);
        if (!markCompleted) return;
        var settings = UserSettings.Load();
        settings.OnboardingCompleted = true;
        settings.OnboardingSkipStep = skipStep;
        if (updateVersion) settings.OnboardingVersion = UserSettings.CurrentOnboardingVersion;
        _ = settings.Save();
    }

    /// <summary>The same readiness the search box itself reports, via the main window's
    /// DataContext (QuickSearchViewModel.IsIndexReady -> ServiceMonitorViewModel). Fail-open: any
    /// surprise in the chain shows the normal step rather than blocking the tour.</summary>
    private bool IsIndexReady() =>
        Application.Current?.MainWindow is MainSearchWindow { DataContext: QuickSearchViewModel vm }
            ? vm.IsIndexReady
            : true;

    private static string T(string key) => TranslationManager.Instance[key];
}
