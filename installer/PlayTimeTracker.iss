; PlayTimeTracker 설치 파일 (Inno Setup 6)
; 만들기: installer\build.ps1 (게시 → 이 스크립트 컴파일)
;
; - 관리자 권한 없이 현재 사용자에게만 설치한다 (%LOCALAPPDATA%\Programs\PlayTimeTracker)
; - 윈도우 시작 시 자동 실행은 넣지 않는다 (CLAUDE.md 규칙)
; - 사용 기록 DB는 %LOCALAPPDATA%\PlayTimeTracker 에 있으며, 제거할 때 지울지 묻는다 (기본: 남겨 둠)

#define AppName "PlayTimeTracker"
#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#define AppExe "PlayTimeTracker.exe"
#define Root ".."

[Setup]
AppId={{6B0E7C2A-3F4D-4C8E-9A51-2D7F1B9C4E60}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; 실행 중이면 앱이 쓰는 뮤텍스로 알아채고 종료를 요청한다
AppMutex=PlayTimeTracker.Recorder
SetupIconFile={#Root}\src\PlayTimeTracker.App\app.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
OutputDir={#Root}\artifacts\installer
OutputBaseFilename={#AppName}-Setup-{#AppVersion}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern

[Languages]
Name: "korean"; MessagesFile: "compiler:Languages\Korean.isl"

[Tasks]
Name: "desktopicon"; Description: "바탕 화면에 바로가기 만들기"; GroupDescription: "추가 작업:"; Flags: unchecked

[Files]
Source: "{#Root}\artifacts\app\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "{#AppName} 실행"; Flags: nowait postinstall skipifsilent

[Code]
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
  begin
    if SuppressibleMsgBox('사용 기록과 설정도 함께 지울까요?' + #13#10 + #13#10 +
         '"아니요"를 누르면 기록이 남아 있어서, 다시 설치했을 때 그대로 이어집니다.',
         mbConfirmation, MB_YESNO or MB_DEFBUTTON2, IDNO) = IDYES then
      DelTree(ExpandConstant('{localappdata}\PlayTimeTracker'), True, True, True);
  end;
end;
