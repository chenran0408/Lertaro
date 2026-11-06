using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Shapes;
using System.Windows.Threading;
using Lertaro.App.Helpers.Visuals;
using Lertaro.App.Services;
using Lertaro.App.ViewModels.Onboarding;
using Lertaro.App.Views.Controls;
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
/// search box, the results list) and never touches the wake path itself. While the main window is
/// up the card folds to a non-blocking strip and returns when it closes; a step ignored for its
/// whole budget folds the same way; Esc anywhere ends the tour. Every exit marks
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

    private const int PracticeBudgetSeconds = 30;
    private const double CardWidth = 420;

    private readonly OnboardingMachine _machine = new();
    private readonly DispatcherTimer _budgetTimer = new() { Interval = TimeSpan.FromSeconds(PracticeBudgetSeconds) };
    private TextBox? _hookedSearchTextBox;
    private ListBox? _hookedResults;
    private SearchBoxControl? _hookedSearchBox;
    private bool _lingerBeforeClose;

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
        _budgetTimer.Tick += (_, _) =>
        {
            if (_lingerBeforeClose) { Close(); return; }
            _machine.Handle(TourEvent.Timeout);
            Render();
        };
        RebuildDots();
        HookMainWindow();
        HookPracticeSurfaces();
        Render();
    }

    /// <summary>First run and tray replay both come through here. A foreground fullscreen app
    /// (video, game) delays the popup a few seconds instead of popping over it.</summary>
    public static void ShowTour()
    {
        var window = new OnboardingWindow();
        window.Owner = Application.Current?.MainWindow;
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
        PlaceAtRightEdge();
        Show();
        RestartBudget();
    }

    // Right edge, vertically centered, inside the work area -- beside the action, never over it.
    private void PlaceAtRightEdge()
    {
        var area = SystemParameters.WorkArea;
        Left = Math.Max(area.Left, area.Right - CardWidth - 20);
        Top = area.Top + Math.Max(0, (area.Height - 220) / 2);
    }

    private void HookMainWindow()
    {
        if (Application.Current?.MainWindow is not MainSearchWindow main) return;
        main.IsVisibleChanged += (_, args) =>
        {
            var visible = (bool)args.NewValue;
            // Hiding while on the Enter step IS the practice: executing a result closes the window
            // by design. Hiding any other time is just the user dismissing the window.
            if (!visible && _machine.StepIndex == 3)
                _machine.Handle(TourEvent.PracticeObserved);
            else
                _machine.Handle(visible ? TourEvent.MainWindowShown : TourEvent.MainWindowHidden);
            Render();
        };
    }

    // Wire the real surfaces once and guard on the current step -- unsubscribing per step would
    // fight the flat driver design for no gain, and the guards keep steps from cross-firing.
    private void HookPracticeSurfaces()
    {
        if (Application.Current?.MainWindow is not MainSearchWindow main) return;
        _hookedSearchTextBox = main.SearchTextBox;
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

        var step = Steps[Math.Min(_machine.StepIndex, Steps.Length - 1)];
        StepCounter.Text = $"{_machine.StepIndex + 1} / {Steps.Length}";
        StepTitle.Text = T(step.TitleKey);
        StepBody.Text = T(step.BodyKey);
        PracticeHint.Text = T(step.HintKey);
        BuildKeycaps(step.Keycaps);
        UpdateDots(_machine.StepIndex);

        // Minimized keeps the strip as the whole window: hide the card content, shrink the frame.
        var minimized = _machine.Mode == TourMode.Minimized;
        ExpandedCard.Visibility = minimized ? Visibility.Collapsed : Visibility.Visible;
        if (minimized)
        {
            Width = 190;
            MinHeight = 0;
            Left = Math.Max(SystemParameters.WorkArea.Left, SystemParameters.WorkArea.Right - Width - 20);
            Top = Math.Max(SystemParameters.WorkArea.Top, SystemParameters.WorkArea.Bottom - 70);
            TryButton.Visibility = Visibility.Visible;
        }
        else
        {
            Width = CardWidth;
            TryButton.Visibility = Visibility.Collapsed;
        }
    }

    private void ShowCompletedCard()
    {
        if (_lingerBeforeClose) return;
        _lingerBeforeClose = true;
        StepCounter.Text = string.Empty;
        StepTitle.Text = T("Tour_DoneTitle");
        StepBody.Text = string.Format(T("Tour_DoneBody"), _machine.CompletedSteps);
        PracticeHint.Text = string.Empty;
        KeycapPanel.Children.Clear();
        UpdateDots(-1);
        _budgetTimer.Stop();
        _budgetTimer.Interval = TimeSpan.FromSeconds(2.5);
        _budgetTimer.Tick += (_, _) => Close();
        _budgetTimer.Start();
    }

    private void Restore_Click(object sender, RoutedEventArgs e)
    {
        _machine.Handle(TourEvent.Restore);
        Render();
        RestartBudget();
    }

    private void Skip_Click(object sender, RoutedEventArgs e)
    {
        _machine.Handle(TourEvent.SkipRequested);
        Close();
    }

    private void RestartBudget()
    {
        _budgetTimer.Stop();
        _budgetTimer.Interval = TimeSpan.FromSeconds(PracticeBudgetSeconds);
        _budgetTimer.Start();
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
        for (var index = 0; index < OnboardingMachine.StepCount; index++)
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
        var settings = UserSettings.Load();
        settings.OnboardingCompleted = true;
        settings.OnboardingVersion = UserSettings.CurrentOnboardingVersion;
        _ = settings.Save();
    }

    private static string T(string key) => TranslationManager.Instance[key];
}
