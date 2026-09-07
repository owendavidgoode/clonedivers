# Builds manifest.json (format 2) from the mods deployed in Helldivers 2\data\: one entry per *.patch_* file with its
# final name, size and SHA-256, so friends' Clonedivers can update by renaming what they have and downloading only
# what changed. Release assets are named by their SHA-256 and reused across pack versions.
#
#   powershell -ExecutionPolicy Bypass -File tools\build-manifest.ps1 -Version 2026.09.05-r3 -Notes "Built for Helldivers 2 patch 7.0.2"
#
# Also writes dist\upload-plan.json (the files publish-pack.ps1 still has to upload). Never touches pack.json: that file
# is frozen for the 1.2.0 app. Optional groups come from pack-recipe.json entries that carry an "option" block, joined to
# files through dist\deploy-report.json (written by deploy-mods.ps1).
param(
    [Parameter(Mandatory = $true)][string]$Version,
    [string]$Notes = "",
    [ValidateSet('ok', 'broken')][string]$Status = 'ok',
    [string]$StatusNotes = "",
    [string]$GameDir = "",
    [string]$Repo = "owendavidgoode/clonedivers",
    [string]$ManifestPath = "",
    [string]$VerifyZips = "",                 # glob of format-1 pack zips whose contents must equal data\ exactly (first 1.3 publish)
    [switch]$AllowPendingGameUpdate,
    [int]$MaxAssetsPerRelease = 900,
    [string]$RecipePath = "",
    [string]$DeployReport = "",
    [string]$UploadPlanPath = ""
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
$root = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
# Defaults live here, not in param(): $PSScriptRoot is empty inside parameter defaults under some invocations.
if (-not $ManifestPath) { $ManifestPath = Join-Path $root "manifest.json" }
if (-not $RecipePath) { $RecipePath = Join-Path $root "pack-recipe.json" }
if (-not $DeployReport) { $DeployReport = Join-Path $root "dist\deploy-report.json" }
if (-not $UploadPlanPath) { $UploadPlanPath = Join-Path $root "dist\upload-plan.json" }
$ManifestPath = [System.IO.Path]::GetFullPath($ManifestPath)
$EmptySha = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855"

function Read-Utf8([string]$Path) { return [System.IO.File]::ReadAllText($Path, [System.Text.Encoding]::UTF8) }
function Write-Utf8([string]$Path, [string]$Text) { [System.IO.File]::WriteAllText($Path, $Text, (New-Object System.Text.UTF8Encoding($false))) }

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
if (-not $GameDir) { $GameDir = Find-GameDir; if (-not $GameDir) { throw "Helldivers 2 not found through Steam; pass -GameDir." } }
$data = Join-Path $GameDir "data"
if (-not (Test-Path $data)) { throw "No data\ folder in $GameDir" }

# --- inventory ------------------------------------------------------------------------------------------------
$rx = [regex]'^(?<arch>.+?)\.patch_(?<idx>\d+)(?<comp>\.(?:gpu_resources|stream))?$'
$compOrder = @{ '' = 0; '.gpu_resources' = 1; '.stream' = 2 }
$files = @(Get-ChildItem -LiteralPath $data -File | Where-Object { $rx.IsMatch($_.Name) } | ForEach-Object {
    $m = $rx.Match($_.Name)
    [pscustomobject]@{ Name = $_.Name; Path = $_.FullName; Size = [long]$_.Length; Arch = $m.Groups['arch'].Value; Idx = [int]$m.Groups['idx'].Value; Comp = $m.Groups['comp'].Value }
} | Sort-Object Arch, Idx, { $compOrder[$_.Comp] })
if ($files.Count -eq 0) { throw "No *.patch_* files in $data. Deploy the mods first (tools\deploy-mods.ps1)." }
$dupe = $files | Group-Object { $_.Name.ToLowerInvariant() } | Where-Object { $_.Count -gt 1 } | Select-Object -First 1
if ($dupe) { throw "Two files differ only by case: $($dupe.Group.Name -join ', ')" }
foreach ($g in ($files | Where-Object { $_.Comp -eq '' } | Group-Object Arch)) {
    $idx = @($g.Group.Idx | Sort-Object -Unique)
    for ($i = 0; $i -lt $idx.Count; $i++) { if ($idx[$i] -ne $i) { Write-Warning "archive $($g.Name): patch numbers have a gap at $i (the game stops loading there)"; break } }
}
Write-Host ("Hashing {0} files ({1:N2} GB) in {2}" -f $files.Count, (($files | Measure-Object Size -Sum).Sum / 1GB), $data)
$sw = [Diagnostics.Stopwatch]::StartNew()
foreach ($f in $files) {
    $sha = if ($f.Size -eq 0) { $EmptySha } else { (Get-FileHash -LiteralPath $f.Path -Algorithm SHA256).Hash.ToLowerInvariant() }
    $f | Add-Member -NotePropertyName Sha -NotePropertyValue $sha
}
Write-Host ("  hashed in {0:N0} s" -f $sw.Elapsed.TotalSeconds)

# --- optional verification against format-1 zips (the first per-file publish must equal the frozen r3 pack) -----
if ($VerifyZips) {
    $zips = @(Get-ChildItem -Path $VerifyZips -File | Sort-Object Name)
    if ($zips.Count -eq 0) { throw "No zips match $VerifyZips" }
    $inZips = @{}
    foreach ($z in $zips) {
        $za = [IO.Compression.ZipFile]::OpenRead($z.FullName)
        try {
            foreach ($e in $za.Entries) {
                if (-not $e.Name -or -not $rx.IsMatch($e.Name)) { continue }
                $s = $e.Open(); try { $h = [BitConverter]::ToString([Security.Cryptography.SHA256]::Create().ComputeHash($s)).Replace('-', '').ToLowerInvariant() } finally { $s.Dispose() }
                $inZips[$e.Name.ToLowerInvariant()] = @{ Size = [long]$e.Length; Sha = $h }
            }
        } finally { $za.Dispose() }
    }
    $bad = @()
    foreach ($f in $files) {
        $z = $inZips[$f.Name.ToLowerInvariant()]
        if (-not $z) { $bad += "not in zips: $($f.Name)"; continue }
        if ($z.Size -ne $f.Size -or $z.Sha -ne $f.Sha) { $bad += "differs: $($f.Name)" }
    }
    foreach ($k in $inZips.Keys) { if (-not ($files | Where-Object { $_.Name.ToLowerInvariant() -eq $k })) { $bad += "only in zips: $k" } }
    if ($bad.Count) { throw ("data\ does not match the zips:`n  " + ($bad -join "`n  ")) }
    Write-Host ("  verified: data\ equals the {0} zip(s) file for file ({1} entries)" -f $zips.Count, $inZips.Count) -ForegroundColor Green
}

# --- Steam build ------------------------------------------------------------------------------------------------
$acfPath = [System.IO.Path]::GetFullPath((Join-Path $GameDir "..\..\appmanifest_553850.acf"))
$gameBuild = $null; $gameDepots = @()
if (Test-Path $acfPath) {
    $acf = Read-Utf8 $acfPath
    $b = [regex]::Match($acf, '(?m)^\s*"buildid"\s+"(\d+)"'); $t = [regex]::Match($acf, '(?m)^\s*"TargetBuildID"\s+"(\d+)"'); $sf = [regex]::Match($acf, '(?m)^\s*"StateFlags"\s+"(\d+)"')
    if ($b.Success) { $gameBuild = $b.Groups[1].Value }
    if ($t.Success -and $b.Success -and $t.Groups[1].Value -ne '0' -and $t.Groups[1].Value -ne $b.Groups[1].Value) {
        if (-not $AllowPendingGameUpdate) { throw "Steam has a Helldivers 2 update queued (build $($b.Groups[1].Value) -> $($t.Groups[1].Value)). Let it install and re-test the pack, or pass -AllowPendingGameUpdate." }
        Write-Warning "Steam update pending; recording the currently installed build $gameBuild"
    }
    if ($sf.Success -and $sf.Groups[1].Value -ne '4') { Write-Warning "appmanifest StateFlags is $($sf.Groups[1].Value) (4 = fully installed)" }
    $depBlock = [regex]::Match($acf, '(?s)"InstalledDepots"\s*\{(.*?)\n\t\}')
    if ($depBlock.Success) { $gameDepots = @([regex]::Matches($depBlock.Groups[1].Value, '"manifest"\s+"(\d+)"') | ForEach-Object { $_.Groups[1].Value }) }
    Write-Host "  game build $gameBuild, $($gameDepots.Count) depot manifest(s)"
} else { Write-Warning "appmanifest_553850.acf not found at $acfPath; gameBuild left empty" }

# --- options (recipe "option" blocks joined to files through the deploy report) --------------------------------
$options = New-Object System.Collections.ArrayList
$fileOption = @{}
if ((Test-Path $RecipePath) -and (Test-Path $DeployReport)) {
    $recipe = (Read-Utf8 $RecipePath) | ConvertFrom-Json
    $report = (Read-Utf8 $DeployReport) | ConvertFrom-Json
    $modOption = @{}
    foreach ($mod in $recipe.mods) {
        if ($mod.PSObject.Properties['option'] -and $mod.option) {
            $o = $mod.option
            if (-not ($options | Where-Object { $_.id -eq $o.id })) {
                [void]$options.Add([ordered]@{ id = [string]$o.id; name = [string]$o.name; description = [string]$(if ($o.PSObject.Properties['description']) { $o.description } else { "" }); default = [bool]$(if ($o.PSObject.Properties['default']) { $o.default } else { $true }) })
            }
            $modOption[$mod.name] = [string]$o.id
        }
    }
    foreach ($e in $report) {
        if ($modOption.ContainsKey($e.mod)) { foreach ($part in @($e.parts)) { $n = if ($part -eq '.patch') { $e.file } else { "$($e.file)$part" }; $fileOption[$n.ToLowerInvariant()] = $modOption[$e.mod] } }
    }
    if ($options.Count) { Write-Host "  options: $(($options | ForEach-Object { $_.id }) -join ', ') ($($fileOption.Count) files tagged)" }
} elseif (Test-Path $RecipePath) { Write-Warning "no dist\deploy-report.json: options cannot be assigned to files" }

# --- URL reuse from the committed manifest ---------------------------------------------------------------------
$prevUrl = @{}
$prevApp = $null
try {
    $prevText = (& git -C $root show HEAD:manifest.json 2>$null | Out-String)
    if ($prevText.Trim().StartsWith('{')) {
        $prev = $prevText | ConvertFrom-Json
        if ($prev.pack -and $prev.pack.files) { foreach ($pf in $prev.pack.files) { if ($pf.url -and $pf.size -gt 0) { $prevUrl[$pf.sha256.ToLowerInvariant()] = [string]$pf.url } } }
    }
} catch { Write-Host "  no committed manifest.json yet: every non-empty file goes into the upload plan" }
if (Test-Path $ManifestPath) {
    try { $cur = (Read-Utf8 $ManifestPath) | ConvertFrom-Json; if ($cur.app) { $prevApp = $cur.app } } catch { }
}

# --- assemble -------------------------------------------------------------------------------------------------
$newShas = @($files | Where-Object { $_.Size -gt 0 -and -not $prevUrl.ContainsKey($_.Sha) } | Select-Object -ExpandProperty Sha -Unique)
$tagFor = @{}
for ($i = 0; $i -lt $newShas.Count; $i++) {
    $chunk = [math]::Floor($i / $MaxAssetsPerRelease)
    $tagFor[$newShas[$i]] = if ($chunk -eq 0) { "pack-$Version-files" } else { "pack-$Version-files-$($chunk + 1)" }
}
$entries = New-Object System.Collections.ArrayList
foreach ($f in $files) {
    $url = if ($f.Size -eq 0) { "" } elseif ($prevUrl.ContainsKey($f.Sha)) { $prevUrl[$f.Sha] } else { "https://github.com/$Repo/releases/download/$($tagFor[$f.Sha])/$($f.Sha)" }
    $e = [ordered]@{ name = $f.Name; size = $f.Size; sha256 = $f.Sha; url = $url }
    $opt = $fileOption[$f.Name.ToLowerInvariant()]
    if ($opt) { $e.option = $opt }
    [void]$entries.Add($e)
}
$pack = [ordered]@{
    version = $Version; name = "Clonedivers pack"; notes = $Notes
    gameBuild = $gameBuild; gameDepots = @($gameDepots); status = $Status; statusNotes = $StatusNotes
    options = @($options); files = @($entries)
}
$manifest = [ordered]@{ format = 2 }
if ($prevApp) { $manifest.app = [ordered]@{ version = [string]$prevApp.version; url = [string]$prevApp.url; size = [long]$prevApp.size; sha256 = [string]$prevApp.sha256 } }
$manifest.pack = $pack
$json = $manifest | ConvertTo-Json -Depth 6
Write-Utf8 $ManifestPath $json

# --- self-check (catches PowerShell 5.1 encoding round-trips and array/scalar surprises) --------------------------
$back = (Read-Utf8 $ManifestPath) | ConvertFrom-Json
if (@($back.pack.files).Count -ne $files.Count) { throw "self-check: wrote $(@($back.pack.files).Count) files, expected $($files.Count)" }
for ($i = 0; $i -lt $files.Count; $i++) {
    $w = $back.pack.files[$i]
    if ($w.name -ne $files[$i].Name -or [long]$w.size -ne $files[$i].Size -or $w.sha256 -ne $files[$i].Sha) { throw "self-check: entry $i differs ($($w.name))" }
}
if ($back.pack.notes -ne $Notes -or $back.pack.statusNotes -ne $StatusNotes) { throw "self-check: notes did not round-trip" }
if ($back.pack.options -and @($back.pack.options).Count -ne $options.Count) { throw "self-check: options count" }

# --- upload plan ----------------------------------------------------------------------------------------------
$plan = New-Object System.Collections.ArrayList
$seen = @{}
foreach ($f in $files) {
    if ($f.Size -eq 0 -or $prevUrl.ContainsKey($f.Sha) -or $seen.ContainsKey($f.Sha)) { continue }
    $seen[$f.Sha] = $true
    [void]$plan.Add([ordered]@{ sha = $f.Sha; size = $f.Size; localPath = $f.Path; tag = $tagFor[$f.Sha] })
}
New-Item -ItemType Directory -Force (Split-Path $UploadPlanPath) | Out-Null
Write-Utf8 $UploadPlanPath ("[" + (($plan | ForEach-Object { $_ | ConvertTo-Json -Compress }) -join ",`n") + "]")

$zero = @($files | Where-Object { $_.Size -eq 0 }).Count
$reused = @($files | Where-Object { $_.Size -gt 0 -and $prevUrl.ContainsKey($_.Sha) } | Select-Object -ExpandProperty Sha -Unique).Count
Write-Host ("manifest {0}: {1} files, {2} zero-byte, {3} unique contents to upload ({4:N2} GB), {5} reused from earlier releases -> {6}" -f `
    $Version, $files.Count, $zero, $plan.Count, (($plan | ForEach-Object { [long]$_.size } | Measure-Object -Sum).Sum / 1GB), $reused, $ManifestPath) -ForegroundColor Green
Write-Host "upload plan -> $UploadPlanPath"
