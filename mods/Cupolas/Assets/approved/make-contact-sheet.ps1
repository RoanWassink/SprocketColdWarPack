Add-Type -AssemblyName System.Drawing
$src=[Drawing.Image]::FromFile((Join-Path $PSScriptRoot 't90a-cupola-icon.png'))
$bmp=New-Object Drawing.Bitmap 400,560
$g=[Drawing.Graphics]::FromImage($bmp)
$g.Clear([Drawing.Color]::White)
$dark=New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(30,31,28))
$g.FillRectangle($dark,0,0,400,280)
$g.InterpolationMode=[Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$g.DrawImage($src,10,10,256,256); $g.DrawImage($src,300,106,64,64)
$g.DrawImage($src,10,290,256,256); $g.DrawImage($src,300,386,64,64)
$bmp.Save((Join-Path $PSScriptRoot 'contact-sheet.png'),[Drawing.Imaging.ImageFormat]::Png)
$g.Dispose(); $bmp.Dispose(); $src.Dispose(); $dark.Dispose()
