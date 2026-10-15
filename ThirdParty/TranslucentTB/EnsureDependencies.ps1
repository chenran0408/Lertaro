param(
    [Parameter(Mandatory=$true)][string]$DependencyDirectory,
    [ValidateSet('x64','arm64')][string]$Architecture = 'x64',
    [switch]$CheckOnly
)
$ErrorActionPreference = 'Stop'
$env:PSModulePath = Join-Path $PSHOME 'Modules'
$requirements = @(
    @{Name='Microsoft.VCLibs.140.00'; Version=[version]'14.0.33519.0'; File="vclibs-uwp-$Architecture.appx"},
    @{Name='Microsoft.VCLibs.140.00.UWPDesktop'; Version=[version]'14.0.33728.0'; File="vclibs-desktop-$Architecture.appx"},
    @{Name='Microsoft.UI.Xaml.2.8'; Version=[version]'8.2501.31001.0'; File="winui-$Architecture.appx"}
)
foreach ($requirement in $requirements) {
    $installed = Get-AppxPackage -Name $requirement.Name | Where-Object {
        $_.Architecture.ToString() -eq $Architecture -and [version]$_.Version -ge $requirement.Version -and
        $_.PackageFamilyName -eq ($requirement.Name + '_8wekyb3d8bbwe')
    }
    if ($installed) { continue }
    if ($CheckOnly) { throw "Missing taskbar runtime: $($requirement.Name) ($Architecture)." }
    $package = Join-Path $DependencyDirectory $requirement.File
    # Windows validates the signed framework package; never sideload unsigned replacement binaries.
    Add-AppxPackage -Path $package -ErrorAction Stop
}
