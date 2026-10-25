# Uygulama simgesini (app.ico) System.Drawing ile çizer. Çok boyutlu, PNG gömülü ICO üretir.
param(
    [string]$OutIco = (Join-Path $PSScriptRoot '..\FotoDuzenleyici.App\app.ico'),
    [string]$PreviewPng = ''
)
Add-Type -AssemblyName System.Drawing
$ErrorActionPreference = 'Stop'

function RoundRect([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = 2 * $r
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

function Draw([int]$s) {
    $bmp = New-Object System.Drawing.Bitmap $s, $s, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'; $g.PixelOffsetMode = 'HighQuality'; $g.Clear([System.Drawing.Color]::Transparent)
    $u = $s / 256.0
    $small = $s -le 24

    # Zemin: yuvarlatılmış kare, mavi degrade
    $bg = RoundRect (8*$u) (8*$u) (240*$u) (240*$u) (52*$u)
    $br = New-Object System.Drawing.Drawing2D.LinearGradientBrush (New-Object System.Drawing.PointF 0,0), (New-Object System.Drawing.PointF $s,$s), ([System.Drawing.Color]::FromArgb(255,14,165,233)), ([System.Drawing.Color]::FromArgb(255,37,99,235))
    $g.FillPath($br, $bg)

    # Arka kart (yığın hissi)
    if (-not $small) {
        $back = RoundRect (70*$u) (44*$u) (140*$u) (128*$u) (14*$u)
        $g.FillPath((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(110,255,255,255))), $back)
    }

    # Ön kart (fotoğraf)
    $fx = 40*$u; $fy = 72*$u; $fw = 164*$u; $fh = 140*$u
    if ($small) { $fx = 36*$u; $fy = 56*$u; $fw = 184*$u; $fh = 156*$u }
    $card = RoundRect $fx $fy $fw $fh (16*$u)
    $g.FillPath([System.Drawing.Brushes]::White, $card)

    # Takvim şeridi
    $hdrH = if ($small) { 0 } else { 30*$u }
    if ($hdrH -gt 0) {
        $g.SetClip($card)
        $g.FillRectangle((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255,239,68,68))), $fx, $fy, $fw, $hdrH)
        $g.ResetClip()
        $ring = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255,255,255,255))
        foreach ($cx in @(($fx + $fw*0.30), ($fx + $fw*0.70))) {
            $g.FillEllipse($ring, $cx - 6*$u, $fy + $hdrH/2 - 6*$u, 12*$u, 12*$u)
        }
    }

    # Manzara: güneş + dağlar
    $ix = $fx + 14*$u; $iy = $fy + $hdrH + 12*$u; $iw = $fw - 28*$u; $ih = $fh - $hdrH - 26*$u
    $g.SetClip((RoundRect $ix $iy $iw $ih (8*$u)))
    $g.FillRectangle((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255,224,242,254))), $ix, $iy, $iw, $ih)
    $sun = 26*$u
    $g.FillEllipse((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255,245,158,11))), $ix + $iw*0.66, $iy + $ih*0.14, $sun, $sun)
    $m1 = @( (New-Object System.Drawing.PointF $ix, ($iy+$ih)), (New-Object System.Drawing.PointF ($ix+$iw*0.38), ($iy+$ih*0.30)), (New-Object System.Drawing.PointF ($ix+$iw*0.76), ($iy+$ih)) )
    $m2 = @( (New-Object System.Drawing.PointF ($ix+$iw*0.40), ($iy+$ih)), (New-Object System.Drawing.PointF ($ix+$iw*0.72), ($iy+$ih*0.52)), (New-Object System.Drawing.PointF ($ix+$iw*1.04), ($iy+$ih)) )
    $g.FillPolygon((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255,22,163,74))), $m1)
    $g.FillPolygon((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255,21,128,61))), $m2)
    $g.ResetClip()

    $g.Dispose()
    return $bmp
}

# 256 → PNG; küçük boyutlar → klasik 32-bit DIB (tüm Windows bileşenleri okuyabilsin diye)
function ToDib([System.Drawing.Bitmap]$b) {
    $s = $b.Width
    $ms = New-Object System.IO.MemoryStream
    $w = New-Object System.IO.BinaryWriter $ms
    $maskRow = [int]([math]::Ceiling($s / 32.0) * 4)
    $w.Write([uint32]40); $w.Write([int32]$s); $w.Write([int32]($s * 2))
    $w.Write([uint16]1); $w.Write([uint16]32); $w.Write([uint32]0)
    $w.Write([uint32]($s * $s * 4 + $maskRow * $s))
    $w.Write([int32]0); $w.Write([int32]0); $w.Write([uint32]0); $w.Write([uint32]0)
    for ($y = $s - 1; $y -ge 0; $y--) {
        for ($x = 0; $x -lt $s; $x++) {
            $c = $b.GetPixel($x, $y)
            $w.Write([byte]$c.B); $w.Write([byte]$c.G); $w.Write([byte]$c.R); $w.Write([byte]$c.A)
        }
    }
    $w.Write((New-Object byte[] ($maskRow * $s)))
    $w.Flush()
    return , $ms.ToArray()
}

$sizes = 16, 24, 32, 48, 64, 128, 256
$pngs = foreach ($s in $sizes) {
    $b = Draw $s
    if ($s -ge 256) {
        $ms = New-Object System.IO.MemoryStream
        $b.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
        $data = $ms.ToArray()
    } else { $data = ToDib $b }
    if ($PreviewPng -and $s -eq 256) { $b.Save($PreviewPng, [System.Drawing.Imaging.ImageFormat]::Png) }
    $b.Dispose()
    , $data
}

# ICO dosyası: ICONDIR + ICONDIRENTRY[] + PNG verileri
$fs = [System.IO.File]::Create((Resolve-Path -LiteralPath (Split-Path $OutIco) | Join-Path -ChildPath (Split-Path $OutIco -Leaf)))
$w = New-Object System.IO.BinaryWriter $fs
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $len = $pngs[$i].Length
    $w.Write([byte]($(if ($s -ge 256) { 0 } else { $s })))
    $w.Write([byte]($(if ($s -ge 256) { 0 } else { $s })))
    $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32)
    $w.Write([uint32]$len); $w.Write([uint32]$offset)
    $offset += $len
}
foreach ($p in $pngs) { $w.Write($p) }
$w.Close()
Write-Host "Simge yazildi: $OutIco"
