# 視窗切換器實戰

本指南會建立一個用來搜尋目前可見視窗的即時結果外掛模組。內容集中於儲存庫中 `WindowSwitcher` 使用的 SDK 接入點，是教學示例，並非完整外掛模組原始碼的複製品。正式外掛模組還會處理程序結束競態、模糊比對、本地化標籤、快取縮圖和更完整的視窗操作選單。視窗切換屬於即時搜尋提供器，不是快速面板分頁。

## 1. 建立專案

使用 .NET 10 Windows 類別庫。組件名稱前綴是外掛模組探索所必需的。參考 SDK，但不要將另一份 SDK DLL 複製到外掛模組輸出目錄。

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

在本儲存庫內開發的外掛模組會像已發佈的外掛模組專案一樣，使用指向 `PluginSdk/PluginSdk.csproj` 並設為 `Private="false"` 的 `ProjectReference`，而非參考已安裝目錄中的 DLL。

## 2. 宣告外掛模組

進入點類別可提供外掛模組資訊和設定，並註冊各類提供器。儲存庫中的 WindowSwitcher 不提供靜態操作；它會根據每個搜尋結果動態提供視窗操作選單。

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
        Fields = [new PluginConfigField
        {
            Key = "TriggerKeyword", IsTriggerWord = true,
            FieldType = ConfigFieldType.Text, DefaultValue = "win", RequireNonEmpty = true
        }]
    };
}
```

`WindowMenuActionProvider` 是儲存庫中按結果提供操作選單的實作。自訂 `IDynamicActionProvider` 必須將 `CanProvideForInstantResults` 設為 `true`，否則不會用於這些即時結果。

## 3. 提供即時結果

`IInstantResultProvider` 會針對目前查詢執行。以下程式碼展示核心流程：讀取觸發詞、呼叫共用觸發詞解析器、列舉候選視窗，並傳回宿主可執行的操作。請將 `WindowEnumerator` 換成自己的視窗探索邏輯。

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
        if (!TriggerWord.TryMatch(query, Trigger, out var term)) yield break;
        foreach (var window in WindowEnumerator.GetSwitchableWindows())
        {
            using var process = Process.GetProcessById(window.ProcessId);
            if (term.Length > 0 &&
                !window.Title.Contains(term, StringComparison.OrdinalIgnoreCase) &&
                !process.ProcessName.Contains(term, StringComparison.OrdinalIgnoreCase)) continue;
            yield return new InstantResultItem
            {
                Title = window.Title, Description = process.ProcessName,
                ActionType = "Execute",
                ActionArgument = $"activatewindow:{window.Handle.ToInt64()}"
            };
        }
    }
}
```

精簡示例省略了列舉期間程序結束等競態；正式程式碼應處理其資料來源可能遇到的故障。正式外掛模組會將標題、程序名稱和 PID 的字面比對排在模糊比對之前，並將結果限制為 50 項。這些是 `WindowSwitcher` 的實作選擇，不是 SDK 保證。

## 4. 改善使用體驗

正式外掛模組會即時讀取觸發詞設定（預設 `win`），並透過 `TriggerWord.TryMatch` 解析觸發詞，使其與宿主規則一致。外掛模組亦可透過 `WindowThumbnailCache.GetIconOrRefresh` 要求視窗縮圖：快取未命中時會在背景開始擷取，因此搜尋不會等待截圖完成。比對排序和縮圖都是可選的產品行為；先保持提供器精簡，再根據量度結果決定是否為每次查詢增加額外工作。

如需查看完整實作，請參閱 `Plugins/WindowSwitcher/` 和 `Tests/Plugins/WindowSwitcher/` 下的定向測試。上面的示例刻意省略完整本地化、錯誤處理、視窗列舉和選單操作。

## 5. 建置與驗證

使用目標 Lertaro 安裝版所附的 SDK 版本建置外掛模組，然後將 DLL 放入 App 的 `Plugins` 目錄，並在**設定 → 外掛模組**中啟用。驗證儲存庫內的實作時，執行對應的定向測試：

```powershell
dotnet test Tests/Plugins/WindowSwitcher/WindowSwitcher.csproj
```

更廣泛的外掛模組生命週期請參閱[快速上手指南](./getting-started)、[打包與分發](./packaging)及[PluginSdk API 參考](/api/index.html)。
