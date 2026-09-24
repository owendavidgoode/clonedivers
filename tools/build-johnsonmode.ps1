# Build the same complete mod content with aggressively reduced texture detail.
# Canonical names come from the published manifest, not the user's renumbered install.
param(
    [string]$GameDir = 'C:\Program Files (x86)\Steam\steamapps\common\Helldivers 2',
    [string]$ManifestPath = (Join-Path $PSScriptRoot '..\manifest.json'),
    [string]$WorkDir = (Join-Path $PSScriptRoot '..\dist\johnsonmode'),
    [ValidateSet(256,512,1024,2048)][int]$MaxSize = 512,
    [ValidateSet(64,128,256)][int]$StreamFloor = 128,
    [string]$DotnetRoot = (Join-Path $PSScriptRoot '..\dist\dotnet10-runtime'),
    [string]$Tool = (Join-Path $env:LOCALAPPDATA 'Programs\stingray-tex\stingray-tex.exe')
)
$ErrorActionPreference = 'Stop'
$WorkDir = [IO.Path]::GetFullPath($WorkDir)
if (Test-Path $WorkDir) { throw 'Use a fresh WorkDir: outputs from different source packs/settings must not be mixed.' }
if ($StreamFloor -gt $MaxSize) { throw 'StreamFloor must not exceed MaxSize.' }
$manifest = Get-Content -LiteralPath $ManifestPath -Raw | ConvertFrom-Json
$files = @($manifest.pack.files | Where-Object { $_.option -ne 'skinny' })
if (-not $files.Count) { throw 'No base pack files found.' }
$names = @{}
foreach ($f in $files) {
    if ($f.name -notmatch '^[a-f0-9]{16}\.patch_\d+(\.(gpu_resources|stream))?$' -or $f.sha256 -notmatch '^[a-f0-9]{64}$' -or $f.size -lt 0 -or $names.ContainsKey($f.name)) { throw "Invalid base entry: $($f.name)" }
    $names[$f.name] = $true
}
New-Item -ItemType Directory -Path $WorkDir | Out-Null
$source = Join-Path $WorkDir 'source'
New-Item -ItemType Directory -Path $source | Out-Null
Copy-Item -LiteralPath $ManifestPath -Destination (Join-Path $WorkDir 'source-manifest.json')
$wantedSizes = @{}; foreach ($f in $files) { if ($f.size -gt 0) { $wantedSizes[[long]$f.size] = $true } }
$byHash = @{}
foreach ($folder in @('data','mods_off','mods_old','mods_download')) {
    $dir = Join-Path $GameDir $folder
    if (-not (Test-Path $dir)) { continue }
    if ((Get-Item $dir).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Linked source directory: $dir" }
    foreach ($f in Get-ChildItem -LiteralPath $dir -File) {
        if ($f.Name -notmatch '\.patch_\d+' -or -not $wantedSizes.ContainsKey([long]$f.Length)) { continue }
        if ($f.Attributes -band [IO.FileAttributes]::ReparsePoint) { continue }
        $hash = (Get-FileHash -LiteralPath $f.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        if (-not $byHash.ContainsKey($hash)) { $byHash[$hash] = $f.FullName }
    }
}
$missing = @($files | Where-Object { $_.size -gt 0 -and -not $byHash.ContainsKey($_.sha256) })
if ($missing.Count) { $missing | ConvertTo-Json | Set-Content (Join-Path $WorkDir 'missing-source.json'); throw "$($missing.Count) source files unavailable locally; see missing-source.json." }
foreach ($f in $files) {
    $dest = Join-Path $source $f.name
    if ($f.size -eq 0) { [IO.File]::WriteAllBytes($dest, [byte[]]@()) }
    else { Copy-Item -LiteralPath $byHash[$f.sha256] -Destination $dest }
    if ((Get-FileHash -LiteralPath $dest).Hash -ne $f.sha256) { throw "Source verification failed: $($f.name)" }
}
$metadata = [ordered]@{ name='JohnsonMode(tm)'; sourcePack=$manifest.pack.version; maxSize=$MaxSize; streamFloor=$StreamFloor; restream=$true; dedup=$false; sourceFiles=$files.Count; createdUtc=[DateTime]::UtcNow.ToString('o') }
$metadata | ConvertTo-Json | Set-Content (Join-Path $WorkDir 'build.json')
& (Join-Path $PSScriptRoot 'optimize-pack.ps1') -Source $source -OutDir (Join-Path $WorkDir 'pack') -MaxSize $MaxSize -StreamFloor $StreamFloor -Restream -AllowUnchangedDiagnostics -DotnetRoot $DotnetRoot -Tool $Tool
if ($LASTEXITCODE -ne 0) { throw 'Optimizer failed; output is not ready for installation.' }
$inventory = @(Get-ChildItem (Join-Path $WorkDir 'pack') -File | ForEach-Object { [ordered]@{name=$_.Name;size=$_.Length;sha256=(Get-FileHash -LiteralPath $_.FullName).Hash.ToLowerInvariant()} })
$inventory | ConvertTo-Json | Set-Content (Join-Path $WorkDir 'inventory.json')
Write-Host "Verified JohnsonMode(tm) build: $WorkDir"
