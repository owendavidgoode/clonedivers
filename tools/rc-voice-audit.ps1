# Inspect existing voice patches by resource identity before building an armor-conditioned branch.
# Reads only the patch index tables. Does not deploy, renumber or change any game/pack files.
param(
    [string]$SourceDir = (Join-Path $PSScriptRoot '..\dist\rc-upgrade\rc-voice-sources'),
    [string]$Out = (Join-Path $PSScriptRoot '..\dist\rc-upgrade\rc-voice-conflicts.json')
)
$ErrorActionPreference = 'Stop'
$sourceRoot = (Resolve-Path -LiteralPath $SourceDir).Path
$patches = @(Get-ChildItem -LiteralPath $sourceRoot -Recurse -File |
    Where-Object Name -Match '^[0-9a-f]{16}\.patch_\d+$')
if ($patches.Count -eq 0) { throw 'No extracted voice patch indexes found.' }
$types = @{
    '535A7BD3E650D799' = 'wwise_bank'
    '504B55235D21440E' = 'wwise_stream'
    'AF32095C82F2B070' = 'audio companion (legacy type)'
    'AA5965F03029FA18' = 'wwise_dep'
}
$assets = @{}
$sources = foreach ($patch in $patches) {
    $relative = $patch.FullName.Substring($sourceRoot.Length + 1)
    $source = ($relative -split '[\\/]')[0]
    $stream = [IO.File]::OpenRead($patch.FullName)
    $reader = [IO.BinaryReader]::new($stream)
    try {
        if ($reader.ReadUInt32() -ne [uint32]::Parse('F0000011', 'HexNumber')) {
            throw "Unrecognized patch magic: $relative"
        }
        $typeCount = $reader.ReadUInt32()
        $entryCount = $reader.ReadUInt32()
        $tableOffset = 72L + 32L * $typeCount
        if ($entryCount -eq 0 -or $tableOffset + 80L * $entryCount -gt $stream.Length) {
            throw "Invalid patch index length: $relative"
        }
        $stream.Position = $tableOffset
        $typeTotals = @{}
        $banks = @()
        for ($index = 0; $index -lt $entryCount; $index++) {
            $name = '{0:X16}' -f $reader.ReadUInt64()
            $type = '{0:X16}' -f $reader.ReadUInt64()
            $stream.Position += 64
            $key = "$name.$type"
            if (-not $assets.ContainsKey($key)) { $assets[$key] = @() }
            if ($assets[$key] -contains $source) { throw "Duplicate resource in source $source : $key" }
            $assets[$key] += $source
            $typeLabel = if ($types.ContainsKey($type)) { $types[$type] } else { $type }
            $typeTotals[$typeLabel]++
            if ($typeLabel -eq 'wwise_bank') { $banks += $name }
        }
        [pscustomobject]@{
            source = $source; patch = $relative; resources = $entryCount
            by_type = $typeTotals; banks = @($banks | Sort-Object)
            sha256 = (Get-FileHash -LiteralPath $patch.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        }
    } finally { $reader.Dispose() }
}
$overlaps = @($assets.GetEnumerator() | Where-Object { $_.Value.Count -gt 1 } | Sort-Object Key |
    ForEach-Object {
        $parts = $_.Key.Split('.')
        [pscustomobject]@{ name_hash = $parts[0]; type_hash = $parts[1]; sources = $_.Value }
    })
$result = [pscustomobject]@{
    sources = @($sources); overlap_count = $overlaps.Count; overlaps = $overlaps
    note = 'Source resource overlap only; filenames do not establish live deployment order.'
}
$result | ConvertTo-Json -Depth 7 | Set-Content -LiteralPath $Out -Encoding UTF8
$sources | Select-Object source, resources, @{N='banks';E={$_.banks.Count}} | Format-Table -AutoSize
Write-Host "Shared resources: $($overlaps.Count). Saved $Out"
