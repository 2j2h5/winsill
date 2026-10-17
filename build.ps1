# Sill.Band.dll 빌드. .NET SDK 없이 Windows에 들어 있는 csc.exe(C# 5)를 쓴다.
$ErrorActionPreference = 'Stop'
$out = Join-Path $PSScriptRoot 'bin'
New-Item -ItemType Directory -Force $out | Out-Null

# 탐색기가 DLL을 잡고 있으면 덮어쓸 수 없으므로 폴더 창 프로세스(/factory)만 종료한다. 작업 표시줄 explorer는 그대로 둔다.
Get-CimInstance Win32_Process -Filter "Name='explorer.exe'" |
    Where-Object { $_.CommandLine -match '/factory' } |
    ForEach-Object { Stop-Process -Id $_.ProcessId -Force; Wait-Process -Id $_.ProcessId -ErrorAction SilentlyContinue }

$csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
& $csc /nologo /codepage:65001 /target:library /platform:x64 /optimize `
    /out:"$out\Sill.Band.dll" /r:System.Windows.Forms.dll /r:System.Drawing.dll `
    (Get-ChildItem "$PSScriptRoot\src\Sill.Band\*.cs").FullName
exit $LASTEXITCODE
