# 설치 프로그램 dist\Sill-Setup-<버전>.exe를 만든다(M5). bin\은 build.ps1로 먼저 빌드해 둔다.
# Inno Setup 컴파일러는 다른 의존성처럼 deps\에 한 번 받는다. 공식 설치 파일을 해시로 확인한 뒤 휴대용(/PORTABLE=1)으로 풀어
# 관리자 권한도, 앱 목록·파일 연결 같은 흔적도 남기지 않는다.
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$inno = Join-Path $root 'deps\innosetup-7.1.0'
$iscc = Join-Path $inno 'ISCC.exe'

if (-not (Test-Path $iscc)) {
    $exe = Join-Path $root 'deps\innosetup-7.1.0-x64.exe'
    Invoke-WebRequest -UseBasicParsing 'https://github.com/jrsoftware/issrc/releases/download/is-7_1_0/innosetup-7.1.0-x64.exe' -OutFile $exe
    if ((Get-FileHash $exe).Hash -ne '0362A383ED217D4C4239B5933866DD96D3EB2102737DA92F80F6057A4B40DF2F') {
        Remove-Item $exe; throw 'Inno Setup 설치 파일의 해시가 다름'
    }
    $p = Start-Process $exe '/VERYSILENT', '/SUPPRESSMSGBOXES', '/CURRENTUSER', '/PORTABLE=1', '/NOICONS', "/DIR=`"$inno`"" -PassThru -Wait
    Remove-Item $exe
    if ($p.ExitCode -or -not (Test-Path $iscc)) { throw "Inno Setup 풀기 실패: $($p.ExitCode)" }
}

foreach ($f in 'Sill.Band.dll', 'sill-host.exe', 'Microsoft.Web.WebView2.Core.dll', 'WebView2Loader.dll', 'web\index.html') {
    if (-not (Test-Path "$root\bin\$f")) { throw "bin\$f 없음: build.ps1을 먼저 실행" }
}
& $iscc /Qp "$root\installer\sill.iss"
if ($LASTEXITCODE) { exit $LASTEXITCODE }
Get-ChildItem "$root\dist\Sill-Setup-*.exe" | Sort-Object LastWriteTime | Select-Object -Last 1 | ForEach-Object { '{0} ({1:N0} bytes)' -f $_.FullName, $_.Length }
