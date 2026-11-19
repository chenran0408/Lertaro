namespace Lertaro.App.Views.Settings.General;

public partial class GeneralSettingsPage : System.Windows.Controls.UserControl
{
    public GeneralSettingsPage() => InitializeComponent();

    private void OnCategoryChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, sender)) CategoryScroll?.ScrollToTop();
    }
}
