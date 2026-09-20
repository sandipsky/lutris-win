<#
.SYNOPSIS
    Publishes Lutris and builds the Windows installer (and optionally a portable zip).

.DESCRIPTION
    1. dotnet publish (Release, x64, the win-x64 publish profile)
    2. Inno Setup compiles installer\Lutris.iss into dist\Lutris-Windows-<version>-Setup.exe
    3. With -Portable, dist\Lutris-Windows-<version>-Portable-x64.zip is created as well.

    The installer normally downloads the Windows App Runtime during setup if the target PC
    lacks it. Pass -BundleRuntime to embed the runtime installer instead (offline installs,
    about 120 MB larger).

.EXAMPLE
    .\build-installer.ps1
    .\build-installer.ps1 -Portable
    .\build-installer.ps1 -BundleRuntime -Portable
#>
param(
    [string]$Configuration = 'Release',
    [switch]$BundleRuntime,
    [switch]$Portable,
    [switch]$SkipPublish
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$platform = 'x64'
$rid = "win-$platform"
$project = Join-Path $root 'Lutris-Native-Win.csproj'
$publishDir = Join-Path $root "bin\$Configuration\net8.0-windows10.0.19041.0\$rid\publish"
$dist = Join-Path $root 'dist'

[xml]$proj = Get-Content $project
$version = ($proj.Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1)
if (-not $version) { $version = '1.0.0' }
Write-Host "Lutris $version ($Configuration, $rid)" -ForegroundColor Cyan

if (-not $SkipPublish) {
    Write-Host 'Publishing...' -ForegroundColor Cyan
    dotnet publish $project -c $Configuration -p:Platform=$platform -p:PublishProfile=$rid -nologo -v:minimal
    if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed.' }
}
if (-not (Test-Path (Join-Path $publishDir 'Lutris.exe'))) {
    throw "Publish output not found at $publishDir"
}

$iscc = (Get-Command ISCC.exe -ErrorAction SilentlyContinue).Source
if (-not $iscc) {
    $iscc = @(
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
    ) | Where-Object { Test-Path $_ } | Select-Object -First 1
}
if (-not $iscc) {
    throw 'Inno Setup 6 was not found. Install it with "winget install JRSoftware.InnoSetup" or from https://jrsoftware.org/isdl.php'
}

New-Item -ItemType Directory -Force -Path $dist | Out-Null
$defines = @("/DAppVersion=$version", "/DPublishDir=$publishDir", "/DOutputDir=$dist")

if ($BundleRuntime) {
    $redist = Join-Path $root 'installer\redist'
    New-Item -ItemType Directory -Force -Path $redist | Out-Null
    $runtime = Join-Path $redist "WindowsAppRuntimeInstall-$platform.exe"
    if (-not (Test-Path $runtime)) {
        Write-Host 'Downloading the Windows App Runtime installer (about 120 MB)...' -ForegroundColor Cyan
        Invoke-WebRequest -Uri "https://aka.ms/windowsappsdk/2.5/latest/windowsappruntimeinstall-$platform.exe" -OutFile $runtime
    }
    $defines += '/DBundleRuntime'
}

Write-Host 'Compiling installer...' -ForegroundColor Cyan
& $iscc /Qp @defines (Join-Path $root 'installer\Lutris.iss')
if ($LASTEXITCODE -ne 0) { throw 'Inno Setup compilation failed.' }

if ($Portable) {
    Write-Host 'Creating portable zip...' -ForegroundColor Cyan
    $zip = Join-Path $dist "Lutris-Windows-$version-Portable-$platform.zip"
    if (Test-Path $zip) { Remove-Item $zip }
    Compress-Archive -Path (Join-Path $publishDir '*') -DestinationPath $zip -CompressionLevel Optimal
}

Write-Host "Done. Output in $dist" -ForegroundColor Green
Get-ChildItem $dist -File | Select-Object Name, @{ Name = 'MB'; Expression = { [math]::Round($_.Length / 1MB, 1) } } | Format-Table -AutoSize
