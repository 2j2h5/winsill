# 열린 탐색기 창(탭)마다 IWebBrowser2::ShowBrowserBar로 Sill 패널을 켜거나 끈다.
param([ValidateSet('on', 'off')] [string]$State = 'on')
$clsid = '{AE2C3550-FB10-4D93-B6E1-4FBED3C0AD90}'
foreach ($w in @((New-Object -ComObject Shell.Application).Windows())) {
    if ($w.FullName -notlike '*\explorer.exe') { continue }
    try {
        $w.ShowBrowserBar($clsid, ($State -eq 'on'), $null)
        "{0} {1}" -f $State, $w.LocationURL
    } catch {
        "fail {0}: {1}" -f $w.LocationURL, $_.Exception.Message
    }
}
