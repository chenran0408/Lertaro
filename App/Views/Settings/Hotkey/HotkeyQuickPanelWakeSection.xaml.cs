namespace Lertaro.App.Views.Settings.Hotkey;

public partial class HotkeyQuickPanelWakeSection : System.Windows.Controls.UserControl
{
    private ViewModels.Settings.QuickPanelWakeSettingsViewModel? _wakeSettings;
    public HotkeyQuickPanelWakeSection()
    {
        InitializeComponent();
        IsVisibleChanged += (_, e) => { if (e.NewValue is false) StopTest(); };
        Loaded += (_, _) => BindStatus();
        DataContextChanged += (_, _) => BindStatus();
        Unloaded += (_, _) =>
        {
            StopTest();
            if (_wakeSettings != null) _wakeSettings.PropertyChanged -= ShowStatus;
            _wakeSettings = null;
        };
    }
    private void BindStatus()
    {
        if (_wakeSettings != null) _wakeSettings.PropertyChanged -= ShowStatus;
        _wakeSettings = (DataContext as ViewModels.Settings.HotkeySettingsViewModel)?.QuickPanelWake;
        if (_wakeSettings != null) _wakeSettings.PropertyChanged += ShowStatus;
    }
    private void ShowStatus(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ViewModels.Settings.QuickPanelWakeSettingsViewModel.TestMessage))
            Dispatcher.BeginInvoke(new Action(() => { if (IsVisible && WakeTestStatus.IsVisible) WakeTestStatus.BringIntoView(); }),
                System.Windows.Threading.DispatcherPriority.ContextIdle);
    }
    private void StopTest()
    {
        if (DataContext is ViewModels.Settings.HotkeySettingsViewModel vm) vm.QuickPanelWake.StopTest();
    }
}
