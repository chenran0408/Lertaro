# Accessibility Audit (read-only)

Scope: all top-level windows and shared controls. Read-only survey — no fixes applied. Implementation
waits for the nine-item batch to merge, then runs as a separate batch.

## Method

Grep across `App/**/*.xaml` and `App/**/*.cs` for: `AutomationProperties`, `AutomationPeer`,
`FocusVisualStyle`, `SystemColors` / `HighContrast`, icon-only buttons (`Content="&#x..."` without
an `AutomationProperties.Name`), and custom OnRender classes. Visual inspection of the Fluent
focus ring (introduced in the redesign) and the high-contrast theme story.

---

## Per-surface findings

### SearchWindow (main search window)

| Finding | Severity | Detail |
|---------|----------|--------|
| Fluent focus ring present | ✅ | All interactive controls use `{DynamicResource FluentFocusRing}` (2px accent + 1px surface gap). Keyboard navigation shows the ring; mouse clicks stay quiet. |
| Result rows lack `AutomationProperties.Name` | ⚠️ Medium | Rows are ListBoxItems with a custom DataTemplate (kind glyph + preview text + subtitle + slot badge). The screen reader announces the raw text content, which works, but the slot badge number and the kind glyph are read as noise ("Ctrl Ctrl" etc.). |
| `SearchBoxControl` has no AutomationPeer | ⚠️ Medium | The search box is a custom composite (TextBox + placeholder + watermark + brand icons). The inner `TextBox` is naturally accessible, but the watermark/mode badge (web-mode indicator) is invisible to screen readers. |
| GridView column headers lack sort announcements | Low | Column headers are clickable for sorting but don't announce the sort state. |
| `StatusBarControl` has no AutomationPeer | Low | Status bar text is read as plain text — acceptable, but the Lertaro logo dot and status counts are announced separately. |

### Settings window

| Finding | Severity | Detail |
|---------|----------|--------|
| Sidebar navigation items have `AutomationProperties.Name` | ✅ | Settings sidebar items use translated names. |
| Blacklist editor buttons have `AutomationProperties.Name` | ✅ | Add/bulk buttons are named. |
| Several checkbox rows lack descriptions for screen readers | ⚠️ Medium | The label `Content` is the accessible name, but the `SettingsHintText` below each checkbox is a separate `TextBlock` not associated with the checkbox via `AutomationProperties.LabeledBy` or a `DescriptionAttribute`. Screen readers miss the explanation. |
| Rule editor rows (3 twin styles) lack group semantics | Low | The three icon-button styles (`RuleIconButton`, `RuleIconButtonAccent`, `QuickNavIconButton`) are functionally identical but not grouped. |
| `RuleTextBox` / `RuleBulkTextBox` lack `AutomationProperties.Name` | ⚠️ Medium | Text boxes in the rule editor are unnamed; the screen reader announces "edit" without context. |

### QuickPanel / QuickSearchWindow

| Finding | Severity | Detail |
|---------|----------|--------|
| Tab strip buttons are icon-only, no `AutomationProperties.Name` | ⚠️ High | Tab close buttons, view toggle, sort toggle, and search clear are all `Content="&#xE711;"` etc. without names. Screen readers announce "button" with no context. |
| QuickPanel rows lack `AutomationProperties.Name` | ⚠️ Medium | Row templates show the title and subtitle, but the tile/grid alternative view has no row announcements. |
| `QuickPanelNumberedStackPanel/WrapPanel` custom OnRender | ⚠️ Medium | These panels draw number badges via `OnRender(DrawingContext)` — the numbers are invisible to screen readers. An AutomationPeer should expose them as numeric properties. |
| `LineBreakMarkerTextBox` custom OnRender | Low | Draws line-break markers as decorations; doesn't affect content reading. |

### Dialogs (CustomMessageBox / PluginFieldPrompt / UpdateDialog)

| Finding | Severity | Detail |
|---------|----------|--------|
| `PluginFieldPromptWindow` close button is icon-only, no name | ⚠️ Medium | `Content="&#xE8BB;"` with no AutomationProperties. |
| `UpdateDialogWindow` content is text-only | ✅ | The dialog body is plain text; screen readers read it fine. |
| No dialog open/close announcements (LiveRegions) | Low | Opening a dialog doesn't trigger a screen-reader announcement; users may not notice a modal appeared. |

### Notifications

| Finding | Severity | Detail |
|---------|----------|--------|
| Read/Close buttons have `AutomationProperties.Name` | ✅ | Named from translations. |
| `AutomationProperties.SetName` called on the card window | ✅ | The card window sets its own accessible name from the notification content. |
| Arrival/dismissal has no LiveRegion announcement | Low | Notification arrival is visual + audio only; no `LiveSetting` or `AutomationNotification` raised. |

### Onboarding / Tips

| Finding | Severity | Detail |
|---------|----------|--------|
| Cards have translated titles/bodies | ✅ | Text is read correctly by screen readers. |
| Keycap animation has no text equivalent | ⚠️ Medium | The keycap visual ("Ctrl + Ctrl") is a `Border` + `TextBlock` sequence that reads as separate characters. Should be grouped with `AutomationProperties.Name` on the containing panel. |
| Dots indicator is `Ellipse` elements, invisible to screen readers | Low | Purely visual step indicator; no `AutomationProperties.Name` or range value. |
| Tip cards have `ShowActivated=False` | ✅ | Don't steal focus, good for accessibility. |

### LocalSend

| Finding | Severity | Detail |
|---------|----------|--------|
| Remove item button has `AutomationProperties.Name` | ✅ | Named from translations. |
| Device panel pin button has `AutomationProperties.Name` | ✅ | Named from translations. |
| Progress list items lack per-file announcements | Low | Transfer progress is not announced per file; screen reader users get no feedback on completion. |

---

## Cross-cutting findings

| Finding | Severity | Detail |
|---------|----------|--------|
| Focus ring is token-driven and consistent | ✅ | The FluentFocusRing is applied via `{DynamicResource}` on buttons, checkboxes, radios, combo boxes and result rows. Text fields use border-color instead of a ring (correct Fluent pattern). |
| High contrast: no `SystemColors` or `HighContrast` references | ⚠️ High | The theme system uses DynamicResource for all colors, but there is no high-contrast theme variant. All colors come from the 83-key palette; a WCAG high-contrast mode would need a separate palette or `SystemColors` fallback. The theme manager does not detect `SystemParameters.HighContrast`. |
| All text colors come from the palette; contrast is tested | ✅ | `Tests/Core/ThemeContrastTests.cs` validates the palette's text-on-background pairs. The A5 redesign's `FluentFocusRing` uses AccentColor + ContentBg, both from the tested palette. |
| No per-control AutomationPeer overrides on custom controls | ⚠️ Medium | `SearchBoxControl`, `HistoryListControl`, `SpaceAnalyzerView`, `SearchSidebarControl`, `TabStripScrollView` — none override `OnCreateAutomationPeer`. WPF's default peers cover the basics but miss the custom visual behaviors. |
| `ToggleSwitch`-like controls (ThemedCheckBox, ThemedRadioButton) use TogglePattern | ✅ | WPF's CheckBox/RadioButton have built-in TogglePattern; the custom templates preserve the base class. |
| Virtualizing lists (result lists) use `VirtualizingStackPanel` | ✅ | UIA can enumerate visible items; scrolling brings new items into the tree. |

---

## Top 10 fixes (ordered by user impact)

| # | Fix | One-line change |
|---|-----|----------------|
| 1 | Add `AutomationProperties.Name` to all icon-only buttons (≈12 sites) | Add `AutomationProperties.Name="{Binding Source={x:Static services:TranslationManager.Instance}, Path=[key]}"` with a new translation key per site |
| 2 | Add `AutomationProperties.LabeledBy` or `HelpText` to checkbox rows with hint text below | Point the checkbox at the adjacent hint `TextBlock` via `AutomationProperties.LabeledBy="{Binding ElementName=HintTextBlock}"` or merge the hint into the checkbox's `Description` |
| 3 | Add `AutomationProperties.Name` to `SearchBoxControl` (the mode badge is invisible) | Set it from the watermark text in the code-behind's web-mode change handler |
| 4 | Add `AutomationProperties.Name` to the tab strip buttons in QuickPanel | Same pattern as #1, with a new translation key per button |
| 5 | Add `AutomationProperties.Name` to `RuleTextBox` / `RuleBulkTextBox` in the rule editor | Set from the adjacent label or a new translation key |
| 6 | Group the keycap animation in onboarding: `AutomationProperties.Name` on the containing panel | Set to the plain-text combo (e.g. "Ctrl, then Ctrl again") instead of letting separate elements be read |
| 7 | Add `IsSearchBoxEnabled` announcements (LiveRegion or AutomationNotification) | Raise `AutomationNotification` when `IsSearchBoxEnabled` flips so screen readers know search readiness changed |
| 8 | Add `AutomationPeer` override on `QuickPanelNumberedStackPanel/WrapPanel` | Return a custom peer exposing the badge numbers as `NumericChildren` |
| 9 | Add high-contrast theme detection: `SystemParameters.HighContrast` → switch palette | In `ThemeManager.Initialize`, check `SystemParameters.HighContrast` and map to a new `BuiltInTheme("HighContrast", ...)` or at minimum log a warning |
| 10 | Add `AutomationNotification` on notification card arrival | Call `AutomationPeer.RaiseAutomationEvent(AutomationElementIds.Notification)` or use `LiveSetting=Assertive` on the card's root element |
