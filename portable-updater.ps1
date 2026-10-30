param([string]$Source, [string]$Destination, [switch]$WaitTaskbarHost)

# Called from the verified service. Also dot-sourced by file/lock regression tests.
function Invoke-LertaroUpdateCopy([string]$Source, [string]$Destination, [string]$Backup) {
    $sourceRoot = [IO.Path]::GetFullPath($Source).TrimEnd('\') + '\'
    $targetRoot = [IO.Path]::GetFullPath($Destination).TrimEnd('\') + '\'
    $backupRoot = [IO.Path]::GetFullPath($Backup).TrimEnd('\') + '\'
    if ($sourceRoot -eq $targetRoot -or $backupRoot -eq $targetRoot -or
        $targetRoot.StartsWith($sourceRoot, [StringComparison]::OrdinalIgnoreCase) -or
        $backupRoot.StartsWith($sourceRoot, [StringComparison]::OrdinalIgnoreCase) -or
        $sourceRoot.StartsWith($backupRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'Update directories overlap.' }
    $changed = [Collections.Generic.List[object]]::new()
    try {
        $entries = @(Get-ChildItem -LiteralPath $sourceRoot -Recurse -Force -ErrorAction Stop)
        if ($entries | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }) { throw 'Update payload contains a link.' }
        foreach ($file in $entries | Where-Object { !$_.PSIsContainer } | Sort-Object FullName) {
            $relative = $file.FullName.Substring($sourceRoot.Length)
            if ($relative -match '^(Data|update-payload)(\\|$)' -or $relative -in @('update.lock', 'update-result.json', '.permissions-version')) { continue }
            $target = [IO.Path]::GetFullPath((Join-Path $targetRoot $relative))
            if (!$target.StartsWith($targetRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'Update path escaped the installation.' }
            $saved = Join-Path $backupRoot $relative
            [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($target)) | Out-Null
            [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($saved)) | Out-Null
            $temporary = $target + '.' + [Guid]::NewGuid().ToString('N') + '.tmp'
            $existed = [IO.File]::Exists($target)
            try {
                [IO.File]::Copy($file.FullName, $temporary, $false)
                if ($existed) { [IO.File]::Replace($temporary, $target, $saved) }
                else { [IO.File]::Move($temporary, $target) }
                $changed.Add(@{ Target = $target; Backup = $saved; Existed = $existed })
            } finally {
                if ([IO.File]::Exists($temporary)) { [IO.File]::Delete($temporary) }
            }
        }
    } catch {
        $copyError = $_
        $rollbackErrors = [Collections.Generic.List[string]]::new()
        for ($i = $changed.Count - 1; $i -ge 0; $i--) {
            $item = $changed[$i]
            try {
                if ($item.Existed) { [IO.File]::Replace($item.Backup, $item.Target, [NullString]::Value) }
                else { [IO.File]::Delete($item.Target) }
            } catch { $rollbackErrors.Add($_.Exception.Message) }
        }
        if ($rollbackErrors.Count) { throw "ROLLBACK FAILED: $($rollbackErrors -join '; '). Recovery files: $backupRoot" }
        throw "Update failed at '$target': $($copyError.Exception.Message)"
    }
}

function Wait-LertaroTaskbarHostExit {
    param([string]$TargetRoot, [int]$TimeoutMs = 15000)
    $root = [IO.Path]::GetFullPath($TargetRoot).TrimEnd('\')
    foreach ($process in Get-Process -Name 'Lertaro.TaskbarHost' -ErrorAction SilentlyContinue) {
        try {
            if ($process.Path -and [IO.Path]::GetDirectoryName($process.Path).TrimEnd('\') -eq $root) {
                # The supervisor restores the taskbar after its parent exits. Wait rather than kill it.
                if (!$process.WaitForExit($TimeoutMs)) { throw 'The taskbar component is still closing. Please retry the update.' }
            }
        } finally { $process.Dispose() }
    }
}

function Stop-LertaroService {
    param([string]$Sc, [int]$TimeoutMs = 15000, [int]$PollMs = 100)
    $deadline = [Diagnostics.Stopwatch]::StartNew()
    while ($true) {
        & $Sc stop LertaroService | Out-Null
        $exit = $LASTEXITCODE
        # 1061 means the SCM is already processing a stop; retry until it accepts a stop or reports stopped.
        if ($exit -in 1060, 1062) { return }
        if ($exit -eq 0) {
            # sc stop acknowledges the request before OnStop has finished closing the hook/indexer.
            $service = Get-Service -Name LertaroService -ErrorAction SilentlyContinue
            if ($service) {
                try { $service.WaitForStatus('Stopped', [TimeSpan]::FromMilliseconds([Math]::Max(1, $TimeoutMs - $deadline.Elapsed.TotalMilliseconds))) }
                finally { $service.Dispose() }
            }
            return
        }
        if ($exit -ne 1061 -or $deadline.Elapsed.TotalMilliseconds -ge $TimeoutMs) { throw "Could not stop service: $exit" }
        Start-Sleep -Milliseconds $PollMs
    }
}

function Stop-LertaroProcesses {
    param([string]$TargetRoot, [int]$TimeoutMs = 15000, [int]$GraceMs = 1000)
    $root = [IO.Path]::GetFullPath($TargetRoot).TrimEnd('\')
    foreach ($process in Get-Process -Name 'Lertaro.App', 'Lertaro.Service', 'lff' -ErrorAction SilentlyContinue) {
        try {
            if ($process.HasExited) { continue }
            if (!$process.Path -or [IO.Path]::GetDirectoryName($process.Path).TrimEnd('\') -ne $root) { continue }
            # The session-0 SYSTEM service is stopped through SCM, never by an administrator's Kill.
            $serviceProcess = $process.ProcessName -eq 'Lertaro.Service' -and $process.SessionId -eq 0
            if ($process.WaitForExit($(if ($serviceProcess) { $TimeoutMs } else { $GraceMs }))) { continue }
            if ($serviceProcess) { throw "The background service is still closing (PID $($process.Id)). Please retry the update." }
            try { $process.Kill() }
            catch {
                # The App/hook may finish exiting between enumeration and Kill; access denied is also
                # returned for a terminating process. A live process with denied access remains an error.
                if (!$process.HasExited) { throw "Could not stop $($process.ProcessName) (PID $($process.Id)): $($_.Exception.Message)" }
            }
            if (!$process.WaitForExit($TimeoutMs)) { throw "Process $($process.ProcessName) (PID $($process.Id)) did not stop." }
        } finally { $process.Dispose() }
    }
}

function Start-LertaroService {
    param([string]$Sc, [int]$TimeoutMs = 60000, [int]$PollMs = 100)
    $deadline = [Diagnostics.Stopwatch]::StartNew()
    while ($true) {
        & $Sc start LertaroService | Out-Null
        $exit = $LASTEXITCODE
        if ($exit -in 0, 1056) { return }
        if ($exit -eq 1053) {
            # sc.exe can time out while the service is still completing its start callback. In
            # particular, a first index build can outlive the SCM command timeout even though the
            # service is healthy. Observe SCM instead of reporting a false update failure.
            do {
                $service = Get-Service -Name LertaroService -ErrorAction SilentlyContinue
                if ($service) {
                    try {
                        $service.Refresh()
                        if ($service.Status -eq 'Running') { return }
                        if ($service.Status -eq 'Stopped') { break }
                    } finally { $service.Dispose() }
                }
                if ($deadline.Elapsed.TotalMilliseconds -ge $TimeoutMs) { break }
                Start-Sleep -Milliseconds $PollMs
            } while ($deadline.Elapsed.TotalMilliseconds -lt $TimeoutMs)
        }
        # A stop from another controller can still make start report 1061 briefly. A 1053 is
        # retried only after observing that SCM has not reached Running yet.
        if ($exit -notin 1061, 1053 -or $deadline.Elapsed.TotalMilliseconds -ge $TimeoutMs) {
            $serviceState = (& $Sc queryex LertaroService 2>&1 | Out-String).Trim()
            throw "Could not restart service: $exit. SCM state: $serviceState"
        }
        Start-Sleep -Milliseconds $PollMs
    }
}

function Complete-LertaroUpdate {
    param([hashtable]$Result, [bool]$RestartSafe, [string]$Sc, [string]$TargetRoot, [IDisposable]$Lease)
    try {
        if ($RestartSafe) {
            try { Start-LertaroService $Sc }
            catch {
                $message = $_.Exception.Message
                $Result.Status = 'failed'
                $Result.Message = if ([string]::IsNullOrWhiteSpace($Result.Message)) { $message } else { "$($Result.Message); $message" }
            }
        }
    } finally {
        try {
            $Result | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $TargetRoot 'update-result.json') -Encoding UTF8
        } finally { $Lease.Dispose() }
    }
}

if ($MyInvocation.InvocationName -eq '.') { return }
$ErrorActionPreference = 'Stop'
# An elevated PowerShell must not discover modules from a user's custom module search path.
$env:PSModulePath = Join-Path $PSHOME 'Modules'
if ($WaitTaskbarHost) {
    Wait-LertaroTaskbarHostExit -TargetRoot $Destination
    exit
}

$sourceRoot = [IO.Path]::GetFullPath($Source).TrimEnd('\')
$targetRoot = [IO.Path]::GetFullPath($Destination).TrimEnd('\')
$payloadRoot = Join-Path $targetRoot 'update-payload'
if (!$sourceRoot.StartsWith($payloadRoot + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Untrusted update source.' }
if ($sourceRoot -eq [IO.Path]::GetPathRoot($sourceRoot) -or $targetRoot -eq [IO.Path]::GetPathRoot($targetRoot)) { throw 'Invalid update root.' }
$principal = [Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
if (!$principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'The updater requires elevation.' }
$lockFile = Join-Path $targetRoot 'update.lock'
$lease = $null
$deadline = [DateTime]::UtcNow.AddSeconds(15)
while (!$lease) {
    try { $lease = [IO.File]::Open($lockFile, 'OpenOrCreate', 'ReadWrite', 'None') }
    catch [IO.IOException] {
        if ([DateTime]::UtcNow -ge $deadline) { throw 'Another update is running.' }
        Start-Sleep -Milliseconds 100
    }
}
$backup = Join-Path $payloadRoot ('rollback-' + [Guid]::NewGuid().ToString('N'))
$sc = Join-Path ([Environment]::GetFolderPath('System')) 'sc.exe'
$restartSafe = $true
$result = @{ Status = 'failed'; Time = [DateTime]::UtcNow.ToString('O'); Message = ''; RecoveryDirectory = $backup }
try {
    Stop-LertaroService $sc
    Stop-LertaroProcesses -TargetRoot $targetRoot
    Wait-LertaroTaskbarHostExit -TargetRoot $targetRoot
    Invoke-LertaroUpdateCopy $sourceRoot $targetRoot $backup
    $result.Status = 'success'
    # Both targets are checked against this installation's staging root before recursive removal.
    foreach ($completed in @($sourceRoot, $backup)) {
        $resolved = [IO.Path]::GetFullPath($completed)
        if ($resolved.StartsWith($payloadRoot + '\', [StringComparison]::OrdinalIgnoreCase) -and (Test-Path -LiteralPath $resolved)) {
            Remove-Item -LiteralPath $resolved -Recurse -Force
        }
    }
} catch {
    $result.Message = $_.Exception.Message
    $restartSafe = !$result.Message.StartsWith('ROLLBACK FAILED:')
} finally {
    Complete-LertaroUpdate -Result $result -RestartSafe $restartSafe -Sc $sc -TargetRoot $targetRoot -Lease $lease
}
if ($result.Status -ne 'success') { Write-Error $result.Message; exit 1 }
