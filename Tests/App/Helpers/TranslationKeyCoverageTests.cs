using System.IO;
using System.Text.RegularExpressions;

namespace Lertaro.App.Tests.Helpers;

// Two releases in a row shipped features that referenced translation keys nobody added to the
// JSON (the tray menu's icon-only rows, then the D1 tips rendering as "[Tip_Clipboard_Title]").
// The runtime fallback is "[key]" -- visible, but only to whoever happens to trigger the surface.
// This test closes the class: every key the App code or XAML references must exist in the en-US
// baseline (the completeness test elsewhere pins the other languages against that baseline).
[TestClass]
public sealed class TranslationKeyCoverageTests
{
    private static readonly string RepoRoot = FindRepoRoot();

    [TestMethod]
    public void EveryReferencedTranslationKeyExistsInTheBaseline()
    {
        var baseline = LoadBaselineKeys();
        var missing = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var file in Directory.EnumerateFiles(Path.Combine(RepoRoot, "App"), "*.*", SearchOption.AllDirectories))
        {
            if (!file.EndsWith(".cs", StringComparison.Ordinal) && !file.EndsWith(".xaml", StringComparison.Ordinal)) continue;
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")) continue;

            var source = File.ReadAllText(file);
            foreach (Match match in Regex.Matches(source, @"TranslationManager\.Instance\[\x22([A-Za-z0-9_.]+)\x22\]"))
                if (!baseline.Contains(match.Groups[1].Value)) missing.Add(match.Groups[1].Value);
            foreach (Match match in Regex.Matches(source, @"Path=\[([A-Za-z0-9_.]+)\]"))
                if (!baseline.Contains(match.Groups[1].Value)) missing.Add(match.Groups[1].Value);
        }

        Assert.IsEmpty(missing,
            "these translation keys are referenced by the app but absent from the en-US baseline "
            + "(the UI would render them as [key]): " + string.Join(", ", missing));
    }

    private static SortedSet<string> LoadBaselineKeys()
    {
        var path = Path.Combine(RepoRoot, "Plugins", "CoreExtensions", "Resources", "Translations", "en-US", "App.json");
        var json = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path));
        return new SortedSet<string>(json!.Keys, StringComparer.Ordinal);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Lertaro.slnx")))
            dir = dir.Parent!;
        return dir?.FullName ?? throw new InvalidOperationException("Repo root not found.");
    }
}
