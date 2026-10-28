# Lertaro adaptation record

This directory contains source copied from `everlosttime/glassdesk-open-source` at
commit `b1eb58c2d7205fca57e192e45d363872ced55624` (GlassDesk 1.4.0).

Lertaro uses GlassDesk's `GlassWindow`, `LiquidGlassSurface`, desktop scene capture,
and `BackgroundSurface` in all 13 primary XAML windows, including the Quick Panel,
search and settings windows, Quick Look, LocalSend, notifications, and dialogs.
`App/Resources/Styles.xaml` also merges GlassDesk's original `LiquidGlassTheme.xaml`
after the legacy app styles, so its implicit WPF control templates take precedence.
The remaining named product templates and content layouts are app-specific; they
have not yet been verified as pixel-identical to GlassDesk at runtime.

The UI project target was changed from `net10.0-windows10.0.26100.0` to
`net10.0-windows` to match Lertaro, and the two Windows SDK runtime assemblies are
copied beside the app so scene capture works when the project is referenced as a
library. Other retained upstream files and their license records are in this
directory.

Typography legibility adjustment (2026-10-28): GlassDesk palette secondary text opacity is raised from 70% to 94% for legibility over live wallpaper. The app-level UI supplies a 14 DIP medium-weight baseline and a restrained text shadow on glass surfaces.

Performance adjustment (2026-10-29): `src/GlassDesk.UI/Controls/LiquidGlassOptics.cs` reuses instance-local frame scratch buffers while returning a separate result frame. `src/GlassDesk.UI/Controls/LiquidGlassSurface.cs` clears those buffers on hide, sleep, unavailable-display, opaque, and dispose paths; an active render defers clearing until its result is released. `src/GlassDesk.UI/Controls/GlassWindow.cs` now sleeps hidden glass surfaces and wakes them during the existing offscreen pre-show material preparation. The App presentation helper reruns preparation after each hide, serializes stale work, and keeps the existing 250 ms show budget. The original `LICENSE`, `NOTICE`, and per-file scopes in `docs/licensing/FILE-LICENSES.json` remain in force.

## License scope

The upstream `docs/licensing/FILE-LICENSES.json` marks `GlassWindow.cs`,
`LiquidGlassSurface.cs`, `GlassBackdropScene.cs`, and `GlassBackgroundLayer.cs`
as files containing new noncommercial changes as well as portions previously
published under Apache-2.0. `LiquidGlassSurface.Shared.cs` is marked as a new
noncommercial addition. Preserve the upstream LICENSE, NOTICE, and per-file
records. The GlassDesk integration is not cleared for commercial distribution
under the current snapshot's license; commercial use requires separate rights
for the noncommercial portions or replacing those portions with independently
licensed code.

This adaptation is not represented as a pixel-perfect 1:1 reproduction. The
remaining visible and platform differences must be evaluated against live
GlassDesk and Lertaro windows on the same Windows build and display scale.
