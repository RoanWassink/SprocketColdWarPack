$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Drawing
$sourceRoot=Split-Path $PSScriptRoot -Parent
$renders=Join-Path $sourceRoot 'bin\renders'
$raw=[Drawing.Image]::FromFile((Join-Path $renders 'compositeSideModule-icon-raw.png'))
$icon=New-Object Drawing.Bitmap 512,512
$g=[Drawing.Graphics]::FromImage($icon)
$g.DrawImage($raw,0,0,512,512);$raw.Dispose()
$g.FillRectangle([Drawing.Brushes]::Black,0,410,512,102)
$font=New-Object Drawing.Font 'Arial',21.75,([Drawing.FontStyle]::Bold),([Drawing.GraphicsUnit]::Point)
$format=New-Object Drawing.StringFormat;$format.Alignment=[Drawing.StringAlignment]::Center
$g.TextRenderingHint=[Drawing.Text.TextRenderingHint]::AntiAliasGridFit
$g.DrawString('CR2 TES',$font,[Drawing.Brushes]::White,[Drawing.RectangleF]::new(0,421,512,40),$format)
$g.DrawString('SIDE MODULE',$font,[Drawing.Brushes]::White,[Drawing.RectangleF]::new(0,458,512,40),$format)
$g.Dispose();$font.Dispose();$format.Dispose()
$icon.Save((Join-Path $renders 'compositeSideModule-icon.png'),[Drawing.Imaging.ImageFormat]::Png)
$icon.Save((Join-Path $sourceRoot 'Material\files\BepInEx\plugins\SprocketExpandedBallistics\era-assets\compositeSideModule-icon.png'),[Drawing.Imaging.ImageFormat]::Png);$icon.Dispose()
# Label the rendered physical arrow. Keep the unlabelled render for reproducibility.
$wallPath=Join-Path $renders 'compositeSideModule-vertical-wall.png'
$wall=[Drawing.Image]::FromFile($wallPath);$labelled=New-Object Drawing.Bitmap $wall.Width,$wall.Height
$g=[Drawing.Graphics]::FromImage($labelled);$g.DrawImage($wall,0,0,$wall.Width,$wall.Height);$wall.Dispose()
$font=New-Object Drawing.Font 'Arial',22,([Drawing.FontStyle]::Bold)
$g.TextRenderingHint=[Drawing.Text.TextRenderingHint]::AntiAliasGridFit
$g.DrawString('Local +Y (exterior)',$font,[Drawing.Brushes]::Gold,760,855)
$g.Dispose();$font.Dispose();$labelled.Save($wallPath,[Drawing.Imaging.ImageFormat]::Png);$labelled.Dispose()
