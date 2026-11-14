param(
    [Parameter(Mandatory=$true)][string]$SourceDirectory,
    [Parameter(Mandatory=$true)][string]$GameRoot,
    [Parameter(Mandatory=$true)][string]$SourceManifest,
    [Parameter(Mandatory=$true)][string]$ExpectedManifestSha256,
    [switch]$PlanOnly
)
$ErrorActionPreference='Stop'
if((Get-FileHash -LiteralPath $SourceManifest -Algorithm SHA256).Hash -ne $ExpectedManifestSha256){throw 'Reviewed source manifest changed.'}
$expeditionImportManifest=Get-Content -LiteralPath $SourceManifest -Raw | ConvertFrom-Json
$expeditionImportSource=[IO.Path]::GetFullPath($SourceDirectory).TrimEnd('\','/')
$expeditionImportRoot=[IO.Path]::GetFullPath((Join-Path $GameRoot 'BepInEx/config/SprocketExpedition'))
$expeditionImportLibrary=Join-Path $expeditionImportRoot 'content-library/blueprint-blobs'
$expeditionImportPool=Join-Path $expeditionImportRoot 'community-blueprints'
# Components have no vehicle header/creationDate. Do not feed cannon/engine/
# suspension blueprints to the native vehicle loader. Era classification remains
# native at runtime; this importer never invents historical date boundaries.
$expeditionImportVehicles=@($expeditionImportManifest.entries | Where-Object { $null -ne $_.date -and $null -ne $_.name })
$expeditionImportTooLarge=@($expeditionImportVehicles | Where-Object {$_.bytes -gt 67108864})
if($expeditionImportTooLarge.Count){throw 'A vehicle exceeds the supported 64 MiB blueprint limit; review before importing.'}
if($PlanOnly){
    [pscustomobject]@{Vehicles=$expeditionImportVehicles.Count;Bytes=($expeditionImportVehicles | Measure-Object bytes -Sum).Sum;Pool=$expeditionImportPool;SourceUnchanged=$true} | ConvertTo-Json
    return
}
if(Get-Process Sprocket -ErrorAction SilentlyContinue){throw 'Close Sprocket normally before importing. No process was terminated.'}
$expeditionImportJournal=Join-Path (Split-Path $PSScriptRoot -Parent) ('artifacts/import-community-'+(Get-Date -Format yyyyMMdd-HHmmss))
New-Item -ItemType Directory -Path $expeditionImportJournal | Out-Null
$expeditionImportPrevious=Join-Path $expeditionImportPool 'community-import.json'
if(Test-Path -LiteralPath $expeditionImportPrevious){Copy-Item -LiteralPath $expeditionImportPrevious -Destination (Join-Path $expeditionImportJournal 'community-import.before.json')}
New-Item -ItemType Directory -Force -Path $expeditionImportPool,$expeditionImportLibrary | Out-Null
$expeditionImportReceipt=[ordered]@{Status='Importing';SourceManifestSha256=$ExpectedManifestSha256;Count=0;Entries=@();OriginalsPreserved=$true;BossSelectionsChanged=$false}
foreach($expeditionImportEntry in $expeditionImportVehicles){
    if(Get-Process Sprocket -ErrorAction SilentlyContinue){throw 'Sprocket started during import; completed immutable files are retained. Run import again with the game closed.'}
    if($expeditionImportEntry.hash -notmatch '^[a-f0-9]{64}$'){throw 'Invalid content hash in source manifest.'}
    $expeditionImportFile=[IO.Path]::GetFullPath((Join-Path $expeditionImportSource $expeditionImportEntry.sources[0]))
    if(!$expeditionImportFile.StartsWith($expeditionImportSource+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Source entry escaped the explicitly named directory.'}
    if((Get-FileHash -LiteralPath $expeditionImportFile -Algorithm SHA256).Hash -ne $expeditionImportEntry.hash){throw 'A source blueprint changed after review.'}
    $expeditionImportBlob=Join-Path $expeditionImportLibrary ($expeditionImportEntry.hash+'.blueprint')
    if(!(Test-Path -LiteralPath $expeditionImportBlob)){Copy-Item -LiteralPath $expeditionImportFile -Destination $expeditionImportBlob}
    if((Get-FileHash -LiteralPath $expeditionImportBlob -Algorithm SHA256).Hash -ne $expeditionImportEntry.hash){throw 'Pinned content blob differs from its identity; no overwrite attempted.'}
    $expeditionImportMarker=Join-Path $expeditionImportPool ($expeditionImportEntry.hash+'.blueprint')
    if(!(Test-Path -LiteralPath $expeditionImportMarker)){
        # Two directory entries to one immutable content file, avoiding another
        # 3.5 GB copy. This never links to the user's mutable Downloads originals.
        New-Item -ItemType HardLink -Path $expeditionImportMarker -Target $expeditionImportBlob | Out-Null
    }
    if((Get-Item -LiteralPath $expeditionImportMarker).Length -ne $expeditionImportEntry.bytes){throw 'Community entry has the wrong size.'}
    $expeditionImportReceipt.Count++
    $expeditionImportReceipt.Entries+=@{Hash=$expeditionImportEntry.hash;Name=$expeditionImportEntry.name;CreationDate=$expeditionImportEntry.date;Bytes=$expeditionImportEntry.bytes;Path=$expeditionImportMarker}
    if($expeditionImportReceipt.Count%100 -eq 0){Write-Output ('Imported '+$expeditionImportReceipt.Count+'/'+$expeditionImportVehicles.Count+' vehicle blueprints')}
}
$expeditionImportReceipt.Status='ImportedPendingNativeValidation'
$expeditionImportReceipt | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $expeditionImportJournal 'journal.json') -Encoding utf8
$expeditionImportReceipt | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $expeditionImportPrevious -Encoding utf8
Write-Output ('Imported '+$expeditionImportReceipt.Count+' unique vehicle blueprints. Native eras/costs are validated when the next new-run catalogue is built. Journal='+$expeditionImportJournal)
