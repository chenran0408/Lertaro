using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using Lertaro.App.Services.ShellIcons;

namespace Lertaro.App.ViewModels.Search.Mapping;

internal readonly record struct LaunchShortcut(string Target, string Arguments, string WorkingDirectory);

// Ranking never calls Resolve or touches a remote shortcut/target. A short-lived bounded cache avoids
// reopening every local link on each streamed paint; edits become visible within one minute.
internal static class LaunchShortcutCache
{
    private static readonly ConcurrentDictionary<string, (long Expires, LaunchShortcut? Link)> Cache = new(StringComparer.OrdinalIgnoreCase);

    // A catalog load reads many links on the same worker. Reuse the full-size buffer without sharing it
    // between COM calls on different threads or truncating long paths, arguments and directories.
    [ThreadStatic] private static StringBuilder? _buffer;

    internal static LaunchShortcut? Read(string path)
    {
        if (!Path.IsPathFullyQualified(path) || path.StartsWith(@"\\", StringComparison.Ordinal)) return null;
        var now = Environment.TickCount64;
        if (Cache.TryGetValue(path, out var cached) && cached.Expires > now) return cached.Link;
        object? instance = null;
        LaunchShortcut? result = null;
        try
        {
            instance = new ShellIconNativeMethods.ShellLink();
            ((IPersistFile)instance).Load(path, 0);
            var link = (ShellIconNativeMethods.IShellLinkW)instance;
            var buffer = _buffer ??= new StringBuilder(32768);
            link.GetPath(buffer.Clear(), buffer.Capacity, IntPtr.Zero, 4); // SLGP_RAWPATH: do not resolve the target.
            if (buffer.Length > 0)
            {
                var target = Environment.ExpandEnvironmentVariables(buffer.ToString());
                link.GetArguments(buffer.Clear(), buffer.Capacity);
                var arguments = buffer.ToString();
                link.GetWorkingDirectory(buffer.Clear(), buffer.Capacity);
                result = new(target, arguments, Environment.ExpandEnvironmentVariables(buffer.ToString()));
            }
        }
        catch (Exception ex) when (ex is COMException or IOException or UnauthorizedAccessException or ArgumentException) { }
        finally { if (instance != null) Marshal.FinalReleaseComObject(instance); }
        if (Cache.Count >= 4096) Cache.Clear();
        Cache[path] = (now + 60000, result);
        return result;
    }
}
