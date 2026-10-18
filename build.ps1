# bin\ 빌드. .NET SDK 없이 Windows에 들어 있는 csc.exe(C# 5)를 쓴다.
# 외부 의존성은 고정 버전을 deps\에 한 번만 받고, 받을 때 SHA-256으로 확인한다. 다르면 아무것도 건드리기 전에 멈춘다.
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$out = Join-Path $root 'bin'
$deps = Join-Path $root 'deps'

function Get-Dep([string]$name, [string]$url, [string]$sha256) {
    $dir = Join-Path $deps $name
    if (-not (Test-Path $dir)) {
        $tmp = "$dir.tmp"
        Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
        New-Item -ItemType Directory -Force $tmp | Out-Null
        Invoke-WebRequest -UseBasicParsing $url -OutFile "$tmp.pkg"
        if ((Get-FileHash "$tmp.pkg").Hash -ne $sha256) {
            Remove-Item $tmp, "$tmp.pkg" -Recurse -Force; throw "해시가 다름: $name"
        }
        tar -xf "$tmp.pkg" -C $tmp    # nupkg(zip)와 npm tgz를 모두 bsdtar로 푼다
        if ($LASTEXITCODE) { throw "압축 풀기 실패: $url" }
        Remove-Item "$tmp.pkg"
        Rename-Item $tmp $dir
    }
    $dir
}

$wv2 = Get-Dep 'webview2-1.0.4258.31' 'https://api.nuget.org/v3-flatcontainer/microsoft.web.webview2/1.0.4258.31/microsoft.web.webview2.1.0.4258.31.nupkg' '56F7F4B8BF9AEE4B8EFEFBBDD4F67D5F74EBD1B100ED0806DA71BF76AF481AA9'
$xterm = Get-Dep 'xterm-6.0.0' 'https://registry.npmjs.org/@xterm/xterm/-/xterm-6.0.0.tgz' '908E66E04AF6C8DC6B00DD3B54DE088E2E81E5ED866284FD6C2FB3C2D1C7A3F6'
$fit = Get-Dep 'addon-fit-0.11.0' 'https://registry.npmjs.org/@xterm/addon-fit/-/addon-fit-0.11.0.tgz' '26003B4517A132B64E4FF228FD88A5FDA3FFF5E606C76093F6DCFF772E9ECEC0'

$csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"

# 단위 시험. 실패하면 탐색기 창을 닫거나 bin을 건드리기 전에 멈춘다.
$test = Join-Path $env:TEMP 'sill-sync-test.exe'
& $csc /nologo /codepage:65001 /platform:x64 /out:"$test" "$root\tests\SyncTest.cs" "$root\src\Sill.Host\Sync.cs"
if ($LASTEXITCODE) { exit $LASTEXITCODE }
& $test
if ($LASTEXITCODE) { exit $LASTEXITCODE }
Remove-Item $test

# Session 단위 시험(보내기 큐, sill-host를 띄우지 못함). 자신을 가짜 sill-host로 띄운다. SILL_TEST: band.log에 쓰지 않음.
$test = Join-Path $env:TEMP 'sill-session-test.exe'
& $csc /nologo /codepage:65001 /platform:x64 /define:SILL_TEST /nowarn:169 /r:System.Windows.Forms.dll /out:"$test" "$root\tests\SessionTest.cs" "$root\src\Sill.Band\Session.cs" "$root\src\Shared.cs"
if ($LASTEXITCODE) { exit $LASTEXITCODE }
& $test
if ($LASTEXITCODE) { exit $LASTEXITCODE }
Remove-Item $test

New-Item -ItemType Directory -Force "$out\web" | Out-Null

# 탐색기가 DLL을 잡고 있으면 덮어쓸 수 없으므로 폴더 창 프로세스(/factory)만 종료한다. 작업 표시줄 explorer는 그대로 둔다.
# sill-host는 band가 사라지면 스스로 끝나지만, 남아 있으면 exe를 덮어쓸 수 없으므로 함께 정리한다.
Get-CimInstance Win32_Process -Filter "Name='explorer.exe' or Name='sill-host.exe'" |
    Where-Object { $_.Name -eq 'sill-host.exe' -or $_.CommandLine -match '/factory' } |
    ForEach-Object { Stop-Process -Id $_.ProcessId -Force; Wait-Process -Id $_.ProcessId -ErrorAction SilentlyContinue }

# SeparateProcess 이전에 연 창에서 band를 띄웠다면 작업 표시줄 explorer가 DLL을 잡고 있다.
# 사용 중인 파일도 이름은 바꿀 수 있으므로 옆으로 치우고, 지난번에 치운 것은 지울 수 있으면 지운다.
Remove-Item "$out\*.old" -ErrorAction SilentlyContinue
if (Test-Path "$out\Sill.Band.dll") { Move-Item "$out\Sill.Band.dll" ("$out\Sill.Band.{0:yyyyMMddHHmmss}.old" -f (Get-Date)) }

$core ="$wv2\lib\net462\Microsoft.Web.WebView2.Core.dll"
& $csc /nologo /codepage:65001 /target:library /platform:x64 /optimize `
    /out:"$out\Sill.Band.dll" /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:"$core" `
    (Get-ChildItem "$root\src\Sill.Band\*.cs").FullName "$root\src\Shared.cs"
if ($LASTEXITCODE) { exit $LASTEXITCODE }
& $csc /nologo /codepage:65001 /target:winexe /platform:x64 /optimize /win32icon:"$root\assets\sill.ico" `
    /out:"$out\sill-host.exe" /r:System.Web.Extensions.dll `
    (Get-ChildItem "$root\src\Sill.Host\*.cs").FullName "$root\src\Shared.cs"
if ($LASTEXITCODE) { exit $LASTEXITCODE }

Copy-Item $core, "$wv2\runtimes\win-x64\native\WebView2Loader.dll" $out
Copy-Item "$root\web\*", "$xterm\package\lib\xterm.js", "$xterm\package\css\xterm.css", "$fit\package\lib\addon-fit.js" "$out\web"

# 단축키 상주 프로세스(sill-host keys)도 위에서 함께 끝냈으므로, 등록되어 있으면 새 exe로 다시 띄운다.
if (Get-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name Sill -ErrorAction SilentlyContinue) { Start-Process "$out\sill-host.exe" keys }
