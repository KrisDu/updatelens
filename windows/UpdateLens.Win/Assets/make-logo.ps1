Add-Type -AssemblyName System.Drawing
$ErrorActionPreference = 'Stop'

$assets = Join-Path (Get-Location) 'Assets'
New-Item -ItemType Directory -Force -Path $assets | Out-Null

function New-LogoBitmap([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)

    # Rounded-square background, blue gradient
    $r = [float]$size
    $corner = $r * 0.225
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc(0, 0, $corner * 2, $corner * 2, 180, 90)
    $path.AddArc($r - $corner * 2, 0, $corner * 2, $corner * 2, 270, 90)
    $path.AddArc($r - $corner * 2, $r - $corner * 2, $corner * 2, $corner * 2, 0, 90)
    $path.AddArc(0, $r - $corner * 2, $corner * 2, $corner * 2, 90, 90)
    $path.CloseFigure()
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        (New-Object System.Drawing.PointF(0, 0)),
        (New-Object System.Drawing.PointF($r, $r)),
        [System.Drawing.Color]::FromArgb(0x1A, 0x86, 0xD4),
        [System.Drawing.Color]::FromArgb(0x00, 0x4E, 0x9E))
    $g.FillPath($brush, $path)
    $brush.Dispose()

    # White refresh ring (270 deg arc) with arrowhead
    $pad = $r * 0.26
    $penW = $r * 0.085
    $pen = New-Object System.Drawing.Pen([System.Drawing.Color]::White, $penW)
    $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $g.DrawArc($pen, $pad, $pad, $r - 2 * $pad, $r - 2 * $pad, 35, 270)

    # Arrowhead at arc end (angle 35deg point on circle)
    $cx = $r / 2; $cy = $r / 2; $rad = ($r - 2 * $pad) / 2
    $a = 35 * [Math]::PI / 180
    $tx = $cx + $rad * [Math]::Cos($a); $ty = $cy + $rad * [Math]::Sin($a)
    $s = $r * 0.11
    $p1 = [System.Drawing.PointF]::new($tx + $s * 0.2, $ty - $s)
    $p2 = [System.Drawing.PointF]::new($tx + $s * 0.9, $ty + $s * 0.35)
    $p3 = [System.Drawing.PointF]::new($tx - $s * 0.75, $ty + $s * 0.45)
    $g.FillPolygon([System.Drawing.Brushes]::White, @($p1, $p2, $p3))
    $pen.Dispose()

    # Lens dot in the middle (the "lens" in UpdateLens)
    $dotR = $r * 0.055
    $g.FillEllipse([System.Drawing.Brushes]::White, $cx - $dotR, $cy - $dotR, $dotR * 2, $dotR * 2)

    $g.Dispose()
    return $bmp
}

# Master PNG
$master = New-LogoBitmap 512
$pngPath = Join-Path $assets 'logo.png'
$master.Save($pngPath, [System.Drawing.Imaging.ImageFormat]::Png)
Write-Host "wrote $pngPath"

# Multi-size ICO (PNG-compressed entries: 16, 32, 48, 256)
$sizes = @(16, 32, 48, 256)
$entries = @()
foreach ($s in $sizes) {
    $b = New-Object System.Drawing.Bitmap($master, $s, $s)
    $ms = New-Object System.IO.MemoryStream
    $b.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $entries += @{ Size = $s; Data = $ms.ToArray() }
    $ms.Dispose(); $b.Dispose()
}
$icoPath = Join-Path $assets 'logo.ico'
$fs = [System.IO.File]::Create($icoPath)
$bw = New-Object System.IO.BinaryWriter($fs)
$bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$entries.Count)
$offset = 6 + 16 * $entries.Count
foreach ($e in $entries) {
    $w = if ($e.Size -ge 256) { 0 } else { $e.Size }
    $bw.Write([byte]$w); $bw.Write([byte]$w)
    $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([uint16]1); $bw.Write([uint16]32)
    $bw.Write([uint32]$e.Data.Length); $bw.Write([uint32]$offset)
    $offset += $e.Data.Length
}
foreach ($e in $entries) { $bw.Write($e.Data) }
$bw.Close(); $fs.Close()
$master.Dispose()
Write-Host "wrote $icoPath"
