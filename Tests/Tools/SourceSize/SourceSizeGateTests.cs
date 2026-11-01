namespace Lertaro.Tools.Tests;

/// <summary>
/// Runs the source-size gate against throwaway git repositories, one scenario per DataRow.
/// </summary>
[TestClass]
public class SourceSizeGateTests
{
    private const int Limit = 32768;

    /// <summary>
    /// Builds the named scenario's fixture and asserts the gate exits with the expected
    /// code and names the offending file when a failure is expected.
    /// </summary>
    [TestMethod]
    [DataRow("UnderLimitPasses", 0, null)]
    [DataRow("NewOverLimitFails", 1, "Src/Big.cs")]
    [DataRow("BaselineGrowthFails", 1, "Src/Grown.cs")]
    [DataRow("BaselineShrinkPasses", 0, null)]
    [DataRow("DeletedFilePasses", 0, null)]
    [DataRow("VendorExcludedPasses", 0, null)]
    [DataRow("CrlfNormalizedPasses", 0, null)]
    [DataRow("UntrackedOverLimitFails", 1, "Untracked.cs")]
    [DataRow("ScratchDirExcludedPasses", 0, null)]
    [DataRow("UnicodeNameOverLimitFails", 1, "大文件.cs")]
    [DataRow("MissingBaselineFails", 1, "Baseline file not found")]
    public void GateEnforcesScenario(string scenario, int expectedExitCode, string? expectedOutputPart)
    {
        var spec = BuildScenario(scenario);
        using var fixture = SourceSizeFixture.Create(spec);
        var (exitCode, output) = SourceSizeFixture.RunGate(fixture);
        Assert.AreEqual(expectedExitCode, exitCode, output);
        if (expectedOutputPart is not null)
        {
            StringAssert.Contains(output, expectedOutputPart);
        }
    }

    private static ScenarioSpec BuildScenario(string name) => name switch
    {
        "UnderLimitPasses" => new ScenarioSpec([new FixtureFile("Src/Small.cs", "class Small {}\n")], Baseline()),
        "NewOverLimitFails" => new ScenarioSpec([new FixtureFile("Src/Big.cs", new string('a', Limit + 1))], Baseline()),
        "BaselineGrowthFails" => new ScenarioSpec(
            [new FixtureFile("Src/Grown.cs", new string('a', Limit + 200))],
            Baseline("Src/Grown.cs", Limit + 100)),
        "BaselineShrinkPasses" => new ScenarioSpec(
            [new FixtureFile("Src/Shrunk.cs", new string('a', Limit + 10))],
            Baseline("Src/Shrunk.cs", Limit + 100)),
        "DeletedFilePasses" => new ScenarioSpec(
            [new FixtureFile("Src/Gone.cs", new string('a', Limit + 1))],
            Baseline(),
            DeleteWorkingTreePath: "Src/Gone.cs"),
        "VendorExcludedPasses" => new ScenarioSpec([new FixtureFile("Vendor/Widget/Big.cs", new string('a', Limit + 1))], Baseline()),
        "CrlfNormalizedPasses" => new ScenarioSpec(
            [new FixtureFile("Src/Crlf.cs", ToCrlf(Limit / 2))],
            Baseline()),
        "UntrackedOverLimitFails" => new ScenarioSpec(
            [new FixtureFile("Src/Small.cs", "class Small {}\n"), new FixtureFile("Src/Untracked.cs", new string('a', Limit + 1), Staged: false)],
            Baseline()),
        "ScratchDirExcludedPasses" => new ScenarioSpec(
            [new FixtureFile("Src/Small.cs", "class Small {}\n"), new FixtureFile("work/Big.cs", new string('a', Limit + 1), Staged: false)],
            Baseline()),
        "UnicodeNameOverLimitFails" => new ScenarioSpec([new FixtureFile("Src/大文件.cs", new string('a', Limit + 1))], Baseline()),
        "MissingBaselineFails" => new ScenarioSpec([new FixtureFile("Src/Small.cs", "class Small {}\n")], BaselineJson: null),
        _ => throw new ArgumentException($"Unknown scenario '{name}'.", nameof(name)),
    };

    private static string Baseline(string? path = null, int maxBytes = 0)
    {
        if (path is null)
        {
            return "{}";
        }
        return "{\"" + path + "\": " + maxBytes + "}";
    }

    private static string ToCrlf(int lineCount)
    {
        var lines = string.Concat(Enumerable.Repeat("a\n", lineCount));
        return lines.Replace("\n", "\r\n");
    }
}
