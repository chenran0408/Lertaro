using System.IO;
using System.Security.Cryptography;

namespace Lertaro.Plugins.ClipboardHistory.Capture;

/// <summary>
/// Turns a capture into a store draft, writing image pixels to their content-addressed file first:
/// the file name is the PNG's own SHA-256, so a dedup hit names the same file the store keeps and a
/// rejected insert leaves nothing to clean up.
/// </summary>
internal static class ClipboardImageIngest
{
    public static async Task<ClipboardEntryDraft?> BuildDraftAsync(ClipboardCapture capture, string imagesDirectory)
    {
        if (capture.Kind == ClipboardEntryKind.Text)
            return new ClipboardEntryDraft(ClipboardEntryKind.Text, capture.Text);
        if (capture.Kind == ClipboardEntryKind.FilePaths)
            return new ClipboardEntryDraft(ClipboardEntryKind.FilePaths, string.Join("\n", capture.FilePaths));

        try
        {
            var hashHex = ClipboardStoreSupport.HashToHex(SHA256.HashData(capture.ImagePng));
            var fileName = hashHex + ".png";
            var filePath = Path.Combine(imagesDirectory, fileName);
            if (!File.Exists(filePath))
                await Task.Run(() => File.WriteAllBytes(filePath, capture.ImagePng)).ConfigureAwait(false);
            return new ClipboardEntryDraft(ClipboardEntryKind.Image, string.Empty,
                fileName, capture.ImageWidth, capture.ImageHeight, hashHex);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            PluginSdk.Logger.Log(
                $"[ClipboardHistory] {ex.GetType().Name} (0x{ex.HResult:X8}).", PluginSdk.LogLevel.Error);
            return null;
        }
    }
}
