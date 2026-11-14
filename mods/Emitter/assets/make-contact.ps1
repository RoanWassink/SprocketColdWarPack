Add-Type -AssemblyName System.Drawing
$icon=[Drawing.Bitmap]::FromFile((Join-Path $PSScriptRoot 'emitter-icon.png'))
$sheet=New-Object Drawing.Bitmap 640,600
$g=[Drawing.Graphics]::FromImage($sheet)
$g.InterpolationMode=[Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$g.Clear([Drawing.Color]::FromArgb(30,31,28))
$g.FillRectangle([Drawing.Brushes]::White,0,300,640,300)
foreach($y in @(20,320)) { $g.DrawImage($icon,30,$y,256,256); $g.DrawImage($icon,350,($y+96),64,64) }
$g.Dispose(); $icon.Dispose(); $sheet.Save((Join-Path $PSScriptRoot 'contact-sheet.png'),[Drawing.Imaging.ImageFormat]::Png); $sheet.Dispose()
