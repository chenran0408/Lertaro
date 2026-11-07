using System.IO;
using System.Text;
using Lertaro.PluginSdk;

namespace Lertaro.App.Helpers;

/// <summary>
/// A2: streams search results to CSV or TXT on a background thread (the UI stays responsive).
/// CSV uses UTF-8 with BOM for Excel compatibility with Chinese text. Export respects the
/// current sort order and any active filters (it receives the already-filtered list).
/// ponytail: string concatenation per row is O(1) here because StreamWriter buffers; if profiling
/// ever shows allocation pressure at 100k+ rows, the upgrade is a pooled StringBuilder.
/// </summary>
internal static class ResultExporter
{
    public static async Task<string> ExportAsync(
        IReadOnlyList<AppSearchResult> results, string filePath, bool asCsv)
    {
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        using var writer = new StreamWriter(filePath, append: false,
            asCsv ? new UTF8Encoding(encoderShouldEmitUTF8Identifier: true) : new UTF8Encoding(false));

        if (asCsv)
        {
            await writer.WriteLineAsync("Name,Path,Type,Size,DateModified").ConfigureAwait(false);
        }

        foreach (var result in results)
        {
            if (result.IsEmptyResult || result.IsSearchSectionHeader) continue;
            if (asCsv)
            {
                var name = CsvEscape(result.Name);
                var path = CsvEscape(result.FullPath);
                var kind = CsvEscape(result.ResultKind);
                var size = result.Metadata.Size > 0 ? result.Metadata.Size.ToString() : "";
                var date = result.Metadata.Modified > DateTime.MinValue
                    ? result.Metadata.Modified.ToString("yyyy-MM-dd HH:mm:ss") : "";
                await writer.WriteLineAsync($"{name},{path},{kind},{size},{date}").ConfigureAwait(false);
            }
            else
            {
                await writer.WriteLineAsync(result.FullPath).ConfigureAwait(false);
            }
        }

        return filePath;
    }

    private static string CsvEscape(string value)
    {
        if (!value.Contains(',') && !value.Contains('"') && !value.Contains('\n') && !value.Contains('\r'))
            return value;
        return '"' + value.Replace("\"", "\"\"") + '"';
    }
}
