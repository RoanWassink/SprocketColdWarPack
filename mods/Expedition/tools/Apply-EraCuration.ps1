param(
    [Parameter(Mandatory=$true)][string]$GameRoot,
    [Parameter(Mandatory=$true)][string]$Plan,
    [Parameter(Mandatory=$true)][string]$ExpectedPlanSha256,
    [switch]$PlanOnly
)
$ErrorActionPreference='Stop'
if((Get-FileHash -LiteralPath $Plan -Algorithm SHA256).Hash -ne $ExpectedPlanSha256){throw 'Reviewed curation plan changed.'}
$curationPlan=Get-Content -LiteralPath $Plan -Raw | ConvertFrom-Json
$curationRoot=[IO.Path]::GetFullPath((Join-Path $GameRoot 'BepInEx/config/SprocketExpedition')).TrimEnd('\','/')
$curationActive=[IO.Path]::GetFullPath((Join-Path $curationRoot 'community-blueprints'))
$curationReserve=[IO.Path]::GetFullPath((Join-Path $curationRoot 'community-blueprints-reserve'))
$curationPlanActive=[IO.Path]::GetFullPath($curationPlan.ActiveDirectory)
$curationPlanReserve=[IO.Path]::GetFullPath($curationPlan.ReserveDirectory)
if($curationPlan.Schema -ne 1 -or $curationPlanActive -ne $curationActive -or $curationPlanReserve -ne $curationReserve){throw 'Plan targets do not match the named Expedition directories.'}
foreach($curationDirectory in @($curationRoot,$curationActive,$curationReserve)){
    if(!$curationDirectory.StartsWith($curationRoot,[StringComparison]::OrdinalIgnoreCase)){throw 'Curation target escaped the intended directory.'}
    if((Test-Path -LiteralPath $curationDirectory) -and ((Get-Item -LiteralPath $curationDirectory).Attributes -band [IO.FileAttributes]::ReparsePoint)){throw 'Reparse-point directory requires separate review.'}
}
function Get-CurationMarker([string]$directory,[string]$hash){
    if($hash -cnotmatch '^[a-f0-9]{64}$'){throw 'Invalid marker identity.'}
    $curationPath=[IO.Path]::GetFullPath((Join-Path $directory ($hash+'.blueprint')))
    if(!$curationPath.StartsWith($directory+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Marker path escaped its explicit directory.'}
    return $curationPath
}
$curationCurrent=@(Get-ChildItem -LiteralPath $curationActive -File -Filter '*.blueprint' | ForEach-Object {$_.BaseName} | Sort-Object)
if(@(Compare-Object $curationCurrent @($curationPlan.BeforePool | Sort-Object)).Count){throw 'Active pool changed since review.'}
$curationAll=@($curationPlan.Keep)+@($curationPlan.Disable)
if($curationAll.Count -ne @($curationAll | Sort-Object -Unique).Count -or
    @(Compare-Object ($curationAll | Sort-Object) $curationCurrent).Count){throw 'Plan does not partition the current pool.'}
$curationExpected=@($curationPlan.Selected | Where-Object Marker | ForEach-Object {$_.Hash} | Sort-Object)
if(@(Compare-Object $curationExpected (@($curationPlan.Keep)+@($curationPlan.Enable) | Sort-Object)).Count){throw 'Selected metadata differs from active-marker plan.'}
$curationMoves=@()
foreach($curationHash in $curationPlan.Disable){$curationMoves+=@{Hash=$curationHash;From=(Get-CurationMarker $curationActive $curationHash);To=(Get-CurationMarker $curationReserve $curationHash)}}
foreach($curationHash in $curationPlan.Enable){$curationMoves+=@{Hash=$curationHash;From=(Get-CurationMarker $curationReserve $curationHash);To=(Get-CurationMarker $curationActive $curationHash)}}
foreach($curationMove in $curationMoves){if(Test-Path -LiteralPath $curationMove.To){throw 'Destination already exists; no overwrite is allowed.'}}
if($PlanOnly){[ordered]@{Keep=$curationPlan.Keep.Count;Reserve=$curationPlan.Disable.Count;Enable=$curationPlan.Enable.Count;Selected=$curationPlan.Selected.Count;TargetsVerified=$true;FilesChanged=$false}|ConvertTo-Json;return}
if(Get-Process Sprocket -ErrorAction SilentlyContinue){throw 'Close Sprocket normally before curation. No process was terminated.'}
# Verify reviewed identities before any marker moves. Pool entries are content
# addressed. A move changes directory membership, never their blueprint bytes.
foreach($curationHash in $curationCurrent){
    $curationFile=Get-CurationMarker $curationActive $curationHash
    if((Get-FileHash -LiteralPath $curationFile -Algorithm SHA256).Hash -ne $curationHash){throw 'Existing marker differs from its content identity.'}
    $curationBlob=Join-Path (Join-Path $curationRoot 'content-library/blueprint-blobs') ($curationHash+'.blueprint')
    if(!(Test-Path -LiteralPath $curationBlob) -or (Get-Item -LiteralPath $curationBlob).Length -ne (Get-Item -LiteralPath $curationFile).Length){throw 'Immutable library backing is unavailable.'}
}
$curationProject=Split-Path $PSScriptRoot -Parent
$curationJournal=Join-Path $curationProject ('artifacts/era-curation-'+(Get-Date -Format yyyyMMdd-HHmmss))
New-Item -ItemType Directory -Path $curationJournal | Out-Null
Copy-Item -LiteralPath $Plan -Destination (Join-Path $curationJournal 'reviewed-plan.json')
$curationMetadata=Join-Path $curationRoot 'pool-curation.json'
if(Test-Path -LiteralPath $curationMetadata){Copy-Item -LiteralPath $curationMetadata -Destination (Join-Path $curationJournal 'pool-curation.before.json')}
$curationReceipt=[ordered]@{Status='VerifiedBeforeMove';PlanSha256=$ExpectedPlanSha256;Active=$curationActive;Reserve=$curationReserve;Before=@($curationCurrent);After=@($curationExpected);Completed=@();BlueprintsDeleted=0;LibraryChanged=$false;SourceFilesChanged=$false;ConfigChanged=$false}
$curationJournalPath=Join-Path $curationJournal 'journal.json'
$curationReceipt | ConvertTo-Json -Depth 7 | Set-Content -LiteralPath $curationJournalPath -Encoding utf8
try{
    if(Get-Process Sprocket -ErrorAction SilentlyContinue){throw 'Sprocket started during verification; no marker changes made.'}
    New-Item -ItemType Directory -Force -Path $curationReserve | Out-Null
    foreach($curationMove in $curationMoves){
        if(Get-Process Sprocket -ErrorAction SilentlyContinue){throw 'Sprocket started during curation; operation stopped with a resumable journal.'}
        # Both absolute paths were checked above; all moves are individual files,
        # entirely within the explicitly named Expedition active/reserve folders.
        Move-Item -LiteralPath $curationMove.From -Destination $curationMove.To
        $curationReceipt.Completed+=@{Hash=$curationMove.Hash;From=$curationMove.From;To=$curationMove.To}
        if($curationReceipt.Completed.Count%50 -eq 0){$curationReceipt | ConvertTo-Json -Depth 7 | Set-Content -LiteralPath $curationJournalPath -Encoding utf8}
    }
    $curationAfter=@(Get-ChildItem -LiteralPath $curationActive -File -Filter '*.blueprint' | ForEach-Object {$_.BaseName} | Sort-Object)
    if(@(Compare-Object $curationAfter $curationExpected).Count){throw 'Final active pool differs from reviewed selection.'}
    foreach($curationMove in $curationReceipt.Completed){if(!(Test-Path -LiteralPath $curationMove.To) -or (Test-Path -LiteralPath $curationMove.From)){throw 'Moved marker verification failed.'}}
    $curationReceipt.Status='AppliedPendingNativeValidation'
    $curationReceipt | ConvertTo-Json -Depth 7 | Set-Content -LiteralPath $curationJournalPath -Encoding utf8
    [ordered]@{Schema=1;PlanSha256=$ExpectedPlanSha256;Journal=$curationJournalPath;Summaries=$curationPlan.Summaries;Active=$curationExpected;ReserveFolder=$curationReserve;AppliedTo='Future new runs only'} | ConvertTo-Json -Depth 7 | Set-Content -LiteralPath $curationMetadata -Encoding utf8
    Write-Output "Era curation applied. Active community markers=$($curationAfter.Count); moved=$($curationMoves.Count); journal=$curationJournal"
}
catch{
    $curationReceipt.Status='Stopped';$curationReceipt['Error']=$_.Exception.Message
    $curationReceipt | ConvertTo-Json -Depth 7 | Set-Content -LiteralPath $curationJournalPath -Encoding utf8
    throw
}
