# MTU powerpack model: export every part (LOD0 + LOD1 for the engine), normalize, check, render.
# Run: powershell -ExecutionPolicy Bypass -File build.ps1 [-SkipExport] [-SkipRenders]
# Outputs: parts\*.stl, shots\*.png, logs\stlcheck.txt, logs\openscad.log
param([switch]$SkipExport, [switch]$SkipRenders)
$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
$openscad = 'C:\Program Files\OpenSCAD\openscad.com'
$logs = Join-Path $here 'logs'; $shots = Join-Path $here 'shots'; $parts = Join-Path $here 'parts'
New-Item -ItemType Directory -Force -Path $logs, $shots, $parts | Out-Null
$log = Join-Path $logs 'openscad.log'
if (-not $SkipExport) { Set-Content -Path $log -Value "build $(Get-Date -Format s)" -Encoding utf8 }

function Invoke-Scad([string]$argString) {
  $err = Join-Path $logs 'last-stderr.txt'; $out = Join-Path $logs 'last-stdout.txt'
  $p = Start-Process -FilePath $openscad -ArgumentList $argString -WorkingDirectory $here -NoNewWindow -Wait -PassThru -RedirectStandardError $err -RedirectStandardOutput $out
  $text = "$(Get-Content -Raw $out)$(Get-Content -Raw $err)"
  Add-Content -Path $log -Value "`n>>> openscad $argString`n$text" -Encoding utf8
  if ($p.ExitCode -ne 0) { throw "openscad failed ($($p.ExitCode)): $argString" }
  if ($text -match 'WARNING') { Write-Output "warning in: $argString" }
  return $text
}

$engine = 'start', 'pair', 'output', 'turbo', 'turbo-outboard'
$trans = 'tra-input', 'tra-shroud', 'tra-output-l', 'tra-output-r', 'lon-input', 'lon-shroud', 'lon-output', 'twin-bridge'
if (-not $SkipExport) {
  $jobs = @()
  foreach ($p in $engine) { foreach ($l in 0, 1) { $jobs += ,@($p, $l) } }
  foreach ($p in $trans) { $jobs += ,@($p, 0) }
  foreach ($j in $jobs) {
    Invoke-Scad "-o parts/$($j[0])-lod$($j[1]).stl -D part=\`"$($j[0])\`" -D lod=$($j[1]) mtu-part.scad" | Out-Null
  }
  # OpenSCAD 2021.01 writes triangles in a random order: canonical order makes SHA256 reproducible
  & py -I (Join-Path $here 'stlnorm.py') @(Get-ChildItem $parts -Filter *.stl | ForEach-Object FullName) | Out-Null
  if ($LASTEXITCODE -ne 0) { throw 'stlnorm failed' }
}

& py -I (Join-Path $here 'stlcheck.py') $here --write (Join-Path $logs 'stlcheck.txt')
$checkFailed = $LASTEXITCODE -ne 0

if (-not $SkipRenders) {
  & py -I (Join-Path $here 'render.py') $here
  if ($LASTEXITCODE -ne 0) { throw 'render failed' }
}
if ($checkFailed) { Write-Output 'BUILD CHECKS FAILED'; exit 1 }
Write-Output 'BUILD CHECKS PASSED'
