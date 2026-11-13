# Theme resolver fixtures never edit the user's Windows Terminal settings.
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$test = Join-Path $env:TEMP ('sill-appearance-' + [Guid]::NewGuid().ToString('N') + '.exe')
try {
    & "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /codepage:65001 /platform:x64 /out:"$test" /r:System.Web.Extensions.dll "$root\tests\AppearanceTest.cs" "$root\src\Sill.Band\TerminalAppearance.cs"
    if ($LASTEXITCODE) { exit $LASTEXITCODE }
    & $test "$root\web\terminal-schemes.json"
    if ($LASTEXITCODE) { exit $LASTEXITCODE }
} finally { Remove-Item -LiteralPath $test -ErrorAction SilentlyContinue }
