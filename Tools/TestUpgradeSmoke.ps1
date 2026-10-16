param(
    [Parameter(Mandatory=$true)][string]$PackageDirectory,
    [Parameter(Mandatory=$true)][string]$Architecture
)
$ErrorActionPreference = 'Stop'
$suffix = if ($Architecture -eq 'Arm64') { '-arm64' } else { '' }
$install = Join-Path $env:RUNNER_TEMP 'Lertaro-Beta-Upgrade-Smoke'
$oldSetup = Join-Path $env:RUNNER_TEMP "Lertaro-beta7-Setup$suffix.exe"
$newZip = Join-Path (Resolve-Path -LiteralPath $PackageDirectory).Path "Lertaro-Portable$suffix.zip"
$stage = Join-Path $install ('update-payload\' + [Guid]::NewGuid().ToString('N'))
$app = Join-Path $install 'Lertaro.App.exe'
if ([Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString() -ne $Architecture) {
    throw "Wrong runner architecture for $Architecture."
}
if (Test-Path -LiteralPath $install) { throw 'Upgrade install directory already exists.' }

$verifier = [Security.Cryptography.ECDsa]::Create()
try {
    $verifier.ImportFromPem([IO.File]::ReadAllText((Join-Path $PSScriptRoot '..\release-signing-public-key.pem')))
    if (!$verifier.VerifyData([IO.File]::ReadAllBytes($newZip), [IO.File]::ReadAllBytes($newZip + '.sig'),
        [Security.Cryptography.HashAlgorithmName]::SHA256,
        [Security.Cryptography.DSASignatureFormat]::Rfc3279DerSequence)) { throw 'Candidate update signature failed.' }
} finally { $verifier.Dispose() }

$installed = $false
try {
    $url = "https://github.com/chenran0408/Lertaro/releases/download/v5.10.0-beta7/Lertaro-Setup$suffix.exe"
    Invoke-WebRequest -Uri $url -OutFile $oldSetup
    $setup = Start-Process -FilePath $oldSetup -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/SP-',"/DIR=$install") -PassThru
    if (!$setup.WaitForExit(480000)) { $setup.Kill(); throw 'beta7 installer timed out.' }
    if ($setup.ExitCode -ne 0) { throw "beta7 installer failed: $($setup.ExitCode)" }
    $installed = $true
    if (!(Test-Path -LiteralPath $app) -or (Get-Item $app).VersionInfo.ProductVersion -notlike '5.10.0-beta7*') {
        throw 'beta7 app was not installed.'
    }
    Expand-Archive -LiteralPath $newZip -DestinationPath $stage
    $payload = Join-Path $stage 'Lertaro'
    $stdout = Join-Path $env:RUNNER_TEMP "Lertaro-upgrade-$Architecture.out.log"
    $stderr = Join-Path $env:RUNNER_TEMP "Lertaro-upgrade-$Architecture.err.log"
    $copy = Start-Process -FilePath (Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe') -ArgumentList @(
        '-NoProfile','-NonInteractive','-File',"`"$(Join-Path $install 'portable-updater.ps1')`"",
        '-Source',"`"$payload`"",'-Destination',"`"$install`"") -Wait -PassThru -RedirectStandardOutput $stdout -RedirectStandardError $stderr
    if ($copy.ExitCode -ne 0) {
        Get-Content -LiteralPath $stdout,$stderr -ErrorAction SilentlyContinue | Write-Output
        $resultPath = Join-Path $install 'update-result.json'
        if (Test-Path -LiteralPath $resultPath) { Get-Content -LiteralPath $resultPath | Write-Output }
        throw "Updater failed: $($copy.ExitCode)"
    }
    $result = Get-Content -LiteralPath (Join-Path $install 'update-result.json') -Raw | ConvertFrom-Json
    if ($result.Status -ne 'success') { throw "Updater reported $($result.Status): $($result.Message)" }
    if ((Get-Item $app).VersionInfo.ProductVersion -notlike '5.10.0-beta8*') { throw 'App version did not advance.' }
    if (Test-Path -LiteralPath $stage) { throw 'Completed update left a staging directory.' }
    $service = Get-Service -Name LertaroService -ErrorAction Stop
    $service.WaitForStatus('Running', [TimeSpan]::FromSeconds(20))
    if ($service.Status -ne 'Running') { throw "Service did not restart: $($service.Status)" }
    Write-Output "Upgraded $Architecture from beta7 to beta8, result=success, service=Running."
}
finally {
    if ($installed) {
        $uninstaller = Join-Path $install 'unins000.exe'
        if (Test-Path -LiteralPath $uninstaller) {
            $uninstall = Start-Process -FilePath $uninstaller -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART') -Wait -PassThru
            if ($uninstall.ExitCode -ne 0) { throw "Uninstall failed: $($uninstall.ExitCode)" }
            Write-Output "Uninstalled upgraded $Architecture."
        }
    }
}
