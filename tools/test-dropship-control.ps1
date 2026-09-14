$ErrorActionPreference='Stop'
$fixture=Join-Path $PSScriptRoot ('../dist/dropship-control-tests/'+[guid]::NewGuid().ToString('N'))
$fixture=[IO.Path]::GetFullPath($fixture)
$game=Join-Path $fixture 'game';$data=Join-Path $game 'data';$off=Join-Path $game 'mods_off'
New-Item -ItemType Directory -Path $data,$off -Force | Out-Null
$receipt=Join-Path $fixture 'control.json';$script=Join-Path $PSScriptRoot 'set-dropship-control.ps1'
$base=Join-Path $data '9ba626afa44a3aa3.patch_0'
[IO.File]::WriteAllText($base,'Fixture existing mod; never a real game archive.')
$baseHash=(Get-FileHash -LiteralPath $base).Hash
$checks=0
function Check($value,$name) {if(-not $value){throw $name};$script:checks++;Write-Output "PASS $name"}
& $script -Action Enable -GameDirectory $game -ReceiptPath $receipt | Out-Null
$state=Get-Content $receipt -Raw | ConvertFrom-Json
$control=Join-Path $data $state.name
Check ($state.state -eq 'enabled' -and $state.name -eq '9ba626afa44a3aa3.patch_1' -and (Test-Path -LiteralPath $control)) 'Control appends one distinct patch'
Check ((Get-FileHash -LiteralPath $base).Hash -eq $baseHash) 'Existing mod bytes preserved'
$failed=$false;try {& $script -Action Enable -GameDirectory $game -ReceiptPath $receipt | Out-Null} catch {$failed=$true}
Check $failed 'Duplicate enable refused'
Move-Item -LiteralPath $control -Destination (Join-Path $off $state.name)
& $script -Action Disable -GameDirectory $game -ReceiptPath $receipt | Out-Null
Check (-not (Test-Path -LiteralPath (Join-Path $off $state.name)) -and (Test-Path -LiteralPath $base)) 'Removal finds exact control after a mode park'
& $script -Action Enable -GameDirectory $game -ReceiptPath $receipt | Out-Null
$state=Get-Content $receipt -Raw | ConvertFrom-Json;$control=Join-Path $data $state.name
[IO.File]::WriteAllText($control,'Different bytes must not be deleted')
$failed=$false;try {& $script -Action Disable -GameDirectory $game -ReceiptPath $receipt | Out-Null} catch {$failed=$true}
Check ($failed -and (Test-Path -LiteralPath $control) -and (Get-FileHash -LiteralPath $base).Hash -eq $baseHash) 'Changed control fails closed without deleting files'
$state.name='../outside';$state | ConvertTo-Json | Set-Content $receipt
$failed=$false;try {& $script -Action Disable -GameDirectory $game -ReceiptPath $receipt | Out-Null} catch {$failed=$true}
Check $failed 'Receipt path traversal rejected'
foreach($name in @('compare-dropship-lods.ps1','build-dropship-control.ps1','set-dropship-control.ps1')) {
    $tokens=$null;$errors=$null;[Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot $name),[ref]$tokens,[ref]$errors) | Out-Null
    Check ($errors.Count -eq 0) "$name parses"
}
Write-Output "$checks checks passed. Disposable evidence retained in $fixture"
