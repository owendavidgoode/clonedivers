param(
    [Parameter(Mandatory=$true)][string]$Template,
    [string]$OutDir = '', [string]$Uv = '', [string]$Python = '3.13'
)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
. (Join-Path $PSScriptRoot 'deployment-contract.ps1')
if (!$OutDir) { $OutDir = Join-Path $root 'dist/rc-rebuild' }
if (!$Uv) { $Uv = Join-Path $root 'dist/rc-upgrade/uv/uv.exe' }
$lock = Get-Content (Join-Path $root 'build-inputs/rc/inputs.lock.json') -Raw | ConvertFrom-Json
$mapping = Join-Path $root 'build-inputs/rc/voice-mapping.json'
if ((Get-ContentHash $mapping) -ne $lock.portableMappingSha256) { throw 'Voice mapping changed; review and update the input lock intentionally.' }
if ((Get-ContentHash $Template) -ne $lock.templateSha256) { throw 'Voice template differs from the shipped build.' }
foreach ($inputFile in @($lock.sources) + @($lock.dependencies)) {
    if ((Get-ContentHash (Join-Path $root $inputFile.path)) -ne $inputFile.sha256) { throw "Input changed: $($inputFile.path)" }
}
# Fresh output prevents a cached converted WEM from concealing a changed conversion tool.
if (Test-Path -LiteralPath $OutDir) { throw "Choose a new empty output path: $OutDir" }
$env:UV_CACHE_DIR = Join-Path $root 'dist/rc-upgrade/uv-cache'
& $Uv run --offline --no-project --python $Python python (Join-Path $PSScriptRoot 'build-rc-voices.py') --workspace $root --template $Template --mapping $mapping --out $OutDir
if ($LASTEXITCODE -ne 0) { throw 'Voice build failed' }
foreach ($f in $lock.outputs) {
    $path = Join-Path $OutDir $f.name
    if ((Get-Item -LiteralPath $path).Length -ne $f.size -or (Get-ContentHash $path) -ne $f.sha256) { throw "Rebuilt output differs: $($f.name)" }
}
Write-ContractJson (Join-Path $OutDir 'reproduction.json') ([ordered]@{verifiedAt=(Get-Date).ToString('o');mappingSha256=$lock.portableMappingSha256;allOutputHashesMatch=$true;files=$lock.outputs})
'PASS: fresh voice build is byte-identical to the released patch.'
