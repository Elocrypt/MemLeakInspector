#Requires -Version 7.0
<#
.SYNOPSIS
    Builds MemLeakInspector and packages it into a distributable mod zip.
.PARAMETER Configuration
    MSBuild configuration. Defaults to Release.
.PARAMETER Version
    Version string for the zip filename. Should match modinfo.json.
.EXAMPLE
    ./build/package.ps1 -Version 2.0.0
#>

param(
    [string]$Configuration = "Release",
    [Parameter(Mandatory = $true)][string]$Version
)

$ErrorActionPreference = "Stop"

$Root       = Split-Path -Parent $PSScriptRoot
$SrcProject = Join-Path $Root "src/MemLeakInspector/MemLeakInspector.csproj"
$StageRoot  = Join-Path $Root "build/stage"
$DistRoot   = Join-Path $Root "build/dist"
$Artifact   = Join-Path $DistRoot "MemLeakInspector_$Version.zip"
$OutputPath = Join-Path $Root "src/MemLeakInspector/bin/$Configuration/net10.0"

Write-Host "== MemLeakInspector $Version ($Configuration) ==" -ForegroundColor Cyan

if (Test-Path $StageRoot) { Remove-Item $StageRoot -Recurse -Force }
if (-not (Test-Path $DistRoot)) { New-Item $DistRoot -ItemType Directory | Out-Null }
New-Item $StageRoot -ItemType Directory | Out-Null

Write-Host "-- Building" -ForegroundColor Cyan
dotnet build $SrcProject --configuration $Configuration /p:DeployMod=false
if ($LASTEXITCODE -ne 0) { throw "Build failed with exit code $LASTEXITCODE" }

Write-Host "-- Staging" -ForegroundColor Cyan

# Required core files
$requiredFiles = @("MemLeakInspector.dll", "modinfo.json")
foreach ($file in $requiredFiles) {
    $source = Join-Path $OutputPath $file
    if (-not (Test-Path $source)) { throw "Missing required file: $source" }
    Copy-Item $source $StageRoot
}

# Optional icon
$optionalIcon = Join-Path $OutputPath "modicon.png"
if (Test-Path $optionalIcon) { Copy-Item $optionalIcon $StageRoot }

# Assets folder (if present)
$assetsDir = Join-Path $OutputPath "assets"
if (Test-Path $assetsDir) { Copy-Item $assetsDir $StageRoot -Recurse }

# === NEW: Include Dashboard folder ===
$dashboardSource = Join-Path $Root "dashboard"
if (Test-Path $dashboardSource) {
    Write-Host "-- Including dashboard folder" -ForegroundColor Cyan
    Copy-Item $dashboardSource $StageRoot -Recurse
} else {
    Write-Host "-- WARNING: dashboard folder not found at $dashboardSource" -ForegroundColor Yellow
}

Write-Host "-- Zipping" -ForegroundColor Cyan
if (Test-Path $Artifact) { Remove-Item $Artifact -Force }
Compress-Archive -Path (Join-Path $StageRoot "*") -DestinationPath $Artifact -Force

Write-Host "== Packaged: $Artifact ==" -ForegroundColor Green
