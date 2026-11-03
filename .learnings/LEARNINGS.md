# Learnings

Corrections, insights, and knowledge gaps captured during development.


## 2026-10-28: the shell glass preview lifecycle

- A live WPF glass sample can continue background rendering after the capture runner finishes; count and CPU-check only the scratch sample processes and explicitly shut down the sample app after capture. The optical surface also exposes `SetPerformanceSleeping` for idle preview state.

## 2026-10-28: Match the fallback rim to the live glass rim

- User screenshot showed an overly bright white perimeter. The normal material rim had already been reduced, but `DrawWaitingRim` still used alpha 100/75 and a 1.5 DIP stroke while backdrop capture was pending or unavailable. Keep fallback and live edge treatments in the same subdued range.

## [LRN-20261029-001] knowledge_gap

**Logged**: 2026-10-29
**Priority**: medium
**Status**: pending
**Area**: tools

### Summary
A tool missing from PATH may still be available in the D-drive task toolchain.

### Details
The system .NET host exposed runtimes but no SDK. The required SDK and `dotnet-counters.exe` were already installed under D:\Dev\Toolchains; checking only Get-Command missed them. A PID-targeted System.Runtime sample then completed without suspending or restarting the app.

### Suggested Action
Before declaring a .NET diagnostic tool unavailable, search the configured D:\Dev\Toolchains tree and invoke the discovered executable by full path.

### Metadata
- Source: error
- Related Files: .learnings/ERRORS.md
- Tags: windows, dotnet, diagnostics

---
## [LRN-20261029-001] classify Lertaro child processes by command line

**Logged**: 2026-10-29T12:46:00+08:00
**Priority**: medium
**Status**: resolved
**Area**: tools

### Summary
Do not infer a second interactive Lertaro instance from the executable name alone.

### Details
A `Lertaro.App.exe` process launched with `--glass-backdrop-worker <pid>` is the existing app's glass sampling worker, and `Lertaro.Service.exe --hook` is a hook service mode. A process-name-only listing incorrectly suggested duplicate full App/Service instances. Inspect `Win32_Process.CommandLine` before deciding whether a diagnostic launch would collide with the current instance.

### Suggested Action
When enumerating Lertaro runtime roles, record process id, executable, and command-line mode together.

---

## 2026-10-30: GitHub language statistics and mapped compaction files

- GitHub language statistics reflect the default branch, not the latest release tag. Confirm the default branch and languages API before explaining an absent language as a small percentage.
- On Windows, File.Replace cannot consume a memory-mapped source. Validate then close the candidate before replacement. A mapped destination can be renamed to a backup, but retry must not overwrite a backup still owned by the retained mapping after a failed publication open.
