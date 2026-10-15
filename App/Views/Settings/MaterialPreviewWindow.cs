using System.Windows;
using System.Windows.Controls;
using Lertaro.App.Helpers.Visuals;
using Lertaro.App.Services;
using Lertaro.App.Views.Controls;

namespace Lertaro.App.Views.Settings;

internal sealed class MaterialPreviewWindow : Window
{
    internal MaterialPreviewWindow(ViewModels.Settings.General.BackgroundSettingsViewModel settings)
    {
        Title = TranslationManager.Instance["Background_MaterialPreview"];
        Width = 500;
        Height = 260;
        MinWidth = 350;
        MinHeight = 180;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var content = new Grid();
        content.Children.Add(new BackgroundSurface());
        var words = new StackPanel { Margin = new Thickness(28), VerticalAlignment = VerticalAlignment.Center };
        var title = new TextBlock { Text = "Lertaro", FontSize = 28, FontWeight = FontWeights.SemiBold };
        title.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimary");
        var hint = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0) };
        hint.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondary");
        hint.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(settings.MaterialHint)) { Source = settings });
        words.Children.Add(title);
        words.Children.Add(hint);
        content.Children.Add(words);
        Content = content;
        NativeMaterialHost.Prepare(this, preview: true);
    }
}
