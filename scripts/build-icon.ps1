#requires -version 5.1
<#
.SYNOPSIS
  Generates a multi-resolution app.ico (16/32/48/256, PNG-encoded).
.DESCRIPTION
  Run once to (re)generate src/MouseGesture.App/Assets/app.ico.
  Uses System.Drawing — Windows only.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Drawing

function New-PngBytes {
    param([int]$Size)

    $bmp = New-Object System.Drawing.Bitmap($Size, $Size)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)

    # Filled rounded square
    $bg = [System.Drawing.Color]::FromArgb(255, 41, 130, 255)
    $brush = New-Object System.Drawing.SolidBrush $bg
    $radius = [Math]::Max(2, [int]($Size * 0.18))
    $rect = New-Object System.Drawing.Rectangle(0, 0, $Size, $Size)
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc($rect.X, $rect.Y, $radius * 2, $radius * 2, 180, 90) | Out-Null
    $path.AddArc($rect.Right - $radius * 2, $rect.Y, $radius * 2, $radius * 2, 270, 90) | Out-Null
    $path.AddArc($rect.Right - $radius * 2, $rect.Bottom - $radius * 2, $radius * 2, $radius * 2, 0, 90) | Out-Null
    $path.AddArc($rect.X, $rect.Bottom - $radius * 2, $radius * 2, $radius * 2, 90, 90) | Out-Null
    $path.CloseFigure()
    $g.FillPath($brush, $path)

    # Arrow stroke
    $penWidth = [Math]::Max(2.0, $Size / 11.0)
    $pen = New-Object System.Drawing.Pen([System.Drawing.Color]::White, $penWidth)
    $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.EndCap   = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round

    $cy = [float]($Size * 0.5)
    $x1 = [float]($Size * 0.22)
    $x2 = [float]($Size * 0.74)
    $head = [float]($Size * 0.16)

    $g.DrawLine($pen, $x1, $cy, $x2, $cy)
    $g.DrawLine($pen, ($x2 - $head), ($cy - $head), $x2, $cy)
    $g.DrawLine($pen, ($x2 - $head), ($cy + $head), $x2, $cy)

    $brush.Dispose()
    $pen.Dispose()
    $path.Dispose()
    $g.Dispose()

    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    return ,$ms.ToArray()
}

$sizes = @(16, 32, 48, 64, 128, 256)
$pngs = @{}
foreach ($s in $sizes) { $pngs[$s] = New-PngBytes -Size $s }

$out = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter($out)

# ICONDIR
$bw.Write([uint16]0)              # reserved
$bw.Write([uint16]1)              # type = icon
$bw.Write([uint16]$sizes.Count)   # count

$dataOffset = 6 + 16 * $sizes.Count
foreach ($s in $sizes) {
    $bytes = $pngs[$s]
    $w = if ($s -ge 256) { [byte]0 } else { [byte]$s }
    $h = $w
    $bw.Write([byte]$w)            # width
    $bw.Write([byte]$h)            # height
    $bw.Write([byte]0)             # colors
    $bw.Write([byte]0)             # reserved
    $bw.Write([uint16]1)           # planes
    $bw.Write([uint16]32)          # bit count
    $bw.Write([uint32]$bytes.Length)
    $bw.Write([uint32]$dataOffset)
    $dataOffset += $bytes.Length
}
foreach ($s in $sizes) { $bw.Write($pngs[$s]) }
$bw.Flush()

$root = Resolve-Path (Join-Path $PSScriptRoot '..')
$dest = Join-Path $root 'src/MouseGesture.App/Assets/app.ico'
$dir  = Split-Path $dest
if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir | Out-Null }
[System.IO.File]::WriteAllBytes($dest, $out.ToArray())
Write-Host "Wrote $dest ($($out.Length) bytes)"
