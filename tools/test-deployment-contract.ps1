$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'deployment-contract.ps1')
$dir=Join-Path (Split-Path $PSScriptRoot) ('dist/contract-tests/' + [Guid]::NewGuid().ToString('N'))
$data=Join-Path $dir 'data'; $variant=Join-Path $dir 'variant'
New-Item -ItemType Directory -Force $data,$variant | Out-Null
$recipe=Join-Path $dir 'recipe.json'; $report=Join-Path $dir 'report.json'
Write-ContractJson $recipe @{mods=@(@{name='base'},@{name='rc';option=@{id='commandos'}})}
$entries=@(@{mod='base';file='a.patch_0';parts=@('.patch')},@{mod='rc';file='a.patch_1';parts=@('.patch')})
Write-ContractJson $report $entries
[IO.File]::WriteAllText((Join-Path $data 'a.patch_0'),'base')
[IO.File]::WriteAllText((Join-Path $data 'a.patch_1'),'rc00')
function Reject([string]$Name,[scriptblock]$Body) {
    $rejected=$false
    try { & $Body } catch { $rejected=$true }
    if (!$rejected) { throw "Expected rejection: $Name" }
    "PASS rejects $Name"
}
Reject 'missing receipt' { Assert-DeploymentReceipt $data $recipe $report }
Write-DeploymentReceipt $data $recipe $report
Assert-DeploymentReceipt $data $recipe $report
'PASS accepts verified deployment'
[IO.File]::WriteAllText((Join-Path $data 'a.patch_1'),'oops')
Reject 'same-size changed payload' { Assert-DeploymentReceipt $data $recipe $report }
[IO.File]::WriteAllText((Join-Path $data 'a.patch_1'),'rc00')
Write-ContractJson $report @($entries[0])
Reject 'stale report with missing RC files' { Write-DeploymentReceipt $data $recipe $report }
Write-ContractJson $report @($entries + $entries[1])
Reject 'duplicate ownership in report' { Write-DeploymentReceipt $data $recipe $report }
Write-ContractJson $report $entries
Write-DeploymentReceipt $data $recipe $report
Write-ContractJson $recipe @{mods=@(@{name='base'},@{name='rc'})}
Reject 'changed mode ownership in recipe' { Assert-DeploymentReceipt $data $recipe $report }
$base=Get-PatchInventory $data
Reject 'unproven texture variant' { Assert-VariantReceipt $variant $base }
[IO.File]::WriteAllText((Join-Path $variant 'a.patch_0'),'thin')
Write-VariantReceipt $variant $base 'fixture verification'
Assert-VariantReceipt $variant $base
'PASS accepts matching variant'
[IO.File]::WriteAllText((Join-Path $data 'a.patch_1'),'next')
Reject 'variant from prior base pack' { Assert-VariantReceipt $variant (Get-PatchInventory $data) }
[IO.File]::WriteAllText((Join-Path $variant 'a.patch_0'),'bad!')
Reject 'variant changed after validation' { Assert-VariantReceipt $variant $base }
