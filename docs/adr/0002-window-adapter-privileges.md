# ADR 0002: Window Adapter Privileges

## Status

Accepted (recorded retrospectively; this describes the design as it exists in the source).

## Context

Inline search and quick navigation operate on third-party file managers through plugin-authored
window adapters (`IInlineSearchAdapter`, `IFileDialogAdapter`) declared in the plugin SDK
([PluginSdk/Abstractions/Plugins/WindowAdapters/IInlineSearchAdapter.cs:3](../../PluginSdk/Abstractions/Plugins/WindowAdapters/IInlineSearchAdapter.cs#L3)).
Adapters talk to real windows via COM/UI automation, which may require a token that can interact
with elevated windows, so the hook process is launched with the session's linked admin token when
that session's user is a genuine administrator (see ADR 0001). Adapters run in-process inside the
hook, on a dedicated STA invoker with a timeout ([Core/Hook/ExplorerIntegration/ExplorerStaInvoker.cs:47](../../Core/Hook/ExplorerIntegration/ExplorerStaInvoker.cs#L47)),
selected per foreground window by the tracker ([Core/Hook/ExplorerIntegration/ExplorerTracker.cs:147](../../Core/Hook/ExplorerIntegration/ExplorerTracker.cs#L147))
and driven by command handlers ([Core/Hook/Commands/InlineAdapterCommandHandler.cs:66](../../Core/Hook/Commands/InlineAdapterCommandHandler.cs#L66)).

## Decision

- Adapter code runs at the hook's privilege, with no sandboxing between plugins and the hook
  process; third-party interop exceptions are caught per call
  ([Core/Hook/Commands/InlineAdapterCommandHandler.cs:59](../../Core/Hook/Commands/InlineAdapterCommandHandler.cs#L59)).
- Anything that must run at the user's own (unelevated) privilege is delegated to the App: the
  hook sends a `HookToolRunRequest` over IPC, the App executes the tool with its own token and
  writes the result to a file the hook pre-creates, so the content crosses the privilege boundary
  through the file rather than the pipe
  ([Core/Hook/Ipc/HookProcess.cs:85](../../Core/Hook/Ipc/HookProcess.cs#L85), wired at
  [Core/Hook/Ipc/HookProcess.cs:224](../../Core/Hook/Ipc/HookProcess.cs#L224) to
  [PluginSdk/Services/ToolRunService.cs:25](../../PluginSdk/Services/ToolRunService.cs#L25)).
- The hook reports its actual elevation when an external indexer asks, so the Everything
  integration answers admin broadcast windows only when it really is elevated
  ([Core/Services/Everything/EverythingIpcMessageDispatcher.cs:51](../../Core/Services/Everything/EverythingIpcMessageDispatcher.cs#L51),
  using [Core/Services/ElevationManager.cs:8](../../Core/Services/ElevationManager.cs#L8)).
- On hotkey and wake events the hook calls `AllowSetForegroundWindow` for the registered App pid
  so the App may take foreground despite being unelevated
  ([Core/Hook/Ipc/HookProcess.cs:377](../../Core/Hook/Ipc/HookProcess.cs#L377)).

## Consequences

Privilege follows the hook process token, decided at launch by the service, never re-prompted at
runtime; there is no UAC fallback in the App or hook
([Service/HookModeLauncher.cs:39](../../Service/HookModeLauncher.cs#L39) logs the resulting level).
Operations needing user privilege must go through the App-delegation path.
