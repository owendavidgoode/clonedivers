# Shared local provenance checks. Receipts bind a recipe/report/variant to exact bytes.
function Get-ContentHash([string]$Path) {
    (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}
function Write-ContractJson([string]$Path, $Value) {
    New-Item -ItemType Directory -Force (Split-Path ([IO.Path]::GetFullPath($Path))) | Out-Null
    [IO.File]::WriteAllText([IO.Path]::GetFullPath($Path), (ConvertTo-Json -InputObject $Value -Depth 12), [Text.UTF8Encoding]::new($false))
}
function Get-PatchInventory([string]$Directory) {
    @(Get-ChildItem -LiteralPath $Directory -File | Where-Object Name -match '^.+\.patch_\d+(\.(gpu_resources|stream))?$' |
        Sort-Object Name | ForEach-Object { [pscustomobject]@{name=$_.Name;size=[long]$_.Length;sha256=(Get-ContentHash $_.FullName)} })
}
function Get-InventoryHash($Inventory) {
    $text = (@($Inventory | Sort-Object name | ForEach-Object { "$($_.name)|$($_.size)|$($_.sha256)" }) -join "`n")
    $sha = [Security.Cryptography.SHA256]::Create()
    try { ([BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($text)))).Replace('-','').ToLowerInvariant() }
    finally { $sha.Dispose() }
}
function Assert-ReportInventory($Report, $Inventory, $Recipe) {
    $mods = @{}
    foreach ($mod in $Recipe.mods) { if ($mod.enabled -ne $false) { $mods[$mod.name] = $true } }
    $names = @{}
    foreach ($entry in $Report) {
        if (!$mods.ContainsKey($entry.mod)) { throw "Report names an absent/disabled mod: $($entry.mod)" }
        if ($entry.file -notmatch '^.+\.patch_\d+$' -or @($entry.parts) -notcontains '.patch') { throw 'Invalid report patch set' }
        foreach ($part in $entry.parts) {
            if ($part -notin @('.patch','.stream','.gpu_resources')) { throw 'Invalid report companion' }
            $name = if ($part -eq '.patch') { $entry.file } else { "$($entry.file)$part" }
            if ($names.ContainsKey($name)) { throw "Duplicate report file: $name" }
            $names[$name] = $true
        }
    }
    $actual = @($Inventory.name)
    if ($actual.Count -ne $names.Count -or @($actual | Where-Object { !$names.ContainsKey($_) }).Count) {
        throw 'Deployment report does not describe the exact file set. Rebuild/report the pack before publishing.'
    }
}
function Write-DeploymentReceipt([string]$Data, [string]$Recipe, [string]$Report) {
    $inventory = Get-PatchInventory $Data
    Assert-ReportInventory (Get-Content $Report -Raw | ConvertFrom-Json) $inventory (Get-Content $Recipe -Raw | ConvertFrom-Json)
    $receipt = [ordered]@{format=1;recipeSha256=(Get-ContentHash $Recipe);reportSha256=(Get-ContentHash $Report);inventorySha256=(Get-InventoryHash $inventory);files=$inventory}
    Write-ContractJson "$Report.receipt.json" $receipt
}
function Assert-DeploymentReceipt([string]$Data, [string]$Recipe, [string]$Report, $Inventory = $null) {
    if (!(Test-Path -LiteralPath "$Report.receipt.json")) { throw 'Deployment receipt missing. Do not publish an unverified or historical report.' }
    $receipt = Get-Content -LiteralPath "$Report.receipt.json" -Raw | ConvertFrom-Json
    if ($receipt.format -ne 1 -or $receipt.recipeSha256 -ne (Get-ContentHash $Recipe) -or $receipt.reportSha256 -ne (Get-ContentHash $Report)) {
        throw 'Recipe/report changed since deployment. Rebuild and regenerate the deployment receipt.'
    }
    if ($null -eq $Inventory) { $Inventory = Get-PatchInventory $Data }
    Assert-ReportInventory (Get-Content $Report -Raw | ConvertFrom-Json) $Inventory (Get-Content $Recipe -Raw | ConvertFrom-Json)
    if ((Get-InventoryHash $Inventory) -ne $receipt.inventorySha256) { throw 'Deployed files changed since the report was verified.' }
}
function Write-VariantReceipt([string]$Directory, $BaseInventory, [string]$Evidence) {
    Write-ContractJson (Join-Path $Directory 'variant-receipt.json') ([ordered]@{
        format=1;baseInventorySha256=(Get-InventoryHash $BaseInventory);inventorySha256=(Get-InventoryHash (Get-PatchInventory $Directory));evidence=$Evidence
    })
}
function Assert-VariantReceipt([string]$Directory, $BaseInventory) {
    $path = Join-Path $Directory 'variant-receipt.json'
    if (!(Test-Path -LiteralPath $path)) { throw "Variant receipt missing: $Directory. Use optimize-verified-pack.ps1." }
    $receipt = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
    if ($receipt.format -ne 1 -or $receipt.baseInventorySha256 -ne (Get-InventoryHash $BaseInventory)) { throw 'Texture variant was built for a different base pack.' }
    if ($receipt.inventorySha256 -ne (Get-InventoryHash (Get-PatchInventory $Directory))) { throw 'Texture variant files changed after verification.' }
}
