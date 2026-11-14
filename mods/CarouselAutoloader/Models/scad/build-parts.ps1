# Carousel Autoloader: export the OpenSCAD parts to STL and regenerate AutoloaderMeshData.cs.
# powershell -NoProfile -File Models\scad\build-parts.ps1 [-OpenScad <openscad.com>] [-Source <project folder>] [-Out <folder for the STL files>]
# The STL files are copied to <Source>\Models by stl_to_cs.py, which also writes <Source>\AutoloaderMeshData.cs.
param(
  [string]$OpenScad = 'C:\Program Files\OpenSCAD\openscad.com',
  [string]$Source = (Split-Path (Split-Path $PSScriptRoot)),
  [string]$Out = (Join-Path ([IO.Path]::GetTempPath()) 'carousel-parts-stl')
)
$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
$parts = Join-Path $here 'parts'
New-Item -ItemType Directory -Force -Path $Out | Out-Null
$jobs = @(
  @('round12', 'round.scad', @('-D', 'fn=12')),
  @('round8', 'round.scad', @('-D', 'fn=8')),
  @('round8c', 'round.scad', @('-D', 'fn=8', '-D', 'coarse=1')),
  @('cradle', 'cradle.scad', @()), @('clamp', 'clamp.scad', @()),
  @('cradle8', 'cradle.scad', @('-D', 'fn=8')), @('clamp8', 'clamp.scad', @('-D', 'fn=8')), @('trough', 'trough.scad', @()),
  @('projectile12', 'projectile.scad', @('-D', 'fn=12')), @('projectile8', 'projectile.scad', @('-D', 'fn=8')),
  @('charge12', 'charge.scad', @('-D', 'fn=12')), @('charge8', 'charge.scad', @('-D', 'fn=8')),
  @('ramhead', 'ramhead.scad', @()), @('hubcap', 'hubcap.scad', @()))
foreach ($job in $jobs) {
  $stl = Join-Path $Out ($job[0] + '.stl')
  $arguments = @('-o', $stl) + $job[2] + @((Join-Path $parts $job[1]))
  & $OpenScad @arguments 2>&1 | Out-Null
  if ($LASTEXITCODE -ne 0 -or -not (Test-Path $stl)) { throw "OpenSCAD failed for $($job[0])" }
}
& py -I (Join-Path $here 'stl_to_cs.py') $Out $Source
if ($LASTEXITCODE -ne 0) { throw 'stl_to_cs.py failed' }
