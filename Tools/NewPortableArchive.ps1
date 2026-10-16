param(
    [Parameter(Mandatory=$true)][string]$SourceDirectory,
    [Parameter(Mandatory=$true)][string]$DestinationPath
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$level = [IO.Compression.CompressionLevel]::Optimal
# PowerShell 7/.NET 6+ supports the denser ZIP level. Keep older local build hosts usable.
if ([Enum]::IsDefined([IO.Compression.CompressionLevel], 'SmallestSize')) {
    $level = [Enum]::Parse([IO.Compression.CompressionLevel], 'SmallestSize')
}
$source = [IO.Path]::GetFullPath($SourceDirectory).TrimEnd('\', '/')
$prefix = [IO.Path]::GetFileName($source) + '/'
if (!(Test-Path -LiteralPath $source -PathType Container)) { throw 'The publish directory is missing.' }
if (Test-Path -LiteralPath $DestinationPath) { Remove-Item -LiteralPath $DestinationPath -Force }
$archive = [IO.Compression.ZipFile]::Open([IO.Path]::GetFullPath($DestinationPath), 'Create')
try {
    # Explicit forward slashes keep ZIP paths identical on Windows PowerShell/.NET Framework.
    foreach ($file in [IO.Directory]::EnumerateFiles($source, '*', 'AllDirectories')) {
        $name = $prefix + $file.Substring($source.Length + 1).Replace('\', '/')
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $file, $name, $level) | Out-Null
    }
    foreach ($directory in [IO.Directory]::EnumerateDirectories($source, '*', 'AllDirectories')) {
        if ([IO.Directory]::GetFileSystemEntries($directory).Length -eq 0) {
            $archive.CreateEntry($prefix + $directory.Substring($source.Length + 1).Replace('\', '/') + '/') | Out-Null
        }
    }
} finally { $archive.Dispose() }
