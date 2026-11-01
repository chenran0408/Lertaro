# Errors

Command failures and integration errors.


## [ERR-20261027-001] core-test-build

**Logged**: 2026-10-27
**Priority**: medium
**Status**: resolved
**Area**: tests

### Summary
Core compile caught a duplicate local variable name in the new double-tap display branch.

### Resolution
Rename the branch-local symbol and rerun targeted validation.

---


## [ERR-20261027-002] doubletap-detector-tests

**Logged**: 2026-10-27
**Priority**: low
**Status**: resolved
**Area**: tests

### Summary
The new detector tests omitted the production namespace import.

### Resolution
Add the explicit using directive and rerun the targeted Core test project.

---


- 2026-10-28: A transient note was first written relative to the scratch directory instead of the repository; the corrected entry belongs under work\Lertaro\.learnings\ERRORS.md.

## [ERR-20261028-001] app-tests-plugin-reference

**Logged**: 2026-10-28
**Priority**: medium
**Status**: unresolved
**Area**: tests

### Summary

The targeted WPF matcher test could not run because building the existing App test project fails in the unrelated CustomCommands plugin with CS0012 for System.Private.Windows.GdiPlus.IImage after restore.

### Next step

Use a targeted build/test path that avoids rebuilding that unrelated plugin, or resolve the project's test dependency configuration if it is necessary for the requested verification.

## [ERR-20261028-002] windows-forms-test-workaround

**Logged**: 2026-10-28
**Priority**: low
**Status**: resolved
**Area**: build

### Summary

Using `UseWindowsForms=true` to expose drawing references caused WPF/WinForms type ambiguities in PluginSdk (`UserControl`, `Panel`).

### Resolution

Avoid enabling WinForms globally; use a test path that references only App/Core or add the narrow missing drawing reference at the responsible project if required.

## [ERR-20261028-003] preview-node-package-resolution

**Logged**: 2026-10-28
**Priority**: low
**Status**: resolved
**Area**: preview

### Summary

System Node.js could not resolve Playwright for the temporary WPF preview runner.

### Resolution

Use the Codex workspace Node.js executable and its bundled package directory via NODE_PATH.

## [ERR-20261028-004] powershell-reserved-host-variable

**Logged**: 2026-10-28
**Priority**: low
**Status**: resolved
**Area**: tooling

### Summary

A scratch file-copy command used `$host` as a variable name. PowerShell treats it as the read-only automatic `$Host` variable, so the command stopped before copying any files.

### Resolution

Use a distinct name such as `$hostDir`; check both source and destination remain within the task's `work` directory before copying.

## [ERR-20261028-005] isolated-glassdesk-diagnostic-path

**Logged**: 2026-10-28
**Priority**: low
**Status**: resolved
**Area**: runtime-diagnostics

### Summary

The isolated GlassDesk capture script expected a diagnostics JSON at an output path, but the app did not create that file there. The runner exited with an error; its `finally` block sent WM_CLOSE and waited for process exit.

### Resolution

The app exports `preview-backdrop-diagnostic.json` under its isolated `Data` directory. The runner was pointed there, and the provider phases were successfully recovered.

## [ERR-20261028-007] computer-use-app-approval-timeout

**Logged**: 2026-10-28
**Priority**: low
**Status**: unresolved
**Area**: ui-automation

### Summary

Computer Use timed out while requesting approval to launch the isolated GlassDesk preview. A fresh app-list query confirmed it did not launch.

### Resolution

No repeat launch was attempted in this turn; the UI runner path needs an available approval before it can provide an outside-shell comparison.

## [ERR-20261028-006] preview-close-mainwindow-noop

**Logged**: 2026-10-28
**Priority**: medium
**Status**: resolved
**Area**: runtime-diagnostics

### Summary

The isolated GlassDesk preview had no usable `Process.CloseMainWindow()` route; the runner timed out waiting for exit and intentionally did not force-terminate it.

### Resolution

The exact isolated PID was already gone on the next process check, so no HWND close or force termination was needed. Treat the previous timeout as delayed managed cleanup; verify process state before attempting another close.

## [ERR-20261028-001] git-diff-from-task-root

**Logged**: 2026-10-28T15:34:38+08:00
**Priority**: low
**Status**: resolved
**Area**: tests

### Summary
`git diff --check` was invoked from the task container directory instead of the repository root.

### Error
Git returned `Not a git repository` and printed usage text; no source files were changed by the failed command.

### Context
The preceding `Add-Content` targeted the task-level `work/PROGRESS.md` intentionally. The command then invoked Git without `-C work/Lertaro`.

### Suggested Fix
Use the repository as the explicit working directory or pass `git -C work/Lertaro` for repository checks.

### Metadata
- Reproducible: yes
- Related Files: work/PROGRESS.md

---

## 2026-10-28: Measurement script lookup path

**Summary**: A source read looked for `Run-LertaroCurrentSample.ps1` under `work/glassdesk-baseline`; it is under `work/measurements`. No files were changed by the failed read. Corrected the lookup to use the actual path.

## [ERR-20261028-008] dotnet-sdk-unavailable

**Logged**: 2026-10-28
**Priority**: low
**Status**: unresolved
**Area**: build

### Summary

The targeted App build could not start because the system `dotnet` command has no installed SDK. `D:\Dev\Toolchains` has no SDK installation.

### Next step

Run the targeted build on a machine or environment with the .NET SDK installed.

## [ERR-20261029-001] codebase-memory transport unavailable

**Logged**: 2026-10-29 Asia/Shanghai
**Issue**: The codebase-memory MCP calls returned Transport closed during the performance audit. Continue with repository source inspection and delegated local audits; retry graph access only if needed.


## [ERR-20261029-002] PowerShell condition passed as a command parameter

**Logged**: 2026-10-29 Asia/Shanghai
**Issue**: A download-preflight command placed `-or` inside the `Test-Path` argument list, so PowerShell stopped before starting the GitHub download.
**Resolution**: Parenthesize each command expression before applying `-or`, then run `gh release download` from the repository directory so GitHub CLI can resolve the remote.

## [ERR-20261029-003] PowerShell path array bound as a single literal path

**Logged**: 2026-10-29 Asia/Shanghai
**Issue**: Passing a hashtable `.Values` collection directly to `Get-Item -LiteralPath` bound the values as one space-joined path. The assets had already been downloaded, hash-checked, and moved; only the final display command failed.
**Resolution**: Iterate each path separately for metadata and SHA-256 verification.

## [ERR-20261029-004] PowerShell pipeline after foreach block did not parse

**Logged**: 2026-10-29 Asia/Shanghai
**Issue**: A follow-up verification command attached `| Format-Table` to a `foreach` statement block; parsing failed before any commands in that block ran.
**Resolution**: Collect per-file result objects in a list, then format the list in a separate statement.

## [ERR-20261029-005] Codebase-memory MCP transport closed during Rust audit

**Logged**: 2026-10-29 Asia/Shanghai
**Issue**: `mcp__codebase_memory_mcp__list_projects` again returned Transport closed while three read-only Rust index audits were underway.
**Resolution**: Continue with agent/source inspection; the storage, watcher, and query audits provided cited file/function paths. Retry graph calls only if they become available and materially help.

## [ERR-20261029-001] memory-baseline-powershell-parser

**Logged**: 2026-10-29
**Priority**: low
**Status**: resolved
**Area**: tests

### Summary
A short process-sampling PowerShell command used a pipeline after a foreach statement block, which PowerShell rejected at parse time; no samples were collected.

### Resolution
Collect deltas into a separate variable and format them afterward.
A follow-up counter-mapping query repeated the same foreach-to-pipeline parse mistake; collect the loop output before formatting.
WPR profile-detail probes were issued concurrently; WPR is a single-instance control tool, so the later probes returned Duplicate instance. Continue WPR operations serially.

## [ERR-20261029-002] wpr-dotnet-systemwide-trace-size

**Logged**: 2026-10-29
**Priority**: medium
**Status**: resolved
**Area**: tests

### Summary
The built-in WPR DotNET verbose profile traced all processes, not only Lertaro, and produced a roughly 992 MiB ETL for a 15-second sample.

### Resolution
The recording stopped successfully and did not stop or modify Lertaro. Do not use the built-in system-wide verbose profile for memory sampling; inspect once for relevant GC data, then remove the local trace if it cannot answer the question. Prefer a process-targeted diagnostic source.

## [ERR-20261029-003] wpa-dotnet-trace-export

**Logged**: 2026-10-29
**Priority**: low
**Status**: unresolved
**Area**: tests

### Summary
WPA exporter 10.0.19041.1 could not process the trace made by WPR 10.0.26100; it reported a missing per-user presets file and failed on a .NET runtime event.

### Next step
Do not retry this exporter unless a compatible WPA version is available. The ETL remains in task work because file deletion was rejected by the shell policy.


## [ERR-20261029-004] codebase-memory-list-projects-transport

**Logged**: 2026-10-29
**Priority**: low
**Status**: unresolved
**Area**: tools

### Summary
The codebase-memory MCP call to list indexed projects failed before returning data.

### Error
```
tool call error: tool call failed for codebase-memory-mcp/list_projects: Transport closed
```

### Context
- Operation: list_projects(detail=identity, limit=50)
- Environment: Windows desktop task, repository indexing available as a configured MCP tool

### Suggested Fix
Retry once after the service reconnects; if it remains unavailable, use targeted source reads and state the graph coverage limitation.

---

## [ERR-20261029-005] exec-invalid-workdir

**Logged**: 2026-10-29
**Priority**: low
**Status**: resolved
**Area**: tests

### Summary
A parallel diff inspection failed because one PowerShell working-directory path omitted the task folder segment.

### Error
```
CreateProcess rejected: directory name is invalid (os error 267)
```

### Resolution
Corrected all workdir values to the checked-out repository path; subsequent status and diff reads succeeded.

---

### Follow-up (2026-10-29)
Retrying `list_projects` and calling `check_index_coverage` also returned `Transport closed`; structural review continued by reading the exact changed files and their call sites. The graph coverage status remains unknown.

## [ERR-20261029-006] guessed-rust-source-path

**Logged**: 2026-10-29
**Priority**: low
**Status**: resolved
**Area**: tests

### Summary
A source inspection command guessed Rust module paths that are not present in this crate and printed file-not-found errors.

### Resolution
Use `rg --files Core/NativeIndexStore` to identify actual paths; no source files were changed by the failed read.

---

## [ERR-20261029-007] system-dotnet-sdk-not-on-path

**Logged**: 2026-10-29
**Priority**: low
**Status**: resolved
**Area**: tests

### Summary
The system `dotnet` host has runtimes but no SDK, so a subagent's targeted test command could not start.

### Error
```
No .NET SDKs were found.
```

### Context
- The approved task toolchain already exists on D: at `D:\Dev\Toolchains\dotnet\dotnet.exe`.
- That executable reports SDK 10.0.100.

### Resolution
Invoke the existing D-drive executable by its full path for builds and tests; no installation is needed.

---

## [ERR-20261029-008] dotnet-counters-wrong-path

**Logged**: 2026-10-29
**Priority**: low
**Status**: resolved
**Area**: tools

### Summary
The first attempt to invoke the existing dotnet-counters tool used a guessed directory under D:\Dev and failed because the executable is under dotnet-tools.

### Error
```
The term 'D:\Dev\Toolchains\dotnet-10.0.100\dotnet-counters.exe' is not recognized.
```

### Resolution
Located the executable with `rg --files D:\Dev\Toolchains -g dotnet-counters.exe`; the correct path is `D:\Dev\Toolchains\dotnet-tools\dotnet-counters.exe`. PID-targeted runtime counters then completed successfully.

---

## [ERR-20261029-009] rg-nonexistent-build-config-paths

**Logged**: 2026-10-29
**Priority**: low
**Status**: resolved
**Area**: tools

### Summary
A targeted ripgrep command included Directory.Build.props and Directory.Build.targets paths that do not exist in this repository, causing rg to return file-not-found diagnostics.

### Resolution
The App project itself declares the relevant GC settings; inspection continued against App/App.csproj and the installed runtimeconfig. No files were changed by the failed search.

---

## [ERR-20261029-010] exec-script-template-literal-parse

**Logged**: 2026-10-29
**Priority**: low
**Status**: resolved
**Area**: tools

### Summary
A PowerShell plan-update command was embedded in a JavaScript template literal while the text itself contained Markdown backticks. The JavaScript parser rejected the tool script before any command ran.

### Resolution
Build shell command text from a plain string array and PowerShell here-string; avoid template literal delimiters in embedded Markdown.

---

## [ERR-20261029-011] self-improvement-skill-path

**Logged**: 2026-10-29
**Priority**: low
**Status**: resolved
**Area**: tools

### Summary
The skill catalog short name was used to guess the filesystem directory, but the installed folder is self-improving-agent.

### Resolution
Resolve the exact path from the skill roots/catalog before reading skill files. No project files or running processes were changed by the failed lookup.

## [ERR-20261029-012] powershell-foreach-pipeline-parse

**Logged**: 2026-10-29
**Priority**: low
**Status**: resolved
**Area**: tools

### Summary
A read-only ZIP inspection script piped directly from a foreach statement and failed PowerShell parsing before opening either archive.

### Resolution
Assign the foreach output to a variable, then pipe that variable to Format-List. The corrected script confirmed both portable archives contain the native index DLL and 56 plugin DLL entries.

## [ERR-20261029-013] powershell-zip-entry-extract-method

**Logged**: 2026-10-29
**Priority**: low
**Status**: resolved
**Area**: tools

### Summary
A local validation helper called ZipArchiveEntry.ExtractToFile as an instance method; PowerShell could not resolve the extension method, so the copy stopped before creating the target file.

### Resolution
Use ZipArchiveEntry.Open() and an explicit FileStream CopyTo, disposing both streams. Verify the target does not already exist before writing.

## 2026-10-29 — Plan append quoting
A functions.exec JavaScript template literal failed to parse because embedded Markdown/PowerShell used raw backticks. Neither failed invocation executed the shell command or changed the plan. Use a normal quoted JS string or encode embedded backticks before composing shell writes.

## [ERR-20261029-014] dotnet-gcdump-version-not-found

**Logged**: 2026-10-29
**Priority**: low
**Status**: resolved
**Area**: tools

### Summary
A guessed `dotnet-gcdump` tool version (`10.0.0`) was not available on NuGet; installation stopped before changing the tool path.

### Resolution
Query the NuGet tool feed for the available package version before installing; keep the tool under `D:\Dev\Toolchains\dotnet-tools` and use the existing D-drive package cache.

## [ERR-20261029-015] process-counter-path-translation

**Logged**: 2026-10-29T18:40:00+08:00
**Priority**: low
**Status**: resolved
**Area**: tools

### Summary
A manually constructed per-process performance-counter path was rejected by Get-Counter.

### Error
`Get-Counter` could not translate one or more supplied counter paths.

### Context
- Tried a PID's explicit Lertaro.App instance with Working Set - Private, Private Bytes, processor, and I/O counters.
- No time-series sample was produced; earlier wildcard Process counters were successfully enumerated.

### Suggested Fix
Use Get-Counter -ListSet Process paths or wildcard instance paths, then filter the resulting samples by the enumerated Lertaro.App instance/PID.

### Metadata
- Reproducible: unknown
- Related Files: .learnings/ERRORS.md

## [ERR-20261029-016] system-dotnet-sdk-missing

**Logged**: 2026-10-29T18:43:00+08:00
**Priority**: low
**Status**: pending
**Area**: tests

### Summary
A targeted App test did not start because the invoked system dotnet host could not locate any SDK.

### Error
The .NET CLI reported that no SDKs were found and that `test` was not a managed application.

### Context
- Invoked `C:\Program Files\dotnet\dotnet.exe test` with task-local DOTNET_CLI_HOME and the D-drive NuGet cache.
- No test case executed and no source change resulted.

### Suggested Fix
Use the SDK-host path already provisioned under D:\Dev\Toolchains and confirm `dotnet --list-sdks` before rerunning.

### Metadata
- Reproducible: yes
- Related Files: Tests/App/App.csproj

## [ERR-20261029-017] guessed-dotnet-sdk-path

**Logged**: 2026-10-29T18:44:00+08:00
**Priority**: low
**Status**: pending
**Area**: tests

### Summary
A guessed D-drive SDK executable path did not exist.

### Context
- `D:\Dev\Toolchains\dotnet-10.0.100` exists as a directory but contains no `dotnet.exe` at its root.
- The follow-up `--list-sdks` command therefore did not execute.

### Suggested Fix
Locate the provisioned dotnet executable by enumerating the existing D-drive toolchain tree, then query its SDK list.

### Metadata
- Reproducible: yes
- Related Files: Tests/App/App.csproj

## [ERR-20261029-018] app-test-suite-has-10-failures

**Logged**: 2026-10-29T07:48:00+08:00
**Priority**: high
**Status**: resolved
**Area**: tests

### Summary
The local full Release App suite completed with 10 failures; the quiet logger omitted individual test names.

### Context
- 2,352 passed, 10 failed, 0 skipped out of 2,362.
- Run used the D-drive .NET 10 SDK and `EnableRustIndexStoreBuild=false` because this machine lacks the native MSVC linker.
- No package or release was started.

### Suggested Fix
Read the test host result/log artifacts or rerun only the failed tests with detailed console output; classify failures before repeating the full suite.

### Metadata
- Reproducible: unknown
- Related Files: Tests/App/App.csproj

### Investigation update (2026-10-29)

All 10 failures are `System.DllNotFoundException` for `lertaro_index_store` from P/Invoke tests, not assertion failures or failures in the memory candidate. This run used `EnableRustIndexStoreBuild=false`, so the native library was not produced in the test output. Before claiming the App suite is green, verify whether an available x64 DLL matches the unchanged Rust ABI and rerun; otherwise rely on the Beta validation matrix that builds the native library from source.

## [ERR-20261029-019] test-rid-assets-missing

**Logged**: 2026-10-29T07:50:00+08:00
**Priority**: low
**Status**: resolved
**Area**: tests

### Summary
A retry using `-r win-x64 --no-restore` stopped before test execution because the restored assets do not include the win-x64 target.

### Resolution
Use the existing x64 native test artifact in the default Release test output (the same method used in prior local validation), or restore the RID-specific assets before using `-r win-x64`. No source or test files changed.

### Resolution
- **Resolved**: 2026-10-29T07:57:00+08:00
- **Notes**: The first run lacked the native DLL in the default test output. After placing the existing 137,216-byte x64 test DLL in that output, the full App Release suite passed 2,362/2,362. Its Rust source commit matches the current native source; source-built x64/ARM64 validation remains in CI run 40790770324.## [ERR-20261029-001] powershell-window-enumeration

**Logged**: 2026-10-29T08:19:00+08:00
**Priority**: low
**Status**: pending
**Area**: infra

### Summary
Read-only window enumeration failed because the local `$pid` variable collided with PowerShell's built-in read-only `$PID` variable.

### Context
- Attempted to enumerate visible windows for Lertaro PID 528 via User32 in PowerShell.
- No application process or repository source was changed; the output was discarded.

### Suggested Fix
Use a non-reserved variable name such as `$ownerProcessId` in PowerShell scripts.

---
## [ERR-20261029-002] dotnet-dump-working-set-perturbation

**Logged**: 2026-10-29T08:30:00+08:00
**Priority**: medium
**Status**: pending
**Area**: infra

### Summary
A heap dump of the live Lertaro process was followed by a large private-working-set increase, likely from pages made resident during diagnostic reads.

### Context
- Before the full heap dump, an agent measured App private working set near 305 MB.
- After the dump, `\Process(lertaro.app)\Working Set - Private` read about 690 MB while Private Bytes remained near 745 MB. The change is diagnostic-contaminated and not an allocation or optimization baseline.
- The dump and gcdump files were deleted after type/root analysis. No app source or running process was changed or stopped.

### Suggested Fix
Use counters and existing app gauges first. If a full dump is essential, record pre/post private working set and report the perturbation; avoid repeating full dumps on a user's live process.

---
## [ERR-20261029-003] powershell-dotnet-home-cleanup-blocked

**Logged**: 2026-10-29T08:45:00+08:00
**Priority**: low
**Status**: pending
**Area**: infra

### Summary
PowerShell blocked recursive deletion of the task-created .NET CLI home after the focused test completed.

### Context
- The CLI home was created under the repository solely for the targeted `dotnet test` run.
- A Remove-Item command first resolved the exact target and checked that it was inside the repository, but command policy rejected it before execution.
- No source or running application was changed. The temporary directory remains untracked.

### Suggested Fix
Set DOTNET_CLI_HOME to an existing D-drive task work directory when running the test, or leave the small temporary directory for workspace cleanup.

---
## [ERR-20261029-004] dotnet-test-native-linker-missing

**Logged**: 2026-10-29T08:55:00+08:00
**Priority**: low
**Status**: resolved
**Area**: tests

### Summary
The first targeted App test build attempted a native Rust rebuild and failed because MSVC `link.exe` is not installed.

### Context
- `dotnet test Tests/App/App.csproj --no-restore --filter FullyQualifiedName~HiddenWindow_ReleasesAllRendererArraysAndRebuildsWhenShown` exited before running tests.
- Re-running the same target with `-p:EnableRustIndexStoreBuild=false` avoided the native rebuild and the focused test passed using existing test outputs.

### Suggested Fix
For managed-only App tests that do not invoke the Rust index store, pass `-p:EnableRustIndexStoreBuild=false` when the MSVC linker is unavailable.

---

## [ERR-20261029-005] invalid-wildcard-pdh-family-sample

**Logged**: 2026-10-29T09:24:00+08:00
**Priority**: low
**Status**: resolved
**Area**: tests

### Summary
A process-family PerfProc sample produced invalid wildcard instances, and the follow-up instance parser used an invalid regular-expression escape.

### Context
- A wildcard `Get-Counter '\Process(*)\ID Process'` query returned an invalid-sample warning.
- A script then repeatedly parsed the counter path with an invalid `\P` regex escape, so its output was discarded. Previously captured PID-specific App samples were valid and unaffected.

### Suggested Fix
Use known per-process counter instance names or construct a PID-to-instance map from valid samples; do not use results from wildcard samples with nonzero Status.

---

## [ERR-20261029-006] codebase-memory-mcp-transport-closed

**Logged**: 2026-10-29T09:25:00+08:00
**Priority**: low
**Status**: unresolved
**Area**: infra

### Summary
The codebase-memory MCP transport closed during two project-list requests.

### Context
- The installed skill's graph-first workflow was selected for the glass-render call-path audit.
- `list_projects` returned `Transport closed` twice. Runtime sampled stacks and exact-path source searches were used instead; no graph completeness claim was made.

### Suggested Fix
Retry the MCP index/session when it is available; continue bounded source reads for the active rendering and capture methods in the meantime.

---

## [ERR-20261029-007] dotnet-sdk-not-on-path

**Logged**: 2026-10-29T09:27:00+08:00
**Priority**: low
**Status**: resolved
**Area**: infra

### Summary
The Release build command resolved a .NET host without an installed SDK.

### Context
- `dotnet --version` and `dotnet build` reported that no SDKs were found.
- The required SDK is expected under `D:\Dev`; the initial invocation used PATH rather than its explicit installation.
- The existing .NET SDK `10.0.100` was found at `D:\Dev\Toolchains\dotnet\dotnet.exe`; the Release App build then succeeded with zero warnings/errors.

### Suggested Fix
Locate the existing SDK under `D:\Dev` and invoke its `dotnet.exe` directly; avoid downloading a duplicate toolchain.

---

## [ERR-20261029-008] git-fetch-did-not-create-tracking-ref

**Logged**: 2026-10-29T09:30:00+08:00
**Priority**: low
**Status**: resolved
**Area**: infra

### Summary
Fetching a named branch without an explicit destination updated FETCH_HEAD but did not create the expected remote-tracking ref.

### Context
- `git fetch origin codex/v5.10.1-input-performance-glass --tags` fetched successfully, but a subsequent `origin/codex/v5.10.1-input-performance-glass` lookup failed because that ref did not exist locally.

### Suggested Fix
Use an explicit refspec to update the remote-tracking branch before comparing local and remote branch heads.

---

## [ERR-20261029-009] gh-download-outside-repository

**Logged**: 2026-10-29T09:34:00+08:00
**Priority**: low
**Status**: resolved
**Area**: infra

### Summary
GitHub CLI could not locate repository context while downloading a CI artifact from the task folder.

### Context
- `gh run download` was invoked outside the Git checkout and returned “not a git repository.” No file was downloaded or modified.

### Suggested Fix
Run from the repository or pass `--repo chenran0408/Lertaro` explicitly.

---

## [ERR-20261029-010] xperf-profile-event-unsupported

**Logged**: 2026-10-29T10:06:00+08:00
**Priority**: low
**Status**: resolved
**Area**: infra

### Summary
The installed Windows Performance Toolkit xperf consumer could not process the sampled CPU ETL because it rejected a newer .NET Runtime event schema.

### Context
- The ETL file outputs/metrics/beta9-settings-cpu-8s.etl has a misleading filename; its header spans 30.69655 seconds, with no lost events.
- Both xperf profile commands failed on a .NET Runtime event with HRESULT 0x80070032. The ETL itself was valid.
- TraceEvent 4.0.0 converted and parsed it successfully, recovering JIT method names and sampled stacks. See the performance report for the attribution and limits.

### Suggested Fix
For this ETL schema, use TraceEvent-compatible parsing rather than retrying the installed older WPA/xperf consumer.

---
## [ERR-20261029-011] github-release-asset-download-eof

**Logged**: 2026-10-29T10:44:00+08:00
**Priority**: low
**Status**: unresolved
**Area**: infra

### Summary
A GitHub CLI download of the Beta10 x64 installer failed with an EOF while following the signed release-asset URL.

### Context
- The public release and asset metadata are available, including GitHub's SHA-256 digest.
- `gh release download` failed during the binary transfer; no checksum comparison was made locally in this attempt.
- Do not copy or expose the signed temporary download URL from the CLI error output.

### Suggested Fix
Retry once from the existing public release link, then compare the downloaded file hash to the release digest. If the transfer still fails, rely on GitHub's published digest and passing CI package/install smoke evidence.

---

## [ERR-20261029-001] PowerShell process-counter sampling

**Logged**: 2026-10-29T10:53:00+08:00
**Priority**: low
**Status**: resolved
**Area**: infra

### Summary
A wildcard process-counter query mixed duplicate executable instances and used PowerShell's read-only `$PID` automatic variable as a local variable.

### Context
- The invalid query output was discarded and was not used for performance claims.
- The follow-up query maps `Win32_PerfRawData_PerfProc_Process` rows by `IDProcess` and reads `WorkingSetPrivate` separately from `PrivateBytes`.

### Suggested Fix
Bind process samples by numeric process ID and avoid PowerShell automatic variable names such as `$PID`.

---


## 2026-10-29 10:58 CST：Beta10 内存口径复核与下一候选

- 当前替换安装版为 `5.10.2-beta.10+36e36de3bec54b8057af94f6a1e6a1669b15418f`；Settings 窗口打开、玻璃效果开启。按 PID 读取 `Win32_PerfRawData_PerfProc_Process.WorkingSetPrivate` 五次，数值稳定：
  - App PID 43028：私有工作集 **112.0 MB**，Private Bytes **343.0 MB**。
  - Glass capture helper PID 23180：**58.8 / 106.8 MB**。
  - Service PID 23188：**20.5 / 34.2 MB**；Hook PID 22840：**13.8 / 19.6 MB**；TaskbarHost PID 13680：**4.2 / 6.8 MB**。
  - 五进程私有工作集约 **209.3 MB**，Private Bytes 合计约 **510.4 MB**。这是同一进程族的 Private Working Set / committed bytes 汇总；不能与总 Working Set 或 GC heap committed 再相加。
- 用户给出的 Listary 约 **50 MB** 是目标参考。本次同一机器只测到了 Lertaro，主 App 当前私有工作集约 112 MB，进程族约 209 MB；尚无同场景 Listary 样本，两个口径也需按同一种进程范围比较。
- 5 秒只读 System.Runtime counters：GC live heap 约 **21.8 MiB**，其中 LOH **17.4 MiB**；GC committed 约 **94.4 MiB**（不是 live 对象大小）。主 App Private Bytes 约343 MB，GC live heap约为其6.4%。SearchableItemCache gauges 为0；ShellIconHelper gauge与 LiquidGlassSurface 的运行态 renderer buffers没有从这个生产实例读出。此次没有 heap dump，也未关闭/重启 App。结论：当前数字不能把主进程较大的非 GC 余量归因给某个玻璃数组；下一项内存改动先做源代码归因，并只在找到可以实测的保留对象/缓存后实施。
- Beta11 CPU 候选（尚未提交/发布）：
  - `AndroidLiquidGlassRenderer` 缓存 Green/Alpha 共用的像素采样分解；同机 Release 320k 像素基准 p50 **13.57 → 12.78 ms（快5.8%）**，p95中位数 **13.92 → 13.26 ms**，像素 SHA-256不变，0 B/frame，renderer专用测试 **6/6** 通过。
  - WGC 紧密行距 readback 将逐行复制合为一次；1920×1080 isolated Release测试测得中位数 **11.60 → 8.75 ms（1.33倍）**，紧密与填充行距逐字节一致，`GlassDesk.UI` Release构建通过，隔离测试 **1/1**。
  - 以旧 WPR采样占比估算，两项全进程 CPU 收益上限约 **3.3%**；该值是跨实验外推，不是新 Beta实测。修改文件为 `Vendor/GlassDesk/src/GlassDesk.UI/Controls/AndroidLiquidGlassRenderer.cs`、`Vendor/GlassDesk/src/GlassDesk.UI/CaptureCompatibility/WgcWindowCapture.cs` 和新增 readback 测试。整个 App 测试项目本机仍受 Rust 原生依赖缺少 `link.exe` 阻挡；候选完整测试/安装 smoke 需由 Beta validation CI确认。
- 用户已允许替换安装。下一步完成内存源代码归因并复核候选变更，推送后等待双架构测试/安装 smoke，再发布并替换安装 Beta；当前只运行 Beta10，Beta11尚未发布，性能目标未完成。
@ | Add-Content -LiteralPath 'D:\CODEX\Tasks\2026-10-27\new-chat-3\outputs\Lertaro-performance-optimization-plan-2026-10-29.md'
@'

## [ERR-20261029-002] ripgrep PowerShell path glob

**Logged**: 2026-10-29T10:58:00+08:00
**Priority**: low
**Status**: resolved
**Area**: infra

### Summary
A PowerShell `rg` invocation passed shell-style root globs and a missing `scripts` directory as literal paths.

### Context
- No source was modified; the version search was rerun with existing explicit paths.

### Suggested Fix
Pass directories that exist and use ripgrep's `--glob` option for file filters under PowerShell.

---


### 2026-10-29 11:04 CST：Beta11 验证中

- 候选已提交并推送到 `codex/v5.10.1-input-performance-glass`：`5a3355e0`；Beta validation run **40809922982** 已创建，当前排队。发布前要求 x64/ARM64 App 测试、双架构打包与安装/升级 smoke 通过；通过后将发布 `v5.10.2-beta.11` 并按用户授权替换安装。
- VMMap 已从 Microsoft 官方 Sysinternals 下载并验证数字签名，但首次命令行运行显示必须接受其许可；没有接受，也没有生成快照。已向用户询问是否授权使用。若未获授权，继续使用现有 Windows/.NET 只读计数与源码归因，明确标出无法区分的内存区域。
@ | Add-Content -LiteralPath 'D:\CODEX\Tasks\2026-10-27\new-chat-3\outputs\Lertaro-performance-optimization-plan-2026-10-29.md'
@'

## [ERR-20261029-003] VMMap first-run license prompt

**Logged**: 2026-10-29T11:04:00+08:00
**Priority**: low
**Status**: awaiting user authorization
**Area**: infra

### Summary
The signed VMMap tool requires an explicit first-run license acceptance before capturing process memory regions.

### Context
- The official Microsoft Sysinternals archive and executable signatures verified successfully.
- No license was accepted and no memory snapshot was created; the user was asked for authorization.

### Suggested Fix
If authorized, accept the license and capture only the current App and glass-worker region summaries. Otherwise continue with existing non-dump counters and document the attribution limit.

---


### 2026-10-29 11:35 CST：Beta11 实际 CPU 与 VMMap 内存分解

- Beta11 已运行；Settings/Appearance 玻璃窗口打开。WMI raw process counters 在冷启动后窗口打开约1分钟采样两轮：主 App 单核占用 **95.17% → 102.07%**（折合20核整机 **4.76% → 5.10%**）；玻璃 capture worker **23.37% → 23.52% 单核**（整机约1.18%）。beta10旧样本主 App为9.44%单核、worker1.21%，但Beta10样本的桌面场景/窗口热身不能确认和当前完全相同，暂不判为回退或收益。
- WPR CPU trace 实际跨度 **60.0秒**（ETL 2.59 GB；转换 ETLX 1.71 GB）。主 App 60,379个SampledProfile样本中 **21,854** 直接落在 `AndroidLiquidGlassRenderer.Render`（**36.2%**）；capture worker inclusive stack中的主要路径样本为 `BackdropCaptureWorker.RunIfRequested` 6,743、`SharedDesktopSceneCapture.TryRead` 2,893、`PersistentWindowCapture.ReadOnce` 2,349、`WgcWindowCapture.PixelReadback.ReadPixels` 1,985、`NativeFrameResize.ResizeCore/ResizeInto`各1,750。inclusive样本会在一个栈的多个层级重复计数，不能相加成CPU百分比。Beta11 renderer仍是主 App CPU 热点，frame capture worker是第二热点。
- 用户授权接受Sysinternals许可后，VMMap对Beta11主 App PID50180和当时的glass worker PID48376采集快照。VMMap按类别列出进程commit与工作集；Microsoft说明见[VMMap文档](https://learn.microsoft.com/en-us/sysinternals/downloads/vmmap)。
  - 主 App：Total Private WS **202,376 K（约197.6 MiB）**；Managed Heap Private WS **124,560 K（约121.6 MiB）**；Heap **40,480 K（约39.5 MiB）**；Private Data **31,984 K（约31.2 MiB）**；Image Private WS **4,268 K（约4.2 MiB）**。
  - Glass worker：Total Private WS **70,716 K（约69.1 MiB）**；Managed Heap **25,308 K（约24.7 MiB）**；Heap **19,600 K（约19.1 MiB）**；Image Private WS **4,696 K（约4.6 MiB）**。
  - 快照后WMI样本为主 App **195.4 MB PWS / 378.5 MB Private Bytes**，worker **62.0 / 109.3 MB**；Service **21.5 MB PWS**，Hook **7.5 MB**，TaskbarHost **5.2 MB**，五进程PWS和约 **291.6 MB**。VMMap与WMI快照时间稍有差异；Private Bytes、总WS、PWS和VMMap分类工作集不能互相相加。
- Beta11只读.NET counters：最近一次GC快照heap各代合计 **22.55 MiB**、LOH **19.53 MiB**；5秒内Gen0约增加3次，Gen1/Gen2不变。分配差分约 **10–13 MB/s**，是短时间窗口。SearchableItemCache gauges仍为0；ShellIcon gauges未出现；生产实例无Surface级统计。GC live/last-collection heap与VMMap Managed Heap Private WS属于不同口径，不意味着121.6MiB都是活对象。
- 下一项已有可验证候选：`AndroidLiquidGlassRenderer.Sample` 每像素32 B（8个float），VMMap确认managed heap private WS为当前主进程最大类别。Luna正在尝试将map压到不超过16 B/pixel；只有多个参数/背景逐字节输出不变且Release性能不退化才保留。若不通过则放弃压缩，另找测量明确的对象。
@ | Add-Content -LiteralPath 'D:\CODEX\Tasks\2026-10-27\new-chat-3\outputs\Lertaro-performance-optimization-plan-2026-10-29.md'
@'

## [ERR-20261029-005] scratch learning log path

**Logged**: 2026-10-29T11:35:00+08:00
**Priority**: low
**Status**: resolved
**Area**: infra

### Summary
A diagnostic note append used the task work directory instead of the repository's existing `.learnings` directory.

### Context
- The append did not modify a file; the intended error log is under `work\Lertaro\.learnings\ERRORS.md`.
- The notes were corrected to use an explicit absolute path.

### Suggested Fix
Use the repo path for skill logs and the task `work` path for temporary diagnostic projects.

---

## [ERR-20261029-006] dotnet SDK selection

**Logged**: 2026-10-29T11:35:00+08:00
**Priority**: low
**Status**: resolved
**Area**: infra

### Summary
The default `dotnet.exe` host had no SDK; the first ETL parser invocation exited before running.

### Context
- .NET SDK 10.0.100 was found at `D:\Dev\Toolchains\dotnet\dotnet.exe`.
- The parser then ran successfully from the D-drive SDK and existing D-drive task cache.

### Suggested Fix
Invoke the installed D-drive SDK by absolute path for task tooling.

---
## [ERR-20261029-007] codebase-memory MCP transport

**Logged**: 2026-10-29T11:41:09+08:00
**Priority**: low
**Status**: pending
**Area**: infra

### Summary
The codebase-memory MCP transport closed during project listing and status lookup.

### Error
Transport closed was returned for both calls.

### Context
- Tried list_projects and index_status as required at the start of structural exploration.
- Used targeted source searches and file reads as the documented fallback; no graph-based absence or exhaustive claims were made.

### Suggested Fix
Retry the graph service in a later task if graph-backed structural coverage is needed.

---
## [ERR-20261029-008] RenderBreakdown scratch benchmark

**Logged**: 2026-10-29T11:50:54+08:00
**Priority**: low
**Status**: resolved
**Area**: tests

### Summary
The temporary renderer/filter phase benchmark could not produce valid measurements and was stopped under the user's no-new-code instruction.

### Error
- Release harness compile failed because System.Diagnostics was missing.
- Static review found it reflected _filtered before the first Render had initialized it, and a lambda wrapper could contaminate timing/allocation samples.

### Context
- Scratch path: D:\CODEX\Tasks\2026-10-27\new-chat-3\work\RenderBreakdown.
- No scratch fix, product source edit, test edit, or new ETL capture was made after the issue was found.

### Suggested Fix
If renderer subphase measurements are needed after discussion, design a valid paired Release measurement before running it; do not use this attempt's timing.

---
## [ERR-20261029-009] powershell process sampling script

**Logged**: 2026-10-29T12:30:00+08:00
**Priority**: low
**Status**: resolved
**Area**: infra

### Summary
A read-only process sampling command failed to parse before it could collect any data.

### Error
PowerShell reported a parser error near the output formatting pipeline.

### Context
- The command attempted two read-only process snapshots separated by 10 seconds.
- No process was modified, and no sampling result was produced.

### Suggested Fix
For multi-process sampling, collect snapshots into typed objects and format the final rows in a separate statement.

---

## [ERR-20261029-010] App build during concurrent vendor edit

**Logged**: 2026-10-29T12:30:00+08:00
**Priority**: medium
**Status**: pending
**Area**: tests

### Summary
A parallel App test build stopped on two GlassDesk vendor compiler errors while a first-display implementation was being edited.

### Error
- `BackdropRecovery.cs:48`: unresolved `DisplayService`.
- `LiquidGlassSurface.cs:1303`: duplicate `MaterialSource` named argument.

### Context
- The audit agent did not modify or revert these files.
- Other build/test agents were asked to pause App builds until the GlassDesk owner diagnoses and repairs them.

### Suggested Fix
Inspect the concurrent GlassDesk diff, correct only the responsible change, then rerun the bounded App build and report whether it passes.

---
## [ERR-20261029-011] live sample output directory

**Logged**: 2026-10-29T12:25:43+08:00
**Priority**: low
**Status**: resolved
**Area**: infra

### Summary
The first multi-interval process sample did not create its CSV because its requested output directory did not exist; a later lookup also used a repository-relative path for a task-level file.

### Context
- No sample data was written by the failed attempt.
- The successful replacement explicitly created `D:\CODEX\Tasks\2026-10-27\new-chat-3\work\metrics\live-sampling` and produced a two-interval CSV there.

### Suggested Fix
Keep task-level metric artifacts under the task work tree and use explicit absolute paths when reading them from the repository checkout.

---
## [ERR-20261029-012] repo path and scratch cleanup

**Logged**: 2026-10-29T12:31:00+08:00
**Priority**: low
**Status**: pending
**Area**: infra

### Summary
A read-only test-file search used an invalid working directory, and a later exact-path scratch cleanup was rejected before execution.

### Context
- The search was rerun from the correct repository path.
- The 2.5 MB generated `work\metrics\rust-index-benchmark` directory was not removed; no source or user data was changed. It is an untracked Luna scratch directory and is not required for the product build.

### Suggested Fix
Use the verified repository working directory for searches. Leave the scratch directory in place unless safe cleanup can be authorized and completed without broadening the target.

---
## [ERR-20261029-013] VMMap CSV mixed summary and region rows

**Logged**: 2026-10-29T12:32:00+08:00
**Priority**: low
**Status**: resolved
**Area**: tests

### Summary
A quick VMMap CSV aggregation attempted to cast address/detail rows and a repeated header as numeric memory totals.

### Error
PowerShell conversion reported non-numeric values such as `Size` and `40 bytes`.

### Context
- The CSV contains both summary categories and individual address-region rows.
- No source or process state changed; the output was partial and is not used as a conclusion.

### Suggested Fix
Restrict attribution summaries to alphabetic VMMap category rows and keep raw values unless the CSV column is numeric.

---
## [ERR-20261029-014] renderer diff review working directory

**Logged**: 2026-10-29T12:33:00+08:00
**Priority**: low
**Status**: resolved
**Area**: infra

### Summary
A read-only `git diff` review was run from the task root instead of the repository and failed with “Not a git repository.”

### Context
- The renderer agent independently reported the source diff empty after reverting its candidate.
- No file was modified by the failed command.

### Suggested Fix
Use `git -C D:\CODEX\Tasks\2026-10-27\new-chat-3\work\Lertaro` when viewing tracked changes from the task root.

---
## [ERR-20261029-015] CUA getApp argument shape

**Logged**: 2026-10-29T12:45:00+08:00
**Priority**: low
**Status**: resolved
**Area**: infra

### Summary
The CUA `getApp` call rejected a display-name string and stated that it requires `{ windowId }` from `listApps()` or `listWindows()`.

### Context
- No window was activated or changed.
- Initial `getState()` had supplied the Lertaro window id; subsequent inspection should use the required object shape.

### Suggested Fix
Use the CUA API's `listWindows()`/`listApps()` result and pass `{ windowId }` to `getApp`.

---

## [ERR-20261029-016] dotnet executable path typo

**Logged**: 2026-10-29
**Priority**: low
**Status**: resolved
**Area**: infra

### Summary
The first Release build attempt referenced a nonexistent versioned dotnet folder.

### Context
- Invoked `D:\Dev\Toolchains\dotnet-10.0.100\dotnet.exe`, which does not exist.
- Existing SDK runtime is at `D:\Dev\Toolchains\dotnet\dotnet.exe`.

### Suggested Fix
Resolve the executable inside the existing D drive toolchain directory before invoking builds.

---

## [ERR-20261029-017] Release build missing MSVC linker

**Logged**: 2026-10-29
**Priority**: medium
**Status**: pending
**Area**: infra

### Summary
App Release build reached the native index store but could not invoke the configured Windows MSVC linker.

### Error
`linker link.exe not found` while compiling `Core/NativeIndexStore` for `x86_64-pc-windows-msvc`.

### Context
- `dotnet build App/App.csproj -c Release` using the existing D-drive .NET SDK.
- .NET projects restored and GlassDesk dependencies compiled before the native Rust build failed.
- No app C# or XAML compile diagnostic was emitted before this prerequisite failure.

### Suggested Fix
Run from a Visual Studio Build Tools developer environment with the C++ build tools installed, or install the required MSVC linker under the approved D-drive toolchain location if the installer supports it.

---

## [ERR-20261029-018] targeted Release tests include background resource failures

**Logged**: 2026-10-29
**Priority**: medium
**Status**: pending
**Area**: tests

### Summary
The filtered Release App test run had two failures in the shared background continuity test; the selected QuickSearch, plugin page, and LocalSend tests passed.

### Error
`LaunchPanelAndSettingsCardRevealOneSharedBackgroundAndRestoreTheme` expected a `LinearGradientBrush` for a settings card background but got null, in both Light and Dark cases.

### Context
- Ran `dotnet test Tests/App/App.csproj -c Release -p:EnableRustIndexStoreBuild=false` filtered to QuickSearchLaunchPanelHeightCalculatorTests, PluginPagePerformanceInvariantTests, LocalSendReceiveWindowHelperTests, and BackgroundContinuityTests.
- Result: 15 passed, 2 failed, 0 skipped. The two failures are the two theme cases of the same background test.
- Failure concerns a shared glass fill assertion, outside the text-size XAML edits in this task.

### Suggested Fix
Review the current settings card style resource and test assumptions in the main integration task; do not attribute this failure to the text-size edits without further evidence.

---
## [ERR-20261029-016] guessed App test path

**Logged**: 2026-10-29T12:47:00+08:00
**Priority**: low
**Status**: resolved
**Area**: tests

### Summary
A direct `Get-Content` lookup used the wrong location for BackgroundContinuityTests.

### Context
- The file is under `Tests/App/Views/Controls`, not `Tests/App/GlassDesk`.
- No source changed from the failed lookup.

### Suggested Fix
Use `rg --files Tests/App | rg BackgroundContinuity` to locate the test before opening it.

---
## [ERR-20261029-019] isolated QuickPanel preview did not reach a window

**Logged**: 2026-10-29T13:24:09+08:00
**Priority**: medium
**Status**: pending
**Area**: tests

### Summary
The local --glass-preview QuickPanel path stayed alive without a main window and produced no new diagnostic report during a bounded run.

### Context
- Started the exact task-owned work/metrics/glass-firstframe-analysis/artifacts/bin/App/release/Lertaro.App.exe with --glass-preview --glass-diagnostics for four seconds.
- CloseMainWindow() returned false; process inspection confirmed the expected isolated executable and arguments, with MainWindowHandle=0.
- Stopped only that verified test PID; no installed App/Service process was touched. The previous Settings preview run succeeded and wrote its diagnostics under the task-local Data directory.

### Suggested Fix
Inspect why the preview's QuickPanel branch does not complete before retrying; do not count the Settings preview as QuickPanel validation.

---

## [ERR-20261029-020] skill directory alias differs from catalog name

**Logged**: 2026-10-29T13:24:09+08:00
**Priority**: low
**Status**: resolved
**Area**: infra

### Summary
The self-improvement skill was initially read from the display-name directory instead of its actual self-improving-agent directory.

### Context
- The failed lookup used C:\Users\28892\.agents\skills\self-improvement\SKILL.md.
- The correct file was then read from C:\Users\28892\.agents\skills\self-improving-agent\SKILL.md.
- No repo or user data changed from the failed lookup.

### Suggested Fix
Resolve catalog entries to their exact skill folder before reading.

---
## [ERR-20261029-021] test command exited successfully without running tests

**Logged**: 2026-10-29T13:30:30+08:00
**Priority**: medium
**Status**: resolved
**Area**: tests

### Summary
A filtered Core test command using --artifacts-path work/tests-core returned exit code 0 after restore only, with no build or test summary.

### Context
- dotnet test Tests/Core/Core.csproj -c Release --artifacts-path work/tests-core --filter ... -p:EnableRustIndexStoreBuild=false restored Core and PluginSdk but produced no test assembly/result.
- Re-running the same filter without --artifacts-path executed the existing test project normally: 11 passed, 0 failed.
- No source or user data was changed by the first invocation.

### Suggested Fix
For these existing MSTest projects, confirm the test count in output; if artifact-path invocation skips the target, use the repository's standard bin/obj paths on D: and don't count exit code alone as a passing test run.

---

## [ERR-20261029-001] isolated-search-diagnostic-setup

**Logged**: 2026-10-29
**Priority**: low
**Status**: pending
**Area**: tooling

### Summary
The first isolated .NET probe build passed a malformed intermediate-output property through PowerShell, causing MSBuild to form an empty-root exclude glob (`/**`). Two later PowerShell summaries also failed because loop output was piped directly after a `foreach` statement.

### Resolution
Keep diagnostic `bin`/`obj` under the task work directory using SDK defaults. In PowerShell, assign loop output to a variable before piping or write each result directly. No user process or data was affected.

---

## [ERR-20261029-002] app-build-project-and-linker

**Logged**: 2026-10-29
**Priority**: medium
**Status**: open
**Area**: tooling

### Summary
The first targeted App build used a guessed project filename and stopped before build. The corrected App.csproj build reached Core but failed because the Rust MSVC target could not find `link.exe`.

### Resolution
Locate the project with `rg --files -g '*.csproj'`; then check whether the D-drive toolchain environment supplies the Visual C++ linker or use an existing compiled Core reference for an App-only compile. No App/Service process or index data was touched.

## [ERR-20261029-003] innosetup-download-link

**Logged**: 2026-10-29
**Priority**: low
**Status**: resolved
**Area**: tooling

### Summary
The guessed official Inno Setup 7.1.0 x64 download URL returned 404 before any file was downloaded or executed.

### Resolution
Use the exact asset link from the official downloads page, then verify its Authenticode publisher before running it. No installer was executed from the failed request.

## [ERR-20261029-004] isolated-app-publish-duplicate-files

**Logged**: 2026-10-29
**Priority**: low
**Status**: resolved
**Area**: tooling

### Summary
A task-staged App publish failed with NETSDK1152 because GlassDesk.UI and the SDK reference pack publish the same Windows SDK reference assemblies.

### Resolution
Use the repository's established ErrorOnDuplicatePublishOutputFiles=false publish property for these combined App outputs. No packaged or installed files were changed.

## [ERR-20261029-005] installer-stage-language-layout

**Logged**: 2026-10-29
**Priority**: low
**Status**: resolved
**Area**: tooling

### Summary
The first isolated Inno compile could not find Languages/en-US.iss because PowerShell copied the language files into the staging Installer root.

### Resolution
Copy that source folder explicitly into the matching staged Languages directory. The compile stopped before producing an installer; repository outputs and installed processes were unaffected.

## [ERR-20261029-006] isolated-reachability-probe-build

**Logged**: 2026-10-29
**Priority**: low
**Status**: resolved
**Area**: tooling

### Summary
The first isolated reachability-gate probe did not compile because System.IO was not included for Path references.

### Resolution
Add the explicit System.IO import and rerun the scratch probe. No product or user files were changed.

## [ERR-20261029-001] GitHub API EOF

**Logged**: 2026-10-29T00:00:00+08:00
**Area**: infra

gh run view 40892133391 failed with an unexpected EOF while querying the GitHub Actions API; local validation continued.

## [ERR-20261030-LINK] dotnet_test_native_linker

**Logged**: 2026-10-30T00:00:00+08:00
**Priority**: high
**Status**: pending
**Area**: tests

### Summary
App test build could not compile the Rust native index because the MSVC linker was unavailable.

### Error
```text
linker `link.exe` not found
cargo build --locked --release --target x86_64-pc-windows-msvc exited with code 101
```

### Context
- Ran the App test project with the bundled .NET 10 SDK and `--no-restore`.
- Managed .NET projects began compiling, but `Core` invokes the Rust native build.

### Suggested Fix
Use a Visual Studio Build Tools environment that provides the MSVC linker before running App tests.

### Metadata
- Reproducible: yes
- Related Files: Core/Core.csproj

---
## [ERR-20261030-RUST-LINK] cargo_test_msvc_linker

**Logged**: 2026-10-30T00:00:00Z
**Priority**: medium
**Status**: pending
**Area**: tests

### Summary
Local Rust tests cannot link because the MSVC linker is not available in the current shell.

### Error
`link.exe not found`

### Context
- Command: `cargo test --manifest-path Core/NativeIndexStore/Cargo.toml`
- The Rust MSVC toolchain is installed under `D:\Dev\Toolchains`, but Visual C++ build tools are not loaded into this shell.

### Suggested Fix
Run the test from a Visual Studio developer environment or use the repository's CI workflow, which already provides the MSVC linker.

### Metadata
- Reproducible: yes
- Related Files: Core/NativeIndexStore/Cargo.toml

---
## [ERR-20261030-RUST-ABI] stale_native_test_binary

**Logged**: 2026-10-30T00:00:00Z
**Priority**: medium
**Status**: pending
**Area**: tests

### Summary
Core tests reached the new P/Invoke but the copied native DLL did not contain the new export.

### Error
`EntryPointNotFoundException: lertaro_record_store_clone`

### Context
- The test command intentionally used `-p:EnableRustIndexStoreBuild=false`, so it reused the previous native binary.
- The managed code now requires the new `lertaro_record_store_clone` export.

### Suggested Fix
Build the Rust MSVC library first, then run Core tests with the rebuilt native artifact.

### Metadata
- Reproducible: yes
- Related Files: Core/NativeIndexStore/src/store/api_mutation.rs, Core/Indexer/NativeRecordStoreInterop.cs

---
## [ERR-20261030-RUST-GNU-TARGET] missing_gnu_std

**Logged**: 2026-10-30T00:00:00Z
**Priority**: low
**Status**: pending
**Area**: tests

### Summary
The fallback GNU target is not installed locally, so it cannot be used to run Rust tests without downloading a target component.

### Error
`can't find crate for std; the x86_64-pc-windows-gnu target may not be installed`

### Context
- Command: `cargo test --manifest-path Core/NativeIndexStore/Cargo.toml --target x86_64-pc-windows-gnu`
- The repository's configured native target is MSVC.

### Suggested Fix
Use CI/Visual Studio Build Tools for the supported MSVC test, or install the GNU target only as a separate local diagnostic environment.

### Metadata
- Reproducible: yes
- Related Files: Core/NativeIndexStore/Cargo.toml

---
## [ERR-20261030-RUST-ABI2] stale_native_test_binary

**Logged**: 2026-10-30T00:00:00Z
**Priority**: medium
**Status**: resolved
**Area**: tests

### Summary
App tests initially loaded the previous native DLL after adding the direct name-view export.

### Error
`EntryPointNotFoundException: lertaro_record_store_name_view`

### Context
- Managed App tests were rebuilt with `EnableRustIndexStoreBuild=false`.
- The rebuilt GNU native DLL had been copied to the Core test output but not the App test output.

### Resolution
- **Resolved**: 2026-10-30T00:00:00Z
- **Notes**: Copied the rebuilt native DLL to the App test output and reran the full App suite.

---
## [ERR-20261030-GH1] gh_run_watch_default_repository

**Logged**: 2026-10-30T00:00:00-07:00
**Priority**: low
**Status**: pending
**Area**: infra

### Summary
`gh run watch` did not infer the repository even though `gh run view` accepted the run ID.

### Error
```text
X No default remote repository has been set.
```

### Context
- Operation: `gh run watch 40926654379 --exit-status`
- Repository: `chenran0408/Lertaro`
- The run ID was inspected successfully with `gh run view`.

### Suggested Fix
Pass `--repo chenran0408/Lertaro` to `gh run watch` and related commands when no GitHub CLI default repository is configured.

### Metadata
- Reproducible: yes
- Related Files: none

---

## [ERR-20261030-WD2] wrong_worktree_path

**Logged**: 2026-10-30T00:00:00-07:00
**Priority**: low
**Status**: pending
**Area**: tools

### Summary
A source inspection command used the old task path instead of the active Lertaro worktree.

### Error
The requested Core paths were not found.

### Context
- Active worktree: D:\\CODEX\\Tasks\\2026-10-27\\new-chat-3\\work\\Lertaro
- No repository files were modified by the failed command.

---

## [ERR-20261030-PACKAGE]

**Logged**: 2026-10-30
**Priority**: medium
**Status**: resolved
**Area**: infra

### Summary
The release package script's hard-coded Inno Setup path did not match the installed 7.1.1 toolchain path.

### Error
`make.local.bat` execution was blocked before starting because the temporary command wrapper was rejected by the command policy; the underlying build-script path mismatch was then confirmed separately.

### Context
- Repository build script checks `D:\Dev\Toolchains\InnoSetup7\ISCC.exe`.
- Installed compiler is at `D:\Dev\Toolchains\InnoSetup7.1.1\ISCC.exe`.

### Suggested Fix
Use a short temporary script or invoke the package steps directly; consider adding the installed toolchain path to local build discovery if this environment is supported.

### Metadata
- Reproducible: yes
- Related Files: make.bat

### Resolution
- **Resolved**: 2026-10-30
- **Notes**: Added the installed `D:\Dev\Toolchains\InnoSetup7.1.1\ISCC.exe` path to the build script and verified the x64 installer and portable package.


## [ERR-20261030-001] cargo-toolchain-path

**Logged**: 2026-10-30
**Priority**: low
**Status**: resolved
**Area**: tests

### Summary
The Rust test command used a nonexistent `D:\Dev\Toolchains\cargo\cargo.exe` path.

### Error
```
The term 'D:\Dev\Toolchains\cargo\cargo.exe' is not recognized as the name of a cmdlet.
```

### Context
- The repository task environment already exposes the Rust toolchain through the installed toolchain/runtime configuration.
- The requested command did not reach Rust or .NET tests.

### Suggested Fix
Use the available `cargo` command after setting `CARGO_HOME`, or locate the executable under `D:\Dev\Toolchains\Rust` before invoking it.

### Metadata
- Reproducible: yes
- Related Files: Core/NativeIndexStore/Cargo.toml

---

## [ERR-20261030-002] stale-native-test-dll

**Logged**: 2026-10-30
**Priority**: medium
**Status**: resolved
**Area**: tests

### Summary
The Core test run loaded an older `lertaro_index_store.dll` that did not contain the new record-store name-view entry point.

### Error
```
System.EntryPointNotFoundException: Unable to find an entry point named 'lertaro_record_store_name_view'
```

### Context
- The test command explicitly disabled the MSBuild Rust build, so the native DLL was not refreshed.
- The managed code had already been rebuilt against the newer interop surface.

### Suggested Fix
Build the native Rust library for the requested RID first, then copy that exact DLL to the test output before running tests.

### Metadata
- Reproducible: yes
- Related Files: Core/NativeIndexStore/src/lib.rs, Core/Indexer/NativeRecordStoreInterop.cs

---

## [ERR-20261030-003] native-build-manifest-path

**Logged**: 2026-10-30
**Priority**: low
**Status**: resolved
**Area**: tests

### Summary
The native release build was invoked from the repository root without its manifest path.

### Error
```
error: could not find `Cargo.toml` in ...\Lertaro or any parent directory
```

### Context
- The Rust manifest is under `Core\NativeIndexStore`.

### Suggested Fix
Pass `--manifest-path Core/NativeIndexStore/Cargo.toml` for root-level builds.

### Metadata
- Reproducible: yes
- Related Files: Core/NativeIndexStore/Cargo.toml

---

## [ERR-20261030-004] app-test-no-restore-assets

**Logged**: 2026-10-30
**Priority**: low
**Status**: resolved
**Area**: tests

### Summary
The App test project had no restored `win-x64` target in its assets file, so `--no-restore` could not build it.

### Error
```
NETSDK1047: project.assets.json has no target for net10.0-windows/win-x64
```

### Context
- Core tests had already been restored for the RID, but App had not.

### Suggested Fix
Run the App test with restore enabled, using the configured local NuGet cache.

### Metadata
- Reproducible: yes
- Related Files: Tests/App/App.csproj

---

## [ERR-20261030-005] make-dotnet-sdk-path

**Logged**: 2026-10-30
**Priority**: medium
**Status**: resolved
**Area**: build

### Summary
The packaging batch selected the system .NET host, which had no SDK, instead of the task's D: drive SDK.

### Error
```
The command could not be loaded ... No .NET SDKs were found.
```

### Context
- `make.bat` invokes `dotnet` through PATH.
- The usable SDK is installed under `D:\Dev\Toolchains\dotnet`.
- The batch continued far enough to create an invalid partial package, which will be replaced by the corrected build.

### Suggested Fix
Prepend `D:\Dev\Toolchains\dotnet` to PATH before invoking the packaging script and validate the resulting package contents.

### Metadata
- Reproducible: yes
- Related Files: make.bat

---

## [ERR-20261030-COMP-BENCH]

**Logged**: 2026-10-30
**Priority**: low
**Status**: resolved
**Area**: tests

### Summary
The compaction benchmark project includes two top-level-program source variants, so the default project glob compiled both.

### Error
```text
CS8802: Only one compilation unit can have top-level statements
```

### Context
- Attempted to build `Current.csproj` in the temporary compaction benchmark directory.
- `Program.cs` and `ProgramNoAlias.cs` were both included by the default SDK glob.

### Suggested Fix
Explicitly include only the intended benchmark source in each temporary project file.

### Metadata
- Reproducible: yes
- Related Files: D:/Dev/Temp/lertaro-compaction-benchmark-20261030/Current.csproj

---

## [ERR-20261030-APP-RID]

**Logged**: 2026-10-30
**Priority**: medium
**Status**: resolved
**Area**: tests

### Summary
The App test run was started with `--no-restore` before the win-x64 assets target had been restored.

### Error
```text
NETSDK1047: project.assets.json has no target for net10.0-windows/win-x64
```

### Context
- Core tests had already been restored and passed.
- App tests require a runtime-specific restore in this checkout.

### Suggested Fix
Restore `Tests/App/App.csproj` with `-r win-x64`, then rerun without `--no-restore`.

### Metadata
- Reproducible: yes
- Related Files: Tests/App/App.csproj

---

Resolution: restored the App test project for win-x64 and reran the suite with the current native DLL; 2363 tests passed.



## [ERR-20261030-APP-RID-ROUND4]

**Logged**: 2026-10-30
**Priority**: low
**Status**: resolved
**Area**: tests

### Summary
Changing `App.csproj` invalidated its runtime-specific assets before the final round-4 rebuild.

### Error
```text
NETSDK1047: project.assets.json has no target for net10.0-windows/win-x64
```

### Context
- The App test project itself had already been restored for win-x64.
- The direct `App/App.csproj` assets needed the same runtime-specific restore after the source edit.

### Resolution
Restored the App project for win-x64 before rebuilding the final beta10 package.

---

## [ERR-20261030-CLI-RID-ROUND5]

**Logged**: 2026-10-30
**Priority**: low
**Status**: resolved
**Area**: tests

### Summary
The standalone CLI PE verification needed a runtime-specific restore before the x64 build.

### Error
```text
NETSDK1047: project.assets.json has no target for net10.0-windows/win-x64
```

### Resolution
Restored `Cli/Cli.csproj` for win-x64, then rebuilt it for the subsystem check.

---

## [ERR-20261030-001] temporary-benchmark-command

**Logged**: 2026-10-30T04:40:00-07:00
**Priority**: low
**Status**: resolved
**Area**: tests

### Summary
The first temporary search benchmark command was rejected before execution because it combined a recursive delete with a long PowerShell script.

### Context
- The command attempted to recreate a temporary benchmark directory and then build/run a console harness.
- No repository files were changed and no delete was performed.

### Suggested Fix
Use a fresh temporary directory and avoid recursive deletion in the combined command.

### Metadata
- Reproducible: unknown
- Related Files: D:/Dev/Temp/lertaro-search-benchmark-20261030

---

## [ERR-20261030-002] snapshot-hot-section-cache

**Logged**: 2026-10-30T04:55:00-07:00
**Priority**: medium
**Status**: resolved
**Area**: backend

### Summary
The first cached-pointer implementation used byte offsets when adding to typed pointers, causing an IndexOutOfRangeException in CSR row lookup.

### Context
- C# pointer arithmetic already scales by the pointed-to element size.
- The cached child/UID section pointers must add element indexes directly, not multiply by four.
- The benchmark caught the error before packaging.

### Suggested Fix
Keep pointer arithmetic in element units and rerun the search benchmark plus full tests.

### Metadata
- Reproducible: yes
- Related Files: Core/IndexV2/Persistence/Snapshot.cs

---

## [ERR-20261030-001] cargo-toolchain-path

**Logged**: 2026-10-30
**Priority**: low
**Status**: resolved
**Area**: tests

### Summary
The first Rust test invocation used a cargo path that does not exist in the installed toolchain layout.

### Error
The configured path `D:\Dev\Toolchains\Rust\cargo\bin\cargo.exe` was not found.

### Context
- The installed cargo executable is under the stable MSVC rustup toolchain.
- No source or project files were affected.

### Suggested Fix
Use `D:\Dev\Toolchains\Rust\rustup\toolchains\stable-x86_64-pc-windows-msvc\bin\cargo.exe` or the configured Rust toolchain PATH.

---

## [ERR-20261030-002] github-release-target

**Logged**: 2026-10-30
**Priority**: medium
**Status**: resolved
**Area**: release

### Summary
GitHub rejected the release target when the commit SHA was passed directly.

### Error
Release.target_commitish is invalid (HTTP 422).

### Context
- The commit is pushed to the working branch.
- Release creation was retried with the pushed branch name.

### Suggested Fix
Use the pushed branch name as the release target when the commit is not on the repository default branch.

---

## 2026-10-30: Local App test transient failures

A full App suite run reported WIC BitmapDecoder OutOfMemoryException, one focus assertion, and a child CLR startup failure. The 37 affected-class tests passed when isolated, and a subsequent full no-build run passed all 2367 tests without code changes. Do not silently suppress these failures or claim a root cause from this observation; retain TRX evidence under D:\Dev\Temp\lertaro-601-validation.

## 2026-10-30: Standard-user update smoke cleanup masked primary failure

The first Beta validation run failed in both x64 and ARM64 standard-user smoke jobs during `finally`: the script unconditionally started `unins000.exe`, which was not present after the test path had already failed. This cleanup exception hid the original failure. Changed cleanup to run the uninstaller only when it exists; rerun the jobs to expose and diagnose the primary failure.

## 2026-10-30: Inno Setup path arguments with spaces

After fixing smoke cleanup, the standard-user update CI check exposed that the test installer command passed `/DIR=` without quoting the install path. Inno Setup split the directory at the space in `C:\Program Files`, so installation succeeded to the wrong location and the expected app executable was missing. Quote the `/DIR` argument whenever its value can contain spaces.

The subsequent CI run reached the smoke-test cleanup but `Receive-Job` failed while stopping the background HTTP listener, masking the primary update test error again. Cleanup now stops/removes the job without waiting for a remoting response; the next run should reveal whether the update handoff itself succeeds.

The standard-user update smoke now exposes the primary issue: the alternate-account probe inherits a temp path under the runner's profile (`C:\Users\RUNNER~1\AppData`) and cannot create its staging directory there. The test runner already creates a dedicated scratch directory writable by the test account; pass that as TEMP/TMP to the probe so it validates update behavior without depending on the runner account's profile ACLs.

## 2026-10-30: Update smoke detected standard-user handoff rejection

CI now passes standard-user staging with an explicit writable TEMP/TMP path, but the retry reaches signature verification and the app reports that the service did not accept the signed update. The test host previously discarded the service response detail. It now initializes a test-local logger and includes its log on failure, so the next CI run will expose the exact rejection instead of returning only a generic assertion.

The HTTP trace shows the smoke-test server terminates while answering the intentional `/missing.zip` request: PowerShell's empty `[byte[]]@()` expression becomes `$null`, and `NetworkStream.Write` throws. That kills the local package server before the retry can begin, so this failure is in the test harness, not evidence of a production service rejection. Return a concrete zero-length byte array for 404 responses.

The explicit initialization was still overwritten by PowerShell's `if` output assignment: the 404 branch emits no pipeline value, turning the byte array back into `$null`. The server then throws at `NetworkStream.Write`, terminating after `/missing.zip`. Initialize the empty array separately, conditionally replace it only for files, and skip the body write when length is zero.
## [ERR-20261030-001] dotnet-runtime-build

**Logged**: 2026-10-30T07:20:00Z
**Priority**: low
**Status**: pending
**Area**: tests

### Summary
A direct Release build with `--no-restore` used an assets file that did not contain the requested `win-x64` runtime target.

### Error
`NETSDK1047: project.assets.json does not have a target for net10.0-windows/win-x64`

### Context
The repository uses runtime-specific restore/build settings in its workflow. The command was a local service-only build with `-r win-x64 --no-restore`; prior restore assets were framework-only.

### Suggested Fix
Use the repository's normal restore/build command or restore the service project for the requested runtime before building.

### Metadata
- Reproducible: yes
- Related Files: Service/Service.csproj

---

## [ERR-20261030-002] beta-link-check

**Logged**: 2026-10-30T10:25:00+08:00
**Priority**: medium
**Status**: fixed
**Area**: config

### Summary
The first CI run after adding the stale repository-link check failed before tests because the PowerShell/Ripgrep exit-code check was not portable in the GitHub runner environment.

### Error
The `Verify public repository links` step failed on both Windows runners even though the checked README and Readmes contain no old repository links.

### Suggested Fix
Use PowerShell `Select-String` and count returned matches instead of relying on `$LASTEXITCODE` after command substitution.

### Metadata
- Source: error
- Related Files: .github/workflows/beta-validation.yml
- Tags: github-actions, powershell, validation

---

## [ERR-20261030-003] github-api-rate-limit

**Logged**: 2026-10-30T10:29:00+08:00
**Priority**: low
**Status**: pending
**Area**: infra

### Summary
Repeated unauthenticated GitHub Actions polling hit the API rate limit while the workflow was still running.

### Error
GitHub REST API returned HTTP 403: `API rate limit exceeded`.

### Suggested Fix
Poll less frequently and use the workflow URL or authenticated GitHub CLI when available; do not restart the workflow because status observation failed.

### Metadata
- Source: external_api
- Tags: github-actions, rate-limit

---
## [ERR-20261030-VAC] incremental-visible-added-count-test

**Logged**: 2026-10-30T00:00:00+08:00
**Priority**: high
**Status**: resolved
**Area**: tests

### Summary
The first incremental `VisibleAddedCount` implementation missed resurrection accounting.

### Error
```text
Expected VisibleAddedCount = 1, actual = 0 after Upsert -> Remove -> Upsert.
```

### Context
- Replaced the LINQ scan with a maintained counter.
- The existing resurrection branch incremented file/directory deltas but did not increment the new visible-added counter.

### Suggested Fix
Increment the counter through the centralized `AddAdded` path when a removed slot is replaced, and route all added-row removal through `MarkAddedRemoved`.

### Metadata
- Reproducible: yes
- Related Files: Core/IndexV2/Delta/DeltaOverlay.cs, Tests/Core/IndexV2/Delta/DeltaOverlayTests.cs

---
## [ERR-20261030-GHL] workflow-job-log-fetch

**Logged**: 2026-10-30T12:28:50+08:00
**Priority**: medium
**Status**: pending
**Area**: infra

### Summary
The GitHub workflow job log endpoint returned 404 while the failed ARM64 test job was still finishing.

### Error
```text
GitHub API error 404: BlobNotFound
```

### Context
- Workflow: Build and Release for v6.1.0-beta6
- Job: ARM64 validation App test
- Job status was still in progress after the test step failed.

### Suggested Fix
Wait for the job to reach a terminal state, then fetch the job logs or rerun only after identifying the failing test.

### Metadata
- Reproducible: unknown
- Related Files: .github/workflows/beta-validation.yml

---
## [ERR-20261030-GHJ] workflow-job-poll-timeout

**Logged**: 2026-10-30T12:38:00+08:00
**Priority**: low
**Status**: pending
**Area**: infra

### Summary
The GitHub job listing call timed out while the rerun of the ARM64 release validation was still active.

### Context
- Workflow run: 41054741879
- The run itself remains in progress according to the Actions API.

### Suggested Fix
Poll the same run again through the API after a short interval.

### Metadata
- Reproducible: unknown

---
## [ERR-20261030-GHA] workflow-jobs-api-server-error

**Logged**: 2026-10-30T12:39:00+08:00
**Priority**: low
**Status**: pending
**Area**: infra

### Summary
The public GitHub Actions jobs endpoint returned a transient Server Error while the publish job was active.

### Context
- Workflow run: 41054741879
- Previous polling showed all validation jobs successful and `publish` in progress.

### Suggested Fix
Retry the same endpoint without restarting the workflow.

### Metadata
- Reproducible: unknown

---
## [ERR-20261030-PSH] release-asset-hash-command

**Logged**: 2026-10-30T12:42:00+08:00
**Priority**: low
**Status**: resolved
**Area**: infra

### Summary
The first PowerShell one-liner for hashing all release assets had an invalid `foreach` pipeline syntax.

### Context
- Release was already published; only local asset verification command failed to parse.
- No files were modified by the failed command.

### Suggested Fix
Accumulate hash objects in a list, then format the list after the loop.

### Metadata
- Reproducible: yes

---
## [ERR-20261030-PSH2] signature-verification-command

**Logged**: 2026-10-30T12:43:00+08:00
**Priority**: low
**Status**: resolved
**Area**: infra

### Summary
The first combined signature and PE verification PowerShell command used a pipeline after a `try/finally` statement, which PowerShell does not parse.

### Context
- This was a verification-only command; no release assets were changed.

### Suggested Fix
Collect verification rows in variables and format them after the `try/finally` block.

### Metadata
- Reproducible: yes

---

- 2026-10-30: Initial codebase-memory skill lookup used the wrong path (0/...); corrected to C:\Users\28892\.codex\skills\codebase-memory\SKILL.md.

- 2026-10-30: App test build found CS0104 in SearchWindow.xaml.cs because Button was ambiguous with System.Windows.Forms.Button; use the fully qualified WPF Button dependency property.

- 2026-10-30: First App test run after the UI change failed in pre-existing QuickSearchWindowFocusHelperTests.PendingFocus_DoesNotOverwriteNewCaretAfterHideAndReopen (Keyboard.FocusedElement was null); no failure was reported from the new filter tests.

- 2026-10-30: gh run view --job --log cannot return logs while the beta7 workflow is still in progress; GitHub reports logs only after completion.

- 2026-10-30: Remote beta7 verification command was rejected because it included a recursive Remove-Item cleanup; switched to a fresh unique D:\Dev\Temp directory instead.
2026-10-30T21:58:32.8795986+08:00 - Asset PE inspection PowerShell one-liner was rejected by command policy; will use Python zipfile instead.
2026-10-30T22:13:44.3707937+08:00 - beta9 ARM64 CI job 124108580018 reported Test app failure; logs unavailable while job is running; release paused pending diagnosis.
2026-10-30T22:17:09.3891469+08:00 - beta9 ARM64 Test app failure isolated to existing BackgroundVideoTests.TwoWindowsShareOneSilentLoopAndOnlyPauseWhenEverySurfaceIsHidden timing timeout (playing=True, position=00:00:00); Rust/Core and other App tests passed.

## 2026-11-02 Clipboard integration checks
- Native P/Invoke methods renamed with a Native suffix need explicit EntryPoint; verify real exports without clipboard access.
- SQLite may delete invalid cold journal sidecars before NOTADB. Separate recovery tests from explicit quarantine file-preservation tests.
- MSTest 4 uses ThrowsAsync rather than ThrowsExceptionAsync; cancellation before semaphore acquisition can throw OperationCanceledException.


## [ERR-20261102-QUALITY] XML documentation property timing

**Logged**: 2026-11-02
**Priority**: low
**Status**: resolved
**Area**: config

### Summary
Setting GenerateDocumentationFile in Directory.Build.targets is too late for the SDK to derive DocumentationFile. IDE0005 still emits EnableGenerateDocumentationFile, which breaks a strict project.

### Resolution
Set GenerateDocumentationFile for that project in Directory.Build.props; leave TreatWarningsAsErrors in Directory.Build.targets. Do not propagate documentation generation as a command-line property to references, since it introduces unrelated CS1591 warnings there.

## [ERR-20261102-DOCS] npm command from repository root

**Logged**: 2026-11-02
**Priority**: low
**Status**: resolved
**Area**: docs

### Summary
`npm run api:build` requires the `Site/` working directory; running it from the repository root returns ENOENT for `package.json`.

### Resolution
Run `npm run docs:build` from `Site/`, as in the workflow and Tools/ApiDocs/README.md. Keep dotnet tool restore at the repository root using `--tool-manifest .config/dotnet-tools.json`.

## [ERR-20261102-BUILD-INTERMEDIATES]

**Logged**: 2026-11-02
**Priority**: low
**Status**: resolved
**Area**: tests

### Summary
A local verification command failed after overriding `BaseIntermediateOutputPath` globally for a project with references.

### Resolution
Keep MSBuild intermediate paths project-specific. The global override caused generated assembly files to collide for a referenced project; the tool project also looked for restore files in the shared directory. Retry with normal per-project `obj` paths (all already on D: in this checkout) and only override output paths when needed.

## [ERR-20261102-YAML-VALIDATION]

**Logged**: 2026-11-02
**Priority**: low
**Status**: resolved
**Area**: infra

### Summary
The workflow syntax check initially tried to load a YAML package that is not installed under Site/node_modules.

### Resolution
Use the existing Python PyYAML installation to parse workflow files; no dependency installation was needed.

## [ERR-20261102-STRYKER-SCOPE]

**Logged**: 2026-11-02
**Priority**: low
**Status**: unresolved
**Area**: tests

### Summary
Stryker.NET 5.0.0 ignored the attempted narrow mutation glob for the Core project and enumerated 26,406 mutants across the whole project.

### Context
The Core tests target .NET 10. Stryker initially resolved the system .NET 8 SDK and failed to analyze the project; prepending the installed .NET 10 SDK fixed analysis. The subsequent run selected all 2,165 Core tests and reported one initial-run failure. A direct Core test run passed all 2,165 tests.

### Suggested Fix
Before adopting Stryker, configure and verify project-relative mutation filters so the pilot cannot expand to the entire Core project. Keep it out of CI until its target scope and baseline are stable.

### Metadata
- Reproducible: yes
- Related Files: docs/build-quality.md, Core/Hook/ExplorerIntegration/ExplorerDialogBounds.cs
