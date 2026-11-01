# Tutorial del conmutador de ventanas

Esta guía crea un proveedor de resultados instantáneos para buscar ventanas visibles. Presenta los puntos de integración del SDK que usa `WindowSwitcher` en este repositorio; es un ejemplo didáctico, no una copia del plugin completo. El plugin real también gestiona las carreras cuando termina un proceso, la búsqueda difusa, las traducciones, las miniaturas en caché y un menú de acciones más amplio. El cambio de ventanas es un proveedor de búsqueda instantánea, no una pestaña del Panel rápido.

## 1. Crear el proyecto

Usa una biblioteca de clases para Windows con .NET 10. El prefijo del nombre del ensamblado es necesario para que Lertaro detecte el plugin. Referencia el SDK, pero no copies otra DLL del SDK a la salida del plugin.

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

Dentro de este repositorio, los plugins publicados usan un `ProjectReference` a `PluginSdk/PluginSdk.csproj` con `Private="false"` en lugar de referenciar la DLL instalada.

## 2. Declarar el plugin

La clase de entrada puede proporcionar los metadatos y la configuración del plugin, además de registrar proveedores. El WindowSwitcher del repositorio no ofrece acciones estáticas: presenta dinámicamente un menú de acciones para cada resultado.

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

`WindowMenuActionProvider` es la implementación del repositorio para los menús específicos de cada resultado. Un `IDynamicActionProvider` propio debe establecer `CanProvideForInstantResults = true` para aparecer en estos resultados.

## 3. Proporcionar resultados instantáneos

`IInstantResultProvider` se ejecuta para la consulta actual. Este ejemplo muestra el flujo esencial: leer el activador, usar el analizador compartido, enumerar ventanas candidatas y devolver acciones que el host pueda ejecutar. Sustituye `WindowEnumerator` por tu propia lógica de descubrimiento de ventanas.

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

El ejemplo reducido omite carreras como que un proceso termine durante la enumeración; el código de producción debe gestionar los fallos propios de su fuente de datos. El plugin real prioriza las coincidencias literales en título, nombre de proceso y PID frente a la búsqueda difusa, y limita los resultados a 50. Son decisiones de implementación de `WindowSwitcher`, no garantías del SDK.

## 4. Mejorar la experiencia

El plugin real lee la configuración del activador en cada consulta (valor predeterminado `win`) y usa `TriggerWord.TryMatch` para seguir las mismas reglas que el host. También puede pedir miniaturas con `WindowThumbnailCache.GetIconOrRefresh`: si no hay ninguna en caché, la captura se inicia en segundo plano y la búsqueda no espera. Tanto la clasificación como las miniaturas son funciones opcionales. Mantén el proveedor sencillo al principio y mide antes de añadir trabajo a cada consulta.

Consulta `Plugins/WindowSwitcher/` y sus pruebas específicas en `Tests/Plugins/WindowSwitcher/` para ver la implementación completa. Este ejemplo omite intencionadamente las traducciones, el tratamiento detallado de errores, la enumeración de ventanas y las acciones del menú.

## 5. Compilar y verificar

Compila con la versión del SDK incluida en la instalación de Lertaro a la que te dirijas, coloca la DLL en la carpeta `Plugins` de App y activa el plugin en **Configuración → Plugins**. Para verificar la implementación del repositorio, ejecuta estas pruebas específicas:

```powershell
dotnet test Tests/Plugins/WindowSwitcher/WindowSwitcher.csproj
```

Consulta [Primeros pasos](./getting-started), [Empaquetado y distribución](./packaging) y la [referencia de la API de PluginSdk](/api/index.html) para obtener más información sobre el ciclo de vida de los plugins.
