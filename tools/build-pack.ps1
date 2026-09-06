# Builds the Clonedivers pack from the mods currently deployed in Helldivers 2\data\ and writes pack.json.
#
# Workflow (once per pack version):
#   1. Deploy your chosen mods with HD2 Arsenal so the numbered *.patch_* files sit in <game>\data\.
#   2. Run:   powershell -ExecutionPolicy Bypass -File tools\build-pack.ps1
#      -> dist\pack\clonedivers-pack-<version>.zip (+ .sha256) and pack.json at the repo root
#   3. Upload the zip somewhere with a direct download link (Cloudflare R2 free tier works, GitHub Release
#      assets work if you pass -MaxPartGB 1.9 so each part stays under GitHub's 2 GB limit).
#   4. Put the URL(s) in pack.json (or pass -Url now), commit, push. Friends' Clonedivers picks it up.
#
# The zip is stored, not compressed: patch files are already compressed binary, so this runs at disk speed.
param(
    [string]$GameDir = "",
    [string]$Version = (Get-Date -Format "yyyy.MM.dd"),
    [string]$Notes = "",
    [string]$Url = "",                 # single URL, or comma-separated list matching the parts in order
    [double]$MaxPartGB = 0,            # 0 = one zip; e.g. 1.9 to split for GitHub Releases
    [string]$OutDir = (Join-Path $PSScriptRoot "..\dist\pack"),
    [string]$ManifestPath = (Join-Path $PSScriptRoot "..\pack.json")
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem

function Find-GameDir {
    $steam = (Get-ItemProperty HKCU:\Software\Valve\Steam -ErrorAction SilentlyContinue).SteamPath
    if (-not $steam) { $steam = (Get-ItemProperty "HKLM:\SOFTWARE\WOW6432Node\Valve\Steam" -ErrorAction SilentlyContinue).InstallPath }
    if (-not $steam) { return $null }
    $steam = $steam -replace '/', '\'
    $libs = @()
    $vdf = Join-Path $steam "steamapps\libraryfolders.vdf"
    if (Test-Path $vdf) {
        foreach ($m in [regex]::Matches((Get-Content $vdf -Raw), '"path"\s+"((?:[^"\\]|\\.)*)"')) {
            $libs += ($m.Groups[1].Value -replace '\\(.)', '$1')
        }
    }
    $libs += $steam
    foreach ($lib in $libs) {
        $g = Join-Path $lib "steamapps\common\Helldivers 2"
        if ((Test-Path (Join-Path $g "data")) -and (Test-Path (Join-Path $g "bin\helldivers2.exe"))) { return $g }
    }
    return $null
}

if (-not $GameDir) { $GameDir = Find-GameDir }
if (-not $GameDir -or -not (Test-Path (Join-Path $GameDir "data"))) { throw "Helldivers 2 folder not found. Pass -GameDir 'C:\...\Helldivers 2'." }
$data = Join-Path $GameDir "data"

$files = Get-ChildItem $data -File | Where-Object { $_.Name -match '\.patch_\d+(\.(gpu_resources|stream))?$' } | Sort-Object Name
if ($files.Count -eq 0) { throw "No *.patch_* files in $data. Deploy your mods with Arsenal first (and make sure Clonedivers says CLONES: ON)." }

# Sanity: patch indices per archive must be consecutive from 0, or the game stops loading at the gap.
$byArchive = $files | Where-Object { $_.Name -match '^(.+)\.patch_(\d+)$' } | ForEach-Object { [pscustomobject]@{ Archive = $Matches[1]; Index = [int]$Matches[2] } } | Group-Object Archive
foreach ($g in $byArchive) {
    $idx = $g.Group.Index | Sort-Object -Unique
    for ($i = 0; $i -lt $idx.Count; $i++) { if ($idx[$i] -ne $i) { Write-Warning "Archive $($g.Name) has patch indices $($idx -join ',') - gap at $i; the game will stop loading this archive's mods there." ; break } }
}

$total = ($files | Measure-Object Length -Sum).Sum
Write-Host ("Packing {0} files, {1:N1} GB, from {2}" -f $files.Count, ($total / 1GB), $data)

# Split into parts (greedy by size) if asked.
$parts = @()
if ($MaxPartGB -gt 0) {
    $limit = [long]($MaxPartGB * 1GB); $cur = @(); $curSize = 0
    foreach ($f in $files) {
        if ($cur.Count -gt 0 -and ($curSize + $f.Length) -gt $limit) { $parts += ,$cur; $cur = @(); $curSize = 0 }
        $cur += $f; $curSize += $f.Length
    }
    if ($cur.Count -gt 0) { $parts += ,$cur }
} else { $parts = ,@($files) }

New-Item -ItemType Directory -Force $OutDir | Out-Null
$manifestFiles = @()
$urls = @()
if ($Url) { $urls = $Url -split ',' | ForEach-Object { $_.Trim() } }
for ($p = 0; $p -lt $parts.Count; $p++) {
    $suffix = if ($parts.Count -gt 1) { "-part$($p + 1)" } else { "" }
    $zipPath = Join-Path $OutDir "clonedivers-pack-$Version$suffix.zip"
    if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
    $zip = [System.IO.Compression.ZipFile]::Open($zipPath, [System.IO.Compression.ZipArchiveMode]::Create)
    try {
        $n = 0
        foreach ($f in $parts[$p]) {
            [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $f.FullName, $f.Name, [System.IO.Compression.CompressionLevel]::NoCompression) | Out-Null
            $n++
            if ($n % 10 -eq 0) { Write-Host ("  {0}/{1} files" -f $n, $parts[$p].Count) }
        }
    } finally { $zip.Dispose() }
    $item = Get-Item $zipPath
    $sha = (Get-FileHash $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
    Set-Content -Path "$zipPath.sha256" -Value "$sha  $($item.Name)" -Encoding ascii
    Write-Host ("  wrote {0} ({1:N2} GB) sha256 {2}" -f $item.Name, ($item.Length / 1GB), $sha)
    $u = if ($p -lt $urls.Count) { $urls[$p] } else { "" }
    $manifestFiles += [ordered]@{ url = $u; size = $item.Length; sha256 = $sha; file = $item.Name }
}

$manifest = [ordered]@{
    version = $Version
    name    = "Clonedivers pack"
    notes   = $Notes
    files   = $manifestFiles
}
$json = $manifest | ConvertTo-Json -Depth 4
[System.IO.File]::WriteAllText([System.IO.Path]::GetFullPath($ManifestPath), $json + "`n", (New-Object System.Text.UTF8Encoding $false))
Write-Host "wrote $ManifestPath"
if (-not $Url) { Write-Host "Next: upload the zip(s), paste the direct URL(s) into pack.json 'url' fields, commit and push." -ForegroundColor Yellow }
