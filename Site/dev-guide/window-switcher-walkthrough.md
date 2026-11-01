# Window Switcher Walkthrough

This walkthrough builds a small instant-result plugin that searches visible windows. It focuses on the SDK integration points used by the repository's `WindowSwitcher`; it is an educational example, not a copy of the full plugin. The real plugin also handles process exit races, fuzzy matching, localized labels, cached thumbnails, and a larger window-action menu. Window switching is an instant search provider, not a Quick Panel tab.

## 1. Create the project

Use a .NET 10 Windows class library. The assembly name prefix is required for plugin discovery. Reference the SDK but do not copy a second SDK DLL into the plugin output.

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0-windows</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <AssemblyName>Lertaro.Plugins.MyWindowSearch</AssemblyName>
  </PropertyGroup>
  <ItemGroup>
    <Reference Include="Lertaro.PluginSdk">
      <HintPath>C:\Program Files\Lertaro\Lertaro.PluginSdk.dll</HintPath>
      <Private>false</Private>
    </Reference>
  </ItemGroup>
</Project>
```

For a plugin built inside this repository, the shipped projects use a `ProjectReference` to `PluginSdk/PluginSdk.csproj` with `Private="false"` instead of the installed DLL reference.

## 2. Declare the plugin

The entry point can provide plugin metadata/configuration and register providers. The shipped WindowSwitcher exposes no static actions; its window-specific menu is provided dynamically for each result.

```csharp
using Lertaro.PluginSdk.Abstractions;
using Lertaro.PluginSdk.Abstractions.Plugins;

public sealed class MyWindowSearchPlugin : IPlugin, IConfigurable, IActionProvider
{
    public string Name => "Window Search";
    public string Description => "Find and activate an open window.";

    public IEnumerable<ISearchResultAction> GetActions() => [];

    public IEnumerable<IDynamicActionProvider> GetDynamicActionProviders() =>
        [new WindowMenuActionProvider()];

    public PluginConfigSchema GetConfigSchema() => new()
    {
        Fields =
        [
            new PluginConfigField
            {
                Key = "TriggerKeyword",
                IsTriggerWord = true,
                FieldType = ConfigFieldType.Text,
                DefaultValue = "win",
                RequireNonEmpty = true
            }
        ]
    };
}
```

`WindowMenuActionProvider` is the repository's result-specific menu provider. A custom implementation of `IDynamicActionProvider` must opt into instant results with `CanProvideForInstantResults = true`; otherwise it will not be offered for these results.

## 3. Provide instant results

An `IInstantResultProvider` runs for the current query. The example below shows the essential shape: read the trigger, use the shared trigger parser, enumerate candidate windows, and return actions understood by the host. Adapt `WindowEnumerator` to your own window-discovery code.

```csharp
using System.Diagnostics;
using Lertaro.PluginSdk.Abstractions.Plugins;
using Lertaro.PluginSdk.Services;

public sealed class WindowSearchProvider : IInstantResultProvider
{
    private static string Trigger =>
        PluginSettingsService.GetSetting("Lertaro.Plugins.MyWindowSearch", "TriggerKeyword", "win");

    public string Name => "Open Windows";
    public IReadOnlyList<string> QueryTriggerKeywords => [Trigger];

    public IEnumerable<InstantResultItem> GetInstantResults(string query)
    {
        if (!TriggerWord.TryMatch(query, Trigger, out var term))
            yield break;

        foreach (var window in WindowEnumerator.GetSwitchableWindows())
        {
            using var process = Process.GetProcessById(window.ProcessId);
            if (term.Length > 0 &&
                !window.Title.Contains(term, StringComparison.OrdinalIgnoreCase) &&
                !process.ProcessName.Contains(term, StringComparison.OrdinalIgnoreCase))
                continue;

            yield return new InstantResultItem
            {
                Title = window.Title,
                Description = process.ProcessName,
                ActionType = "Execute",
                ActionArgument = $"activatewindow:{window.Handle.ToInt64()}"
            };
        }
    }
}
```

This reduced sample omits races such as a process exiting during enumeration; production code should account for the failure modes relevant to its data source. The shipped plugin ranks literal title, process-name, and PID matches ahead of fuzzy matches and caps its results at 50. Those are `WindowSwitcher` implementation choices, not SDK guarantees.

## 4. Improve the experience

The shipped plugin reads its trigger setting live (default `win`) and uses `TriggerWord.TryMatch` so trigger parsing stays consistent with the host. It can also request window thumbnails through `WindowThumbnailCache.GetIconOrRefresh`: a cache miss starts capture in the background, so the search does not wait for screenshot work. Both ranking and thumbnails are optional product behavior; keep the initial provider simple and measure before adding extra work to each query.

For a complete implementation, inspect `Plugins/WindowSwitcher/` and the focused tests under `Tests/Plugins/WindowSwitcher/`. The example above intentionally does not reproduce all of its localization, error handling, window enumeration, or menu operations.

## 5. Build and verify

Build your plugin against the SDK version shipped by the Lertaro installation you target, then place its DLL in the App's `Plugins` directory and enable it in **Settings → Plugins**. For the repository's implementation, run its focused tests:

```powershell
dotnet test Tests/Plugins/WindowSwitcher/WindowSwitcher.csproj
```

See [Getting Started](./getting-started), [Packaging & Distribution](./packaging), and the [PluginSdk API Reference](/api/index.html) for the broader plugin lifecycle and API details.
