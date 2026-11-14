# Heavy Trunnion Mount: export STLs, run the checks, render the PNGs and the icon.
# Run: powershell -ExecutionPolicy Bypass -File build.ps1 [-SkipRenders]
# Outputs: heavy-trunnion-*.stl, materials\*.stl, shots\*.png, heavy-trunnion-icon.png, logs\*.txt
param([switch]$SkipRenders)
$ErrorActionPreference = 'Stop'
$collide = $false
$here = $PSScriptRoot
$openscad = 'C:\Program Files\OpenSCAD\openscad.com'
$logs = Join-Path $here 'logs'; $shots = Join-Path $here 'shots'; $mats = Join-Path $here 'materials'
New-Item -ItemType Directory -Force -Path $logs, $shots, $mats | Out-Null
$log = Join-Path $logs 'openscad.log'
Set-Content -Path $log -Value "build $(Get-Date -Format s)" -Encoding utf8

# Runs openscad.com with an argument string, appends its console output to the log, returns that output.
function Invoke-Scad([string]$argString) {
  # openscad.com writes its messages to stdout and/or stderr; read both
  $err = Join-Path $logs 'last-stderr.txt'; $out = Join-Path $logs 'last-stdout.txt'
  $p = Start-Process -FilePath $openscad -ArgumentList $argString -WorkingDirectory $here -NoNewWindow -Wait -PassThru -RedirectStandardError $err -RedirectStandardOutput $out
  $text = "$(Get-Content -Raw $out)$(Get-Content -Raw $err)"
  Add-Content -Path $log -Value "`n>>> openscad $argString`n$text" -Encoding utf8
  if ($p.ExitCode -ne 0 -and $text -notmatch 'top level object is empty') { throw "openscad failed ($($p.ExitCode)): $argString" }
  return $text
}

# 1. game meshes: one STL per group, plus the same groups split per material
Invoke-Scad '-o heavy-trunnion-static.stl heavy-trunnion-static.scad' | Out-Null
Invoke-Scad '-o heavy-trunnion-moving.stl heavy-trunnion-moving.scad' | Out-Null
Invoke-Scad '-o heavy-trunnion-pinion.stl heavy-trunnion-pinion.scad' | Out-Null
$i = 1; foreach ($m in 'cast', 'steel', 'bronze') { Invoke-Scad "-o materials/static-$m.stl -D m=$i heavy-trunnion-static.scad" | Out-Null; $i++ }
$i = 1; foreach ($m in 'cast', 'steel') { Invoke-Scad "-o materials/moving-$m.stl -D m=$i heavy-trunnion-moving.scad" | Out-Null; $i++ }
# the pinion is steel only: its material file is the same unit-origin geometry as its group file
Invoke-Scad '-o materials/pinion-steel.stl heavy-trunnion-pinion.scad' | Out-Null
# OpenSCAD writes the triangles in a random order: canonical order makes the SHA256 reproducible
$stls = @('heavy-trunnion-static.stl', 'heavy-trunnion-moving.stl', 'heavy-trunnion-pinion.stl') + (Get-ChildItem $mats -Filter *.stl | ForEach-Object { "materials/$($_.Name)" })
& py -I (Join-Path $here 'stlnorm.py') @($stls | ForEach-Object { Join-Path $here $_ }) | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'stlnorm failed' }
# reproducibility: a second export of each group, normalized, must give the same bytes
$det = @('second export + normalize vs stored STL')
foreach ($g in 'static', 'moving', 'pinion') {
  Invoke-Scad "-o logs/redo-$g.stl heavy-trunnion-$g.scad" | Out-Null
  & py -I (Join-Path $here 'stlnorm.py') (Join-Path $logs "redo-$g.stl") | Out-Null
  $a = (Get-FileHash (Join-Path $here "heavy-trunnion-$g.stl") -Algorithm SHA256).Hash; $b = (Get-FileHash (Join-Path $logs "redo-$g.stl") -Algorithm SHA256).Hash
  $det += "$g : stored $a, redo $b -> $(if ($a -eq $b) { 'SAME' } else { 'DIFFERENT' })"
  if ($a -ne $b) { $collide = $true }
  Remove-Item (Join-Path $logs "redo-$g.stl")
}
Set-Content -Path (Join-Path $logs 'determinism.txt') -Value $det -Encoding utf8
$det | Write-Output

# 2. clearance over the elevation range; the pinion turns along (-3.75 x elevation)
$clear = @("clearance; moving turns e about X, pinion turns -3.75 e about its axis; journals in the bushes and the pinion shaft in its bores excluded; empty intersection = no collision")
# -10..+25 = the part's elevation limits (maxElevation 25, minElevation -10, like the native Panther mantlet)
$angles = @{ 'static-moving' = @(-10, -5, 0, 5, 10, 15, 20, 25); 'pinion-moving' = @(-10, -7.5, -5, -2.5, 0, 2.5, 5, 7.5, 10, 12.5, 15, 17.5, 20, 22.5, 25); 'pinion-static' = @(-10, -5, 0, 5, 10, 15, 20, 25) }
foreach ($pair in 'static-moving', 'pinion-moving', 'pinion-static') {
  foreach ($e in $angles[$pair]) {
    $tmp = Join-Path $logs 'clearance-tmp.stl'; if (Test-Path $tmp) { Remove-Item $tmp }
    $text = Invoke-Scad "-o logs/clearance-tmp.stl -D e=$e -D pair=\`"$pair\`" clearance.scad"
    $empty = $text -match 'top level object is empty'
    if (-not $empty) { $collide = $true }
    $clear += "{0,-13} elevation {1,5} deg: {2}" -f $pair, $e, $(if ($empty) { 'no collision' } else { 'COLLISION (see logs/openscad.log)' })
  }
}
if (Test-Path (Join-Path $logs 'clearance-tmp.stl')) { Remove-Item (Join-Path $logs 'clearance-tmp.stl') }
$clear += $(if ($collide) { 'RESULT FAIL' } else { 'RESULT PASS' })
Set-Content -Path (Join-Path $logs 'clearance.txt') -Value $clear -Encoding utf8
$clear | Write-Output

# 3. mesh checks (triangles, bounds, manifold, pins vs bushes, hashes)
& py -I (Join-Path $here 'stlcheck.py') $here --write (Join-Path $logs 'stlcheck.txt')
$checkFailed = $LASTEXITCODE -ne 0

# 4. PNGs of the exported meshes (viewer file = the same imports the user opens)
if (-not $SkipRenders) {
  $common = '--imgsize=1200,800 --colorscheme=Tomorrow'
  $c = '0,-96,-63'
  $views = @(
    @('34-boven-achter', "--camera=-1050,-1150,800,$c --projection=p", ''),
    @('34-boven-voor', "--camera=-1100,1100,800,$c --projection=p", ''),
    @('voor', "--camera=0,1400,-63,$c --projection=o", ''),
    @('zij', "--camera=-1400,-96,-63,$c --projection=o", ''),
    @('zij-elev+20', "--camera=-1400,-96,-63,$c --projection=o", '-D elevation=20'),
    @('zij-elev-10', "--camera=-1400,-96,-63,$c --projection=o", '-D elevation=-10'),
    @('native-voor', '--camera=164,1900,-63,164,-96,-63 --projection=o', '-D show_native=true -D labels=true -D line=5'),
    @('native-34', '--camera=-900,-1350,950,160,-96,-63 --projection=p', '-D show_native=true -D line=5'))
  foreach ($v in $views) {
    Invoke-Scad "-o shots/$($v[0]).png $common $($v[1]) $($v[2]) view-heavy-trunnion.scad" | Out-Null
  }
  # 5. icon: 3/4 render without bounds, cropped, background keyed out, 128 x 128 RGBA
  Invoke-Scad "-o logs/icon-render.png --imgsize=900,900 --colorscheme=Tomorrow --camera=-1200,-1000,450,0,-96,-63 --viewall --autocenter --projection=p -D show_bounds=false view-heavy-trunnion.scad" | Out-Null
  & py -I (Join-Path $here 'pngtool.py') icon (Join-Path $logs 'icon-render.png') (Join-Path $here 'heavy-trunnion-icon.png') 128
  if ($LASTEXITCODE -ne 0) { throw 'icon step failed' }
}
if ($collide -or $checkFailed) { Write-Output 'BUILD CHECKS FAILED'; exit 1 }
Write-Output 'BUILD CHECKS PASSED'
