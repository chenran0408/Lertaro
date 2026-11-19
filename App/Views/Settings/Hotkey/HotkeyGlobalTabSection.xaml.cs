using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Lertaro.App.Services;
using Lertaro.App.Views.Controls;
using Panel = System.Windows.Controls.Panel;

namespace Lertaro.App.Views.Settings.Hotkey;

public partial class HotkeyGlobalTabSection : System.Windows.Controls.UserControl
{
    public HotkeyGlobalTabSection()
    {
        InitializeComponent();
        Loaded += (_, _) => TranslationManager.Instance.PropertyChanged += OnLanguageChanged;
        Unloaded += (_, _) => TranslationManager.Instance.PropertyChanged -= OnLanguageChanged;
    }

    internal void ClearFilter() => HotkeyFilterBox.Clear();

    private void OnLanguageChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TranslationManager.CurrentCulture))
            Dispatcher.BeginInvoke(new Action(ApplyFilter));
    }

    private void Filter_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (HotkeyGroups != null) ApplyFilter();
    }

    private void ApplyFilter()
    {
        var query = HotkeyFilterBox.Text.Trim();
        var keyQuery = CompactShortcut(query);
        var anyResults = false;
        foreach (var card in HotkeyGroups.Children.OfType<Border>())
        {
            var content = (StackPanel)card.Child;
            var titleMatches = content.Children.OfType<TextBlock>().FirstOrDefault()?.Text
                .Contains(query, StringComparison.CurrentCultureIgnoreCase) == true;
            var hasRows = false;
            foreach (var row in content.Children.OfType<StackPanel>())
            {
                var matches = query.Length == 0 || titleMatches
                    || (row.Tag is string label && label.Contains(query, StringComparison.CurrentCultureIgnoreCase))
                    || ShortcutMatches(row, keyQuery);
                row.Visibility = matches ? Visibility.Visible : Visibility.Collapsed;
                hasRows |= matches;
            }
            card.Visibility = hasRows ? Visibility.Visible : Visibility.Collapsed;
            anyResults |= hasRows;
        }
        NoMatchingHotkeys.Visibility = anyResults ? Visibility.Collapsed : Visibility.Visible;
    }

    private static bool ShortcutMatches(Panel row, string keyQuery)
    {
        foreach (var child in row.Children)
        {
            if (child is HotkeyRecorderControl recorder &&
                CompactShortcut(Core.HotkeyStringFormat.ToDisplayText(recorder.Value))
                    .Contains(keyQuery, StringComparison.OrdinalIgnoreCase))
                return true;
            if (child is Panel panel && ShortcutMatches(panel, keyQuery))
                return true;
        }
        return false;
    }

    private static string CompactShortcut(string text) => string.Concat(text.Where(ch => !char.IsWhiteSpace(ch)));
}
