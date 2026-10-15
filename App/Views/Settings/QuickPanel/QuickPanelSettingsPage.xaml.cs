namespace Lertaro.App.Views.Settings.QuickPanel;

// System.Windows.Forms is in scope project-wide (shell interop), so the WPF UserControl is named in
// full rather than imported -- the two are ambiguous otherwise.
public partial class QuickPanelSettingsPage : System.Windows.Controls.UserControl
{
    public QuickPanelSettingsPage() => InitializeComponent();
    private void OpenWakeSettings(object sender, System.Windows.RoutedEventArgs e)
    {
        if (System.Windows.Window.GetWindow(this) is SettingsWindow window)
            window.ActivateSearchResult(new Helpers.SettingsSearchResultItem("", "", "Hotkeys",
                vm => vm.Hotkeys.SelectedTab = "Global", "TabGlobal/QuickPanelWakeSection/RowAddWake"));
    }
}
