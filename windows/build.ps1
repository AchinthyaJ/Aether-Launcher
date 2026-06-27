# Aether Launcher — Windows Build Script
# Usage: .\build.ps1 [-Configuration Release|Debug] [-OutputDir <path>] [-CreateInstaller]

param(
    [ValidateSet("Release", "Debug")]
    [string]$Configuration = "Release",
    [string]$OutputDir = ".\publish\windows",
    [switch]$CreateInstaller
)

$ErrorActionPreference = "Stop"
$ProjectRoot = Split-Path -Parent $PSScriptRoot
$ProjectFile = Join-Path $ProjectRoot "OfflineMinecraftLauncher.csproj"

Write-Host "═══════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host "  Aether Launcher — Windows Build" -ForegroundColor White
Write-Host "  Configuration: $Configuration" -ForegroundColor Gray
Write-Host "  Output: $OutputDir" -ForegroundColor Gray
Write-Host "═══════════════════════════════════════════════════" -ForegroundColor Cyan

# Step 1: Clean previous builds
Write-Host "`n[1/4] Cleaning previous build..." -ForegroundColor Yellow
if (Test-Path $OutputDir) {
    Remove-Item -Recurse -Force $OutputDir
}

# Step 2: Publish with ReadyToRun for faster cold startup
Write-Host "[2/4] Building and publishing..." -ForegroundColor Yellow
dotnet publish $ProjectFile `
    --configuration $Configuration `
    --runtime win-x64 `
    --self-contained true `
    --output $OutputDir `
    -p:PublishReadyToRun=true `
    -p:PublishSingleFile=false `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=None `
    -p:DebugSymbols=false

if ($LASTEXITCODE -ne 0) {
    Write-Host "Build failed!" -ForegroundColor Red
    exit 1
}

# Step 3: Copy Windows-specific resources
Write-Host "[3/4] Copying platform resources..." -ForegroundColor Yellow
$assetsSource = Join-Path $ProjectRoot "assets"
$assetsDest = Join-Path $OutputDir "assets"
if (Test-Path $assetsSource) {
    Copy-Item -Recurse -Force $assetsSource $assetsDest
}

# Copy node-skin-server
$skinServerSource = Join-Path $ProjectRoot "node-skin-server"
$skinServerDest = Join-Path $OutputDir "node-skin-server"
if (Test-Path $skinServerSource) {
    Copy-Item -Recurse -Force $skinServerSource $skinServerDest
}

# Step 4: Create NSIS installer if requested
if ($CreateInstaller) {
    Write-Host "[4/4] Creating NSIS installer..." -ForegroundColor Yellow
    $nsiScript = Join-Path $PSScriptRoot "setup\installer.nsi"
    if (Test-Path $nsiScript) {
        makensis /DBUILD_DIR="$OutputDir" /DPUBLISH_DIR="$OutputDir" $nsiScript
        if ($LASTEXITCODE -ne 0) {
            Write-Host "Installer creation failed!" -ForegroundColor Red
        } else {
            Write-Host "Installer created successfully!" -ForegroundColor Green
        }
    } else {
        Write-Host "NSIS script not found at $nsiScript — skipping installer" -ForegroundColor Yellow
    }
} else {
    Write-Host "[4/4] Skipping installer (use -CreateInstaller to enable)" -ForegroundColor Gray
}

Write-Host "`n═══════════════════════════════════════════════════" -ForegroundColor Cyan
$exePath = Join-Path $OutputDir "AetherLauncher.exe"
if (Test-Path $exePath) {
    $size = [math]::Round((Get-Item $exePath).Length / 1MB, 2)
    Write-Host "  Build complete! ($size MB)" -ForegroundColor Green
    Write-Host "  Run: $exePath" -ForegroundColor White
} else {
    # Try alternative executable name
    $altExe = Get-ChildItem -Path $OutputDir -Filter "*.exe" | Select-Object -First 1
    if ($altExe) {
        $size = [math]::Round($altExe.Length / 1MB, 2)
        Write-Host "  Build complete! ($size MB)" -ForegroundColor Green
        Write-Host "  Run: $($altExe.FullName)" -ForegroundColor White
    } else {
        Write-Host "  Build complete!" -ForegroundColor Green
    }
}
Write-Host "═══════════════════════════════════════════════════" -ForegroundColor Cyan
