using System.Windows;
using Lertaro.App.ViewModels.Settings;
using Lertaro.App.ViewModels.Settings.Plugins;
using Lertaro.App.Services;
using System.Windows.Data;
using System.Windows.Threading;
using System.ComponentModel;
using Binding = System.Windows.Data.Binding;

namespace Lertaro.App.Views.Settings;

public partial class TaskbarSettingsSection : System.Windows.Controls.UserControl
{
    private bool _initialized;
    private PluginPreviewSession? _session;
    private bool _previewRequested;
    private PluginInfoViewModel? _plugin;
    private readonly DispatcherTimer _previewTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };

    public TaskbarSettingsSection()
    {
        InitializeComponent();
        Loaded += (_, _) => LoadSettings();
        IsVisibleChanged += (_, _) => LoadSettings();
        _previewTimer.Tick += async (_, _) => { _previewTimer.Stop(); await PreviewAsync(); };
    }

    private void LoadSettings()
    {
        if (_initialized || !IsVisible || !TaskbarOptions.IsExpanded || DataContext is not SettingsViewModel settings) return;
        _initialized = true;
        // Reuse the plugin's staged fields, so editing through either entry point cannot diverge.
        var plugin = settings.Plugins.Plugins.FirstOrDefault(p => p.DllFileName.Equals("Lertaro.Plugins.TaskbarAppearance.dll", StringComparison.OrdinalIgnoreCase));
        if (plugin is not { IsPluginEnabled: true } || plugin.ConfigFields.Count == 0) return;
        _plugin = plugin;
        _session = new PluginPreviewSession(plugin);
        plugin.IsConfigTab = true;
        Fields.DataContext = plugin;
        Fields.Visibility = Visibility.Visible;
        Unavailable.Visibility = Visibility.Collapsed;
        QuickTools.Visibility = Visibility.Visible;
        var opacity = plugin.ConfigFields.FirstOrDefault(f => f.SchemaField.Key == "Opacity");
        if (opacity != null) OpacitySlider.SetBinding(System.Windows.Controls.Primitives.RangeBase.ValueProperty, new Binding("Value") { Source = opacity, Mode = BindingMode.TwoWay });
        foreach (var field in plugin.ConfigFields) field.PropertyChanged += FieldChanged;
        if (Window.GetWindow(this) is { } window) window.Closed += (_, _) =>
        {
            _session.Close();
            _previewTimer.Stop();
            foreach (var field in plugin.ConfigFields) field.PropertyChanged -= FieldChanged;

        };
    }

    private void OptionsExpanded(object sender, RoutedEventArgs args) => LoadSettings();

    private void FieldChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName != "Value" || _session == null || _session.IsClosed || (!_session.IsActive && !_session.IsBusy)) return;
        if (_session.IsBusy) { _previewRequested = true; return; }
        _previewTimer.Stop();
        _previewTimer.Start();
    }

    private void ChooseColor(object sender, RoutedEventArgs args)
    {
        var field = _plugin?.ConfigFields.FirstOrDefault(f => f.SchemaField.Key == "Color");
        if (field == null) return;
        var color = Helpers.Visuals.ColorPickerDialog.Choose(Window.GetWindow(this), field.Value as string ?? "");
        if (color != null) field.Value = color;
    }

    private async void Preview(object sender, RoutedEventArgs args) => await PreviewAsync();

    private async Task PreviewAsync()
    {
        if (_session == null || _session.IsBusy || _session.IsClosed || _plugin?.OnPreviewAsync == null) return;
        QuickTools.IsEnabled = false;
        Fields.IsEnabled = false;
        PreviewStatus.Text = "";
        try
        {
            await _session.PreviewAsync();
        }
        catch (Exception ex) { PreviewStatus.Text = TranslationManager.Instance["Appearance_TaskbarPreviewFailed"] + "\n" + ex.Message; }
        finally
        {
            QuickTools.IsEnabled = true;
            Fields.IsEnabled = true;
            if (!_session.IsClosed && _previewRequested && _session.IsActive) _previewTimer.Start();
            _previewRequested = false;
        }
    }

    private void UndoPreview(object sender, RoutedEventArgs args)
    {
        _previewTimer.Stop();
        _session?.Undo();
        PreviewStatus.Text = "";
    }
}
