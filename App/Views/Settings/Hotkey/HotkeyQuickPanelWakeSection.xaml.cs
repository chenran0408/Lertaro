namespace Lertaro.App.Views.Settings.Hotkey;

public partial class HotkeyQuickPanelWakeSection : System.Windows.Controls.UserControl
{
    public HotkeyQuickPanelWakeSection()
    {
        InitializeComponent();
        IsVisibleChanged += (_, e) => { if (e.NewValue is false) StopTest(); };
        Unloaded += (_, _) => StopTest();
    }
    private void StopTest()
    {
        if (DataContext is ViewModels.Settings.HotkeySettingsViewModel vm) vm.QuickPanelWake.StopTest();
    }
}
