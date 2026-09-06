# Assembles Helldivers 2\data\ from downloaded mod zips according to pack-recipe.json.
# This is the "deploy" step of a mod manager, done reproducibly: pick options, number the patch files, copy.
#
#   powershell -ExecutionPolicy Bypass -File tools\deploy-mods.ps1                 # deploy into the game's data\
#   powershell -ExecutionPolicy Bypass -File tools\deploy-mods.ps1 -DryRun         # just print the plan
#   powershell -ExecutionPolicy Bypass -File tools\deploy-mods.ps1 -OutDir X:\test # deploy somewhere else
#
# How Helldivers 2 mods work: every mod ships folders of files named <archive-hash>.patch_0 (plus optional
# .patch_0.gpu_resources / .patch_0.stream). The game loads <hash>.patch_0, .patch_1, .patch_2 ... in order and stops
# at the first gap, and a later index wins when two mods touch the same asset. So each option folder we install
# becomes the next free index for its archive, in recipe order. Galactic Map must therefore be last in the recipe.
#
# Recipe (pack-recipe.json):
#   { "version": "...", "notes": "...",
#     "sources": [ "folders to search for zips (env vars allowed)" ],
#     "mods": [ { "name": "...", "zip": "wildcard matching one zip file", "enabled": true,
#                 "select": { "<Option Name>": "<SubOption Name>" },   # radio groups; "none" skips the group
#                 "toggles": { "<Option Name>": true|false } } ] }     # options without sub-options; default ON
param(
    [string]$Recipe = (Join-Path $PSScriptRoot "..\pack-recipe.json"),
    [string]$OutDir = "",
    [switch]$DryRun,
    [switch]$SkipMissing,     # deploy what is downloaded; treat missing/still-downloading zips as disabled
    [switch]$KeepExisting,    # default: existing *.patch_* in OutDir are moved to <game>\mods_old first
    [switch]$DeleteExisting   # build machine only: delete existing *.patch_* in OutDir instead of parking them
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
    if (Test-Path $vdf) { foreach ($m in [regex]::Matches((Get-Content $vdf -Raw), '"path"\s+"((?:[^"\\]|\\.)*)"')) { $libs += ($m.Groups[1].Value -replace '\\(.)', '$1') } }
    $libs += $steam
    foreach ($lib in $libs) { $g = Join-Path $lib "steamapps\common\Helldivers 2"; if ((Test-Path (Join-Path $g "data")) -and (Test-Path (Join-Path $g "bin\helldivers2.exe"))) { return $g } }
    return $null
}

$recipePath = [IO.Path]::GetFullPath($Recipe)
$r = Get-Content $recipePath -Raw | ConvertFrom-Json
$sources = @($r.sources | ForEach-Object { [Environment]::ExpandEnvironmentVariables($_) } | Where-Object { Test-Path $_ })
if ($sources.Count -eq 0) { throw "None of the recipe's source folders exist." }

$gameDir = $null
if (-not $OutDir) {
    $gameDir = Find-GameDir
    if (-not $gameDir) { throw "Helldivers 2 not found; pass -OutDir." }
    $OutDir = Join-Path $gameDir "data"
}
$OutDir = [IO.Path]::GetFullPath($OutDir)
Write-Host "Recipe:  $recipePath  (v$($r.version))"
Write-Host "Sources: $($sources -join '; ')"
Write-Host "Target:  $OutDir"
if (Get-Process helldivers2 -ErrorAction SilentlyContinue) { throw "Helldivers 2 is running. Close it first." }

$patchRx = '^(?<hash>[^.]+)\.patch_(?<idx>\d+)(?<ext>\.gpu_resources|\.stream)?$'

# Walk manifest options and collect the Include folders selected by the recipe.
function Resolve-Includes($options, $select, $toggles, [ref]$notes, $depth = 0) {
    $inc = New-Object System.Collections.Generic.List[string]
    foreach ($o in @($options | Where-Object { $_ })) {
        $subs = @($o.SubOptions | Where-Object { $_ })          # @($null) would count as one item
        if ($subs.Count -gt 0) {
            $want = if ($select -and $select.PSObject.Properties[$o.Name]) { $select.($o.Name) } else { $null }
            if (-not $want) { $want = $subs[0].Name; $notes.Value.Add("radio '$($o.Name)': no choice in recipe, took first sub-option '$want'") }
            if ($want -eq 'none') { $notes.Value.Add("radio '$($o.Name)': skipped"); continue }
            $pick = $subs | Where-Object { $_.Name -eq $want } | Select-Object -First 1
            if (-not $pick) { throw "Recipe wants '$want' for option '$($o.Name)' but the manifest offers: $(($subs | ForEach-Object { $_.Name }) -join ' | ')" }
            foreach ($i in @($pick.Include | Where-Object { $_ })) { $inc.Add($i) }
            foreach ($i in @($o.Include | Where-Object { $_ })) { $inc.Add($i) }      # some authors put shared files on the group itself
            $deeper = @($pick.SubOptions | Where-Object { $_ })
            if ($deeper.Count -gt 0) { foreach ($d in @(Resolve-Includes $deeper $select $toggles $notes ($depth + 1))) { if ($d) { $inc.Add([string]$d) } } }
        } else {
            $on = $true
            if ($toggles -and $toggles.PSObject.Properties[$o.Name]) { $on = [bool]$toggles.($o.Name) }
            if (-not $on) { $notes.Value.Add("toggle '$($o.Name)': off"); continue }
            foreach ($i in @($o.Include | Where-Object { $_ })) { $inc.Add($i) }
        }
    }
    return $inc.ToArray()
}

# Plan: list of units; each unit = one folder of patch files from one zip, to be installed as the next index per hash.
$plan = New-Object System.Collections.Generic.List[object]
$counter = @{}   # hash -> next index
$problems = New-Object System.Collections.Generic.List[string]

foreach ($mod in $r.mods) {
    if ($mod.PSObject.Properties['enabled'] -and -not $mod.enabled) { Write-Host ("  skip     {0}" -f $mod.name) -ForegroundColor DarkGray; continue }
    # "zip" may be one wildcard or a list of alternatives in preference order (first pattern that matches a real file wins).
    $zip = $null
    foreach ($pattern in @($mod.zip)) {
        foreach ($s in $sources) { $zip = Get-ChildItem $s -File -Filter *.zip | Where-Object { $_.Name -like $pattern -and $_.Length -gt 1024 } | Sort-Object LastWriteTime -Descending | Select-Object -First 1; if ($zip) { break } }
        if ($zip) { break }
    }
    # Last resort: a zip rebuilt from an earlier deploy by tools\recover-sources.ps1 ("recovered - <mod name>.zip").
    # It holds only the folders that were deployed then, with no manifest, so select/toggles do not apply to it.
    $recovered = $false
    if (-not $zip) {
        $invalidChars = [IO.Path]::GetInvalidFileNameChars()
        $safe = ($mod.name.ToCharArray() | ForEach-Object { if ($invalidChars -contains $_) { '_' } else { $_ } }) -join ''
        foreach ($s in $sources) { $zip = Get-ChildItem $s -File -Filter *.zip | Where-Object { $_.Name -eq "recovered - $safe.zip" -and $_.Length -gt 1024 } | Select-Object -First 1; if ($zip) { $recovered = $true; break } }
    }
    if (-not $zip) { $zip = $null; foreach ($pattern in @($mod.zip)) { foreach ($s in $sources) { $zip = Get-ChildItem $s -File -Filter *.zip | Where-Object { $_.Name -like $pattern } | Select-Object -First 1; if ($zip) { break } }; if ($zip) { break } } }   # an empty (still downloading) file, reported below
    if (-not $zip) { $problems.Add("MISSING zip for '$($mod.name)': $(@($mod.zip) -join ' | ')"); Write-Host ("  MISSING  {0}   ({1})" -f $mod.name, (@($mod.zip) -join ' | ')) -ForegroundColor Red; continue }
    if ($zip.Length -lt 1024) { $problems.Add("EMPTY zip for '$($mod.name)': $($zip.Name) is $($zip.Length) bytes (download not finished?)"); Write-Host ("  EMPTY    {0}" -f $zip.Name) -ForegroundColor Red; continue }

    $a = [IO.Compression.ZipFile]::OpenRead($zip.FullName)
    try {
        $entries = @($a.Entries | Where-Object { $_.Name -and ($_.Name -match $patchRx) })
        if ($entries.Count -eq 0) { $problems.Add("no patch files in $($zip.Name)"); continue }
        $manifestEntry = $a.Entries | Where-Object { $_.Name -ieq 'manifest.json' } | Select-Object -First 1
        $folders = New-Object System.Collections.Generic.List[string]
        $notes = New-Object System.Collections.Generic.List[string]
        $mj = $null
        if ($manifestEntry -and $manifestEntry.FullName -notmatch '/') {
            $sr = New-Object IO.StreamReader($manifestEntry.Open()); $mj = $sr.ReadToEnd() | ConvertFrom-Json; $sr.Dispose()
        }
        # A manifest only steers the install if at least one Option actually names an Include folder.
        $manifestIncludes = 0
        if ($mj) { foreach ($o in @($mj.Options | Where-Object { $_ })) { $manifestIncludes += @($o.Include | Where-Object { $_ }).Count; foreach ($s in @($o.SubOptions | Where-Object { $_ })) { $manifestIncludes += @($s.Include | Where-Object { $_ }).Count } } }
        $hasOptions = $manifestIncludes -gt 0
        if ($hasOptions) {
            # Every Include path the manifest knows about. When one Include is a parent folder of another (e.g. a radio
            # group's 'Shield Backpacks' above its sub-options 'Shield Backpacks/Bubble'), the parent must not swallow
            # the children: files under a more specific Include belong to that Include only.
            $allInc = New-Object System.Collections.Generic.List[string]
            foreach ($o in @($mj.Options | Where-Object { $_ })) { foreach ($x in @($o.Include | Where-Object { $_ })) { $allInc.Add((($x -replace '\\', '/').TrimEnd('/'))) }; foreach ($s in @($o.SubOptions | Where-Object { $_ })) { foreach ($x in @($s.Include | Where-Object { $_ })) { $allInc.Add((($x -replace '\\', '/').TrimEnd('/'))) } } }
            $includes = @(Resolve-Includes $mj.Options $mod.select $mod.toggles ([ref]$notes)) | Where-Object { $_ }
            if (-not $includes) { $problems.Add("'$($mod.name)': recipe selects nothing from this manifest") }
            foreach ($i in ($includes | Select-Object -Unique)) {
                $prefix = ($i -replace '\\', '/').TrimEnd('/')
                $deeper = @($allInc | Where-Object { $_ -ne $prefix -and $_.StartsWith($prefix + '/', 'OrdinalIgnoreCase') })
                $hit = $entries | Where-Object {
                    $f = $_.FullName
                    if (-not ($f -eq $prefix -or $f.StartsWith($prefix + '/', 'OrdinalIgnoreCase'))) { return $false }
                    foreach ($d in $deeper) { if ($f -eq $d -or $f.StartsWith($d + '/', 'OrdinalIgnoreCase')) { return $false } }
                    return $true
                }
                if (-not $hit -and $deeper.Count -gt 0) { continue }   # a pure parent folder: its children are picked by their own options
                if (-not $hit) { $problems.Add("'$($mod.name)': Include '$i' matches no patch files in the zip"); continue }
                foreach ($dir in ($hit | ForEach-Object { $p = $_.FullName; if ($p.Contains('/')) { $p.Substring(0, $p.LastIndexOf('/')) } else { '' } } | Select-Object -Unique)) { if (-not $folders.Contains($dir)) { $folders.Add($dir) } }
            }
        } else {
            foreach ($dir in ($entries | ForEach-Object { $p = $_.FullName; if ($p.Contains('/')) { $p.Substring(0, $p.LastIndexOf('/')) } else { '' } } | Select-Object -Unique)) { $folders.Add($dir) }
            if ($mj) { $notes.Add("manifest has no options: everything installs") }
            elseif ($mod.select -or $mod.toggles) { $notes.Add("no manifest: select/toggles ignored, everything installs") }
        }

        # Collect the mod's patch sets: one per (folder, hash, original index). The author's original index encodes
        # load order inside the mod (a base mesh at patch_0, its textures at patch_53 must come later), so sort by it
        # before handing out new consecutive indices; folder order breaks ties.
        $units = New-Object System.Collections.Generic.List[object]
        $order = 0
        foreach ($dir in $folders) {
            $inDir = $entries | Where-Object { $p = $_.FullName; $d = if ($p.Contains('/')) { $p.Substring(0, $p.LastIndexOf('/')) } else { '' }; $d -eq $dir }
            $groups = $inDir | Group-Object { $m = [regex]::Match($_.Name, $patchRx); "$($m.Groups['hash'].Value)|$($m.Groups['idx'].Value)" }
            foreach ($g in $groups) {
                $hash, $orig = $g.Name -split '\|'
                $files = @{}
                foreach ($e in $g.Group) { $m = [regex]::Match($e.Name, $patchRx); $files[$m.Groups['ext'].Value] = $e.FullName }
                $units.Add([pscustomobject]@{ Folder = $dir; Hash = $hash; Orig = [int]$orig; Order = $order; Files = $files; Bytes = ($g.Group | Measure-Object Length -Sum).Sum })
            }
            $order++
        }
        $unitCount = 0
        foreach ($u in ($units | Sort-Object Hash, Orig, Order)) {
            if (-not $counter.ContainsKey($u.Hash)) { $counter[$u.Hash] = 0 }
            $idx = $counter[$u.Hash]; $counter[$u.Hash]++
            $plan.Add([pscustomobject]@{ Mod = $mod.name; Zip = $zip.FullName; Folder = $u.Folder; Hash = $u.Hash; Orig = $u.Orig; Index = $idx; Files = $u.Files; Bytes = $u.Bytes })
            $unitCount++
        }
        if ($recovered) { $notes.Insert(0, "from recovered zip (original download gone; options frozen as deployed)") }
        Write-Host ("  {0,-44} {1,3} patch set(s)  {2}" -f $mod.name, $unitCount, ($notes -join '; '))
    } finally { $a.Dispose() }
}

Write-Host ""
$total = ($plan | Measure-Object Bytes -Sum).Sum
Write-Host ("Plan: {0} patch sets, {1} files, {2:N2} GB" -f $plan.Count, ($plan | ForEach-Object { $_.Files.Count } | Measure-Object -Sum).Sum, ($total / 1GB))
foreach ($h in $counter.Keys) { Write-Host ("  archive {0}: indices 0..{1}" -f $h, ($counter[$h] - 1)) }
if ($problems.Count -gt 0) { Write-Host "Problems:" -ForegroundColor Yellow; $problems | ForEach-Object { Write-Host "  - $_" -ForegroundColor Yellow } }
if ($DryRun) { Write-Host "Dry run: nothing written."; exit ($(if ($problems.Count) { 2 } else { 0 })) }
$missing = @($problems | Where-Object { $_ -like 'MISSING*' -or $_ -like 'EMPTY*' })
if ($missing.Count -gt 0) {
    if ($SkipMissing) { Write-Host "Continuing without $($missing.Count) missing/empty zip(s) (-SkipMissing)." -ForegroundColor Yellow }
    else { throw 'Fix the missing/empty zips above (or set "enabled": false for those mods, or pass -SkipMissing), then run again.' }
}

# Clear the target of old mod files first (moved by default; deleted only with -DeleteExisting on the build machine).
New-Item -ItemType Directory -Force $OutDir | Out-Null
$existing = Get-ChildItem $OutDir -File | Where-Object { $_.Name -match '\.patch_\d+(\.(gpu_resources|stream))?$' }
if ($existing -and $DeleteExisting) {
    foreach ($f in $existing) { Remove-Item $f.FullName -Force }
    Write-Host "Deleted $($existing.Count) existing mod file(s) from $OutDir (-DeleteExisting)"
} elseif ($existing -and -not $KeepExisting) {
    $old = if ($gameDir) { Join-Path $gameDir "mods_old" } else { Join-Path (Split-Path $OutDir) "mods_old" }
    New-Item -ItemType Directory -Force $old | Out-Null
    foreach ($f in $existing) { Move-Item $f.FullName (Join-Path $old $f.Name) -Force }
    Write-Host "Moved $($existing.Count) existing mod file(s) to $old"
}

# Write. Extract to a temp name then rename, so a crash never leaves a half file that looks like a mod.
$written = 0; $done = 0
foreach ($u in $plan) {
    $a = [IO.Compression.ZipFile]::OpenRead($u.Zip)
    try {
        foreach ($ext in $u.Files.Keys) {
            $e = $a.GetEntry($u.Files[$ext])
            $dest = Join-Path $OutDir ("{0}.patch_{1}{2}" -f $u.Hash, $u.Index, $ext)
            $tmp = "$dest.deploying"
            [IO.Compression.ZipFileExtensions]::ExtractToFile($e, $tmp, $true)
            Move-Item $tmp $dest -Force
            $written++
        }
    } finally { $a.Dispose() }
    $done += [double]$u.Bytes
    $pct = [int][math]::Min(100, [math]::Floor(100.0 * $done / [math]::Max(1.0, [double]$total)))
    Write-Progress -Activity "Deploying" -Status ("{0}  ->  {1}.patch_{2}" -f $u.Mod, $u.Hash, $u.Index) -PercentComplete $pct
}
Write-Progress -Activity "Deploying" -Completed

# Verify: every archive's indices are consecutive from 0.
$out = Get-ChildItem $OutDir -File | Where-Object { $_.Name -match '^(?<hash>[^.]+)\.patch_(?<idx>\d+)$' }
$gaps = 0
foreach ($g in ($out | Group-Object { ($_.Name -split '\.')[0] })) {
    $idx = $g.Group | ForEach-Object { [int]([regex]::Match($_.Name, '\.patch_(\d+)$').Groups[1].Value) } | Sort-Object -Unique
    for ($i = 0; $i -lt $idx.Count; $i++) { if ($idx[$i] -ne $i) { Write-Warning "Gap in $($g.Name) at index $i"; $gaps++; break } }
}
Write-Host ("Deployed {0} files ({1:N2} GB) into {2}. {3}" -f $written, ($total / 1GB), $OutDir, $(if ($gaps) { "$gaps GAP(S) FOUND" } else { "No gaps." })) -ForegroundColor Green

# Report next to the recipe, for the README / debugging.
$report = $plan | ForEach-Object { [ordered]@{ mod = $_.Mod; folder = $_.Folder; file = ("{0}.patch_{1}" -f $_.Hash, $_.Index); parts = @($_.Files.Keys | ForEach-Object { if ($_) { $_ } else { '.patch' } }); bytes = $_.Bytes } }
$reportPath = Join-Path (Split-Path $recipePath) "dist\deploy-report.json"
New-Item -ItemType Directory -Force (Split-Path $reportPath) | Out-Null
[IO.File]::WriteAllText($reportPath, ($report | ConvertTo-Json -Depth 4), (New-Object System.Text.UTF8Encoding $false))
Write-Host "Report: $reportPath"
