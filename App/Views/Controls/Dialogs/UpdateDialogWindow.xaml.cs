using System.Windows;
using System.Windows.Input;
using Lertaro.App.Services;

namespace Lertaro.App.Views.Controls.Dialogs;

public enum UpdateDialogResult
{
    Primary,
    Secondary,
    Closed,
}

/// <summary>
/// The update-flow dialog: a message, an optional "never automatically prompt again" checkbox and up
/// to three explicit buttons. One window serves both update interactions -- the startup
/// "new version available" prompt (checkbox shown, primary = open the About page) and the
/// post-download install guidance (checkbox hidden, primary = run the installer, secondary = open
/// the containing folder) -- so the two flows keep identical behavior and styling.
/// </summary>
public partial class UpdateDialogWindow : AppWindow
{
    /// <summary>Whether the user checked "never automatically prompt again" (only read when the
    /// checkbox was made visible).</summary>
    public bool NeverPromptAgain => ChkNeverPrompt.IsChecked == true;

    public UpdateDialogResult Result { get; private set; } = UpdateDialogResult.Closed;

    public UpdateDialogWindow(string title, string message, bool showNeverPrompt)
    {
        InitializeComponent();
        TxtTitle.Text = title;
        TxtMessage.Text = message;
        BtnClose.Content = TranslationManager.Instance["About_Later"];
        if (showNeverPrompt)
            ChkNeverPrompt.Visibility = Visibility.Visible;
        if (System.Windows.Application.Current?.MainWindow?.Icon is { } icon)
        {
            TitleBarLogo.Source = icon;
        }
    }

    /// <summary>Sets the primary and optional secondary button captions; the secondary button is
    /// hidden when its caption is empty.</summary>
    public void SetButtons(string primary, string secondary = "")
    {
        BtnPrimary.Content = primary;
        if (!string.IsNullOrEmpty(secondary))
        {
            BtnSecondary.Content = secondary;
            BtnSecondary.Visibility = Visibility.Visible;
        }
        BtnPrimary.IsDefault = true;
    }

    private void BtnPrimary_Click(object sender, RoutedEventArgs e)
    {
        Result = UpdateDialogResult.Primary;
        Close();
    }

    private void BtnSecondary_Click(object sender, RoutedEventArgs e)
    {
        Result = UpdateDialogResult.Secondary;
        Close();
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        Result = UpdateDialogResult.Closed;
        Close();
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            try { DragMove(); } catch { }
        }
    }
}
