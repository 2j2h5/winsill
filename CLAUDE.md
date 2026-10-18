# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## 명령

.NET SDK가 없다. Windows에 들어 있는 `csc.exe`(C# 5 문법: 문자열 보간·`?.`·`nameof` 사용 불가)로 빌드한다. 스크립트는 PowerShell 5.1에서 읽히도록 UTF-8 BOM으로 저장한다.

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1               # bin\ (Sill.Band.dll, sill-host.exe, WebView2 DLL, web\). 의존성은 deps\에 고정 버전으로 한 번 받음(받을 때 SHA-256 확인, 다르면 멈춤). 먼저 단위 시험(Sync, Session)을 돌리고(실패하면 멈춤), 폴더 창 explorer와 sill-host(상주 프로세스 포함)를 먼저 종료하고, 끝에 상주 프로세스를 다시 띄움
powershell -ExecutionPolicy Bypass -File .\tools\host-check.ps1 [-Shell powershell|pwsh|cmd|gitbash]  # 탐색기 없이 sill-host 단독 확인 (PASS/FAIL). 셸마다 돌린다
powershell -ExecutionPolicy Bypass -File .\tools\register.ps1      # HKCU에만 등록 (관리자 권한 불필요): band, Run의 상주 프로세스, 폴더 빈 곳 오른쪽 클릭 메뉴 "Open in Sill"
powershell -ExecutionPolicy Bypass -File .\tools\toggle.ps1 on|off # 열린 모든 탐색기 창(탭)에 ShowBrowserBar
powershell -ExecutionPolicy Bypass -File .\tools\unregister.ps1    # 등록 키 전부 삭제와 상주 프로세스 종료 (탐색기가 계속 죽을 때 탈출구)
powershell -ExecutionPolicy Bypass -File .\tools\package.ps1       # dist\Sill-Setup-<버전>.exe (installer\sill.iss, Inno Setup 7.1.0을 deps\에 휴대용으로 받음). bin\을 먼저 빌드
```

- 사용자 기능(M6): `Ctrl+``(패널 열기·포커스·닫기)와 `Ctrl+Shift+``(새 터미널, M9)는 상주 프로세스 `sill-host.exe keys`(HKCU `Run`, 뮤텍스로 하나)가 탐색기 창이 전경일 때만 `RegisterHotKey`로 받는다. 오른쪽 클릭 메뉴는 `sill-host.exe open "%V"`(전경이 탐색기 창이 아니면, 예를 들어 바탕 화면이면, 그 폴더로 탐색기를 열고 `Shell.Application.Windows()`에서 그 경로를 보는 새 항목을 찾아 패널을 켬, M8). 둘 다 활성 탭의 band 창(제목 `Sill`, 패널이 닫혀 있어도 숨은 채 남음)에 `Sill.Key` 창 메시지(wParam 0 토글, 1 새 터미널, 2 포커스)를 보내고, 닫힌 패널은 band가 스스로 연다(새 터미널이면 터미널을 먼저 만들고 엶). band 창이 없을 때만(그 탭에서 처음) 상주 프로세스가 `ShowBrowserBar`로 연다(M9). 페이지는 `Ctrl+,`(settings.json을 메모장으로), `Ctrl+PageUp/PageDown`(이전·다음 터미널, 끝에서 돌아감), VS Code의 스크롤 키를 처리한다. 사용자에게 보이는 문구는 영어, 로그는 한국어.
- 패널 모양(M7): 탐색기의 왼쪽 띠 머리(×, 세로 제목)는 없애고, 페이지가 Windows Terminal 같은 탭 줄을 그린다. M9부터 높이 28 px, 탭이 줄 높이를 다 채우고(폭 200 px에서 72 px까지 줄어듦) 제목·탭 ×·+·패널 ×는 모두 줄 가운데. 터미널마다 탭과 탭 ×(20 px, 모든 탭에 보임, 가운데 클릭도 닫음), 탭 바로 뒤 +(32 px), 오른쪽 끝에 패널 닫기 ×(32 px). 선택되지 않은 탭끼리, 그리고 선택되지 않은 마지막 탭과 + 사이는 높이 14 px의 세로선으로 나눈다(선택된 탭 옆에는 없음). 탭 제목은 프로그램이 보낸 창 제목(OSC 0·2, xterm.js `onTitleChange`), 시작 제목은 sill-host가 `STARTUPINFO.lpTitle`로 준 셸 이름(`powershell`·`pwsh`·`cmd`·`bash`, 그 밖은 명령줄 첫 낱말). 색은 Windows 앱 모드(`AppsUseLightTheme`)를 따른다: 페이지는 `prefers-color-scheme`, band는 레지스트리(바뀌면 페이지가 `t`로 알림). 탐색기 실측 색: 다크 내용 #191919·탭 줄/틀 #202020·분할선 선 #2B2B2B, 라이트 #FFFFFF·#E8E8E8·#F7F7F7. xterm.js `minimumContrastRatio` 4.5. "Open in Sill" 아이콘은 `assets/sill.ico`(큰 크기는 개발자의 `sill.svg`를 Edge 헤드리스로 그린 것, 16·20 px는 SVG를 줄이면 `>_`가 뭉개져 개발자가 고른 픽셀 시안)를 `sill-host.exe`에 넣고 메뉴 키 `Icon`이 가리킨다.
- 로그: `%LOCALAPPDATA%\Sill\band.log`(band 호출, `이동 폴더`, `셸 폴더`, `Sill.Key n 보임=… 포커스=…`, `터미널 n 만듦(이유)`/`닫음(탭 × | exit)`/`다시 시작`, `터미널 n 셸 끝남 code=`, `p 건너뜀(뒤 터미널 n)`, `WebView2 다시 만들기`, `사이트 스타일`, `BaseBar … 서브클래스`, `앱 모드`, `닫기 버튼`, `띠 머리 남음`(BSIS_NOCAPTION이 안 들었을 때), `WebView2 안내`/`WebView2 다시 시도`(WebView2를 만들지 못함), `링크 열기 <호스트>`/`링크 거부`, `새 창 막음`/`이동 막음`, `sill-host 실행 실패`, `보내기 큐 넘침, 세션 끊음`), `host.log`(sill-host, `OSC 9;9`, `cd 보냄`/`cd 보냄(키)`/`c 건너뜀(이유)`/`c 보류`, `p 보냄`/`p 건너뜀`, `셸 시작 <셸>`, `셸 끝남 code=`, `안내: …`(설정 문제), `c 건너뜀(셸이 못 감)`, `입력 중 유지(프롬프트 다시 그림)`, `133;A 없는 133;B 무시`, `133 무시(난수 없음)`·`9;9 무시(프롬프트 밖)`(세션마다 처음 한 번), `다시 그리기`, `설정 파일 만듦`, `시작 폴더 아님`), `keys.log`(상주 프로세스, `단축키 등록/해제`, `패널 열기`, `Sill.Key`, `메뉴`, `새 창에 패널 열기`). WebView2 데이터 폴더는 `%LOCALAPPDATA%\Sill\WebView2`. 로그에는 날짜가 없으니 시험에서는 줄 수로 표시해 두고 그 뒤를 읽는다.
- 설정: `%LOCALAPPDATA%\Sill\settings.json`의 `{"shell": "..."}` 하나(powershell, pwsh, cmd, gitbash, 그 밖에는 명령줄 그대로). 없으면 pwsh가 PATH에 있으면 pwsh, 아니면 powershell. sill-host가 세션을 시작할 때마다 읽는다. 파일이 없을 때 `Ctrl+,`는 `{"shell": ""}`(빈 값 = 자동 선택)로 만든다(M8). 개발자의 PC에는 이 파일이 없다(시험 뒤 지울 것). 패널 높이는 탐색기가 끌기 직후 `Explorer Bars\{CLSID}\BarSize`(패널 높이 + 4 px)에 직접 저장한다(M4).
- 배포(M5): 사용자는 Inno Setup 설치 프로그램으로 `%LOCALAPPDATA%\Programs\Sill`에 관리자 없이 설치한다. **등록 키는 `tools\register.ps1`(개발)과 `installer\sill.iss`(설치본) 두 곳에 있으므로 한쪽을 바꾸면 다른 쪽도 바꾼다.** 설치 프로그램은 쓰이는 파일(탐색기의 DLL, 열린 패널의 sill-host.exe)을 `*.old`로 이름 바꿔 비키고(관리자가 아니면 `restartreplace`가 듣지 않음), 제거 때 지우지 못한 폴더는 HKCU `RunOnce`로 다음 로그인 때 지운다. 로그는 1 MB를 넘으면 `.old`로 돌린다.
- 작업 표시줄 explorer가 예전 DLL을 잡고 있으면 build.ps1이 `bin\Sill.Band.*.old`로 이름을 바꿔 치운다.
- Claude Code를 Sill 패널 안에서 돌리면 build.ps1이 sill-host를 끝낼 때 그 세션도 함께 끝난다(0.1.1 M1에서 겪음). 먼저 부모 프로세스에 sill-host가 있는지 본다. sill-host만 바뀌었으면 단위 시험을 돌린 뒤 쓰이는 `bin\sill-host.exe`를 `.old`로 비키고 그것만 빌드한다(상주 프로세스는 다시 띄움).
- 개발 중에는 `HKCU\...\Explorer\Advanced\SeparateProcess=1`이 켜져 있다(원래 값 0). 새 폴더 창은 `explorer.exe /factory,...` 프로세스에서 열리므로 band가 죽어도 작업 표시줄은 살아남는다. 이 설정 전에 열려 있던 창은 작업 표시줄 프로세스에 있으니 시험에 쓰지 않는다.
- 자동화 테스트는 셋이다. `tests\SyncTest.cs`는 Sync 단위 시험(난수 확인, 메시지 경계에서 잘린 프롬프트, Ctrl+L, 되먹임, bash 다시 그리기, 보류)으로 build.ps1이 bin을 건드리기 전에 `%TEMP%`에 빌드해 돌리고, 실패하면 빌드가 멈춘다. Shared.cs 대신 빈 `Shared.Log`를 두어 host.log에 쓰지 않는다. `tests\SessionTest.cs`는 band의 Session 단위 시험(sill-host가 읽지 않아도 Send가 막히지 않음, 순서, 큐 상한, 막힌 동안 닫기, sill-host를 띄우지 못함)으로 SyncTest 다음에 돈다. 자기 자신을 가짜 sill-host로 띄우고, `SILL_TEST`로 빌드해 band.log에 쓰지 않는다. host-check.ps1은 sill-host 통합 시험이다(M1 입출력·정리 + M2 상태 판정 + M3 `p`·되먹임 목록 + M4 셸별 항목 + M6 cd 숨김·같은 크기 `r` 다시 그리기·종료 줄 + M7 같은 크기 `r` 뒤 제목 + M8 출력 묶기·되울림 지연·파이프 ACL·PS1을 매번 쓰는 `.bashrc`(Git Bash 시험 HOME)·낡은 되먹임 항목·끊긴 네트워크 경로(`\\192.0.2.x\x`, 약 21 s) 뒤 입력·끊긴 시작 폴더 + M9 종료 줄 뒤 `e0` + M10 시작 제목 0.3 s 이하·셸을 띄우는 동안 band 쪽 쓰기 10 ms 이하 + 0.1.1 셸 환경에 난수 없음·위조 시퀀스를 담은 파일을 출력하는 동안 cd·p 없음). host-check는 첫 프롬프트에서 난수를 읽어 그 난수가 붙은 A·B만 센다. sill-host의 두 번째 인수로 셸을 주므로 settings.json을 건드리지 않는다. 나머지 확인은 PowerShell `Shell.Application.Windows()`로 창을 잡아 `ShowBrowserBar`·`Navigate2`를 부르고, band.log·host.log와 `PrintWindow` 캡처로 한다.
- CI(`.github/workflows/ci.yml`): main으로 가는 PR과 main push마다 windows-2025 러너에서 build.ps1(단위 시험 둘 포함), host-check 셸 넷, package.ps1을 돌리고, 실패하면 `%LOCALAPPDATA%\Sill\*.log`를 `logs` 아티팩트로 올린다. 러너는 영어 Windows(OEM 437)다. 액션은 커밋 해시로 고정한다.
- 버전은 `src/Shared.cs`의 `Shared.Version` 하나다. exe·DLL의 파일·제품 버전이 되고, `installer\sill.iss`가 빌드된 sill-host.exe에서 읽고, 로그의 `시작`(host.log, keys.log)·`SetSite`(band.log) 줄에 붙는다. `AssemblyVersion`은 0.0.0.0으로 둔다(등록 키의 `Assembly` 값).
- 탐색기를 띄우는 시험(창, 키 입력, 캡처) 전에 `docs/testing-notes.md`를 읽는다. 어기면 개발자의 파일과 창을 건드리는 규칙 둘: **패널에 SendInput으로 키를 보내기 전에 포커스가 band 창의 엄격한 자손(WebView2 창)인지 키마다 확인한다**(아니면 키가 파일 목록으로 가서 복사·삭제가 일어남, M1). **`Navigate2`에는 존재하는 경로만 넘긴다**(없으면 개발자 화면에 오류 창, M2).
- band는 탐색 때 얻은 COM 개체(Document·Folder·Self)와 닫을 때 자기가 얻은 브라우저·연결점을 `Marshal.ReleaseComObject`로 놓는다. 다른 .NET 셸 확장과 RCW를 함께 쓰므로 **얻은 만큼만** 놓고, 받은 개체(이벤트 인자)는 놓지 않는다. 더 놓으면 다른 쪽에 `InvalidComObjectException`이 난다(M11 사전 확인).

## 프로젝트 개요

**Sill** (저장소 `2j2h5/winsill`): Windows 탐색기 창 하단에 Dolphin(KDE)식 터미널 패널을 붙이는 셸 확장이다. 탐색기를 대체하는 앱이 아니라 탐색기 **자체**를 확장한다.

## 아키텍처

```
[explorer.exe]
  └ Sill Band (C#, COM, Explorer Bar / CATID_CommBand)
      └ WebView2 + xterm.js     ← 렌더링·입력만
            ⇅ named pipe IPC
[sill-host.exe]  (별도 프로세스, 터미널마다 하나)
  └ ConPTY → pwsh / cmd / claude
[sill-host.exe keys]  (상주 프로세스 하나, 단축키) ── Sill.Key 창 메시지 / ShowBrowserBar ──▶ band
```

- **탐색기 프로세스 안의 코드는 얇게 유지한다.** band 안에서 예외나 크래시가 나면 explorer.exe 전체가 같이 죽는다. 셸 실행과 무거운 작업은 반드시 `sill-host`에서 한다.
- Band는 `IDeskBand`, `IObjectWithSite`, `IInputObject`를 구현한다. 켜고 끄는 것은 `IWebBrowser2::ShowBrowserBar`로 한다.
- 키 입력은 WebView2(msedgewebview2.exe) 창이 받으므로 탐색기가 가로채지 않는다(M1에서 키 24개로 확인). `TranslateAcceleratorIO`는 아무것도 하지 않는다(키 로그는 M8에서 지움).
- band ↔ sill-host는 메시지 모드 named pipe, UTF-8 문자열, 첫 글자가 종류(r 크기, i 입력, o 출력, c 탐색기 폴더, p 셸 폴더, s 설정 파일, e 셸의 종료 코드; band가 직접 처리하는 것은 p, s(메모장은 band가 띄움: sill-host의 자식은 작업 개체와 함께 끝남), e(0이면 그 터미널을 닫음), x 끊김, n 다시 시작, h 탭 줄의 패널 닫기, t 앱 모드 바뀜, l 페이지가 뜸(band가 터미널 목록 `+n`·활성 `an`을 보냄), + 새 터미널, a 활성 터미널, - 터미널 닫기, u 링크 열기). 페이지와 band 사이에서 한 터미널의 메시지는 종류 글자 뒤에 band가 붙인 번호와 빈칸이 온다(`i2 dir`, `o2 …`). band가 번호를 떼고 붙이며, sill-host 쪽 형식은 그대로다(M9). band는 셸 출력을 해석하지 않는다(`src/Shared.cs`). 셸이 있는데 같은 크기의 r이 오면(WebView2를 다시 만든 새 페이지) sill-host가 ConPTY 크기를 한 칸 바꿨다 되돌려 화면을 다시 받는다. 그래서 페이지는 같은 크기를 두 번 보내지 않는다.
- sill-host는 셸을 띄우자마자 셸 이름을 OSC 0으로 보낸다. ConPTY는 셸이 첫 화면을 그릴 때에야 `lpTitle`을 보내서 그동안 탭 제목이 비었다(powershell 1.9 s, M10). 파이프 서버 버퍼는 64 KB다. 0이면 sill-host가 읽지 않는 동안(셸을 띄우는 동안, 끊긴 시작 폴더면 약 2 s) band의 쓰기, 곧 탐색기 UI 스레드가 막혔다(M10). band의 Session은 연결을 기다리다 닫혔으면 연결 직후 끝낸다(안 그러면 읽기에서 영원히 기다리고 sill-host가 남음). band는 파이프에 UI 스레드에서 쓰지 않는다. `Send`는 큐에 넣고 돌아오고 터미널마다 쓰기 스레드가 쓴다(0.1.1 M2). sill-host의 파이프 읽기 스레드는 큰 입력을 셸에 넣는 동안(4 MB에 약 5 s) 읽지 않으므로, 동기로 쓰면 큰 붙여넣기가 이어질 때 탐색기 창이 멈췄다. 쌓인 것이 1,600만 글자를 넘으면 파이프를 닫는다(탭에 다시 시작 안내). sill-host를 띄우지 못하면 Session이 첫 `r`에 안내 줄과 `x`를 돌려준다. 로그(`Shared.Log`)는 `FileSystemRights.AppendData`와 `FileShare.ReadWrite | Delete`로 연다. `File.AppendAllText`는 여러 프로세스가 동시에 쓰면 공유 위반으로 줄을 잃었다(4개 × 500줄에 739줄, M10).
- sill-host는 셸 출력을 16 ms에 한 번까지 o 하나로 묶어 보낸다(조용하던 셸의 첫 출력은 바로). 조각마다 보내면 대량 출력 중 탐색기 CPU가 한 코어의 57 %였다(M8). p·종료 줄 앞에서는 버퍼를 먼저 비워 순서를 지킨다. 셸이 시작된 뒤의 c는 폴더 작업 스레드가 받은 순서대로 처리한다(끊긴 네트워크 경로의 `Directory.Exists`가 약 21 s 걸려 입력을 막았음). 셸 시작 전의 파이프 읽기 스레드에서 하는 확인은 시간을 제한한다: 시작 폴더는 2 s 안에 답이 없으면 홈, PATH 검색(`OnPath`)은 네트워크 폴더를 건너뜀(M8 후속). `Pty.Write`는 세 스레드(파이프 읽기, 폴더 작업, Pump)가 부르므로 잠근다. 파이프 DACL은 현재 사용자만.
- WebView2의 브라우저·렌더러 프로세스가 죽으면 band가 컨트롤러를 다시 만든다(10초 안 재실패면 멈춤). 호스트 이름은 `sill.example`이다(`.local`은 이름 풀이로 첫 적재가 2초 늦었다, M6). OSC 8 링크는 Ctrl+클릭하면 페이지의 `linkHandler`가 `u주소`를 보내고 band가 http·https만 `Process.Start`로 기본 브라우저에서 연다(핸들러가 없으면 xterm.js가 확인 창 뒤 주소창 없는 WebView2 팝업을 열었다). `NewWindowRequested`는 늘 막고 `NavigationStarting`은 `https://sill.example/`만 허락한다(0.1.1 M2).
- 세션은 band 수명(SetSite → CloseDW)에 묶이고 터미널마다 하나다(M9). band가 세션 목록(번호 순 = 탭 순)과 활성 번호를 갖는다. 첫 터미널은 SetSite에서, 그 뒤는 +·`Ctrl+Shift+``·터미널이 없는 패널이 보일 때(ShowDW) 만든다. 셸이 코드 0으로 끝나면(exit) 그 탭을 닫고, 0이 아니거나 sill-host가 끊기면 탭에 종료 줄과 "Press any key to restart."를 남긴다. 마지막 탭이 닫히면 패널도 닫는다. 숨은 터미널은 `visibility: hidden`이라 출력을 계속 그린다. 패널 크기가 바뀌면 보이는 터미널만 맞추고, 숨은 터미널은 보이게 될 때 맞춘다(M10, 끄는 동안 모든 sill-host에 크기 변경이 몰리던 것: 터미널 5개 20번에 71 → 20번). sill-host는 자신을 KILL_ON_JOB_CLOSE 작업 개체에 넣어, 어떻게 끝나든 셸과 자식이 함께 종료된다.
- 스택: C# / .NET Framework 4.8 (COM interop), WebView2, xterm.js, ConPTY. 필요하면 band만 C++로 옮긴다.

## 폴더 동기화 규칙

- 여러 터미널이면 폴더 동기화는 활성 터미널과만 한다(M9): c는 활성 터미널에만 보내고, 뒤 터미널의 p는 band가 버린다. 탭을 바꿔도 아무것도 옮기지 않는다. 새 터미널은 탐색기의 지금 폴더에서 시작한다.
- 탐색기 → 터미널: `NavigateComplete2`가 오면 `cd`를 보낸다. 단, **셸이 프롬프트 대기 상태일 때만**(OSC 133으로 판단) 보낸다. Claude Code 같은 프로그램이 실행 중이면 절대 입력을 주입하지 않는다.
  - band는 `Document.Folder.Self.Path`를 읽어 `c경로`만 보낸다(`::`로 시작하는 가상 폴더는 안 보냄). 판정은 `src/Sill.Host/Sync.cs`: 시작 전이면 시작 폴더, 대기면 cd, 입력 중이면 건너뜀(줄을 지우지 않음), 실행 중이면 건너뜀(Sill의 cd가 도는 중이면 마지막 경로를 보류). 건너뛴 이동은 나중에 맞추지 않는다(Dolphin과 같음, M6).
  - cd 숨김(M6): sill-host가 `SILL_CD` 환경 변수의 파일(`%LOCALAPPDATA%\Sill\cd-<pid>.txt`)에 경로를 쓰고 `ESC [24;8~`(Ctrl+Alt+Shift+F12)를 보낸다. PowerShell은 PSReadLine 키 처리기가 `Set-Location` 뒤 `InvokePrompt`(화면·기록에 남지 않음), Git Bash는 `bind -x` 함수 뒤 빈 Enter(빈 프롬프트가 남음). 셸 통합이 키 처리기를 등록했을 때만 그 파일을 만들고, 파일이 없으면(cmd) cd 한 줄을 입력한다.
  - 실행 중에서 대기로 가는 것은 133;A를 본 뒤의 133;B뿐이다. bash는 `bind -x` 함수 뒤 줄을 다시 그리며 133;B만 낸다(M6).
  - 출처 확인(0.1.1): 셸에서 돈 프로그램도 출력에 같은 시퀀스를 섞을 수 있다(악성 파일을 cat, ssh·wsl 안의 셸). sill-host가 세션마다 128비트 난수를 만들어 환경 변수 `SILL_NONCE`로 건네고, 셸 통합은 값을 셸 안으로 옮긴 뒤 환경 변수를 지운다(VS Code의 633 nonce와 같은 방식, 셸이 띄우는 프로그램은 모름). 셸 통합의 A·B는 `133;A;sill=<난수>`·`133;B;sill=<난수>`이고, `Sync`는 이것과 **진짜 A 바로 다음 OSC인 9;9**(창 제목 OSC는 세지 않음)만 믿는다. A와 B 사이에는 사용자 프롬프트가 그려지므로 그 안의 9;9는 믿지 않는다. 버린 시퀀스는 존재 확인(UNC면 그 서버에 접속)도 하지 않는다. 난수 없는 133은 페이지의 Ctrl+Up 표식에만 쓰인다. cmd는 `%SILL_NONCE%`를 prompt에 푼 뒤 `set "SILL_NONCE="`로 지우지만 `PROMPT` 환경 변수에 `$e` 꼴로 남는다(ESC가 아니라 출력해도 시퀀스가 안 됨). PowerShell 프로필은 셸 통합보다 먼저 돌므로 프로필이 띄운 프로세스는 난수를 물려받는다.
  - 셸 통합(`src/Sill.Host/Shells.cs`의 셸 표): 모든 셸이 프롬프트마다 OSC 133;A, 9;9(Windows 경로), 133;B를 내므로 `Sync`는 셸을 모른다. 셸마다 다른 것은 명령줄과 Sill이 넣는 `cd`뿐이다. PowerShell은 `-NoExit -EncodedCommand`로 프로필 적재 뒤 `prompt`를 감싼다(실행 정책에 안 걸림). cmd는 `/k prompt …`(AutoRun 뒤에 적용, 사용자 `PROMPT`는 가운데에 살림), `cd /d "…"`에서 `%`는 `%""`로(대화형 cmd는 따옴표 안에서도 `%이름%`을 푼다), UNC는 cd 불가라 건너뜀. Git Bash는 `--rcfile %LOCALAPPDATA%\Sill\sill.bash -i`(파일 안에서 `/etc/profile`·`~/.bash_profile`을 직접 읽음, `cygpath -w`로 9;9, 종료 문자는 BEL, PROMPT_COMMAND 배열 끝의 `__sill_b`가 PS1에서 지워진 133;B를 다시 붙임(starship처럼 PS1을 매번 쓰는 설정, bash 5.1+, M8)), `cd -- '…'`.
  - 입력 중(Typing)은 Enter나 Ctrl+C까지 유지한다. Ctrl+L은 입력 줄을 둔 채 프롬프트를 다시 그리므로(PSReadLine은 prompt를 다시 부르고 bash는 PS1만 다시 냄) 133;B로 대기가 되면 사용자가 치던 줄에 cd가 끼어든다(M4).
- 터미널 → 탐색기: sill-host가 OSC 9;9의 폴더가 바뀌었고, 첫 프롬프트가 아니고, Sill이 넣은 cd의 결과가 아니고, 폴더가 있을 때만 `p경로`를 보낸다. band는 `IShellBrowser::BrowseObject(SBSP_SAMEBROWSER | SBSP_ACTIVATE_NOFOCUS)`로 이동만 한다. `Navigate2`는 파일 목록에 포커스를 가져가서 패널에 이어 치는 키가 파일 목록으로 가므로 쓰지 않는다(M3).
  - 되먹임: `p`로 보낸 경로는 sill-host의 되먹임 목록(FIFO)에 들어가고, 탐색기가 따라온 뒤 돌려주는 `c`는 목록과 맞춰 버린다(`c 건너뜀(셸이 옮김)`, Dolphin의 `m_sendCdToTerminalHistory`와 같음). 탐색기가 따라오지 못한 항목이 남지 않도록 2초 지난 항목은 맞춰 보지 않는다(M8).
  - OSC 7은 읽지 않는다. Sill이 넣는 셸 통합이 Windows 경로로 OSC 9;9를 낸다(M4). WSL은 시험할 수 없어 M4에서 뺐다(`"shell": "wsl.exe"`는 시작 폴더만 맞음).

## 제약

- 설치·제거는 관리자 권한 없이 HKCU 등록만으로 한다.
- Windows 10 이상만 지원한다. 현재 범위 밖: SSH, 분할 터미널. 한 탭에 여러 터미널은 M9에서 했다(`Ctrl+Shift+``는 VS Code처럼 "새 터미널"). 언어는 영어 하나로 둔다(언어 패치는 하지 않기로 함).
