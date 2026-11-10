param(
    [Parameter(Mandatory=$true)][string]$Tree,
    [Parameter(Mandatory=$true)][ValidateSet('test','engine','package')][string]$Stage,
    [string]$SteamApi,[string]$SteamSdk
)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
# A pwsh parent exports its module paths to Windows PowerShell. Prefer this
# child's own built-in modules so cmdlets cannot resolve to incompatible PS7 code.
$env:PSModulePath=(Join-Path $PSHOME 'Modules')+';'+$env:PSModulePath
Import-Module (Join-Path $PSHOME 'Modules/Microsoft.PowerShell.Utility/Microsoft.PowerShell.Utility.psd1') -ErrorAction Stop
$null=Get-Command Get-FileHash -ErrorAction Stop
Push-Location $Tree
try {
    switch($Stage){
        'test' { & ./scripts/Test.ps1 }
        'engine' { & ./scripts/Build-Engine.ps1 -SteamApi $SteamApi -SteamSdk $SteamSdk }
        'package' {
            # DebugType=none still lets MSBuild copy an adjacent project-reference PDB.
            # Use .NET SDK 10.0.403's related-file defaults with only .pdb removed.
            $env:AllowedReferenceRelatedFileExtensions='.xml;.pri;.dll.config;.exe.config'
            & ./scripts/Package-HellForge.ps1 -EnginePackage (Join-Path $Tree 'artifacts/engine-package/hellforge-engine-0.1.47.zip')
        }
    }
} finally { Pop-Location }
# Official scripts throw on errors; some successful smoke tests deliberately leave
# LASTEXITCODE=1 or2. Never interpret that leftover value as this wrapper's exit.
exit 0
