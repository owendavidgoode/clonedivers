# Read-only metadata comparison following Filediver's unit/geometry_group loaders.
param([string]$EvidenceDirectory=(Join-Path $PSScriptRoot '../dist/investigation-2026-09-13/geometry'))
$ErrorActionPreference='Stop'
$EvidenceDirectory=[IO.Path]::GetFullPath($EvidenceDirectory)
$unitPath=Join-Path $EvidenceDirectory 'vanilla/content/fac_cyborgs/vehicles/cyborg_dropship/cyborg_dropship.unit.main'
$groupPath=Join-Path $EvidenceDirectory 'vanilla-geometry/0x417da1b8e06cb5b9.geometry_group.main'
$gpuPath=Join-Path $EvidenceDirectory 'vanilla-geometry/0x417da1b8e06cb5b9.geometry_group.gpu'
if (-not ('DropshipNameHash' -as [type])) {
    Add-Type -TypeDefinition @'
public static class DropshipNameHash {
    public static ulong Sum(string value) {
        unchecked {
            byte[] b=System.Text.Encoding.UTF8.GetBytes(value);
            const ulong m=0xc6a4a7935bd1e995UL;
            ulong h=(ulong)b.Length*m; int p=0;
            for (;p+8<=b.Length;p+=8) { ulong k=System.BitConverter.ToUInt64(b,p);k*=m;k^=k>>47;k*=m;h^=k;h*=m; }
            if(p<b.Length) { for(int j=0;p+j<b.Length;j++) h^=(ulong)b[p+j]<<(8*j);h*=m; }
            h^=h>>47;h*=m;h^=h>>47;return h;
        }
    }
}
'@
}
if ([DropshipNameHash]::Sum('content/fac_cyborgs/vehicles/cyborg_dropship/cyborg_dropship').ToString('x16') -ne 'db90077e76faa025') {throw 'Hash implementation failed known resource identity'}
$thinNames=@{}
$thinFile=Join-Path $PSScriptRoot '../dist/rc-upgrade/filediver-source/hashes/thinhashes.txt'
foreach ($name in [IO.File]::ReadLines([IO.Path]::GetFullPath($thinFile))) {
    if ($name -and -not $name.StartsWith('//')) { $thin=([DropshipNameHash]::Sum($name) -shr 32).ToString('x8');$thinNames[$thin]=$name }
}
function Bounds([byte[]]$data,[long]$offset,[long]$length) {
    if ($offset -lt 0 -or $length -lt 0 -or $offset+$length -gt $data.LongLength) { throw "Metadata outside resource: $offset + $length" }
}
function U32([byte[]]$data,[long]$offset) { Bounds $data $offset 4;[BitConverter]::ToUInt32($data,[int]$offset) }
function U64Hex([byte[]]$data,[long]$offset) { Bounds $data $offset 8;'{0:x16}' -f [BitConverter]::ToUInt64($data,[int]$offset) }
function F32([byte[]]$data,[long]$offset) { Bounds $data $offset 4;[BitConverter]::ToSingle($data,[int]$offset) }
function Count([byte[]]$data,[long]$offset) { $n=U32 $data $offset;if ($n -gt 10000) {throw 'Implausible metadata count'};$n }
function Pointers([byte[]]$data,[long]$offset) {
    if ($offset -eq 0) {return};$n=Count $data $offset
    Bounds $data ($offset+4) (4*$n)
    for ($i=0;$i -lt $n;$i++) { [long]$offset+(U32 $data ($offset+4+4*$i)) }
}
$unit=[IO.File]::ReadAllBytes($unitPath);$group=[IO.File]::ReadAllBytes($groupPath)
$gpuLength=(Get-Item -LiteralPath $gpuPath).Length
$models=Count $group 8;$layoutsOffset=U32 $group 12
$layouts=@(foreach ($offset in (Pointers $group $layoutsOffset)) {
    Bounds $group $offset 448
    $vertices=U32 $group ($offset+352);$indices=U32 $group ($offset+392)
    $vo=U32 $group ($offset+416);$vb=U32 $group ($offset+420);$io=U32 $group ($offset+424);$ib=U32 $group ($offset+428)
    if ([long]$vo+$vb -gt $gpuLength -or [long]$io+$ib -gt $gpuLength) {throw 'Layout exceeds GPU data'}
    [pscustomobject]@{vertices=$vertices;indices=$indices;vertexBytes=$vb;indexBytes=$ib}
})
$target='db90077e76faa025';$modelIndex=-1
for ($i=0;$i -lt $models;$i++) {if ((U64Hex $group (16+16*$i+8)) -eq $target) {$modelIndex=$i;break}}
if ($modelIndex -lt 0) {throw 'Dropship unit absent from external geometry group'}
$infoOffset=U32 $group (16+16*$models+4*$modelIndex);$meshCount=Count $group $infoOffset
$meshes=@(for ($i=0;$i -lt $meshCount;$i++) {
    $offset=[long]$infoOffset+(U32 $group ($infoOffset+4+4*$meshCount+4*$i))
    Bounds $group $offset 48
    $layout=U32 $group $offset;$n=Count $group ($offset+40);$go=U32 $group ($offset+44)
    if ($layout -ge $layouts.Count) {throw 'Invalid external layout index'}
    $groups=@(for ($j=0;$j -lt $n;$j++) {
        $p=$offset+$go+24*$j;Bounds $group $p 24
        $indexStart=U32 $group ($p+12);$indices=U32 $group ($p+16)
        $vertexStart=U32 $group ($p+4);$vertices=U32 $group ($p+8)
        if ([long]$indexStart+$indices -gt $layouts[$layout].indices -or [long]$vertexStart+$vertices -gt $layouts[$layout].vertices) {throw 'Mesh group exceeds layout'}
        [pscustomobject]@{material=(U32 $group $p);vertices=$vertices;indices=$indices}
    })
    $boneHash='{0:x8}' -f (U32 $group ($infoOffset+4+4*$i))
    [pscustomobject]@{layout=$layout;groups=$groups;boneHash=$boneHash;boneName=$thinNames[$boneHash]}
})
# External mesh records are in a DIFFERENT order from the unit's mesh table.
# Resolve each unit mesh by its GroupBoneHash; LOD entries index the unit table.
$unitMeshOffsets=@(Pointers $unit (U32 $unit 100))
$orderedMeshes=@(foreach ($offset in $unitMeshOffsets) {
    $bone='{0:x8}' -f (U32 $unit ($offset+40))
    $matches=@($meshes | Where-Object boneHash -eq $bone)
    if ($matches.Count -ne 1) {throw "Unit mesh bone must resolve uniquely in geometry group: $bone"}
    $matches[0]
})
$vanillaLods=@(foreach ($offset in (Pointers $unit (U32 $unit 48))) {
    $n=Count $unit ($offset+16)
    $levels=@(for ($i=0;$i -lt $n;$i++) {
        $p=[long]$offset+(U32 $unit ($offset+20+4*$i));$refs=Count $unit ($p+8)
        $ids=@(for ($j=0;$j -lt $refs;$j++) {U32 $unit ($p+12+4*$j)})
        [pscustomobject]@{level=$i;detailMax=(F32 $unit $p);detailMin=(F32 $unit ($p+4));indices=$ids}
    })
    [pscustomobject]@{levels=$levels}
})
function Summarize($lods,$meshTable) {
    $groupIndex=0
    foreach ($lod in $lods) {
        foreach ($level in $lod.levels) {
            $indexSum=[long]0;$groups=0
            foreach ($id in $level.indices) {
                if ($id -ge $meshTable.Count) {throw 'LOD references absent mesh'}
                foreach ($part in $meshTable[$id].groups) {$indexSum+=$part.indices;$groups++}
            }
            if ($indexSum%3) {throw 'Index count not divisible by three; triangle-list equivalent inappropriate'}
            [pscustomobject]@{group=$groupIndex;level=$level.level;meshIds=@($level.indices);boneNames=@($level.indices | ForEach-Object {$meshTable[$_].boneName});detailMin=$level.detailMin;detailMax=$level.detailMax;materialGroups=$groups;triangleListEquivalent=$indexSum/3}
        }
        $groupIndex++
    }
}
$inventory=Get-Content (Join-Path $EvidenceDirectory 'representative-meshes.json') -Raw | ConvertFrom-Json
$mod=$inventory | Where-Object id -eq $target
if (@($mod).Count -ne 1) {throw 'Expected one effective modded dropship'}
# Verify matching unit slots by their actual bone hashes, not numeric indices alone.
$entries=Get-Content (Join-Path $EvidenceDirectory '../assets/entries-skinny.json') -Raw | ConvertFrom-Json
$winner=$entries | Where-Object {$_.key -eq ($target+':e0a48d0be9a7453f')} | Select-Object -Last 1
if (-not $winner) {throw 'Mod source resource not found'}
$patchPath=Join-Path $PSScriptRoot ('../dist/pack-optimized-current/'+$winner.patch)
$reader=[IO.BinaryReader]::new([IO.File]::OpenRead([IO.Path]::GetFullPath($patchPath)))
try {$reader.BaseStream.Position=$winner.mo;$modBytes=$reader.ReadBytes([int]$winner.main)} finally {$reader.Dispose()}
$modOffsets=@(Pointers $modBytes (U32 $modBytes 100))
if ($modOffsets.Count -ne $unitMeshOffsets.Count) {throw 'Unit mesh table sizes differ'}
for ($i=0;$i -lt $modOffsets.Count;$i++) {
    if ((U32 $modBytes ($modOffsets[$i]+40)) -ne (U32 $unit ($unitMeshOffsets[$i]+40))) {throw 'Mod and vanilla unit mesh bone identities differ'}
}
$vanilla=@(Summarize $vanillaLods $orderedMeshes);$replacement=@(Summarize $mod.lodGroups $mod.meshes)
$comparison=@(foreach ($row in $vanilla) {
    $other=@($replacement | Where-Object {$_.group -eq $row.group -and $_.level -eq $row.level})
    if ($other.Count -ne 1 -or [math]::Abs($other[0].detailMin-$row.detailMin) -gt .000001 -or [math]::Abs($other[0].detailMax-$row.detailMax) -gt .000001) {throw 'LOD tables cannot be aligned by group/level/threshold'}
    [pscustomobject]@{group=$row.group;level=$row.level;boneNames=$row.boneNames;vanillaTriangleEquivalent=$row.triangleListEquivalent;modTriangleEquivalent=$other[0].triangleListEquivalent;ratio=if($row.triangleListEquivalent){$other[0].triangleListEquivalent/$row.triangleListEquivalent}else{$null};vanillaMaterialGroups=$row.materialGroups;modMaterialGroups=$other[0].materialGroups}
})
$result=[ordered]@{
    resource=$target;geometryGroup='417da1b8e06cb5b9';vanillaLayoutCount=$layouts.Count;vanillaMeshCount=$meshes.Count
    sources=@($unitPath,$groupPath,$gpuPath,(Join-Path $EvidenceDirectory 'representative-meshes.json')) | ForEach-Object { @{name=[IO.Path]::GetFileName($_);sha256=(Get-FileHash -LiteralPath $_).Hash.ToLowerInvariant()} }
    vanilla=$vanilla;replacement=$replacement;comparison=$comparison
    method='Filediver metadata structures; external geometry resolved by unit ID then GroupBoneHash (external order differs from unit mesh order). LOD entries index the unit mesh table. Counts sum indices/3 per referenced entry; groups are not assumed simultaneous or necessarily ordinary visible geometry. Matching thresholds checked.'
    limitation='Static asset comparison, not runtime draws, distance selection, GPU residency or measured FPS. Hashes identify extracted inputs, not the entire installed game.'
}
$result | ConvertTo-Json -Depth 10 | Set-Content (Join-Path $EvidenceDirectory 'dropship-vanilla-comparison.json') -Encoding UTF8
$comparison | Format-Table -AutoSize
