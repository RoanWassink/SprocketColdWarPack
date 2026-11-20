<#
.SYNOPSIS
    Converts saved Sprocket .blueprint files from old mod storage keys to the new Sprocket Cold War Pack keys.

.DESCRIPTION
    Dry-run by default: scans for .blueprint files containing old mod keys and reports what would change.
    Pass -Apply to write changes (creates a full timestamped backup of every modified file first).
    Pass -Restore <BackupDir> to restore files from a previous backup.

.EXAMPLE
    .\Convert-OldModBlueprints.ps1
    Dry-run on "$env:USERPROFILE\Documents\My Games\Sprocket".

.EXAMPLE
    .\Convert-OldModBlueprints.ps1 -Apply
    Backs up and converts all matching .blueprint files in "$env:USERPROFILE\Documents\My Games\Sprocket".

.EXAMPLE
    .\Convert-OldModBlueprints.ps1 -Path "D:\MyBackups\Sprocket" -Apply
    Backs up and converts .blueprint files in a custom folder.
#>
[CmdletBinding()]
param(
    [string]$Path = (Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'My Games\Sprocket'),
    [switch]$Apply,
    [string]$BackupDir,
    [string]$Restore
)

$ErrorActionPreference = 'Stop'

if ($Restore) {
    if (-not (Test-Path -LiteralPath $Restore)) {
        throw "Backup folder not found: $Restore"
    }
    $restoreRoot = (Resolve-Path -LiteralPath $Restore).Path
    $targetRoot = (Resolve-Path -LiteralPath $Path).Path
    $restored = 0
    Get-ChildItem -LiteralPath $restoreRoot -Recurse -File | ForEach-Object {
        $rel = $_.FullName.Substring($restoreRoot.Length).TrimStart('\', '/')
        $dst = Join-Path $targetRoot $rel
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $dst) | Out-Null
        Copy-Item -LiteralPath $_.FullName -Destination $dst -Force
        $restored++
    }
    Write-Host "Restored $restored file(s) from $restoreRoot to $targetRoot."
    exit 0
}

if (-not (Test-Path -LiteralPath $Path)) {
    throw "Sprocket folder not found: $Path"
}

$targetPath = (Resolve-Path -LiteralPath $Path).Path

$renames = @(
    @{ Old = '"roanHydropneumaticV1"';   New = '"sprocketHydropneumaticV1"' }
    @{ Old = '"roanTelescopeBaseModel"'; New = '"sprocketTelescopeBaseModel"' }
    @{ Old = '"roanTelescopePartModel"'; New = '"sprocketTelescopePartModel"' }
    @{ Old = '"roanMastPiston"';         New = '"sprocketMastPiston"' }
    @{ Old = '"roanMastSleeve"';         New = '"sprocketMastSleeve"' }
    @{ Old = '"roanTelescopicMast"';     New = '"sprocketTelescopicMast"' }
    @{ Old = '"roanTelescopeBase"';      New = '"sprocketTelescopeBase"' }
    @{ Old = '"roanTelescopePart"';      New = '"sprocketTelescopePart"' }
    @{ Old = '"roanCarouselEnabledV1"';  New = '"sprocketCarouselEnabledV1"' }
    @{ Old = '"roanCarouselCannonV1"';   New = '"sprocketCarouselCannonV1"' }
    @{ Old = '"roanCarouselLayoutV2"';   New = '"sprocketCarouselLayoutV2"' }
    @{ Old = '"roanBustleEnabledV1"';    New = '"sprocketBustleEnabledV1"' }
    @{ Old = '"roanBustleCannonV1"';     New = '"sprocketBustleCannonV1"' }
    @{ Old = '"roanShellProfile"';       New = '"sprocketShellProfile"' }
    @{ Old = '"roanApfsdsBeta"';         New = '"sprocketApfsdsBeta"' }
    @{ Old = '"roanThermalProfile"';     New = '"sprocketThermalProfile"' }
)

if (-not $BackupDir) {
    $stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
    $BackupDir = Join-Path (Split-Path -Parent $targetPath) "Sprocket-blueprint-backup-$stamp"
}

$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
$utf8Bom   = New-Object System.Text.UTF8Encoding($true)

$files = Get-ChildItem -LiteralPath $targetPath -Filter '*.blueprint' -Recurse -File
$changedFiles = 0
$totalReplacements = 0

foreach ($file in $files) {
    $bytes = [System.IO.File]::ReadAllBytes($file.FullName)
    $hasBom = ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF)
    $text = if ($hasBom) { $utf8Bom.GetString($bytes, 3, $bytes.Length - 3) } else { $utf8NoBom.GetString($bytes) }

    $updated = $text
    $fileHits = @()

    foreach ($r in $renames) {
        $count = ([regex]::Matches($updated, [regex]::Escape($r.Old))).Count
        if ($count -gt 0) {
            $updated = $updated.Replace($r.Old, $r.New)
            $fileHits += "$($r.Old.Trim('"')) ($count)"
            $totalReplacements += $count
        }
    }

    if ($fileHits.Count -eq 0) {
        continue
    }

    $changedFiles++
    $rel = $file.FullName.Substring($targetPath.Length).TrimStart('\', '/')
    $hitSummary = $fileHits -join ', '

    if ($Apply) {
        $bakPath = Join-Path $BackupDir $rel
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $bakPath) | Out-Null
        Copy-Item -LiteralPath $file.FullName -Destination $bakPath -Force
        $enc = if ($hasBom) { $utf8Bom } else { $utf8NoBom }
        [System.IO.File]::WriteAllText($file.FullName, $updated, $enc)
        Write-Host "CONVERTED: $rel [$hitSummary]"
    } else {
        Write-Host "WOULD CONVERT: $rel [$hitSummary]"
    }
}

Write-Host ""
if ($Apply) {
    Write-Host "Done: $changedFiles file(s) converted ($totalReplacements replacement(s))."
    if ($changedFiles -gt 0) {
        Write-Host "Backup saved to: $BackupDir"
    }
} else {
    Write-Host "Dry run: $changedFiles file(s) would be converted ($totalReplacements replacement(s))."
    if ($changedFiles -gt 0) {
        Write-Host "Run again with -Apply to convert these files (a backup will be created automatically)."
    }
}
