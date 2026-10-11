using System.Diagnostics;

namespace Lertaro.App.Services;

internal sealed record RunningProcessInfo(string Name, string WindowTitle, bool HasWindow);

internal static class RunningProcessCatalog
{
    public static IReadOnlyList<RunningProcessInfo> Capture()
    {
        var items = new List<RunningProcessInfo>();
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    var name = process.ProcessName;
                    if (string.IsNullOrWhiteSpace(name)) continue;
                    if (!name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name += ".exe";
                    var hasWindow = process.MainWindowHandle != IntPtr.Zero;
                    items.Add(new(name, hasWindow ? process.MainWindowTitle : string.Empty, hasWindow));
                }
                catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or NotSupportedException)
                {
                    // Processes can exit or deny access between enumeration and reading their window.
                }
            }
        }
        return Normalize(items);
    }

    internal static IReadOnlyList<RunningProcessInfo> Normalize(IEnumerable<RunningProcessInfo> items) =>
        items.Where(x => !string.IsNullOrWhiteSpace(x.Name))
            .GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(x => x.HasWindow)
                .ThenByDescending(x => !string.IsNullOrWhiteSpace(x.WindowTitle)).First())
            .OrderByDescending(x => x.HasWindow)
            .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();

    internal static bool Matches(RunningProcessInfo item, string search, bool showAll) =>
        (showAll || item.HasWindow) &&
        (item.Name.Contains(search.Trim(), StringComparison.OrdinalIgnoreCase) ||
         item.WindowTitle.Contains(search.Trim(), StringComparison.OrdinalIgnoreCase));
}
