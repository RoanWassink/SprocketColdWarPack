$ErrorActionPreference='Stop'
$shared=Join-Path $PSScriptRoot 'Make-PartIcon.ps1'
$items=@(
 @('telescope-base','TELESCOPE BASE'),@('telescope-part','TELESCOPE PART'),
 @('hinge-base','HINGE BASE'),@('hinge-leaf','HINGE LEAF'),@('hinge-mount','HINGE MOUNT'),
 @('rotator-base','ROTATOR BASE'),@('rotator-plate','ROTATOR PLATE'))
foreach($item in $items){
 $argsMap=@{Scad=(Join-Path $PSScriptRoot ($item[0]+'.scad')); Label=$item[1]; Out=(Join-Path $PSScriptRoot ('..\assets\'+$item[0]+'-icon.png'))}
 if($item[0] -eq 'hinge-mount'){$argsMap.Hidden=$true}
 & $shared @argsMap
}
foreach($item in $items){& $shared -Check (Join-Path $PSScriptRoot ('..\assets\'+$item[0]+'-icon.png'))}
Add-Type -AssemblyName System.Drawing
$sheet=New-Object Drawing.Bitmap 1792,840,([Drawing.Imaging.PixelFormat]::Format32bppArgb)
$g=[Drawing.Graphics]::FromImage($sheet)
try {
 $g.Clear([Drawing.ColorTranslator]::FromHtml('#1E1F1C'));$g.FillRectangle([Drawing.Brushes]::White,0,584,1792,256);$g.InterpolationMode='HighQualityBicubic'
 for($i=0;$i -lt $items.Count;$i++){
  $img=[Drawing.Image]::FromFile((Join-Path $PSScriptRoot ('..\assets\'+$items[$i][0]+'-icon.png')))
  try {$g.DrawImage($img,$i*256,0,256,256);$g.DrawImage($img,$i*256+96,266,64,64);$g.DrawImage($img,$i*256,584,256,256)} finally {$img.Dispose()}
 }
 $sheet.Save((Join-Path $PSScriptRoot 'contact-sheet.png'),[Drawing.Imaging.ImageFormat]::Png)
} finally {$g.Dispose();$sheet.Dispose()}
