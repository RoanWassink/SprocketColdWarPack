$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$makeIcon = "c:\Users\Roanj\Documents\Codex\.claude\skills\sprocket-bepinex\scripts\Make-PartIcon.ps1"
$scad = Join-Path $here "wheeled-suspension.scad"
$outIcon = Join-Path $here "wheeled-suspension-icon.png"
$outSheet = Join-Path $here "contact-sheet.png"

& powershell -NoProfile -ExecutionPolicy Bypass -File $makeIcon -Scad $scad -Label "WHEELED" -Out $outIcon
& powershell -NoProfile -ExecutionPolicy Bypass -File $makeIcon -Check $outIcon

# Build contact sheet on #1E1F1C at 1:1 (256px) and 64px preview
Add-Type -AssemblyName System.Drawing
$icon = [Drawing.Bitmap]::FromFile($outIcon)
$sheet = New-Object Drawing.Bitmap 420, 316, ([Drawing.Imaging.PixelFormat]::Format32bppArgb)
try {
    $g = [Drawing.Graphics]::FromImage($sheet)
    $g.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::HighQuality
    $g.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.Clear([Drawing.Color]::FromArgb(255, 0x1E, 0x1F, 0x1C))

    # 1:1 tile
    $g.DrawImage($icon, 20, 30, 256, 256)
    # 64px selector-size tile
    $g.DrawImage($icon, 316, 126, 64, 64)

    $font = New-Object Drawing.Font("Arial", 11, [Drawing.FontStyle]::Bold)
    $brush = New-Object Drawing.SolidBrush([Drawing.Color]::FromArgb(220, 230, 230, 230))
    $g.DrawString("256x256 (1:1)", $font, $brush, 20, 8)
    $g.DrawString("64x64 (UI)", $font, $brush, 310, 102)
    $font.Dispose()
    $brush.Dispose()
    $g.Dispose()
    $sheet.Save($outSheet, [Drawing.Imaging.ImageFormat]::Png)
    Write-Output "Saved contact sheet: $outSheet"
} finally {
    $icon.Dispose()
    $sheet.Dispose()
}
