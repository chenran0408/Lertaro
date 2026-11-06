# ADR 0004: Fluent Token Layer Over the Theme Contract

## Status

Accepted (2026-11, WinUI 3 Fluent alignment, phase 2-4).

## Context

The visual palette is a contract of 83 resource keys shared verbatim by 48 theme files across
three plugins (`Plugins/*/Resources/Themes/*.xaml`), validated by `Tests/Core/ThemeContrastTests`.
Restyling surfaces toward WinUI 3 Fluent must not fork that contract: every curated theme would
need rewriting and every palette addition would become a 48-file change.

The audit also found dead visual machinery: the DWM corner path (`DwmWindowCorners`,
`LayeredSurface`) had no caller, so every window rendered square-cornered while three different
corner numbers coexisted; focus was suppressed (`FocusVisualStyle={x:Null}`) in 22 places with no
replacement, leaving system dotted rectangles; five shadow specs and two popover stroke sources
coexisted; LocalSend carried the app's only hardcoded colors.

## Decision

- **The 83-key palette is the primitive/alias layer and does not change.** Brush aliases in WPF
  cannot rewrap a DynamicResource brush (a `SolidColorBrush Color=` needs a Color, not a Brush),
  so surfaces reference palette keys directly; `Tokens/Colors.xaml` adds only what the palette
  cannot express: the shared `PopoverShadowEffect` (its `Color` rides `ShadowColor`, a true Color
  key) and the `FluentFocusRing` style.
- **Corners stay a three-scale token set** (Window 10 / Popover 8 / Control 6, with the window
  band complements) plus one sanctioned literal: 14px selection badges use `CornerRadius="7"`
  because there it means "circle", not a corner scale. Top-level windows get real DWM rounding
  via `AppWindow`'s `SourceInitialized`; pre-Windows 11 fails the call and stays square.
- **Keyboard focus is always visible.** The `FluentFocusRing` (2px accent + 1px surface gap)
  replaces every `{x:Null}` suppression except text fields, which express focus through their
  border color instead of a ring inside the field. WPF only renders the ring for keyboard
  navigation, so pointer use is unchanged.
- **Elevation**: menus and tooltips keep the system drop shadow; custom popups share one
  `PopoverShadowEffect`. Window chrome carries no WPF effects.
- **Typography** stays code-owned (`AppTypography`): Segoe UI Variable Text / Cascadia Mono with
  Windows 10 fallbacks; the XAML-side discipline of zero literal `FontFamily` continues.
- FlowLauncher compatibility aliases defer with `<DynamicResource>` instead of capturing brushes
  at dictionary load, so plugin settings pages follow theme switches.

## Consequences

- New themes need only the 83 keys; Fluent surfaces come for free.
- A theme key rename now breaks surfaces at compile time only where `StaticResource` is used;
  `DynamicResource` gaps fail silently -- the two broken references found in the audit
  (`FieldBackground`, `SelectedBackground`) are the cautionary example and were fixed.
- The sidebar item styles remain three similar copies in two per-window dictionaries
  (`SearchWindow.xaml`, `SettingsSidebar.xaml`); merging them couples dictionary load order for
  ~40 saved lines and was deliberately skipped.
