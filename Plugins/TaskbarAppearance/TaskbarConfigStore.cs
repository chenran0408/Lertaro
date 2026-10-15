using System.Text;

namespace Lertaro.Plugins.TaskbarAppearance;

internal static class TaskbarConfigStore
{
    public static bool Apply(string path, TaskbarOptions options)
    {
        var marker = path + ".lertaro-managed";
        if (!options.Enabled && !File.Exists(marker)) return false;
        var original = File.Exists(path) ? File.ReadAllText(path) : "";
        // Validate all profiles before creating a backup or changing an external setting.
        var updated = TaskbarConfiguration.Build(original, options);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        if (File.Exists(path) && !File.Exists(path + ".lertaro-backup")) File.Copy(path, path + ".lertaro-backup");
        var temporary = path + ".lertaro-" + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, updated, new UTF8Encoding(false));
            File.Move(temporary, path, overwrite: true);
            if (options.Enabled) File.WriteAllText(marker, "Lertaro taskbar appearance settings");
            else if (File.Exists(marker)) File.Delete(marker);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        return true;
    }
}
