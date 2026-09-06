# The Clonedivers helmet, drawn with System.Drawing. Shared by make-icon.ps1 (the .ico and docs\icon.png) and
# make-banner.ps1 (docs\banner.png, docs\social-preview.png), so the three always show the same helmet.
# Dot-source it:   . (Join-Path $PSScriptRoot 'helmet-draw.ps1')
Add-Type -AssemblyName System.Drawing

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

# Draws the helmet into the $size x $size square whose top-left corner is ($x, $y).
# The geometry is authored in a 256-unit space and scaled, so every caller gets the icon's helmet exactly.
# $tile: a System.Drawing.Color to fill the rounded background tile with, or $null for the helmet alone.
function Draw-Helmet([System.Drawing.Graphics]$g, [float]$x, [float]$y, [float]$size, $tile) {
    $s = $g.Save()
    $g.TranslateTransform($x, $y)
    $g.ScaleTransform($size / 256, $size / 256)

    # Background tile
    if ($tile -is [System.Drawing.Color]) {
        $g.FillPath((New-Object System.Drawing.SolidBrush $tile), (RoundedRect 0 0 256 256 52))
    }

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

    $g.Restore($s)
}
