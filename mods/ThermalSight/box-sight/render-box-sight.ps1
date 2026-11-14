$ErrorActionPreference = 'Stop'
$scad = 'C:\Program Files\OpenSCAD\openscad.com'
$root = Split-Path -Parent $PSScriptRoot
$model = Join-Path $PSScriptRoot 'box-sight.scad'
$renders = Join-Path $PSScriptRoot 'renders'
New-Item -ItemType Directory -Path $renders -Force | Out-Null
$views = @(
    @{ Name='front-oblique'; Camera='1000,-1250,750,0,-60,135' },
    @{ Name='rear-oblique'; Camera='-1000,1250,650,0,-60,135' },
    @{ Name='side'; Camera='1400,-60,135,0,-60,135' },
    @{ Name='paint-mask'; Camera='1000,-1250,750,0,-60,135' }
)
$log = Join-Path $PSScriptRoot 'RENDERS.txt'
'OpenSCAD 2021.01; exact converter STL surfaces; game Y is up, +Z is forward.' | Set-Content $log
foreach ($view in $views) {
    $args = @('-o', (Join-Path $renders ($view.Name+'.png')), '--imgsize=1200,800',
        ('--camera='+$view.Camera), '--projection=o', '--viewall', '--colorscheme=Tomorrow')
    if ($view.Name -eq 'paint-mask') { $args += @('-D','paint_mask=true') }
    & $scad @args $model 2>&1 | Add-Content $log
    if ($LASTEXITCODE -ne 0) { throw ('OpenSCAD failed: '+$view.Name) }
}
$icon = Join-Path $PSScriptRoot 'box-sight-icon.png'
& $scad -o $icon --imgsize=512,512 --camera=1000,-1250,750,0,-60,135 --projection=o --viewall --colorscheme=Tomorrow $model 2>&1 | Add-Content $log
if ($LASTEXITCODE -ne 0) { throw 'OpenSCAD icon failed' }
# Native menu icons use a transparent background. Geometry and colour remain the OpenSCAD render.
Add-Type -AssemblyName System.Drawing
$bitmap = [System.Drawing.Bitmap]::new($icon)
$bitmap.MakeTransparent($bitmap.GetPixel(0,0))
$temporary = Join-Path $PSScriptRoot 'icon-transparent.png'
$bitmap.Save($temporary,[System.Drawing.Imaging.ImageFormat]::Png)
$bitmap.Dispose()
Move-Item -LiteralPath $temporary -Destination $icon -Force
Copy-Item -LiteralPath $icon -Destination (Join-Path $root 'package\BepInEx\plugins\SprocketThermalSight\assets\box-sight-icon.png') -Force
'Rendered four 1200x800 views and transparent 512x512 icon.' | Add-Content $log
