param([string]$Output = "$PSScriptRoot/branding")
Add-Type -AssemblyName System.Drawing
New-Item -ItemType Directory -Force $Output | Out-Null
function Draw-Logo([int]$size) {
 $b = New-Object Drawing.Bitmap($size,$size); $g=[Drawing.Graphics]::FromImage($b); $g.SmoothingMode='AntiAlias'; $g.Clear([Drawing.Color]::FromArgb(6,14,32))
 $brush=New-Object Drawing.Drawing2D.LinearGradientBrush([Drawing.Point]::new(0,0),[Drawing.Point]::new($size,$size),[Drawing.Color]::FromArgb(36,157,255),[Drawing.Color]::FromArgb(99,56,239))
 $g.FillEllipse($brush,$size*.08,$size*.08,$size*.84,$size*.84)
 $white=[Drawing.Brushes]::White
 $g.FillRectangle($white,$size*.29,$size*.32,$size*.10,$size*.40); $g.FillRectangle($white,$size*.46,$size*.23,$size*.10,$size*.49); $g.FillRectangle($white,$size*.63,$size*.41,$size*.10,$size*.31)
 $pen=New-Object Drawing.Pen([Drawing.Color]::White,($size*.035)); $g.DrawLines($pen,[Drawing.PointF[]]@([Drawing.PointF]::new($size*.28,$size*.46),[Drawing.PointF]::new($size*.51,$size*.28),[Drawing.PointF]::new($size*.72,$size*.42)))
 $g.Dispose(); $pen.Dispose(); $brush.Dispose(); return $b
}
$logo=Draw-Logo 1024; $logo.Save((Join-Path $Output 'app.png'),[Drawing.Imaging.ImageFormat]::Png); $logo.Dispose()
$streams=@(); foreach($size in @(16,32,48,256)){ $b=Draw-Logo $size; $ms=New-Object IO.MemoryStream; $b.Save($ms,[Drawing.Imaging.ImageFormat]::Png); $streams+=,$ms.ToArray(); $ms.Dispose(); $b.Dispose() }
$ico=[IO.File]::Create((Join-Path $Output 'app.ico')); $writer=New-Object IO.BinaryWriter($ico); $writer.Write([uint16]0);$writer.Write([uint16]1);$writer.Write([uint16]4); $offset=6+16*4; $index=0
foreach($size in @(16,32,48,256)){ $dim=if($size-eq256){0}else{$size}; $writer.Write([byte]$dim);$writer.Write([byte]$dim);$writer.Write([byte]0);$writer.Write([byte]0);$writer.Write([uint16]1);$writer.Write([uint16]32);$writer.Write([uint32]$streams[$index].Length);$writer.Write([uint32]$offset);$offset+=$streams[$index].Length;$index++ }
foreach($bytes in $streams){$writer.Write([byte[]]$bytes)}; $writer.Dispose();$ico.Dispose()
foreach($name in @('wizard','dmg')){
 $w=if($name-eq'wizard'){164}else{800};$h=if($name-eq'wizard'){314}else{480}; $b=New-Object Drawing.Bitmap($w,$h);$g=[Drawing.Graphics]::FromImage($b);$g.SmoothingMode='AntiAlias';$g.Clear([Drawing.Color]::FromArgb(6,14,32));$br=New-Object Drawing.Drawing2D.LinearGradientBrush([Drawing.Point]::new(0,0),[Drawing.Point]::new($w,$h),[Drawing.Color]::FromArgb(15,43,83),[Drawing.Color]::FromArgb(43,18,91));$g.FillRectangle($br,0,0,$w,$h)
 $l=Draw-Logo 128; if($name-eq'wizard'){$g.DrawImage($l,28,22,108,108);$font=New-Object Drawing.Font('Arial',15,[Drawing.FontStyle]::Bold);$g.DrawString("PRASA`nRESEARCH",$font,[Drawing.Brushes]::White,15,150);$small=New-Object Drawing.Font('Arial',8);$g.DrawString("Financial intelligence`nfrom official UK filings",$small,[Drawing.Brushes]::LightSteelBlue,15,220);$b.Save((Join-Path $Output 'wizard.bmp'),[Drawing.Imaging.ImageFormat]::Bmp)}
 else{$g.DrawImage($l,36,30,72,72);$font=New-Object Drawing.Font('Arial',25,[Drawing.FontStyle]::Bold);$g.DrawString('PrasaResearch',$font,[Drawing.Brushes]::White,130,37);$small=New-Object Drawing.Font('Arial',12);$g.DrawString('Drag the application to Applications to install.',$small,[Drawing.Brushes]::LightSteelBlue,132,84);$pen=New-Object Drawing.Pen([Drawing.Color]::FromArgb(117,128,255),5);$g.DrawLine($pen,335,260,465,260);$g.DrawLine($pen,445,240,465,260);$g.DrawLine($pen,445,280,465,260);$pen.Dispose();$g.DrawString('Revenue intelligence. Evidence you can inspect.',$small,[Drawing.Brushes]::LightSteelBlue,190,425);$b.Save((Join-Path $Output 'dmg-background.png'),[Drawing.Imaging.ImageFormat]::Png)}
 $font.Dispose();$small.Dispose();$l.Dispose();$g.Dispose();$br.Dispose();$b.Dispose()
}
$smallLogo=Draw-Logo 55;$smallLogo.Save((Join-Path $Output 'wizard-small.bmp'),[Drawing.Imaging.ImageFormat]::Bmp);$smallLogo.Dispose()
