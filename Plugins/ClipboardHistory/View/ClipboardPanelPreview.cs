using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace Lertaro.Plugins.ClipboardHistory.View;

/// <summary>
/// Fills the preview pane for the selected row: the full text (wrapping, scrollable), the image at
/// a readable size (decoded capped to 1024 px on the long edge), or the file path list. Refreshes
/// on every selection change; an empty selection clears the pane.
/// </summary>
internal static class ClipboardPanelPreview
{
    private const int MaxImageDecodePixels = 1024;

    public static void Apply(Grid host, TextBlock title, ClipboardEntry? entry, string imagesDirectory)
    {
        host.Children.Clear();
        if (entry == null)
        {
            title.Text = string.Empty;
            return;
        }

        switch (entry.Kind)
        {
            case ClipboardEntryKind.Text:
                title.Text = ClipboardPanelLabels.PreviewTextTitle();
                host.Children.Add(MakeTextBox(entry.Content));
                break;
            case ClipboardEntryKind.Image:
                title.Text = $"{entry.ImageWidth}\u00d7{entry.ImageHeight}";
                var image = MakeImage(Path.Combine(imagesDirectory, entry.ImagePath));
                if (image != null) host.Children.Add(image);
                break;
            case ClipboardEntryKind.FilePaths:
                title.Text = ClipboardPanelLabels.PreviewFilesTitle();
                var list = new StackPanel();
                foreach (var path in entry.PathList)
                    list.Children.Add(new TextBlock
                    {
                        Text = path,
                        FontSize = 11.5,
                        TextWrapping = TextWrapping.Wrap,
                        Margin = new Thickness(0, 2, 0, 2),
                    });
                host.Children.Add(list);
                break;
        }
    }

    private static TextBox MakeTextBox(string content) => new()
    {
        Text = content,
        IsReadOnly = true,
        TextWrapping = TextWrapping.Wrap,
        BorderThickness = new Thickness(0),
        Background = System.Windows.Media.Brushes.Transparent,
        FontSize = 12,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
    };

    private static Image? MakeImage(string filePath)
    {
        if (!File.Exists(filePath)) return null;
        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            // The thumbnail box decodes at 88 px; the preview wants the real thing, capped so a
            // 4K screenshot still decodes fast.
            bitmap.DecodePixelWidth = MaxImageDecodePixels;
            bitmap.UriSource = new Uri(filePath, UriKind.Absolute);
            bitmap.EndInit();
            bitmap.Freeze();
            return new Image
            {
                Source = bitmap,
                Stretch = System.Windows.Media.Stretch.Uniform,
                MaxWidth = MaxImageDecodePixels,
                HorizontalAlignment = HorizontalAlignment.Left,
            };
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or InvalidOperationException)
        {
            return null;
        }
    }
}
