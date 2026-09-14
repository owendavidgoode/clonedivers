# Stage a private geometry control. Never installs it or changes a public manifest.
param(
    [string]$EvidenceDirectory=(Join-Path $PSScriptRoot '../dist/investigation-2026-09-13/geometry'),
    [string]$OutDirectory=(Join-Path $PSScriptRoot '../dist/investigation-2026-09-13/geometry/dropship-control')
)
$ErrorActionPreference='Stop'
$OutDirectory=[IO.Path]::GetFullPath($OutDirectory)
if (Test-Path -LiteralPath $OutDirectory) {throw 'Use a new staging directory; do not overwrite an existing control'}
$EvidenceDirectory=[IO.Path]::GetFullPath($EvidenceDirectory)
$comparison=Get-Content (Join-Path $EvidenceDirectory 'dropship-vanilla-comparison.json') -Raw | ConvertFrom-Json
if ($comparison.resource -ne 'db90077e76faa025' -or $comparison.geometryGroup -ne '417da1b8e06cb5b9') {throw 'Expected verified dropship comparison'}
$vanillaPath=Join-Path $EvidenceDirectory 'vanilla/content/fac_cyborgs/vehicles/cyborg_dropship/cyborg_dropship.unit.main'
$expected=@($comparison.sources | Where-Object name -eq 'cyborg_dropship.unit.main')
if ($expected.Count -ne 1 -or (Get-FileHash -LiteralPath $vanillaPath).Hash -ne $expected[0].sha256) {throw 'Vanilla unit differs from comparison input'}
$inventory=Get-Content (Join-Path $EvidenceDirectory 'representative-meshes.json') -Raw | ConvertFrom-Json
$donor=@($inventory | Where-Object id -eq 'db90077e76faa025')
if ($donor.Count -ne 1) {throw 'Expected one modded dropship template'}
$archive=Join-Path $PSScriptRoot ('../dist/pack-optimized-current/'+$donor[0].patch)
$wanted=@{'db90077e76faa025:e0a48d0be9a7453f'=$true}
$reader=[IO.BinaryReader]::new([IO.File]::OpenRead($archive))
try {
    $header=$reader.ReadBytes(72)
    if ([BitConverter]::ToUInt32($header,0) -ne 4026531857) {throw 'Unexpected archive magic'}
    $nt=[BitConverter]::ToUInt32($header,4);$nr=[BitConverter]::ToUInt32($header,8)
    if (72+32L*$nt+80L*$nr -gt $reader.BaseStream.Length) {throw 'Invalid archive table extent'}
    $typeRows=@{}
    for ($i=0;$i -lt $nt;$i++) {$raw=$reader.ReadBytes(32);$typeRows[[BitConverter]::ToUInt64($raw,8).ToString('x16')]=$raw}
    $selected=@(for ($i=0;$i -lt $nr;$i++) {
        $reader.BaseStream.Position=72+32L*$nt+80L*$i;$row=$reader.ReadBytes(80)
        $id=[BitConverter]::ToUInt64($row,0).ToString('x16');$type=[BitConverter]::ToUInt64($row,8).ToString('x16');$key=$id+':'+$type
        if (-not $wanted.ContainsKey($key)) {continue}
        $payloads=@(for ($part=0;$part -lt 3;$part++) {
            $bytes=if ($part -eq 0) {[IO.File]::ReadAllBytes($vanillaPath)} else {[byte[]]::new(0)}
            if ($null -eq $bytes) {$bytes=[byte[]]::new(0)}
            [BitConverter]::GetBytes([uint32]$bytes.Length).CopyTo($row,56+4*$part)
            # Wrapper avoids PowerShell flattening byte arrays.
            [pscustomobject]@{bytes=$bytes}
        })
        [pscustomobject]@{key=$key;type=$type;row=$row;payloads=$payloads}
    })
} finally {$reader.Dispose()}
if ($selected.Count -ne 1) {throw 'Expected the exact dropship unit control resource'}
$selected=@($selected | Sort-Object type,key)
$types=@($selected.type | Select-Object -Unique)
[BitConverter]::GetBytes([uint32]$types.Count).CopyTo($header,4)
[BitConverter]::GetBytes([uint32]$selected.Count).CopyTo($header,8)
$streams=@([IO.MemoryStream]::new(),[IO.MemoryStream]::new(),[IO.MemoryStream]::new())
$table=[IO.MemoryStream]::new()
try {
    $writer=[IO.BinaryWriter]::new($table);$writer.Write($header)
    foreach ($type in $types) {$row=[byte[]]$typeRows[$type].Clone();[BitConverter]::GetBytes([uint64]1).CopyTo($row,16);$writer.Write($row)}
    $mainStart=72+32*$types.Count+80*$selected.Count+8
    for ($i=0;$i -lt $selected.Count;$i++) {
        $resource=$selected[$i];$row=[byte[]]$resource.row.Clone()
        for ($part=0;$part -lt 3;$part++) {
            $offset=$streams[$part].Length;if ($part -eq 0) {$offset+=$mainStart}
            [BitConverter]::GetBytes([uint64]$offset).CopyTo($row,16+8*$part)
            $bytes=$resource.payloads[$part].bytes;$streams[$part].Write($bytes,0,$bytes.Length)
            $padding=[byte[]]::new((16-$bytes.Length%16)%16);$streams[$part].Write($padding,0,$padding.Length)
        }
        [Array]::Clear($row,40,16)
        [BitConverter]::GetBytes([uint32]$i).CopyTo($row,76)
        $writer.Write($row)
    }
    $writer.Write([byte[]]::new(8));$writer.Write($streams[0].ToArray())
    if ($table.Length -lt 256*$selected.Count) {$writer.Write([byte[]]::new(256*$selected.Count-$table.Length))}
    $outputs=@([pscustomobject]@{suffix='';bytes=$table.ToArray()},[pscustomobject]@{suffix='.stream';bytes=$streams[1].ToArray()},[pscustomobject]@{suffix='.gpu_resources';bytes=$streams[2].ToArray()})
    # Independently read generated TOC and compare every raw payload to its source.
    $resourceHashes=@(for ($i=0;$i -lt $selected.Count;$i++) {
        $rowAt=72+32*$types.Count+80*$i
        $hashes=@(for ($part=0;$part -lt 3;$part++) {
            $offset=[BitConverter]::ToUInt64($outputs[0].bytes,$rowAt+16+8*$part)
            $length=[BitConverter]::ToUInt32($outputs[0].bytes,$rowAt+56+4*$part)
            $sha=[Security.Cryptography.SHA256]::Create()
            try {
                $actual=[Convert]::ToBase64String($sha.ComputeHash($outputs[$part].bytes,[int]$offset,[int]$length))
                $expected=[Convert]::ToBase64String($sha.ComputeHash($selected[$i].payloads[$part].bytes))
                if ($actual -ne $expected) {throw 'Staged payload differs from vanilla source'}
                $actual
            } finally {$sha.Dispose()}
        })
        @{key=$selected[$i].key;payloadSha256Base64=$hashes}
    })
    New-Item -ItemType Directory -Path $OutDirectory | Out-Null
    $files=@(foreach ($output in $outputs) {
        $path=Join-Path $OutDirectory ('9ba626afa44a3aa3.patch_0'+$output.suffix)
        [IO.File]::WriteAllBytes($path,$output.bytes)
        @{name=[IO.Path]::GetFileName($path);bytes=$output.bytes.Length;sha256=(Get-FileHash -LiteralPath $path).Hash.ToLowerInvariant()}
    })
    $receipt=[ordered]@{
        purpose='Private dropship geometry control; not a release or optimized HMP asset'
        sourceVirtualArchive='fdf011daecf24312';templateSha256=(Get-FileHash -LiteralPath $archive).Hash.ToLowerInvariant();resources=$resourceHashes;files=$files
        validation='Original extracted vanilla unit payload preserved byte-for-byte after repacking. Uses base-game external geometry group 417da1b8e06cb5b9, independently extracted and parsed in the comparison.'
        installed=$false;runtimeTested=$false
        requirements='Requires base-game dependencies. One original material df762856873e639c is overridden elsewhere in CIS; this control leaves shared material overrides intact. Appearance must be checked before interpreting captures.'
        installation='Do not import with normal launcher or copy as patch_0. Test harness must use a free final patch index and remove only exact control hashes to return to HMP.'
        limitation='This comparison changes dropship unit geometry/material references together, not a pure polygon-count experiment. Controlled gameplay can measure the net replacement cost; it cannot attribute all of it to geometry.'
    }
    $receipt | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $OutDirectory 'build-report.json') -Encoding UTF8
    $files | Format-Table -AutoSize
} finally {foreach ($stream in $streams) {$stream.Dispose()};$table.Dispose()}
