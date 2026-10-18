# 탐색기 없이 sill-host를 띄워 확인한다.
# M1: 크기·입력을 보내면 셸 출력이 오고, 파이프를 닫으면 셸까지 정리되는가.
# M2: c(탐색기 폴더)를 셸 상태에 맞게 처리하는가. 시작 폴더, 대기 중 cd, 실행 중·입력 중 건너뜀, 빠른 연속 이동.
# M3: 셸의 cd를 p(셸 폴더)로 알리고, 탐색기가 따라온 뒤 돌려주는 c에는 cd를 넣지 않는가.
# M4: 위 항목을 셸마다(powershell, pwsh, cmd, gitbash) 돌린다. 사용자의 settings.json은 건드리지 않는다.
# M8: 출력 묶기, 되울림 지연, 파이프 ACL, PS1을 매번 쓰는 .bashrc, 낡은 되먹임 항목, 끊긴 네트워크 경로 뒤 입력, 끊긴 시작 폴더.
# M9: 종료 줄 뒤의 종료 코드(e).
param([ValidateSet('powershell', 'pwsh', 'cmd', 'gitbash')] [string]$Shell = 'powershell')
$ErrorActionPreference = 'Stop'
$base = Join-Path $env:TEMP ('sill-check-' + [guid]::NewGuid().ToString('N'))
$dirA = New-Item -ItemType Directory "$base\a"
$dirB = New-Item -ItemType Directory "$base\it's [x] 한글 `$x #b %PATH%"

# 셸마다 다른 명령. 출력 확인용 echo는 입력 에코에 'sill-ok'가 그대로 나오지 않게 쓴다.
$cmds = @{
    powershell = @{ ok = "Write-Output ('sill' + '-ok')"; sleep = 'Start-Sleep 2'; flood = '1..20000'; cd = { param($p) "cd -LiteralPath '" + ($p -replace "'", "''") + "'" } }
    cmd        = @{ ok = 'echo sill-^ok'; sleep = 'ping -n 3 127.0.0.1 >nul'; flood = 'for /l %i in (1,1,20000) do @echo %i'; cd = { param($p) 'cd /d "' + $p.Replace('%', '%""') + '"' } }
    gitbash    = @{ ok = "echo sill-''ok"; sleep = 'sleep 2'; flood = 'seq 1 20000'; cd = { param($p) "cd -- '" + $p.Replace("'", "'\''") + "'" } }
}
$cmds.pwsh = $cmds.powershell
$c = $cmds[$Shell]
if ($Shell -eq 'gitbash') {
    # 사용자 .bashrc가 살아 있는지 보려고 임시 HOME을 쓴다(sill-host가 물려받는다).
    $home2 = New-Item -ItemType Directory "$base\home"
    # M8: starship처럼 프롬프트마다 PS1을 새로 쓰면 셸 통합이 붙인 133;B가 지워진다.
    [IO.File]::WriteAllText("$home2\.bashrc", "alias sillalias='echo alias-''ok'`nPROMPT_COMMAND='PS1=`"\w\$ `"'`n")
    $env:HOME = $home2.FullName
}
$name = 'sill-check-' + [guid]::NewGuid().ToString('N')
$hostProc = Start-Process "$PSScriptRoot\..\bin\sill-host.exe" "$name $Shell" -PassThru
$pipe = New-Object System.IO.Pipes.NamedPipeClientStream('.', $name, 'InOut', 'Asynchronous')
$pipe.Connect(5000)
$pipe.ReadMode = 'Message'
# M8: 파이프는 현재 사용자만 열 수 있다.
$rules = @($pipe.GetAccessControl().GetAccessRules($true, $false, [Security.Principal.SecurityIdentifier]))
$aclOk = $rules.Count -eq 1 -and $rules[0].IdentityReference -eq [Security.Principal.WindowsIdentity]::GetCurrent().User
function Send([string]$s) { $b = [Text.Encoding]::UTF8.GetBytes($s); $pipe.Write($b, 0, $b.Length) }

# 받은 출력에서 메시지 머리글자 o를 떼고 이어 붙인다(OSC가 메시지 경계에서 나뉠 수 있다). p 메시지는 따로 모은다.
$script:text = ''; $script:moves = @(); $script:kind = 'o'; $script:atStart = $true; $script:t = $null; $script:omsgs = 0
$buf = New-Object byte[] 65536
function WaitFor([scriptblock]$cond, [double]$sec = 20) {
    $deadline = (Get-Date).AddSeconds($sec)
    while (-not (& $cond) -and (Get-Date) -lt $deadline) {
        if ($script:t -eq $null) { $script:t = $pipe.ReadAsync($buf, 0, $buf.Length) }
        if (-not $script:t.Wait(500)) { continue }
        $chunk = [Text.Encoding]::UTF8.GetString($buf, 0, $script:t.Result)
        if ($script:atStart -and $chunk.Length) { $script:kind = $chunk[0]; $chunk = $chunk.Substring(1); if ($script:kind -eq 'o') { $script:omsgs++ } }
        $script:atStart = $pipe.IsMessageComplete
        if ($script:kind -eq 'p') { $script:moves += $chunk } elseif ($script:kind -eq 'e') { $script:exitCode = $chunk } else { $script:text += $chunk }
        $script:t = $null
    }
    [bool](& $cond)
}
function Cwd { $m = [regex]::Matches($script:text, "\x1b\]9;9;([^\x07\x1b]*)"); if ($m.Count) { $m[$m.Count - 1].Groups[1].Value } }
# 프롬프트는 133;A로 센다. bash는 입력 줄을 다시 그릴 때 133;B만 다시 낸다.
function Prompts { [regex]::Matches($script:text, "\x1b\]133;A").Count }
function Ready { $i = $script:text.LastIndexOf("$([char]27)]133;A"); $i -ge 0 -and $script:text.IndexOf("$([char]27)]133;B", $i) -ge 0 }
# 프롬프트 수는 보내기 전에 세어 둔다. 보낸 뒤에 세면 그사이 온 프롬프트를 놓친다.
function WaitPrompt([int]$n) { WaitFor { (Prompts) -gt $n -and (Ready) } | Out-Null }
$results = [ordered]@{}

Send "c$dirA"      # 셸 시작 전: 시작 폴더가 된다
Send 'r100,30'
$results['시작 폴더'] = WaitFor { (Cwd) -eq $dirA.FullName -and (Prompts) -gt 0 } 30
Start-Sleep 1      # 프로필이 프롬프트를 두 번 그리는 경우까지 기다린다

# 입력 에코에는 'sill-ok'가 그대로 나오지 않고, 실행 결과에만 나온다.
$n = Prompts; Send "i$($c.ok)`r"; WaitPrompt $n
$results['입출력'] = $script:text -match 'sill-ok'
# M6: 셸이 있는데 같은 크기가 오면(새 페이지) ConPTY가 화면을 다시 보낸다. 다시 온 화면에 앞의 출력이 있어야 한다.
$len = $script:text.Length; Send 'r100,30'
$results['같은 크기 r → 화면 다시 옴'] = WaitFor { $script:text.IndexOf('sill-ok', $len) -ge 0 } 5
# M7: 탭 제목(OSC 0)은 ConPTY가 다시 보내지 않으므로 sill-host가 마지막 제목을 보낸다.
$results['같은 크기 r → 제목 다시 옴'] = $script:text.IndexOf("$([char]27)]0;", $len) -ge 0

$results['파이프 ACL 현재 사용자만'] = $aclOk
$info = [ordered]@{}

# M8: 대량 출력은 sill-host가 16 ms에 한 번까지 묶어 보낸다. 입력 에코에는 19999가 없다.
$len = $script:text.Length; $m0 = $script:omsgs; $sw = [Diagnostics.Stopwatch]::StartNew()
$n = Prompts; Send "i$($c.flood)`r"; WaitPrompt $n
$sec = $sw.Elapsed.TotalSeconds; $rate = ($script:omsgs - $m0) / $sec
$info['출력 묶기'] = '{0}개 / {1:F2} s = {2:F0}개/s' -f ($script:omsgs - $m0), $sec, $rate
$results['출력 묶기(65개/s 이하, 끝까지 옴)'] = $rate -le 65 -and $script:text.Substring($len) -match '(?s)19999.*20000'

# M8: 대기 중에 친 글자 하나가 되울려 오는 시간
$lat = foreach ($ch in 'abcdefghijklmnopqrst'.ToCharArray()) {
    $len = $script:text.Length; $sw.Restart(); Send "i$ch"
    WaitFor { $script:text.Length -gt $len } 2 | Out-Null
    $sw.Elapsed.TotalMilliseconds
    Start-Sleep -Milliseconds 50
}
$lat = @($lat | Sort-Object)
$info['되울림 지연'] = '중앙값 {0:F1} ms, 최대 {1:F1} ms' -f (($lat[9] + $lat[10]) / 2), $lat[-1]
$n = Prompts; Send "i$([char]3)"; WaitPrompt $n

$n = Prompts; Send "c$dirB"; WaitPrompt $n
$results['대기 중 cd (특수 문자 경로)'] = (Cwd) -eq $dirB.FullName

# M6: PowerShell과 Git Bash는 Sill의 cd를 키 처리기로 받으므로 cd 줄이 화면(출력)과 명령 기록에 없다. cmd는 지금처럼 입력한다.
if ($Shell -ne 'cmd') {
    $results['Sill의 cd 줄 없음'] = $script:text -notmatch "cd -LiteralPath|cd -- '"
    $len = $script:text.Length; Send "i$([char]27)[A"; WaitFor { $false } 1 | Out-Null
    $results['위 화살표에 Sill의 cd 없음'] = $script:text.Substring($len) -notmatch "LiteralPath|cd -- "
    $n = Prompts; Send "i$([char]3)"; WaitPrompt $n
}

$n = Prompts; Send "i$($c.sleep)`r"
Start-Sleep -Milliseconds 300
Send "c$dirA"; WaitPrompt $n
$results['실행 중 건너뜀'] = (Cwd) -eq $dirB.FullName

Send 'iecho typed'
Start-Sleep -Milliseconds 300
Send "c$dirA"
Start-Sleep -Milliseconds 500
$n = Prompts; Send "i`r"; WaitPrompt $n
# cd가 끼어들었다면 실행된 줄이 "echo typedcd ..."가 된다.
$results['입력 중 건너뜀'] = ((Cwd) -eq $dirB.FullName) -and ($script:text -notmatch 'typedcd')

# 입력 중에 Ctrl+L로 프롬프트를 다시 그려도 입력 중이다. 다시 그린 프롬프트를 새 프롬프트로 보면 cd가 끼어든다.
Send 'iecho part'
Start-Sleep -Milliseconds 300
$n = Prompts; Send "i$([char]12)"
WaitFor { (Prompts) -gt $n } 2 | Out-Null     # PowerShell은 1초 남짓 뒤에 프롬프트를 다시 그린다. bash는 133;A 없이 다시 그린다
Send "c$dirA"
Start-Sleep -Milliseconds 500
$n = Prompts; Send "i`r"; WaitPrompt $n
$results['입력 중 다시 그림 뒤 건너뜀'] = ((Cwd) -eq $dirB.FullName) -and ($script:text -notmatch 'partcd')

# Ctrl+C로 입력 줄을 버리면 새 프롬프트에서 다시 대기다.
Send 'iecho cc'
Start-Sleep -Milliseconds 300
$n = Prompts; Send "i$([char]3)"; WaitPrompt $n
$n = Prompts; Send "c$dirA"; WaitPrompt $n
$results['Ctrl+C 뒤 대기'] = (Cwd) -eq $dirA.FullName
$n = Prompts; Send "c$dirB"; WaitPrompt $n

# A로 가는 cd가 도는 사이 B가 오면, 보류했다가 다음 프롬프트에서 B로 간다.
Send "c$dirA"; Send "c$dirB"
WaitFor { (Cwd) -eq $dirB.FullName } 10 | Out-Null
WaitFor { $false } 1 | Out-Null
$results['빠른 연속 이동'] = (Cwd) -eq $dirB.FullName

# 지금까지는 첫 프롬프트와 Sill이 넣은 cd뿐이므로 p가 없어야 한다.
$results['첫 프롬프트·Sill의 cd는 p 없음'] = $script:moves.Count -eq 0

Send ("i" + (& $c.cd $dirA.FullName) + "`r")
WaitFor { $script:moves.Count -ge 1 } 10 | Out-Null    # p는 프롬프트 출력(o) 뒤에 온다
$results['셸의 cd → p'] = $script:moves.Count -eq 1 -and $script:moves[-1] -eq $dirA.FullName

# 셸이 B, A로 연달아 옮긴 뒤 탐색기가 받은 p를 차례로 돌려준다. 되먹임 목록이 없으면 c B에서 셸을 B로 되돌린다.
# 두 프롬프트가 한 번에 읽히면 sill-host는 마지막 폴더(A)만 알린다(M8). 그래서 받은 p만큼 c를 돌려준다.
# bash는 9;9(PROMPT_COMMAND)와 133;B(PS1)를 따로 내므로 p 뒤에 프롬프트 끝까지 기다린다.
$m0 = $script:moves.Count
$n = Prompts; Send ("i" + (& $c.cd $dirB.FullName) + "`r" + (& $c.cd $dirA.FullName) + "`r")
WaitFor { $script:moves.Count -gt $m0 -and $script:moves[-1] -eq $dirA.FullName -and (Prompts) -ge $n + 2 -and (Ready) } 10 | Out-Null
$back = @($script:moves | Select-Object -Skip $m0)
$n = Prompts
foreach ($m in $back) { Send "c$m" }
WaitFor { $false } 2 | Out-Null
$info['연달아 옮김의 p'] = $back.Count
$results['돌아온 c는 cd 없음'] = $back[-1] -eq $dirA.FullName -and (Prompts) -eq $n -and (Cwd) -eq $dirA.FullName

# M8: 탐색기가 따라오지 못한 p(BrowseObject 실패)는 2초 뒤에는 되먹임으로 보지 않는다. 그 뒤 탐색기에서 B로 가면 cd가 들어가야 한다.
$n = Prompts; Send ("i" + (& $c.cd $dirB.FullName) + "`r"); WaitPrompt $n
$m0 = $script:moves.Count
$n = Prompts; Send ("i" + (& $c.cd $dirA.FullName) + "`r")
WaitFor { $script:moves.Count -gt $m0 -and (Prompts) -gt $n -and (Ready) } 10 | Out-Null
WaitFor { $false } 2.5 | Out-Null
$n = Prompts; Send "c$dirB"; WaitPrompt $n
$results['낡은 되먹임 항목 버림'] = (Cwd) -eq $dirB.FullName
$n = Prompts; Send "c$dirA"; WaitPrompt $n

# 셸이 없는 폴더를 알려도 p를 보내지 않는다(탐색기 오류 창). 다음 프롬프트는 A로 돌아오므로 p A는 온다.
if ($Shell -in 'powershell', 'pwsh') {
    $n = Prompts; Send "iWrite-Host -NoNewline ([char]27 + ']9;9;C:\sill-nope' + [char]27 + '\')`r"; WaitPrompt $n
    $results['없는 경로는 p 없음'] = $script:moves -notcontains 'C:\sill-nope'
}
if ($Shell -eq 'cmd') {
    # cmd는 UNC 경로로 cd할 수 없으므로 넣지 않는다.
    $n = Prompts; Send 'c\\localhost\c$\Windows'; WaitFor { $false } 2 | Out-Null
    $results['cmd는 UNC cd 없음'] = (Prompts) -eq $n -and (Cwd) -eq $dirA.FullName
}
if ($Shell -eq 'gitbash') {
    # 사용자 .bashrc의 별칭이 살아 있고, 셸 통합이 직전 명령의 $?를 바꾸지 않는다.
    $n = Prompts; Send "isillalias`rfalse`recho rc=`$?`r"; WaitFor { (Prompts) -ge $n + 3 } | Out-Null
    $results['사용자 .bashrc·$? 유지'] = $script:text -match 'alias-ok' -and $script:text -match 'rc=1'
}

# M8: 끊긴 네트워크 경로의 폴더 확인(약 21 s)이 입력을 막지 않는다. 뒤의 c가 밀리므로 맨 끝에 둔다.
# 응답하지 않는 문서용 주소(192.0.2.0/24). Windows는 응답 없던 서버를 잠시 기억해 다음 확인이 바로 끝나므로, 항목마다 다른 주소를 쓴다.
function Get-DeadPath { '\\192.0.2.{0}\x' -f (Get-Random -Minimum 1 -Maximum 255) }
$ok = ([regex]::Matches($script:text, 'sill-ok')).Count
Send ('c' + (Get-DeadPath)); Start-Sleep -Milliseconds 100
$sw.Restart(); Send "i$($c.ok)`r"
$fast = WaitFor { ([regex]::Matches($script:text, 'sill-ok')).Count -gt $ok } 30
$info['끊긴 경로 뒤 입력'] = '{0:F0} ms' -f $sw.Elapsed.TotalMilliseconds
$results['끊긴 경로 뒤 입력(2 s 이하)'] = $fast -and $sw.Elapsed.TotalSeconds -le 2

$children = @(Get-CimInstance Win32_Process -Filter "ParentProcessId=$($hostProc.Id)" | ForEach-Object { $_.ProcessId })
# M6: 셸이 끝나면 Windows Terminal 모양의 종료 줄이 오고 sill-host도 끝난다.
$len = $script:text.Length; Send "iexit`r"
$results['종료 줄'] = WaitFor { $script:text.IndexOf('[process exited with code 0 (0x00000000)]', $len) -ge 0 } 10
# M9: 종료 줄 뒤에 종료 코드(e)가 온다. band는 0이면 탭을 닫는다.
$results['종료 코드 e0'] = WaitFor { $script:exitCode -eq '0' } 5
$pipe.Dispose()
$exited = $hostProc.WaitForExit(5000)
Start-Sleep -Milliseconds 500
$left = @($children | Where-Object { Get-Process -Id $_ -ErrorAction SilentlyContinue })
$results['정리'] = $exited -and $left.Count -eq 0

# M8 후속: 시작 폴더가 끊긴 네트워크 경로여도 셸이 곧 뜬다. 확인은 2초까지만 기다리고 홈에서 시작한다.
$name = 'sill-check-' + [guid]::NewGuid().ToString('N')
$hostProc = Start-Process "$PSScriptRoot\..\bin\sill-host.exe" "$name $Shell" -PassThru
$pipe = New-Object System.IO.Pipes.NamedPipeClientStream('.', $name, 'InOut', 'Asynchronous')
$pipe.Connect(5000)
$pipe.ReadMode = 'Message'
$script:text = ''; $script:atStart = $true; $script:t = $null
$sw.Restart()
Send ('c' + (Get-DeadPath)); Send 'r100,30'
$started = WaitFor { (Prompts) -gt 0 -and (Cwd) } 30
$info['끊긴 시작 폴더 → 첫 프롬프트'] = '{0:F0} ms' -f $sw.Elapsed.TotalMilliseconds
$results['끊긴 시작 폴더는 홈에서 곧 시작(5 s 이하)'] = $started -and $sw.Elapsed.TotalSeconds -le 5 -and (Cwd) -eq $env:USERPROFILE
$pipe.Dispose()
$hostProc.WaitForExit(5000) | Out-Null
Remove-Item $base -Recurse -Force

$info.GetEnumerator() | ForEach-Object { "{0}: {1}" -f $_.Key, $_.Value }
$results.GetEnumerator() | ForEach-Object { "{0}: {1}" -f $_.Key, $(if ($_.Value) { 'ok' } else { 'FAIL' }) }
if ($results.Values -notcontains $false) { 'PASS' } else { 'FAIL'; exit 1 }
