$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'Make-PartIcon.ps1') -Check (Join-Path $PSScriptRoot '..\assets\rotator-base-icon.png')
$b=New-Object Drawing.Bitmap 9,9,([Drawing.Imaging.PixelFormat]::Format32bppArgb)
try {
 $g=[Drawing.Graphics]::FromImage($b); $g.Clear([Drawing.Color]::FromArgb(29,31,33)); $g.FillRectangle([Drawing.Brushes]::Gray,2,2,5,5);$g.Dispose()
 $b.SetPixel(4,4,[Drawing.Color]::FromArgb(29,31,33)); [PartIconPixels]::Key($b)
 if($b.GetPixel(0,0).A -ne 0 -or $b.GetPixel(4,4).A -ne 255){throw 'Flood-fill regression'}
 'PASS flood fill retains enclosed background-coloured model pixel'
} finally {$b.Dispose()}
$source=[Drawing.Bitmap]::FromFile((Resolve-Path (Join-Path $PSScriptRoot '..\assets\rotator-base-icon.png')).Path)
$bad=Join-Path $PSScriptRoot 'check-negative.png'
try {$copy=New-Object Drawing.Bitmap $source; $copy.SetPixel(128,90,[Drawing.Color]::Transparent);$copy.Save($bad,[Drawing.Imaging.ImageFormat]::Png);$copy.Dispose()} finally {$source.Dispose()}
try {
 try { & (Join-Path $PSScriptRoot 'Make-PartIcon.ps1') -Check $bad; throw 'Expected check to reject hole' }
 catch {if($_.Exception.Message -notmatch 'enclosed transparent pixels'){throw}; 'PASS Check rejects enclosed transparent pixel'}
} finally {Remove-Item -LiteralPath $bad -ErrorAction SilentlyContinue}
