<#
.SYNOPSIS
    Fails when a source file grows past the repo's per-file size limit.

.DESCRIPTION
    Checks every .cs file listed by `git ls-files --cached --others --exclude-standard`
    (tracked plus non-ignored untracked) against a 32768-byte limit. Byte counts use
    normalized UTF-8 (CRLF folded to LF), so Windows and Linux checkouts of the same
    commit measure identically and CI does not fail on line-ending differences.

    Excluded from the check: any path under a Vendor directory, top-level scratch
    directories (`work`, `scratch`) and top-level dot-directories, and files deleted in
    the working tree but still staged. Files over the limit must be listed in
    source-size-baseline.json with their exact allowed max bytes; baseline entries may
    shrink or be removed freely, but a file may never grow past its entry, and a new
    file over the limit always fails. Prefer splitting a file over adding a baseline
    entry; an entry is an explicit exception, not a convenience.

.PARAMETER RepoRoot
    Repository to check. Defaults to the parent of this script's Tools folder, so CI can
    invoke the script with no arguments regardless of the caller's working directory.

.PARAMETER BaselinePath
    Baseline JSON (flat object: path -> max bytes). Defaults to
    source-size-baseline.json next to this script.

.EXAMPLE
    ./Tools/TestSourceSize.ps1
#>
param(
    [string]$RepoRoot = (Join-Path $PSScriptRoot '..'),
    [string]$BaselinePath = (Join-Path $PSScriptRoot 'source-size-baseline.json')
)
$ErrorActionPreference = 'Stop'

# git emits raw UTF-8 (with core.quotepath=false); decode it as UTF-8 rather than the
# console's legacy codepage so non-ASCII file names survive the round-trip.
[Console]::OutputEncoding = [Text.Encoding]::UTF8

$limit = 32768
$repoRoot = [IO.Path]::GetFullPath($RepoRoot)
$baseline = @{}
if (-not (Test-Path -LiteralPath $BaselinePath)) {
    throw "Baseline file not found: $BaselinePath"
}
foreach ($entry in (Get-Content -LiteralPath $BaselinePath -Raw | ConvertFrom-Json).PSObject.Properties) {
    $baseline[$entry.Name] = [int]$entry.Value
}

# ponytail: the scratch-dir list is explicit because "unrelated untracked dir" cannot be
# inferred; when a new scratch directory appears, add its top-level name here.
$scratchTopDirs = 'work', 'scratch'

$raw = git -C $repoRoot -c core.quotepath=false ls-files --cached --others --exclude-standard -z -- '*.cs'
if ($LASTEXITCODE -ne 0) {
    throw "git ls-files failed: $raw"
}
# -z gives NUL-separated unquoted paths, so spaces and non-ASCII names survive intact.
$listed = @($raw -split "`0" | Where-Object { $_ })

$failures = [System.Collections.Generic.List[string]]::new()
$checked = 0
foreach ($rel in $listed) {
    if ($rel -match '(^|/)Vendor/') { continue }
    $top = ($rel -split '/')[0]
    if ($top -like '.*' -or $scratchTopDirs -contains $top) { continue }
    $full = Join-Path $repoRoot $rel
    if (-not (Test-Path -LiteralPath $full)) { continue } # deleted in the working tree, still in the index

    # Compare normalized UTF-8 byte counts (CRLF folded to LF) so Windows and Linux
    # checkouts of the same commit measure identically.
    $text = [IO.File]::ReadAllText($full)
    $size = [Text.Encoding]::UTF8.GetByteCount($text.Replace("`r`n", "`n"))
    $checked++
    if ($size -le $limit) { continue }

    $max = if ($baseline.ContainsKey($rel)) { $baseline[$rel] } else { $limit }
    if ($size -gt $max) {
        $failures.Add("$rel is $size bytes (max $max)")
    }
}

if ($failures.Count -gt 0) {
    Write-Output 'Source size gate FAILED:'
    $failures | ForEach-Object { Write-Output "  $_" }
    exit 1
}
Write-Output "Source size gate passed ($checked file(s) checked)."
