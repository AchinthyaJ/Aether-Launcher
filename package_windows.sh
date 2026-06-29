#!/bin/bash

# Configuration
VERSION="1.0.4"
APP_NAME="aether-launcher"
DISPLAY_NAME="Aether Launcher"

# Directories
DIST_DIR="dist"
WINDOWS_BUILD_DIR="dist/windows-x64"
NSIS_TEMP_DIR="dist/windows-publish"

echo "========================================================"
echo "Creating Standalone Windows Setup Bundle..."
echo "========================================================"
mkdir -p "$WINDOWS_BUILD_DIR"
mkdir -p "$NSIS_TEMP_DIR"

echo "Publishing self-contained Windows (win-x64) executable (no trimming for maximum compatibility)..."
dotnet publish OfflineMinecraftLauncher.csproj -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true \
  -p:PublishTrimmed=false \
  -p:PublishReadyToRun=true \
  -p:DebuggerSupport=false \
  -p:EnableUnsafeBinaryFormatterSerialization=false \
  -p:EnableUnsafeUTF7Encoding=false \
  -p:HttpActivityPropagationSupport=false \
  -p:InvariantGlobalization=false \
  -p:JsonSerializerIsReflectionEnabledByDefault=true \
  -o "$NSIS_TEMP_DIR"

if [ $? -ne 0 ]; then
    echo "========================================================"
    echo " ERROR: Standalone Windows build failed."
    echo "========================================================"
    exit 1
fi

# Detect if makensis is installed on the host compiling machine
if ! command -v makensis &> /dev/null; then
    echo "--------------------------------------------------------"
    echo "WARNING: 'makensis' (NSIS compiler) is not installed."
    echo "Because of this, an interactive GUI Setup installer cannot be compiled."
    echo "To compile a real Windows installer that registers in Program Files,"
    echo "please install NSIS on your Linux host machine:"
    echo "   sudo apt-get update && sudo apt-get install -y nsis"
    echo "--------------------------------------------------------"
    echo "Preparing a fully self-contained PORTABLE Windows build instead..."
    
    # Clear and copy all files (including node-skin-server, death-client, assets, etc.)
    rm -rf "$WINDOWS_BUILD_DIR"/*
    cp -r "$NSIS_TEMP_DIR"/* "$WINDOWS_BUILD_DIR/"
    
    echo "========================================================"
    echo " SUCCESS! Portable Windows build prepared!"
    echo " Location: ./$WINDOWS_BUILD_DIR/"
    echo " Run './$WINDOWS_BUILD_DIR/AetherLauncher.exe' to launch."
    echo "========================================================"
    exit 0
fi

echo "Compiling high-end interactive GUI Setup Installer via makensis..."
makensis -DBUILD_DIR="$(pwd)/$WINDOWS_BUILD_DIR" -DPUBLISH_DIR="$(pwd)/$NSIS_TEMP_DIR" windows/setup/installer.nsi
MAKENSIS_STATUS=$?

if [ $MAKENSIS_STATUS -eq 0 ]; then
    echo "========================================================"
    echo " SUCCESS! Interactive GUI Setup installer.exe generated!"
    echo " It will install to C:\\Program Files\\${DISPLAY_NAME}"
    echo " and register the app in the Windows App List."
    echo " Location: ./$WINDOWS_BUILD_DIR/setup.exe"
    echo "========================================================"
else
    echo "ERROR: NSIS compilation failed."
    exit 1
fi
