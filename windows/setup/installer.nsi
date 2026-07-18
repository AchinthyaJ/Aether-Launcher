!include "MUI2.nsh"

Name "Fugo Launcher"
OutFile "${BUILD_DIR}/setup.exe"
InstallDir "$PROGRAMFILES64\Fugo Launcher"
InstallDirRegKey HKLM "Software\FugoLauncher" "Install_Dir"

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
  WriteRegStr HKLM "Software\FugoLauncher" "Install_Dir" "$INSTDIR"
  
  ; Write uninstall registry keys
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\FugoLauncher" "DisplayName" "Fugo Launcher"
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\FugoLauncher" "UninstallString" '"$INSTDIR\uninstall.exe"'
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\FugoLauncher" "DisplayIcon" '"$INSTDIR\FugoLauncher.exe"'
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\FugoLauncher" "Publisher" "Fugo Launcher Team"
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\FugoLauncher" "DisplayVersion" "3.1.0"
  WriteRegDWORD HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\FugoLauncher" "NoModify" 1
  WriteRegDWORD HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\FugoLauncher" "NoRepair" 1
  
  ; Create uninstaller
  WriteUninstaller "$INSTDIR\uninstall.exe"
  
  ; Create shortcuts
  CreateDirectory "$SMPROGRAMS\Fugo Launcher"
  CreateShortcut "$SMPROGRAMS\Fugo Launcher\Fugo Launcher.lnk" "$INSTDIR\FugoLauncher.exe" "" "$INSTDIR\FugoLauncher.exe" 0
  CreateShortcut "$SMPROGRAMS\Fugo Launcher\Uninstall.lnk" "$INSTDIR\uninstall.exe"
  CreateShortcut "$DESKTOP\Fugo Launcher.lnk" "$INSTDIR\FugoLauncher.exe" "" "$INSTDIR\FugoLauncher.exe" 0
SectionEnd

Section "Uninstall"
  SetRegView 64
  Delete "$DESKTOP\Fugo Launcher.lnk"
  RMDir /r "$SMPROGRAMS\Fugo Launcher"
  
  ; Remove files
  RMDir /r "$INSTDIR"
  
  ; Remove registry keys
  DeleteRegKey HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\FugoLauncher"
  DeleteRegKey HKLM "Software\FugoLauncher"
SectionEnd
