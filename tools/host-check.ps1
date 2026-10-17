# 탐색기 없이 sill-host를 띄워 확인한다: 크기·입력을 보내면 셸 출력이 오고, 파이프를 닫으면 셸까지 정리되는가.
$ErrorActionPreference = 'Stop'
$name = 'sill-check-' + [guid]::NewGuid().ToString('N')
$hostProc = Start-Process "$PSScriptRoot\..\bin\sill-host.exe" $name -PassThru
$pipe = New-Object System.IO.Pipes.NamedPipeClientStream('.', $name, 'InOut', 'Asynchronous')
$pipe.Connect(5000)
$pipe.ReadMode = 'Message'
function Send([string]$s) { $b = [Text.Encoding]::UTF8.GetBytes($s); $pipe.Write($b, 0, $b.Length) }

Send 'r100,30'
# 입력 에코에는 'sill-ok'가 그대로 나오지 않고, 실행 결과에만 나온다.
Send "iWrite-Output ('sill' + '-ok')`r"
$text = ''
$buf = New-Object byte[] 65536
$deadline = (Get-Date).AddSeconds(15)
$t = $null
while ($text -notmatch 'sill-ok' -and (Get-Date) -lt $deadline) {
    if ($t -eq $null) { $t = $pipe.ReadAsync($buf, 0, $buf.Length) }
    if (-not $t.Wait(1000)) { continue }
    $text += [Text.Encoding]::UTF8.GetString($buf, 0, $t.Result)
    $t = $null
}
$children = @(Get-CimInstance Win32_Process -Filter "ParentProcessId=$($hostProc.Id)" | ForEach-Object { $_.ProcessId })
$pipe.Dispose()
$exited = $hostProc.WaitForExit(5000)
Start-Sleep -Milliseconds 500
$left = @($children | Where-Object { Get-Process -Id $_ -ErrorAction SilentlyContinue })

$ok = ($text -match 'sill-ok') -and $exited -and $left.Count -eq 0
"출력에 sill-ok: {0}, sill-host 종료: {1}, 자식 {2}개 중 남은 것 {3}개" -f ($text -match 'sill-ok'), $exited, $children.Count, $left.Count
if ($ok) { 'PASS' } else { 'FAIL'; exit 1 }
