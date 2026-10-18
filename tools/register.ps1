# 관리자 권한 없이 HKCU에만 SillBand를 가로형 Explorer Bar로 등록한다.
# HKCR에 쓰면 실제로는 HKLM에 저장되므로(관리자 필요) HKCU\Software\Classes에 직접 쓴다.
$ErrorActionPreference = 'Stop'
$clsid = '{AE2C3550-FB10-4D93-B6E1-4FBED3C0AD90}'
$dll = (Resolve-Path "$PSScriptRoot\..\bin\Sill.Band.dll").Path
# .NET Framework는 CodeBase 경로의 #을 적재하지 못한다(%23도 안 됨, M5 시험)
if ($dll.Contains('#')) { throw "Sill can't be registered from a path that contains '#': $dll" }

$key = "HKCU:\Software\Classes\CLSID\$clsid"
New-Item $key -Value 'Sill' -Force | Out-Null
New-Item "$key\InprocServer32" -Value 'mscoree.dll' -Force | Out-Null
$values = @{
    ThreadingModel = 'Apartment'
    Class          = 'Sill.Band.SillBand'
    Assembly       = 'Sill.Band, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null'
    RuntimeVersion = 'v4.0.30319'
    # regasm /codebase와 같은 꼴. [Uri].AbsoluteUri처럼 한글을 %XX로 바꾸면 탐색기가 적재하지 못한다(M5 시험)
    CodeBase       = 'file:///' + $dll.Replace('\', '/')
}
foreach ($v in $values.GetEnumerator()) { Set-ItemProperty "$key\InprocServer32" -Name $v.Key -Value $v.Value }
# CATID_CommBand = 가로형 Explorer Bar
New-Item "$key\Implemented Categories\{00021494-0000-0000-C000-000000000046}" -Force | Out-Null

# '승인된 셸 확장만 허용' 정책이 켜진 PC 대비
$approved = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Shell Extensions\Approved'
if (-not (Test-Path $approved)) { New-Item $approved | Out-Null }
Set-ItemProperty $approved -Name $clsid -Value 'Sill'

# 기본 높이 240px (BarSize: 앞 4바이트 = 픽셀, 리틀 엔디언). 사용자가 바꾼 높이는 덮지 않는다(M5).
# 등록 키는 installer\sill.iss와 같아야 한다. 한쪽을 바꾸면 다른 쪽도 바꾼다.
$bar = "HKCU:\Software\Microsoft\Internet Explorer\Explorer Bars\$clsid"
if (-not (Get-ItemProperty $bar -Name BarSize -ErrorAction SilentlyContinue)) {
    New-Item $bar -Force | Out-Null
    Set-ItemProperty $bar -Name BarSize -Type Binary -Value ([byte[]](0xF0, 0, 0, 0, 0, 0, 0, 0))
}

# 탐색기가 캐시한 Explorer Bar 목록을 지워 새 바를 인식하게 한다
foreach ($cat in '{00021493-0000-0000-C000-000000000046}', '{00021494-0000-0000-C000-000000000046}') {
    Remove-Item "HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Discardable\PostSetup\Component Categories\$cat\Enum" -Recurse -ErrorAction SilentlyContinue
}
# 단축키 상주 프로세스(로그온 때 시작)와 폴더 빈 곳의 오른쪽 클릭 메뉴(M6)
$hostExe = (Resolve-Path "$PSScriptRoot\..\bin\sill-host.exe").Path
Set-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name Sill -Value "`"$hostExe`" keys"
$menu = 'HKCU:\Software\Classes\Directory\Background\shell\Sill'
New-Item $menu -Force | Out-Null
Set-ItemProperty $menu -Name MUIVerb -Value 'Open in Sill'
# sill-host.exe에 넣은 assets\sill.ico(M7). 번호(,0) 대신 리소스 ID(,-32512)로 가리킨다.
# Windows 아이콘 캐시는 "경로,번호"로 그림을 기억해서, 개발 중 아이콘을 바꾼 뒤에도 예전 그림이 남았다(M7).
Set-ItemProperty $menu -Name Icon -Value "`"$hostExe`",-32512"
New-Item "$menu\command" -Value "`"$hostExe`" open `"%V`"" -Force | Out-Null
Start-Process $hostExe keys
"registered $clsid -> $dll"
