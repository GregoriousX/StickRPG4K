param([string]$OriginalExe, [switch]$NoUpscale)

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

Add-Type -Path (Join-Path $root 'installer\Patcher.cs')
$upscale = -not $NoUpscale
# Expected hashes for the chosen variant; -NoUpscale keeps the original bitmaps
# wherever a recipe step is marked option=upscale.
function Expected($file) {
    $plain = $null
    if(-not $upscale -and $file.PSObject.Properties['variants'] -and $file.variants.PSObject.Properties['no-upscale']) { $plain = $file.variants.'no-upscale' }
    if($null -ne $plain) {
        $hq = $null; if($plain.PSObject.Properties['soundtrack_sha256']) { $hq = $plain.soundtrack_sha256 }
        return @{ sha256 = $plain.sha256; header = $plain.header; hq = $hq }
    }
    $hq = $null; if($file.PSObject.Properties['soundtrack']) { $hq = $file.soundtrack.sha256 }
    return @{ sha256 = $file.sha256; header = $file.header; hq = $hq }
}
function KnownHashes($file) {
    $values = @($file.sha256)
    if($file.PSObject.Properties['soundtrack']) { $values += $file.soundtrack.sha256 }
    if($file.PSObject.Properties['variants']) {
        foreach($variant in $file.variants.PSObject.Properties) {
            $values += $variant.Value.sha256
            if($variant.Value.PSObject.Properties['soundtrack_sha256']) { $values += $variant.Value.soundtrack_sha256 }
        }
    }
    return $values
}
function UpdateSoundtrack([string]$folder, [string]$previousFolder) {
    foreach($file in $manifest.files) {
        if(-not $file.PSObject.Properties['soundtrack']) { continue }
        $target = LocalPath $folder $file.path
        $expected = Expected $file
        $currentHash = Hash $target
        if($currentHash -eq $expected.hq) {
            Write-Output ('Album audio already installed: ' + $file.path)
            continue
        }
        if($currentHash -ne $expected.sha256) { throw ('Unexpected generated game file: ' + $file.path) }
        $available = $true
        $checked = @{}
        foreach($replacement in $file.soundtrack.replacements) {
            if($checked.ContainsKey($replacement.source)) { continue }
            $source = LocalPath $root $replacement.source
            if(-not (Test-Path -LiteralPath $source -PathType Leaf) -or (Hash $source) -ne $replacement.sha256) { $available = $false; break }
            $checked[$replacement.source] = $true
        }
        $previousSounds = $null
        if(-not $available -and $previousFolder) {
            # Album sound tags are identical in every known variant and version.
            $previousKnown = @(KnownHashes $file)
            if($file.soundtrack.PSObject.Properties['previous_sha256']) { $previousKnown += $file.soundtrack.previous_sha256 }
            $previousPath = LocalPath $previousFolder $file.path
            if((Test-Path -LiteralPath $previousPath) -and $previousKnown -contains (Hash $previousPath)) {
                $previousSounds = @{}
                foreach($sound in [StickRpg4kPatch]::ReadTags($previousPath)) {
                    if($sound.Code -eq 14) { $previousSounds[[int][BitConverter]::ToUInt16($sound.Data,0)] = $sound }
                }
            }
        }
        if(-not $available -and $null -eq $previousSounds) {
            Write-Output ('Keeping embedded audio in ' + $file.path + ' (matching Steam Soundtrack files are not all present).')
            continue
        }
        if($null -ne $previousSounds) { Write-Output ('Reusing verified album audio from the previous game: ' + $file.path) }
        else { Write-Output ('Installing original-quality Soundtrack excerpts: ' + $file.path) }
        $tags = [StickRpg4kPatch]::ReadTags($target)
        foreach($replacement in $file.soundtrack.replacements) {
            $index = [int]$replacement.index
            if($tags[$index].Code -ne 14 -or [BitConverter]::ToUInt16($tags[$index].Data,0) -ne $replacement.id) { throw 'Unexpected soundtrack sound ID.' }
            if($null -ne $previousSounds) { $tags[$index] = $previousSounds[[int]$replacement.id] }
            else { $tags[$index] = [StickRpg4kPatch]::SoundtrackSound($tags[$index], (LocalPath $root $replacement.source), [int]$replacement.start_sample) }
        }
        $temporary = $target + '.sr4k-' + [Guid]::NewGuid().ToString('N')
        try {
            [StickRpg4kPatch]::WriteResizedSwf($temporary, [Convert]::FromBase64String($expected.header), $tags)
            if((Hash $temporary) -ne $expected.hq) { throw ('Soundtrack checksum mismatch: ' + $file.path) }
            [IO.File]::Replace($temporary, $target, [NullString]::Value)
        } finally {
            if(Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary -Force }
        }
    }
}

$gameFolder = Join-Path $root 'game'
$stampPath = Join-Path $gameFolder '.installed.json'
if(Test-Path -LiteralPath $stampPath) {
    $stamp = Get-Content -LiteralPath $stampPath -Raw | ConvertFrom-Json
    $installedUpscale = -not $stamp.PSObject.Properties['upscale'] -or [bool]$stamp.upscale
    $ready = $stamp.version -eq $manifest.version -and $installedUpscale -eq $upscale
    foreach($file in $manifest.files) {
        $path = LocalPath $gameFolder $file.path
        if(-not (Test-Path -LiteralPath $path)) { $ready = $false; break }
        $currentHash = Hash $path
        $expected = Expected $file
        if($currentHash -ne $expected.sha256 -and $currentHash -ne $expected.hq) { $ready = $false; break }
    }
    if($ready) { UpdateSoundtrack $gameFolder; exit 0 }
}

Add-Type -Path (Join-Path $root 'installer\GameSource.cs')
$candidates = @()
if($OriginalExe) {
    $OriginalExe = [StickRpg4k.SourceDiscovery]::ResolveOriginal($OriginalExe)
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
if([IO.Path]::GetFullPath($original.DirectoryName).TrimEnd('\') -ne [IO.Path]::GetFullPath($root).TrimEnd('\')) {
    throw 'Extract the complete release ZIP next to the original game EXE and run config.exe in that folder.'
}
if(Test-Path -LiteralPath $gameFolder) {
    # Upgrade only an intact, recognized generated game. User saves live outside
    # this folder; unexpected files or edited assets prevent any replacement.
    if(((Get-Item -LiteralPath $gameFolder -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'Cannot upgrade a linked game folder.' }
    if(-not (Test-Path -LiteralPath $stampPath)) { throw 'An incomplete or older game\ folder exists. Rename that generated folder, then run config.exe again. Your saves are in saves\.' }
    $oldStamp = Get-Content -LiteralPath $stampPath -Raw | ConvertFrom-Json
    $compatible = $null
    if($oldStamp.version -eq $manifest.version) {
        # Same version, other option: accept any variant of the current build.
        $current = New-Object PSObject
        foreach($file in $manifest.files) { $current | Add-Member -NotePropertyName $file.path -NotePropertyValue @(KnownHashes $file) }
        $compatible = New-Object PSObject -Property @{ Value = $current }
    } elseif($manifest.PSObject.Properties['upgrade_from']) { $compatible = $manifest.upgrade_from.PSObject.Properties[$oldStamp.version] }
    if($null -eq $compatible) { throw 'This generated game version cannot be upgraded automatically. Rename game\ and run config.exe again. Your saves are in saves\.' }
    $expectedFiles = @('.installed.json') + @($manifest.files.path)
    $gamePrefix = [IO.Path]::GetFullPath($gameFolder).TrimEnd('\') + '\'
    foreach($entry in Get-ChildItem -LiteralPath $gameFolder -Recurse -Force) {
        if(($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'Cannot upgrade a game folder containing links.' }
        if(-not $entry.PSIsContainer) {
            $relative = $entry.FullName.Substring($gamePrefix.Length).Replace('\','/')
            if($expectedFiles -notcontains $relative) { throw ('Unexpected file in generated game folder: ' + $relative) }
        }
    }
    foreach($file in $manifest.files) {
        $path = LocalPath $gameFolder $file.path
        # Files missing from the old build (e.g. newly packaged ones) are simply regenerated.
        if(-not (Test-Path -LiteralPath $path)) { continue }
        $accepted = $compatible.Value.PSObject.Properties[$file.path]
        if($null -eq $accepted -or @($accepted.Value) -notcontains (Hash $path)) { throw ('Modified or incomplete generated game file: ' + $file.path) }
    }
    Write-Output 'Verified the previous generated game; preparing an automatic upgrade.'
}
if($manifest.format -ne 1) { throw 'Unsupported package version.' }
Write-Output 'Checking prebuilt assets and patches...'
foreach($payload in $manifest.payloads.PSObject.Properties) {
    $path = LocalPath $root $payload.Name
    if((Hash $path) -ne $payload.Value) { throw ('Package checksum mismatch: ' + $payload.Name) }
}

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
        if($file.copy -and $file.PSObject.Properties['replace']) {
            # Small text settings: copy with exact, verified value substitutions.
            $text = [IO.File]::ReadAllText($source, [Text.Encoding]::GetEncoding(28591))
            foreach($pair in $file.replace) {
                if(-not $text.Contains($pair[0])) { throw ('Unexpected original text in ' + $file.path) }
                $text = $text.Replace($pair[0], $pair[1])
            }
            [IO.File]::WriteAllText($target, $text, [Text.Encoding]::GetEncoding(28591))
        } elseif($file.copy) {
            [IO.File]::Copy($source, $target)
        } else {
            $oldTags = [StickRpg4kPatch]::ReadTags($source)
            $newTags = New-Object 'System.Collections.Generic.List[SR4KTag]'
            foreach($step in $file.tags) {
                if($step -is [ValueType]) {
                    $tag = $oldTags[[int]$step]
                } elseif(-not $upscale -and $step.PSObject.Properties['option'] -and $step.option -eq 'upscale') {
                    $tag = $oldTags[[int]$step.original]
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
            [StickRpg4kPatch]::WriteSwf($target, [Convert]::FromBase64String((Expected $file).header), $newTags)
        }
        if((Hash $target) -ne (Expected $file).sha256) { throw ('Rebuilt file checksum mismatch: ' + $file.path) }
    }
    UpdateSoundtrack $staging $gameFolder
    @{version=$manifest.version; upscale=$upscale; original_exe_sha256=$manifest.original_exe_sha256; source_exe_path=$original.Name} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $staging '.installed.json') -Encoding UTF8
    if(Test-Path -LiteralPath $gameFolder) {
        $previousGame = LocalPath $work 'previous-game'
        Move-Item -LiteralPath $gameFolder -Destination $previousGame
        try { Move-Item -LiteralPath $staging -Destination $gameFolder }
        catch { Move-Item -LiteralPath $previousGame -Destination $gameFolder; throw }
    } else { Move-Item -LiteralPath $staging -Destination $gameFolder }
    Write-Output 'Mod files prepared.'
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
