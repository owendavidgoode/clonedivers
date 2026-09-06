# Asset-level conflict report for the deployed pack.
# Every Helldivers 2 mod patches the same archive, so "conflict" means two patch sets contain the same asset ID;
# the game loads patch_0, patch_1, ... in order and the highest index wins. This reads the index table at the top
# of every <hash>.patch_N in data\ (no game process involved: plain file reads of the first few KB) and reports
# each asset that appears in more than one patch set, with the mods involved and which one wins.
#
#   powershell -ExecutionPolicy Bypass -File tools\conflict-report.ps1            # uses dist\deploy-report.json for names
param(
    [string]$DataDir = "",
    [string]$Report = (Join-Path $PSScriptRoot "..\dist\deploy-report.json"),
    [string]$Out = (Join-Path $PSScriptRoot "..\dist\conflict-report.md")
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
if (-not $DataDir) { $g = Find-GameDir; if (-not $g) { throw "Helldivers 2 not found; pass -DataDir." }; $DataDir = Join-Path $g "data" }

# Known Stingray asset type hashes (names are cosmetic; unknown types are shown as hex).
$typeNames = @{
    '0D972BAB10B40FD3' = 'strings'; 'E0A48D0BE9A7453F' = 'unit'; 'CD4238C6A0C69E32' = 'texture'; 'EAC0B497876ADEDF' = 'material'
    'A8193123526FAD64' = 'particles'; '504B55235D21440E' = 'wwise_stream'; '535A7BD3E650D799' = 'wwise_bank'; 'AA5965F03029FA18' = 'wwise_dep'
    'A14E8DFA2CD117E2' = 'lua'; '18DEAD01056B72E9' = 'bones'; '931E336D7646CC26' = 'animation'; '2A690FD348FE9AC5' = 'level'
    'AD9C6D9ED1E5E77A' = 'package'; '27862FE24795319C' = 'render_config'; 'F7505933166D6755' = 'state_machine'; '9199BB50B6896F02' = 'bik_video'
    'D37E11A77BAFD3D2' = 'physics'; '92D3EE038EEB610D' = 'font'; 'E985C5F61C169997' = 'speedtree'; 'C4F0F4BE7FB0C8D6' = 'entity'
    'FE73C7DCFF8A7CA5' = 'vector_field'; '9EFE0A916AAE7880' = 'shader_library'; 'E5EE32A477239A93' = 'shader_library_group'; 'B7893ADF7567506A' = 'texture_atlas'
    '1D59BD6687DB6B33' = 'animation_curves'; 'A486D4045106165C' = 'geometry_group'; 'AB2F78E885F513C6' = 'level_settings'
}

# Map deployed file -> mod/folder from the deploy report, when available.
$names = @{}
if (Test-Path $Report) { foreach ($r in (Get-Content $Report -Raw | ConvertFrom-Json)) { $names[$r.file] = "$($r.mod)  [$($r.folder)]" } }

$patches = Get-ChildItem $DataDir -File | Where-Object { $_.Name -match '^(?<hash>[0-9a-f]{16})\.patch_(?<idx>\d+)$' } |
    Sort-Object { ($_.Name -split '\.')[0] }, { [int]([regex]::Match($_.Name, '\.patch_(\d+)$').Groups[1].Value) }
if (-not $patches) { throw "No patch files in $DataDir" }

$assets = @{}      # "hash|assetId" -> list of @{ Index; File; Mod; Type }
$parsed = 0; $badParse = New-Object System.Collections.Generic.List[string]
foreach ($p in $patches) {
    $fs = [IO.File]::OpenRead($p.FullName)
    try {
        $br = New-Object IO.BinaryReader($fs)
        $magic = $br.ReadUInt32()
        $expected = [uint32]::Parse('F0000011', [System.Globalization.NumberStyles]::HexNumber)   # PowerShell would read the literal 0xF0000011 as a negative Int32
        if ($magic -ne $expected) { $badParse.Add("$($p.Name): magic 0x{0:X8}" -f $magic); continue }
        $numTypes = $br.ReadUInt32(); $numFiles = $br.ReadUInt32()
        if ($numFiles -eq 0 -or $numFiles -gt 200000) { $badParse.Add("$($p.Name): implausible file count $numFiles"); continue }
        $fs.Position = 72 + 32 * $numTypes                      # 72-byte header, 32-byte type entries
        $need = $fs.Position + 80 * $numFiles
        if ($need -gt $fs.Length) { $badParse.Add("$($p.Name): index table longer than file"); continue }
        $hash = ($p.Name -split '\.')[0]
        $idx = [int]([regex]::Match($p.Name, '\.patch_(\d+)$').Groups[1].Value)
        $label = if ($names.ContainsKey($p.Name)) { $names[$p.Name] } else { "(unknown mod)" }
        for ($i = 0; $i -lt $numFiles; $i++) {
            $id = $br.ReadUInt64(); $type = $br.ReadUInt64()
            $fs.Position += 80 - 16                                # skip offsets/sizes
            # A Stingray resource is identified by (name hash, type hash): the same name commonly exists as a unit,
            # a material and a bones asset at once, so the key must include the type.
            $t = '{0:X16}' -f $type
            $key = "$hash|{0:X16}|$t" -f $id
            if (-not $assets.ContainsKey($key)) { $assets[$key] = New-Object System.Collections.Generic.List[object] }
            $assets[$key].Add([pscustomobject]@{ Index = $idx; File = $p.Name; Mod = $label; Type = $(if ($typeNames.ContainsKey($t)) { $typeNames[$t] } else { $t }) })
        }
        $parsed++
    } finally { $fs.Dispose() }
}

$conflicts = $assets.GetEnumerator() | Where-Object { $_.Value.Count -gt 1 } | ForEach-Object {
    $list = $_.Value | Sort-Object Index
    [pscustomobject]@{ Asset = ($_.Key -split '\|')[1]; Type = $list[0].Type; Count = $list.Count; Winner = $list[-1]; Losers = @($list | Select-Object -First ($list.Count - 1)) }
}
# Cross-mod pairs only (a mod overriding its own earlier folder is usually deliberate layering, listed separately).
$pairs = @{}
$selfOverrides = @{}
foreach ($c in $conflicts) {
    foreach ($l in $c.Losers) {
        $wm = ($c.Winner.Mod -split '  \[')[0]; $lm = ($l.Mod -split '  \[')[0]
        if ($wm -eq $lm) { $selfOverrides[$wm] = 1 + $(if ($selfOverrides.ContainsKey($wm)) { $selfOverrides[$wm] } else { 0 }); continue }
        $k = "$lm  ==>  $wm"
        if (-not $pairs.ContainsKey($k)) { $pairs[$k] = @{ Count = 0; Types = @{} } }
        $pairs[$k].Count++
        $pairs[$k].Types[$c.Type] = 1 + $(if ($pairs[$k].Types.ContainsKey($c.Type)) { $pairs[$k].Types[$c.Type] } else { 0 })
    }
}

$sb = New-Object System.Text.StringBuilder
[void]$sb.AppendLine("# Asset conflict report")
[void]$sb.AppendLine("")
[void]$sb.AppendLine("Data folder: ``$DataDir``  ·  patch files parsed: $parsed of $($patches.Count)  ·  distinct assets: $($assets.Count)  ·  assets touched by more than one patch set: $(@($conflicts).Count)")
if ($badParse.Count) { [void]$sb.AppendLine(""); [void]$sb.AppendLine("Not parsed: " + ($badParse -join '; ')) }
[void]$sb.AppendLine("")
[void]$sb.AppendLine("## Cross-mod overrides (loser ==> winner, winner loads later)")
[void]$sb.AppendLine("")
[void]$sb.AppendLine("| Loser ==> Winner | Assets | By type |")
[void]$sb.AppendLine("|---|---|---|")
foreach ($k in ($pairs.Keys | Sort-Object { -$pairs[$_].Count })) {
    $types = ($pairs[$k].Types.GetEnumerator() | Sort-Object { -$_.Value } | ForEach-Object { "$($_.Key) $($_.Value)" }) -join ', '
    [void]$sb.AppendLine("| $k | $($pairs[$k].Count) | $types |")
}
[void]$sb.AppendLine("")
[void]$sb.AppendLine("## Within-mod layering (a mod's later folder overriding its earlier one; normally intended)")
[void]$sb.AppendLine("")
foreach ($k in ($selfOverrides.Keys | Sort-Object)) { [void]$sb.AppendLine("- $k : $($selfOverrides[$k]) asset(s)") }
[void]$sb.AppendLine("")
[void]$sb.AppendLine("## Every cross-mod conflict")
[void]$sb.AppendLine("")
[void]$sb.AppendLine("| Asset | Type | Winner (index) | Overridden (index) |")
[void]$sb.AppendLine("|---|---|---|---|")
foreach ($c in ($conflicts | Sort-Object Type, Asset)) {
    $cross = @($c.Losers | Where-Object { ($_.Mod -split '  \[')[0] -ne ($c.Winner.Mod -split '  \[')[0] })
    if (-not $cross) { continue }
    [void]$sb.AppendLine("| $($c.Asset) | $($c.Type) | $($c.Winner.Mod) ($($c.Winner.Index)) | " + (($cross | ForEach-Object { "$($_.Mod) ($($_.Index))" }) -join '; ') + " |")
}
New-Item -ItemType Directory -Force (Split-Path $Out) | Out-Null
[IO.File]::WriteAllText([IO.Path]::GetFullPath($Out), $sb.ToString(), (New-Object System.Text.UTF8Encoding $false))

Write-Host ("Parsed {0}/{1} patch files, {2} distinct assets, {3} assets in more than one patch set." -f $parsed, $patches.Count, $assets.Count, @($conflicts).Count)
if ($badParse.Count) { Write-Warning ("Not parsed: " + ($badParse -join '; ')) }
Write-Host "Cross-mod overrides:"
foreach ($k in ($pairs.Keys | Sort-Object { -$pairs[$_].Count })) { Write-Host ("  {0,4}  {1}   ({2})" -f $pairs[$k].Count, $k, (($pairs[$k].Types.GetEnumerator() | Sort-Object { -$_.Value } | ForEach-Object { "$($_.Key) $($_.Value)" }) -join ', ')) }
Write-Host "Report: $Out"
