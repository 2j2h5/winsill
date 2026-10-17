# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## 진행 방식

milepost로 진행한다. 상태는 `milepost/state.json`에 있고, 프로포절·마일스톤 설계·결과 보고서는 `milepost/html/sill/`(PDF는 `milepost/pdf/sill/`)에 있다. 설계 결정의 근거는 이 보고서들과 `PROPOSAL.md`다.

## 명령

.NET SDK가 없다. Windows에 들어 있는 `csc.exe`(C# 5 문법: 문자열 보간·`?.`·`nameof` 사용 불가)로 빌드한다. 스크립트는 PowerShell 5.1에서 읽히도록 UTF-8 BOM으로 저장한다.

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1               # bin\ (Sill.Band.dll, sill-host.exe, WebView2 DLL, web\). 의존성은 deps\에 고정 버전으로 한 번 받음. 폴더 창 explorer와 sill-host를 먼저 종료
powershell -ExecutionPolicy Bypass -File .\tools\host-check.ps1 [-Shell powershell|pwsh|cmd|gitbash]  # 탐색기 없이 sill-host 단독 확인 (PASS/FAIL). 셸마다 돌린다
powershell -ExecutionPolicy Bypass -File .\tools\register.ps1      # HKCU에만 등록 (관리자 권한 불필요)
powershell -ExecutionPolicy Bypass -File .\tools\toggle.ps1 on|off # 열린 모든 탐색기 창(탭)에 ShowBrowserBar
powershell -ExecutionPolicy Bypass -File .\tools\unregister.ps1    # 등록 키 전부 삭제 (탐색기가 계속 죽을 때 탈출구)
```

- 로그: `%LOCALAPPDATA%\Sill\band.log`(band 호출, WebView2 키 이벤트, `이동 폴더`, `셸 폴더`), `host.log`(sill-host, `OSC 9;9`, `cd 보냄`/`c 건너뜀(이유)`/`c 보류`, `p 보냄`/`p 건너뜀`, `셸 시작 <셸>`, `안내: …`(설정 문제), `c 건너뜀(셸이 못 감)`, `입력 중 유지(프롬프트 다시 그림)`). WebView2 데이터 폴더는 `%LOCALAPPDATA%\Sill\WebView2`.
- 설정: `%LOCALAPPDATA%\Sill\settings.json`의 `{"shell": "..."}` 하나(powershell, pwsh, cmd, gitbash, 그 밖에는 명령줄 그대로). 없으면 pwsh가 PATH에 있으면 pwsh, 아니면 powershell. sill-host가 세션을 시작할 때마다 읽는다. 개발자의 PC에는 이 파일이 없다(시험 뒤 지울 것). 패널 높이는 탐색기가 끌기 직후 `Explorer Bars\{CLSID}\BarSize`(패널 높이 + 4 px)에 직접 저장한다(M4).
- 작업 표시줄 explorer가 예전 DLL을 잡고 있으면 build.ps1이 `bin\Sill.Band.*.old`로 이름을 바꿔 치운다.
- 개발 중에는 `HKCU\...\Explorer\Advanced\SeparateProcess=1`이 켜져 있다(원래 값 0). 새 폴더 창은 `explorer.exe /factory,...` 프로세스에서 열리므로 band가 죽어도 작업 표시줄은 살아남는다. 이 설정 전에 열려 있던 창은 작업 표시줄 프로세스에 있으니 시험에 쓰지 않는다.
- 자동화 테스트는 host-check.ps1 하나다(M1 입출력·정리 + M2 상태 판정 + M3 `p`·되먹임 목록 + M4 셸별 항목). sill-host의 두 번째 인수로 셸을 주므로 settings.json을 건드리지 않는다. 나머지 확인은 PowerShell `Shell.Application.Windows()`로 창을 잡아 `ShowBrowserBar`·`Navigate2`를 부르고, band.log·host.log와 `PrintWindow` 캡처로 한다.
- `Navigate2`에는 **존재하는 경로만** 넘긴다. 없는 경로면 탐색기가 개발자 화면에 "경로 또는 주소가 정확한지 확인" 오류 창을 띄우고 0x800704C7을 돌려준다(M2에서 겪음. 빈 폴더는 `Compress-Archive`가 zip에 넣지 않는 것도 원인이었다). `powershell -File`에 배열 인수를 넘기면 하나의 문자열로 합쳐지는 것도 주의한다. `Navigate2`의 새 탭 플래그(0x800)는 무시되므로, 탭이 필요한 시험은 개발자에게 + 버튼 클릭을 부탁한다.
- **패널에 SendInput으로 키를 보내는 시험은 포커스가 band 창의 자손인지 키마다 확인한 뒤에만 한다.** 탐색기 창 안 어딘가로만 확인하면 키가 파일 목록으로 가서 실제 파일 작업(복사·삭제·창 닫기)이 일어난다(M1에서 겪음). WebView2의 포커스 창은 msedgewebview2 스레드에 있으므로 패널 자손 창들의 스레드마다 `GetGUIThreadInfo`로 본다. 개발자가 다른 창을 쓰는 중이면 전경 잠금 때문에 시험 창을 앞으로 가져올 수 없으니, 패널 클릭을 부탁하고 기다린다. 클릭 뒤 시험이 끝날 때까지 다른 창으로 옮기지 말아 달라고도 부탁한다(M3에서 클릭 직후 개발자가 다른 창으로 가서 한 번 멈춤).
- 시험 스크립트의 `Navigate2`도 파일 목록에 포커스를 가져간다. 그 뒤 키를 보내려면, 시험 창이 전경이고 `WindowFromPoint`가 band(또는 자식) 창일 때만 패널 아래쪽을 마우스로 클릭해 포커스를 되찾는다(M4). 패널에서 `exit`로 셸을 바꿀 때는 band.log의 `sill-host 끊김`을 본 뒤에 다시 시작 키를 보낸다(먼저 보내면 끝나 가는 셸이 먹는다).
- PowerShell 시험 스크립트의 함수 이름이 기본 별칭(`h`=Get-History, `measure`=Measure-Object 등)과 겹치면 별칭이 이긴다. `Verb-Noun` 이름을 쓴다(M4에서 두 번 겪음).

## 프로젝트 개요

**Sill** (저장소 `2j2h5/winsill`): Windows 탐색기 창 하단에 Dolphin(KDE)식 터미널 패널을 붙이는 셸 확장이다. 탐색기를 대체하는 앱이 아니라 탐색기 **자체**를 확장한다.

## 아키텍처

```
[explorer.exe]
  └ Sill Band (C#, COM, Explorer Bar / CATID_CommBand)
      └ WebView2 + xterm.js     ← 렌더링·입력만
            ⇅ named pipe IPC
[sill-host.exe]  (별도 프로세스)
  └ ConPTY → pwsh / cmd / claude
```

- **탐색기 프로세스 안의 코드는 얇게 유지한다.** band 안에서 예외나 크래시가 나면 explorer.exe 전체가 같이 죽는다. 셸 실행과 무거운 작업은 반드시 `sill-host`에서 한다.
- Band는 `IDeskBand`, `IObjectWithSite`, `IInputObject`를 구현한다. 켜고 끄는 것은 `IWebBrowser2::ShowBrowserBar`로 한다.
- 키 입력은 WebView2(msedgewebview2.exe) 창이 받으므로 탐색기가 가로채지 않는다(M1에서 키 24개로 확인). `TranslateAcceleratorIO`는 로그만 남긴다.
- band ↔ sill-host는 메시지 모드 named pipe, UTF-8 문자열, 첫 글자가 종류(r 크기, i 입력, o 출력, c 탐색기 폴더, p 셸 폴더; band가 직접 처리하는 것은 p, x 끊김, n 다시 시작). band는 셸 출력을 해석하지 않는다(`src/Shared.cs`).
- 세션은 band 수명(SetSite → CloseDW)에 묶이고 탭마다 하나다. sill-host는 자신을 KILL_ON_JOB_CLOSE 작업 개체에 넣어, 어떻게 끝나든 셸과 자식이 함께 종료된다.
- 스택: C# / .NET Framework 4.8 (COM interop), WebView2, xterm.js, ConPTY. 필요하면 band만 C++로 옮긴다.

## 폴더 동기화 규칙

- 탐색기 → 터미널: `NavigateComplete2`가 오면 `cd`를 보낸다. 단, **셸이 프롬프트 대기 상태일 때만**(OSC 133으로 판단) 보낸다. Claude Code 같은 프로그램이 실행 중이면 절대 입력을 주입하지 않는다.
  - band는 `Document.Folder.Self.Path`를 읽어 `c경로`만 보낸다(`::`로 시작하는 가상 폴더는 안 보냄). 판정은 `src/Sill.Host/Sync.cs`: 시작 전이면 시작 폴더, 대기면 `cd -LiteralPath`, 입력 중이면 건너뜀(줄을 지우지 않음), 실행 중이면 건너뜀(Sill의 cd가 도는 중이면 마지막 경로를 보류).
  - 셸 통합(`src/Sill.Host/Shells.cs`의 셸 표): 모든 셸이 프롬프트마다 OSC 133;A, 9;9(Windows 경로), 133;B를 내므로 `Sync`는 셸을 모른다. 셸마다 다른 것은 명령줄과 Sill이 넣는 `cd`뿐이다. PowerShell은 `-NoExit -EncodedCommand`로 프로필 적재 뒤 `prompt`를 감싼다(실행 정책에 안 걸림). cmd는 `/k prompt …`(AutoRun 뒤에 적용, 사용자 `PROMPT`는 가운데에 살림), `cd /d "…"`에서 `%`는 `%""`로(대화형 cmd는 따옴표 안에서도 `%이름%`을 푼다), UNC는 cd 불가라 건너뜀. Git Bash는 `--rcfile %LOCALAPPDATA%\Sill\sill.bash -i`(파일 안에서 `/etc/profile`·`~/.bash_profile`을 직접 읽음, `cygpath -w`로 9;9, 종료 문자는 BEL), `cd -- '…'`.
  - 입력 중(Typing)은 Enter나 Ctrl+C까지 유지한다. Ctrl+L은 입력 줄을 둔 채 프롬프트를 다시 그리므로(PSReadLine은 prompt를 다시 부르고 bash는 PS1만 다시 냄) 133;B로 대기가 되면 사용자가 치던 줄에 cd가 끼어든다(M4).
- 터미널 → 탐색기: sill-host가 OSC 9;9의 폴더가 바뀌었고, 첫 프롬프트가 아니고, Sill이 넣은 cd의 결과가 아니고, 폴더가 있을 때만 `p경로`를 보낸다. band는 `IShellBrowser::BrowseObject(SBSP_SAMEBROWSER | SBSP_ACTIVATE_NOFOCUS)`로 이동만 한다. `Navigate2`는 파일 목록에 포커스를 가져가서 패널에 이어 치는 키가 파일 목록으로 가므로 쓰지 않는다(M3).
  - 되먹임: `p`로 보낸 경로는 sill-host의 되먹임 목록(FIFO)에 들어가고, 탐색기가 따라온 뒤 돌려주는 `c`는 목록과 맞춰 버린다(`c 건너뜀(셸이 옮김)`, Dolphin의 `m_sendCdToTerminalHistory`와 같음).
  - OSC 7은 읽지 않는다. Sill이 넣는 셸 통합이 Windows 경로로 OSC 9;9를 낸다(M4 설계 리뷰). WSL은 시험할 수 없어 M4에서 뺐다(`"shell": "wsl.exe"`는 시작 폴더만 맞음).

## 제약

- 설치·제거는 관리자 권한 없이 HKCU 등록만으로 한다.
- Windows 10 이상만 지원한다. 현재 범위 밖: SSH, 분할 터미널, 다중 탭.
- 마일스톤은 M0(빈 band를 Win11 탐색기에 표시)부터 M5(배포)까지 순서대로 진행한다. M0이 실패하면 터미널 창 도킹 방식으로 방향을 바꾼다. 자세한 내용은 `PROPOSAL.md` 4–5절에 있다.
