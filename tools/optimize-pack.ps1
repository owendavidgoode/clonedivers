# Makes the deployed pack lighter on RAM and VRAM without changing what it looks like.
#
# Every mod's textures ship flagged "not streamed", so the game keeps all of them resident the moment they load:
# 4.5 GB of the 8.8 GB r3 pack, which is the RAM spike that killed 16 GB machines at boot. Stingray (the game's
# engine) can instead keep only a small tail of each texture resident and stream the sharp mip levels in on demand,
# inside a fixed 1.5 GB budget it evicts from. This script rewrites every texture-bearing bundle that way with
# Stingray Texture Optimizer (https://github.com/Shiroiame-Kusu/StingrayTextureOptimizer, GPL-3.0): the mip
# chain moves into the bundle's .stream companion byte for byte, nothing is discarded or re-encoded, and every
# non-texture payload (meshes, audio, animation) is copied through unchanged and verified against the original.
#
#   powershell -ExecutionPolicy Bypass -File tools\optimize-pack.ps1                     # <game>\data -> dist\pack-optimized
#   powershell -ExecutionPolicy Bypass -File tools\optimize-pack.ps1 -Source X:\r3 -OutDir X:\r4
#   powershell -ExecutionPolicy Bypass -File tools\optimize-pack.ps1 -MaxSize 4096         # also cap 8K textures (lossy; off by default)
#
# The tool: download a release archive from the GitHub page above (or build it from source with the .NET 10 SDK:
# dotnet build src\Stingray.Cli -c Release) and put stingray-tex.exe in %LOCALAPPDATA%\Programs\stingray-tex\, or
# pass -Tool. A from-source build needs the .NET 10 runtime: -DotnetRoot points at it (default
# %LOCALAPPDATA%\Microsoft\dotnet10 when that folder exists).
#
# Re-runnable: bundles already written to OutDir and passing verification are skipped, so an interrupted run resumes.
param(
    [string]$Source = "",
    [string]$OutDir = (Join-Path $PSScriptRoot "..\dist\pack-optimized"),
    [string]$Tool = "",
    [string]$DotnetRoot = "",
    [int]$StreamFloor = 512,       # largest mip level kept permanently resident; 512 is within 6 MiB of the best case
    [int]$MaxSize = 0,             # 0 = never resize. A cap discards levels above it for good (the only lossy option)
    [switch]$Dedup,                # share byte-identical payloads (shrinks the download, not memory; the engine has not been seen relying on it)
    [switch]$Force                 # rewrite bundles already present in OutDir
)
$ErrorActionPreference = 'Stop'

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

if (-not $Source) { $g = Find-GameDir; if (-not $g) { throw "Helldivers 2 not found; pass -Source." }; $Source = Join-Path $g "data" }
$Source = [IO.Path]::GetFullPath($Source)
$OutDir = [IO.Path]::GetFullPath($OutDir)
if ($Source -eq $OutDir) { throw "OutDir must differ from Source; this script never rewrites in place." }
if (-not $Tool) {
    $Tool = Join-Path $env:LOCALAPPDATA "Programs\stingray-tex\stingray-tex.exe"
    if (-not (Test-Path $Tool)) { $cmd = Get-Command stingray-tex -ErrorAction SilentlyContinue; if ($cmd) { $Tool = $cmd.Source } }
}
if (-not (Test-Path $Tool)) { throw "stingray-tex.exe not found. Download it from https://github.com/Shiroiame-Kusu/StingrayTextureOptimizer/releases (or build from source) and pass -Tool." }
if (-not $DotnetRoot) { $d = Join-Path $env:LOCALAPPDATA "Microsoft\dotnet10"; if (Test-Path $d) { $DotnetRoot = $d } }
if ($DotnetRoot) { $env:DOTNET_ROOT = $DotnetRoot }
if (Get-Process helldivers2 -ErrorAction SilentlyContinue) { throw "Helldivers 2 is running. Close it first." }

New-Item -ItemType Directory -Force $OutDir | Out-Null
# The log sits next to OutDir, never inside it: OutDir must hold nothing but the pack.
$log = Join-Path (Split-Path $OutDir -Parent) ((Split-Path $OutDir -Leaf) + ".optimize-log.txt")
function Log([string]$s) { $s | Tee-Object -FilePath $log -Append | Out-Host }
"" | Set-Content $log
Log ("optimize-pack  {0}" -f (Get-Date -Format s))
Log "Source: $Source"
Log "OutDir: $OutDir"
Log ("Tool:   {0}  ({1})" -f $Tool, ((& $Tool --version) -join ' / '))
$common = @('--stream', "$StreamFloor", '--strategy', 'quality')
if ($MaxSize -gt 0) { $common += @('--max-size', "$MaxSize") }
if (-not $Dedup) { $common += '--no-dedup' }
Log ("Options: {0}" -f ($common -join ' '))

$bundles = Get-ChildItem $Source -File | Where-Object { $_.Name -match '^(?<hash>[0-9a-f]{16})\.patch_(?<idx>\d+)$' } |
    Sort-Object { ($_.Name -split '\.')[0] }, { [int]([regex]::Match($_.Name, '\.patch_(\d+)$').Groups[1].Value) }
if (-not $bundles) { throw "No *.patch_N bundles in $Source" }

function Copy-Through($b, $reason) {
    foreach ($suffix in @('', '.gpu_resources', '.stream')) {
        $src = $b.FullName + $suffix
        if (Test-Path $src) { Copy-Item $src (Join-Path $OutDir ($b.Name + $suffix)) -Force }
    }
    $script:copied++
    Log ("  copied   {0,-28} {1}" -f $b.Name, $reason)
}

$converted = 0; $copied = 0; $skipped = 0; $failed = New-Object System.Collections.Generic.List[string]
$streamAdded = 0L
$n = 0
foreach ($b in $bundles) {
    $n++
    Write-Progress -Activity "Optimising bundles" -Status $b.Name -PercentComplete ([int](100 * $n / $bundles.Count))
    $outBundle = Join-Path $OutDir $b.Name
    if (-not $Force -and (Test-Path $outBundle)) {
        # Resume: trust an output that verifies against its original.
        $v = & $Tool verify $outBundle --original $b.FullName 2>&1
        if ($LASTEXITCODE -eq 0) { $skipped++; Log ("  kept     {0,-28} already written and verified" -f $b.Name); continue }
    }
    if (-not (Test-Path ($b.FullName + '.gpu_resources'))) { Copy-Through $b "no gpu_resources (audio / video / text bundle)"; continue }

    $out = & $Tool optimize $b.FullName --output $OutDir @common 2>&1
    $code = $LASTEXITCODE
    $text = ($out | Out-String)
    if ($code -ne 0) {
        $failed.Add($b.Name)
        Log ("  FAILED   {0,-28} exit {1}" -f $b.Name, $code)
        Log (($text -split "`n" | Select-Object -Last 12) -join "`n")
        continue
    }
    if ($text -match 'Nothing to do') { Copy-Through $b "no texture to stream (meshes / animation only)"; continue }
    if (-not (Test-Path $outBundle)) { Copy-Through $b "tool wrote nothing"; continue }

    # Independent check against the original: header, entry identity, every passthrough payload byte for byte.
    $v = & $Tool verify $outBundle --original $b.FullName 2>&1
    if ($LASTEXITCODE -ne 0) {
        $failed.Add($b.Name)
        Log ("  FAILED   {0,-28} verification against the original" -f $b.Name)
        Log (($v | Out-String))
        continue
    }
    $converted++
    $m = [regex]::Match($text, 'gpu_resources ([\d.]+ \w+) -> ([\d.]+ \w+)')
    $s = [regex]::Match($text, 'streaming: (\d+) texture\(s\) move.*adding ([\d.]+ \w+)')
    $line = "  streamed {0,-28} gpu {1} -> {2}" -f $b.Name, $m.Groups[1].Value, $m.Groups[2].Value
    if ($s.Success) { $line += "   {0} textures, +{1} in .stream" -f $s.Groups[1].Value, $s.Groups[2].Value }
    Log $line
}
Write-Progress -Activity "Optimising bundles" -Completed

# Every bundle must be present in the output under its own name, with the same numbering, or the game stops at the gap.
$missing = @($bundles | Where-Object { -not (Test-Path (Join-Path $OutDir $_.Name)) } | ForEach-Object { $_.Name })
foreach ($g in ($bundles | Group-Object { ($_.Name -split '\.')[0] })) {
    $idx = Get-ChildItem $OutDir -File | Where-Object { $_.Name -match ('^' + [regex]::Escape($g.Name) + '\.patch_(\d+)$') } |
        ForEach-Object { [int]([regex]::Match($_.Name, '\.patch_(\d+)$').Groups[1].Value) } | Sort-Object -Unique
    for ($i = 0; $i -lt $idx.Count; $i++) { if ($idx[$i] -ne $i) { Log "GAP in $($g.Name) at index $i"; break } }
}

function Total($dir, $filter) { $f = Get-ChildItem $dir -File | Where-Object { $_.Name -match $filter }; if ($f) { ($f | Measure-Object Length -Sum).Sum } else { 0 } }
$before = @{ patch = Total $Source '\.patch_\d+$'; gpu = Total $Source '\.gpu_resources$'; stream = Total $Source '\.stream$' }
$after  = @{ patch = Total $OutDir '\.patch_\d+$'; gpu = Total $OutDir '\.gpu_resources$'; stream = Total $OutDir '\.stream$' }
Log ""
Log ("Bundles: {0} streamed, {1} copied through, {2} kept from an earlier run, {3} FAILED{4}" -f $converted, $copied, $skipped, $failed.Count, $(if ($failed.Count) { ': ' + ($failed -join ', ') } else { '' }))
if ($missing.Count) { Log ("MISSING in output: " + ($missing -join ', ')) }
Log ("resident GPU data (.gpu_resources): {0:N2} GB -> {1:N2} GB" -f ($before.gpu / 1GB), ($after.gpu / 1GB))
Log ("streamed on demand (.stream):       {0:N2} GB -> {1:N2} GB" -f ($before.stream / 1GB), ($after.stream / 1GB))
Log ("pack on disk:                       {0:N2} GB -> {1:N2} GB" -f (($before.patch + $before.gpu + $before.stream) / 1GB), (($after.patch + $after.gpu + $after.stream) / 1GB))
Log "Log: $log"
if ($failed.Count -or $missing.Count) { exit 2 }
