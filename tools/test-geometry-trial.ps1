# Tiny independent fixtures only: this script never targets the installed game or real candidates.
$ErrorActionPreference='Stop'
$fixture=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot ('../dist/geometry-trial-tests/'+[guid]::NewGuid().ToString('N'))))
$installer=Join-Path $PSScriptRoot 'set-geometry-trial.ps1'
$game=Join-Path $fixture 'fake-game';$data=Join-Path $game 'data';$off=Join-Path $game 'mods_off'
$candidateRoot=Join-Path $fixture 'candidates';$candidate=Join-Path $candidateRoot 'full/shadows'
$receiptPath=Join-Path $fixture 'install.json';$controlPath=Join-Path $fixture 'control.json'
$oldAppData=$env:APPDATA;$env:APPDATA=Join-Path $fixture 'appdata'
$global:GeometryTrialFixtureRunning=$false
function Get-Process {param($Name,$ErrorAction) if($global:GeometryTrialFixtureRunning){[pscustomobject]@{Name='Clonedivers'}}}
$checks=0
function Check($condition,[string]$label){if(-not $condition){throw "FAIL $label"};$script:checks++;Write-Output "PASS $label"}
function Reject([scriptblock]$operation,[string]$label,[string]$pattern='.'){
    $failed=$false;try {& $operation|Out-Null} catch {if($_.Exception.Message -notmatch $pattern){throw "Unexpected failure for ${label}: $_"};$failed=$true};Check $failed $label
}
function Hash([string]$path){(Get-FileHash -LiteralPath $path).Hash.ToLowerInvariant()}
function InvokeTrial([string]$action){& $installer -Action $action -Profile full -Variant shadows -GameDirectory $game -CandidateRoot $candidateRoot -ReceiptPath $receiptPath -ControlReceiptPath $controlPath}
function ReadReceipt {Get-Content -LiteralPath $receiptPath -Raw|ConvertFrom-Json}
function WriteReceipt($value){$value|ConvertTo-Json -Depth 12|Set-Content -LiteralPath $receiptPath -Encoding UTF8}
try {
    $state=InvokeTrial Status
    Check ($state.state -eq 'absent' -and -not (Test-Path -LiteralPath $fixture)) 'Status is read-only, including absent directories'
    New-Item -ItemType Directory -Path $data,$off,$candidate -Force|Out-Null
    $names=@('9ba626afa44a3aa3.patch_6','9ba626afa44a3aa3.patch_6.gpu_resources')
    $originals=@('source-main','source-gpus');$modified=@('trial--main','trial--gpus')
    $files=@(for($i=0;$i -lt 2;$i++){
        [IO.File]::WriteAllText((Join-Path $data $names[$i]),$originals[$i])
        [IO.File]::WriteAllText((Join-Path $candidate $names[$i]),$modified[$i])
        [pscustomobject]@{name=$names[$i];size=11;sha256=(Hash (Join-Path $candidate $names[$i]));sourceSha256=(Hash (Join-Path $data $names[$i]))}
    })
    $report=[pscustomobject]@{schemaVersion=2;packaging='preserved-source-layout';profile='full';variant='shadows';pack='2026.09.12-r8';resources=@(1..8);files=$files}
    $reportPath=Join-Path $candidate 'build-report.json'
    $report|ConvertTo-Json -Depth 12|Set-Content -LiteralPath $reportPath -Encoding UTF8
    $reportText=[IO.File]::ReadAllText($reportPath)
    $untouched=Join-Path $data '9ba626afa44a3aa3.patch_6.stream';[IO.File]::WriteAllText($untouched,'unmodified companion')
    $untouchedHash=Hash $untouched
    # Last-source failures must not cause a first-file replacement or create a receipt.
    [IO.File]::WriteAllText((Join-Path $data $names[1]),'wrong-profl')
    Reject {InvokeTrial Enable} 'Exact source hash rejects wrong texture profile'
    Check ((Hash (Join-Path $data $names[0])) -eq $files[0].sourceSha256 -and -not (Test-Path $receiptPath)) 'Preflight rejects before first replacement/receipt'
    [IO.File]::WriteAllText((Join-Path $data $names[1]),$originals[1])
    [IO.File]::WriteAllText((Join-Path $candidate $names[1]),'wrong-trial')
    Reject {InvokeTrial Enable} 'Tampered candidate rejected'
    [IO.File]::WriteAllText((Join-Path $candidate $names[1]),$modified[1])
    $report.profile='lighter';$report|ConvertTo-Json -Depth 12|Set-Content $reportPath
    Reject {InvokeTrial Enable} 'Report profile mismatch rejected'
    [IO.File]::WriteAllText($reportPath,$reportText)
    $report.files[0].name='../escape';$report|ConvertTo-Json -Depth 12|Set-Content $reportPath
    Reject {InvokeTrial Enable} 'Candidate path traversal rejected'
    [IO.File]::WriteAllText($reportPath,$reportText);$report=$reportText|ConvertFrom-Json;$files=@($report.files)
    '{"state":"enabled"}'|Set-Content $controlPath
    Reject {InvokeTrial Enable} 'Original-dropship overlap rejected'
    '{"state":"disabled"}'|Set-Content $controlPath
    $global:GeometryTrialFixtureRunning=$true
    Reject {InvokeTrial Enable} 'Running launcher/game guard enforced' 'Close game and launcher'
    $global:GeometryTrialFixtureRunning=$false
    New-Item -ItemType Directory -Path (Join-Path $env:APPDATA 'Clonedivers') -Force|Out-Null
    $plan=Join-Path $env:APPDATA 'Clonedivers/apply-plan.json';'{}'|Set-Content $plan
    Reject {InvokeTrial Enable} 'Pending launcher operation rejected'
    Remove-Item -LiteralPath $plan
    Move-Item -LiteralPath (Join-Path $data $names[1]) -Destination (Join-Path $off $names[1])
    Reject {InvokeTrial Enable} 'Fresh Enable requires active sources'
    Move-Item -LiteralPath (Join-Path $off $names[1]) -Destination (Join-Path $data $names[1])
    InvokeTrial Enable|Out-Null;$receipt=ReadReceipt
    Check ($receipt.state -eq 'enabled' -and $receipt.schemaVersion -eq 2) 'Replacement Enable records schema2 receipt'
    foreach($file in $files){
        Check ((Hash (Join-Path $data $file.name)) -eq $file.sha256) "Candidate installed: $($file.name)"
        Check ((Hash (Join-Path $receipt.backupDirectory $file.name)) -eq $file.sourceSha256) "Exact durable backup: $($file.name)"
    }
    Check ((Hash $untouched) -eq $untouchedHash) 'Unlisted stream companion untouched'
    $backup=$receipt.backupDirectory
    InvokeTrial Enable|Out-Null
    Check ((ReadReceipt).backupDirectory -eq $backup) 'Repeated Enable verifies and preserves original backup'
    Reject {& $installer -Action Enable -Profile full -Variant balanced -GameDirectory $game -CandidateRoot $candidateRoot -ReceiptPath $receiptPath -ControlReceiptPath $controlPath} 'Changing active variant fails closed' 'Finish Disable'
    Reject {& $installer -Action Enable -Profile lighter -Variant shadows -GameDirectory $game -CandidateRoot $candidateRoot -ReceiptPath $receiptPath -ControlReceiptPath $controlPath} 'Changing active profile fails closed' 'Finish Disable'
    # Model crash after only one atomic replacement: prepared receipt plus mixed target hashes.
    $receipt.state='prepared';WriteReceipt $receipt
    [IO.File]::WriteAllText((Join-Path $data $names[1]),$originals[1])
    InvokeTrial Enable|Out-Null
    Check ((ReadReceipt).state -eq 'enabled' -and (Hash (Join-Path $data $names[1])) -eq $files[1].sha256) 'Prepared mixed-state Enable resumes safely'
    $receipt=ReadReceipt;$receipt.state='prepared';WriteReceipt $receipt
    [IO.File]::WriteAllText((Join-Path $data $names[1]),$originals[1])
    InvokeTrial Disable|Out-Null
    Check ((ReadReceipt).state -eq 'disabled' -and (Hash (Join-Path $data $names[0])) -eq $files[0].sourceSha256 -and (Hash (Join-Path $data $names[1])) -eq $files[1].sourceSha256) 'Prepared mixed-state Enable can instead roll back'
    InvokeTrial Enable|Out-Null;$receipt=ReadReceipt;$backup=$receipt.backupDirectory
    # Ambiguous/changed targets and broken backup all reject before restoring even the first file.
    Copy-Item -LiteralPath (Join-Path $data $names[1]) -Destination (Join-Path $off $names[1])
    Reject {InvokeTrial Disable} 'Duplicate active/parked target rejected'
    Remove-Item -LiteralPath (Join-Path $off $names[1])
    [IO.File]::WriteAllText((Join-Path $data $names[1]),'wrong-bytes')
    Reject {InvokeTrial Disable} 'Changed target is never overwritten'
    Check ((Hash (Join-Path $data $names[0])) -eq $files[0].sha256) 'Restore preflight leaves first target intact on later failure'
    [IO.File]::WriteAllText((Join-Path $data $names[1]),$modified[1])
    Move-Item -LiteralPath (Join-Path $data $names[1]) -Destination (Join-Path $fixture 'temporarily-missing')
    Reject {InvokeTrial Disable} 'Missing target rejected'
    Move-Item -LiteralPath (Join-Path $fixture 'temporarily-missing') -Destination (Join-Path $data $names[1])
    [IO.File]::WriteAllText((Join-Path $backup $names[1]),'bad--backup')
    Reject {InvokeTrial Disable} 'Changed backup rejected before restoration'
    [IO.File]::WriteAllText((Join-Path $backup $names[1]),$originals[1])
    $receipt=ReadReceipt;$receipt.backupDirectory=Join-Path $fixture 'outside';WriteReceipt $receipt
    Reject {InvokeTrial Disable} 'Receipt backup path confinement enforced'
    $receipt.backupDirectory=$backup;$receipt.gameDirectory=Join-Path $fixture 'other-game';WriteReceipt $receipt
    Reject {InvokeTrial Disable} 'Receipt game identity enforced'
    $receipt.gameDirectory=$game;$receipt.state='disabling';WriteReceipt $receipt
    [IO.File]::WriteAllText((Join-Path $data $names[0]),$originals[0])
    foreach($name in $names){Move-Item -LiteralPath (Join-Path $data $name) -Destination (Join-Path $off $name)}
    # Disable must need only backups, even when candidates/report disappeared.
    Move-Item -LiteralPath $reportPath -Destination (Join-Path $fixture 'report-unavailable.json')
    InvokeTrial Disable|Out-Null
    Check ((ReadReceipt).state -eq 'disabled') 'Interrupted Disable resumes with candidates unavailable'
    foreach($file in $files){Check ((Hash (Join-Path $off $file.name)) -eq $file.sourceSha256) "Parked original restored: $($file.name)"}
    InvokeTrial Disable|Out-Null
    Check ((Hash $untouched) -eq $untouchedHash -and (Test-Path $backup)) 'Repeated Disable preserves originals and backup evidence'
    '{"state":"enabled","variant":"shadows"}'|Set-Content $receiptPath
    Reject {InvokeTrial Disable} 'Legacy overlay receipts refused without deleting files'
    $tokens=$null;$errors=$null
    [Management.Automation.Language.Parser]::ParseFile($installer,[ref]$tokens,[ref]$errors)|Out-Null
    Check ($errors.Count -eq 0) 'Installer parses'
    Write-Output "$checks checks passed. Tiny fixture evidence retained at $fixture"
} finally {$env:APPDATA=$oldAppData;Remove-Variable -Name GeometryTrialFixtureRunning -Scope Global -ErrorAction SilentlyContinue}
