# 탐색기 탭 떼기 시험(0.1.2 M1). 개발자 PC 전용, CI에서는 돌지 않는다. Windows 업데이트 뒤 떼기 동작이 바뀌었는지 볼 때도 쓴다.
# 시험 창(/separate)에 탭을 더하고, 둘째 탭의 패널에 터미널 둘을 만들어 둘째에서 줄 300개와 0.5 s마다 tick을 내게 한 뒤, 마우스로 그 탭을 끌어 뗀다.
# 판정: 같은 sill-host·셸, 새 창의 넘겨받음(터미널 수·활성·패널), 다시 연결·다시 틀기, 놓은 뒤 첫 출력까지, 원래 창에 패널 없음, 새 창을 닫은 뒤 정리.
# 캡처(-Out)는 눈으로 본다: 2s(놓은 뒤 2 s 화면), tick-a·b(1 s 간격, 숫자가 늘어남), top(Ctrl+Home 뒤 맨 위 line-1).
# -Hidden: 패널을 닫아 둔 탭을 뗀다(새 창에서도 닫힌 채, 다시 열면 지난 출력).
# -Back: 뗀 뒤 떼낸 창의 탭을 원래 창의 탭 줄에 다시 놓는다(떼낸 창은 사라지고 원래 창이 넘겨받음, back 캡처).
# 도는 동안 마우스와 키보드를 쓰지 않는다. 키는 포커스를 키마다 확인한 뒤에만 보낸다(docs/testing-notes.md).
param([int]$Count = 1, [switch]$Hidden, [switch]$Back, [string]$Out = "$env:TEMP\sill-tearoff-out")
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing, System.Windows.Forms
Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
public static class Native
{
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] public struct GUITHREADINFO { public int cbSize, flags; public IntPtr hwndActive, hwndFocus, hwndCapture, hwndMenuOwner, hwndMoveSize, hwndCaret; public RECT rcCaret; }
    public delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] public static extern bool GetGUIThreadInfo(uint tid, ref GUITHREADINFO info);
    [DllImport("user32.dll")] public static extern bool IsChild(IntPtr parent, IntPtr h);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr parent, EnumProc proc, IntPtr l);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindowEx(IntPtr p, IntPtr after, string cls, string title);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern uint RegisterWindowMessage(string s);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
    [DllImport("user32.dll")] public static extern uint MapVirtualKey(uint code, uint type);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
    [DllImport("user32.dll")] public static extern void mouse_event(uint flags, int dx, int dy, uint data, UIntPtr extra);
    [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(POINT p);
    [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr h, uint flags);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);

    static string Text(IntPtr h) { var s = new StringBuilder(64); GetWindowText(h, s, 64); return s.ToString(); }
    static string Class(IntPtr h) { var s = new StringBuilder(64); GetClassName(h, s, 64); return s.ToString(); }

    // 활성 탭 창(z-순서 맨 위의 ShellTabWindowClass) 안의 band 창(제목 Sill, WinForms)
    public static IntPtr Band(IntPtr frame)
    {
        IntPtr tab = FindWindowEx(frame, IntPtr.Zero, "ShellTabWindowClass", null), found = IntPtr.Zero;
        EnumChildWindows(tab, (h, l) => { if (Text(h) == "Sill" && Class(h).StartsWith("WindowsForms10.")) { found = h; return false; } return true; }, IntPtr.Zero);
        return found;
    }

    // 포커스가 band 창의 엄격한 자손(WebView2)인가. 포커스 창은 msedgewebview2 스레드에 있으므로 자손 창의 스레드마다 본다(M1, M6).
    public static bool PanelFocused(IntPtr band)
    {
        if (band == IntPtr.Zero) return false;
        uint pid;
        var threads = new HashSet<uint> { GetWindowThreadProcessId(band, out pid) };
        EnumChildWindows(band, (h, l) => { uint p; threads.Add(GetWindowThreadProcessId(h, out p)); return true; }, IntPtr.Zero);
        foreach (uint t in threads)
        {
            var i = new GUITHREADINFO { cbSize = Marshal.SizeOf(typeof(GUITHREADINFO)) };
            if (GetGUIThreadInfo(t, ref i) && i.hwndFocus != band && IsChild(band, i.hwndFocus)) return true;
        }
        return false;
    }

    public static IntPtr FocusOf(IntPtr frame)
    {
        uint pid;
        var i = new GUITHREADINFO { cbSize = Marshal.SizeOf(typeof(GUITHREADINFO)) };
        GetGUIThreadInfo(GetWindowThreadProcessId(frame, out pid), ref i);
        return i.hwndFocus;
    }

    // 스캔 코드를 함께 준다. 없으면 페이지의 e.code가 비어 Ctrl+Home 같은 키가 듣지 않는다(M6).
    public static void Key(byte vk, bool up, bool ext) { keybd_event(vk, (byte)MapVirtualKey(vk, 0), (up ? 2u : 0u) | (ext ? 1u : 0u), UIntPtr.Zero); }

    public static IntPtr RootAt(int x, int y) { return GetAncestor(WindowFromPoint(new POINT { X = x, Y = y }), 2); }
}
'@
[Native]::SetProcessDPIAware() | Out-Null     # UI Automation 좌표와 커서 좌표를 같은 물리 픽셀로

$clsid = '{AE2C3550-FB10-4D93-B6E1-4FBED3C0AD90}'
$sill = Join-Path $env:LOCALAPPDATA 'Sill'
$dir = Join-Path $env:TEMP 'sill-tearoff'
New-Item -ItemType Directory -Force $dir, $Out | Out-Null
$shell = New-Object -ComObject Shell.Application
$keyMsg = [Native]::RegisterWindowMessage('Sill.Key')
# 셸마다 줄 300개를 낸 뒤 0.5 s마다 tick을 내는 명령. 셸은 sill-host가 settings.json대로 고른다(host.log의 셸 시작).
$feed = @{
    powershell = '1..300 | % { "line-$_" }; for ($i = 1;; $i++) { "tick $i"; Start-Sleep -Milliseconds 500 }'
    cmd        = 'for /l %i in (1,1,300) do @echo line-%i'
    gitbash    = "seq -f 'line-%g' 1 300; i=0; while true; do i=`$((i+1)); echo tick `$i; sleep 0.5; done"
}
$feed.pwsh = $feed.powershell

# 하나만 찾으면 PowerShell이 배열을 풀어 돌려주므로 부르는 쪽은 @()로 감싼다(.Count가 빔).
function Get-Tabs([string]$path) { @($shell.Windows() | Where-Object { try { $_.Document.Folder.Self.Path -eq $path } catch { $false } }) }
# 로그에는 날짜가 없으므로 줄 수로 표시해 두고 그 뒤를 읽는다.
function Get-Mark($file) { $p = "$sill\$file"; if (Test-Path $p) { @(Get-Content -Encoding UTF8 $p).Count } else { 0 } }
function Get-Since($file, $mark) { @(Get-Content -Encoding UTF8 "$sill\$file" | Select-Object -Skip $mark) }
function Test-Log($file, $mark, $pattern) { [bool](@(Get-Since $file $mark) -match $pattern) }
function Wait-Until([scriptblock]$cond, [double]$sec) { $d = (Get-Date).AddSeconds($sec); while (-not (& $cond) -and (Get-Date) -lt $d) { Start-Sleep -Milliseconds 100 }; [bool](& $cond) }
function Get-WindowPid($hwnd) { $p = [uint32]0; [Native]::GetWindowThreadProcessId($hwnd, [ref]$p) | Out-Null; [int]$p }
function Get-Alive($ids) { @($ids | Where-Object { Get-Process -Id $_ -ErrorAction SilentlyContinue }).Count }
function Get-LogTime([string]$line) { [datetime]::ParseExact($line.Substring(0, 12), 'HH:mm:ss.fff', $null) }
# 개발자의 터미널이 전경이면 새 창이 앞에 나오지 않는다. Alt를 눌렀다 뗀 뒤 SetForegroundWindow(M5).
# 이미 앞에 있으면 Alt를 보내지 않는다. 탐색기에서 Alt는 명령 모음으로 포커스를 가져갈 수 있다.
function Set-Front($hwnd) {
    if ([Native]::GetForegroundWindow() -eq $hwnd) { return $true }
    [Native]::Key(0x12, $false, $false); [Native]::Key(0x12, $true, $false)
    [Native]::SetForegroundWindow($hwnd) | Out-Null
    Wait-Until { [Native]::GetForegroundWindow() -eq $hwnd } 2
}
$screen = [Windows.Forms.Screen]::PrimaryScreen.Bounds
function Move-Abs($px, $py) { [Native]::mouse_event(0x8001, [int]($px * 65535 / ($screen.Width - 1)), [int]($py * 65535 / ($screen.Height - 1)), 0, [UIntPtr]::Zero) }
function Send-Ctrl([byte]$vk, [bool]$ext) { [Native]::Key(0x11, $false, $false); [Native]::Key($vk, $false, $ext); [Native]::Key($vk, $true, $ext); [Native]::Key(0x11, $true, $false) }
function Save-Window($hwnd, $file) {
    $r = New-Object Native+RECT; [Native]::GetWindowRect($hwnd, [ref]$r) | Out-Null
    $bmp = New-Object Drawing.Bitmap ($r.Right - $r.Left), ($r.Bottom - $r.Top)
    $g = [Drawing.Graphics]::FromImage($bmp); $hdc = $g.GetHdc()
    [Native]::PrintWindow($hwnd, $hdc, 2) | Out-Null     # PW_RENDERFULLCONTENT: WebView2까지
    $g.ReleaseHdc($hdc); $g.Dispose(); $bmp.Save($file); $bmp.Dispose()
}

$clip = Get-Clipboard -Raw
$cursor = New-Object Native+POINT; [Native]::GetCursorPos([ref]$cursor) | Out-Null
$pass = 0
try {
for ($k = 1; $k -le $Count; $k++) {
    $r = [ordered]@{}; $info = [ordered]@{}
    # 개발자가 같은 폴더를 연 창이 있으면 시험이 그 창을 건드린다(M10).
    if (@(Get-Tabs $dir).Count) { throw '시험 폴더를 보는 창이 이미 있다' }
    $mb = Get-Mark 'band.log'; $mh = Get-Mark 'host.log'; $mk = Get-Mark 'keys.log'

    Start-Process explorer.exe "/separate,`"$dir`""
    # 새 탐색기 프로세스가 처음 뜰 때는 10 s를 넘기기도 했다.
    if (-not (Wait-Until { @(Get-Tabs $dir).Count -eq 1 } 30)) { throw '시험 창이 열리지 않았다' }
    $frame = [IntPtr]@(Get-Tabs $dir)[0].HWND
    $testPid = Get-WindowPid $frame
    if (-not (Set-Front $frame)) { throw '시험 창을 앞으로 가져오지 못했다' }
    Start-Sleep -Milliseconds 500
    # Ctrl+T는 파일 목록에 포커스가 있을 때만 보낸다(패널이면 터미널로 간다). 새 탭은 "내 PC"(빈 LocationURL)로 나타난다(0.1.1 M4).
    $focus = [Native]::FocusOf($frame)
    if (-not ([Native]::GetForegroundWindow() -eq $frame -and [Native]::IsChild($frame, $focus) -and [Native]::Band($frame) -eq [IntPtr]::Zero)) { throw '포커스가 시험 창의 파일 목록이 아니다' }
    Send-Ctrl 0x54 $false
    Wait-Until { @($shell.Windows() | Where-Object { [IntPtr]$_.HWND -eq $frame }).Count -eq 2 } 5 | Out-Null
    $tab2 = @($shell.Windows() | Where-Object { [IntPtr]$_.HWND -eq $frame -and $_.LocationURL -eq '' })[0]
    if (-not $tab2) { throw '새 탭이 생기지 않았다' }
    $tab2.Navigate2($dir)       # 있는 경로만(M2)
    Wait-Until { try { $tab2.Document.Folder.Self.Path -eq $dir } catch { $false } } 5 | Out-Null

    # 둘째 탭에 패널을 열고 터미널을 하나 더 만든다(Sill.Key 1). 활성은 둘째 터미널.
    $tab2.ShowBrowserBar($clsid, $true, $null)
    Wait-Until { @(Get-Since 'band.log' $mb) -match "\[$testPid\] 터미널 1 만듦" } 10 | Out-Null
    $band = [Native]::Band($frame)
    [Native]::PostMessage($band, $keyMsg, [IntPtr]1, [IntPtr]::Zero) | Out-Null
    Wait-Until { @(@(Get-Since 'band.log' $mb) -match "\[$testPid\] sill-host 실행 pid=").Count -eq 2 } 10 | Out-Null
    $hostPids = @(@(Get-Since 'band.log' $mb) -match "\[$testPid\] sill-host 실행 pid=" | ForEach-Object { [int]($_ -replace '.*pid=', '') })
    $second = $hostPids[-1]
    Wait-Until { @(Get-Since 'host.log' $mh) -match "\[$second\] OSC 9;9" } 20 | Out-Null     # 둘째 터미널의 첫 프롬프트
    $shellName = (@(Get-Since 'host.log' $mh) -match "\[$second\] 셸 시작 " | Select-Object -First 1) -replace '.*셸 시작 (\S+).*', '$1'
    $shellName = [IO.Path]::GetFileNameWithoutExtension($shellName)      # powershell.exe 또는 gitbash
    if (-not $feed[$shellName]) { throw "모르는 셸 $shellName" }
    Set-Clipboard -Value $feed[$shellName]
    if (-not [Native]::PanelFocused($band)) { throw '포커스가 패널이 아니다(붙여넣기 전)' }
    Send-Ctrl 0x56 $false
    Start-Sleep -Milliseconds 300
    if (-not [Native]::PanelFocused($band)) { throw '포커스가 패널이 아니다(Enter 전)' }
    [Native]::Key(0x0D, $false, $false); [Native]::Key(0x0D, $true, $false)
    Start-Sleep 2
    if ($Hidden) {
        $tab2.ShowBrowserBar($clsid, $false, $null)
        Wait-Until { @(Get-Since 'band.log' $mb) -match "\[$testPid\] ShowDW\(False\)" } 5 | Out-Null
    }
    $shellPids = @(foreach ($h in $hostPids) { Get-CimInstance Win32_Process -Filter "ParentProcessId=$h and Name<>'conhost.exe'" | ForEach-Object { $_.ProcessId } })

    # 둘째 탭 머리를 UI Automation으로 찾아 360 px 아래로 끌어 놓는다(프로포절 재현과 같음).
    if (-not (Set-Front $frame)) { throw '시험 창을 앞으로 가져오지 못했다(끌기 전)' }
    $cond = New-Object Windows.Automation.PropertyCondition ([Windows.Automation.AutomationElement]::ControlTypeProperty), ([Windows.Automation.ControlType]::TabItem)
    $items = @([Windows.Automation.AutomationElement]::FromHandle($frame).FindAll([Windows.Automation.TreeScope]::Descendants, $cond))
    if ($items.Count -ne 2) { throw "탭 머리가 $($items.Count)개" }
    $rect = $items[1].Current.BoundingRectangle
    $x = [int]($rect.X + $rect.Width / 2); $y = [int]($rect.Y + $rect.Height / 2)
    # 겹친 창이 있으면 다른 창을 누를 수 있다(0.1.1 M4).
    if ([Native]::RootAt($x, $y) -ne $frame) { throw '탭 머리 위치가 시험 창이 아니다' }
    # SetCursorPos로 옮기면 탭이 떨어지지 않았다. 실제 입력(MOVE|ABSOLUTE)으로 옮기고 누르기 앞뒤에 멈춘다(프로포절 재현과 같음).
    Move-Abs $x $y; Start-Sleep -Milliseconds 200
    [Native]::mouse_event(2, 0, 0, 0, [UIntPtr]::Zero); Start-Sleep -Milliseconds 150
    for ($i = 1; $i -le 30; $i++) { Move-Abs ($x + 4 * $i) ($y + 12 * $i); Start-Sleep -Milliseconds 20 }
    Start-Sleep -Milliseconds 300
    $drop = Get-Date
    [Native]::mouse_event(4, 0, 0, 0, [UIntPtr]::Zero)

    Wait-Until { @(Get-Tabs $dir | Where-Object { $_.HWND -and [IntPtr]$_.HWND -ne $frame }).Count -eq 1 } 5 | Out-Null
    $newTab = @(Get-Tabs $dir | Where-Object { $_.HWND -and [IntPtr]$_.HWND -ne $frame })[0]
    if (-not $newTab) { throw '새 창이 생기지 않았다' }
    $newFrame = [IntPtr]$newTab.HWND
    $newPid = Get-WindowPid $newFrame
    $info['새 창 프로세스'] = $(if ($newPid -eq $testPid) { "시험 창과 같음 $newPid" } else { $newPid })
    Wait-Until { @(Get-Since 'band.log' $mb) -match "\[$newPid\] 넘겨받음" } 5 | Out-Null
    Start-Sleep -Milliseconds ([Math]::Max(0, 2000 - ((Get-Date) - $drop).TotalMilliseconds))
    Save-Window $newFrame "$Out\$k-2s.png"

    $panel = $(if ($Hidden) { '0' } else { '1' })
    $band2 = [Native]::Band($newFrame)
    $r['새 창 넘겨받음(2개, 활성 2)'] = Test-Log 'band.log' $mb "\[$newPid\] 넘겨받음 2개 활성=2 패널=$panel"
    $r['상주 프로세스가 패널을 엶'] = Test-Log 'keys.log' $mk '넘김 패널 열기'
    $r['sill-host마다 다시 연결·다시 틀기'] = @($hostPids | Where-Object { (Test-Log 'host.log' $mh "\[$_\] 다시 연결$") -and (Test-Log 'host.log' $mh "\[$_\] 다시 틀기 \d+자") }).Count -eq 2
    $first = @(@(Get-Since 'band.log' $mb) -match "\[$newPid\] 첫 출력" | ForEach-Object { ((Get-LogTime $_) - $drop).TotalSeconds })
    $info['놓은 뒤 첫 출력'] = ($first | ForEach-Object { '{0:F2} s' -f $_ }) -join ', '
    $r['놓은 뒤 첫 출력(2 s 이하)'] = $first.Count -eq 2 -and ($first | Measure-Object -Maximum).Maximum -le 2
    $r['원래 창에 패널 없음'] = [Native]::Band($frame) -eq [IntPtr]::Zero
    if ($Hidden) {
        $r['새 창에서도 패널 닫힘'] = $band2 -ne [IntPtr]::Zero -and -not [Native]::IsWindowVisible($band2)
        $shows = @(@(Get-Since 'band.log' $mb) -match "\[$newPid\] ShowDW\(" | ForEach-Object { Get-LogTime $_ })
        if ($shows.Count -ge 2) { $info['패널 자리가 보인 시간'] = '{0:F0} ms' -f ($shows[1] - $shows[0]).TotalMilliseconds }
        $newTab.ShowBrowserBar($clsid, $true, $null)        # 다시 열면 지난 출력이 보여야 한다
        Start-Sleep 1
        $band2 = [Native]::Band($newFrame)
    } else {
        $r['새 창에 패널 보임'] = $band2 -ne [IntPtr]::Zero -and [Native]::IsWindowVisible($band2)
    }
    Save-Window $newFrame "$Out\$k-tick-a.png"; Start-Sleep 1; Save-Window $newFrame "$Out\$k-tick-b.png"
    # Ctrl+Home(맨 위로)은 새 창 패널에 포커스가 있을 때만.
    if ((Set-Front $newFrame) -and [Native]::PanelFocused($band2)) { Send-Ctrl 0x24 $true; Start-Sleep -Milliseconds 500; Save-Window $newFrame "$Out\$k-top.png" }
    else { $info['맨 위 캡처'] = '못 함(패널 포커스 아님)' }

    Start-Sleep -Milliseconds ([Math]::Max(0, 10000 - ((Get-Date) - $drop).TotalMilliseconds))
    $r['같은 sill-host·셸(10 s 뒤)'] = (Get-Alive $hostPids) -eq 2 -and $shellPids.Count -ge 2 -and (Get-Alive $shellPids) -eq $shellPids.Count

    if ($Back) {
        # 떼낸 창의 탭을 원래 창의 첫 탭 오른쪽 1/4에 놓는다. 떼낸 창은 탭이 하나라 사라지므로, 상주 프로세스는 창 닫기와 새 탭 등록으로 가른다.
        $mb = Get-Mark 'band.log'; $mh = Get-Mark 'host.log'; $mk = Get-Mark 'keys.log'
        # 떼낸 창은 화면 왼쪽 위에 열려 원래 창의 탭 줄을 가리기도 했다. 원래 창 탭 줄 아래로 옮긴다(크기는 그대로).
        $or = New-Object Native+RECT; [Native]::GetWindowRect($frame, [ref]$or) | Out-Null
        [Native]::SetWindowPos($newFrame, [IntPtr]::Zero, $or.Left, $or.Top + 200, 0, 0, 0x15) | Out-Null
        Start-Sleep -Milliseconds 300
        if (-not (Set-Front $newFrame)) { throw '떼낸 창을 앞으로 가져오지 못했다(다시 붙이기 전)' }
        $sr = @([Windows.Automation.AutomationElement]::FromHandle($newFrame).FindAll([Windows.Automation.TreeScope]::Descendants, $cond))[0].Current.BoundingRectangle
        $dr = @([Windows.Automation.AutomationElement]::FromHandle($frame).FindAll([Windows.Automation.TreeScope]::Descendants, $cond))[0].Current.BoundingRectangle
        $x = [int]($sr.X + $sr.Width / 2); $y = [int]($sr.Y + $sr.Height / 2)
        $tx = [int]($dr.X + $dr.Width * 0.75); $ty = [int]($dr.Y + $dr.Height / 2)
        if ([Native]::RootAt($x, $y) -ne $newFrame -or [Native]::RootAt($tx, $ty) -ne $frame) { throw '다시 붙일 위치가 시험 창이 아니다' }
        Move-Abs $x $y; Start-Sleep -Milliseconds 200
        [Native]::mouse_event(2, 0, 0, 0, [UIntPtr]::Zero); Start-Sleep -Milliseconds 150
        for ($i = 1; $i -le 30; $i++) { Move-Abs ($x + ($tx - $x) * $i / 30) ($y + ($ty - $y) * $i / 30); Start-Sleep -Milliseconds 30 }
        Start-Sleep -Milliseconds 400
        [Native]::mouse_event(4, 0, 0, 0, [UIntPtr]::Zero)
        Wait-Until { @(Get-Since 'band.log' $mb) -match "\[$testPid\] 넘겨받음" } 5 | Out-Null
        Start-Sleep 2
        Save-Window $frame "$Out\$k-back.png"
        $r['다시 붙이기: 떼낸 창 사라짐'] = -not [Native]::IsWindowVisible($newFrame)
        $r['다시 붙이기: 원래 창 넘겨받음(2개, 활성 2)'] = Test-Log 'band.log' $mb "\[$testPid\] 넘겨받음 2개 활성=2 패널=1"
        $r['다시 붙이기: 상주 프로세스(원래 창 없음, 등록=1)'] = Test-Log 'keys.log' $mk '넘김 패널 열기 .*원래 창 없음, 등록=1'
        $r['다시 붙이기: 다시 연결 2개'] = @($hostPids | Where-Object { Test-Log 'host.log' $mh "\[$_\] 다시 연결$" }).Count -eq 2
        Start-Sleep 3
        $r['다시 붙이기: 같은 sill-host·셸(5 s 뒤)'] = (Get-Alive $hostPids) -eq 2 -and (Get-Alive $shellPids) -eq $shellPids.Count
    }

    # 패널이 있는 창을 닫으면(원래 창이 사라짐) 넘기지 않고 유예 뒤 끝난다. 남은 창도 닫는다(두 탭 창은 WM_CLOSE 한 번에 탭 하나만 닫히기도 했다).
    $sw = [Diagnostics.Stopwatch]::StartNew()
    [Native]::PostMessage($(if ($Back) { $frame } else { $newFrame }), 0x10, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
    $gone = Wait-Until { (Get-Alive $hostPids) -eq 0 } 6
    $info['창을 닫은 뒤 끝남'] = '{0:F1} s' -f $sw.Elapsed.TotalSeconds
    $r['창을 닫으면 끝남(4 s 이하)'] = $gone -and $sw.Elapsed.TotalSeconds -le 4 -and (Get-Alive $shellPids) -eq 0
    foreach ($w in $frame, $newFrame) { for ($n = 0; $n -lt 3 -and [Native]::IsWindowVisible($w); $n++) { [Native]::PostMessage($w, 0x10, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null; Start-Sleep 1 } }
    Wait-Until { @(Get-Tabs $dir).Count -eq 0 } 5 | Out-Null

    $ok = $r.Values -notcontains $false
    if ($ok) { $pass++ }
    "== $k/$Count $(if ($ok) { 'ok' } else { 'FAIL' })"
    $info.GetEnumerator() | ForEach-Object { "  {0}: {1}" -f $_.Key, $_.Value }
    $r.GetEnumerator() | ForEach-Object { "  {0}: {1}" -f $_.Key, $(if ($_.Value) { 'ok' } else { 'FAIL' }) }
}
} finally {
    if ($clip) { Set-Clipboard -Value $clip }
    [Native]::SetCursorPos($cursor.X, $cursor.Y) | Out-Null
}
"떼기 $pass/$Count"
if ($pass -ne $Count) { exit 1 }
