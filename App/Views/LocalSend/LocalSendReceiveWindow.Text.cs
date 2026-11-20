using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using Lertaro.App.Services;
using Lertaro.Core.Services.LocalSend;

namespace Lertaro.App.Views.LocalSend;

public partial class LocalSendReceiveWindow
{
    private void BtnCopyText_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            System.Windows.Clipboard.SetText(TxtTextMessage.Text);
            Close();
        }
        catch (ExternalException)
        {
            ShowTextActionError("Settings_LocalSend_CopyFailed");
        }
    }

    private void BtnOpenTextUrl_Click(object sender, RoutedEventArgs e)
    {
        if (!LocalSendTextMessageHelper.TryGetHttpUrl(TxtTextMessage.Text, out var url)) return;
        try
        {
            Process.Start(new ProcessStartInfo(url!.AbsoluteUri) { UseShellExecute = true });
            Close();
        }
        catch (Win32Exception)
        {
            ShowTextActionError("Settings_LocalSend_OpenLinkFailed");
        }
    }

    private void ShowTextActionError(string key)
    {
        TextActionError.Text = TranslationManager.Instance[key];
        TextActionError.Visibility = Visibility.Visible;
    }
}
