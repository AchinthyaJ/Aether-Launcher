param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"

$root = Resolve-Path (Join-Path $PSScriptRoot "..")
$publishDir = Join-Path $root "dist\publish\win-x64"
$installerDir = Join-Path $root "dist\installer"
$wxsFile = Join-Path $PSScriptRoot "FugoLauncher.wxs"
$projectFile = Join-Path $root "OfflineMinecraftLauncher.csproj"

Push-Location $root
try {
    dotnet publish $projectFile -c $Configuration -r $Runtime -o $publishDir --self-contained true /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true /p:PublishReadyToRun=false
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed with exit code $LASTEXITCODE."
    }

    dotnet tool restore
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet tool restore failed with exit code $LASTEXITCODE."
    }
}
finally {
    Pop-Location
}

New-Item -ItemType Directory -Force -Path $installerDir | Out-Null

$msiPath = Join-Path $installerDir "FugoLauncher-Setup.msi"

$wixArgs = @(
    "tool", "run", "wix", "--", "build",
    "-arch", "x64",
    "-src", $wxsFile,
    "-out", $msiPath
)

Write-Host "Running: dotnet $($wixArgs -join ' ')"
& dotnet @wixArgs

if ($LASTEXITCODE -ne 0) {
    throw "WiX build failed with exit code $LASTEXITCODE."
}

Write-Host "MSI created at $msiPath"
