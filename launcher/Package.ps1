param([string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if(-not $OutputDirectory) { $OutputDirectory = Join-Path $root 'dist' }
$version = [Reflection.AssemblyName]::GetAssemblyName((Join-Path $root 'config.exe')).Version.ToString(3)
$manifest = Get-Content -LiteralPath (Join-Path $root 'patches\manifest.json') -Raw | ConvertFrom-Json
$files = @('README.md','config.exe','StickRPG4K.exe','Install.ps1','installer/GameSource.cs','installer/Patcher.cs','runtime/ruffle.exe','runtime/LICENSE.md','patches/manifest.json')
$files += @($manifest.payloads.PSObject.Properties.Name)
Add-Type -AssemblyName System.IO.Compression
[IO.Directory]::CreateDirectory($OutputDirectory) | Out-Null
$zipPath = Join-Path $OutputDirectory ('StickRPG4K-v' + $version + '.zip')
if(Test-Path -LiteralPath $zipPath) { throw 'Release ZIP already exists; refusing to overwrite it.' }
$stream = [IO.File]::Open($zipPath, [IO.FileMode]::CreateNew)
$archive = New-Object IO.Compression.ZipArchive($stream, [IO.Compression.ZipArchiveMode]::Create, $false)
try {
    foreach($relative in $files) {
        $source = [IO.Path]::GetFullPath((Join-Path $root $relative))
        if(-not $source.StartsWith([IO.Path]::GetFullPath($root).TrimEnd('\')+'\',[StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe release path.' }
        $entry = $archive.CreateEntry($relative.Replace('\','/'), [IO.Compression.CompressionLevel]::Optimal)
        $inputStream = [IO.File]::OpenRead($source)
        $outputStream = $entry.Open()
        try { $inputStream.CopyTo($outputStream) } finally { $inputStream.Dispose(); $outputStream.Dispose() }
    }
} finally { $archive.Dispose(); $stream.Dispose() }
$algorithm = [Security.Cryptography.SHA256]::Create()
$stream = [IO.File]::OpenRead($zipPath)
try { $hash = [BitConverter]::ToString($algorithm.ComputeHash($stream)).Replace('-','').ToLowerInvariant() }
finally { $stream.Dispose(); $algorithm.Dispose() }
[IO.File]::WriteAllText($zipPath+'.sha256', $hash+'  '+[IO.Path]::GetFileName($zipPath)+[Environment]::NewLine, [Text.Encoding]::ASCII)
Write-Output ('Release ZIP: '+$zipPath)
Write-Output ('SHA-256: '+$hash)
