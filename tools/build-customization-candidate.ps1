# Add a local, default-on CIS visual switch to the verified r9 profile feed.
# This writes only a fresh candidate directory; it never installs or publishes.
param([Parameter(Mandatory=$true)][string]$OutDirectory)
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
. (Join-Path $PSScriptRoot 'deployment-contract.ps1')
$OutDirectory=[IO.Path]::GetFullPath($OutDirectory)
if (Test-Path -LiteralPath $OutDirectory) { throw 'Choose a fresh candidate directory.' }
$source=Join-Path $root 'manifest-v3.json'
$reportPath=Join-Path $root 'build-inputs/r8/deploy-report.json'
$receipt=Get-Content (Join-Path $root 'build-inputs/r8/deployment-receipt.json') -Raw | ConvertFrom-Json
if ((Get-ContentHash $reportPath) -ne $receipt.reportSha256) { throw 'Deployment ownership report failed provenance check.' }
$manifest=Get-Content $source -Raw | ConvertFrom-Json
if ($manifest.format -ne 3 -or $manifest.pack.version -ne '2026.09.13-r9') { throw 'This migration is bound to the published r9 profile feed.' }
if ($manifest.pack.options.id -contains 'droids') { throw 'Droid option already exists.' }
$report=Get-Content $reportPath -Raw | ConvertFrom-Json
$groups=@($report | Where-Object mod -EQ 'Automaton to CIS Overhaul')
if ($groups.Count -ne 33) { throw "Unexpected CIS ownership count: $($groups.Count)" }
$gated=@{}
foreach ($group in $groups) { $gated[$group.file]=$true }
$receiptFiles=@{}; foreach ($f in $receipt.files) { $receiptFiles[$f.name]=$f }
$count=0
foreach ($file in $manifest.pack.files) {
    $main=$file.name -replace '(\.stream|\.gpu_resources)$',''
    if (!$gated.ContainsKey($main)) { continue }
    if ($file.option -or $file.unlessOption) { throw 'CIS file already has an independent optional predicate.' }
    if ($file.textureProfiles -contains 'full') {
        $original=$receiptFiles[$file.name]
        if (!$original -or $original.sha256 -ne $file.sha256 -or $original.size -ne $file.size) { throw "Ownership baseline changed: $($file.name)" }
    }
    $file | Add-Member -NotePropertyName option -NotePropertyValue 'droids'
    $count++
}
foreach ($group in $groups) {
    foreach ($profile in @('full','lighter')) {
        if (@($manifest.pack.files | Where-Object { $_.name -eq $group.file -and $profile -in $_.textureProfiles -and $_.option -eq 'droids' }).Count -ne 1) { throw 'CIS main bundle missing from a profile.' }
    }
}
$manifest.pack.options += [pscustomobject]@{id='droids';name='Droid skins';description='Replace Automaton enemies, vehicles and structures with the CIS visual pack. Turn off to restore their original models. Enemy audio stays unchanged.';default=$true}
$manifest.pack.version='2026.09.20-customization-preview'
Write-ContractJson (Join-Path $OutDirectory 'manifest.json') $manifest
Write-ContractJson (Join-Path $OutDirectory 'candidate.json') ([ordered]@{
    sourceManifestSha256=(Get-ContentHash $source);ownershipReportSha256=(Get-ContentHash $reportPath)
    candidateManifestSha256=(Get-ContentHash (Join-Path $OutDirectory 'manifest.json'))
    option='droids';bundleCount=$groups.Count;gatedEntries=$count;newAssets=0;runtimeTested=$false
    groups=$groups
})
"Prepared default-on droid toggle: $($groups.Count) bundles, $count entries, no new asset bytes. Not installed or published."
