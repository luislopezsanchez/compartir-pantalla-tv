# Genera icon.ico (16-256 px) y icon-preview.png: TV con ondas de transmision sobre degradado.
Add-Type -AssemblyName System.Drawing
$dir = Split-Path -Parent $MyInvocation.MyCommand.Path

function New-RoundRect([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

function New-IconBitmap([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.PixelOffsetMode = 'HighQuality'
    $g.Clear([System.Drawing.Color]::Transparent)
    $s = $size / 256.0
    $g.ScaleTransform($s, $s)
    $small = $size -le 32

    # Fondo: cuadrado redondeado con degradado azul -> violeta
    $bg = New-RoundRect 8 8 240 240 52
    $grad = New-Object System.Drawing.Drawing2D.LinearGradientBrush (New-Object System.Drawing.PointF 0, 0), (New-Object System.Drawing.PointF 256, 256), ([System.Drawing.Color]::FromArgb(255, 37, 99, 235)), ([System.Drawing.Color]::FromArgb(255, 147, 51, 234))
    $g.FillPath($grad, $bg)
    $shine = New-Object System.Drawing.Drawing2D.LinearGradientBrush (New-Object System.Drawing.PointF 0, 8), (New-Object System.Drawing.PointF 0, 250), ([System.Drawing.Color]::FromArgb(50, 255, 255, 255)), ([System.Drawing.Color]::FromArgb(0, 255, 255, 255))
    $g.FillPath($shine, $bg)

    $white = [System.Drawing.Color]::White
    $stroke = if ($small) { 18 } else { 13 }

    # TV: marco blanco con pantalla oscura
    $tv = New-RoundRect 40 54 176 118 16
    $g.FillPath((New-Object System.Drawing.SolidBrush $white), $tv)
    $screen = New-RoundRect 54 68 148 90 7
    $g.FillPath((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 17, 24, 39))), $screen)
    # Pie
    $pen = New-Object System.Drawing.Pen $white, $stroke
    $pen.StartCap = 'Round'; $pen.EndCap = 'Round'
    $g.DrawLine($pen, 128, 176, 128, 196)
    $g.DrawLine($pen, 92, 202, 164, 202)

    # Ondas de transmision dentro de la pantalla (esquina inferior izquierda)
    $wave = [System.Drawing.Color]::FromArgb(255, 96, 205, 255)
    $wp = New-Object System.Drawing.Pen $wave, $(if ($small) { 12 } else { 9 })
    $wp.StartCap = 'Round'; $wp.EndCap = 'Round'
    $ox = 66; $oy = 148
    $g.FillEllipse((New-Object System.Drawing.SolidBrush $wave), $ox - 8, $oy - 8, 16, 16)
    $radii = if ($small) { @(36, 66) } else { @(28, 50, 72) }
    foreach ($r in $radii) { $g.DrawArc($wp, $ox - $r, $oy - $r, 2 * $r, 2 * $r, 270, 90) }

    $g.Dispose()
    return $bmp
}

$sizes = 256, 128, 64, 48, 32, 24, 16
$pngs = @()
foreach ($sz in $sizes) {
    $b = New-IconBitmap $sz
    $ms = New-Object System.IO.MemoryStream
    $b.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    if ($sz -eq 256) { $b.Save((Join-Path $dir 'icon-preview.png'), [System.Drawing.Imaging.ImageFormat]::Png) }
    $b.Dispose()
    $pngs += , $ms.ToArray()
}

# Formato ICO con imagenes PNG embebidas
$out = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter $out
$w.Write([UInt16]0); $w.Write([UInt16]1); $w.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $sz = $sizes[$i]
    $w.Write([byte]($sz % 256)); $w.Write([byte]($sz % 256)); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([UInt16]1); $w.Write([UInt16]32)
    $w.Write([UInt32]$pngs[$i].Length); $w.Write([UInt32]$offset)
    $offset += $pngs[$i].Length
}
foreach ($p in $pngs) { $w.Write($p) }
$w.Flush()
[System.IO.File]::WriteAllBytes((Join-Path $dir 'icon.ico'), $out.ToArray())
"icon.ico creado"
