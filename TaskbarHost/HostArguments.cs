using System.Globalization;

namespace Lertaro.TaskbarHost;

internal sealed record HostArguments(int ParentId, long ParentStarted, string Engine, string StatusFile)
{
    internal static HostArguments Parse(string[] args)
    {
        if (args.Length != 4 || !int.TryParse(args[0], out var parent) || parent <= 0
            || !long.TryParse(args[1], CultureInfo.InvariantCulture, out var started) || started <= 0
            || !Path.IsPathFullyQualified(args[2]) || !Path.IsPathFullyQualified(args[3])
            || !Path.GetFileName(args[2]).Equals("TranslucentTB.exe", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Invalid taskbar host arguments.");
        return new(parent, started, args[2], args[3]);
    }
}
