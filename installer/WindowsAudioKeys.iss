; Windows Audio Keys 설치 스크립트 (Inno Setup 7).
; package.ps1이 빌드·테스트·버전 확인을 마친 뒤 /DAppVersion=<버전>을 넘겨 컴파일한다.
; 직접 컴파일하려면: ISCC.exe /DAppVersion=1.2.0 installer\WindowsAudioKeys.iss
;
; 설계 원칙
; - 관리자 권한으로 C:\Program Files\WindowsAudioKeys 에만 설치한다(위치 변경 불가).
;   로그인 시 관리자 권한 자동 시작은 관리자 전용 위치에서만 허용되기 때문이다(src\InstallLocation.cs).
; - 제거할 때 이 설치본을 가리키는 이 앱 소유 자동 시작 작업만 지운다(--uninstall-cleanup).
; - 사용자 설정(%LOCALAPPDATA%\GameAudioKeys)은 건드리지 않는다.

#ifndef AppVersion
  #error AppVersion is not defined. Run package.ps1, or pass /DAppVersion=<major.minor.patch>.
#endif

; 저장소 루트(이 스크립트의 상위 폴더). 설치 파일에는 build.ps1이 루트에 내보낸 배포본을 넣는다.
#define RepoRoot AddBackslash(SourcePath) + ".."

[Setup]
; AppId는 업그레이드·제거 시 같은 설치로 인식하는 기준이므로 절대 바꾸지 않는다.
AppId={{C4B28F6F-1CC9-4927-8047-470BD7FD1D27}
AppName=Windows Audio Keys
AppVersion={#AppVersion}
AppVerName=Windows Audio Keys {#AppVersion}
AppPublisherURL=https://github.com/devbattery/windows-audio-keys
AppSupportURL=https://github.com/devbattery/windows-audio-keys/issues
AppUpdatesURL=https://github.com/devbattery/windows-audio-keys/releases
VersionInfoVersion={#AppVersion}.0
DefaultDirName={autopf}\WindowsAudioKeys
DisableDirPage=yes
; 이전 설치 위치(레지스트리)를 다시 쓰지 않는다. 위치는 PrepareToInstall에서도 검사한다.
UsePreviousAppDir=no
DisableProgramGroupPage=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
; 설치·업그레이드 중 실행 중인 앱은 Restart Manager로 닫는다(대화형은 묻고, 무인 설치는 자동).
; 앱은 WM_ENDSESSION을 받으면 정상 종료한다(src\MainForm.cs). AppMutex는 무인 업그레이드를
; 항상 취소시키므로 쓰지 않고, 제거 단계에서만 [Code]에서 실행 여부를 확인한다.
CloseApplications=yes
RestartApplications=no
SetupIconFile={#RepoRoot}\bin\WindowsAudioKeys.ico
UninstallDisplayIcon={app}\WindowsAudioKeys.exe
UninstallDisplayName=Windows Audio Keys
WizardStyle=modern
OutputDir={#RepoRoot}\dist
OutputBaseFilename=WindowsAudioKeys-v{#AppVersion}-setup
Compression=lzma2
SolidCompression=yes

[Languages]
Name: "korean"; MessagesFile: "compiler:Languages\Korean.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#RepoRoot}\WindowsAudioKeys.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#RepoRoot}\WindowsAudioKeys.exe.config"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#RepoRoot}\README.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#RepoRoot}\CHANGELOG.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#RepoRoot}\LICENSE"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\Windows Audio Keys"; Filename: "{app}\WindowsAudioKeys.exe"
Name: "{autodesktop}\Windows Audio Keys"; Filename: "{app}\WindowsAudioKeys.exe"; Tasks: desktopicon

[Run]
; postinstall 항목은 설치를 시작한 사용자의 일반 권한으로 실행된다.
Filename: "{app}\WindowsAudioKeys.exe"; Description: "{cm:LaunchProgram,Windows Audio Keys}"; Flags: postinstall nowait skipifsilent

[CustomMessages]
korean.AppRunningUninstall=Windows Audio Keys가 실행 중입니다.%n%n트레이 아이콘 메뉴의 [종료]로 앱을 끈 뒤 [확인]을 누르세요. [취소]를 누르면 제거를 중단합니다.
english.AppRunningUninstall=Windows Audio Keys is running.%n%nExit it from the tray icon menu, then click OK. Click Cancel to stop uninstalling.
korean.CleanupFailed=로그인 자동 시작 작업을 모두 지우지 못했습니다(코드 %1).%n%n제거는 계속합니다. 작업 스케줄러 라이브러리에서 "WindowsAudioKeys-"로 시작하는 작업이 남아 있으면 직접 삭제하세요.
english.CleanupFailed=Could not remove every login startup task (code %1).%n%nUninstall will continue. If a task starting with "WindowsAudioKeys-" remains in the Task Scheduler Library, delete it manually.
korean.WrongInstallDir=Windows Audio Keys는 %1 에만 설치할 수 있습니다(로그인 자동 시작 보안 조건). /DIR 없이 다시 실행하세요.
english.WrongInstallDir=Windows Audio Keys can only be installed to %1 (required by the login startup security check). Run Setup again without /DIR.

[Code]
// 앱의 단일 실행 뮤텍스(src\Program.cs의 MutexName과 같아야 한다).
const
  AppMutexName = 'Local\GameAudioKeys.SingleInstance';

// 제거 전에 앱이 실행 중이면 종료를 안내한다. /SUPPRESSMSGBOXES 무인 제거에서는 [취소]로
// 처리되어 실행 중인 파일을 지우려 하지 않고 안전하게 중단한다(/VERYSILENT만 주면 이 안내는 표시된다).
// Local\ 뮤텍스라 같은 세션의 인스턴스만 감지한다.
function InitializeUninstall(): Boolean;
begin
  Result := True;
  while CheckForMutexes(AppMutexName) do
  begin
    if SuppressibleMsgBox(CustomMessage('AppRunningUninstall'), mbConfirmation, MB_OKCANCEL, IDCANCEL) <> IDOK then
    begin
      Result := False;
      Exit;
    end;
  end;
end;

// 설치 위치 고정: 명령줄 /DIR 로도 Program Files\WindowsAudioKeys 밖에는 설치하지 않는다.
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  Expected: String;
begin
  Result := '';
  Expected := ExpandConstant('{autopf}\WindowsAudioKeys');
  if CompareText(RemoveBackslashUnlessRoot(ExpandConstant('{app}')), Expected) <> 0 then
    Result := FmtMessage(CustomMessage('WrongInstallDir'), [Expected]);
end;

// 파일을 지우기 전에 이 설치본을 가리키는 이 앱 소유 자동 시작 작업을 지운다.
// [UninstallRun]은 종료 코드를 확인하지 못하므로 여기서 실행하고, 실패하면 알린 뒤 제거를 계속한다.
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  ResultCode: Integer;
begin
  if CurUninstallStep = usUninstall then
  begin
    if not Exec(ExpandConstant('{app}\WindowsAudioKeys.exe'), '--uninstall-cleanup', '',
      SW_HIDE, ewWaitUntilTerminated, ResultCode) then
      ResultCode := -1;
    if ResultCode <> 0 then
      SuppressibleMsgBox(FmtMessage(CustomMessage('CleanupFailed'), [IntToStr(ResultCode)]),
        mbError, MB_OK, IDOK);
  end;
end;
