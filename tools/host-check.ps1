# 탐색기 없이 sill-host를 띄워 확인한다.
# M1: 크기·입력을 보내면 셸 출력이 오고, 파이프를 닫으면 셸까지 정리되는가.
# M2: c(탐색기 폴더)를 셸 상태에 맞게 처리하는가. 시작 폴더, 대기 중 cd, 실행 중·입력 중 건너뜀, 빠른 연속 이동.
$ErrorActionPreference = 'Stop'
$base = Join-Path $env:TEMP ('sill-check-' + [guid]::NewGuid().ToString('N'))
$dirA = New-Item -ItemType Directory "$base\a"
$dirB = New-Item -ItemType Directory "$base\it's [x] 한글 `$x #b"
$name = 'sill-check-' + [guid]::NewGuid().ToString('N')
$hostProc = Start-Process "$PSScriptRoot\..\bin\sill-host.exe" $name -PassThru
$pipe = New-Object System.IO.Pipes.NamedPipeClientStream('.', $name, 'InOut', 'Asynchronous')
$pipe.Connect(5000)
$pipe.ReadMode = 'Message'
function Send([string]$s) { $b = [Text.Encoding]::UTF8.GetBytes($s); $pipe.Write($b, 0, $b.Length) }

# 받은 출력에서 메시지 머리글자 o를 떼고 이어 붙인다(OSC가 메시지 경계에서 나뉠 수 있다).
$script:text = ''; $script:atStart = $true; $script:t = $null
$buf = New-Object byte[] 65536
function WaitFor([scriptblock]$cond, [int]$sec = 20) {
    $deadline = (Get-Date).AddSeconds($sec)
    while (-not (& $cond) -and (Get-Date) -lt $deadline) {
        if ($script:t -eq $null) { $script:t = $pipe.ReadAsync($buf, 0, $buf.Length) }
        if (-not $script:t.Wait(500)) { continue }
        $chunk = [Text.Encoding]::UTF8.GetString($buf, 0, $script:t.Result)
        if ($script:atStart -and $chunk.Length) { $chunk = $chunk.Substring(1) }
        $script:atStart = $pipe.IsMessageComplete
        $script:text += $chunk
        $script:t = $null
    }
    [bool](& $cond)
}
function Cwd { $m = [regex]::Matches($script:text, "\x1b\]9;9;([^\x07\x1b]*)"); if ($m.Count) { $m[$m.Count - 1].Groups[1].Value } }
function Prompts { [regex]::Matches($script:text, "\x1b\]133;B").Count }
# 프롬프트 수는 보내기 전에 세어 둔다. 보낸 뒤에 세면 그사이 온 프롬프트를 놓친다.
function WaitPrompt([int]$n) { WaitFor { (Prompts) -gt $n } | Out-Null }
$results = [ordered]@{}

Send "c$dirA"      # 셸 시작 전: 시작 폴더가 된다
Send 'r100,30'
$results['시작 폴더'] = WaitFor { (Cwd) -eq $dirA.FullName -and (Prompts) -gt 0 } 30
Start-Sleep 1      # 프로필이 프롬프트를 두 번 그리는 경우까지 기다린다

# 입력 에코에는 'sill-ok'가 그대로 나오지 않고, 실행 결과에만 나온다.
$n = Prompts; Send "iWrite-Output ('sill' + '-ok')`r"; WaitPrompt $n
$results['입출력'] = $script:text -match 'sill-ok'

$n = Prompts; Send "c$dirB"; WaitPrompt $n
$results['대기 중 cd (특수 문자 경로)'] = (Cwd) -eq $dirB.FullName

$n = Prompts; Send "iStart-Sleep 2`r"
Start-Sleep -Milliseconds 300
Send "c$dirA"; WaitPrompt $n
$results['실행 중 건너뜀'] = (Cwd) -eq $dirB.FullName

Send 'iecho typed'
Start-Sleep -Milliseconds 300
Send "c$dirA"
Start-Sleep -Milliseconds 500
$n = Prompts; Send "i`r"; WaitPrompt $n
# cd가 끼어들었다면 실행된 줄이 "echo typedcd -LiteralPath ..."가 된다.
$results['입력 중 건너뜀'] = ((Cwd) -eq $dirB.FullName) -and ($script:text -notmatch 'typedcd')

# A로 가는 cd가 도는 사이 B가 오면, 보류했다가 다음 프롬프트에서 B로 간다.
Send "c$dirA"; Send "c$dirB"
WaitFor { (Cwd) -eq $dirB.FullName } 10 | Out-Null
WaitFor { $false } 1 | Out-Null
$results['빠른 연속 이동'] = (Cwd) -eq $dirB.FullName

$children = @(Get-CimInstance Win32_Process -Filter "ParentProcessId=$($hostProc.Id)" | ForEach-Object { $_.ProcessId })
$pipe.Dispose()
$exited = $hostProc.WaitForExit(5000)
Start-Sleep -Milliseconds 500
$left = @($children | Where-Object { Get-Process -Id $_ -ErrorAction SilentlyContinue })
$results['정리'] = $exited -and $left.Count -eq 0
Remove-Item $base -Recurse -Force

$results.GetEnumerator() | ForEach-Object { "{0}: {1}" -f $_.Key, $(if ($_.Value) { 'ok' } else { 'FAIL' }) }
if ($results.Values -notcontains $false) { 'PASS' } else { 'FAIL'; exit 1 }
