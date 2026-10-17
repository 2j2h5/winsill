# register.ps1이 만든 HKCU 키를 모두 지운다. band 때문에 탐색기가 계속 죽으면 Win+R에서 이 스크립트를 실행한다.
$clsid = '{AE2C3550-FB10-4D93-B6E1-4FBED3C0AD90}'
Remove-Item "HKCU:\Software\Classes\CLSID\$clsid" -Recurse -ErrorAction SilentlyContinue
Remove-Item "HKCU:\Software\Microsoft\Internet Explorer\Explorer Bars\$clsid" -Recurse -ErrorAction SilentlyContinue
Remove-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Shell Extensions\Approved' -Name $clsid -ErrorAction SilentlyContinue
foreach ($cat in '{00021493-0000-0000-C000-000000000046}', '{00021494-0000-0000-C000-000000000046}') {
    Remove-Item "HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Discardable\PostSetup\Component Categories\$cat\Enum" -Recurse -ErrorAction SilentlyContinue
}
"unregistered $clsid"
