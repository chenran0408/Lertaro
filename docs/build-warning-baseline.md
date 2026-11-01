# Initial build warning inventory (2026-11-02)

This is a snapshot from targeted Release builds and tests with .NET SDK 10.0.100, C# 14 and build-time code style checks enabled. It covers the projects listed below. Diagnostics are deduplicated by project, source location and rule; WPF temporary project names and MSBuild node prefixes are normalized.

The projects retain their warnings until each is cleaned and added to the strict list. Missing documentation setup (`EnableGenerateDocumentationFile`) currently prevents IDE0005 from checking those projects; enabling XML documentation may reveal additional warnings. This inventory therefore does not establish that those projects have no unused imports.

| Project | Unique diagnostics | Rules |
| --- | ---: | --- |
| `App/App.csproj` | 19 | EnableGenerateDocumentationFile, IDE0007, IDE0019, IDE0021, IDE0022, IDE0025, IDE0031 |
| `Core/Core.csproj` | 8 | EnableGenerateDocumentationFile, IDE0007, IDE0022, IDE0031, IDE0053, IDE0270 |
| `Plugins/BrowserData/BrowserData.csproj` | 1 | EnableGenerateDocumentationFile |
| `Plugins/Calendar/Calendar.csproj` | 1 | EnableGenerateDocumentationFile |
| `Plugins/ClipboardHistory/ClipboardHistory.csproj` | 1 | EnableGenerateDocumentationFile |
| `Plugins/ContentSearch/ContentSearch.csproj` | 1 | EnableGenerateDocumentationFile |
| `Plugins/CustomActions/CustomActions.csproj` | 1 | EnableGenerateDocumentationFile |
| `Plugins/CustomCommands/CustomCommands.csproj` | 1 | EnableGenerateDocumentationFile |
| `Plugins/SpanishAlias/SpanishAlias.csproj` | 1 | EnableGenerateDocumentationFile |
| `Plugins/SystemSettings/SystemSettings.csproj` | 1 | EnableGenerateDocumentationFile |
| `Plugins/WebSearch/WebSearch.csproj` | 1 | EnableGenerateDocumentationFile |
| `PluginSdk/PluginSdk.csproj` | 1 | EnableGenerateDocumentationFile |
| `Service/Service.csproj` | 1 | EnableGenerateDocumentationFile |
| `Tests/App/App.csproj` | 14 | EnableGenerateDocumentationFile, IDE0022, IDE0270 |
| `Tests/Core/Core.csproj` | 13 | EnableGenerateDocumentationFile, IDE0022 |
| `Tests/Plugins/ClipboardHistory/ClipboardHistory.csproj` | 3 | EnableGenerateDocumentationFile, IDE0022 |
| `Tests/Plugins/SpanishAlias/SpanishAlias.csproj` | 1 | EnableGenerateDocumentationFile |
| `Tests/PluginSdk/PluginSdk.csproj` | 2 | EnableGenerateDocumentationFile, IDE0022 |
| `Tools/CompactionBenchmark/CompactionBenchmark.csproj` | 0 | Warnings are errors |
| `Tests/Tools/SourceSize/SourceSize.csproj` | 0 | Warnings are errors |

## Locations

### App/App.csproj

- `App/Helpers/HotkeyActionTrigger.cs(106,9)`: `IDE0022`
- `App/Helpers/SearchInputHelper.cs(345,9)`: `IDE0019`
- `App/Helpers/SearchInputHelper.cs(37,9)`: `IDE0022`
- `App/Services/AppWindow/AppWindowManager.cs(76,9)`: `IDE0022`
- `App/Services/InlineSearch/InlineSearchWindowCreationSupport.cs(61,9)`: `IDE0022`
- `App/Services/Notifications/NotificationService.cs(320,9)`: `IDE0022`
- `App/Services/QuickLookManager.cs(310,9)`: `IDE0022`
- `App/Services/Theme/ThemeManager.cs(39,9)`: `IDE0007`
- `App/ViewModels/Search/Dispatch/SearchQueryDispatchController.cs(444,9)`: `IDE0022`
- `App/ViewModels/Settings/FavoritesSettingsViewModel.cs(242,9)`: `IDE0022`
- `App/Views/Controls/Dialogs/OwnedDialog.cs(90,9)`: `IDE0022`
- `App/Views/InlineSearchWindow/Helpers/InlineCardSpace.cs(92,9)`: `IDE0007`
- `App/Views/Notifications/NotificationNoticeWindow.xaml.cs(14,9)`: `IDE0021`
- `App/Views/QuickPanel/QuickPanelItemReordering.cs(184,17)`: `IDE0019`
- `App/Views/QuickPanel/QuickPanelItemReordering.cs(233,13)`: `IDE0031`
- `App/Views/SearchWindow/SearchWindowChromeHandler.cs(37,9)`: `IDE0022`
- `App/Views/Settings/AboutSettingsPage.xaml.cs(236,9)`: `IDE0022`
- `App/Views/Settings/AboutSettingsPage.xaml.cs(48,13)`: `IDE0025`
- `CSC`: `EnableGenerateDocumentationFile`

### Core/Core.csproj

- `Core/Hook/Ipc/HookProcess.cs(66,49)`: `IDE0031`
- `Core/IndexV2/Compaction.cs(35,9)`: `IDE0022`
- `Core/IndexV2/Persistence/SnapshotWriter.cs(123,9)`: `IDE0007`
- `Core/IndexV2/Search/SearchMatcher.cs(210,13)`: `IDE0053`
- `Core/Services/Search/CallerVisibility.cs(129,30)`: `IDE0053`
- `Core/Services/Search/SearchService.cs(108,72)`: `IDE0053`
- `Core/Services/UsnServicePipeServer.cs(44,9)`: `IDE0270`
- `CSC`: `EnableGenerateDocumentationFile`

### Plugins/BrowserData/BrowserData.csproj

- `CSC`: `EnableGenerateDocumentationFile`

### Plugins/Calendar/Calendar.csproj

- `CSC`: `EnableGenerateDocumentationFile`

### Plugins/ClipboardHistory/ClipboardHistory.csproj

- `CSC`: `EnableGenerateDocumentationFile`

### Plugins/ContentSearch/ContentSearch.csproj

- `CSC`: `EnableGenerateDocumentationFile`

### Plugins/CustomActions/CustomActions.csproj

- `CSC`: `EnableGenerateDocumentationFile`

### Plugins/CustomCommands/CustomCommands.csproj

- `CSC`: `EnableGenerateDocumentationFile`

### Plugins/SpanishAlias/SpanishAlias.csproj

- `CSC`: `EnableGenerateDocumentationFile`

### Plugins/SystemSettings/SystemSettings.csproj

- `CSC`: `EnableGenerateDocumentationFile`

### Plugins/WebSearch/WebSearch.csproj

- `CSC`: `EnableGenerateDocumentationFile`

### PluginSdk/PluginSdk.csproj

- `CSC`: `EnableGenerateDocumentationFile`

### Service/Service.csproj

- `CSC`: `EnableGenerateDocumentationFile`

### Tests/App/App.csproj

- `CSC`: `EnableGenerateDocumentationFile`
- `Tests/App/Helpers/CalendarTextServiceTests.cs(34,9)`: `IDE0022`
- `Tests/App/Helpers/CalendarTextServiceTests.cs(41,9)`: `IDE0022`
- `Tests/App/Helpers/CalendarTextServiceTests.cs(48,9)`: `IDE0022`
- `Tests/App/Helpers/CalendarTextServiceTests.cs(55,9)`: `IDE0022`
- `Tests/App/Search/SystemSettingsKeywordTests.cs(15,9)`: `IDE0022`
- `Tests/App/Search/SystemSettingsKeywordTests.cs(22,9)`: `IDE0022`
- `Tests/App/ViewModels/Settings/Plugins/PluginConfigEntryTransferTests.cs(410,9)`: `IDE0022`
- `Tests/App/Views/Controls/StatusBarControlTests.cs(112,9)`: `IDE0270`
- `Tests/App/Views/SearchWindow/SearchWindowFilterShortcutTests.cs(12,9)`: `IDE0022`
- `Tests/App/Views/SearchWindow/SearchWindowFilterShortcutTests.cs(19,9)`: `IDE0022`
- `Tests/App/Views/SearchWindow/SearchWindowFilterShortcutTests.cs(25,9)`: `IDE0022`
- `Tests/App/Views/Settings/Hotkey/HotkeyBlacklistTabSectionTests.cs(26,9)`: `IDE0270`
- `Tests/App/Views/Settings/Hotkey/HotkeyBlacklistTabSectionTests.cs(77,9)`: `IDE0270`

### Tests/Core/Core.csproj

- `CSC`: `EnableGenerateDocumentationFile`
- `Tests/Core/NaturalNameComparerTests.cs(23,9)`: `IDE0022`
- `Tests/Core/SearchIndex/AliasMatchRulesTests.cs(100,9)`: `IDE0022`
- `Tests/Core/SearchIndex/AliasMatchRulesTests.cs(115,9)`: `IDE0022`
- `Tests/Core/SearchIndex/AliasMatchRulesTests.cs(24,9)`: `IDE0022`
- `Tests/Core/SearchIndex/AliasMatchRulesTests.cs(30,9)`: `IDE0022`
- `Tests/Core/SearchIndex/AliasMatchRulesTests.cs(37,9)`: `IDE0022`
- `Tests/Core/SearchIndex/AliasMatchRulesTests.cs(45,9)`: `IDE0022`
- `Tests/Core/SearchIndex/AliasMatchRulesTests.cs(53,9)`: `IDE0022`
- `Tests/Core/SearchIndex/AliasMatchRulesTests.cs(60,9)`: `IDE0022`
- `Tests/Core/SearchIndex/AliasMatchRulesTests.cs(66,9)`: `IDE0022`
- `Tests/Core/SearchIndex/AliasMatchRulesTests.cs(94,9)`: `IDE0022`
- `Tests/Core/SearchIndex/HighlightMaskTests.cs(122,9)`: `IDE0022`

### Tests/Plugins/ClipboardHistory/ClipboardHistory.csproj

- `CSC`: `EnableGenerateDocumentationFile`
- `Tests/Plugins/ClipboardHistory/Capture/ClipboardExclusionListTests.cs(18,9)`: `IDE0022`
- `Tests/Plugins/ClipboardHistory/Capture/ClipboardExclusionListTests.cs(24,9)`: `IDE0022`

### Tests/Plugins/SpanishAlias/SpanishAlias.csproj

- `CSC`: `EnableGenerateDocumentationFile`

### Tests/PluginSdk/PluginSdk.csproj

- `CSC`: `EnableGenerateDocumentationFile`
- `Tests/PluginSdk/Helpers/UserPathResolverTests.cs(98,9)`: `IDE0022`
