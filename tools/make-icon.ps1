# Generates Clonedivers\clonedivers.ico: a stylised clone-trooper helmet on Republic navy.
# Pure System.Drawing, no external tools.  Run:  powershell -ExecutionPolicy Bypass -File tools\make-icon.ps1
# The helmet itself is drawn by helmet-draw.ps1, which make-banner.ps1 shares.
param([string]$Out = (Join-Path $PSScriptRoot "..\Clonedivers\clonedivers.ico"))

Add-Type -AssemblyName System.Drawing
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'helmet-draw.ps1')

function DrawIcon([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)
    Draw-Helmet $g 0 0 $size ([System.Drawing.Color]::FromArgb(255, 11, 16, 32))
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
