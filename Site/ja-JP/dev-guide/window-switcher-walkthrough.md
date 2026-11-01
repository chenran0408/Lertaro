# ウィンドウ切り替えウォークスルー

このガイドでは、表示中のウィンドウを検索する即時結果プラグインを作成します。リポジトリの `WindowSwitcher` が使う SDK 連携ポイントを紹介する教育用の例であり、完全なプラグインソースの複製ではありません。実際のプラグインには、プロセス終了時の競合処理、ファジーマッチ、ローカライズ、キャッシュ付きサムネイル、より多くのウィンドウ操作メニューも含まれます。ウィンドウ切り替えは即時検索プロバイダーであり、クイックパネルのタブではありません。

## 1. プロジェクトを作成する

.NET 10 Windows クラスライブラリを使います。アセンブリ名のプレフィックスはプラグイン検出に必要です。SDK を参照しますが、別の SDK DLL をプラグイン出力先へコピーしないでください。

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

このリポジトリ内で開発する場合、同梱プラグインと同様に、インストール先の DLL ではなく `PluginSdk/PluginSdk.csproj` への `Private="false"` 付き `ProjectReference` を使います。

## 2. プラグインを宣言する

エントリーポイントはプラグイン情報と設定を提供し、各プロバイダーを登録できます。リポジトリの WindowSwitcher は静的アクションを提供せず、結果ごとのウィンドウメニューを動的に提供します。

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

`WindowMenuActionProvider` はリポジトリにある結果別メニューの実装です。独自の `IDynamicActionProvider` を即時結果にも表示するには、`CanProvideForInstantResults = true` を指定します。

## 3. 即時結果を提供する

`IInstantResultProvider` は現在のクエリに対して実行されます。次の例は、トリガーの読み取り、共通パーサーの使用、候補ウィンドウの列挙、ホストが実行できるアクションの返却という基本形を示します。`WindowEnumerator` は用途に合わせたウィンドウ検索処理に置き換えてください。

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

簡略化した例では列挙中にプロセスが終了する場合などの競合を省いています。実際のコードではデータソースに応じた失敗を処理してください。製品版はタイトル、プロセス名、PID の部分一致をファジーマッチより優先し、結果を50件までに制限します。これらは `WindowSwitcher` 固有の実装であり、SDK の保証ではありません。

## 4. 使い勝手を高める

製品版はトリガー設定（既定値 `win`）を都度読み込み、`TriggerWord.TryMatch` でホストと同じ規則に従います。また、`WindowThumbnailCache.GetIconOrRefresh` でウィンドウのサムネイルを要求できます。キャッシュになければバックグラウンドで取得するため、検索はキャプチャ完了を待ちません。並べ替えやサムネイルは任意の製品機能です。まずは提供処理を小さく保ち、測定後に各クエリへ追加処理を加えるか判断してください。

完全な実装は `Plugins/WindowSwitcher/` と `Tests/Plugins/WindowSwitcher/` の個別テストを参照してください。この例ではローカライズ、詳細なエラー処理、ウィンドウ列挙、メニュー操作を意図的に省略しています。

## 5. ビルドと確認

対象とする Lertaro のインストール版に付属する SDK を参照してビルドし、DLL を App の `Plugins` フォルダーに配置して**設定 → プラグイン**で有効にします。リポジトリの実装を確認する場合は、次の個別テストを実行します。

```powershell
dotnet test Tests/Plugins/WindowSwitcher/WindowSwitcher.csproj
```

プラグイン全般については[クイックスタート](./getting-started)、[パッケージングと配布](./packaging)、[PluginSdk API リファレンス](/api/index.html)を参照してください。
