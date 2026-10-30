param(
    [Parameter(Mandatory=$true)][string]$PackageDirectory,
    [Parameter(Mandatory=$true)][string]$ClientDirectory,
    [Parameter(Mandatory=$true)][ValidateSet('X64','Arm64')][string]$Architecture
)
$ErrorActionPreference = 'Stop'
if (!$env:GITHUB_ACTIONS) { throw 'Run this machine-changing test only on a disposable GitHub Actions runner.' }
if ([Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString() -ne $Architecture) {
    throw "Wrong runner architecture for $Architecture."
}
$suffix = if ($Architecture -eq 'Arm64') { '-arm64' } else { '' }
$package = (Resolve-Path -LiteralPath $PackageDirectory).Path
$client = (Resolve-Path -LiteralPath $ClientDirectory).Path
$install = Join-Path $env:ProgramFiles ('Lertaro-StandardUser-Smoke-' + [Guid]::NewGuid().ToString('N'))
$scratch = Join-Path $env:RUNNER_TEMP ('Lertaro-StandardUser-' + [Guid]::NewGuid().ToString('N'))
$user = 'LertaroSmoke'
$password = [Guid]::NewGuid().ToString('N') + '!aA9'
$secure = ConvertTo-SecureString $password -AsPlainText -Force
$job = $null
$installed = $false
$createdUser = $false
try {
    New-Item -ItemType Directory -Path $scratch | Out-Null
    New-LocalUser -Name $user -Password $secure -AccountNeverExpires | Out-Null
    $createdUser = $true
    $account = Get-LocalUser -Name $user
    Add-LocalGroupMember -SID 'S-1-5-32-545' -Member $account
    & icacls.exe $scratch /grant "*$($account.SID.Value):(OI)(CI)M" | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Unable to grant the probe output directory.' }
    $setup = Start-Process -FilePath (Join-Path $package "Lertaro-Setup$suffix.exe") -WindowStyle Hidden -ArgumentList @(
        '/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/SP-',"/DIR=`"$install`"") -PassThru
    if (!$setup.WaitForExit(480000)) { throw 'Installer timed out.' }
    if ($setup.ExitCode -ne 0) { throw "Installer failed: $($setup.ExitCode)" }
    $installed = $true
    $app = Join-Path $install 'Lertaro.App.exe'
    $dataDirectory = Join-Path $install 'Data'
    New-Item -ItemType Directory -Force -Path $dataDirectory | Out-Null
    $sentinel = Join-Path $dataDirectory 'update-smoke-settings.txt'
    [IO.File]::WriteAllText($sentinel, 'keep-user-data')
    $expectedHash = (Get-FileHash -LiteralPath $app -Algorithm SHA256).Hash
    foreach ($process in Get-Process -Name Lertaro.App -ErrorAction SilentlyContinue) {
        try { if ($process.Path -eq $app) { $process.Kill(); $process.WaitForExit() } }
        finally { $process.Dispose() }
    }
    $serviceInstall = Start-Process -FilePath (Join-Path $install 'Lertaro.Service.exe') -WindowStyle Hidden -ArgumentList '--install' -Wait -PassThru
    if ($serviceInstall.ExitCode -ne 0) { throw 'Service installation failed.' }
    (Get-Service LertaroService).WaitForStatus('Running', [TimeSpan]::FromSeconds(30))

    # Only instrument the caller host; the installed App/Core libraries and SYSTEM service stay intact.
    foreach ($name in @('Lertaro.UpdateSmoke.dll','Lertaro.UpdateSmoke.deps.json','Lertaro.UpdateSmoke.runtimeconfig.json')) {
        Copy-Item -LiteralPath (Join-Path $client $name) -Destination $install
    }
    Copy-Item -LiteralPath (Join-Path $client 'Lertaro.UpdateSmoke.exe') -Destination $app -Force
    $portFile = Join-Path $scratch 'port'
    $requestLog = Join-Path $scratch 'http-requests.log'
    $serverErrorFile = Join-Path $scratch 'http-server-error.txt'
    $job = Start-Job -ArgumentList $package,$suffix,$portFile,$requestLog,$serverErrorFile -ScriptBlock {
        param($package,$suffix,$portFile,$requestLog,$serverErrorFile)
        $ErrorActionPreference = 'Stop'
        $listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
        $listener.Start()
        try {
            [IO.File]::WriteAllText($portFile, [string]$listener.LocalEndpoint.Port)
            for ($i = 0; $i -lt 4; $i++) {
                $connection = $listener.AcceptTcpClient()
                try {
                    $stream = $connection.GetStream()
                    $reader = [IO.StreamReader]::new($stream, [Text.Encoding]::ASCII, $false, 1024, $true)
                    $requestLine = $reader.ReadLine()
                    $request = $requestLine.Split(' ')[1]
                    Add-Content -LiteralPath $requestLog -Value $request
                    while ($reader.ReadLine()) { }
                    $file = switch ($request) {
                        '/package.zip' { Join-Path $package "Lertaro-Portable$suffix.zip" }
                        '/package.zip.sig' { Join-Path $package "Lertaro-Portable$suffix.zip.sig" }
                        default { $null }
                    }
                    [byte[]]$body = if ($file) { [IO.File]::ReadAllBytes($file) } else { [byte[]]::new(0) }
                    $status = if ($file) { '200 OK' } else { '404 Not Found' }
                    $header = [Text.Encoding]::ASCII.GetBytes("HTTP/1.1 $status`r`nContent-Length: $($body.Length)`r`nConnection: close`r`n`r`n")
                    $stream.Write($header, 0, $header.Length)
                    $stream.Write($body, 0, $body.Length)
                    $stream.Flush()
                } finally { $connection.Dispose() }
            }
        } catch {
            [IO.File]::WriteAllText($serverErrorFile, $_.ToString())
            throw
        } finally { $listener.Stop() }
    }
    $clock = [Diagnostics.Stopwatch]::StartNew()
    while (!(Test-Path -LiteralPath $portFile)) {
        if ($job.State -eq 'Failed' -or $clock.Elapsed.TotalSeconds -gt 30) { throw 'Package server did not start.' }
        Start-Sleep -Milliseconds 100
    }
    $port = [IO.File]::ReadAllText($portFile)
    $resultFile = Join-Path $scratch 'handoff.txt'
    $credential = [PSCredential]::new("$env:COMPUTERNAME\$user", $secure)
    Start-Service seclogon
    $probe = Start-Process -FilePath $app -Credential $credential -LoadUserProfile -WindowStyle Hidden -WorkingDirectory $install -ArgumentList @(
        "http://127.0.0.1:$port", "`"$resultFile`"") -Environment @{ TEMP = $scratch; TMP = $scratch } -PassThru
    if (!$probe.WaitForExit(120000)) { throw 'Standard-user update handoff timed out.' }
    if ($probe.ExitCode -ne 0) {
        $requests = Get-Content -LiteralPath $requestLog -Raw -ErrorAction SilentlyContinue
        $serverError = Get-Content -LiteralPath $serverErrorFile -Raw -ErrorAction SilentlyContinue
        throw "Probe failed: $(Get-Content -LiteralPath $resultFile -Raw -ErrorAction SilentlyContinue) Requests: $requests Server: $serverError"
    }
    if ([IO.File]::ReadAllText($resultFile) -ne 'standard-user;download-retry;verified;service-accepted') { throw 'Missing standard-user handoff proof.' }
    $resultPath = Join-Path $install 'update-result.json'
    $clock.Restart()
    while (!(Test-Path -LiteralPath $resultPath)) {
        if ($clock.Elapsed.TotalSeconds -gt 120) { throw 'The service updater did not finish.' }
        Start-Sleep -Milliseconds 250
    }
    $result = Get-Content -LiteralPath $resultPath -Raw | ConvertFrom-Json
    if ($result.Status -ne 'success') { throw "Updater failed: $($result.Message)" }
    if ((Get-FileHash -LiteralPath $app -Algorithm SHA256).Hash -ne $expectedHash) { throw 'The updater did not restore the signed application host.' }
    if ([IO.File]::ReadAllText($sentinel) -ne 'keep-user-data') { throw 'Updating changed the existing user data.' }
    (Get-Service LertaroService).WaitForStatus('Running', [TimeSpan]::FromSeconds(30))
    Write-Output "Verified $Architecture standard-user download, immediate retry, signature check, SYSTEM service handoff and file replacement."
}
finally {
    if ($job) {
        Stop-Job $job -ErrorAction SilentlyContinue
        Remove-Job $job -Force -ErrorAction SilentlyContinue
    }
    if ($installed) {
        $uninstaller = Join-Path $install 'unins000.exe'
        if (Test-Path -LiteralPath $uninstaller) {
            $uninstall = Start-Process -FilePath $uninstaller -WindowStyle Hidden -ArgumentList '/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART' -Wait -PassThru
            if ($uninstall.ExitCode -ne 0) { Write-Error "Uninstaller failed: $($uninstall.ExitCode)" }
        }
    }
    if ($createdUser) { Remove-LocalUser -Name $user }
}
