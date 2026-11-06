using System.Windows;
using System.Windows.Shapes;
using Lertaro.App.Helpers.Visuals;
using Lertaro.App.Services;
using Lertaro.App.Services.Theme;
using Lertaro.Core;
using Application = System.Windows.Application;
using Brushes = System.Windows.Media.Brushes;

namespace Lertaro.App.Views.Controls.Dialogs;

/// <summary>
/// Listary-style first-run tour: a five-step card that introduces the wake hotkey, search, the
/// clipboard panel and global quick paste, then hands the user to the settings. Shown once after
/// install (OnboardingCompleted) and replayable from the tray menu; every exit path marks the flag
/// so the tour never nags, it only invites.
/// </summary>
public partial class WelcomeWindow : AppWindow
{
    // Each step carries a live-hotkey selector so the tour teaches THIS install's configured
    // combos, not the defaults -- the wake key especially is commonly rebound.
    private sealed record WelcomeStep(string Glyph, string TitleKey, string BodyKey, Func<HotkeyPageSettings, string?>? Hotkey);

    private static readonly WelcomeStep[] Steps =
    [
        new("\uE80F", "Onboarding_Step1Title", "Onboarding_Step1Body", null),
        new("\uE721", "Onboarding_Step2Title", "Onboarding_Step2Body", hotkeys => hotkeys.ToggleWindowHotkey),
        new("\uE8C8", "Onboarding_Step3Title", "Onboarding_Step3Body", hotkeys => hotkeys.ClipboardPanelHotkey),
        new("\uE765", "Onboarding_Step4Title", "Onboarding_Step4Body", null),
        new("\uE713", "Onboarding_Step5Title", "Onboarding_Step5Body", null),
    ];

    private int _step;

    public WelcomeWindow()
    {
        InitializeComponent();
        SystemMenuBlocker.Attach(this, blockClose: false);
        ThemedWindowIconHelper.Apply(this);
        RebuildDots();
        ShowStep(0);
    }

    /// <summary>Entry point for the first run and the tray replay alike. Marks the settings flag
    /// no matter how the tour ends, so it shows exactly once by itself.</summary>
    public static void ShowTour()
    {
        var window = new WelcomeWindow();
        window.Owner = Application.Current?.MainWindow;
        window.ShowDialog();
        var settings = UserSettings.Load();
        if (!settings.OnboardingCompleted)
        {
            settings.OnboardingCompleted = true;
            _ = settings.Save();
        }
    }

    private void ShowStep(int index)
    {
        _step = Math.Clamp(index, 0, Steps.Length - 1);
        var step = Steps[_step];
        StepGlyph.Text = step.Glyph;
        StepTitle.Text = T(step.TitleKey);
        StepBody.Text = BuildBody(step);
        BackButton.IsEnabled = _step > 0;
        NextButton.Content = T(_step == Steps.Length - 1 ? "Onboarding_Done" : "Onboarding_Next");
        UpdateDots();
    }

    // The body leads with the explanation and, when the step names a rebindable hotkey, appends
    // the live configured combo on its own line.
    private static string BuildBody(WelcomeStep step)
    {
        var body = T(step.BodyKey);
        if (step.Hotkey == null) return body;
        var combo = step.Hotkey(UserSettings.Load().Hotkeys);
        return string.IsNullOrWhiteSpace(combo) ? body : $"{body}\n{combo}";
    }

    private void RebuildDots()
    {
        for (var index = 0; index < Steps.Length; index++)
        {
            Dots.Children.Add(new Ellipse
            {
                Width = 8,
                Height = 8,
                Margin = new Thickness(4, 0, 4, 0),
                Fill = Brushes.Gray,
                Opacity = 0.35,
            });
        }
    }

    private void UpdateDots()
    {
        for (var index = 0; index < Dots.Children.Count; index++)
        {
            var dot = (Ellipse)Dots.Children[index];
            var active = index == _step;
            dot.Fill = (System.Windows.Media.Brush)FindResource(active ? "AccentBlue" : "TextSecondary");
            dot.Opacity = active ? 1 : 0.35;
        }
    }

    private void Back_Click(object sender, RoutedEventArgs e) => ShowStep(_step - 1);

    private void Next_Click(object sender, RoutedEventArgs e)
    {
        if (_step == Steps.Length - 1) Close(); // ShowTour marks the flag after any close
        else ShowStep(_step + 1);
    }

    private void Skip_Click(object sender, RoutedEventArgs e) => Close();

    private static string T(string key) => TranslationManager.Instance[key];
}
