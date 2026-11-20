$ErrorActionPreference='Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
py -I Models\render_belt.py
if($LASTEXITCODE){throw 'Mesh snapshot conversion failed'}
$openscad='C:\Program Files\OpenSCAD\openscad.com'
foreach($name in @('bradley-spaced-42','bradley-spaced-25','bradley-spaced-2','bradley-spaced-0','bustle-spaced-42')) {
    & $openscad -o "logs\ingame-0.3.2\$name.png" --projection=ortho --imgsize=1200,800 --camera=0,0,0,58,0,28,1200 "logs\ingame-0.3.2\view-$name.scad" *> "logs\ingame-0.3.2\$name-render.log"
    if($LASTEXITCODE){throw "Render failed: $name"}
}
& $openscad -o 'logs\ingame-0.3.2\bradley-spaced-42-closed.png' -D 'cutaway=false' --projection=ortho --imgsize=1200,800 --camera=0,0,0,58,0,28,1200 'logs\ingame-0.3.2\view-bradley-spaced-42.scad' *> 'logs\ingame-0.3.2\closed-render.log'
if($LASTEXITCODE){throw 'Closed-box render failed'}
Add-Type -AssemblyName System.Drawing
$sheet=New-Object Drawing.Bitmap 800,360
$graphics=[Drawing.Graphics]::FromImage($sheet)
try {
    $graphics.Clear([Drawing.ColorTranslator]::FromHtml('#1E1F1C'))
    $graphics.InterpolationMode='HighQualityBicubic'
    $icon=[Drawing.Image]::FromFile((Join-Path (Get-Location) 'Assets\belt-feed-box-icon.png'))
    try {
        $graphics.DrawImage($icon,24,24,256,256)
        $graphics.DrawImage($icon,312,120,128,128)
        $graphics.DrawImage($icon,488,152,64,64)
        $graphics.DrawImage($icon,600,168,32,32)
        $sheet.Save((Join-Path (Get-Location) 'logs\ingame-0.3.2\icon-contact-sheet.png'),[Drawing.Imaging.ImageFormat]::Png)
    } finally {$icon.Dispose()}
} finally {$graphics.Dispose();$sheet.Dispose()}
