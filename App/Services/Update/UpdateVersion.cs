using System.Reflection;
using System.Text.RegularExpressions;

namespace Lertaro.App.Services.Update;

internal static class UpdateVersion
{
    internal static string Current => typeof(App).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
        .InformationalVersion.Split('+')[0] ?? typeof(App).Assembly.GetName().Version!.ToString(3);

    internal static bool IsPreview(string value) => TryParse(value, out _, out var suffix) && suffix.Length > 0;
    internal static bool IsNewer(string candidate, string current) => Compare(candidate, current) is > 0;

    internal static int? Compare(string left, string right)
    {
        if (!TryParse(left, out var a, out var sa) || !TryParse(right, out var b, out var sb)) return null;
        var numeric = a.CompareTo(b);
        if (numeric != 0) return numeric;
        if (sa.Length == 0 || sb.Length == 0) return sa.Length == sb.Length ? 0 : sa.Length == 0 ? 1 : -1;
        for (var i = 0; i < Math.Min(sa.Length, sb.Length); i++)
        {
            var na = ulong.TryParse(sa[i], out var va);
            var nb = ulong.TryParse(sb[i], out var vb);
            var order = na && nb ? va.CompareTo(vb) : na != nb ? na ? -1 : 1 : StringComparer.OrdinalIgnoreCase.Compare(sa[i], sb[i]);
            if (order != 0) return order;
        }
        return sa.Length.CompareTo(sb.Length);
    }

    private static bool TryParse(string? value, out Version version, out string[] suffix)
    {
        version = new Version(0, 0); suffix = [];
        if (string.IsNullOrWhiteSpace(value)) return false;
        var parts = value.Trim().TrimStart('v', 'V').Split('+')[0].Split('-', 2);
        if (!Version.TryParse(parts[0], out var parsed)) return false;
        version = new Version(parsed.Major, parsed.Minor, Math.Max(0, parsed.Build), Math.Max(0, parsed.Revision));
        if (parts.Length == 1) return true;
        if (!Regex.IsMatch(parts[1], @"^[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*$")) return false;
        // Our published tags use beta3 rather than beta.3; compare their counters numerically.
        suffix = Regex.Replace(parts[1], @"^(alpha|beta|rc)(\d+)", "$1.$2", RegexOptions.IgnoreCase).Split('.');
        return true;
    }
}
