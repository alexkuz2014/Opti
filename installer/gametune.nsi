; Установщик GameTune (NSIS 3). Сборка: build/publish.sh (Linux) или build/publish.ps1 (Windows).
; makensis -DVERSION=1.0.0 -DSRCDIR=<папка с файлами> -DOUTFILE=<GameTune-Setup.exe> -DICON=<gametune.ico> installer/gametune.nsi
Unicode true
ManifestDPIAware true
!include "MUI2.nsh"
!include "x64.nsh"

!ifndef VERSION
  !define VERSION "0.0.0"
!endif
!ifndef NUMVER
  !define NUMVER "0.0.0"
!endif
!define APPNAME "GameTune"
!define UNINSTKEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\GameTune"

Name "${APPNAME} ${VERSION}"
OutFile "${OUTFILE}"
InstallDir "$PROGRAMFILES64\GameTune"
InstallDirRegKey HKLM "Software\GameTune" "InstallDir"
RequestExecutionLevel admin
SetCompressor /SOLID lzma
BrandingText "GameTune ${VERSION}"
VIProductVersion "${NUMVER}.0"
VIAddVersionKey /LANG=1049 "ProductName" "GameTune"
VIAddVersionKey /LANG=1049 "FileDescription" "Установщик GameTune"
VIAddVersionKey /LANG=1049 "FileVersion" "${VERSION}"
VIAddVersionKey /LANG=1049 "ProductVersion" "${VERSION}"
VIAddVersionKey /LANG=1049 "LegalCopyright" "GameTune"

!define MUI_ICON "${ICON}"
!define MUI_UNICON "${ICON}"
!define MUI_ABORTWARNING
!define MUI_WELCOMEPAGE_TEXT "GameTune находит реальные причины лагов, применяет только подходящие исправления и честно сравнивает результат «до / после» по замерам.$\r$\n$\r$\nКаждое изменение записывается в журнал и может быть откачено."
!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!define MUI_FINISHPAGE_RUN "$INSTDIR\GameTune.exe"
!define MUI_FINISHPAGE_RUN_TEXT "Запустить GameTune"
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_LANGUAGE "Russian"
!insertmacro MUI_LANGUAGE "English"

Function .onInit
  ${IfNot} ${RunningX64}
    MessageBox MB_ICONSTOP "GameTune требует 64-битную Windows 10 или 11."
    Abort
  ${EndIf}
  SetRegView 64
FunctionEnd

Section "GameTune" SecMain
  SectionIn RO
  SetRegView 64
  SetOutPath "$INSTDIR"
  File /r "${SRCDIR}\*.*"
  WriteUninstaller "$INSTDIR\Uninstall.exe"

  CreateDirectory "$SMPROGRAMS\GameTune"
  CreateShortcut "$SMPROGRAMS\GameTune\GameTune.lnk" "$INSTDIR\GameTune.exe"
  CreateShortcut "$SMPROGRAMS\GameTune\Удалить GameTune.lnk" "$INSTDIR\Uninstall.exe"
  CreateShortcut "$DESKTOP\GameTune.lnk" "$INSTDIR\GameTune.exe"

  WriteRegStr HKLM "Software\GameTune" "InstallDir" "$INSTDIR"
  WriteRegStr HKLM "${UNINSTKEY}" "DisplayName" "GameTune"
  WriteRegStr HKLM "${UNINSTKEY}" "DisplayVersion" "${VERSION}"
  WriteRegStr HKLM "${UNINSTKEY}" "Publisher" "GameTune"
  WriteRegStr HKLM "${UNINSTKEY}" "DisplayIcon" "$INSTDIR\GameTune.exe"
  WriteRegStr HKLM "${UNINSTKEY}" "InstallLocation" "$INSTDIR"
  WriteRegStr HKLM "${UNINSTKEY}" "UninstallString" '"$INSTDIR\Uninstall.exe"'
  WriteRegStr HKLM "${UNINSTKEY}" "QuietUninstallString" '"$INSTDIR\Uninstall.exe" /S'
  WriteRegStr HKLM "${UNINSTKEY}" "URLInfoAbout" "https://github.com/allora-dev/optim"
  WriteRegDWORD HKLM "${UNINSTKEY}" "NoModify" 1
  WriteRegDWORD HKLM "${UNINSTKEY}" "NoRepair" 1
SectionEnd

Function un.onInit
  SetRegView 64
FunctionEnd

Section "Uninstall"
  ; Изменения в системе не откатываются сами — спрашиваем.
  IfFileExists "$INSTDIR\gametune-cli.exe" 0 skip_rollback
    MessageBox MB_YESNO|MB_ICONQUESTION "Откатить все изменения, сделанные GameTune, перед удалением?$\r$\n$\r$\n«Нет» — изменения останутся, журнал отката сохранится в C:\ProgramData\GameTune." /SD IDNO IDNO skip_rollback
    ExecWait '"$INSTDIR\gametune-cli.exe" rollback --all --yes'
  skip_rollback:

  nsExec::Exec 'schtasks.exe /Delete /F /TN "GameTune\Resume"'

  Delete "$DESKTOP\GameTune.lnk"
  RMDir /r "$SMPROGRAMS\GameTune"
  ; Удаляем папку целиком, только если это действительно папка GameTune
  IfFileExists "$INSTDIR\GameTune.exe" 0 +2
    RMDir /r "$INSTDIR"

  DeleteRegKey HKLM "${UNINSTKEY}"
  DeleteRegKey HKLM "Software\GameTune"
SectionEnd
