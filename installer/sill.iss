; Sill 설치 프로그램(Inno Setup 7). 관리자 권한 없이 %LOCALAPPDATA%\Programs\Sill에 설치하고 HKCU에만 등록한다(M5 설계 3.1–3.3절).
; 등록 키는 tools\register.ps1과 같아야 한다. 한쪽을 바꾸면 다른 쪽도 바꾼다.
; 빌드: tools\package.ps1 (build.ps1의 bin\으로 dist\Sill-Setup-<버전>.exe)

#define AppVersion "0.1.0"
; Inno 문자열에서 { 는 {{ 로 쓴다
#define Clsid "{{AE2C3550-FB10-4D93-B6E1-4FBED3C0AD90}"
#define CacheKey "Software\Microsoft\Windows\CurrentVersion\Explorer\Discardable\PostSetup\Component Categories"

[Setup]
AppId={{3AAB78AF-7F6E-4FCB-A0D2-0C5D758B8E3E}
AppName=Sill
AppVersion={#AppVersion}
AppPublisher=2j2h5
AppPublisherURL=https://github.com/2j2h5/winsill
DefaultDirName={autopf}\Sill
PrivilegesRequired=lowest
DisableDirPage=yes
DisableProgramGroupPage=yes
; Restart Manager는 band DLL을 쥔 explorer.exe를 닫겠다고 묻는다. 쓰이는 파일은 이름을 바꿔 비킨다(CurStepChanged).
CloseApplications=no
; bin\은 x64이고, 32비트 모드면 HKCU\Software\Classes\CLSID가 Wow6432Node로 간다
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
WizardStyle=modern
SetupIconFile=..\assets\sill.ico
UninstallDisplayIcon={app}\bin\sill-host.exe
UninstallDisplayName=Sill
OutputDir=..\dist
OutputBaseFilename=Sill-Setup-{#AppVersion}

[Files]
; bin\의 예전 *.old가 따라오지 않게 하나씩 적는다
Source: "..\bin\Sill.Band.dll"; DestDir: "{app}\bin"
Source: "..\bin\sill-host.exe"; DestDir: "{app}\bin"
Source: "..\bin\Microsoft.Web.WebView2.Core.dll"; DestDir: "{app}\bin"
Source: "..\bin\WebView2Loader.dll"; DestDir: "{app}\bin"
Source: "..\bin\web\*"; DestDir: "{app}\bin\web"
Source: "..\LICENSE"; DestDir: "{app}"
Source: "..\THIRD-PARTY-NOTICES.txt"; DestDir: "{app}"

[Registry]
; band(가로형 Explorer Bar). HKCR에 쓰면 HKLM으로 가므로(관리자 필요) HKCU\Software\Classes에 쓴다.
Root: HKCU; Subkey: "Software\Classes\CLSID\{#Clsid}"; ValueType: string; ValueData: "Sill"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\CLSID\{#Clsid}\InprocServer32"; ValueType: string; ValueData: "mscoree.dll"
Root: HKCU; Subkey: "Software\Classes\CLSID\{#Clsid}\InprocServer32"; ValueType: string; ValueName: "ThreadingModel"; ValueData: "Apartment"
Root: HKCU; Subkey: "Software\Classes\CLSID\{#Clsid}\InprocServer32"; ValueType: string; ValueName: "Class"; ValueData: "Sill.Band.SillBand"
Root: HKCU; Subkey: "Software\Classes\CLSID\{#Clsid}\InprocServer32"; ValueType: string; ValueName: "Assembly"; ValueData: "Sill.Band, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null"
Root: HKCU; Subkey: "Software\Classes\CLSID\{#Clsid}\InprocServer32"; ValueType: string; ValueName: "RuntimeVersion"; ValueData: "v4.0.30319"
Root: HKCU; Subkey: "Software\Classes\CLSID\{#Clsid}\InprocServer32"; ValueType: string; ValueName: "CodeBase"; ValueData: "{code:CodeBase}"
Root: HKCU; Subkey: "Software\Classes\CLSID\{#Clsid}\Implemented Categories\{{00021494-0000-0000-C000-000000000046}"
; '승인된 셸 확장만 허용' 정책이 켜진 PC 대비
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Shell Extensions\Approved"; ValueType: string; ValueName: "{#Clsid}"; ValueData: "Sill"; Flags: uninsdeletevalue
; 패널 높이 240px. 업데이트 때 사용자가 바꾼 높이를 덮지 않는다.
Root: HKCU; Subkey: "Software\Microsoft\Internet Explorer\Explorer Bars\{#Clsid}"; ValueType: binary; ValueName: "BarSize"; ValueData: "f0 00 00 00 00 00 00 00"; Flags: createvalueifdoesntexist uninsdeletekey
; 탐색기가 캐시한 Explorer Bar 목록을 지워 새 바를 인식하게 한다(설치·제거 둘 다)
Root: HKCU; Subkey: "{#CacheKey}\{{00021493-0000-0000-C000-000000000046}\Enum"; Flags: deletekey dontcreatekey uninsdeletekey
Root: HKCU; Subkey: "{#CacheKey}\{{00021494-0000-0000-C000-000000000046}\Enum"; Flags: deletekey dontcreatekey uninsdeletekey
; 단축키 상주 프로세스와 폴더 빈 곳의 "Open in Sill"(아이콘은 리소스 ID로, M7 결과 3.4절)
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "Sill"; ValueData: """{app}\bin\sill-host.exe"" keys"; Flags: uninsdeletevalue
Root: HKCU; Subkey: "Software\Classes\Directory\Background\shell\Sill"; ValueType: string; ValueName: "MUIVerb"; ValueData: "Open in Sill"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\Directory\Background\shell\Sill"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\bin\sill-host.exe"",-32512"
Root: HKCU; Subkey: "Software\Classes\Directory\Background\shell\Sill\command"; ValueType: string; ValueData: """{app}\bin\sill-host.exe"" open ""%V"""

[Run]
Filename: "{app}\bin\sill-host.exe"; Parameters: "keys"; Flags: nowait

[UninstallDelete]
; 로그, 설정, WebView2 데이터. 쓰이는 중이라 남으면 RunOnce가 다음 로그인 때 지운다.
Type: filesandordirs; Name: "{localappdata}\Sill"
Type: files; Name: "{app}\bin\*.old"

[Code]
var
  MovedAside: Boolean;

// regasm /codebase와 register.ps1과 같은 꼴(이스케이프 없음). 한글을 %XX로 바꾸면 탐색기가 적재하지 못한다(M5 시험).
function CodeBase(Param: String): String;
begin
  Result := 'file:///' + ExpandConstant('{app}\bin\Sill.Band.dll');
  StringChangeEx(Result, '\', '/', True);
end;

// .NET Framework는 CodeBase 경로의 #을 적재하지 못한다(%23도 안 됨). 설치 위치는 고정이라 사용자 이름에 #이 있을 때만.
function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  if Pos('#', ExpandConstant('{app}')) > 0 then
    Result := 'Sill can''t be installed in a folder whose path contains "#": ' + ExpandConstant('{app}');
end;

// sill-host를 끝내고 끝낸 수를 돌려준다. KeysOnly면 단축키 상주 프로세스만(명령줄이 keys로 끝남), 아니면 열린 패널의 세션까지.
function TerminateHosts(KeysOnly: Boolean): Integer;
var
  Locator, Wmi, Procs, P: Variant;
  I: Integer;
  Cmd: String;
begin
  Result := 0;
  try
    Locator := CreateOleObject('WbemScripting.SWbemLocator');
    Wmi := Locator.ConnectServer('.', 'root\CIMV2');
    Procs := Wmi.ExecQuery('SELECT ProcessId, CommandLine FROM Win32_Process WHERE Name = ''sill-host.exe''');
    for I := 0 to Procs.Count - 1 do
    begin
      P := Procs.ItemIndex(I);
      Cmd := '';
      if not VarIsNull(P.CommandLine) then Cmd := Trim(P.CommandLine);
      if (not KeysOnly) or (Copy(Cmd, Length(Cmd) - 4, 5) = ' keys') then
      begin
        Log('sill-host 끝냄: ' + Cmd);
        P.Terminate();
        Result := Result + 1;
      end;
    end;
  except
    Log('sill-host 끝내기 실패: ' + GetExceptionMessage);
  end;
end;

// 끝낸 프로세스가 사라질 때까지(최대 5 s) 기다린다. 옛 상주 프로세스가 남아 있으면 새 상주 프로세스가 뮤텍스를 보고 그냥 끝난다.
procedure StopHosts(KeysOnly: Boolean);
var
  I: Integer;
begin
  for I := 1 to 50 do
  begin
    if TerminateHosts(KeysOnly) = 0 then Exit;
    Sleep(100);
  end;
end;

// 쓰이는 파일(탐색기에 적재된 DLL, 열린 패널의 sill-host.exe)은 지울 수 없지만 이름은 바꿀 수 있다.
// 관리자가 아니면 재부팅 때 바꾸기(restartreplace)가 듣지 않는다(M5 설계 2.3절).
procedure MoveAside(const Name: String);
var
  F: String;
begin
  F := ExpandConstant('{app}\bin\') + Name;
  if FileExists(F) and not DeleteFile(F) then
    if RenameFile(F, F + '.' + GetDateTimeString('yyyymmddhhnnsszzz', #0, #0) + '.old') then
    begin
      Log('쓰이는 파일 이름 바꿈: ' + F);
      MovedAside := True;
    end
    else
      Log('이름 바꾸기 실패: ' + F);
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssInstall then
  begin
    // 지난 제거가 남긴 RunOnce가 다음 로그인 때 이 설치를 지우지 않게 한다
    RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\RunOnce', 'Sill');
    StopHosts(True);
    DelTree(ExpandConstant('{app}\bin\*.old'), False, True, False);
    MoveAside('Sill.Band.dll');
    MoveAside('sill-host.exe');
    MoveAside('Microsoft.Web.WebView2.Core.dll');
    MoveAside('WebView2Loader.dll');
  end;
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  if (CurPageID = wpFinished) and MovedAside then
  begin
    WizardForm.FinishedLabel.Caption := WizardForm.FinishedLabel.Caption + #13#10#13#10 +
      'Restart File Explorer or sign out to use the new version.';
    WizardForm.FinishedLabel.AdjustHeight;
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Cmd: String;
begin
  // 제거 확인 뒤, 파일을 지우기 전. 작업 개체 때문에 셸과 자식도 함께 끝난다.
  if CurUninstallStep = usUninstall then StopHosts(False);
  if CurUninstallStep <> usPostUninstall then Exit;
  Cmd := '';
  if DirExists(ExpandConstant('{app}')) then Cmd := 'rd /s /q "' + ExpandConstant('{app}') + '"';
  if DirExists(ExpandConstant('{localappdata}\Sill')) then
  begin
    if Cmd <> '' then Cmd := Cmd + ' & ';
    Cmd := Cmd + 'rd /s /q "' + ExpandConstant('{localappdata}\Sill') + '"';
  end;
  if Cmd <> '' then
  begin
    RegWriteStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\RunOnce', 'Sill', 'cmd /c ' + Cmd);
    Log('지우지 못한 폴더는 다음 로그인 때: ' + Cmd);
  end;
end;
