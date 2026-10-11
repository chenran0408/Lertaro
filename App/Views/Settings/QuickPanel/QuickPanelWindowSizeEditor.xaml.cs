using System.Windows;

namespace Lertaro.App.Views.Settings.QuickPanel;

public partial class QuickPanelWindowSizeEditor : System.Windows.Controls.UserControl
{
    private QuickPanelSizePreviewWindow? _preview;

    public QuickPanelWindowSizeEditor() => InitializeComponent();

    private void OnPreview(object sender, RoutedEventArgs e)
    {
        if (_preview != null)
        {
            _preview.Activate();
            return;
        }
        _preview = new QuickPanelSizePreviewWindow
        {
            DataContext = DataContext,
            Owner = Window.GetWindow(this)
        };
        var owner = _preview.Owner;
        if (owner != null)
        {
            var area = Screen.FromHandle(new System.Windows.Interop.WindowInteropHelper(owner).Handle).WorkingArea;
            var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(owner);
            _preview.MaxWidth = area.Width / dpi.DpiScaleX;
            _preview.MaxHeight = area.Height / dpi.DpiScaleY;
            _preview.WindowStartupLocation = WindowStartupLocation.Manual;
            _preview.Left = area.Left / dpi.DpiScaleX;
            _preview.Top = area.Top / dpi.DpiScaleY;
        }
        _preview.Closed += (_, _) => _preview = null;
        _preview.Show();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e) => _preview?.Close();
}
