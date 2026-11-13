# Compile/run feature regression tests without registering a shell extension.
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$test = Join-Path $env:TEMP ('sill-feature-' + [Guid]::NewGuid().ToString('N') + '.exe')
try {
    & "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /codepage:65001 /platform:x64 /define:SILL_TEST /out:"$test" /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll "$root\tests\FeatureTest.cs" "$root\src\Preferences.cs" "$root\src\Shared.cs" "$root\src\Sill.Host\TerminalLauncher.cs" "$root\src\Sill.Band\RightDock.cs" "$root\src\Sill.Band\Interop.cs"
    if ($LASTEXITCODE) { exit $LASTEXITCODE }
    & $test
    if ($LASTEXITCODE) { exit $LASTEXITCODE }
} finally { Remove-Item -LiteralPath $test -ErrorAction SilentlyContinue }
