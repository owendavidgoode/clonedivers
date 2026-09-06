# Rebuilds one source zip per mod from a deployed data\ folder and its deploy report.
# Use when the original Nexus zips are gone (HD2 Arsenal clears its download folder) but the deployed pack is intact.
# The recovered zips contain only the option folders that were actually deployed, laid out as
#   <folder>/<hash>.patch_<deployed index>[.gpu_resources|.stream]
# with no manifest, so deploy-mods.ps1 installs everything in them (recipe select/toggles no longer apply to these).
#
#   powershell -ExecutionPolicy Bypass -File tools\recover-sources.ps1          # -> dist\mods\recovered - <mod>.zip
param(
    [string]$Report = (Join-Path $PSScriptRoot "..\dist\deploy-report.json"),
    [string]$DataDir = "",
    [string]$OutDir = (Join-Path $PSScriptRoot "..\dist\mods")
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem

function Find-GameDir {
    $steam = (Get-ItemProperty HKCU:\Software\Valve\Steam -ErrorAction SilentlyContinue).SteamPath
    if (-not $steam) { $steam = (Get-ItemProperty "HKLM:\SOFTWARE\WOW6432Node\Valve\Steam" -ErrorAction SilentlyContinue).InstallPath }
    if (-not $steam) { return $null }
    $steam = $steam -replace '/', '\'
    $libs = @(); $vdf = Join-Path $steam "steamapps\libraryfolders.vdf"
    if (Test-Path $vdf) { foreach ($m in [regex]::Matches((Get-Content $vdf -Raw), '"path"\s+"((?:[^"\\]|\\.)*)"')) { $libs += ($m.Groups[1].Value -replace '\\(.)', '$1') } }
    $libs += $steam
    foreach ($lib in $libs) { $g = Join-Path $lib "steamapps\common\Helldivers 2"; if ((Test-Path (Join-Path $g "data")) -and (Test-Path (Join-Path $g "bin\helldivers2.exe"))) { return $g } }
    return $null
}
if (-not $DataDir) { $g = Find-GameDir; if (-not $g) { throw "Helldivers 2 not found; pass -DataDir." }; $DataDir = Join-Path $g "data" }

$rep = Get-Content $Report -Raw | ConvertFrom-Json
$OutDir = [IO.Path]::GetFullPath($OutDir)
New-Item -ItemType Directory -Force $OutDir | Out-Null
$invalid = [IO.Path]::GetInvalidFileNameChars()

$byMod = $rep | Group-Object mod
foreach ($g in $byMod) {
    $safe = ($g.Name.ToCharArray() | ForEach-Object { if ($invalid -contains $_) { '_' } else { $_ } }) -join ''
    $zipPath = Join-Path $OutDir "recovered - $safe.zip"
    if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
    $zip = [IO.Compression.ZipFile]::Open($zipPath, [IO.Compression.ZipArchiveMode]::Create)
    $n = 0; $bytes = 0L; $missing = 0
    try {
        foreach ($set in $g.Group) {
            foreach ($part in @($set.parts)) {
                $name = if ($part -eq '.patch') { $set.file } else { "$($set.file)$part" }
                $src = Join-Path $DataDir $name
                if (-not (Test-Path $src)) { $missing++; continue }
                $entry = if ($set.folder) { "$($set.folder)/$name" } else { $name }
                [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $src, $entry, [IO.Compression.CompressionLevel]::NoCompression) | Out-Null
                $n++; $bytes += (Get-Item $src).Length
            }
        }
    } finally { $zip.Dispose() }
    Write-Host ("  {0,-48} {1,3} files {2,8:N0} MB{3}" -f $g.Name, $n, ($bytes / 1MB), $(if ($missing) { "  ($missing MISSING in data\)" } else { "" }))
}
Write-Host "Recovered $($byMod.Count) mod zip(s) into $OutDir"
