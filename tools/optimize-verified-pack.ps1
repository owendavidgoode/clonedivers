param(
    [Parameter(Mandatory=$true)][string]$Source,
    [Parameter(Mandatory=$true)][string]$OutDir,
    [string]$Tool = '', [string]$DotnetRoot = '',
    [switch]$AllowUnchangedDiagnostics,
    [ValidateRange(1,8192)][int]$StreamFloor=512,
    [ValidateRange(0,16384)][int]$MaxSize=0,
    [switch]$Restream
)
$ErrorActionPreference = 'Stop'
if (($StreamFloor -band ($StreamFloor - 1)) -ne 0 -or ($MaxSize -gt 0 -and ($MaxSize -band ($MaxSize - 1)) -ne 0)) { throw 'Texture size and stream floor must be powers of two (or MaxSize 0 for full detail).' }
. (Join-Path $PSScriptRoot 'deployment-contract.ps1')
if (Test-Path -LiteralPath $OutDir) { throw 'Choose a fresh variant output folder; verified variants are not scratch space.' }
if ([IO.Path]::GetFullPath($Source) -eq [IO.Path]::GetFullPath($OutDir)) { throw 'Variant output must differ from the source.' }
$before = Get-PatchInventory $Source
$params = @('-NoProfile','-ExecutionPolicy','Bypass','-File',(Join-Path $PSScriptRoot 'optimize-pack.ps1'),'-Source',$Source,'-OutDir',$OutDir)
if ($Tool) { $params += @('-Tool',$Tool) }; if ($DotnetRoot) { $params += @('-DotnetRoot',$DotnetRoot) }
if ($AllowUnchangedDiagnostics) { $params += '-AllowUnchangedDiagnostics' }
$params += @('-StreamFloor',"$StreamFloor",'-MaxSize',"$MaxSize")
if ($Restream) { $params += '-Restream' }
# Use a child so the optimizer's exit statement cannot skip receipt verification.
& powershell @params
if ($LASTEXITCODE -ne 0) { throw "Optimizer failed: $LASTEXITCODE" }
if ((Get-InventoryHash (Get-PatchInventory $Source)) -ne (Get-InventoryHash $before)) { throw 'Base pack changed during optimization' }
Write-VariantReceipt $OutDir $before 'Verified optimizer completed successfully; source unchanged during build.'
$receiptPath=Join-Path $OutDir 'variant-receipt.json'
$receipt=Get-Content -LiteralPath $receiptPath -Raw|ConvertFrom-Json
$receipt|Add-Member -NotePropertyName settings -NotePropertyValue ([ordered]@{streamFloor=$StreamFloor;maxSize=$MaxSize;restream=[bool]$Restream;dedup=$false;allowUnchangedDiagnostics=[bool]$AllowUnchangedDiagnostics;tool=$Tool})
if($Tool -and (Test-Path -LiteralPath $Tool)){$receipt.settings.toolSha256=Get-ContentHash $Tool}
if($Tool){
    $assemblies=@{}
    foreach($name in @('stingray-tex.dll','Stingray.Core.dll','BCnEncoder.dll')){
        $path=Join-Path (Split-Path $Tool -Parent) $name
        if(Test-Path -LiteralPath $path){$assemblies[$name]=Get-ContentHash $path}
    }
    $receipt.settings.assemblySha256=$assemblies
}
Write-ContractJson $receiptPath $receipt
