# ADR 0001: Process Boundaries

## Status

Accepted (recorded retrospectively; this describes the design as it exists in the source).

## Context

Lertaro's work is split across processes with different privilege levels. The background
service runs from the same executable in `--service` mode ([Service/Program.cs:101](../../Service/Program.cs#L101))
as a Windows Service running as LocalSystem, which also handles portable copies: the portable
folder is locked to SYSTEM and Administrators before the service installs from it
([Service/ServiceInstaller.cs:32](../../Service/ServiceInstaller.cs#L32)). A debug console mode
exists for running the service logic as a plain user ([Service/Program.cs:132](../../Service/Program.cs#L132)),
so the LocalSystem service should not be assumed to be running on every machine.

The App must never launch the hook itself, because only the LocalSystem service can start a
process in another logon session and hand it the session's own token: `WTSQueryUserToken`
requires `SeTcbPrivilege` ([Core/Services/HookLaunch/SessionProcessLauncher.cs:12](../../Core/Services/HookLaunch/SessionProcessLauncher.cs#L12)).

## Decision

- The App requests hook launches over the service's named pipe; it always asks for elevation
  ([Core/Hook/Ipc/HookIpcClient.cs:419](../../Core/Hook/Ipc/HookIpcClient.cs#L419)), and the
  service decides server-side.
- The pipe handler identifies the caller from the pipe's kernel handle and only serves this
  install's `Lertaro.App.exe` ([Core/Services/HookLaunch/HookLaunchRequestHandler.cs:31](../../Core/Services/HookLaunch/HookLaunchRequestHandler.cs#L31)).
- The broker keeps one live hook per session ([Core/Services/HookLaunch/HookProcessBroker.cs:11](../../Core/Services/HookLaunch/HookProcessBroker.cs#L11))
  and launches via the session's own token. Elevation swaps in the UAC-linked admin token only
  when that session's user is genuinely an administrator; otherwise the plain user token is used,
  so a request can only ever downgrade, never escalate
  ([Core/Services/HookLaunch/SessionProcessLauncher.cs:56](../../Core/Services/HookLaunch/SessionProcessLauncher.cs#L56)).
  The same launcher relaunches the App after an update unelevated ([Service/UsnService.cs:110](../../Service/UsnService.cs#L110))
  and runs the update applier elevated ([Core/Services/Update/UpdateApplyRequestHandler.cs:103](../../Core/Services/Update/UpdateApplyRequestHandler.cs#L103)).
- Elevated children get a filtered environment: user-settable runtime-hook variables (`DOTNET_`,
  `COMPlus_`, ...) are stripped ([Core/Services/HookLaunch/SessionProcessLauncher.cs:118](../../Core/Services/HookLaunch/SessionProcessLauncher.cs#L118)).
- Plugins load in-process via `Assembly.LoadFrom` in the App, service, and hook
  ([Core/Services/Plugin/Loading/ServicePluginLoader.cs:58](../../Core/Services/Plugin/Loading/ServicePluginLoader.cs#L58));
  there is no plugin sandbox process.
- The hook watches the App's pid and self-stops when the App it serves is gone
  ([Core/Hook/Ipc/HookProcess.cs:160](../../Core/Hook/Ipc/HookProcess.cs#L160)).

## Consequences

Cross-session launch and elevation live entirely in the LocalSystem service. Plugin code runs
with each host process's privileges. The service also runs at BelowNormal priority so background
indexing does not starve the App's UI thread ([Service/UsnService.cs:41](../../Service/UsnService.cs#L41)).
