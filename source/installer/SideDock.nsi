Unicode true
!include "MUI2.nsh"
!include "LogicLib.nsh"
!include "FileFunc.nsh"
!include "x64.nsh"
!ifndef APP_VERSION
!define APP_VERSION "0.3.0"
!endif
!ifndef OUTPUT_FILE
!define OUTPUT_FILE "${__FILEDIR__}\..\..\dist\SideDock-${APP_VERSION}-Setup.exe"
!endif
Name "SideDock"
OutFile "${OUTPUT_FILE}"
InstallDir "$LOCALAPPDATA\Programs\SideDock"
RequestExecutionLevel user
SetCompressor /SOLID lzma
BrandingText "SideDock ${APP_VERSION}"
Var TestMode
!define MUI_ABORTWARNING
!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_LANGUAGE "SimpChinese"
!insertmacro MUI_LANGUAGE "English"

Function .onInit
  ${IfNot} ${RunningX64}
    MessageBox MB_OK "SideDock requires 64-bit Windows."
    Abort
  ${EndIf}
  ${GetParameters} $0
  ClearErrors
  ${GetOptions} $0 "/TESTMODE" $1
  ${IfNot} ${Errors}
    StrCpy $TestMode "1"
  ${EndIf}
  ${If} $TestMode != "1"
  System::Call 'kernel32::OpenMutexW(i 0x100000, i 0, w "Local\SideDock.Main") p.r0'
  ${If} $0 != 0
    System::Call 'kernel32::CloseHandle(p r0)'
    MessageBox MB_OK "Please exit SideDock from its tray menu before installing."
    Abort
  ${EndIf}
  ${EndIf}
FunctionEnd

Section "SideDock" Main
  SetOutPath "$INSTDIR"
  File /r /x smoke-* /x startup-result.json "${__FILEDIR__}\..\..\portable\*.*"
  WriteUninstaller "$INSTDIR\Uninstall.exe"
  FileOpen $0 "$INSTDIR\sidedock-install.marker" w
  FileWrite $0 "SideDock ${APP_VERSION}"
  FileClose $0
  ${If} $TestMode != "1"
    CreateDirectory "$SMPROGRAMS\SideDock"
    CreateShortcut "$SMPROGRAMS\SideDock\SideDock.lnk" "$INSTDIR\SideDock.exe"
    CreateShortcut "$SMPROGRAMS\SideDock\Uninstall.lnk" "$INSTDIR\Uninstall.exe"
    CreateShortcut "$DESKTOP\SideDock.lnk" "$INSTDIR\SideDock.exe"
    WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\SideDock" "DisplayName" "SideDock"
    WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\SideDock" "DisplayVersion" "${APP_VERSION}"
    WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\SideDock" "Publisher" "SideDock Project"
    WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\SideDock" "UninstallString" '$\"$INSTDIR\Uninstall.exe$\"'
    WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\SideDock" "InstallLocation" "$INSTDIR"
    WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\SideDock" "DisplayIcon" "$INSTDIR\SideDock.exe"
    WriteRegDWORD HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\SideDock" "NoModify" 1
    WriteRegDWORD HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\SideDock" "NoRepair" 1
    ReadRegStr $0 HKCU "Software\Microsoft\Windows\CurrentVersion\Run" "SideDock"
    ${If} $0 != ""
      WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Run" "SideDock" '$\"$INSTDIR\SideDock.exe$\" --startup'
    ${EndIf}
  ${EndIf}
SectionEnd

Function un.onInit
  ${GetParameters} $0
  ClearErrors
  ${GetOptions} $0 "/TESTMODE" $1
  ${IfNot} ${Errors}
    StrCpy $TestMode "1"
  ${EndIf}
  IfFileExists "$INSTDIR\sidedock-install.marker" +3 0
  MessageBox MB_OK "Installation marker missing; uninstall stopped."
  Abort
  ${If} $TestMode != "1"
  System::Call 'kernel32::OpenMutexW(i 0x100000, i 0, w "Local\SideDock.Main") p.r0'
  ${If} $0 != 0
    System::Call 'kernel32::CloseHandle(p r0)'
    MessageBox MB_OK "Please exit SideDock from its tray menu before uninstalling."
    Abort
  ${EndIf}
  ${EndIf}
FunctionEnd

Section "Uninstall"
  ${If} $TestMode != "1"
    Delete "$DESKTOP\SideDock.lnk"
    Delete "$SMPROGRAMS\SideDock\SideDock.lnk"
    Delete "$SMPROGRAMS\SideDock\Uninstall.lnk"
    RMDir "$SMPROGRAMS\SideDock"
    DeleteRegKey HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\SideDock"
    DeleteRegValue HKCU "Software\Microsoft\Windows\CurrentVersion\Run" "SideDock"
  ${EndIf}
  ; Remove only known installed files. Keep unrelated files and personal browser data.
  !include "uninstall-files.nsh"
  Delete "$INSTDIR\sidedock-install.marker"
  Delete "$INSTDIR\Uninstall.exe"
  RMDir "$INSTDIR"
SectionEnd
