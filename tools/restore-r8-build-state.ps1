# Recover local build provenance from the released r8 manifest. Never changes game files.
param(
    [Parameter(Mandatory=$true)][string]$GameDir,
    [string]$VariantSource = '', [string]$VariantOutput = ''
)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
. (Join-Path $PSScriptRoot 'deployment-contract.ps1')
if (!$VariantSource) { $VariantSource=Join-Path $root 'dist/pack-optimized' }
if (!$VariantOutput) { $VariantOutput=Join-Path $root 'dist/pack-optimized-current' }
$manifestPath=Join-Path $root 'manifest.json'
$manifest=Get-Content $manifestPath -Raw | ConvertFrom-Json
if ($manifest.pack.version -ne '2026.09.12-r8') { throw 'This recovery is specific to the verified r8 release.' }
$data=Join-Path $GameDir 'data'
$base=Get-PatchInventory $data
$expected=@($manifest.pack.files | Where-Object option -ne 'skinny')
if ((Get-InventoryHash $base) -ne (Get-InventoryHash $expected)) { throw 'Live pack does not match r8 full textures, Commandodivers enabled.' }
$report=@(Get-Content (Join-Path $root 'docs/pack/2026.09.07-r6/deploy-report.json') -Raw | ConvertFrom-Json)
foreach ($entry in $report) {
    if ($entry.file -match '^9ba626afa44a3aa3\.patch_(\d+)$') {
        $i=[int]$Matches[1]
        if ($i -ge 249) { $entry.file='9ba626afa44a3aa3.patch_' + ($i + $(if ($i -ge 257) {5} else {4})) }
    }
}
foreach ($new in @(@(249,'Republic Commando Remastered Intro','RC Intro'),@(250,'Republic Commando Remastered Intro','RC Intro Audio'),@(251,'Republic Commando Voices','RC Voices'),@(252,'Republic Commando Voice and Armor Labels','RC Labels'),@(261,'Republic Commando Starter Helmets','Starter Helmets'))) {
    $stem='9ba626afa44a3aa3.patch_' + $new[0]
    $parts=@($base | Where-Object { $_.name -eq $stem -or $_.name.StartsWith($stem + '.') } | ForEach-Object { if ($_.name -eq $stem) {'.patch'} else {$_.name.Substring($stem.Length)} })
    $report += [pscustomobject]@{mod=$new[1];folder=$new[2];file=$stem;parts=$parts;bytes=0}
}
$sizes=@{}; foreach ($f in $base) { $sizes[$f.name]=$f.size }
foreach ($entry in $report) {
    [long]$sum=0
    foreach ($part in $entry.parts) { $name=if ($part -eq '.patch') {$entry.file} else {"$($entry.file)$part"}; $sum += $sizes[$name] }
    $entry.bytes=$sum
}
$recipePath=Join-Path $root 'pack-recipe.json'
Assert-ReportInventory $report $base (Get-Content $recipePath -Raw | ConvertFrom-Json)
# Reuse verified local bytes by content hash, never by old patch number.
$available=@{}
foreach ($dir in @($data,$VariantSource)) {
    foreach ($f in (Get-PatchInventory $dir)) { $available[$f.sha256]=Join-Path $dir $f.name }
}
$variant=@($manifest.pack.files | Where-Object unlessOption -ne 'skinny')
foreach ($f in $variant) { if ($f.size -gt 0 -and !$available.ContainsKey($f.sha256)) { throw "Missing local variant asset: $($f.sha256)" } }
New-Item -ItemType Directory -Force $VariantOutput | Out-Null
$allowed=@{}; foreach ($f in $variant) { $allowed[$f.name]=$true }
foreach ($f in (Get-ChildItem -LiteralPath $VariantOutput -File -Filter '*.patch_*')) {
    if (!$allowed.ContainsKey($f.Name)) { throw 'Variant output has unrelated files; choose a new output folder.' }
}
foreach ($f in $variant) {
    $target=Join-Path $VariantOutput $f.name
    if (Test-Path -LiteralPath $target) {
        if ((Get-ContentHash $target) -ne $f.sha256) { throw "Existing output differs: $target" }
    } elseif ($f.size -eq 0) { [IO.File]::WriteAllBytes($target,[byte[]]@()) }
    else {
        try { New-Item -ItemType HardLink -Path $target -Target $available[$f.sha256] -ErrorAction Stop | Out-Null }
        catch { if (Test-Path -LiteralPath $target) { throw }; Copy-Item -LiteralPath $available[$f.sha256] -Destination $target }
    }
}
if ((Get-InventoryHash (Get-PatchInventory $VariantOutput)) -ne (Get-InventoryHash $variant)) { throw 'Reconstructed variant differs from the release' }
Write-VariantReceipt $VariantOutput $base ("Reconstructed by exact released file hashes; manifest SHA256 " + (Get-ContentHash $manifestPath))
$reportPath=Join-Path $root 'dist/deploy-report.json'
$backup=Join-Path $root 'dist/maintenance-review/deploy-report.before-repair.json'
if (!(Test-Path -LiteralPath $backup)) { New-Item -ItemType Directory -Force (Split-Path $backup) | Out-Null; Copy-Item -LiteralPath $reportPath -Destination $backup }
Write-ContractJson $reportPath $report
Write-DeploymentReceipt $data $recipePath $reportPath
'PASS: r8 report and lighter-texture build state recovered by content hash; game files untouched.'
