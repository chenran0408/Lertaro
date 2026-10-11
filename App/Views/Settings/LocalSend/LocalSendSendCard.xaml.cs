using System.Windows;
using Lertaro.App.Helpers.LocalSend;
using Lertaro.App.Services;
using Lertaro.App.ViewModels.LocalSend;
using Lertaro.App.ViewModels.Settings.LocalSend;
using WpfDragEventArgs = System.Windows.DragEventArgs;
using DataFormats = System.Windows.DataFormats;
using DragDropEffects = System.Windows.DragDropEffects;

namespace Lertaro.App.Views.Settings.LocalSend;

public partial class LocalSendSendCard : System.Windows.Controls.UserControl
{
    public LocalSendSendCard() => InitializeComponent();
    private bool CanSend => DataContext is LocalSendSettingsViewModel { IsServiceRunning: true };

    private void SendFiles_Click(object sender, RoutedEventArgs e)
    {
        if (!CanSend) return;
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Multiselect = true,
            Title = TranslationManager.Instance["Settings_LocalSend_ChooseFiles"]
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true)
            LocalSendAppEventHandler.OpenSendWindow(dialog.FileNames, mode: LocalSendSendMode.Items);
    }

    private void SendFolder_Click(object sender, RoutedEventArgs e)
    {
        if (!CanSend) return;
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = TranslationManager.Instance["Settings_LocalSend_ChooseFolder"] };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true)
            LocalSendAppEventHandler.OpenSendWindow([dialog.FolderName], mode: LocalSendSendMode.Items);
    }

    private void SendText_Click(object sender, RoutedEventArgs e)
    {
        if (CanSend) LocalSendAppEventHandler.OpenSendWindow(mode: LocalSendSendMode.Text);
    }

    private void Card_DragOver(object sender, WpfDragEventArgs e)
    {
        e.Effects = GetDropEffect(CanSend, e.Data.GetDataPresent(DataFormats.FileDrop));
        e.Handled = true;
    }

    internal static DragDropEffects GetDropEffect(bool canSend, bool hasFiles) =>
        canSend && hasFiles ? DragDropEffects.Copy : DragDropEffects.None;

    private void Card_Drop(object sender, WpfDragEventArgs e)
    {
        e.Handled = true;
        if (CanSend && e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } paths)
            LocalSendAppEventHandler.OpenSendWindow(paths, mode: LocalSendSendMode.Items);
    }
}
