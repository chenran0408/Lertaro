# Performance Baseline

Measured by `tools/perf-smoke/perf-smoke.ps1` against a Release publish on this machine.
Wall-clock, outside the process — carries ~50-100 ms of launch noise. Regression signal, not a
profiler.

## How to run

```powershell
pwsh tools/perf-smoke/perf-smoke.ps1 -BuildConfig Release -FileCount 10000 -QueryRounds 5
```

Requires the .NET 10 SDK at `D:\dev\Toolchains\dotnet\dotnet.exe` (adjust `-dotnet` if different).
Creates and destroys a temp directory tree; does not touch the installed app.

## Baseline (2026-11-07, v6.6.0-beta7, Release, 125% DPI, single monitor 1920×1080)

| Metric | Value |
|--------|-------|
| Cold start (process → main window) | _TBD — first run to fill_ |
| Test data files | 10,000 |

| Query | Text | Min (ms) | P95 (ms) | Max (ms) |
|-------|------|----------|----------|----------|
| single-char (large result set) | `e` | _TBD_ | _TBD_ | _TBD_ |
| multi-word (moderate) | `alpha beta` | _TBD_ | _TBD_ | _TBD_ |
| extension-filtered | `alpha ext:cs` | _TBD_ | _TBD_ | _TBD_ |

| Incremental index (100 file creates) | _TBD files/s_ |

## Notes

- The single-char query (`e`) is the stress case: it matches thousands of index rows and the
  result set fills the window multiple times. P95 here is the number to watch for regressions.
- The extension-filtered query exercises the A1 filter path (`ext:cs`) — compare against the
  pre-A1 baseline when the filters land.
- Incremental throughput measures filesystem write speed + the USN watcher's reaction time; it is
  a noise-sensitive number and varies with the drive's write cache state.
