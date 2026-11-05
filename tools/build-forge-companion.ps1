param([Parameter(Mandatory=$true)][string]$BridgeBinary,[Parameter(Mandatory=$true)][string]$OutputFolder)
$ErrorActionPreference='Stop'
$output=[IO.Path]::GetFullPath($OutputFolder)
if(Test-Path -LiteralPath $output){throw 'Use a fresh companion output folder.'}
if(!(Test-Path -LiteralPath $BridgeBinary)){throw 'Native Linux bridge binary missing.'}
New-Item -ItemType Directory -Path $output|Out-Null
Copy-Item -LiteralPath $BridgeBinary -Destination (Join-Path $output 'HellForge.Launcher.ClonediversBridge')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot '../LICENSE') -Destination (Join-Path $output 'LICENSE-CLONEDIVERS.txt')
@{format=1;bridgeVersion='0.4.0';bridgeSha256=(Get-FileHash -LiteralPath $BridgeBinary).Hash.ToLowerInvariant();gameBuild='25480438';
 control='native-linux';hellforge='0.4.0-beta';engine='0.1.47';modSdk=16;
 qualification='Native control/package checks; actual Linux capture, gameplay and multiplayer need separate acceptance';
 runtime='Uses the installed official native CLI/backend/launcher and full official namespace/sandbox/relay helpers. No 0.3 helper scripts are overlaid.'}|
 ConvertTo-Json|Set-Content -LiteralPath (Join-Path $output 'companion.json') -Encoding utf8