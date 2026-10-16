param([string]$OriginalExe)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$manifest = Get-Content -LiteralPath (Join-Path $root 'patches\manifest.json') -Raw | ConvertFrom-Json

function LocalPath([string]$base, [string]$relative) {
    $baseFull = [IO.Path]::GetFullPath($base).TrimEnd('\') + '\'
    $full = [IO.Path]::GetFullPath((Join-Path $base $relative))
    if(-not $full.StartsWith($baseFull, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'A package path points outside its destination.'
    }
    return $full
}
function Hash([string]$path) {
    $algorithm = [Security.Cryptography.SHA256]::Create()
    $stream = [IO.File]::OpenRead($path)
    try { return [BitConverter]::ToString($algorithm.ComputeHash($stream)).Replace('-','').ToLowerInvariant() }
    finally { $stream.Dispose(); $algorithm.Dispose() }
}

$gameFolder = Join-Path $root 'game'
$stampPath = Join-Path $gameFolder '.installed.json'
if(Test-Path -LiteralPath $stampPath) {
    $stamp = Get-Content -LiteralPath $stampPath -Raw | ConvertFrom-Json
    $ready = $stamp.version -eq $manifest.version
    foreach($file in $manifest.files) {
        $path = LocalPath $gameFolder $file.path
        if(-not (Test-Path -LiteralPath $path) -or (Hash $path) -ne $file.sha256) { $ready = $false; break }
    }
    if($ready) { exit 0 }
}

Add-Type -Path (Join-Path $root 'installer\GameSource.cs')
$candidates = @()
if($OriginalExe) {
    if(-not (Test-Path -LiteralPath $OriginalExe -PathType Leaf)) { throw 'The selected original game EXE does not exist.' }
    $candidates = @((Get-Item -LiteralPath $OriginalExe))
} else {
    $candidates = @([StickRpg4k.SourceDiscovery]::FindCandidates($root) | ForEach-Object { Get-Item -LiteralPath $_.Path })
}
$original = $null
foreach($candidate in $candidates) {
    if((Hash $candidate.FullName) -eq $manifest.original_exe_sha256) { $original = $candidate; break }
}
if($null -eq $original) {
    if($candidates.Count -eq 0) {
        throw "Install Stick RPG 2 on Steam, or choose your original Stick RPG 2 Director's Cut.exe in the launcher."
    }
    throw ('The supplied EXE is not the supported original Director''s Cut build. Expected SHA-256: ' + $manifest.original_exe_sha256)
}
if(Test-Path -LiteralPath $gameFolder) {
    throw 'An incomplete or older game\ folder exists. Rename that generated folder, then run Play.bat again. Your saves are in saves\.'
}
if($manifest.format -ne 1) { throw 'Unsupported package version.' }
Write-Output 'Checking prebuilt assets and patches...'
foreach($payload in $manifest.payloads.PSObject.Properties) {
    $path = LocalPath $root $payload.Name
    if((Hash $path) -ne $payload.Value) { throw ('Package checksum mismatch: ' + $payload.Name) }
}
Add-Type -Path (Join-Path $root 'installer\Patcher.cs')

$workRoot = Join-Path $root '.install-work'
$work = LocalPath $workRoot ([Guid]::NewGuid().ToString('N'))
$extraction = Join-Path $work 'extracted'
$bases = Join-Path $work 'original-files'
$staging = Join-Path $work 'rebuilt'
New-Item -ItemType Directory -Path $extraction,$bases,$staging -Force | Out-Null
$process = $null
try {
    Write-Output 'Extracting your original game (it runs briefly in the background)...'
    $startInfo = New-Object Diagnostics.ProcessStartInfo
    $startInfo.FileName = $original.FullName
    $startInfo.WorkingDirectory = [IO.Path]::GetDirectoryName($original.FullName)
    $startInfo.UseShellExecute = $false
    $startInfo.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
    $startInfo.EnvironmentVariables['TEMP'] = $extraction
    $startInfo.EnvironmentVariables['TMP'] = $extraction
    $process = [Diagnostics.Process]::Start($startInfo)
    $deadline = [DateTime]::UtcNow.AddSeconds(45)
    $found = @{}
    do {
        Start-Sleep -Milliseconds 500
        $found.Clear()
        foreach($entry in Get-ChildItem -LiteralPath $extraction -Recurse -File) { $found[$entry.Name] = $entry }
        $complete = $true
        foreach($needed in $manifest.sources.PSObject.Properties) {
            if(-not $found.ContainsKey($needed.Name) -or $found[$needed.Name].Length -ne $needed.Value.bytes) { $complete = $false; break }
        }
        if($complete) {
            try {
                foreach($needed in $manifest.sources.PSObject.Properties) {
                    if((Hash $found[$needed.Name].FullName) -ne $needed.Value.sha256) { $complete = $false; break }
                }
            } catch { $complete = $false }
        }
        if($complete) { break }
        if($process.HasExited) { throw 'The original game exited before its files could be extracted.' }
    } while([DateTime]::UtcNow -lt $deadline)
    if(-not $complete) { throw 'Timed out extracting the original game files.' }
    foreach($needed in $manifest.sources.PSObject.Properties) {
        $target = LocalPath $bases $needed.Name
        [IO.File]::Copy($found[$needed.Name].FullName, $target)
        if((Hash $target) -ne $needed.Value.sha256) { throw 'Original file checksum mismatch.' }
    }
    if(-not $process.HasExited) { $process.Kill(); $process.WaitForExit(5000) | Out-Null }
    $process.Dispose()
    $process = $null

    Write-Output 'Applying ready-made 4K assets and code patches...'
    foreach($file in $manifest.files) {
        $source = LocalPath $bases $file.source
        $target = LocalPath $staging $file.path
        [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($target)) | Out-Null
        if($file.copy) {
            [IO.File]::Copy($source, $target)
        } else {
            $oldTags = [StickRpg4kPatch]::ReadTags($source)
            $newTags = New-Object 'System.Collections.Generic.List[SR4KTag]'
            foreach($step in $file.tags) {
                if($step -is [ValueType]) {
                    $tag = $oldTags[[int]$step]
                } elseif($step.PSObject.Properties['asset']) {
                    $bytes = [StickRpg4kPatch]::Asset((LocalPath $root $step.asset), [long]$step.offset, [int]$step.length)
                    $tag = New-Object SR4KTag ([int]$step.code),$bytes
                } elseif($step.PSObject.Properties['abc']) {
                    $prefix = [Convert]::FromBase64String($step.prefix)
                    $tag = [StickRpg4kPatch]::PatchAbc($oldTags[[int]$step.abc], $prefix, (LocalPath $root $step.delta))
                } elseif($step.PSObject.Properties['addon']) {
                    $prefix = [Convert]::FromBase64String($step.prefix)
                    $tag = [StickRpg4kPatch]::Addon($prefix, [IO.File]::ReadAllBytes((LocalPath $root $step.addon)))
                } else { throw 'Unknown patch record.' }
                $newTags.Add($tag)
            }
            [StickRpg4kPatch]::WriteSwf($target, [Convert]::FromBase64String($file.header), $newTags)
        }
        if((Hash $target) -ne $file.sha256) { throw ('Rebuilt file checksum mismatch: ' + $file.path) }
    }
    @{version=$manifest.version; original_exe_sha256=$manifest.original_exe_sha256; source_exe_path=$original.FullName} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $staging '.installed.json') -Encoding UTF8
    Move-Item -LiteralPath $staging -Destination $gameFolder
    Write-Output 'Installed. Starting StickRPG4K.'
} finally {
    if($null -ne $process) {
        if(-not $process.HasExited) { $process.Kill(); $process.WaitForExit(5000) | Out-Null }
        $process.Dispose()
    }
    $checkedWork = [IO.Path]::GetFullPath($work)
    $checkedRoot = [IO.Path]::GetFullPath($workRoot).TrimEnd('\') + '\'
    if(-not $checkedWork.StartsWith($checkedRoot,[StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe cleanup path.' }
    if(Test-Path -LiteralPath $checkedWork) { Remove-Item -LiteralPath $checkedWork -Recurse -Force }
}
