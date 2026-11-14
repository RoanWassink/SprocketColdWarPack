<#
Create: powershell -File Make-PartIcon.ps1 -Scad part.scad -Label 'PART NAME' -Out part-icon.png [-Hidden] [-D @{name='value'}]
Check:  powershell -File Make-PartIcon.ps1 -Check part-icon.png
-D values are OpenSCAD expressions, e.g. @{diameter='42'}.
#>
param([string]$Scad,[string]$Label,[string]$Out,[switch]$Hidden,[hashtable]$D=@{},[string]$Check)
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Drawing
# Border-connected flood fills: four-neighbour connectivity, RGB Manhattan distance.
if (-not ('PartIconPixels' -as [type])) {
 Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @"
using System;
using System.Drawing;
public static class PartIconPixels {
 static bool Match(Color p, Color bg) {
  return Math.Abs(p.R-bg.R)+Math.Abs(p.G-bg.G)+Math.Abs(p.B-bg.B)<=6;
 }
 public static void Key(Bitmap b) {
  int w=b.Width,h=b.Height; Color bg=b.GetPixel(0,0);
  bool[] seen=new bool[w*h]; int[] queue=new int[w*h]; int head=0,tail=0;
  Action<int,int> add=(x,y)=>{int i=y*w+x; if(!seen[i]) {seen[i]=true; if(Match(b.GetPixel(x,y),bg)) queue[tail++]=i;}};
  for(int x=0;x<w;x++){add(x,0);add(x,h-1);} for(int y=0;y<h;y++){add(0,y);add(w-1,y);}
  while(head<tail){int i=queue[head++],x=i%w,y=i/w; b.SetPixel(x,y,Color.Transparent);
   if(x>0)add(x-1,y); if(x+1<w)add(x+1,y); if(y>0)add(x,y-1); if(y+1<h)add(x,y+1);
  }
 }
 public static int EnclosedTransparent(Bitmap b) {
  int w=b.Width,h=198; bool[] seen=new bool[w*h]; int[] q=new int[w*h]; int head=0,tail=0;
  Action<int,int> add=(x,y)=>{int i=y*w+x;if(!seen[i]){seen[i]=true;if(b.GetPixel(x,y).A==0)q[tail++]=i;}};
  for(int x=0;x<w;x++){add(x,0);add(x,h-1);} for(int y=0;y<h;y++){add(0,y);add(w-1,y);}
  while(head<tail){int i=q[head++],x=i%w,y=i/w;if(x>0)add(x-1,y);if(x+1<w)add(x+1,y);if(y>0)add(x,y-1);if(y+1<h)add(x,y+1);if(x>0&&y>0)add(x-1,y-1);if(x+1<w&&y>0)add(x+1,y-1);if(x>0&&y+1<h)add(x-1,y+1);if(x+1<w&&y+1<h)add(x+1,y+1);}
  int count=0;for(int y=0;y<h;y++)for(int x=0;x<w;x++)if(!seen[y*w+x] && b.GetPixel(x,y).A==0)count++;
  return count;
 }
}
"@
}
if ($Check) {
 $b=[Drawing.Bitmap]::FromFile((Resolve-Path -LiteralPath $Check).Path)
 try {
  $errors=New-Object 'System.Collections.Generic.List[string]'
  if ($b.Width -ne 256 -or $b.Height -ne 256) { $errors.Add('size must be 256x256') }
  if ($b.PixelFormat -ne [Drawing.Imaging.PixelFormat]::Format32bppArgb) { $errors.Add('must be 32-bit RGBA') }
  if ($b.Width -eq 256 -and $b.Height -eq 256) {
   if ($b.GetPixel(0,0).A -ne 0 -or $b.GetPixel(255,0).A -ne 0) {$errors.Add('top corners not transparent')}
   foreach($x in 0,255) { $c=$b.GetPixel($x,255); if ($c.A -ne 210 -or $c.R -ne 13 -or $c.G -ne 18 -or $c.B -ne 12) {$errors.Add('band colour incorrect'); break} }
   $outside=$false; $visible=$false
   for($y=0;$y -lt 198;$y++){for($x=0;$x -lt 256;$x++){if($b.GetPixel($x,$y).A -gt 0){$visible=$true; if($x -lt 8 -or $x -gt 247){$outside=$true}}}}
   if($outside){$errors.Add('picture outside 240 px box')}; if(-not $visible){$errors.Add('picture missing')}
   $holes=[PartIconPixels]::EnclosedTransparent($b); if($holes -gt 0){$errors.Add("$holes enclosed transparent pixels (verify whether a genuine opening)")}
   for($y=198;$y -lt 256;$y++){for($x=0;$x -lt 256;$x++){ $c=$b.GetPixel($x,$y); if($c.A -lt 210 -or $c.R -lt 13 -or $c.G -lt 18 -or $c.B -lt 12){$errors.Add('invalid band pixel'); $y=256; break} }}
  }
  if($errors.Count){throw ($errors -join '; ')}
  Write-Output "PASS $Check"
 } finally {$b.Dispose()}
 return
}
if(-not $Scad -or -not $Label -or -not $Out){throw 'Supply -Scad, -Label and -Out, or -Check.'}
$scadPath=(Resolve-Path -LiteralPath $Scad).Path
$rawPath=Join-Path ([IO.Path]::GetTempPath()) ("part-icon-"+[guid]::NewGuid().ToString('N')+'.png')
$maskPath=Join-Path ([IO.Path]::GetTempPath()) ("part-mask-"+[guid]::NewGuid().ToString('N')+'.png')
try {
 $argsList=@('-o',$rawPath,'--imgsize=512,396','--projection=p','--colorscheme=Tomorrow Night','--camera=0,0,0,58,0,28,1000','--viewall','--autocenter')
 foreach($key in ($D.Keys | Sort-Object)) {if($key -notmatch '^[A-Za-z_][A-Za-z0-9_]*$'){throw "Invalid define: $key"}; $argsList+=@('-D',"$key=$($D[$key])")}
 $argsList+=$scadPath
 & 'C:\Program Files\OpenSCAD\openscad.com' @argsList
 if($LASTEXITCODE -ne 0 -or -not (Test-Path $rawPath)){throw 'OpenSCAD render failed'}
 # A second background supplies a silhouette mask when a dark model face matches Tomorrow Night.
 $maskArgs=@($argsList); $maskArgs[1]=$maskPath; $maskArgs[4]='--colorscheme=Cornfield'
 & 'C:\Program Files\OpenSCAD\openscad.com' @maskArgs
 if($LASTEXITCODE -ne 0 -or -not (Test-Path $maskPath)){throw 'OpenSCAD mask render failed'}
 $source=[Drawing.Bitmap]::FromFile($rawPath)
 $raw=New-Object Drawing.Bitmap $source.Width,$source.Height,([Drawing.Imaging.PixelFormat]::Format32bppArgb)
 $copy=[Drawing.Graphics]::FromImage($raw); $copy.DrawImageUnscaled($source,0,0); $copy.Dispose(); $source.Dispose()
 try {
  $original=$raw.Clone()
  [PartIconPixels]::Key($raw)
  $maskSource=[Drawing.Bitmap]::FromFile($maskPath)
  $mask=New-Object Drawing.Bitmap $maskSource.Width,$maskSource.Height,([Drawing.Imaging.PixelFormat]::Format32bppArgb)
  $mg=[Drawing.Graphics]::FromImage($mask); $mg.DrawImageUnscaled($maskSource,0,0); $mg.Dispose(); $maskSource.Dispose()
  try {
   [PartIconPixels]::Key($mask)
   for($yy=0;$yy -lt $raw.Height;$yy++){for($xx=0;$xx -lt $raw.Width;$xx++){
    if($raw.GetPixel($xx,$yy).A -eq 0 -and $mask.GetPixel($xx,$yy).A -gt 0){$raw.SetPixel($xx,$yy,$original.GetPixel($xx,$yy))}
   }}
  } finally {$mask.Dispose();$original.Dispose()}
  $minX=$raw.Width; $minY=$raw.Height; $maxX=-1; $maxY=-1
  for($y=0;$y -lt $raw.Height;$y++){for($x=0;$x -lt $raw.Width;$x++){
   if($raw.GetPixel($x,$y).A -gt 0){if($x -lt $minX){$minX=$x};if($x -gt $maxX){$maxX=$x};if($y -lt $minY){$minY=$y};if($y -gt $maxY){$maxY=$y}}
  }}
  if($maxX -lt 0){throw 'Empty render'}
  $w=$maxX-$minX+1; $h=$maxY-$minY+1; $scale=[Math]::Min(240/$w,186/$h)
  if($Hidden){$scale=[Math]::Min($scale,170/[Math]::Sqrt($w*$w+$h*$h))}
  $dw=[int][Math]::Round($w*$scale);$dh=[int][Math]::Round($h*$scale)
  $bmp=New-Object Drawing.Bitmap 256,256,([Drawing.Imaging.PixelFormat]::Format32bppArgb)
  $g=[Drawing.Graphics]::FromImage($bmp)
  try {
   $g.Clear([Drawing.Color]::Transparent); $g.InterpolationMode='HighQualityBicubic'; $g.PixelOffsetMode='HighQuality'; $g.SmoothingMode='AntiAlias'
   $dest=New-Object Drawing.Rectangle ([int]((256-$dw)/2)),([int]((198-$dh)/2)),$dw,$dh
   $g.DrawImage($raw,$dest,$minX,$minY,$w,$h,[Drawing.GraphicsUnit]::Pixel)
   if($Hidden){$pen=New-Object Drawing.Pen ([Drawing.ColorTranslator]::FromHtml('#E3C34A')),6; $pen.DashStyle='Dash'; $g.DrawEllipse($pen,36,7,184,184); $pen.Dispose()}
   $brush=New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(210,13,18,12)); $g.FillRectangle($brush,0,198,256,58);$brush.Dispose()
   $g.TextRenderingHint='AntiAliasGridFit'; $format=New-Object Drawing.StringFormat; $format.Alignment='Center';$format.LineAlignment='Center'
   $text=$Label.ToUpperInvariant(); $font=$null
   for($size=27;$size -ge 22;$size--){if($font){$font.Dispose()};$font=New-Object Drawing.Font 'Arial',$size,([Drawing.FontStyle]::Bold),([Drawing.GraphicsUnit]::Pixel);if($g.MeasureString($text,$font).Width -le 240){break}}
   if($g.MeasureString($text,$font).Width -gt 240){$words=$text.Split(' '); $best=$null; $bestWidth=[double]::MaxValue; for($i=1;$i -lt $words.Length;$i++){ $a=($words[0..($i-1)] -join ' ');$z=($words[$i..($words.Length-1)] -join ' ');$width=[Math]::Max($g.MeasureString($a,$font).Width,$g.MeasureString($z,$font).Width);if($width -lt $bestWidth){$bestWidth=$width;$best="$a`n$z"} }; if(-not $best -or $bestWidth -gt 240){throw 'Label cannot fit in two lines'}; $text=$best }
   $g.DrawString($text,$font,[Drawing.Brushes]::White,(New-Object Drawing.RectangleF 0,198,256,58),$format);$font.Dispose();$format.Dispose()
   $target=[IO.Path]::GetFullPath($Out); $bmp.Save($target,[Drawing.Imaging.ImageFormat]::Png)
  } finally {$g.Dispose();$bmp.Dispose()}
 } finally {$raw.Dispose()}
} finally {if(Test-Path $rawPath){Remove-Item -LiteralPath $rawPath -Force}; if(Test-Path $maskPath){Remove-Item -LiteralPath $maskPath -Force}}



