using System.Collections.Specialized;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Lertaro.Plugins.ClipboardHistory.Capture;

/// <summary>
/// The clipboard write-backs (the Copy action and the paste's clipboard hand): text, text with its
/// captured HTML flavor, file lists and images. Owns the write-suppression scope and runs every
/// write on the monitor's STA pump, so the recorder's sequence-number guard sees exactly what the
/// app itself wrote.
/// </summary>
internal sealed class ClipboardWriter(
    IClipboardNative native,
    ClipboardWriteSuppression suppression,
    Func<Dispatcher?> dispatcher,
    Func<Task> pumpReady)
{
    public Task WriteTextAsync(string text) => WriteAsync(
        () => ClipboardWriteRetry.Execute(() => Clipboard.SetDataObject(text, true)));

    /// <summary>Writes text together with its captured HTML flavor (the keep-format paste/copy for
    /// a Text row with an HTML flavor). A plain-text paste uses <see cref="WriteTextAsync"/>.</summary>
    public Task WriteTextWithHtmlAsync(string text, string html) => WriteAsync(
        () => ClipboardWriteRetry.Execute(() =>
        {
            var data = new DataObject();
            data.SetText(text);
            data.SetData(IClipboardNative.HtmlFormatName, html);
            Clipboard.SetDataObject(data, true);
        }));

    /// <summary>Writes a file path list back to the clipboard (the Copy action for a FilePaths row).</summary>
    public Task WriteFilesAsync(IReadOnlyList<string> paths)
    {
        var list = new StringCollection();
        list.AddRange(paths.ToArray());
        return WriteAsync(() => ClipboardWriteRetry.Execute(() => Clipboard.SetFileDropList(list)));
    }

    /// <summary>Writes an image from disk back to the clipboard (the Copy action for an Image row).</summary>
    public Task WriteImageAsync(string imageFilePath) => WriteAsync(() =>
    {
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.UriSource = new Uri(imageFilePath, UriKind.Absolute);
        bitmap.EndInit();
        bitmap.Freeze();
        ClipboardWriteRetry.Execute(() => Clipboard.SetImage(bitmap));
    });

    private async Task WriteAsync(Action write)
    {
        await pumpReady().ConfigureAwait(false);
        await dispatcher()!.InvokeAsync(() =>
        {
            var before = native.GetClipboardSequenceNumber();
            // Scoped suppression covers the notifications WPF raises reentrantly inside
            // SetDataObject; EndWrite runs even when a busy clipboard throws so the scope can
            // never stay latched. The suppression is the MONITOR'S instance: its update handler
            // consults the same object to skip our own writes.
            suppression.BeginWrite();
            try
            {
                write();
            }
            finally
            {
                var after = native.GetClipboardSequenceNumber();
                native.GetWindowThreadProcessId(native.GetClipboardOwner(), out var ownerProcess);
                if (ownerProcess == Environment.ProcessId) suppression.EndWrite(before, after);
                else suppression.Clear();
            }
        }).Task;
    }
}
