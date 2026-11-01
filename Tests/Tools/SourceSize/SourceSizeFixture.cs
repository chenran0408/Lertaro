using System.Diagnostics;
using System.Text;

namespace Lertaro.Tools.Tests;

internal sealed record FixtureFile(string Path, string Content, bool Staged = true);

internal sealed record ScenarioSpec(
    IReadOnlyList<FixtureFile> Files,
    string? BaselineJson,
    string? DeleteWorkingTreePath = null);

// Builds throwaway git repositories in %TEMP% and runs Tools/TestSourceSize.ps1 against
// them; the real repository is never touched.
internal sealed class SourceSizeFixture : IDisposable
{
    private SourceSizeFixture(string root)
    {
        Root = root;
        BaselinePath = Path.Combine(root, "baseline.json");
    }

    public string Root { get; }
    public string BaselinePath { get; }

    public static SourceSizeFixture Create(ScenarioSpec spec)
    {
        var root = Path.Combine(Path.GetTempPath(), "source-size-gate-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var fixture = new SourceSizeFixture(root);
        try
        {
            Git(root, "init");
            Git(root, "config", "core.autocrlf", "false");
            foreach (var file in spec.Files.Where(f => f.Staged))
            {
                WriteFile(root, file);
                Git(root, "add", "--", file.Path);
            }
            if (spec.BaselineJson is { } baselineJson)
            {
                File.WriteAllText(fixture.BaselinePath, baselineJson, new UTF8Encoding(false));
            }
            foreach (var file in spec.Files.Where(f => !f.Staged))
            {
                WriteFile(root, file);
            }
            if (spec.DeleteWorkingTreePath is { } deleted)
            {
                File.Delete(Path.Combine(root, deleted));
            }
            return fixture;
        }
        catch
        {
            fixture.Dispose();
            throw;
        }
    }

    public static (int ExitCode, string Output) RunGate(SourceSizeFixture fixture)
    {
        var psi = new ProcessStartInfo
        {
            FileName = ResolvePowerShell(),
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        psi.ArgumentList.Add("-NoProfile");
        psi.ArgumentList.Add("-NoLogo");
        psi.ArgumentList.Add("-File");
        psi.ArgumentList.Add(Path.Combine(FindRepoRoot(), "Tools", "TestSourceSize.ps1"));
        psi.ArgumentList.Add("-RepoRoot");
        psi.ArgumentList.Add(fixture.Root);
        psi.ArgumentList.Add("-BaselinePath");
        psi.ArgumentList.Add(fixture.BaselinePath);
        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start PowerShell.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        return (process.ExitCode, stdout.GetAwaiter().GetResult() + stderr.GetAwaiter().GetResult());
    }

    public void Dispose()
    {
        var resolved = Path.GetFullPath(Root);
        var temp = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())) + Path.DirectorySeparatorChar;
        if (!Directory.Exists(resolved) || !resolved.StartsWith(temp, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }
        // git stores loose objects read-only; clear the attribute so the delete succeeds.
        foreach (var file in Directory.EnumerateFiles(resolved, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }
        Directory.Delete(resolved, recursive: true);
    }

    private static void WriteFile(string root, FixtureFile file)
    {
        var fullPath = Path.Combine(root, file.Path.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, file.Content, new UTF8Encoding(false));
    }

    private static void Git(string workingDirectory, params string[] arguments)
    {
        var psi = new ProcessStartInfo("git")
        {
            UseShellExecute = false,
            RedirectStandardError = true,
            WorkingDirectory = workingDirectory,
        };
        foreach (var argument in arguments)
        {
            psi.ArgumentList.Add(argument);
        }
        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start git.");
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"git {string.Join(' ', arguments)} failed: {error}");
        }
    }

    private static string ResolvePowerShell()
    {
        var pathVariable = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var directory in pathVariable.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var pwsh = Path.Combine(directory.Trim(), "pwsh.exe");
            if (File.Exists(pwsh))
            {
                return pwsh;
            }
        }
        return Environment.OSVersion.Platform == PlatformID.Win32NT ? "powershell.exe" : "pwsh";
    }

    private static string FindRepoRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Tools", "TestSourceSize.ps1")))
            {
                return directory.FullName;
            }
        }
        throw new InvalidOperationException("Could not locate the repository root from the test output directory.");
    }
}
