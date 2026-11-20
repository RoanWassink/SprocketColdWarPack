[CmdletBinding(SupportsShouldProcess=$true)]
param(
    [string]$GamePath='E:\SteamLibrary\steamapps\common\Sprocket',
    [switch]$Restore,
    [string]$BackupPath
)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$gameRoot=[IO.Path]::GetFullPath($GamePath).TrimEnd('\','/')
$pluginRelative='BepInEx/plugins/SprocketPlateDestructionP0'
$configRelative='BepInEx/config/sprocket.platedestruction.p0.cfg'
$backupRoot=[IO.Path]::GetFullPath((Join-Path $gameRoot 'BepInEx/platedestruction-p0-backups'))
function Assert-PlainPath([string]$Path,[string]$AllowedRoot){
    $full=[IO.Path]::GetFullPath($Path)
    $prefix=$AllowedRoot.TrimEnd('\','/')+[IO.Path]::DirectorySeparatorChar
    if($full -ne $AllowedRoot -and -not $full.StartsWith($prefix,[StringComparison]::OrdinalIgnoreCase)){throw "Path outside own scope: $full"}
    $cursor=$full
    while($cursor -and $cursor.Length -ge $gameRoot.Length){
        if(Test-Path -LiteralPath $cursor){
            if((Get-Item -LiteralPath $cursor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint){throw "Refusing reparse point: $cursor"}
        }
        $cursor=[IO.Path]::GetDirectoryName($cursor)
    }
    return $full
}
function Target([string]$Relative){
    $normal=$Relative.Replace('\','/')
    $allowed=@(
        'BepInEx/plugins/SprocketPlateDestructionP0/SprocketPlateDestructionP0.dll'
    )
    if($allowed -cnotcontains $normal){throw "Not a Plate Destruction P0 target: $Relative"}
    $allowed=[IO.Path]::GetFullPath((Join-Path $gameRoot ([IO.Path]::GetDirectoryName($normal))))
    return Assert-PlainPath (Join-Path $gameRoot $normal) ([IO.Path]::GetFullPath($allowed))
}
function Hash([string]$Path){return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash}
function Assert-GameClosed{
    $expected=[IO.Path]::GetFullPath((Join-Path $gameRoot 'Sprocket.exe'))
    foreach($process in @(Get-Process -Name Sprocket -ErrorAction SilentlyContinue)){
        try{$runningPath=$process.Path}catch{throw 'Cannot determine running Sprocket location; close Sprocket first.'}
        if(-not $runningPath -or [string]::Equals($runningPath,$expected,[StringComparison]::OrdinalIgnoreCase)){throw 'Sprocket is running in this target. Close it first; no files changed.'}
    }
}
if(-not (Test-Path -LiteralPath (Join-Path $gameRoot 'BepInEx/core') -PathType Container)){throw 'Target has no BepInEx/core directory.'}
Assert-GameClosed
if($Restore){
    if(-not $BackupPath){throw '-Restore requires an explicit -BackupPath from a previous install.'}
    $backup=Assert-PlainPath $BackupPath $backupRoot
    $receiptPath=Join-Path $backup 'receipt.json'
    $receipt=Get-Content -LiteralPath $receiptPath -Raw | ConvertFrom-Json
    if($receipt.Mod -ne 'nero.sprocket.platedestruction.p0' -or $receipt.GamePath -ne $gameRoot){throw 'Backup belongs to another mod or game target.'}
    # Validate the complete restore plan before changing any file.
    foreach($entry in $receipt.Files){
        $target=Target $entry.Relative
        if($entry.Relative -eq $configRelative){continue} # Preserve current user settings.
        if(Test-Path -LiteralPath $target){if((Hash $target) -ne $entry.AfterHash){throw "Changed installed file; restore refused: $target"}}
        if($entry.Existed){
            $source=Assert-PlainPath (Join-Path $backup $entry.Relative) $backup
            if((Hash $source) -ne $entry.BeforeHash){throw "Backup hash mismatch: $source"}
        }
    }
    if($PSCmdlet.ShouldProcess($gameRoot,"Restore only SprocketPlateDestructionP0 files from $backup; preserve existing config")){
        Assert-GameClosed
        foreach($entry in $receipt.Files){
            $target=Target $entry.Relative
            if($entry.Relative -eq $configRelative){
                # Only remove a newly generated, still-unmodified default config.
                if(-not $entry.Existed -and (Test-Path -LiteralPath $target) -and (Hash $target) -eq $entry.AfterHash){Remove-Item -LiteralPath $target}
                continue
            }
            if($entry.Existed){
                New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($target)) -Force | Out-Null
                Copy-Item -LiteralPath (Join-Path $backup $entry.Relative) -Destination $target -Force
                if((Hash $target) -ne $entry.BeforeHash){throw "Restored hash mismatch: $target"}
            }elseif(Test-Path -LiteralPath $target){Remove-Item -LiteralPath $target}
        }
        Write-Output "Restored own files; current config preserved. Backup retained: $backup"
    }
    return
}
$manifestPath=Join-Path $PSScriptRoot 'package-manifest.json'
$manifest=Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
if($manifest.Mod -ne 'nero.sprocket.platedestruction.p0' -or $manifest.Version -ne '0.8.3'){throw 'Unexpected package identity.'}
if(@($manifest.Files).Count -ne 1){throw 'Manifest must contain exactly the one allowlisted DLL.'}
$entries=@()
foreach($item in $manifest.Files){
    $target=Target $item.Relative
    $source=[IO.Path]::GetFullPath((Join-Path (Join-Path $PSScriptRoot 'payload') $item.Relative))
    $payloadRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot 'payload'))+[IO.Path]::DirectorySeparatorChar
    if(-not $source.StartsWith($payloadRoot,[StringComparison]::OrdinalIgnoreCase)){throw 'Package traversal refused.'}
    if((Hash $source) -ne $item.SHA256){throw "Package hash mismatch: $source"}
    $existed=Test-Path -LiteralPath $target -PathType Leaf
    $before=if($existed){Hash $target}else{$null}
    # Known installed versions: 0.0.1..0.8.2 or 0.8.3 itself (re-install); also a fresh game folder with nothing installed. Anything else is refused.
    $known=@('E1B607845DAE7220AC6A15B635960583EB63C44D3AAA930A5836DDBE238D53B2','D8101A5F1AD5DDDEE736E2B9629D312175EC45FE8F27B58EAA5BD9CBBA802DA0','D425EB8D0951D02D2E50B91D70D10217A68C21EB7985ABC0AF1AFCBBF7D05E52','92E005C01BF555BCBF546E5F3E478F80D709879A504AF1D7959A14367A7428B6','A3753BD756C551547F6481789C3CB2B6961EC84AD8797B41100C6F76E690BA88','1032B999F8809CA8C6B8DD641AA0C3B85FB3177339841C9E5B9E0818E1B9766C','C2D866D2D6CC2291F2E13EE4E61A6AD93BB308CA647AF1A881C6FDF7F0FE358E','8AD85915B613CB94F0CC0434E7696E63B744EA28365F495A278C80AE422679FB','5E302C5CE4DF872B69E8641EEA60B8A8DC7AA396A7E4A496EC2151270508C708','7FD729A7EC16AC416E7DA01951206364E1ED08A0E613E27FFA40C038803FF75D','1A2358731E4452D8DF9AADC68EE079BCF49FE59AA438E485516D72AA845DEDAA','1A6A03DF467A70594D63E8585A80F04F91286137ED042DE5BC163C2B2BEFC35A','EA68C33262EBCF32286FF901F920BAE7E588372256DB90ABF679B076E27852EA','B2D1D72EB06DE79BE3D68E1B6FC9DD0403667497F4B32585C82A164BBED3706F','E02C3F5FC501398731A11EA2E954A908D245EA917D2085DC59B40CC7C7D046E1','8399B1FEC252DBD39AD5E2E695B3C5151CEEE5D8D09875792EB11F6E1ECE55FE','4CD27D5500F43D90D7F0DF48D629992DF0557A705E7775BE684E2EE152D7B33C','97AF68EB55DC6A8657B4890103C3243934CF5DA9AA185F96021E74991E8FADB1',$item.SHA256)
    if($existed -and $known -notcontains $before){throw "Installed DLL has unexpected hash $before; refusing to overwrite."}
    Write-Output "Existing version: $(if(-not $existed){'none'}elseif($before -eq $known[0]){'0.0.1'}elseif($before -eq $known[1]){'0.0.2'}elseif($before -eq $known[2]){'0.0.3'}elseif($before -eq $known[3]){'0.0.4'}elseif($before -eq $known[4]){'0.1.0'}elseif($before -eq $known[5]){'0.1.1'}elseif($before -eq $known[6]){'0.2.0'}elseif($before -eq $known[7]){'0.3.0'}elseif($before -eq $known[8]){'0.3.1'}elseif($before -eq $known[9]){'0.4.0'}elseif($before -eq $known[10]){'0.4.1'}elseif($before -eq $known[11]){'0.5.0'}elseif($before -eq $known[12]){'0.6.0'}elseif($before -eq $known[13]){'0.7.0'}elseif($before -eq $known[14]){'0.7.1'}elseif($before -eq $known[15]){'0.8.0'}elseif($before -eq $known[16]){'0.8.1'}elseif($before -eq $known[17]){'0.8.2'}else{'0.8.3'})"
    $entries+= [pscustomobject]@{Relative=$item.Relative;Existed=$existed;BeforeHash=$before;AfterHash=$item.SHA256}
}
if($PSCmdlet.ShouldProcess($gameRoot,'Back up and install SprocketPlateDestructionP0 0.8.3; preserve existing config')){
    Assert-GameClosed
    $backup=Assert-PlainPath (Join-Path $backupRoot ([DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss-fffffff'))) $backupRoot
    New-Item -ItemType Directory -Path $backup -Force | Out-Null
    foreach($entry in $entries){
        if($entry.Existed){
            $destination=Assert-PlainPath (Join-Path $backup $entry.Relative) $backup
            New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($destination)) -Force | Out-Null
            Copy-Item -LiteralPath (Target $entry.Relative) -Destination $destination
            if((Hash $destination) -ne $entry.BeforeHash){throw 'Backup verification failed; install not started.'}
        }
    }
    [pscustomobject]@{Mod='nero.sprocket.platedestruction.p0';Version='0.8.3';GamePath=$gameRoot;Files=$entries} | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $backup 'receipt.json') -Encoding UTF8
    foreach($entry in $entries){
        $target=Target $entry.Relative
        if($entry.Relative -eq $configRelative -and $entry.Existed){continue}
        New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($target)) -Force | Out-Null
        Copy-Item -LiteralPath (Join-Path (Join-Path $PSScriptRoot 'payload') $entry.Relative) -Destination $target -Force
        if((Hash $target) -ne $entry.AfterHash){throw "Installed hash mismatch: $target. Restore using backup $backup"}
    }
    Write-Output "Prepared package installed. BackupPath: $backup"
}
