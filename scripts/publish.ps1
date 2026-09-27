#requires -version 5.1
<#
.SYNOPSIS
  Publishes MouseGesture as a self-contained, single-file Windows x64 build,
  and packages it as a portable .zip plus (if Inno Setup is available) an installer.

.PARAMETER Version
  Override version (defaults to <Version> from Directory.Build.props).

.PARAMETER SkipInstaller
  Skip Inno Setup even if ISCC.exe is present.

.OUTPUTS
  publish/  — raw publish output
  dist/MouseGesture-<ver>-portable.zip
  dist/MouseGestureSetup-<ver>.exe   (if Inno Setup present)
#>
[CmdletBinding()]
param(
    [string]$Version,
    [switch]$SkipInstaller
)

$ErrorActionPreference = 'Stop'

$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
Push-Location $root
try {

# Resolve version from Directory.Build.props if not supplied.
if (-not $Version) {
    $props = [xml](Get-Content (Join-Path $root 'Directory.Build.props'))
    $Version = $props.Project.PropertyGroup.Version
}
Write-Host "Version: $Version"

$publishDir = Join-Path $root 'publish'
$distDir    = Join-Path $root 'dist'
if (Test-Path $publishDir) { Remove-Item -Recurse -Force $publishDir }
if (-not (Test-Path $distDir)) { New-Item -ItemType Directory -Path $distDir | Out-Null }

# Make sure the icon exists.
$icon = Join-Path $root 'src/MouseGesture.App/Assets/app.ico'
if (-not (Test-Path $icon)) {
    Write-Host 'app.ico missing — generating...'
    & (Join-Path $PSScriptRoot 'build-icon.ps1')
}

Write-Host '--- dotnet publish ---'
dotnet publish (Join-Path $root 'src/MouseGesture.App/MouseGesture.App.csproj') `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:PublishReadyToRun=true `
    -p:DebugType=embedded `
    -o $publishDir
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed' }

# Drop large native PDBs (Skia/HarfBuzz). Not needed at runtime.
Get-ChildItem -Path $publishDir -Filter 'lib*.pdb' -File | Remove-Item -Force

Write-Host '--- portable zip ---'
$zip = Join-Path $distDir "MouseGesture-$Version-portable.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $publishDir '*') -DestinationPath $zip
Write-Host "Wrote $zip"

if ($SkipInstaller) {
    Write-Host 'SkipInstaller specified — done.'
    return
}

# --- Inno Setup ---
$iscc = $null
foreach ($p in @(
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"   # per-user install (e.g. winget --scope user)
)) { if (Test-Path $p) { $iscc = $p; break } }
if (-not $iscc) {
    # (no ?. here: this script must also run on Windows PowerShell 5.1)
    $cmd = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($cmd) { $iscc = $cmd.Source }
}

if (-not $iscc) {
    Write-Warning 'Inno Setup (ISCC.exe) not found — skipping installer build. Install from https://jrsoftware.org/isdl.php to enable.'
    return
}

Write-Host "--- Inno Setup ($iscc) ---"
$iss = Join-Path $PSScriptRoot 'installer.iss'
& $iscc /Qp "/DMyAppVersion=$Version" "/DPublishDir=$publishDir" "/DOutputDir=$distDir" $iss
if ($LASTEXITCODE -ne 0) { throw 'ISCC failed' }
Write-Host 'Installer build complete.'
}
finally {
    Pop-Location
}
