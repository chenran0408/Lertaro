# 윈도우 전환기 워크스루

이 가이드에서는 표시 중인 창을 검색하는 즉시 결과 플러그인을 만듭니다. 저장소의 `WindowSwitcher`가 사용하는 SDK 연결 지점을 소개하는 학습용 예제이며 전체 플러그인 소스를 복제한 것은 아닙니다. 실제 플러그인에는 프로세스 종료 경합 처리, 퍼지 매칭, 현지화, 캐시 썸네일, 더 다양한 창 작업 메뉴가 포함됩니다. 창 전환은 즉시 검색 제공자이며 빠른 패널 탭이 아닙니다.

## 1. 프로젝트 만들기

.NET 10 Windows 클래스 라이브러리를 사용합니다. 어셈블리 이름 접두사는 플러그인 검색에 필요합니다. SDK를 참조하되 별도의 SDK DLL을 플러그인 출력 폴더에 복사하지 마세요.

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

이 저장소에서 개발하는 플러그인은 배포된 플러그인처럼 설치 경로의 DLL 대신 `PluginSdk/PluginSdk.csproj`를 가리키고 `Private="false"`로 설정한 `ProjectReference`를 사용합니다.

## 2. 플러그인 선언하기

진입 클래스는 플러그인 정보와 설정을 제공하고 각 제공자를 등록할 수 있습니다. 저장소의 WindowSwitcher는 정적 작업을 제공하지 않고 각 검색 결과에 맞는 창 메뉴를 동적으로 제공합니다.

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

`WindowMenuActionProvider`는 저장소의 결과별 메뉴 구현입니다. 사용자 정의 `IDynamicActionProvider`를 즉시 결과에 표시하려면 `CanProvideForInstantResults = true`를 지정해야 합니다.

## 3. 즉시 결과 제공하기

`IInstantResultProvider`는 현재 쿼리에 대해 실행됩니다. 다음 예제는 트리거 읽기, 공용 파서 사용, 후보 창 열거, 호스트가 실행할 수 있는 작업 반환이라는 기본 흐름을 보여 줍니다. `WindowEnumerator`는 직접 구현한 창 검색 로직으로 바꾸세요.

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

간단한 예제는 열거 중 프로세스가 종료되는 경우와 같은 경합을 생략합니다. 실제 코드는 데이터 소스에서 발생할 수 있는 실패를 처리해야 합니다. 제품 플러그인은 제목, 프로세스 이름, PID의 부분 일치를 퍼지 매칭보다 우선하고 결과를 최대 50개로 제한합니다. 이는 `WindowSwitcher`의 구현 선택일 뿐 SDK 보장이 아닙니다.

## 4. 사용성 개선

제품 플러그인은 트리거 설정(기본값 `win`)을 매번 읽고 `TriggerWord.TryMatch`를 사용하여 호스트와 같은 규칙을 적용합니다. `WindowThumbnailCache.GetIconOrRefresh`로 창 썸네일을 요청할 수도 있습니다. 캐시가 비어 있으면 백그라운드에서 캡처하므로 검색은 캡처 완료를 기다리지 않습니다. 정렬과 썸네일은 선택적인 제품 기능입니다. 먼저 제공자를 간결하게 유지하고 측정한 뒤 각 쿼리에 추가 작업이 필요한지 결정하세요.

전체 구현은 `Plugins/WindowSwitcher/`와 `Tests/Plugins/WindowSwitcher/`의 해당 테스트를 참조하세요. 이 예제에서는 현지화, 상세 오류 처리, 창 열거, 메뉴 작업을 의도적으로 생략했습니다.

## 5. 빌드 및 확인

대상 Lertaro 설치본에 포함된 SDK를 기준으로 빌드한 다음 DLL을 App의 `Plugins` 폴더에 배치하고 **설정 → 플러그인**에서 활성화합니다. 저장소의 구현을 확인하려면 다음 개별 테스트를 실행하세요.

```powershell
dotnet test Tests/Plugins/WindowSwitcher/WindowSwitcher.csproj
```

플러그인 전반은 [시작하기](./getting-started), [패키징 및 배포](./packaging), [PluginSdk API 참조](/api/index.html)를 확인하세요.
