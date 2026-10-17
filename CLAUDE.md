# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## 진행 방식

milepost로 진행한다. 상태는 `docs/reports/state.json`에 있고, 프로포절·마일스톤 설계·결과 보고서는 `docs/reports/sill/`에 있다. 설계 결정의 근거는 이 보고서들과 `PROPOSAL.md`다.

## 명령

.NET SDK가 없다. Windows에 들어 있는 `csc.exe`(C# 5 문법: 문자열 보간·`?.`·`nameof` 사용 불가)로 빌드한다. 스크립트는 PowerShell 5.1에서 읽히도록 UTF-8 BOM으로 저장한다.

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1               # bin\Sill.Band.dll (폴더 창 explorer 프로세스를 먼저 종료)
powershell -ExecutionPolicy Bypass -File .\tools\register.ps1      # HKCU에만 등록 (관리자 권한 불필요)
powershell -ExecutionPolicy Bypass -File .\tools\toggle.ps1 on|off # 열린 모든 탐색기 창(탭)에 ShowBrowserBar
powershell -ExecutionPolicy Bypass -File .\tools\unregister.ps1    # 등록 키 전부 삭제 (탐색기가 계속 죽을 때 탈출구)
```

- band 호출 로그: `%LOCALAPPDATA%\Sill\band.log` (시각, PID, 메서드).
- 개발 중에는 `HKCU\...\Explorer\Advanced\SeparateProcess=1`이 켜져 있다(원래 값 0). 새 폴더 창은 `explorer.exe /factory,...` 프로세스에서 열리므로 band가 죽어도 작업 표시줄은 살아남는다. 이 설정 전에 열려 있던 창은 작업 표시줄 프로세스에 있으니 시험에 쓰지 않는다.
- 자동화 테스트는 없다. 확인은 PowerShell `Shell.Application.Windows()`로 창을 잡아 `ShowBrowserBar`를 부르고, band.log와 `PrintWindow` 캡처로 한다.

## 프로젝트 개요

**Sill** (저장소 `2j2h5/winsill`): Windows 탐색기 창 하단에 Dolphin(KDE)식 터미널 패널을 붙이는 셸 확장이다. 탐색기를 대체하는 앱이 아니라 탐색기 **자체**를 확장한다.

## 아키텍처 (계획)

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
- 키 입력 우선권(F2, Backspace, Ctrl+C 등을 탐색기가 먼저 가로채는 문제)은 `IInputObject::TranslateAcceleratorIO`에서 처리한다.
- 스택: C# / .NET Framework 4.8 (COM interop), WebView2, xterm.js, ConPTY. 필요하면 band만 C++로 옮긴다.

## 폴더 동기화 규칙

- 탐색기 → 터미널: `NavigateComplete2`가 오면 `cd`를 보낸다. 단, **셸이 프롬프트 대기 상태일 때만**(OSC 133으로 판단) 보낸다. Claude Code 같은 프로그램이 실행 중이면 절대 입력을 주입하지 않는다.
- 터미널 → 탐색기: 프롬프트가 내보내는 OSC 7 / OSC 9;9를 파싱해 `IShellBrowser::BrowseObject`로 이동한다.

## 제약

- 설치·제거는 관리자 권한 없이 HKCU 등록만으로 한다.
- Windows 10 이상만 지원한다. 현재 범위 밖: SSH, 분할 터미널, 다중 탭.
- 마일스톤은 M0(빈 band를 Win11 탐색기에 표시)부터 M5(배포)까지 순서대로 진행한다. M0이 실패하면 터미널 창 도킹 방식으로 방향을 바꾼다. 자세한 내용은 `PROPOSAL.md` 4–5절에 있다.
