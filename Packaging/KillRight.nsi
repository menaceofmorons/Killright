; Defines passed by Build-AlphaPackage.ps1: APP_DIR, OUT_FILE, APP_VERSION
Unicode true
SetCompressor /SOLID lzma
!include "MUI2.nsh"

!define APP_NAME "KillRight"
!define APP_EXE "Killright.UI.exe"
!define UNINST_KEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\KillRight"

Name "${APP_NAME} Alpha v0.1"
OutFile "${OUT_FILE}"
InstallDir "$LOCALAPPDATA\Programs\KillRight"
InstallDirRegKey HKCU "Software\KillRight" "InstallDir"
RequestExecutionLevel user
BrandingText "KillRight Alpha v0.1"

VIProductVersion "${APP_VERSION}.0"
VIAddVersionKey "ProductName" "${APP_NAME}"
VIAddVersionKey "FileDescription" "${APP_NAME} Alpha v0.1 Setup"
VIAddVersionKey "FileVersion" "${APP_VERSION}"
VIAddVersionKey "ProductVersion" "${APP_VERSION}"

!define MUI_ICON "${NSISDIR}\Contrib\Graphics\Icons\modern-install.ico"
!define MUI_ABORTWARNING
!define MUI_FINISHPAGE_RUN "$INSTDIR\${APP_EXE}"
!define MUI_FINISHPAGE_RUN_TEXT "Launch ${APP_NAME}"

!insertmacro MUI_PAGE_WELCOME
!define MUI_PAGE_CUSTOMFUNCTION_LEAVE DirLeave
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_LANGUAGE "English"

; Always install into a dedicated "KillRight" folder so uninstall (RMDir /r) can never remove a shared folder.
Function DirLeave
    StrCpy $0 $INSTDIR "" -10
    StrCmp $0 "\KillRight" +2
    StrCpy $INSTDIR "$INSTDIR\KillRight"
FunctionEnd

Section "Install"
    nsExec::Exec 'taskkill /F /IM ${APP_EXE}'
    Sleep 500

    SetOutPath "$INSTDIR"
    File /r "${APP_DIR}\*.*"
    WriteUninstaller "$INSTDIR\Uninstall.exe"

    CreateShortcut "$SMPROGRAMS\${APP_NAME}.lnk" "$INSTDIR\${APP_EXE}" "" "$INSTDIR\${APP_EXE}" 0

    WriteRegStr HKCU "Software\KillRight" "InstallDir" "$INSTDIR"
    WriteRegStr HKCU "${UNINST_KEY}" "DisplayName" "${APP_NAME} (Alpha v0.1)"
    WriteRegStr HKCU "${UNINST_KEY}" "DisplayVersion" "${APP_VERSION}"
    WriteRegStr HKCU "${UNINST_KEY}" "Publisher" "${APP_NAME}"
    WriteRegStr HKCU "${UNINST_KEY}" "InstallLocation" "$INSTDIR"
    WriteRegStr HKCU "${UNINST_KEY}" "DisplayIcon" "$INSTDIR\${APP_EXE}"
    WriteRegStr HKCU "${UNINST_KEY}" "UninstallString" '"$INSTDIR\Uninstall.exe"'
    WriteRegDWORD HKCU "${UNINST_KEY}" "NoModify" 1
    WriteRegDWORD HKCU "${UNINST_KEY}" "NoRepair" 1
SectionEnd

Section "Uninstall"
    nsExec::Exec 'taskkill /F /IM ${APP_EXE}'
    Sleep 500

    Delete "$SMPROGRAMS\${APP_NAME}.lnk"
    DeleteRegKey HKCU "${UNINST_KEY}"
    DeleteRegKey HKCU "Software\KillRight"
    RMDir /r "$INSTDIR"
    ; User data in %LOCALAPPDATA%\KillRight (database, backups, logs) is intentionally kept.
SectionEnd
