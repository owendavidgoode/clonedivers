# Build a format-3 candidate from a verified published base and independent texture profiles.
# Does not deploy or publish. The legacy build-manifest.ps1 keeps its format-2 contract.
param(
    [Parameter(Mandatory=$true)][string]$BaseManifest,
    [Parameter(Mandatory=$true)][string]$BaseDirectory,
    [Parameter(Mandatory=$true)][string]$ProfilesPath,
    [Parameter(Mandatory=$true)][string]$Version,
    [Parameter(Mandatory=$true)][string]$OutPath,
    [Parameter(Mandatory=$true)][string]$UploadPlanPath,
    [string]$Repo='owendavidgoode/clonedivers'
)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'deployment-contract.ps1')
$source=Get-Content -LiteralPath $BaseManifest -Raw|ConvertFrom-Json
if($source.format -ne 2){throw 'Profile migration currently requires a published format-2 base.'}
if(Test-Path -LiteralPath $OutPath){throw 'Use a fresh candidate manifest path.'}
$spec=Get-Content -LiteralPath $ProfilesPath -Raw|ConvertFrom-Json
$baseFiles=@($source.pack.files|Where-Object {$_.option -ne 'skinny'})
$baseInventory=Get-PatchInventory $BaseDirectory
function Assert-SameInventory($actual,$expected,[string]$label){
    if((Get-InventoryHash $actual) -ne (Get-InventoryHash $expected)){throw "$label does not match the base manifest's canonical hashes."}
}
Assert-SameInventory $baseInventory $baseFiles 'Full source'
$knownSources=@((Get-InventoryHash $baseInventory))
$lighterFiles=@($source.pack.files|Where-Object {$_.unlessOption -ne 'skinny'})
$knownSources+=(Get-InventoryHash $lighterFiles)
$profiles=New-Object System.Collections.ArrayList
[void]$profiles.Add([ordered]@{id='full';name='Full textures';description='Original texture detail.'})
$catalog=@{full=$true};$variants=@{}
foreach($p in @($spec)){
    if($p.id -notmatch '^[a-z][a-z0-9-]{0,31}$' -or $catalog.ContainsKey([string]$p.id) -or ![string]$p.name){throw 'Invalid or duplicate profile ID/name.'}
    $catalog[[string]$p.id]=$true
    $inventory=Get-PatchInventory $p.directory
    $receiptBase=$baseInventory
    if($p.PSObject.Properties['receiptSourceDirectory'] -and $p.receiptSourceDirectory){
        $receiptBase=Get-PatchInventory $p.receiptSourceDirectory
        if($knownSources -notcontains (Get-InventoryHash $receiptBase)){throw 'Variant receipt source is not a verified original or prior profile.'}
    }
    Assert-VariantReceipt $p.directory $receiptBase
    $baseMain=@($baseInventory|Where-Object name -match '\.patch_\d+$'|Select-Object -ExpandProperty name|Sort-Object)
    $variantMain=@($inventory|Where-Object name -match '\.patch_\d+$'|Select-Object -ExpandProperty name|Sort-Object)
    if(($baseMain -join "`n") -cne ($variantMain -join "`n")){throw 'Profile must preserve every canonical patch bundle.'}
    foreach($f in $inventory){if(($f.name -replace '(\.gpu_resources|\.stream)$','') -notin $baseMain){throw "Orphan companion: $($f.name)"}}
    foreach($f in $baseInventory|Where-Object size -gt 0){if($f.name -notin @($inventory.name)){throw "Profile removed a nonempty companion: $($f.name)"}}
    $variants[[string]$p.id]=@{inventory=$inventory;directory=[string]$p.directory}
    $knownSources+=(Get-InventoryHash $inventory)
    [void]$profiles.Add([ordered]@{id=[string]$p.id;name=[string]$p.name;description=[string]$p.description})
}
$byName=@{};foreach($f in $baseFiles){$byName[$f.name]=$f}
$urls=@{};foreach($f in $source.pack.files){if($f.url){$urls[$f.sha256]=[string]$f.url}}
$entries=New-Object System.Collections.ArrayList;$uploads=@{};$grouped=@{}
foreach($profile in $profiles){
    $inventory=if($profile.id -eq 'full'){$baseInventory}else{$variants[$profile.id].inventory}
    $directory=if($profile.id -eq 'full'){$BaseDirectory}else{$variants[$profile.id].directory}
    foreach($f in $inventory){
        $parent=$byName[($f.name -replace '(\.gpu_resources|\.stream)$','')]
        if(!$parent){throw "No mode owner for $($f.name)"}
        $owner=if($byName.ContainsKey($f.name)){$byName[$f.name]}else{$parent}
        $modes=@(if($owner.option -eq 'commandos'){'commandos'}else{'clonedivers';'commandos'})
        $option=if($owner.option -and $owner.option -ne 'commandos'){$owner.option}else{$null}
        $unless=if($owner.unlessOption -and $owner.unlessOption -ne 'skinny'){$owner.unlessOption}else{$null}
        $key="$($f.name)|$($f.sha256)|$($modes -join ',')|$option|$unless"
        if($grouped.ContainsKey($key)){[void]$grouped[$key].textureProfiles.Add([string]$profile.id);continue}
        $url=''
        if($f.size -gt 0){
            if($urls.ContainsKey($f.sha256)){$url=$urls[$f.sha256]}
            else{
                $tag="pack-$Version-files";$url="https://github.com/$Repo/releases/download/$tag/$($f.sha256)"
                if(!$uploads.ContainsKey($f.sha256)){$uploads[$f.sha256]=[ordered]@{sha=$f.sha256;size=[long]$f.size;localPath=(Join-Path $directory $f.name);tag=$tag}}
            }
        }
        $allowed=New-Object System.Collections.ArrayList;[void]$allowed.Add([string]$profile.id)
        $entry=[ordered]@{name=$f.name;url=$url;size=[long]$f.size;sha256=$f.sha256;modes=$modes;textureProfiles=$allowed}
        if($option){$entry.option=$option};if($unless){$entry.unlessOption=$unless}
        [void]$entries.Add($entry);$grouped[$key]=$entry
    }
}
$source.format=3;$source.pack.version=$Version
$source.pack.options=@($source.pack.options|Where-Object id -ne 'skinny')
$source.pack|Add-Member -NotePropertyName textureProfiles -NotePropertyValue @($profiles) -Force
$source.pack.files=@($entries)
Write-ContractJson $OutPath $source
Write-ContractJson $UploadPlanPath @($uploads.Values)
Write-ContractJson "$OutPath.receipt.json" ([ordered]@{format=1;baseManifestSha256=(Get-ContentHash $BaseManifest);baseInventorySha256=(Get-InventoryHash $baseInventory);profileSpecSha256=(Get-ContentHash $ProfilesPath);manifestSha256=(Get-ContentHash $OutPath);uploadPlanSha256=(Get-ContentHash $UploadPlanPath);profiles=@($profiles.id);note='Offline staging only; native format-3 validation and gameplay acceptance required before publication.'})
Write-Host "Format 3 staged: $($profiles.Count) profiles, $($entries.Count) entries, $($uploads.Count) unique new assets."
