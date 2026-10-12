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
            var target = new StringBuilder(32768);
            var arguments = new StringBuilder(32768);
            var directory = new StringBuilder(32768);
            link.GetPath(target, target.Capacity, IntPtr.Zero, 4); // SLGP_RAWPATH: do not resolve the target.
            link.GetArguments(arguments, arguments.Capacity);
            link.GetWorkingDirectory(directory, directory.Capacity);
            if (target.Length > 0)
                result = new(Environment.ExpandEnvironmentVariables(target.ToString()), arguments.ToString(), Environment.ExpandEnvironmentVariables(directory.ToString()));
        }
        catch (Exception ex) when (ex is COMException or IOException or UnauthorizedAccessException or ArgumentException) { }
        finally { if (instance != null) Marshal.FinalReleaseComObject(instance); }
        if (Cache.Count >= 4096) Cache.Clear();
        Cache[path] = (now + 60000, result);
        return result;
    }
}
