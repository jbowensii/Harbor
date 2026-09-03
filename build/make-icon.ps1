<#
    Builds src\Harbor\harbor.ico from a square PNG source.

    Windows picks a different size from the .ico depending on where it draws it - 16px in the
    title bar, 32px on the taskbar, 256px in large-icon Explorer views - so all of them are
    embedded rather than letting Windows rescale one bitmap badly.

    Each entry is stored as PNG, which Vista and later read natively.
#>
[CmdletBinding()]
param(
    [string]$Source = "$PSScriptRoot\harbor-source.png",
    [string]$Output = "$PSScriptRoot\..\src\Harbor\harbor.ico"
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

if (-not (Test-Path -LiteralPath $Source)) { throw "Source image not found: $Source" }

$sizes = @(16, 24, 32, 48, 64, 128, 256)
$src = [System.Drawing.Image]::FromFile((Resolve-Path -LiteralPath $Source).Path)

# The source is not always perfectly square; letterbox it so nothing is stretched.
$side = [Math]::Max($src.Width, $src.Height)
$square = New-Object System.Drawing.Bitmap($side, $side, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$sg = [System.Drawing.Graphics]::FromImage($square)
$sg.Clear([System.Drawing.Color]::Transparent)
$sg.DrawImage($src, [int](($side - $src.Width) / 2), [int](($side - $src.Height) / 2), $src.Width, $src.Height)
$sg.Dispose()
$src.Dispose()

# The source logo is drawn for a large canvas: a thin outer ring, hairline light beams and a
# lighthouse occupying maybe a third of the width. Reproduced verbatim at 16-48px it collapses
# into mush, which is what a desktop icon actually gets rendered at.
#
# So the icon is a crop, not a copy: zoom past the outer ring until the lighthouse fills the
# frame, clip to a circle, and add a hairline rim. The rim restores the ring's silhouette at a
# weight that survives 16px, and gives the icon a defined edge against any wallpaper.
# The in-app wordmark still uses the untouched artwork, where there is room for the detail.
$pngs = @()
foreach ($size in $sizes) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.InterpolationMode  = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode    = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.SmoothingMode      = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)

    $g.FillEllipse([System.Drawing.Brushes]::White, 0, 0, $size - 1, $size - 1)

    $clip = New-Object System.Drawing.Drawing2D.GraphicsPath
    $clip.AddEllipse(0, 0, $size - 1, $size - 1)
    $g.SetClip($clip)
    $zoom = [int]($size * 1.34)
    $off  = [int](($size - $zoom) / 2)
    $g.DrawImage($square, $off, $off, $zoom, $zoom)
    $g.ResetClip()
    $clip.Dispose()

    $rim = [single]([Math]::Max(1, $size * 0.055))
    $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(110, 110, 112)), $rim
    $g.DrawEllipse($pen, $size * 0.03, $size * 0.03, $size * 0.94, $size * 0.94)
    $pen.Dispose()

    $g.Dispose()

    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $pngs += ,@{ Size = $size; Bytes = $ms.ToArray() }
    $ms.Dispose(); $bmp.Dispose()
}
$square.Dispose()

$out = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter($out)

# ICONDIR
$w.Write([UInt16]0)                # reserved
$w.Write([UInt16]1)                # type: 1 = icon
$w.Write([UInt16]$pngs.Count)

# ICONDIRENTRY blocks come first, so the offsets are known up front.
$offset = 6 + (16 * $pngs.Count)
foreach ($p in $pngs) {
    $dim = if ($p.Size -ge 256) { 0 } else { $p.Size }   # 0 means 256 in the ICO header
    $w.Write([Byte]$dim)           # width
    $w.Write([Byte]$dim)           # height
    $w.Write([Byte]0)              # palette entries
    $w.Write([Byte]0)              # reserved
    $w.Write([UInt16]1)            # colour planes
    $w.Write([UInt16]32)           # bits per pixel
    $w.Write([UInt32]$p.Bytes.Length)
    $w.Write([UInt32]$offset)
    $offset += $p.Bytes.Length
}

foreach ($p in $pngs) { $w.Write($p.Bytes) }

$w.Flush()
$dir = Split-Path -Parent $Output
if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
[System.IO.File]::WriteAllBytes($Output, $out.ToArray())
$w.Dispose(); $out.Dispose()

$info = Get-Item -LiteralPath $Output
Write-Host "Wrote $($info.FullName) - $($info.Length) bytes, sizes: $($sizes -join ', ')"
