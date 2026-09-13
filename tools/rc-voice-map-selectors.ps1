# Export original RC SoundMultiple object properties to recover exact semantic clip groups.
# Requires the disposable runtime made by export-rc-dialogue.ps1; never touches the RC installation.
param(
    [string]$RuntimeSystem = (Join-Path $PSScriptRoot '..\dist\rc-dialogue-complete\runtime\System'),
    [string]$OutDir = (Join-Path $PSScriptRoot '..\dist\rc-upgrade\rc-selectors')
)
$ErrorActionPreference = 'Stop'
$runtimePath = (Resolve-Path -LiteralPath $RuntimeSystem).Path
$outputPath = [IO.Path]::GetFullPath($OutDir)
if (Test-Path -LiteralPath $outputPath) { throw 'Choose a new output directory; existing selector exports are preserved.' }
if (-not (Test-Path -LiteralPath (Join-Path $runtimePath 'UCC.exe'))) { throw 'Disposable RC runtime must contain the community UCC commandlet.' }
New-Item -ItemType Directory -Path $outputPath | Out-Null
$logPath = $outputPath + '.log'
Push-Location $runtimePath
try {
    & .\UCC.exe batchexport character_voice.uax SoundMultiple t3d $outputPath > $logPath 2>&1
    $exportCode = $LASTEXITCODE
} finally { Pop-Location }
if ($exportCode -notin 0,1 -or -not ((Get-Content -LiteralPath $logPath) -match '^Success - 0 error')) {
    throw "Selector export failed; inspect $logPath"
}
$selectors = @(Get-ChildItem -LiteralPath $outputPath -File -Filter 'D*.t3d' |
    Where-Object BaseName -Match '^D(07|38|40|62)_')
if (-not $selectors.Count) { throw 'Export produced no Delta SoundMultiple selector objects.' }
$references = 0
foreach ($selector in $selectors) {
    $content = Get-Content -LiteralPath $selector.FullName -Raw
    if ($content -notmatch '^Begin Object Class=SoundMultiple' -or $content -notmatch 'End Object') {
        throw "Malformed object export: $($selector.FullName)"
    }
    $references += [regex]::Matches($content, "Sounds\(\d+\)=Sound'Character_Voice\.Delta_(07|38|40|62)\.[^']+'").Count
}
Write-Host "Exported $($selectors.Count) Delta selector objects with $references original Sound references."
