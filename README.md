# Sill: a terminal panel for Windows File Explorer

[![Latest release](https://img.shields.io/github/v/release/2j2h5/winsill)](https://github.com/2j2h5/winsill/releases/latest)
[![License: MIT](https://img.shields.io/github/license/2j2h5/winsill)](LICENSE)
![Windows 11 | 10 untested](https://img.shields.io/badge/Windows-11%20%7C%2010%20untested-0078D4)

Sill adds an integrated terminal to the bottom of File Explorer on Windows, like the terminal panel in KDE Dolphin. Open PowerShell, Command Prompt (cmd) or Git Bash in the folder you are looking at, and File Explorer and the terminal follow each other's current folder.

![Sill: a PowerShell terminal panel docked at the bottom of a Windows 11 File Explorer window, with two terminal tabs](assets/screenshot.png)

Sill extends File Explorer itself. It is not a replacement file manager. The panel and the folder window stay in sync:

- When you go to another folder in File Explorer, the terminal runs `cd` to follow, but only while the shell is waiting at its prompt.
- When you `cd` in the terminal, File Explorer goes to that folder.

## Features

- **Terminal inside File Explorer:** a panel at the bottom of every File Explorer window. `` Ctrl+` `` opens and closes it, like the terminal in VS Code.
- **Two-way folder sync:** File Explorer and the shell stay in the same folder. Sill types `cd` only while the shell waits at its prompt, which its shell integration marks, so it doesn't type into a running program such as Claude Code or `vim`.
- **Terminal tabs:** several terminals per window, with VS Code keys (`` Ctrl+Shift+` ``, `Ctrl+PageUp`/`Ctrl+PageDown`).
- **Tab tear-off:** drag a File Explorer tab out into its own window, or into another window, and its terminals keep running and move with it.
- **Alerts:** when a terminal you aren't looking at rings the bell, for example when Claude Code or Codex finishes, its tab shows a dot, the taskbar button flashes, and a closed panel opens (you can turn that off). For Claude Code and other AI coding agents, check **Receive alerts from AI coding agents** when you install Sill. Codex alerts without it.
- **Your shell:** PowerShell 7, Windows PowerShell, cmd, Git Bash, or any command line.
- **"Open in Sill"** on the right-click menu of a folder background.
- **Light and dark themes** that follow Windows.
- **No administrator rights:** per-user install and uninstall, registered under HKCU only.

## Requirements

- Windows 10 version 1903 or later, or Windows 11
- [WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2/). Windows 11 already has it.

Sill has only been tested on Windows 11 26H2 (build 26300). Windows 10 should work but hasn't been tested. Each terminal uses about 86 MB of memory (PowerShell, including its WebView2 share).

## Install

1. Download `Sill-Setup-<version>.exe` from [Releases](https://github.com/2j2h5/winsill/releases) and run it. You don't need administrator rights. Sill is installed for the current user in `%LOCALAPPDATA%\Programs\Sill`.
2. The installer is not code-signed, so SmartScreen may show "Windows protected your PC". Click **More info**, then **Run anyway**.
3. Open a new File Explorer window and press `` Ctrl+` ``.

**Update:** run the setup of the newer version. If File Explorer had the panel loaded, restart File Explorer or sign out to use the new version. Your settings and panel height are kept.

**Uninstall:** in Settings > Apps, find Sill and choose Uninstall. This also deletes `%LOCALAPPDATA%\Sill` (logs, settings, WebView2 data). Files that are still in use are deleted at your next sign-in.

## Usage

### Keys

| Key | Action |
|---|---|
| `` Ctrl+` `` | Open the panel, focus it, or close it (in a File Explorer window) |
| `` Ctrl+Shift+` `` | New terminal |
| `` Ctrl+Alt+` `` | Move the panel between the bottom and right of Explorer |
| `Ctrl+PageUp` / `Ctrl+PageDown` | Previous / next terminal (wraps around) |
| `Ctrl+,` | Open `settings.json` in Notepad |
| `Ctrl+Up` / `Ctrl+Down` | Scroll to the previous / next command |
| `Ctrl+Home` / `Ctrl+End` | Scroll to the top / bottom |
| `Ctrl+Alt+PageUp` / `Ctrl+Alt+PageDown` | Scroll one line |
| `Shift+PageUp` / `Shift+PageDown` | Scroll one page |
| `Ctrl+C` | Copy if text is selected, otherwise send `^C` |
| `Ctrl+V` | Paste |
| `Ctrl+click` | Open a link in your default browser (http and https only) |

Sill handles these keys itself and doesn't send them to the shell (except `^C`). `` Ctrl+` `` and `` Ctrl+Shift+` `` work while a File Explorer window is in the foreground.

### Terminal tabs

- **+** (or `` Ctrl+Shift+` ``) opens a new terminal in the folder File Explorer is showing.
- Close a tab with its **×** or a middle click. Closing the last tab closes the panel.
- `exit` closes the tab. If the shell ends with a non-zero exit code, the tab stays open and shows the code. Press any key to restart it.
- The tab title is the title the running program sets, or the shell name.
- Drag a File Explorer tab out into a new window, back again, or into another window's tab bar: the panel moves with it, with the same shells and their output. Closing a File Explorer tab or window ends its terminals.

### Panel position

Use the **Move panel** button in the tab strip or `` Ctrl+Alt+` `` to switch between
the bottom and the right. Existing terminal sessions keep running. Drag the left
edge of a right panel to adjust its width. Closing the panel gives its space back
to Explorer. The last position is saved as `panelPosition` in `settings.json` and
is used by new panels; other open panels keep their own position.

### Alerts

When a terminal you aren't looking at rings the bell (BEL) or asks for a desktop notification (OSC 9, OSC 777 or OSC 99), Sill lets you know:

- A small dot, in the color the taskbar uses when it flashes, appears to the right of that terminal's name on its tab. It goes away as soon as you can see that terminal: when its File Explorer window is in front with the panel open on that terminal, or when you switch to it.
- If the File Explorer window isn't in the foreground, its taskbar button flashes.
- If the panel is closed, it opens and shows that terminal, without taking the keyboard focus.

Sill alerts you once until you look at the terminal. A bell in the terminal you are typing in does nothing, and Sill never plays a sound. An alert from another tab of the same File Explorer window shows as the dot when you switch back to that tab. When the panel opens for an alert, folder sync follows that terminal, and the file list gets shorter, so the item under the mouse can move. A program that keeps ringing the bell opens the panel again each time you close it. To keep the panel closed, set `"alertOpensPanel": false` in settings.json.

Windows can turn taskbar flashing off: Settings > Personalization > Taskbar > Taskbar behaviors > Show flashing on taskbar apps.

### AI coding agents

Any program that rings the bell or sends one of these notification sequences triggers an alert. Most AI coding agents don't do that on their own, so Sill can set them up for you: check **Receive alerts from AI coding agents** when you install Sill, or run this once later (for example after you install a new agent):

```powershell
& "$env:LOCALAPPDATA\Programs\Sill\bin\sill-host.exe" agents
```

It sets up the agents it finds and shows what it did. Everything it adds rings the bell only inside Sill (it checks `TERM_PROGRAM=Sill`, and for a Claude Code background session, whether Claude Code is open in a Sill terminal) and does nothing in other terminals. `sill-host.exe agents remove` takes it all out again, and uninstalling Sill does too. Agents load these settings when a session starts, so restart sessions that were already running (in Claude Code, `/reload-plugins` also works).

| Agent | What Sill sets up | Alerts when |
|---|---|---|
| Claude Code | The Sill plugin, installed with `claude plugin install` | Claude finishes (after the background tasks it started), asks for permission, or stops on an error, also in a background session you open from Sill. Tested with 2.1.322 |
| Codex | Nothing. Codex notifies by default when its terminal isn't focused (`[tui] notifications`) | Codex finishes or asks for approval. Tested with 0.167.1 |
| GitHub Copilot CLI | A hook file, `%USERPROFILE%\.copilot\hooks\sill.json` | The agent finishes a turn or shows a notification (permission prompt and others). Not tested yet |
| opencode | A plugin file, `~\.config\opencode\plugins\sill.js` | The session goes idle or asks for permission. Not tested yet |

For other agents, turn on their notification setting, or add a hook that runs `sill-host.exe bell`, which rings the bell in Sill.

### Context menu

Right-click an empty area of a folder and choose **Open in Sill**. On Windows 11 it is under **Show more options**. On the desktop, Sill opens that folder in a new File Explorer window with the panel.

### Folder sync

- File Explorer → terminal: Sill types `cd` only while the shell is waiting at the prompt. If you are typing a command or a program is running (for example Claude Code or `vim`), Sill skips the change and doesn't catch up later. It never sends input to a running program. Once you start typing at the prompt, the line counts as in use until you press Enter or Ctrl+C, even if you delete what you typed.
- Terminal → File Explorer: when the shell's folder changes, File Explorer follows.
- With several terminals, only the visible one is kept in sync. Switching tabs doesn't move File Explorer.
- In PowerShell and Git Bash the `cd` from Sill doesn't show up in the terminal or in your command history. In cmd it is typed as a normal command. cmd can't `cd` to a UNC path, so those changes are skipped.

### Settings

`%LOCALAPPDATA%\Sill\settings.json` (press `Ctrl+,` to open it):

```json
{ "shell": "pwsh" }
```

| `shell` | Runs |
|---|---|
| `""` or no file | `pwsh` if it is on PATH, otherwise Windows PowerShell |
| `powershell` | Windows PowerShell 5.1 |
| `pwsh` | PowerShell 7 |
| `cmd` | Command Prompt |
| `gitbash` | Git Bash |
| anything else | That command line, as is (folder sync needs one of the shells above) |

`"alertOpensPanel": false` keeps a closed panel closed when a terminal alerts you (see [Alerts](#alerts)). The default is `true`.

A change applies to the next terminal you open.

### Appearance

The panel follows the Windows app mode (light or dark) under Settings > Personalization > Colors.

## Troubleshooting

- **File Explorer keeps crashing:** press Win+R and run `%LOCALAPPDATA%\Programs\Sill\unins000.exe`. This works without File Explorer.
- **Setup says the path contains "#":** .NET Framework can't load the panel from such a folder, so Sill can't be installed for a user whose profile path contains `#`.
- **Smart App Control** blocks unsigned programs. On a PC where it is turned on, the setup won't run.
- **The panel says WebView2 is missing:** install the WebView2 Runtime and open the panel again.
- **The panel stays gray:** delete `%LOCALAPPDATA%\Sill\WebView2` and open the panel again.
- **The "Open in Sill" icon looks old after an update:** Windows caches menu icons. It refreshes eventually, or after you clear the icon cache.
- **Logs:** `%LOCALAPPDATA%\Sill\band.log` (panel), `host.log` (shell sessions), `keys.log` (shortcut keys). Each log is limited to about 2 MB.

## How it works

```
[explorer.exe]
  └ Sill.Band.dll (C#, COM Explorer Bar)
      └ WebView2 + xterm.js        draws the terminal and takes keyboard input
            ⇅ named pipe
[sill-host.exe]  one per terminal
  └ ConPTY → PowerShell / cmd / Git Bash
[sill-host.exe keys]  one per sign-in, shortcut keys
```

The code inside File Explorer is kept small, because an exception there brings down `explorer.exe`. Shells run in `sill-host.exe`. The shell integration reports the prompt state with OSC 133 and the current folder with OSC 9;9. Sill uses these to decide when it is safe to `cd`. Each terminal gets a random value that only its shell integration knows. Sill ignores these sequences when they don't carry it, so output from a program can't make Sill type `cd` or move File Explorer. `sill-host.exe` also watches the output for the bell and the desktop-notification sequences, and the panel decides whether you are looking at that terminal.

## Build from source

You don't need the .NET SDK. The build uses the C# compiler that comes with Windows (.NET Framework 4.8).

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1             # bin\ (runs the unit tests first; closes File Explorer folder windows that hold the DLL)
powershell -ExecutionPolicy Bypass -File .\build.ps1 -Isolated   # build without stopping Explorer or installed Sill processes
powershell -ExecutionPolicy Bypass -File .\tools\register.ps1    # register bin\ for the current user
powershell -ExecutionPolicy Bypass -File .\tools\host-check.ps1  # sill-host test without File Explorer
powershell -ExecutionPolicy Bypass -File .\tools\tearoff-check.ps1  # drags a File Explorer tab with the mouse (hands off while it runs)
powershell -ExecutionPolicy Bypass -File .\tools\package.ps1     # dist\Sill-Setup-<version>.exe (downloads Inno Setup into deps\)
powershell -ExecutionPolicy Bypass -File .\tools\unregister.ps1  # remove the registration
```

GitHub Actions runs the build, the shell tests and the installer build on every push to main.

## License

[MIT](LICENSE). Third-party licenses (xterm.js, WebView2 SDK) are in [THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt).
