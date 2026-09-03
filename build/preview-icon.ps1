<#
    Renders icon candidates at real desktop sizes so they can be judged before shipping.

    The source logo is drawn for a large canvas: a thin ring, hairline light beams, and no
    backplate. At 16-48px that collapses into mush and, with no plate, it has no contrast
    against an arbitrary wallpaper. These variants test fixes for both problems.
#>
[CmdletBinding()]
param(
    [string]$Source = "$PSScriptRoot\harbor-source.png",
    [string]$OutDir = "$PSScriptRoot\icon-preview"
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$logo = [System.Drawing.Image]::FromFile((Resolve-Path -LiteralPath $Source).Path)

function New-RoundedPath([int]$size, [single]$radius) {
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $radius * 2
    $path.AddArc(0, 0, $d, $d, 180, 90)
    $path.AddArc($size - $d, 0, $d, $d, 270, 90)
    $path.AddArc($size - $d, $size - $d, $d, $d, 0, 90)
    $path.AddArc(0, $size - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    return $path
}

function Render([int]$size, [string]$variant) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.InterpolationMode  = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode    = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.SmoothingMode      = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)

    switch ($variant) {
        "plain" {
            # Current shipping icon: the logo, untouched.
            $g.DrawImage($logo, 0, 0, $size, $size)
        }
        "disc" {
            # White circle, logo zoomed so the thin outer ring falls outside the crop, plus a
            # hairline rim for definition. The lighthouse becomes the whole icon.
            $g.FillEllipse([System.Drawing.Brushes]::White, 0, 0, $size - 1, $size - 1)
            $clip = New-Object System.Drawing.Drawing2D.GraphicsPath
            $clip.AddEllipse(0, 0, $size - 1, $size - 1)
            $g.SetClip($clip)
            $z = [int]($size * 1.34); $o = [int](($size - $z) / 2)
            $g.DrawImage($logo, $o, $o, $z, $z)
            $g.ResetClip()
            $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(110, 110, 112)), ([single]([Math]::Max(1, $size * 0.055)))
            $g.DrawEllipse($pen, $size * 0.03, $size * 0.03, $size * 0.94, $size * 0.94)
            $pen.Dispose(); $clip.Dispose()
        }
        "squareWhite" {
            # Rounded square, zoomed far enough that the logo's own white fill covers the
            # corners - so it reads as a clean white tile carrying the lighthouse.
            $path = New-RoundedPath $size ($size * 0.21)
            $g.FillPath([System.Drawing.Brushes]::White, $path)
            $g.SetClip($path)
            $z = [int]($size * 1.55); $o = [int](($size - $z) / 2)
            $g.DrawImage($logo, $o, $o, $z, $z)
            $g.ResetClip()
            $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(60, 110, 110, 112)), 1.0
            $g.DrawPath($pen, $path)
            $pen.Dispose(); $path.Dispose()
        }
        "slate" {
            # Slate plate matching the app accent, logo zoomed so the thin ring falls
            # outside the plate: at small sizes the plate edge reads better than the ring.
            $path = New-RoundedPath $size ($size * 0.22)
            $brush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(45, 62, 79))
            $g.FillPath($brush, $path)
            $g.SetClip($path)
            $zoom = [int]($size * 1.34)
            $off = [int](($size - $zoom) / 2)
            $g.DrawImage($logo, $off, $off, $zoom, $zoom)
            $g.ResetClip()
            $brush.Dispose(); $path.Dispose()
        }
    }

    $g.Dispose()
    return $bmp
}

# Contact sheet: each variant as a row, rendered at real size then magnified 5x with
# nearest-neighbour so the actual pixels are visible rather than a resampled impression.
$sizes = @(16, 24, 32, 48, 64)
$variants = @("plain", "disc", "squareWhite", "slate")
$zoom = 5
$pad = 14

$rowH = (($sizes | Measure-Object -Maximum).Maximum * $zoom) + $pad
$width = $pad + (($sizes | ForEach-Object { $_ * $zoom + $pad }) | Measure-Object -Sum).Sum
$height = $pad + ($variants.Count * $rowH)

$sheet = New-Object System.Drawing.Bitmap($width, $height, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$sg = [System.Drawing.Graphics]::FromImage($sheet)
$sg.Clear([System.Drawing.Color]::FromArgb(70, 70, 74))   # mid grey: neither light nor dark wallpaper
$sg.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
$sg.PixelOffsetMode   = [System.Drawing.Drawing2D.PixelOffsetMode]::Half

$font = New-Object System.Drawing.Font("Segoe UI", 11)
$white = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::White)

$y = $pad
foreach ($v in $variants) {
    $x = $pad
    foreach ($s in $sizes) {
        $bmp = Render $s $v
        $sg.DrawImage($bmp, $x, $y, $s * $zoom, $s * $zoom)
        $bmp.Dispose()
        $x += $s * $zoom + $pad
    }
    $sg.DrawString($v, $font, $white, [single]($width - 80), [single]($y + 4))
    $y += $rowH
}

$sg.Dispose()
$sheetPath = Join-Path $OutDir "contact-sheet.png"
$sheet.Save($sheetPath, [System.Drawing.Imaging.ImageFormat]::Png)
$sheet.Dispose()
$logo.Dispose()

Write-Host "Wrote $sheetPath  (rows: $($variants -join ', ') | sizes: $($sizes -join ', ') at ${zoom}x)"
