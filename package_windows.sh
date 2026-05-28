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

echo "Publishing self-contained Windows (win-x64) executable..."
dotnet publish OfflineMinecraftLauncher.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:PublishReadyToRun=true -p:IncludeNativeLibrariesForSelfContained=true -o "$NSIS_TEMP_DIR"

if [ $? -ne 0 ]; then
    echo "========================================================"
    echo " ERROR: Standalone Windows build failed."
    echo "========================================================"
    exit 1
fi

# Detect if makensis is installed on the host compiling machine
if ! command -v makensis &> /dev/null; then
    echo "--------------------------------------------------------"
    echo "NOTE: 'makensis' (NSIS compiler) is not installed."
    echo "To compile an interactive Windows installer that registers"
    echo "in Program Files & the Windows Apps/Remove programs list,"
    echo "please install NSIS on your Linux host machine:"
    echo "   sudo apt-get update && sudo apt-get install -y nsis"
    echo "--------------------------------------------------------"
    echo "Generating portable setup.exe fallback in separate folder..."
    cp "$NSIS_TEMP_DIR/AetherLauncher.exe" "$WINDOWS_BUILD_DIR/setup.exe"
    echo "========================================================"
    echo " SUCCESS! Portable Windows setup.exe generated!"
    echo " Location: ./$WINDOWS_BUILD_DIR/setup.exe"
    echo "========================================================"
    exit 0
fi

echo "Generating NSIS installation script..."
cat <<EOT > dist/installer.nsi
!include "MUI2.nsh"

Name "${DISPLAY_NAME}"
OutFile "${WINDOWS_BUILD_DIR}/setup.exe"
InstallDir "\$PROGRAMFILES64\\${DISPLAY_NAME}"
InstallDirRegKey HKLM "Software\\AetherLauncher" "Install_Dir"

RequestExecutionLevel admin

!define MUI_ABORTWARNING
!define MUI_ICON "minecraft.ico"
!define MUI_UNICON "minecraft.ico"

; Pages
!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH

!insertmacro MUI_UNPAGE_WELCOME
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_UNPAGE_FINISH

!insertmacro MUI_LANGUAGE "English"

Section "Install"
  SetOutPath "\$INSTDIR"
  
  ; Copy all files compiled by dotnet publish
  File /r "${NSIS_TEMP_DIR}\\*.*"
  
  ; Write install path to registry
  WriteRegStr HKLM "Software\\AetherLauncher" "Install_Dir" "\$INSTDIR"
  
  ; Write uninstall registry keys
  WriteRegStr HKLM "Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\AetherLauncher" "DisplayName" "${DISPLAY_NAME}"
  WriteRegStr HKLM "Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\AetherLauncher" "UninstallString" '"\$INSTDIR\\uninstall.exe"'
  WriteRegStr HKLM "Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\AetherLauncher" "DisplayIcon" '"\$INSTDIR\\AetherLauncher.exe"'
  WriteRegStr HKLM "Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\AetherLauncher" "Publisher" "Aether Launcher Team"
  WriteRegStr HKLM "Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\AetherLauncher" "DisplayVersion" "${VERSION}"
  WriteRegDWORD HKLM "Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\AetherLauncher" "NoModify" 1
  WriteRegDWORD HKLM "Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\AetherLauncher" "NoRepair" 1
  
  ; Create uninstaller
  WriteUninstaller "\$INSTDIR\\uninstall.exe"
  
  ; Create shortcuts
  CreateDirectory "\$SMPROGRAMS\\${DISPLAY_NAME}"
  CreateShortcut "\$SMPROGRAMS\\${DISPLAY_NAME}\\${DISPLAY_NAME}.lnk" "\$INSTDIR\\AetherLauncher.exe" "" "\$INSTDIR\\AetherLauncher.exe" 0
  CreateShortcut "\$SMPROGRAMS\\${DISPLAY_NAME}\\Uninstall.lnk" "\$INSTDIR\\uninstall.exe"
  CreateShortcut "\$DESKTOP\\${DISPLAY_NAME}.lnk" "\$INSTDIR\\AetherLauncher.exe" "" "\$INSTDIR\\AetherLauncher.exe" 0
SectionEnd

Section "Uninstall"
  Delete "\$DESKTOP\\${DISPLAY_NAME}.lnk"
  RMDir /r "\$SMPROGRAMS\\${DISPLAY_NAME}"
  
  ; Remove files
  RMDir /r "\$INSTDIR"
  
  ; Remove registry keys
  DeleteRegKey HKLM "Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\AetherLauncher"
  DeleteRegKey HKLM "Software\\AetherLauncher"
SectionEnd
EOT

echo "Compiling high-end interactive GUI Setup Installer via makensis..."
makensis dist/installer.nsi

if [ $? -eq 0 ]; then
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
