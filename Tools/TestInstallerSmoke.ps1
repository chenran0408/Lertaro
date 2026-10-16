param(
    [Parameter(Mandatory=$true)][string]$InstallerPath,
    [Parameter(Mandatory=$true)][string]$Architecture
)
$ErrorActionPreference = 'Stop'
$installer = (Resolve-Path -LiteralPath $InstallerPath).Path
$installDirectory = Join-Path $env:RUNNER_TEMP 'Lertaro-Beta-Install-Smoke'
$installLog = Join-Path $env:RUNNER_TEMP 'Lertaro-Beta-Install-Smoke.log'
if ([Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString() -ne $Architecture) {
    throw "Wrong runner architecture for $Architecture package."
}
if (Test-Path -LiteralPath $installDirectory) { throw 'Smoke install directory already exists.' }
$installed = $false
try {
    Write-Output "Available runtimes: $(dotnet --list-runtimes | Out-String)"
    $clock = [Diagnostics.Stopwatch]::StartNew()
    $setup = Start-Process -FilePath $installer -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/SP-',"/DIR=$installDirectory","/LOG=$installLog") -PassThru
    if (!$setup.WaitForExit(480000)) {
        $setup.Kill()
        throw 'Installer did not finish within eight minutes.'
    }
    if ($setup.ExitCode -ne 0) { throw "Installer failed with code $($setup.ExitCode)." }
    $installed = $true
    $installTime = $clock.Elapsed.TotalSeconds
    $app = Join-Path $installDirectory 'Lertaro.App.exe'
    $service = Join-Path $installDirectory 'Lertaro.Service.exe'
    $plugins = @(Get-ChildItem -LiteralPath (Join-Path $installDirectory 'Plugins') -Recurse -Filter 'Lertaro.Plugins.*.dll' -File)
    if (!(Test-Path -LiteralPath $app) -or !(Test-Path -LiteralPath $service) -or $plugins.Count -lt 31) {
        throw "Incomplete install: $($plugins.Count) plugins."
    }
    $size = (Get-ChildItem -LiteralPath $installDirectory -Recurse -File | Measure-Object Length -Sum).Sum
    Start-Sleep -Seconds 5
    $running = @(Get-Process -Name 'Lertaro.App' -ErrorAction SilentlyContinue | Where-Object Path -eq $app)
    if ($running.Count -eq 0) { throw 'The app launched by the installer did not remain running.' }
    Write-Output "Installed ${Architecture}: size=$size bytes, install=$([math]::Round($installTime,2)) s, first start survived 5 s, plugins=$($plugins.Count)."
}
catch {
    if (Test-Path -LiteralPath $installLog) { Get-Content -LiteralPath $installLog -Tail 50 }
    throw
}
finally {
    if ($installed) {
        $uninstaller = Join-Path $installDirectory 'unins000.exe'
        if (Test-Path -LiteralPath $uninstaller) {
            $result = Start-Process -FilePath $uninstaller -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART') -Wait -PassThru
            if ($result.ExitCode -ne 0) { throw "Uninstaller failed with code $($result.ExitCode)." }
            if (Test-Path -LiteralPath $app) { throw 'Application remained after uninstall.' }
            Write-Output "Uninstalled $Architecture."
        }
    }
}
