Add-Type -AssemblyName System.Drawing

function New-RoundedPath([float]$x,[float]$y,[float]$w,[float]$h,[float]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

function Render-Icon([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)

    $s = $size / 256.0
    $bg = New-RoundedPath (8*$s) (8*$s) (240*$s) (240*$s) (48*$s)
    $grad = New-Object System.Drawing.Drawing2D.LinearGradientBrush ([System.Drawing.PointF]::new(0,0)), ([System.Drawing.PointF]::new(0,$size)), ([System.Drawing.Color]::FromArgb(245, 19, 31, 51)), ([System.Drawing.Color]::FromArgb(245, 8, 14, 26))
    $g.FillPath($grad, $bg)
    $border = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(180, 92, 122, 168)), (2.5*$s)
    $g.DrawPath($border, $bg)

    # chip outline
    $chip = New-RoundedPath (142*$s) (72*$s) (74*$s) (74*$s) (12*$s)
    $chipPen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(255, 139, 196, 235)), (6*$s)
    $g.DrawPath($chipPen, $chip)
    $pinPen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(255, 139, 196, 235)), (5*$s)
    foreach($py in @(86, 109, 132)) {
        $g.DrawLine($pinPen, 126*$s, $py*$s, 142*$s, $py*$s)
        $g.DrawLine($pinPen, 216*$s, $py*$s, 232*$s, $py*$s)
    }
    foreach($px in @(157, 180, 203)) {
        $g.DrawLine($pinPen, $px*$s, 56*$s, $px*$s, 72*$s)
        $g.DrawLine($pinPen, $px*$s, 146*$s, $px*$s, 162*$s)
    }
    # chip core
    $core = New-RoundedPath (163*$s) (93*$s) (32*$s) (32*$s) (6*$s)
    $coreBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush ([System.Drawing.PointF]::new(163*$s,93*$s)), ([System.Drawing.PointF]::new(195*$s,125*$s)), ([System.Drawing.Color]::FromArgb(255, 118, 226, 190)), ([System.Drawing.Color]::FromArgb(255, 62, 157, 214))
    $g.FillPath($coreBrush, $core)

    # thermometer tube
    $tube = New-RoundedPath (70*$s) (48*$s) (40*$s) (132*$s) (20*$s)
    $tubeBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush ([System.Drawing.PointF]::new(70*$s,48*$s)), ([System.Drawing.PointF]::new(110*$s,180*$s)), ([System.Drawing.Color]::FromArgb(255, 235, 246, 255)), ([System.Drawing.Color]::FromArgb(255, 166, 204, 232))
    $g.FillPath($tubeBrush, $tube)
    $tubePen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(255, 231, 243, 255)), (4*$s)
    $g.DrawPath($tubePen, $tube)

    # mercury column
    $mercBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush ([System.Drawing.PointF]::new(80*$s,90*$s)), ([System.Drawing.PointF]::new(100*$s,190*$s)), ([System.Drawing.Color]::FromArgb(255, 255, 120, 120)), ([System.Drawing.Color]::FromArgb(255, 225, 42, 58))
    $merc = New-RoundedPath (83*$s) (96*$s) (14*$s) (72*$s) (7*$s)
    $g.FillPath($mercBrush, $merc)

    # bulb
    $bulb = New-Object System.Drawing.Drawing2D.GraphicsPath
    $bulb.AddEllipse(64*$s, 158*$s, 52*$s, 52*$s)
    $g.FillPath($mercBrush, $bulb)
    $bulbPen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(255, 255, 170, 170)), (3*$s)
    $g.DrawPath($bulbPen, $bulb)

    # small highlight
    $hl = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(110,255,255,255))
    $g.FillEllipse($hl, 74*$s, 62*$s, 7*$s, 74*$s)

    $g.Dispose()
    return $bmp
}

function Get-DibBytes([System.Drawing.Bitmap]$bmp) {
    $w = $bmp.Width; $h = $bmp.Height
    $rect = New-Object System.Drawing.Rectangle 0,0,$w,$h
    $locked = $bmp.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $stride = $locked.Stride
    $pixels = New-Object byte[] ($stride * $h)
    [Runtime.InteropServices.Marshal]::Copy($locked.Scan0, $pixels, 0, $pixels.Length)
    $bmp.UnlockBits($locked)
    $ms = New-Object System.IO.MemoryStream
    $bw = New-Object System.IO.BinaryWriter $ms
    $bw.Write([UInt32]40)
    $bw.Write([Int32]$w)
    $bw.Write([Int32]($h * 2))
    $bw.Write([UInt16]1)
    $bw.Write([UInt16]32)
    $bw.Write([UInt32]0)
    $bw.Write([UInt32]($w * $h * 4))
    $bw.Write([Int32]0); $bw.Write([Int32]0)
    $bw.Write([UInt32]0); $bw.Write([UInt32]0)
    for($y = $h - 1; $y -ge 0; $y--) { $bw.Write($pixels, $y * $stride, $w * 4) }
    $maskStride = [int]([Math]::Ceiling($w / 32.0) * 4)
    $mask = New-Object byte[] ($maskStride * $h)
    $bw.Write($mask)
    $bw.Flush()
    $bytes = $ms.ToArray()
    $bw.Close(); $ms.Close()
    return ,$bytes
}

$sizes = @(256,128,64,48,32,16)
$frames = @()
foreach($sz in $sizes) {
    $bmp = Render-Icon $sz
    $frames += ,(Get-DibBytes $bmp)
    $bmp.Dispose()
}

$out = Join-Path $PSScriptRoot 'app.ico'
$fs = [System.IO.File]::Create($out)
$bw = New-Object System.IO.BinaryWriter $fs
$bw.Write([UInt16]0); $bw.Write([UInt16]1); $bw.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for($i = 0; $i -lt $sizes.Count; $i++) {
    $sz = $sizes[$i]; $data = $frames[$i]
    $dim = if($sz -ge 256){0}else{$sz}
    $bw.Write([Byte]$dim); $bw.Write([Byte]$dim)
    $bw.Write([Byte]0); $bw.Write([Byte]0)
    $bw.Write([UInt16]1); $bw.Write([UInt16]32)
    $bw.Write([UInt32]$data.Length); $bw.Write([UInt32]$offset)
    $offset += $data.Length
}
for($i = 0; $i -lt $frames.Count; $i++){ $bw.Write($frames[$i]) }
$bw.Flush(); $bw.Close(); $fs.Close()
Get-Item $out | Select-Object FullName,Length,LastWriteTime
