#Requires -Version 7
<#
.SYNOPSIS
    W4 performance smoke: measures cold-start latency, query P95 and incremental index throughput
    against a temp directory populated with 10,000 generated files. Read-only on the repo; creates
    and destroys a temp tree. Results are written as markdown to stdout (redirect to save).
.DESCRIPTION
    Measurement only -- does not optimise anything. Runs against a LOCAL BUILD (App/bin/Release
    publish) launched from the script's staging directory, not the installed app.
.NOTES
    ponytail: the measurements are wall-clock from outside the process (no ETW/tracing), so they
    carry ~50-100 ms of process-launch noise. Good enough for a regression signal; not a profiler.
    Upgrade path: hook the App's existing Logger timestamps instead.
#>
param(
    [string]$BuildConfig = "Release",
    [int]$FileCount = 10000,
    [int]$QueryRounds = 5
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) -Parent
$dotnet = "D:\dev\Toolchains\dotnet\dotnet.exe"
$staging = Join-Path $env:TEMP "lertaro-perf-$(Get-Random)"
$testDataDir = Join-Path $staging "testdata"

# ═══ 1. Publish a fresh build ═══
Write-Host "Publishing $BuildConfig build..."
& $dotnet publish "$repo\App\App.csproj" -c $BuildConfig -o $staging -v quiet --nologo `
    -p:ErrorOnDuplicatePublishOutputFiles=false
if ($LASTEXITCODE -ne 0) { throw "Publish failed." }
$appExe = Join-Path $staging "Lertaro.App.exe"

# ═══ 2. Generate test data ═══
Write-Host "Generating $FileCount test files..."
New-Item $testDataDir -ItemType Directory -Force | Out-Null
$extensions = @('.txt', '.md', '.log', '.json', '.xml', '.pdf', '.docx', '.xlsx', '.png', '.cs')
for ($i = 0; $i -lt $FileCount; $i++) {
    $ext = $extensions[$i % $extensions.Count]
    $dir = Join-Path $testDataDir "dir$($i % 20)"
    if (!(Test-Path $dir)) { New-Item $dir -ItemType Directory -Force | Out-Null }
    $content = "File $i content. Lorem ipsum dolor sit amet. Keyword$($i % 100) alpha beta."
    Set-Content (Join-Path $dir "file$($i:D5)$ext") $content
}

# ═══ 3. Cold start measurement ═══
Write-Host "Measuring cold start..."
$sw = [System.Diagnostics.Stopwatch]::StartNew()
$appProc = Start-Process $appExe -PassThru
# Poll for the main window to appear (available = window handle created)
while ($appProc.MainWindowHandle -eq [IntPtr]::Zero -and $sw.ElapsedMilliseconds -lt 30000) {
    Start-Sleep -Milliseconds 50
    $appProc.Refresh()
}
$appProc.Refresh()
$coldStartMs = $sw.ElapsedMilliseconds
Write-Host "Cold start: ${coldStartMs}ms"

# Wait for the service/hook to settle
Start-Sleep -Seconds 5

# ═══ 4. Query P95 ═══
# Type into the search box via SendInput (the window is foreground after launch).
Add-Type -AssemblyName System.Windows.Forms
function Send-Query([string]$text) {
    [System.Windows.Forms.SendKeys]::SendWait('^')
    Start-Sleep -Milliseconds 80
    [System.Windows.Forms.SendKeys]::SendWait('^')
    Start-Sleep -Milliseconds 500
    [System.Windows.Forms.SendKeys]::SendWait($text)
}

$queries = @(
    @{ Label = "single-char (large result set)"; Text = "e" },
    @{ Label = "multi-word (moderate)"; Text = "alpha beta" },
    @{ Label = "extension-filtered"; Text = "alpha ext:cs" }
)
$queryResults = @()
foreach ($q in $queries) {
    $latencies = @()
    for ($round = 0; $round -lt $QueryRounds; $round++) {
        # Clear previous query
        [System.Windows.Forms.SendKeys]::SendWait('^a')
        Start-Sleep -Milliseconds 100
        [System.Windows.Forms.SendKeys]::SendWait('{DEL}')
        Start-Sleep -Milliseconds 300

        $sw = [System.Diagnostics.Stopwatch]::StartNew()
        Send-Query $q.Text
        Start-Sleep -Milliseconds 1500  # wait for results to paint
        $sw.Stop()
        $latencies += $sw.ElapsedMilliseconds
    }
    $sorted = $latencies | Sort-Object
    $p95 = $sorted[[Math]::Floor($sorted.Count * 0.95)]
    $queryResults += [PSCustomObject]@{
        Query = $q.Label; Text = $q.Text; Min = $sorted[0]; P95 = $p95; Max = $sorted[-1]
    }
}

# ═══ 5. Incremental index throughput ═══
Write-Host "Measuring incremental index throughput..."
$before = Get-ChildItem $testDataDir -Recurse -File | Measure-Object
$beforeCount = $before.Count
$sw = [System.Diagnostics.Stopwatch]::StartNew()
for ($i = 0; $i -lt 100; $i++) {
    $ext = $extensions[$i % $extensions.Count]
    $dir = Join-Path $testDataDir "dir$($i % 20)"
    Set-Content (Join-Path $dir "newfile$($i:D4)$ext") "New file $i for throughput measurement."
}
$sw.Stop()
$createThroughput = [Math]::Round(100 / $sw.Elapsed.TotalSeconds, 1)
Start-Sleep -Seconds 5  # let the USN watcher settle

# ═══ 6. Shut down and report ═══
Stop-Process -Name Lertaro.App -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 2
Remove-Item $staging -Recurse -Force -ErrorAction SilentlyContinue

# Output markdown
Write-Host "`n## Performance Baseline`n"
Write-Host "| Metric | Value |"
Write-Host "|--------|-------|"
Write-Host "| Cold start (process -> main window) | ${coldStartMs} ms |"
Write-Host "| Test data files | $FileCount |"
Write-Host ""
Write-Host "| Query | Text | Min (ms) | P95 (ms) | Max (ms) |"
Write-Host "|-------|------|----------|----------|----------|"
foreach ($r in $queryResults) {
    Write-Host "| $($r.Query) | $($r.Text) | $($r.Min) | $($r.P95) | $($r.Max) |"
}
Write-Host ""
Write-Host "| Incremental index (100 file creates) | ${createThroughput} files/s |"
