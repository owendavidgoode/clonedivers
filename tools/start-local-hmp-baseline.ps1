# Activate the verified installed pack using ordinary same-drive mod-file moves.
# No downloads, base files, graphics settings, or diagnostic control changes.
param([Parameter(Mandatory)][string]$SessionDirectory)
$ErrorActionPreference='Stop'
$game='C:/Program Files (x86)/Steam/steamapps/common/Helldivers 2'
$data=[IO.Path]::GetFullPath((Join-Path $game 'data'))
$off=[IO.Path]::GetFullPath((Join-Path $game 'mods_off'))
if (Get-Process helldivers2,Clonedivers -ErrorAction SilentlyContinue) {throw 'Close game and launcher before baseline activation'}
if (Test-Path -LiteralPath (Join-Path $env:APPDATA 'Clonedivers/apply-plan.json')) {throw 'Pending launcher operation must finish first'}
$manifest=Get-Content (Join-Path $PSScriptRoot '../manifest.json') -Raw | ConvertFrom-Json
$config=Get-Content (Join-Path $env:APPDATA 'Clonedivers/config.json') -Raw | ConvertFrom-Json
if ($manifest.pack.version -ne '2026.09.12-r8' -or $config.InstalledPackVersion -ne $manifest.pack.version -or -not $config.Options.commandos -or $config.Options.skinny) {throw 'Expected installed Commandodivers Full r8'}
$enabled=@{commandos=$true;skinny=$false}
$files=@($manifest.pack.files | Where-Object {(-not $_.option -or $enabled[$_.option]) -and (-not $_.unlessOption -or -not $enabled[$_.unlessOption])})
$active=@(Get-ChildItem -LiteralPath $data -File | Where-Object Name -match '\.patch_\d+(\.(stream|gpu_resources))?$')
$parked=@(Get-ChildItem -LiteralPath $off -File | Where-Object Name -match '\.patch_\d+(\.(stream|gpu_resources))?$')
if ($active.Count -ne 0 -or $files.Count -ne 788 -or $parked.Count -ne 788) {throw 'Expected exactly the verified parked baseline and no active patches'}
foreach ($file in $files) {
    if ($file.name -notmatch '^9ba626afa44a3aa3\.patch_\d+(\.(stream|gpu_resources))?$') {throw 'Unexpected baseline filename'}
    $source=[IO.Path]::GetFullPath((Join-Path $off $file.name));$destination=[IO.Path]::GetFullPath((Join-Path $data $file.name))
    if ([IO.Path]::GetDirectoryName($source) -ne $off -or [IO.Path]::GetDirectoryName($destination) -ne $data) {throw 'Baseline path escapes exact game directories'}
    if (Test-Path -LiteralPath $destination) {throw 'Baseline destination already exists'}
    if ((Get-Item -LiteralPath $source).Length -ne $file.size -or (Get-FileHash -LiteralPath $source).Hash -ne $file.sha256) {throw "Baseline hash mismatch: $($file.name)"}
}
$receipt=[ordered]@{state='prepared';originalMode='helldivers';targetMode='commandos';textureProfile='full';packVersion=$manifest.pack.version;gameDirectory=$game;files=$files;createdUtc=[DateTime]::UtcNow.ToString('o')}
$receiptPath=Join-Path $SessionDirectory 'baseline-activation.json'
if(Test-Path -LiteralPath $receiptPath){throw 'Session already has an activation receipt'}
$receipt | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $receiptPath -Encoding UTF8
foreach ($file in $files) {Move-Item -LiteralPath (Join-Path $off $file.name) -Destination (Join-Path $data $file.name)}
$receipt.state='active';$receipt | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $receiptPath -Encoding UTF8
Write-Output 'Activated verified Commandodivers Full r8 baseline: 788 existing mod files.'
Start-Process -FilePath 'steam://rungameid/553850'
