# Reproducible 0.4.0 asset pipeline. No game files are written.
param([switch]$SkipBaseExport,[switch]$SkipRenders)
$ErrorActionPreference='Stop'
Push-Location $PSScriptRoot
try {
    if (-not $SkipBaseExport) {
        & powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1 -SkipRenders
        if ($LASTEXITCODE -ne 0) { throw 'Base asset export failed' }
    }
    & py -I .\build_hulls.py
    if ($LASTEXITCODE -ne 0) { throw 'Hull export failed' }
    & py -I .\stlnorm.py @(Get-ChildItem .\parts -Filter '*hull.stl' | ForEach-Object FullName)
    if ($LASTEXITCODE -ne 0) { throw 'Hull normalization failed' }
    & py -I .\embed_meshes.py
    if ($LASTEXITCODE -ne 0) { throw 'Mesh embedding failed' }
    if (-not $SkipRenders) {
        & py -I .\render040.py
        if ($LASTEXITCODE -ne 0) { throw 'Render failed (run managed checks with --bridge-json first)' }
    }
} finally { Pop-Location }
