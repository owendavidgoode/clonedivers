# Generates docs\banner.png (README header, 1600x400) and docs\social-preview.png (GitHub social preview, 1280x640):
# the icon's helmet, the CLONEDIVERS wordmark, a blue/orange rule and the subtitle on a rounded navy card.
# Pure System.Drawing; needs Segoe UI, i.e. any Windows.  Run:  powershell -ExecutionPolicy Bypass -File tools\make-banner.ps1
# No version text on purpose: it would go stale with every release. The social preview is uploaded by hand
# (repo Settings -> General -> Social preview); GitHub has no API for it.
# Keep this file ASCII: PowerShell 5.1 reads a BOM-less .ps1 as ANSI, so a literal middle dot renders as two characters.
param([string]$OutDir = "$PSScriptRoot\..\docs")

Add-Type -AssemblyName System.Drawing
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'helmet-draw.ps1')

$Subtitle = "Helldivers 2  $([char]0xB7)  Clone Wars mod switch"    # the app's own subtitle, middle dot included
$Bold     = [System.Drawing.FontStyle]::Bold
$Regular  = [System.Drawing.FontStyle]::Regular
$Px       = [System.Drawing.GraphicsUnit]::Pixel
$Fmt      = [System.Drawing.StringFormat]::GenericTypographic

# Width of $text drawn one glyph at a time with $tracking px after each glyph (how the wordmark is drawn).
function Tracked-Width([System.Drawing.Graphics]$g, [string]$text, [System.Drawing.Font]$font, [float]$tracking) {
    $w = 0.0
    foreach ($ch in $text.ToCharArray()) { $w += $g.MeasureString([string]$ch, $font, 10000, $Fmt).Width + $tracking }
    return $w - $tracking
}

# Distance from the top of a text line's box to the top of the capitals' ink, for $px-pixel capitals in $fam.
function Cap-Top([System.Drawing.FontFamily]$fam, [System.Drawing.FontStyle]$style, [float]$px) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $p.AddString('CLONEDIVERS', $fam, [int]$style, $px, [System.Drawing.PointF]::new(0, 0), $Fmt)
    $top = $p.GetBounds().Top
    $p.Dispose()
    return $top
}

function Draw-Card([int]$w, [int]$h, [float]$pad, [float]$helmetPx, [float]$titlePx, [float]$subPx, [string]$tagline) {
    $bmp = New-Object System.Drawing.Bitmap $w, $h, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit   # not ClearType: the PNG is transparent outside the card
    $g.Clear([System.Drawing.Color]::Transparent)

    # Navy card with rounded corners; everything else is clipped to it.
    $card = RoundedRect 0 0 $w $h 28
    $g.FillPath((Brush 11 16 32), $card)
    $g.SetClip($card)

    $fam = New-Object System.Drawing.FontFamily 'Segoe UI'
    $gap = 0.8 * $pad                                   # helmet to text column
    $textMax = $w - 2 * $pad - $helmetPx - $gap         # the widest the text column may be

    # Wordmark font: the requested size, shrunk only if CLONEDIVERS would not fit the column.
    $title = New-Object System.Drawing.Font $fam, $titlePx, $Bold, $Px
    $wordW = Tracked-Width $g 'CLONEDIVERS' $title (0.05 * $titlePx)
    if ($wordW -gt $textMax) {
        $titlePx = [math]::Floor($titlePx * $textMax / $wordW)
        $title.Dispose(); $title = New-Object System.Drawing.Font $fam, $titlePx, $Bold, $Px
        $wordW = Tracked-Width $g 'CLONEDIVERS' $title (0.05 * $titlePx)
        Write-Host "  wordmark shrunk to $titlePx px to fit the card"
    }
    $sub  = New-Object System.Drawing.Font $fam, $subPx, $Regular, $Px
    $subW = $g.MeasureString($Subtitle, $sub, 10000, $Fmt).Width
    $tagW = if ($tagline) { $g.MeasureString($tagline, $sub, 10000, $Fmt).Width } else { 0 }
    $textW = [math]::Max([math]::Max($wordW, 560), [math]::Max($subW, $tagW))

    # Vertical metrics, relative to the top of the wordmark's line box. Every line is capitals or descender-free,
    # so the ink block runs from the wordmark's cap top to the last line's baseline.
    $titleAscent = $titlePx * $fam.GetCellAscent($Bold) / $fam.GetEmHeight($Bold)
    $subAscent   = $subPx   * $fam.GetCellAscent($Regular) / $fam.GetEmHeight($Regular)
    $capTop   = Cap-Top $fam $Bold $titlePx
    $baseline = $titleAscent
    $ruleY    = $baseline + 0.18 * $titlePx
    $subTop   = $ruleY + 6 + 0.45 * $subPx
    $tagTop   = $subTop + $sub.GetHeight($g) * 1.05
    $blockBottom = if ($tagline) { $tagTop + $subAscent } else { $subTop + $subAscent }

    # Centre the lockup (helmet + text column) on the card, and the text block on the helmet.
    $lockupW = $helmetPx + $gap + $textW
    $hx = ($w - $lockupW) / 2
    $hy = ($h - $helmetPx) / 2
    $x0 = $hx + $helmetPx + $gap
    $y  = $h / 2 - ($capTop + $blockBottom) / 2

    # Helmet on a tile a hair lighter than the card.
    Draw-Helmet $g $hx $hy $helmetPx ([System.Drawing.Color]::FromArgb(255, 18, 25, 46))

    # Wordmark, one glyph at a time so it can be tracked (letter-spaced) evenly.
    $x = $x0
    $white = Brush 240 243 250
    foreach ($ch in 'CLONEDIVERS'.ToCharArray()) {
        $s = [string]$ch
        $g.DrawString($s, $title, $white, $x, $y, $Fmt)
        $x += $g.MeasureString($s, $title, 10000, $Fmt).Width + 0.05 * $titlePx
    }

    # Rule under the wordmark: 501st blue, with 212th orange over its first 72 px.
    $g.FillRectangle((Brush 31 95 204),  $x0, $y + $ruleY, 560, 6)
    $g.FillRectangle((Brush 226 118 28), $x0, $y + $ruleY, 72, 6)

    # Subtitle, and the optional tagline under it, in the app's dim text grey.
    $grey = Brush 150 162 190
    $g.DrawString($Subtitle, $sub, $grey, $x0, $y + $subTop, $Fmt)
    if ($tagline) { $g.DrawString($tagline, $sub, $grey, $x0, $y + $tagTop, $Fmt) }

    Write-Host ("{0}x{1}: helmet x={2:N0}..{3:N0}, text x={4:N0}..{5:N0} (wordmark {6:N0} px at {7} px), ink y={8:N0}..{9:N0}" -f `
        $w, $h, $hx, ($hx + $helmetPx), $x0, ($x0 + $textW), $wordW, $titlePx, ($y + $capTop), ($y + $blockBottom))
    $title.Dispose(); $sub.Dispose(); $fam.Dispose(); $g.Dispose()
    return $bmp
}

$OutDir = [System.IO.Path]::GetFullPath($OutDir)
New-Item -ItemType Directory -Force $OutDir | Out-Null

$banner = Draw-Card 1600 400 64 260 118 40 $null
$banner.Save((Join-Path $OutDir 'banner.png'), [System.Drawing.Imaging.ImageFormat]::Png); $banner.Dispose()

$social = Draw-Card 1280 640 72 280 118 40 'Download one exe. Click one button.'
$social.Save((Join-Path $OutDir 'social-preview.png'), [System.Drawing.Imaging.ImageFormat]::Png); $social.Dispose()

Write-Host "wrote $OutDir\banner.png (1600x400) and $OutDir\social-preview.png (1280x640)"
