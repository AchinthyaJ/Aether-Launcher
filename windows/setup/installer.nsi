!include "MUI2.nsh"

Name "Aether Launcher"
OutFile "${BUILD_DIR}/setup.exe"
InstallDir "$PROGRAMFILES64\Aether Launcher"
InstallDirRegKey HKLM "Software\AetherLauncher" "Install_Dir"

RequestExecutionLevel admin

!define MUI_ABORTWARNING
!define MUI_ICON "../../minecraft.ico"
!define MUI_UNICON "../../minecraft.ico"

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

Function .onInit
  SetRegView 64
FunctionEnd

Function un.onInit
  SetRegView 64
FunctionEnd

Section "Install"
  SetRegView 64
  SetOutPath "$INSTDIR"
  
  ; Copy all files compiled by dotnet publish
  File /r "${PUBLISH_DIR}/*.*"
  
  ; Write install path to registry
  WriteRegStr HKLM "Software\AetherLauncher" "Install_Dir" "$INSTDIR"
  
  ; Write uninstall registry keys
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\AetherLauncher" "DisplayName" "Aether Launcher"
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\AetherLauncher" "UninstallString" '"$INSTDIR\uninstall.exe"'
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\AetherLauncher" "DisplayIcon" '"$INSTDIR\AetherLauncher.exe"'
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\AetherLauncher" "Publisher" "Aether Launcher Team"
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\AetherLauncher" "DisplayVersion" "1.0.4"
  WriteRegDWORD HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\AetherLauncher" "NoModify" 1
  WriteRegDWORD HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\AetherLauncher" "NoRepair" 1
  
  ; Create uninstaller
  WriteUninstaller "$INSTDIR\uninstall.exe"
  
  ; Create shortcuts
  CreateDirectory "$SMPROGRAMS\Aether Launcher"
  CreateShortcut "$SMPROGRAMS\Aether Launcher\Aether Launcher.lnk" "$INSTDIR\AetherLauncher.exe" "" "$INSTDIR\AetherLauncher.exe" 0
  CreateShortcut "$SMPROGRAMS\Aether Launcher\Uninstall.lnk" "$INSTDIR\uninstall.exe"
  CreateShortcut "$DESKTOP\Aether Launcher.lnk" "$INSTDIR\AetherLauncher.exe" "" "$INSTDIR\AetherLauncher.exe" 0
SectionEnd

Section "Uninstall"
  SetRegView 64
  Delete "$DESKTOP\Aether Launcher.lnk"
  RMDir /r "$SMPROGRAMS\Aether Launcher"
  
  ; Remove files
  RMDir /r "$INSTDIR"
  
  ; Remove registry keys
  DeleteRegKey HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\AetherLauncher"
  DeleteRegKey HKLM "Software\AetherLauncher"
SectionEnd
