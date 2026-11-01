# 窗口切换器实战

本指南将构建一个即时结果插件，用于搜索当前可见窗口。内容聚焦于仓库中 `WindowSwitcher` 使用的 SDK 接入点，是教学示例，不是完整插件源码的复制品。正式插件还处理进程退出竞态、模糊匹配、本地化标签、缓存缩略图和更完整的窗口操作菜单。窗口切换属于即时搜索提供器，不是快速面板标签页。

## 1. 创建项目

使用 .NET 10 Windows 类库。程序集名称前缀是插件发现所必需的。引用 SDK，但不要把另一份 SDK DLL 复制到插件输出目录。

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

在本仓库内开发的插件会像已发布插件项目一样，使用指向 `PluginSdk/PluginSdk.csproj` 且设置 `Private="false"` 的 `ProjectReference`，而不是引用已安装目录中的 DLL。

## 2. 声明插件

入口类可提供插件信息和配置，并注册各类提供器。仓库中的 WindowSwitcher 不提供静态操作；它会根据每个搜索结果动态提供窗口操作菜单。

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

`WindowMenuActionProvider` 是仓库中按结果提供操作菜单的实现。自定义 `IDynamicActionProvider` 必须将 `CanProvideForInstantResults` 设为 `true`，否则它不会用于这些即时结果。

## 3. 提供即时结果

`IInstantResultProvider` 会针对当前查询运行。以下代码展示核心流程：读取触发词、调用共享的触发词解析器、枚举候选窗口，并返回宿主能够执行的操作。请将 `WindowEnumerator` 替换为你自己的窗口发现逻辑。

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

精简示例省略了枚举期间进程退出等竞态；生产代码应处理其数据来源可能遇到的故障。正式插件会将标题、进程名和 PID 的字面匹配排在模糊匹配之前，并将结果限制为 50 条。这些是 `WindowSwitcher` 的实现选择，不是 SDK 保证。

## 4. 改善使用体验

正式插件会实时读取触发词设置（默认 `win`），并通过 `TriggerWord.TryMatch` 解析触发词，使其与宿主规则一致。插件还可通过 `WindowThumbnailCache.GetIconOrRefresh` 请求窗口缩略图：缓存未命中时会在后台开始捕获，因此搜索不会等待截图完成。匹配排序和缩略图都是可选的产品行为；先保持提供器精简，再根据测量结果决定是否为每次查询增加额外工作。

如需查看完整实现，请参阅 `Plugins/WindowSwitcher/` 和 `Tests/Plugins/WindowSwitcher/` 下的定向测试。上面的示例有意省略完整的本地化、错误处理、窗口枚举和菜单操作。

## 5. 构建与验证

使用目标 Lertaro 安装版所附的 SDK 版本构建插件，然后将 DLL 放入 App 的 `Plugins` 目录，并在**设置 → 插件**中启用。验证仓库内实现时，运行对应的定向测试：

```powershell
dotnet test Tests/Plugins/WindowSwitcher/WindowSwitcher.csproj
```

更广泛的插件生命周期请参阅[快速上手指南](./getting-started)、[打包与分发](./packaging)以及[PluginSdk API 参考](/api/index.html)。
