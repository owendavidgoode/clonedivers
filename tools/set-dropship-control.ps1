# Developer-only temporary overlay. No pack feed, launcher settings or base-game edits.
param(
    [ValidateSet('Status','Enable','Disable')][string]$Action='Status',
    [string]$GameDirectory='C:/Program Files (x86)/Steam/steamapps/common/Helldivers 2',
    [string]$ControlDirectory=(Join-Path $PSScriptRoot '../dist/investigation-2026-09-13/geometry/dropship-control'),
    [string]$ReceiptPath=(Join-Path $PSScriptRoot '../dist/investigation-2026-09-13/geometry/control-install.json')
)
$ErrorActionPreference='Stop'
$GameDirectory=[IO.Path]::GetFullPath($GameDirectory).TrimEnd('\','/')
$ControlDirectory=[IO.Path]::GetFullPath($ControlDirectory)
$ReceiptPath=[IO.Path]::GetFullPath($ReceiptPath)
function PlainPath([string]$path) {
    $current=$path
    while ($current) {
        if (Test-Path -LiteralPath $current) {
            # OneDrive placeholders are reparse points too; reject actual path links.
            if ((Get-Item -LiteralPath $current -Force).LinkType -in @('Junction','SymbolicLink')) {throw 'Linked paths are not supported for this private control'}
        }
        $parent=[IO.Path]::GetDirectoryName($current)
        if ($parent -eq $current) {break};$current=$parent
    }
}
function SaveReceipt($receipt) {
    $temp=$ReceiptPath+'.tmp';$receipt | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $temp -Encoding UTF8
    Move-Item -LiteralPath $temp -Destination $ReceiptPath -Force
}
$data=Join-Path $GameDirectory 'data';$off=Join-Path $GameDirectory 'mods_off'
foreach ($path in @($data,$off,$ReceiptPath,$ControlDirectory)) {PlainPath $path}
$receipt=if (Test-Path -LiteralPath $ReceiptPath) {Get-Content -LiteralPath $ReceiptPath -Raw | ConvertFrom-Json} else {$null}
if ($Action -eq 'Status') {
    [pscustomobject]@{action=$Action;receiptState=if($receipt){$receipt.state}else{'absent'};activeMainPatches=@(Get-ChildItem -LiteralPath $data -File -ErrorAction SilentlyContinue | Where-Object Name -match '\.patch_\d+$').Count;instruction='Run the normal HMP baseline before enabling. Close game and launcher for Enable/Disable.'}
    return
}
if (Get-Process helldivers2,Clonedivers -ErrorAction SilentlyContinue) {throw 'Close the game and launcher before changing the control'}
if (Test-Path -LiteralPath (Join-Path $env:APPDATA 'Clonedivers/apply-plan.json')) {throw 'Finish the pending launcher operation first'}
if ($Action -eq 'Disable') {
    if (-not $receipt -or $receipt.state -eq 'disabled') {Write-Output 'No active control receipt.';return}
    if ($receipt.gameDirectory -ne $GameDirectory -or $receipt.name -notmatch '^9ba626afa44a3aa3\.patch_\d+$' -or $receipt.sha256 -notmatch '^[a-f0-9]{64}$') {throw 'Receipt does not identify this game control'}
    $targets=@(foreach ($folder in @($data,$off)) {
        $path=Join-Path $folder $receipt.name;PlainPath $path
        if (Test-Path -LiteralPath $path) {
            if ((Get-FileHash -LiteralPath $path).Hash -ne $receipt.sha256) {throw 'Control filename contains different bytes; nothing removed'}
            $path
        }
    })
    # All candidates verified before any removal. Only exact recorded nonrecursive files.
    foreach ($path in $targets) {Remove-Item -LiteralPath $path}
    $receipt.state='disabled';SaveReceipt $receipt
    Write-Output 'Dropship control removed. Existing pack and mode preserved.'
    return
}
if ($receipt -and $receipt.state -ne 'disabled') {throw 'An earlier control receipt is active; Disable it before enabling again'}
$geometryReceipt=Join-Path $PSScriptRoot '../dist/geometry-candidates/install.json'
if (Test-Path -LiteralPath $geometryReceipt) {
    $geometry=Get-Content -LiteralPath $geometryReceipt -Raw | ConvertFrom-Json
    if ($geometry.state -ne 'disabled') {throw 'Restore the geometry trial before enabling the original-dropship control'}
}
$patches=@(Get-ChildItem -LiteralPath $data -File | Where-Object Name -match '^9ba626afa44a3aa3\.patch_\d+$')
if (-not $patches.Count) {throw 'Select the normal modded pack first; this control is not a standalone installation'}
$report=Get-Content -LiteralPath (Join-Path $ControlDirectory 'build-report.json') -Raw | ConvertFrom-Json
$file=@($report.files | Where-Object name -eq '9ba626afa44a3aa3.patch_0')
if ($file.Count -ne 1 -or $file[0].sha256 -notmatch '^[a-f0-9]{64}$') {throw 'Invalid staged control report'}
$source=Join-Path $ControlDirectory $file[0].name
if ((Get-FileHash -LiteralPath $source).Hash -ne $file[0].sha256 -or (Get-Item -LiteralPath $source).Length -ne $file[0].bytes) {throw 'Staged control bytes differ from build report'}
$next=1+($patches | ForEach-Object {[int]($_.Name -replace '^.*patch_','')} | Measure-Object -Maximum).Maximum
$name='9ba626afa44a3aa3.patch_'+$next
foreach ($folder in @($data,$off)) {
    foreach ($suffix in @('','.stream','.gpu_resources')) {if(Test-Path -LiteralPath (Join-Path $folder ($name+$suffix))) {throw 'Chosen control slot already exists'}}
}
$receipt=[pscustomobject]@{state='prepared';gameDirectory=$GameDirectory;name=$name;sha256=$file[0].sha256;bytes=$file[0].bytes;createdUtc=[DateTime]::UtcNow.ToString('o');purpose=$report.purpose}
SaveReceipt $receipt
$destination=Join-Path $data $name
# Zero-length companions are unnecessary: control TOC has no stream/GPU payload.
Copy-Item -LiteralPath $source -Destination $destination
if ((Get-FileHash -LiteralPath $destination).Hash -ne $receipt.sha256) {throw 'Control copy failed verification; retain receipt for recovery'}
$receipt.state='enabled';SaveReceipt $receipt
Write-Output 'Private dropship geometry control enabled. Check appearance first; keep launcher closed until Disable restores HMP.'
