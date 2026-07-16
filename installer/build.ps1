<#
.SYNOPSIS
    Build ZMK Companion — compiles the app and packages it as a Windows installer.

.DESCRIPTION
    Steps:
      1. dotnet publish (Release, win-x64, single-file)
      2. ISCC.exe to produce ZmkCompanion-Setup-<version>.exe in installer\output\

.PARAMETER SkipBuild
    Skip dotnet publish and go straight to Inno Setup (useful when the binary is fresh).

.EXAMPLE
    .\build.ps1
    .\build.ps1 -SkipBuild
#>
param(
    [switch]$SkipBuild
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$root        = Split-Path $PSScriptRoot -Parent
$appProject  = Join-Path $root "app\ZmkCompanion\ZmkCompanion.csproj"
$issFile     = Join-Path $PSScriptRoot "ZmkCompanion.iss"
$outputDir   = Join-Path $PSScriptRoot "output"

# ── 1. dotnet publish ──────────────────────────────────────────────────────────

if (-not $SkipBuild) {
    Write-Host "`n==> dotnet publish`n" -ForegroundColor Cyan

    $dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
    if (-not $dotnet) {
        Write-Error ".NET SDK not found. Install from https://dot.net"
    }

    & dotnet publish $appProject `
        --configuration Release `
        --runtime win-x64 `
        --self-contained true `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:PublishReadyToRun=true

    if ($LASTEXITCODE -ne 0) {
        Write-Error "dotnet publish failed (exit $LASTEXITCODE)"
    }

    Write-Host "`n==> Build succeeded.`n" -ForegroundColor Green
}

# ── 2. Inno Setup ─────────────────────────────────────────────────────────────

Write-Host "==> Running Inno Setup compiler`n" -ForegroundColor Cyan

# Common install locations for ISCC.exe
$isccCandidates = @(
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles}\Inno Setup 6\ISCC.exe",
    "ISCC.exe"   # if on PATH
)

$iscc = $isccCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1

if (-not $iscc) {
    $iscc = Get-Command ISCC.exe -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source
}

if (-not $iscc) {
    Write-Error @"
Inno Setup 6 not found.
Install from https://jrsoftware.org/isdl.php or add ISCC.exe to PATH.
"@
}

New-Item -ItemType Directory -Force -Path $outputDir | Out-Null

& $iscc $issFile

if ($LASTEXITCODE -ne 0) {
    Write-Error "Inno Setup failed (exit $LASTEXITCODE)"
}

$installer = Get-ChildItem -Path $outputDir -Filter "ZmkCompanion-Setup-*.exe" |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1

Write-Host "`n==> Installer ready:" -ForegroundColor Green
Write-Host "    $($installer.FullName)" -ForegroundColor White
Write-Host "    Size: $([Math]::Round($installer.Length / 1MB, 1)) MB`n"
