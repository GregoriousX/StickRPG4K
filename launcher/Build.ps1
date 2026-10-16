$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$sources = @('launcher\Core.cs','launcher\Launcher.cs','launcher\EscMenuBridge.cs','installer\GameSource.cs','installer\SteamIntegration.cs') | ForEach-Object { Join-Path $root $_ }
foreach($program in @(@('GameProgram','StickRPG4K.exe'), @('ConfigProgram','config.exe'))) {
    & $compiler /nologo /target:winexe /platform:anycpu /optimize+ ('/main:StickRpg4k.'+$program[0]) ('/out:'+(Join-Path $root $program[1])) ('/win32manifest:'+(Join-Path $PSScriptRoot 'app.manifest')) /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Web.Extensions.dll $sources
    if($LASTEXITCODE -ne 0) { throw 'Launcher compilation failed.' }
}
