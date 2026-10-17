# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## 진행 방식

milepost로 진행한다. 상태는 `docs/reports/state.json`에 있고, 프로포절·마일스톤 설계·결과 보고서는 `docs/reports/sill/`에 있다. 설계 결정의 근거는 이 보고서들과 `PROPOSAL.md`다.

## 명령

.NET SDK가 없다. Windows에 들어 있는 `csc.exe`(C# 5 문법: 문자열 보간·`?.`·`nameof` 사용 불가)로 빌드한다. 스크립트는 PowerShell 5.1에서 읽히도록 UTF-8 BOM으로 저장한다.

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1               # bin\ (Sill.Band.dll, sill-host.exe, WebView2 DLL, web\). 의존성은 deps\에 고정 버전으로 한 번 받음. 폴더 창 explorer와 sill-host를 먼저 종료
powershell -ExecutionPolicy Bypass -File .\tools\host-check.ps1    # 탐색기 없이 sill-host 단독 확인 (PASS/FAIL)
powershell -ExecutionPolicy Bypass -File .\tools\register.ps1      # HKCU에만 등록 (관리자 권한 불필요)
powershell -ExecutionPolicy Bypass -File .\tools\toggle.ps1 on|off # 열린 모든 탐색기 창(탭)에 ShowBrowserBar
powershell -ExecutionPolicy Bypass -File .\tools\unregister.ps1    # 등록 키 전부 삭제 (탐색기가 계속 죽을 때 탈출구)
```

- 로그: `%LOCALAPPDATA%\Sill\band.log`(band 호출, WebView2 키 이벤트, `이동 폴더`), `host.log`(sill-host, `OSC 9;9`, `cd 보냄`/`c 건너뜀(이유)`/`c 보류`). WebView2 데이터 폴더는 `%LOCALAPPDATA%\Sill\WebView2`.
- 작업 표시줄 explorer가 예전 DLL을 잡고 있으면 build.ps1이 `bin\Sill.Band.*.old`로 이름을 바꿔 치운다.
- 개발 중에는 `HKCU\...\Explorer\Advanced\SeparateProcess=1`이 켜져 있다(원래 값 0). 새 폴더 창은 `explorer.exe /factory,...` 프로세스에서 열리므로 band가 죽어도 작업 표시줄은 살아남는다. 이 설정 전에 열려 있던 창은 작업 표시줄 프로세스에 있으니 시험에 쓰지 않는다.
- 자동화 테스트는 host-check.ps1 하나다(M1 입출력·정리 + M2 상태 판정). 나머지 확인은 PowerShell `Shell.Application.Windows()`로 창을 잡아 `ShowBrowserBar`·`Navigate2`를 부르고, band.log·host.log와 `PrintWindow` 캡처로 한다.
- `Navigate2`에는 **존재하는 경로만** 넘긴다. 없는 경로면 탐색기가 개발자 화면에 "경로 또는 주소가 정확한지 확인" 오류 창을 띄우고 0x800704C7을 돌려준다(M2에서 겪음. 빈 폴더는 `Compress-Archive`가 zip에 넣지 않는 것도 원인이었다). `powershell -File`에 배열 인수를 넘기면 하나의 문자열로 합쳐지는 것도 주의한다. `Navigate2`의 새 탭 플래그(0x800)는 무시되므로, 탭이 필요한 시험은 개발자에게 + 버튼 클릭을 부탁한다.
- **패널에 SendInput으로 키를 보내는 시험은 포커스가 band 창의 자손인지 키마다 확인한 뒤에만 한다.** 탐색기 창 안 어딘가로만 확인하면 키가 파일 목록으로 가서 실제 파일 작업(복사·삭제·창 닫기)이 일어난다(M1에서 겪음). WebView2의 포커스 창은 msedgewebview2 스레드에 있으므로 패널 자손 창들의 스레드마다 `GetGUIThreadInfo`로 본다. 개발자가 다른 창을 쓰는 중이면 전경 잠금 때문에 시험 창을 앞으로 가져올 수 없으니, 패널 클릭을 부탁하고 기다린다.

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
- band ↔ sill-host는 메시지 모드 named pipe, UTF-8 문자열, 첫 글자가 종류(r 크기, i 입력, o 출력, c 탐색기 폴더; band가 직접 처리하는 것은 x 끊김, n 다시 시작). band는 셸 출력을 해석하지 않는다(`src/Shared.cs`).
- 세션은 band 수명(SetSite → CloseDW)에 묶이고 탭마다 하나다. sill-host는 자신을 KILL_ON_JOB_CLOSE 작업 개체에 넣어, 어떻게 끝나든 셸과 자식이 함께 종료된다.
- 스택: C# / .NET Framework 4.8 (COM interop), WebView2, xterm.js, ConPTY. 필요하면 band만 C++로 옮긴다.

## 폴더 동기화 규칙

- 탐색기 → 터미널: `NavigateComplete2`가 오면 `cd`를 보낸다. 단, **셸이 프롬프트 대기 상태일 때만**(OSC 133으로 판단) 보낸다. Claude Code 같은 프로그램이 실행 중이면 절대 입력을 주입하지 않는다.
  - band는 `Document.Folder.Self.Path`를 읽어 `c경로`만 보낸다(`::`로 시작하는 가상 폴더는 안 보냄). 판정은 `src/Sill.Host/Sync.cs`: 시작 전이면 시작 폴더, 대기면 `cd -LiteralPath`, 입력 중이면 건너뜀(줄을 지우지 않음), 실행 중이면 건너뜀(Sill의 cd가 도는 중이면 마지막 경로를 보류).
  - 셸 통합: sill-host가 PowerShell을 `-NoExit -EncodedCommand`로 시작해 프로필 적재 뒤 `prompt`를 감싼다(OSC 133;A, 9;9, 133;B). 파일이 아니라서 실행 정책에 걸리지 않는다. cmd 통합은 M4.
- 터미널 → 탐색기: 프롬프트가 내보내는 OSC 7 / OSC 9;9를 파싱해 `IShellBrowser::BrowseObject`로 이동한다.

## 제약

- 설치·제거는 관리자 권한 없이 HKCU 등록만으로 한다.
- Windows 10 이상만 지원한다. 현재 범위 밖: SSH, 분할 터미널, 다중 탭.
- 마일스톤은 M0(빈 band를 Win11 탐색기에 표시)부터 M5(배포)까지 순서대로 진행한다. M0이 실패하면 터미널 창 도킹 방식으로 방향을 바꾼다. 자세한 내용은 `PROPOSAL.md` 4–5절에 있다.
