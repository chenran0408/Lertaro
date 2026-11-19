using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Lertaro.App.Services;
using Lertaro.App.ViewModels.Settings.Plugins;
using Lertaro.App.Views.Controls;
using Lertaro.App.Views.Controls.Dialogs;

namespace Lertaro.App.Views.Settings.Plugins;

// Code-behind for FieldRowTemplate.xaml's array master/detail event handlers -- split out of
// PluginConfigWindow.xaml.cs purely to let the recursive field-row template live in this separate
// ResourceDictionary and keep PluginConfigWindow.xaml under the file-length limit; none of these
// depend on PluginConfigWindow's own state.
public partial class PluginConfigFieldRowTemplate : ResourceDictionary
{
    private void ArrayLayout_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!e.WidthChanged || sender is not Grid layout) return;
        var wide = e.NewSize.Width >= 760;
        var detail = layout.Children.OfType<Border>().Single();
        if (wide == (Grid.GetColumn(detail) == 1)) return;

        var master = layout.Children.OfType<Grid>().Single();
        master.Children.OfType<System.Windows.Controls.ListBox>().Single().MaxHeight = wide ? 360 : 180;
        layout.ColumnDefinitions[1].Width = wide ? new GridLength(1.35, GridUnitType.Star) : new GridLength(0);
        Grid.SetRow(detail, wide ? 0 : 1);
        Grid.SetColumn(detail, wide ? 1 : 0);
        detail.Margin = detail.Padding = wide ? new Thickness(16, 0, 0, 0) : new Thickness(0, 10, 0, 0);
        detail.BorderThickness = wide ? new Thickness(1, 0, 0, 0) : new Thickness(0, 1, 0, 0);
    }

    private void ChoosePreset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: PluginConfigFieldViewModel field } button
            || field.SchemaField.DefaultValue is not IEnumerable<object> defaults) return;
        var menu = new System.Windows.Controls.ContextMenu
        {
            PlacementTarget = button,
            Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom
        };
        var fields = field.SchemaField.SubFields!;
        var title = fields.FirstOrDefault(f => f.FieldType == PluginSdk.Abstractions.ConfigFieldType.Text)?.Key;
        var trigger = fields.FirstOrDefault(f => f.IsTriggerWord)?.Key;
        foreach (var preset in defaults.OfType<Dictionary<string, object>>())
        {
            if (!preset.TryGetValue(field.SchemaField.PresetIdentityKey!, out var identity)) continue;
            var item = new System.Windows.Controls.MenuItem { Header = title != null && preset.TryGetValue(title, out var label) ? label : identity };
            if (trigger != null && preset.TryGetValue(trigger, out var word)) item.InputGestureText = word?.ToString();
            item.Click += (_, _) => field.ArraySupport.SelectOrAddPreset(preset);
            menu.Items.Add(item);
        }
        menu.IsOpen = true;
    }

    private void ImportEntry_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: PluginConfigFieldViewModel { SupportsEntryTransfer: true } field } element) return;
        var owner = Window.GetWindow(element);
        try
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "JSON (*.json)|*.json", CheckFileExists = true,
                Title = TranslationManager.Instance["Plugins_EntryImport"]
            };
            if (dialog.ShowDialog(owner) != true) return;
            var warning = field.ImportEntry(PluginConfigEntryTransfer.ReadFile(dialog.FileName));
            if (warning.Length != 0)
                CustomMessageBox.Show(owner, warning, TranslationManager.Instance["Plugins_EntryImport"], MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception ex) when (ex is System.IO.InvalidDataException or System.IO.IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or NotSupportedException)
        {
            ShowEntryError(owner, ex);
        }
    }

    private void ExportEntry_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: PluginConfigFieldViewModel { CanExportEntry: true } field } element) return;
        var owner = Window.GetWindow(element);
        try
        {
            var bytes = field.ExportEntry();
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "JSON (*.json)|*.json", DefaultExt = ".json", AddExtension = true,
                FileName = PluginConfigEntryTransfer.SuggestedFileName(field.SelectedArrayItem!),
                Title = TranslationManager.Instance["Plugins_EntryExport"]
            };
            if (dialog.ShowDialog(owner) == true) PluginConfigEntryTransfer.WriteFile(dialog.FileName, bytes);
        }
        catch (Exception ex) when (ex is System.IO.InvalidDataException or System.IO.IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or NotSupportedException)
        {
            ShowEntryError(owner, ex);
        }
    }

    private static void ShowEntryError(Window? owner, Exception error) => CustomMessageBox.Show(owner,
        PluginConfigEntryTransfer.ErrorMessage(error),
        TranslationManager.Instance["Plugins_EntryErrorTitle"], MessageBoxButton.OK, MessageBoxImage.Error);

    private void InlineEditorHost_EditCompleted(object sender, RoutedEventArgs e)
    {
        if (sender is not InlineEditorHost { DataContext: PluginConfigFieldViewModel { IsIconField: true } field } host
            || SvgIconInputHelper.IsValidPathData(field.Value?.ToString()))
            return;

        field.Value = string.Empty;
        var owner = Window.GetWindow(host);
        var message = TranslationManager.Instance["Plugins_IconConversionError"];
        var caption = TranslationManager.Instance["Plugins_IconConversionErrorTitle"];
        host.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            CustomMessageBox.Show(owner, message, caption, MessageBoxButton.OK, MessageBoxImage.Error)));
    }

    // Brings the newly-added (and newly-selected) row into view when the master list has more
    // items than fit -- otherwise Add silently appends off-screen below the visible scroll area.
    private void ArrayMasterList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is System.Windows.Controls.ListBox { SelectedItem: not null } listBox)
            listBox.ScrollIntoView(listBox.SelectedItem);
    }

    private void ArrayDetailPanel_Loaded(object sender, RoutedEventArgs e) => TriggerWindowResizeToFit(sender);

    private void ArrayDetailPanel_SizeChanged(object sender, SizeChangedEventArgs e) => TriggerWindowResizeToFit(sender);

    private static void TriggerWindowResizeToFit(object sender)
    {
        if (sender is FrameworkElement element && Window.GetWindow(element) is PluginFieldPromptWindow promptWindow)
        {
            promptWindow.ResizeToFit();
        }
    }
}
