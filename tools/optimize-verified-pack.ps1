param(
    [Parameter(Mandatory=$true)][string]$Source,
    [Parameter(Mandatory=$true)][string]$OutDir,
    [string]$Tool = '', [string]$DotnetRoot = '',
    [switch]$AllowUnchangedDiagnostics
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'deployment-contract.ps1')
if (Test-Path -LiteralPath $OutDir) { throw 'Choose a fresh variant output folder; verified variants are not scratch space.' }
if ([IO.Path]::GetFullPath($Source) -eq [IO.Path]::GetFullPath($OutDir)) { throw 'Variant output must differ from the source.' }
$before = Get-PatchInventory $Source
$params = @('-NoProfile','-ExecutionPolicy','Bypass','-File',(Join-Path $PSScriptRoot 'optimize-pack.ps1'),'-Source',$Source,'-OutDir',$OutDir)
if ($Tool) { $params += @('-Tool',$Tool) }; if ($DotnetRoot) { $params += @('-DotnetRoot',$DotnetRoot) }
if ($AllowUnchangedDiagnostics) { $params += '-AllowUnchangedDiagnostics' }
# Use a child so the optimizer's exit statement cannot skip receipt verification.
& powershell @params
if ($LASTEXITCODE -ne 0) { throw "Optimizer failed: $LASTEXITCODE" }
if ((Get-InventoryHash (Get-PatchInventory $Source)) -ne (Get-InventoryHash $before)) { throw 'Base pack changed during optimization' }
Write-VariantReceipt $OutDir $before 'Verified optimizer completed successfully; source unchanged during build.'
