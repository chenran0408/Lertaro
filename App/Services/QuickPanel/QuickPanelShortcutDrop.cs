using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using Lertaro.App.Services.ShellIcons;

namespace Lertaro.App.Services.QuickPanel;

internal static class QuickPanelShortcutDrop
{
    // Called on the shell STA worker. Existing links are copied intact to retain arguments and icons.
    internal static void Add(IEnumerable<string> sources, string folder)
    {
        if (!Directory.Exists(folder)) throw new DirectoryNotFoundException(folder);
        foreach (var source in sources.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var isDirectory = Directory.Exists(source);
            if (!File.Exists(source) && !isDirectory) continue;
            var isLink = !isDirectory && (Path.GetExtension(source).Equals(".lnk", StringComparison.OrdinalIgnoreCase)
                || Path.GetExtension(source).Equals(".url", StringComparison.OrdinalIgnoreCase));
            if (isLink && string.Equals(Path.GetDirectoryName(source), folder.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase)) continue;
            var name = isLink ? Path.GetFileName(source) : ShortcutName(source, isDirectory);
            var destination = UniquePath(folder, name);
            if (isLink)
            {
                File.Copy(source, destination, overwrite: false);
                continue;
            }

            object? linkObject = null;
            // Reserve the name without overwriting a file created by another drop or process.
            using (new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { }
            try
            {
                linkObject = new ShellIconNativeMethods.ShellLink();
                var link = (ShellIconNativeMethods.IShellLinkW)linkObject;
                link.SetPath(source);
                link.SetWorkingDirectory(isDirectory ? source : Path.GetDirectoryName(source)!);
                ((IPersistFile)linkObject).Save(destination, true);
            }
            catch
            {
                File.Delete(destination);
                throw;
            }
            finally
            {
                if (linkObject != null) Marshal.FinalReleaseComObject(linkObject);
            }
        }
    }

    internal static string ShortcutName(string source, bool isDirectory)
    {
        var leaf = Path.GetFileName(source.TrimEnd('\\', '/'));
        if (leaf.Length == 0) leaf = Path.GetPathRoot(source) ?? string.Empty;
        var stem = isDirectory ? leaf : Path.GetFileNameWithoutExtension(leaf);
        stem = string.Concat(stem.Where(character => !Path.GetInvalidFileNameChars().Contains(character))).TrimEnd('.', ' ');
        return (stem.Length == 0 ? "Shortcut" : stem) + ".lnk";
    }

    internal static string UniquePath(string folder, string name)
    {
        var path = Path.Combine(folder, name);
        for (var suffix = 2; File.Exists(path) || Directory.Exists(path); suffix++)
            path = Path.Combine(folder, $"{Path.GetFileNameWithoutExtension(name)} ({suffix}){Path.GetExtension(name)}");
        return path;
    }
}
