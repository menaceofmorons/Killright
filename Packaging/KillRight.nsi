; Defines passed by Build-AlphaPackage.ps1: APP_DIR, OUT_FILE, APP_VERSION
Unicode true
SetCompressor /SOLID lzma
!include "MUI2.nsh"

!define APP_NAME "KillRight"
!define APP_EXE "Killright.UI.exe"
!define UNINST_KEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\KillRight"

Name "${APP_NAME} Alpha v${APP_VERSION}"
OutFile "${OUT_FILE}"
InstallDir "$LOCALAPPDATA\Programs\KillRight"
InstallDirRegKey HKCU "Software\KillRight" "InstallDir"
RequestExecutionLevel user
BrandingText "KillRight Alpha v${APP_VERSION}"

VIProductVersion "${APP_VERSION}.0"
VIAddVersionKey "ProductName" "${APP_NAME}"
VIAddVersionKey "FileDescription" "${APP_NAME} Alpha v${APP_VERSION} Setup"
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

    SetRegView 64
    StrCpy $1 "$PROGRAMFILES64\dotnet\shared\Microsoft.WindowsDesktop.App\9.*"
    FindFirst $0 $2 $1
    FindClose $0
    StrCmp $2 "" 0 RuntimeFound
    MessageBox MB_YESNO|MB_ICONEXCLAMATION "${APP_NAME} needs the .NET 9 Desktop Runtime (x64), which was not found on this computer.$\r$\n$\r$\nOpen the Microsoft download page now? Install the Desktop Runtime, then start ${APP_NAME}." /SD IDNO IDNO RuntimeFound
    ExecShell "open" "https://dotnet.microsoft.com/download/dotnet/9.0"
    RuntimeFound:

    Delete "$INSTDIR\*.dll"
    Delete "$INSTDIR\*.pdb"
    Delete "$INSTDIR\*.json"
    Delete "$INSTDIR\${APP_EXE}"
    Delete "$INSTDIR\createdump.exe"
    RMDir /r "$INSTDIR\cs"
    RMDir /r "$INSTDIR\de"
    RMDir /r "$INSTDIR\es"
    RMDir /r "$INSTDIR\fr"
    RMDir /r "$INSTDIR\it"
    RMDir /r "$INSTDIR\ja"
    RMDir /r "$INSTDIR\ko"
    RMDir /r "$INSTDIR\pl"
    RMDir /r "$INSTDIR\pt-BR"
    RMDir /r "$INSTDIR\ru"
    RMDir /r "$INSTDIR\tr"
    RMDir /r "$INSTDIR\zh-Hans"
    RMDir /r "$INSTDIR\zh-Hant"

    SetOutPath "$INSTDIR"
    File /r "${APP_DIR}\*.*"
    WriteUninstaller "$INSTDIR\Uninstall.exe"

    CreateShortcut "$SMPROGRAMS\${APP_NAME}.lnk" "$INSTDIR\${APP_EXE}" "" "$INSTDIR\${APP_EXE}" 0

    WriteRegStr HKCU "Software\KillRight" "InstallDir" "$INSTDIR"
    WriteRegStr HKCU "${UNINST_KEY}" "DisplayName" "${APP_NAME} (Alpha v${APP_VERSION})"
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
