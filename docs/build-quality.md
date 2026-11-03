# Build quality rollout

## Shared settings and packages

`Directory.Build.props` sets C# 14, nullable analysis, .NET 10 analyzers and build-time code style checks. Existing projects keep warnings visible. `Directory.Build.targets` enables warnings as errors only for verified projects, matched by full path because production and test projects share names.

`Directory.Packages.props` owns the versions of all 14 directly referenced NuGet packages. The initial migration covers 38 projects (5 production projects and 33 test projects), including ClipboardHistory. Package versions are unchanged. Add a `PackageVersion` there and a versionless `PackageReference` in the consuming project. NuGet rejects inline versions and `VersionOverride`.

the shell glass uses its existing, nearer `Directory.*` files under `Vendor/the shell glass`; it keeps its own build and package policy.

## Warning cleanup

The initial strict projects are `Tools/CompactionBenchmark/CompactionBenchmark.csproj` and `Tests/Tools/SourceSize/SourceSize.csproj`. They generate XML documentation so the existing IDE0005 rule can run. The SDK derives the documentation path early, so enable documentation in `Directory.Build.props` or the project's property group before SDK targets are evaluated.

The initial [warning inventory](build-warning-baseline.md) records the projects actually built during this migration. Other projects still need an inventory when their batch is addressed. Generating documentation for public APIs also introduces CS1591 diagnostics for missing comments; complete those comments before making that project strict.

To add a project to the strict list:

1. Rebuild that project with its normal dependencies and resolve its diagnostics without suppression.
2. Add its full-path condition to `Directory.Build.targets`, and enable documentation early in `Directory.Build.props` if needed.
3. Add the targeted build or test command to `.github/workflows/quality.yml` and confirm a clean rebuild.

For a fresh warning log, run a targeted build, for example:

```powershell
dotnet build Core/Core.csproj -c Release -t:Rebuild '-flp:logfile=artifacts/core-warnings.log;verbosity=normal'
```

## CI and file sizes

The Code quality workflow runs on pull requests and is called by Beta validation before tests and packaging. It checks source sizes, tests the size gate and builds the strict project. NuGet validates central package versions during restore/build.

`Tools/TestSourceSize.ps1` documents the source-size policy. The current ceiling is 32 KiB of UTF-8 C# source after newline normalization. The check includes tracked and non-ignored untracked files, excluding Vendor and the scratch directories named in the script. Existing oversized files have individual ceilings in `Tools/source-size-baseline.json`; they can shrink or disappear. Review baseline changes as exceptions, never automatically regenerate it to accept growth. Lower the recorded ceiling when shrinking an oversized file so the saved space stays protected.

## Subsequent work

1. PluginSdk API generation is configured in [Tools/ApiDocs](../Tools/ApiDocs/README.md), with seven localized manual entries. Retrospective [ADRs](adr/) record the process, privilege and index boundaries; the manual includes a WindowSwitcher walkthrough. Documentation generation is checked before publishing.
2. Add behavior tests around one large class, then extract a cohesive responsibility without changing behavior. Keep each extraction independently reviewable.
3. The PR template now requests a bug reproducer and regression-test evidence. The Core coverage pilot is documented below; collect broader samples before considering a changed-line threshold. A selective mutation-test pilot is also documented below and remains out of CI until its scope can be constrained.

## Coverage and mutation-test pilot

`Tests/Core/Core.csproj` includes Coverlet's VSTest collector for targeted local measurement. Collect a Cobertura report with:

```powershell
dotnet test Tests/Core/Core.csproj -c Release -p:EnableRustIndexStoreBuild=false --filter FullyQualifiedName~ExplorerDialogBoundsTests --collect:"XPlat Code Coverage"
```

The pilot's three tests covered all executable lines in `ExplorerDialogBounds.cs` and 94.4% of its branches. This is a useful report, but one small pure helper is not enough evidence for a changed-line coverage threshold. The collector is not run in the quality workflow yet, because that workflow currently builds narrowly and the beta workflow already runs the Core tests.

Stryker.NET 5.0.0 was evaluated against the same helper. With the current multi-project Core test setup, the CLI mutation glob was not constrained to the selected file: it discovered 26,406 mutants across Core and selected all 2,165 Core tests. The first test run also reported one failing test, while a subsequent direct Core run passed all 2,165. Do not add this to CI until the mutation scope can be reliably constrained and the baseline is stable.

Required branch checks need repository settings to be configured separately. A `fix:` commit touching a test file does not establish that the bug has a useful regression test; review the reproducer and assertion.
