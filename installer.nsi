; PrettyMark NSIS Installer Script
; Produces a standard Windows Setup.exe with Start menu shortcuts,
; .md/.markdown file association, and uninstaller.

!include "MUI2.nsh"
!include "FileFunc.nsh"

; --- Configuration ---
!define PRODUCT_NAME "PrettyMark"
!define PRODUCT_VERSION "1.0.0"
!define PRODUCT_PUBLISHER "Eagle1"
!define PRODUCT_WEB_SITE "https://gitlab.com/eagle1/prettymark"
!define PRODUCT_DIR_REGKEY "Software\${PRODUCT_NAME}"
!define PRODUCT_UNINST_KEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\${PRODUCT_NAME}"

Name "${PRODUCT_NAME} ${PRODUCT_VERSION}"
OutFile "bin\PrettyMark-Setup-${PRODUCT_VERSION}-win-x64.exe"
InstallDir "$PROGRAMFILES64\${PRODUCT_NAME}"
InstallDirRegKey HKLM "${PRODUCT_DIR_REGKEY}" "InstallDir"
RequestExecutionLevel admin
SetCompressor /SOLID lzma
SetCompressorDictSize 64

; --- MUI Settings ---
!define MUI_ABORTWARNING
!define MUI_ICON "assets\favicon.ico"
!define MUI_UNICON "assets\favicon.ico"

; --- Pages ---
!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!define MUI_FINISHPAGE_RUN "$INSTDIR\PrettyMark.exe"
!define MUI_FINISHPAGE_RUN_TEXT "Launch PrettyMark"
!insertmacro MUI_PAGE_FINISH

!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES

; --- Languages ---
!insertmacro MUI_LANGUAGE "English"
!insertmacro MUI_LANGUAGE "Italian"
!insertmacro MUI_LANGUAGE "Spanish"
!insertmacro MUI_LANGUAGE "PortugueseBR"
!insertmacro MUI_LANGUAGE "French"
!insertmacro MUI_LANGUAGE "German"
!insertmacro MUI_LANGUAGE "SimpChinese"
!insertmacro MUI_LANGUAGE "Japanese"
!insertmacro MUI_LANGUAGE "Korean"
!insertmacro MUI_LANGUAGE "Russian"
!insertmacro MUI_LANGUAGE "Turkish"
!insertmacro MUI_LANGUAGE "Ukrainian"

; --- Install Section ---
Section "Install"
    SetOutPath "$INSTDIR"

    ; Main executable
    File "bin\Release\net8.0-windows\win-x64\publish\PrettyMark.exe"

    ; Icon for shortcuts
    File /oname=PrettyMark.ico "assets\favicon.ico"

    ; Write uninstaller
    WriteUninstaller "$INSTDIR\Uninstall.exe"

    ; Start Menu shortcuts
    CreateDirectory "$SMPROGRAMS\${PRODUCT_NAME}"
    CreateShortCut "$SMPROGRAMS\${PRODUCT_NAME}\${PRODUCT_NAME}.lnk" "$INSTDIR\PrettyMark.exe" "" "$INSTDIR\PrettyMark.ico"
    CreateShortCut "$SMPROGRAMS\${PRODUCT_NAME}\Uninstall.lnk" "$INSTDIR\Uninstall.exe"

    ; Desktop shortcut
    CreateShortCut "$DESKTOP\${PRODUCT_NAME}.lnk" "$INSTDIR\PrettyMark.exe" "" "$INSTDIR\PrettyMark.ico"

    ; File associations: .md
    WriteRegStr HKLM "Software\Classes\.md" "" "PrettyMark.Markdown"
    WriteRegStr HKLM "Software\Classes\.md" "Content Type" "text/markdown"
    WriteRegStr HKLM "Software\Classes\.md\OpenWithProgids" "PrettyMark.Markdown" ""

    ; File associations: .markdown
    WriteRegStr HKLM "Software\Classes\.markdown" "" "PrettyMark.Markdown"
    WriteRegStr HKLM "Software\Classes\.markdown\OpenWithProgids" "PrettyMark.Markdown" ""

    ; ProgId
    WriteRegStr HKLM "Software\Classes\PrettyMark.Markdown" "" "Markdown File"
    WriteRegStr HKLM "Software\Classes\PrettyMark.Markdown\DefaultIcon" "" "$INSTDIR\PrettyMark.ico,0"
    WriteRegStr HKLM "Software\Classes\PrettyMark.Markdown\shell\open\command" "" '"$INSTDIR\PrettyMark.exe" "%1"'

    ; Registry: install dir
    WriteRegStr HKLM "${PRODUCT_DIR_REGKEY}" "InstallDir" "$INSTDIR"

    ; Registry: Add/Remove Programs
    WriteRegStr HKLM "${PRODUCT_UNINST_KEY}" "DisplayName" "${PRODUCT_NAME}"
    WriteRegStr HKLM "${PRODUCT_UNINST_KEY}" "DisplayVersion" "${PRODUCT_VERSION}"
    WriteRegStr HKLM "${PRODUCT_UNINST_KEY}" "Publisher" "${PRODUCT_PUBLISHER}"
    WriteRegStr HKLM "${PRODUCT_UNINST_KEY}" "URLInfoAbout" "${PRODUCT_WEB_SITE}"
    WriteRegStr HKLM "${PRODUCT_UNINST_KEY}" "DisplayIcon" "$INSTDIR\PrettyMark.ico"
    WriteRegStr HKLM "${PRODUCT_UNINST_KEY}" "UninstallString" '"$INSTDIR\Uninstall.exe"'
    WriteRegDWORD HKLM "${PRODUCT_UNINST_KEY}" "NoModify" 1
    WriteRegDWORD HKLM "${PRODUCT_UNINST_KEY}" "NoRepair" 1

    ; Estimated size
    ${GetSize} "$INSTDIR" "/S=0K" $0 $1 $2
    IntFmt $0 "0x%08X" $0
    WriteRegDWORD HKLM "${PRODUCT_UNINST_KEY}" "EstimatedSize" $0

    ; Refresh shell icons
    System::Call 'Shell32::SHChangeNotify(i 0x8000000, i 0, p 0, p 0)'
SectionEnd

; --- Uninstall Section ---
Section "Uninstall"
    ; Remove files
    Delete "$INSTDIR\PrettyMark.exe"
    Delete "$INSTDIR\PrettyMark.ico"
    Delete "$INSTDIR\Uninstall.exe"
    RMDir "$INSTDIR"

    ; Remove Start Menu
    Delete "$SMPROGRAMS\${PRODUCT_NAME}\${PRODUCT_NAME}.lnk"
    Delete "$SMPROGRAMS\${PRODUCT_NAME}\Uninstall.lnk"
    RMDir "$SMPROGRAMS\${PRODUCT_NAME}"

    ; Remove Desktop shortcut
    Delete "$DESKTOP\${PRODUCT_NAME}.lnk"

    ; Remove file associations
    DeleteRegKey HKLM "Software\Classes\PrettyMark.Markdown"
    DeleteRegValue HKLM "Software\Classes\.md\OpenWithProgids" "PrettyMark.Markdown"
    DeleteRegValue HKLM "Software\Classes\.markdown\OpenWithProgids" "PrettyMark.Markdown"

    ; Remove registry keys
    DeleteRegKey HKLM "${PRODUCT_DIR_REGKEY}"
    DeleteRegKey HKLM "${PRODUCT_UNINST_KEY}"

    ; Refresh shell icons
    System::Call 'Shell32::SHChangeNotify(i 0x8000000, i 0, p 0, p 0)'
SectionEnd
