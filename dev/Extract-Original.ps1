param([Parameter(Mandatory=$true)][string]$OriginalExe)
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$manifest=Get-Content -LiteralPath (Join-Path $root 'patches\manifest.json') -Raw | ConvertFrom-Json
if((Get-FileHash -LiteralPath $OriginalExe).Hash.ToLowerInvariant() -ne $manifest.original_exe_sha256){throw 'Not the supported original EXE.'}
$original=(Get-Item -LiteralPath $OriginalExe).FullName
$local=Join-Path $PSScriptRoot 'local'
$output=Join-Path $local 'original'
if(Test-Path -LiteralPath $output){throw 'dev/local/original already exists; preserve or rename it before extracting again.'}
$work=Join-Path $local ('extract-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $work -Force | Out-Null
$process=$null
try {
    $info=New-Object Diagnostics.ProcessStartInfo
    $info.FileName=$original;$info.WorkingDirectory=[IO.Path]::GetDirectoryName($original)
    $info.UseShellExecute=$false;$info.WindowStyle=[Diagnostics.ProcessWindowStyle]::Hidden
    $info.EnvironmentVariables['TEMP']=$work;$info.EnvironmentVariables['TMP']=$work
    $process=[Diagnostics.Process]::Start($info)
    $deadline=[DateTime]::UtcNow.AddSeconds(45)
    do {
        Start-Sleep -Milliseconds 500
        $found=@{}
        foreach($file in Get-ChildItem -LiteralPath $work -Recurse -File){$found[$file.Name]=$file.FullName}
        $ready=$true
        foreach($source in $manifest.sources.PSObject.Properties) {
            if(-not $found.ContainsKey($source.Name)){$ready=$false;break}
            try {if((Get-FileHash -LiteralPath $found[$source.Name]).Hash.ToLowerInvariant() -ne $source.Value.sha256){$ready=$false;break}} catch {$ready=$false;break}
        }
        if($ready){break}
        if($process.HasExited){throw 'The original game exited before extraction finished.'}
    } while([DateTime]::UtcNow -lt $deadline)
    if(-not $ready){throw 'Extraction timed out.'}
    New-Item -ItemType Directory -Path $output | Out-Null
    foreach($source in $manifest.sources.PSObject.Properties){Copy-Item -LiteralPath $found[$source.Name] -Destination (Join-Path $output $source.Name)}
    Write-Output ('Verified original files: '+$output)
} finally {
    if($process){if(-not $process.HasExited){$process.Kill();$process.WaitForExit()};$process.Dispose()}
    $prefix=[IO.Path]::GetFullPath($local).TrimEnd('\')+'\'
    if(-not [IO.Path]::GetFullPath($work).StartsWith($prefix,[StringComparison]::OrdinalIgnoreCase)){throw 'Unsafe extraction cleanup path.'}
    Remove-Item -LiteralPath $work -Recurse -Force
}
