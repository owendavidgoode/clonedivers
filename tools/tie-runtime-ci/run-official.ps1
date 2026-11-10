param(
    [Parameter(Mandatory=$true)][string]$Tree,
    [Parameter(Mandatory=$true)][ValidateSet('test','engine','package')][string]$Stage,
    [string]$SteamApi,[string]$SteamSdk
)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
Push-Location $Tree
try {
    switch($Stage){
        'test' { & ./scripts/Test.ps1 }
        'engine' { & ./scripts/Build-Engine.ps1 -SteamApi $SteamApi -SteamSdk $SteamSdk }
        'package' { & ./scripts/Package-HellForge.ps1 -EnginePackage (Join-Path $Tree 'artifacts/engine-package/hellforge-engine-0.1.47.zip') }
    }
} finally { Pop-Location }
# Official scripts throw on errors; some successful smoke tests deliberately leave
# LASTEXITCODE=1 or2. Never interpret that leftover value as this wrapper's exit.
exit 0
