@echo off
setlocal
chcp 65001 >nul

set "ROOT=%~dp0"
set "DIST=%ROOT%dist"

:: Prefer the same PowerShell edition as modern build runners. Starting Windows PowerShell
:: from PowerShell 7 can inherit module paths that hide Get-FileHash and Expand-Archive.
set "BUILD_POWERSHELL=powershell"
where pwsh >nul 2>&1
if not errorlevel 1 set "BUILD_POWERSHELL=pwsh"

echo ==========================================
echo Lertaro Build and Package Script (make)
echo ==========================================

:: 1. Check for dotnet CLI
where dotnet >nul 2>&1
if errorlevel 1 (
    echo [Error] dotnet CLI was not found. Please install the required .NET SDK.
    exit /b 1
)

:: 2. Clean and create the output directory (the per-architecture publish dirs are cleaned in :build_arch)
echo.
echo [1/3] Cleaning dist directory...
if exist "%DIST%" (
    rmdir /s /q "%DIST%" >nul 2>&1
    if exist "%DIST%" (
        timeout /t 1 >nul
        rmdir /s /q "%DIST%" >nul 2>&1
    )
)
mkdir "%DIST%"

:: 3. Find the 64-bit Inno Setup 7 compiler (hoisted ahead of both passes)
:: Try System-wide install first, then User-specific install
set "ISCC="

if exist "C:\Program Files\Inno Setup 7\ISCC.exe" (
    set "ISCC=C:\Program Files\Inno Setup 7\ISCC.exe"
    goto :iscc_found
)

if exist "%LOCALAPPDATA%\Programs\Inno Setup 7\ISCC.exe" (
    set "ISCC=%LOCALAPPDATA%\Programs\Inno Setup 7\ISCC.exe"
    goto :iscc_found
)

if exist "D:\Dev\Toolchains\InnoSetup7\ISCC.exe" (
    set "ISCC=D:\Dev\Toolchains\InnoSetup7\ISCC.exe"
    goto :iscc_found
)

:iscc_not_found
echo [Error] Inno Setup 7 compiler (ISCC.exe) not found.
echo Checked paths:
echo   1. C:\Program Files\Inno Setup 7\ISCC.exe
echo   2. %LOCALAPPDATA%\Programs\Inno Setup 7\ISCC.exe
echo.
echo Please install Inno Setup 7.
exit /b 1

:iscc_found
"%ISCC%" /? 2>&1 | findstr /c:"Inno Setup 7" >nul
if errorlevel 1 (
    echo [Error] Inno Setup 7 is required to compile the installers.
    echo The selected ISCC.exe is not an Inno Setup 7 compiler.
    exit /b 1
)

:: 4. Extract application version from App.csproj (likewise shared by both passes)
echo.
echo Extracting application version...
for /f "usebackq tokens=*" %%v in (`%BUILD_POWERSHELL% -NoProfile -Command "([xml](Get-Content '%ROOT%App\App.csproj')).Project.PropertyGroup.Version"`) do set "APP_VER=%%v"
for /f "usebackq tokens=*" %%v in (`%BUILD_POWERSHELL% -NoProfile -Command "$numeric = ('%APP_VER%' -split '[-+]')[0]; $a = ($numeric + '.0.0.0') -split '\.'; $a[0..3] -join '.'"`) do set "APP_VER_4=%%v"
echo App Version: %APP_VER% (PE Version: %APP_VER_4%)


:: Two architectures, same steps. x64 publishes with no RID exactly as it always has, so its output is
:: unchanged; arm64 is a cross-publish from whatever machine is building. They are separate artifacts
:: rather than one combined installer because the BrowserData plugin carries a native library and only
:: one architecture's copy can sit in its (flat, deps.json-less) load directory -- see that project.
echo.
echo === Building x64 ===
set "ARCH=x64"
set "ARCHSUFFIX="
set "RIDARGS=-p:RustIndexStoreTarget=x86_64-pc-windows-msvc"
call :build_arch
if errorlevel 1 exit /b 1

echo.
echo === Building arm64 ===
set "ARCH=arm64"
:: The new repository is not visible to legacy installs, so use the conventional hyphenated suffix.
:: Kept in step with UpdateAssetSelector.SuffixFor and installer.iss.
set "ARCHSUFFIX=-arm64"
set "RIDARGS=-r win-arm64 --self-contained false -p:RustIndexStoreTarget=aarch64-pc-windows-msvc"
call :build_arch
if errorlevel 1 exit /b 1

:: 5. Clean up temporary publish folder
echo.
echo Cleaning up temporary publish folder...
rmdir /s /q "%ROOT%publish" >nul 2>&1

echo.
echo ==========================================
echo Build and Package Completed Successfully!
echo Installer (x64):     %DIST%\Lertaro-Setup.exe
echo Portable ZIP (x64):  %DIST%\Lertaro-Portable.zip
echo Installer (arm64):   %DIST%\Lertaro-Setup-arm64.exe
echo Portable ZIP (arm64):%DIST%\Lertaro-Portable-arm64.zip
echo ==========================================
exit /b 0


:: ---------------------------------------------------------------------------------------------
:: Per-architecture build. Driven by ARCH / ARCHSUFFIX / RIDARGS set by the caller rather than by
:: parameters, because an empty argument (x64 passes no RID) is awkward to quote in batch.
:: ---------------------------------------------------------------------------------------------
:build_arch
set "OUT=%ROOT%publish\%ARCH%\Lertaro"
if exist "%OUT%" (
    rmdir /s /q "%OUT%" >nul 2>&1
    if exist "%OUT%" (
        timeout /t 1 >nul
        rmdir /s /q "%OUT%" >nul 2>&1
    )
)
mkdir "%OUT%"

:: Publish App/Service/Cli in Release mode
::
:: Publish each project separately to keep shared output deterministic and avoid NETSDK1194.
echo.
echo [1/4] Publishing App/Service/Cli in Release mode...
for %%p in (App\App.csproj Service\Service.csproj Cli\Cli.csproj TaskbarHost\TaskbarHost.csproj) do (
    dotnet publish "%ROOT%%%p" -c Release -o "%OUT%" %RIDARGS% -p:ErrorOnDuplicatePublishOutputFiles=false -v quiet
    if errorlevel 1 exit /b 1
)

echo.
echo [2/4] Publishing Plugins in Release mode...
%BUILD_POWERSHELL% -NoProfile -Command "$projects = ([xml](Get-Content -LiteralPath '%ROOT%Lertaro.Plugins.slnx')).Solution.Project; foreach ($project in $projects) { dotnet publish (Join-Path '%ROOT%' $project.Path) -c Release -o '%OUT%\Plugins' %RIDARGS% -p:ErrorOnDuplicatePublishOutputFiles=false -v quiet; if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE } }"
if errorlevel 1 exit /b 1


:: Copy portable updater/cleanup files and clean PDB files
echo.
echo [3/4] Copying portable updater/cleanup files and cleaning PDB files...
%BUILD_POWERSHELL% -NoProfile -ExecutionPolicy Bypass -File "%ROOT%Tools\PrepareTaskbarBundle.ps1" -OutputDirectory "%OUT%" -CacheDirectory "%ROOT%obj\TaskbarDownloadCache" -Architecture "%ARCH%"
if errorlevel 1 exit /b 1
copy "%ROOT%portable-updater.bat" "%OUT%\" >nul
if errorlevel 1 (
    echo [Warning] Failed to copy portable-updater.bat.
)
copy "%ROOT%portable-updater.ps1" "%OUT%\" >nul
if errorlevel 1 exit /b 1
copy "%ROOT%install-dotnet-runtime.bat" "%OUT%\" >nul
if errorlevel 1 (
    echo [Warning] Failed to copy install-dotnet-runtime.bat.
)
copy "%ROOT%portable-cleanup.bat" "%OUT%\" >nul
if errorlevel 1 (
    echo [Warning] Failed to copy portable-cleanup.bat.
)

:: Preserve the application license in both the installer and portable ZIP.
copy "%ROOT%LICENSE" "%OUT%\LICENSE" >nul
if errorlevel 1 exit /b 1
del /s /q "%OUT%\*.pdb" >nul 2>&1

:: Compile Inno Setup Installer
echo.
echo [4/4] Compiling Inno Setup Installer...
echo Using Inno Setup compiler: "%ISCC%"
"%ISCC%" /DAppVersion="%APP_VER%" /DAppVersion4="%APP_VER_4%" /DArch="%ARCH%" "%ROOT%Installer\installer.iss"
if errorlevel 1 (
    echo [Error] Inno Setup compilation failed for %ARCH%.
    exit /b 1
)

:: Create Portable ZIP Archive
echo.
echo Creating Portable ZIP Archive (%ARCH%)...
%BUILD_POWERSHELL% -NoProfile -File "%ROOT%Tools\NewPortableArchive.ps1" -SourceDirectory "%OUT%" -DestinationPath "%DIST%\Lertaro-Portable%ARCHSUFFIX%.zip"
if errorlevel 1 (
    echo [Error] Portable ZIP creation failed.
    exit /b 1
)

goto :eof
