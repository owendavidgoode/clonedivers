# Generates Clonedivers\clonedivers.ico: a stylised clone-trooper helmet on Republic navy.
# Pure System.Drawing, no external tools.  Run:  powershell -ExecutionPolicy Bypass -File tools\make-icon.ps1
param([string]$Out = (Join-Path $PSScriptRoot "..\Clonedivers\clonedivers.ico"))

Add-Type -AssemblyName System.Drawing
$ErrorActionPreference = 'Stop'

function RoundedRect([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

function Brush([int]$r, [int]$g, [int]$b) { New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, $r, $g, $b)) }

function DrawIcon([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)
    $k = $size / 256.0
    $g.ScaleTransform($k, $k)

    # Background tile
    $g.FillPath((Brush 11 16 32), (RoundedRect 0 0 256 256 52))

    # Helmet shell (clone white): dome + straight cheeks + rounded jaw
    $helm = New-Object System.Drawing.Drawing2D.GraphicsPath
    $helm.AddArc(50, 34, 156, 150, 180, 180)     # dome
    $helm.AddLine(206, 109, 206, 176)            # right cheek
    $helm.AddArc(172, 176, 34, 34, 0, 90)        # jaw right
    $helm.AddLine(189, 210, 67, 210)             # chin
    $helm.AddArc(50, 176, 34, 34, 90, 90)        # jaw left
    $helm.CloseFigure()
    $g.FillPath((Brush 245 247 252), $helm)

    # 501st blue fin down the crown
    $g.FillPath((Brush 31 95 204), (RoundedRect 118 34 20 46 6))

    # T-visor (near black)
    $g.FillPath((Brush 14 17 26), (RoundedRect 72 104 112 24 10))
    $g.FillPath((Brush 14 17 26), (RoundedRect 116 122 24 60 8))

    # Breather vents (armour grey)
    $g.FillPath((Brush 150 160 185), (RoundedRect 70 166 30 20 6))
    $g.FillPath((Brush 150 160 185), (RoundedRect 156 166 30 20 6))

    # Mouth grille
    $g.FillPath((Brush 14 17 26), (RoundedRect 106 190 44 6 3))

    $g.Dispose()
    return $bmp
}

$sizes = 16, 24, 32, 48, 64, 128, 256
$pngs = New-Object System.Collections.ArrayList
foreach ($s in $sizes) {
    $bmp = DrawIcon $s
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    [void]$pngs.Add($ms.ToArray())
    $ms.Dispose(); $bmp.Dispose()
}

# ICO container: header, one directory entry per image, then the PNG blobs.
$Out = [System.IO.Path]::GetFullPath($Out)
$fs = [System.IO.File]::Create($Out)
$bw = New-Object System.IO.BinaryWriter $fs
$bw.Write([UInt16]0); $bw.Write([UInt16]1); $bw.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $blob = $pngs[$i]
    $dim = if ($s -ge 256) { 0 } else { $s }      # 0 means 256 in the ICO directory
    $bw.Write([byte]$dim); $bw.Write([byte]$dim); $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([UInt16]1); $bw.Write([UInt16]32)
    $bw.Write([UInt32]$blob.Length); $bw.Write([UInt32]$offset)
    $offset += $blob.Length
}
foreach ($blob in $pngs) { $bw.Write([byte[]]$blob) }
$bw.Flush(); $bw.Dispose(); $fs.Dispose()

# Also drop a 256px PNG next to the README for the repo page.
$preview = Join-Path (Split-Path $Out) "..\docs\icon.png"
New-Item -ItemType Directory -Force (Split-Path $preview) | Out-Null
$big = DrawIcon 256; $big.Save([System.IO.Path]::GetFullPath($preview), [System.Drawing.Imaging.ImageFormat]::Png); $big.Dispose()

Write-Host "wrote $Out ($((Get-Item $Out).Length) bytes) and docs\icon.png"
