$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
New-Item -ItemType Directory -Force shots | Out-Null
$scad = 'C:\Program Files\OpenSCAD\openscad.com'
$expected = @{'reference-board-frame.stl'='E5131334F96149F0274C2397F6070E653DDA69C8F207459B247B01FCE1F1EC70';'reference-board-image.stl'='AC92DF4F4F66BBF48693DF9DE98644A4F1BE793DBEB0C73F1AB27C91AFB344D6'}
foreach ($name in $expected.Keys) { if ((Get-FileHash $name -Algorithm SHA256).Hash -ne $expected[$name]) { throw "Hash mismatch: $name" } }
foreach ($i in 0,1) {
 $name = @('reference-board-frame.stl','reference-board-image.stl')[$i]
 $temp = Join-Path $PSScriptRoot "reexport-$name"
 try {
  & $scad -o $temp -D "part=$i" reference-board.scad
  if ($LASTEXITCODE -ne 0) { throw "Re-export failed: $name" }; & py compare-stl.py $name $temp; if ($LASTEXITCODE -ne 0) { throw "Re-export geometry mismatch: $name" }
 } finally { if (Test-Path $temp) { Remove-Item -LiteralPath $temp } }
}
function Render($file,$camera,$extra=@()) {
 & $scad -o $file --imgsize=1200,800 "--camera=$camera" @extra view-reference-board.scad
 if ($LASTEXITCODE -ne 0) { throw "Render failed: $file" }
}
Render shots/three-quarter.png '0,0,0,70,0,25,8'
Render shots/front.png '0,0,0,90,0,0,8'
Render shots/side.png '0,0,0,90,0,90,8'
Render shots/side-detail.png '0,0,0,90,0,90,0.07' @('--projection=o')
Render shots/scale-small.png '1,0,1.2,70,0,30,22' @('-D','comparison=0.1')
Render shots/scale-large.png '4,0,5.5,70,0,25,50' @('-D','comparison=6')
& $scad -o icon-raw.png --imgsize=128,128 --camera=0,0,0,70,0,25,8 --colorscheme=Tomorrow icon-scene.scad
if ($LASTEXITCODE -ne 0) { throw 'Icon render failed' }
Add-Type -AssemblyName System.Drawing
$src = [System.Drawing.Bitmap]::FromFile((Join-Path $PSScriptRoot 'icon-raw.png'))
$dst = New-Object System.Drawing.Bitmap $src.Width,$src.Height,([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$bg = $src.GetPixel(0,0).ToArgb()
for($y=0;$y -lt $src.Height;$y++) { for($x=0;$x -lt $src.Width;$x++) { $c=$src.GetPixel($x,$y); if($c.ToArgb() -eq $bg) { $c=[System.Drawing.Color]::Transparent }; $dst.SetPixel($x,$y,$c) } }
$dst.Save((Join-Path $PSScriptRoot 'reference-image-icon.png'),[System.Drawing.Imaging.ImageFormat]::Png)
$src.Dispose(); $dst.Dispose()
Write-Host 'STL hashes and re-export geometry verified. Phase B must separately verify embedded mesh bytes against these STLs.'


