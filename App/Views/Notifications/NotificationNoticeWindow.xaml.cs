using System.Windows;
using System.Windows.Controls;
using Lertaro.PluginSdk.Abstractions;

namespace Lertaro.App.Views.Notifications;

/// <summary>
/// Position 2: the single line at the bottom centre of the screen. No title, no source, no controls.
/// </summary>
public partial class NotificationNoticeWindow : GlassDesk.UI.Controls.GlassWindow
{
    public NotificationNoticeWindow()
    {
        InitializeComponent();
    }

    /// <summary>The widest the line may get, in DIP: a third of the work area, past which the text ellipsizes.</summary>
    public void ConsumeWidth(double maxTextWidthDip) => Line.MaxWidth = Math.Max(40, maxTextWidthDip);

    public void SetContent(string message, NotificationLevel level)
    {
        Line.Text = message;
        // The text colour is the only thing that carries the level here: there is no room for an icon in a
        // 36 DIP pill. Warning and Error use semantic colors; the ordinary reading color comes from the
        // GlassWindow foreground palette and changes with its light/dark appearance.
        var semanticColor = level switch
        {
            NotificationLevel.Warn => "WarningBrush",
            NotificationLevel.Error => "ErrorBrush",
            _ => null,
        };
        if (semanticColor is null)
            Line.ClearValue(TextBlock.ForegroundProperty);
        else
            Line.SetResourceReference(TextBlock.ForegroundProperty, semanticColor);
    }
}
