param(
    [Parameter(Mandatory=$true)][string]$OutputDirectory,
    [Parameter(Mandatory=$true)][string]$CacheDirectory,
    [ValidateSet('x64','arm64')][string]$Architecture = 'x64'
)
$ErrorActionPreference = 'Stop'
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
$CacheDirectory = [IO.Path]::GetFullPath($CacheDirectory)
$referenceDirectory = Join-Path $PSScriptRoot '..\ThirdParty\TranslucentTB'
$assets = Get-Content -LiteralPath (Join-Path $referenceDirectory 'assets.json') -Raw | ConvertFrom-Json
$bundle = Join-Path $OutputDirectory 'Components\TranslucentTB'
$engine = Join-Path $bundle 'engine'
$dependencies = Join-Path $bundle 'Dependencies'
New-Item -ItemType Directory -Force -Path $CacheDirectory,$engine,$dependencies | Out-Null
function Get-VerifiedAsset([string]$Name) {
    $asset = $assets.PSObject.Properties[$Name].Value
    if (!$asset) { throw "Unknown taskbar asset: $Name" }
    $path = Join-Path $CacheDirectory $Name
    if (!(Test-Path -LiteralPath $path) -or (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $asset.sha256) {
        Invoke-WebRequest -Uri $asset.url -OutFile $path -UseBasicParsing
    }
    if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $asset.sha256) { throw "Checksum mismatch: $Name" }
    return $path
}
$portable = Get-VerifiedAsset "TranslucentTB-portable-$Architecture.zip"
Expand-Archive -LiteralPath $portable -DestinationPath $engine -Force
foreach ($name in @("vclibs-uwp-$Architecture.appx","vclibs-desktop-$Architecture.appx","winui-$Architecture.appx")) {
    Copy-Item -LiteralPath (Get-VerifiedAsset $name) -Destination $dependencies
}
# The exact upstream source archive accompanies the unchanged GPL binary in both installers and portable zips.
Copy-Item -LiteralPath (Get-VerifiedAsset 'source.zip') -Destination (Join-Path $bundle 'TranslucentTB-2026.2-source.zip')
foreach ($name in @('LICENSE.md','NOTICE.md','EnsureDependencies.ps1','assets.json')) {
    Copy-Item -LiteralPath (Join-Path $referenceDirectory $name) -Destination $bundle
}
$engineFiles = @(Get-ChildItem -LiteralPath $engine -Recurse -File | ForEach-Object {
    @{path=$_.FullName.Substring($engine.Length + 1).Replace('\','/'); sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()}
})
@{version='2026.2'; architecture=$Architecture; engineFiles=$engineFiles} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $bundle 'manifest.json') -Encoding UTF8
