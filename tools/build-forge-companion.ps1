param(
    [Parameter(Mandatory=$true)][string]$BridgeBinary,
    [Parameter(Mandatory=$true)][string]$OutputFolder
)
$ErrorActionPreference='Stop'
$outputPath=[IO.Path]::GetFullPath($OutputFolder)
if(Test-Path -LiteralPath $outputPath){throw 'Use a fresh companion output folder.'}
$pin=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'hellforge-linux/upstream-pin.json') -Raw | ConvertFrom-Json
$sourcePath=Join-Path ([IO.Path]::GetTempPath()) ('clonedivers-forge-scripts-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path (Join-Path $sourcePath 'linux') -Force | Out-Null
$baseUrl='https://raw.githubusercontent.com/SkyeShade/HellForge/'+$pin.SourceCommit+'/'
foreach($file in $pin.Scripts){
    $target=Join-Path $sourcePath ('linux/'+$file.Name)
    Invoke-WebRequest -Uri ($baseUrl+'linux/'+$file.Name) -OutFile $target
    if((Get-FileHash -LiteralPath $target).Hash.ToLowerInvariant() -cne $file.UpstreamSha256){throw ('Upstream script hash mismatch: '+$file.Name)}
}
Invoke-WebRequest -Uri ($baseUrl+'LICENSE') -OutFile (Join-Path $sourcePath 'LICENSE')
if((Get-FileHash -LiteralPath (Join-Path $sourcePath 'LICENSE')).Hash.ToLowerInvariant() -cne '3972dc9744f6499f0f9b2dbf76696f2ae7ad8af9b23dde66d6af86c9dfb36986'){throw 'Upstream GPL license hash mismatch.'}
# stage-hellforge-linux checks every original hash and applies only the reviewed
# lifetime ancestor exclusion. It never writes into an installed game/prefix.
& (Join-Path $PSScriptRoot 'stage-hellforge-linux.ps1') -SourceRoot $sourcePath -OutputFolder (Join-Path $outputPath 'linux')
if(!(Test-Path -LiteralPath $BridgeBinary)){throw 'Bridge binary missing.'}
Copy-Item -LiteralPath $BridgeBinary -Destination (Join-Path $outputPath 'HellForge.Launcher.ClonediversBridge.exe')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot '../LICENSE') -Destination (Join-Path $outputPath 'LICENSE-CLONEDIVERS.txt')
@{format=1;bridgeVersion='0.3.8';bridgeSha256=(Get-FileHash -LiteralPath $BridgeBinary).Hash.ToLowerInvariant();gameBuild='25480438';
 qualification='Source/package checks only; actual Linux account, Proton gameplay and Windows-Linux multiplayer remain unqualified';
 scripts='GPL-3.0 complete source scripts/COPYING; reviewed lifetime-only adaptation'} |
 ConvertTo-Json | Set-Content -LiteralPath (Join-Path $outputPath 'companion.json') -Encoding utf8
