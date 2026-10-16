param(
    [Parameter(Mandatory=$true)][string]$InstallerPath,
    [Parameter(Mandatory=$true)][string]$Architecture
)
$ErrorActionPreference = 'Stop'
$installer = (Resolve-Path -LiteralPath $InstallerPath).Path
$installDirectory = Join-Path $env:RUNNER_TEMP 'Lertaro-Beta-Install-Smoke'
if ([Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString() -ne $Architecture) {
    throw "Wrong runner architecture for $Architecture package."
}
if (Test-Path -LiteralPath $installDirectory) { throw 'Smoke install directory already exists.' }
$installed = $false
try {
    $clock = [Diagnostics.Stopwatch]::StartNew()
    $setup = Start-Process -FilePath $installer -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/SP-',"/DIR=$installDirectory") -Wait -PassThru
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
    $process = Start-Process -FilePath $app -PassThru
    Start-Sleep -Seconds 5
    $process.Refresh()
    if ($process.HasExited) { throw "First start exited with code $($process.ExitCode)." }
    Write-Output "Installed $Architecture: size=$size bytes, install=$([math]::Round($installTime,2)) s, first start survived 5 s, plugins=$($plugins.Count)."
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
