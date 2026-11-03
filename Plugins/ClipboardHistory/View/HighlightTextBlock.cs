using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Lertaro.PluginSdk.Services;

namespace Lertaro.Plugins.ClipboardHistory.View;

/// <summary>
/// A TextBlock that, when <see cref="Query"/> is non-empty, renders its text with the host
/// matcher's highlight mask: matched characters take the accent color. With an empty query it
/// behaves exactly like a plain TextBlock. Rebuilds its runs only when Text or Query changes.
/// </summary>
public sealed class HighlightTextBlock : TextBlock
{
    public static readonly DependencyProperty QueryProperty = DependencyProperty.Register(
        nameof(Query), typeof(string), typeof(HighlightTextBlock),
        new PropertyMetadata(string.Empty, OnTextChanged));

    static HighlightTextBlock() =>
        // Hook the inherited Text property so XAML binding keeps its normal storage; the hook only
        // re-renders the runs.
        TextProperty.OverrideMetadata(typeof(HighlightTextBlock),
            new FrameworkPropertyMetadata(string.Empty, OnTextChanged));

    public string Query
    {
        get => (string)GetValue(QueryProperty);
        set => SetValue(QueryProperty, value);
    }

    private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (HighlightTextBlock)d;
        if (control.Dispatcher.CheckAccess()) control.Rebuild();
        else control.Dispatcher.BeginInvoke(control.Rebuild);
    }

    private void Rebuild()
    {
        var text = Text ?? string.Empty;
        var query = Query ?? string.Empty;
        Inlines.Clear();
        if (query.Length == 0 || text.Length == 0)
            return; // plain TextBlock rendering already covers this

        var mask = FuzzyMatchService.GetHighlightMask(text, query);
        var highlightBrush = ResolveHighlightBrush();
        foreach (var segment in ClipboardPanelText.Split(text, mask))
        {
            var run = new Run(segment.Text);
            if (segment.Highlighted && highlightBrush != null)
                run.Foreground = highlightBrush;
            Inlines.Add(run);
        }
    }

    /// <summary>The host theme's AccentBlue, resolved per rebuild so a theme switch applies on the
    /// next refresh; falls back to inherited color when the resource is absent.</summary>
    private static Brush? ResolveHighlightBrush()
    {
        var resource = Application.Current?.TryFindResource("AccentBlue");
        return resource switch
        {
            Brush brush => brush,
            Color color => new SolidColorBrush(color),
            _ => null,
        };
    }
}
