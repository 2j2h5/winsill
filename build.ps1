# bin\ 빌드. .NET SDK 없이 Windows에 들어 있는 csc.exe(C# 5)를 쓴다.
# 외부 의존성은 고정 버전을 deps\에 한 번만 받는다(M1 설계 표 4).
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$out = Join-Path $root 'bin'
$deps = Join-Path $root 'deps'

function Get-Dep([string]$name, [string]$url) {
    $dir = Join-Path $deps $name
    if (-not (Test-Path $dir)) {
        $tmp = "$dir.tmp"
        Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
        New-Item -ItemType Directory -Force $tmp | Out-Null
        Invoke-WebRequest -UseBasicParsing $url -OutFile "$tmp.pkg"
        tar -xf "$tmp.pkg" -C $tmp    # nupkg(zip)와 npm tgz를 모두 bsdtar로 푼다
        if ($LASTEXITCODE) { throw "압축 풀기 실패: $url" }
        Remove-Item "$tmp.pkg"
        Rename-Item $tmp $dir
    }
    $dir
}

$wv2 = Get-Dep 'webview2-1.0.4258.31' 'https://api.nuget.org/v3-flatcontainer/microsoft.web.webview2/1.0.4258.31/microsoft.web.webview2.1.0.4258.31.nupkg'
$xterm = Get-Dep 'xterm-6.0.0' 'https://registry.npmjs.org/@xterm/xterm/-/xterm-6.0.0.tgz'
$fit = Get-Dep 'addon-fit-0.11.0' 'https://registry.npmjs.org/@xterm/addon-fit/-/addon-fit-0.11.0.tgz'

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

$csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$core = "$wv2\lib\net462\Microsoft.Web.WebView2.Core.dll"
& $csc /nologo /codepage:65001 /target:library /platform:x64 /optimize `
    /out:"$out\Sill.Band.dll" /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:"$core" `
    (Get-ChildItem "$root\src\Sill.Band\*.cs").FullName "$root\src\Shared.cs"
if ($LASTEXITCODE) { exit $LASTEXITCODE }
& $csc /nologo /codepage:65001 /target:winexe /platform:x64 /optimize `
    /out:"$out\sill-host.exe" `
    (Get-ChildItem "$root\src\Sill.Host\*.cs").FullName "$root\src\Shared.cs"
if ($LASTEXITCODE) { exit $LASTEXITCODE }

Copy-Item $core, "$wv2\runtimes\win-x64\native\WebView2Loader.dll" $out
Copy-Item "$root\web\*", "$xterm\package\lib\xterm.js", "$xterm\package\css\xterm.css", "$fit\package\lib\addon-fit.js" "$out\web"
