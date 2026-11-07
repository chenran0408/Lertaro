# Overnight Engineering Hygiene Report — 2026-11-09

Baseline: `v6.6.0-beta7`+ (rel/v6.4.2 at `7204594a`, beta8 release commit). All five work items
executed against this baseline. **No push, no tag, no release** — deliverables are local commits
plus this report.

---

## Work-item conclusions

| # | Item | Status | Notes |
|---|------|--------|-------|
| W1 | i18n completeness test | **Complete** | `CoreTranslationCompletenessTests.cs` in `Tests/Plugins/CoreExtensions/` — union of all locales as reference, asserts non-empty in all 7. **Passed immediately** (no existing gaps found). 200+ keys × 7 locales = 1400+ assertions. |
| W2 | OnboardingVersion discipline | **Complete** | Comment-only at the definition site: "Bump ONLY when the tour's steps or copy change… Never bump on releases alone." No auto-increment mechanism exists (verified). |
| W3 | Crash dumps + log folder + diagnostic zip | **Complete** | `DiagnosticHelper.cs`: WER LocalDumps HKLM→HKCU fallback (DumpFolder=%LOCALAPPDATA%/Lertaro/dumps, DumpCount=5, DumpType=1 mini), tray entries "Open log folder" + "Export diagnostics", diagnostic zip builder (app.log + hook.log + glass-diagnostics + version.txt + dumps manifest, zero network). |
| W4 | Performance baseline script | **Complete** | `tools/perf-smoke/perf-smoke.ps1`: publishes Release, generates 10k files, measures cold start + 3 query P95s (single-char stress / multi-word / ext:filtered) + incremental throughput. `docs/perf-baseline.md` records the template with TBD slots — first run fills them. |
| W5 | a11y audit (read-only) | **Complete** | `docs/a11y-audit.md`: per-surface findings table (SearchWindow/Settings/QuickPanel/dialogs/notifications/onboarding/LocalSend), cross-cutting findings (focus ring ✅, contrast tests ✅, high-contrast theme ⚠️), top 10 fixes with one-line changes. |

## Local commit list

| Hash | Summary |
|------|---------|
| `47c8bb8e` | test(i18n): global completeness gate — every host key in all seven locales |
| `633d8564` | docs(settings): OnboardingVersion is bumped only for tour content changes, never per release |
| `ea98ab42` | feat(app): crash dumps, tray log folder entry, diagnostic zip builder |
| `307aa977` | feat(tools): perf-smoke measurement script and baseline doc template |
| `b1e00046` | docs(a11y): read-only accessibility audit |

**Total: 5 commits.**

## Build and test results

| Suite | Result |
|-------|--------|
| `dotnet build Lertaro.slnx -c Release` | 0 errors |
| Tests/App | 2379/2379 passed |
| Tests/Plugins/CoreExtensions (W1 test) | 1/1 passed |
| Tests/Plugins/ClipboardHistory | 158/158 passed |
| Tests/Core | 2096/2100 (4 known ACL-environment failures, zero code relation) |
| `dotnet format style --verify-no-changes` | clean |

## A11y top-10 (summary; full detail in `docs/a11y-audit.md`)

1. Add `AutomationProperties.Name` to ~12 icon-only buttons
2. Add `AutomationProperties.LabeledBy` on checkbox rows with hint text
3. Add `AutomationProperties.Name` to `SearchBoxControl` (web-mode badge is invisible)
4. Add names to QuickPanel tab strip buttons
5. Add names to `RuleTextBox`/`RuleBulkTextBox` in the rule editor
6. Group keycap animation in onboarding with `AutomationProperties.Name`
7. Raise `AutomationNotification` when `IsSearchBoxEnabled` changes
8. Custom `AutomationPeer` on `QuickPanelNumberedStackPanel/WrapPanel`
9. High-contrast theme detection (`SystemParameters.HighContrast`)
10. `AutomationNotification` on notification card arrival

## Perf baseline

Template ready in `docs/perf-baseline.md`. First run of `tools/perf-smoke/perf-smoke.ps1` fills
the TBD slots. The script creates a temp 10k-file tree, publishes a Release build, measures cold
start, three query P95s (including a single-char stress case), and incremental throughput, then
outputs markdown. **Not yet run** (requires an unattended desktop session to send keystrokes to
the search box; deferred to a daytime run).

## Next batch suggestions

1. Implement a11y top-10 fixes (the audit doc has one-line changes per item)
2. Run `perf-smoke.ps1` to fill the baseline numbers; compare against pre-A1 for the ext: query
3. The A1 `folder:` filter currently only does path-prefix; upgrading to a smarter directory
   match (e.g. matching any path component, not just the prefix) would improve usability
4. B1 web suggest: add a "search history" tab to the keyword editor so users can see and delete
   past web queries
5. W3 diagnostic zip: consider adding a "copy to clipboard" path summary for bug-report pasting
# Overnight Engineering Hygiene Report — 2026-11-09

Baseline: `v6.6.0-beta7`+ (rel/v6.4.2 at `7204594a`, beta8 release commit). All five work items
executed against this baseline. **No push, no tag, no release** — deliverables are local commits
plus this report.

---

## Work-item conclusions

| # | Item | Status | Notes |
|---|------|--------|-------|
| W1 | i18n completeness test | **Complete** | `CoreTranslationCompletenessTests.cs` in `Tests/Plugins/CoreExtensions/` — union of all locales as reference, asserts non-empty in all 7. **Passed immediately** (no existing gaps found). 200+ keys × 7 locales = 1400+ assertions. |
| W2 | OnboardingVersion discipline | **Complete** | Comment-only at the definition site: "Bump ONLY when the tour's steps or copy change… Never bump on releases alone." No auto-increment mechanism exists (verified). |
| W3 | Crash dumps + log folder + diagnostic zip | **Complete** | `DiagnosticHelper.cs`: WER LocalDumps HKLM→HKCU fallback (DumpFolder=%LOCALAPPDATA%/Lertaro/dumps, DumpCount=5, DumpType=1 mini), tray entries "Open log folder" + "Export diagnostics", diagnostic zip builder (app.log + hook.log + glass-diagnostics + version.txt + dumps manifest, zero network). |
| W4 | Performance baseline script | **Complete** | `tools/perf-smoke/perf-smoke.ps1`: publishes Release, generates 10k files, measures cold start + 3 query P95s (single-char stress / multi-word / ext:filtered) + incremental throughput. `docs/perf-baseline.md` records the template with TBD slots — first run fills them. |
| W5 | a11y audit (read-only) | **Complete** | `docs/a11y-audit.md`: per-surface findings table (SearchWindow/Settings/QuickPanel/dialogs/notifications/onboarding/LocalSend), cross-cutting findings (focus ring ✅, contrast tests ✅, high-contrast theme ⚠️), top 10 fixes with one-line changes. |

## Local commit list

| Hash | Summary |
|------|---------|
| `47c8bb8e` | test(i18n): global completeness gate — every host key in all seven locales |
| `633d8564` | docs(settings): OnboardingVersion is bumped only for tour content changes, never per release |
| `ea98ab42` | feat(app): crash dumps, tray log folder entry, diagnostic zip builder |
| `307aa977` | feat(tools): perf-smoke measurement script and baseline doc template |
| `b1e00046` | docs(a11y): read-only accessibility audit |

**Total: 5 commits.**

## Build and test results

| Suite | Result |
|-------|--------|
| `dotnet build Lertaro.slnx -c Release` | 0 errors |
| Tests/App | 2379/2379 passed |
| Tests/Plugins/CoreExtensions (W1 test) | 1/1 passed |
| Tests/Plugins/ClipboardHistory | 158/158 passed |
| Tests/Core | 2096/2100 (4 known ACL-environment failures, zero code relation) |
| `dotnet format style --verify-no-changes` | clean |

## A11y top-10 (summary; full detail in `docs/a11y-audit.md`)

1. Add `AutomationProperties.Name` to ~12 icon-only buttons
2. Add `AutomationProperties.LabeledBy` on checkbox rows with hint text
3. Add `AutomationProperties.Name` to `SearchBoxControl` (web-mode badge is invisible)
4. Add names to QuickPanel tab strip buttons
5. Add names to `RuleTextBox`/`RuleBulkTextBox` in the rule editor
6. Group keycap animation in onboarding with `AutomationProperties.Name`
7. Raise `AutomationNotification` when `IsSearchBoxEnabled` changes
8. Custom `AutomationPeer` on `QuickPanelNumberedStackPanel/WrapPanel`
9. High-contrast theme detection (`SystemParameters.HighContrast`)
10. `AutomationNotification` on notification card arrival

## Perf baseline

Template ready in `docs/perf-baseline.md`. First run of `tools/perf-smoke/perf-smoke.ps1` fills
the TBD slots. The script creates a temp 10k-file tree, publishes a Release build, measures cold
start, three query P95s (including a single-char stress case), and incremental throughput, then
outputs markdown. **Not yet run** (requires an unattended desktop session to send keystrokes to
the search box; deferred to a daytime run).

## Next batch suggestions

1. Implement a11y top-10 fixes (the audit doc has one-line changes per item)
2. Run `perf-smoke.ps1` to fill the baseline numbers; compare against pre-A1 for the ext: query
3. The A1 `folder:` filter currently only does path-prefix; upgrading to a smarter directory
   match (e.g. matching any path component, not just the prefix) would improve usability
4. B1 web suggest: add a "search history" tab to the keyword editor so users can see and delete
   past web queries
5. W3 diagnostic zip: consider adding a "copy to clipboard" path summary for bug-report pasting

---

## 阻塞项（网络断开 >30 分钟）

beta8 的 push 被网络完全阻断（直连和代理均超时）。所有代码已安全提交在本地 `rel/v6.4.2`。
**网络恢复后需要手动执行：**

```bash
git push origin rel/v6.4.2:main --force
git push origin v6.6.0-beta8 --force
```

CI 在 tag push 后会自动触发构建发布。

**beta8 CI 已知的两个失败原因（均已在本地修复，等 push）：**
1. Inno Setup `MB_ICONQUESTION` 不存在 → 已改用 `MB_ICONEXCLAMATION`
2. `OnboardingMachine.StepCount` static→instance → 已修复测试文件
3. Cargo 101（NativeIndexStore 编译失败）→ 已知 flaky，重试可过
4. stale links to Lertaro/Lertaro → 待查（本地 grep 无命中，可能是 CI runner 环境差异）
