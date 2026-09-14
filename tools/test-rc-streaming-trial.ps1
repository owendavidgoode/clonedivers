# Synthetic fifteen-file fixtures only. Never targets the real game or settings.
$ErrorActionPreference='Stop'
$fixture=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot ('../dist/rc-streaming-trial-tests/'+[guid]::NewGuid().ToString('N'))))
$installer=Join-Path $PSScriptRoot 'set-rc-streaming-trial.ps1'
$game=Join-Path $fixture 'fake-game';$data=Join-Path $game 'data';$off=Join-Path $game 'mods_off';$candidate=Join-Path $fixture 'candidate'
$baselinePath=Join-Path $fixture 'baseline.json';$candidatePath=Join-Path $fixture 'candidate.json';$receiptPath=Join-Path $fixture 'install.json';$blocked=Join-Path $fixture 'geometry.json'
$oldAppData=$env:APPDATA;$env:APPDATA=Join-Path $fixture 'appdata'
$global:RcStreamingFixtureRunning=$false
function Get-Process {param($Name,$ErrorAction) if($global:RcStreamingFixtureRunning){[pscustomobject]@{Name='Clonedivers'}}}
$checks=0
function Check($condition,[string]$label){if(-not $condition){throw "FAIL $label"};$script:checks++;Write-Output "PASS $label"}
function Reject([scriptblock]$operation,[string]$label){$failed=$false;try {& $operation|Out-Null}catch{$failed=$true};Check $failed $label}
function Hash([string]$path){(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()}
function Trial([string]$action){& $installer -Action $action -GameDirectory $game -BaselineManifest $baselinePath -CandidateManifest $candidatePath -CandidateDirectory $candidate -ReceiptPath $receiptPath -BlockedReceiptPaths @($blocked)}
function Receipt {Get-Content -LiteralPath $receiptPath -Raw|ConvertFrom-Json}
function SaveReceipt($value){$value|ConvertTo-Json -Depth 12|Set-Content -LiteralPath $receiptPath -Encoding UTF8}
try{
    $status=Trial Status
    Check ($status.state -eq 'absent' -and -not (Test-Path -LiteralPath $fixture)) 'Status creates no directories or files'
    New-Item -ItemType Directory -Force -Path $data,$off,$candidate|Out-Null
    $names=@(foreach($index in @(254,256,258,260,261)){foreach($suffix in @('','.gpu_resources','.stream')){"9ba626afa44a3aa3.patch_$index$suffix"}})
    $original=@{};$modified=@{};$sourceFiles=@();$destFiles=@()
    foreach($name in $names){
        $original[$name]=if($name.EndsWith('.stream')){''}else{'original-'+$name}
        $modified[$name]='candidate-longer-content-'+$name
        [IO.File]::WriteAllText((Join-Path $data $name),$original[$name]);[IO.File]::WriteAllText((Join-Path $candidate $name),$modified[$name])
        $sourceFiles+=[pscustomobject]@{name=$name;size=(Get-Item (Join-Path $data $name)).Length;sha256=(Hash (Join-Path $data $name));option='commandos'}
        $destFiles+=[pscustomobject]@{name=$name;size=(Get-Item (Join-Path $candidate $name)).Length;sha256=(Hash (Join-Path $candidate $name));modes=@('commandos');textureProfiles=@('lighter')}
    }
    @{format=2;pack=@{version='2026.09.12-r8';files=$sourceFiles}}|ConvertTo-Json -Depth 12|Set-Content $baselinePath
    @{format=3;pack=@{version='2026.09.13-r9';files=$destFiles}}|ConvertTo-Json -Depth 12|Set-Content $candidatePath
    $unrelated=Join-Path $data '9ba626afa44a3aa3.patch_253';[IO.File]::WriteAllText($unrelated,'untouched');$untouchedHash=Hash $unrelated
    $last=$names[-1];[IO.File]::WriteAllText((Join-Path $data $last),'unexpected')
    Reject {Trial Enable} 'Unexpected fifteenth source rejected before any replacement'
    Check ((Hash (Join-Path $data $names[0])) -eq $sourceFiles[0].sha256 -and -not (Test-Path $receiptPath)) 'Source preflight failure preserves first file and creates no receipt'
    [IO.File]::WriteAllText((Join-Path $data $last),$original[$last])
    [IO.File]::WriteAllText((Join-Path $candidate $last),'tampered')
    Reject {Trial Enable} 'Tampered candidate rejected'
    [IO.File]::WriteAllText((Join-Path $candidate $last),$modified[$last])
    '{"state":"enabled"}'|Set-Content $blocked
    Reject {Trial Enable} 'Active geometry trial blocks Enable'
    '{"state":"disabled"}'|Set-Content $blocked
    $global:RcStreamingFixtureRunning=$true;Reject {Trial Enable} 'Running game or launcher blocks mutation';$global:RcStreamingFixtureRunning=$false
    New-Item -ItemType Directory -Force (Join-Path $env:APPDATA 'Clonedivers')|Out-Null
    $pending=Join-Path $env:APPDATA 'Clonedivers/apply-plan.json';'{}'|Set-Content $pending
    Reject {Trial Enable} 'Pending launcher operation blocks mutation';Remove-Item -LiteralPath $pending
    Trial Enable|Out-Null;$receipt=Receipt;$backup=$receipt.backupDirectory
    Check ($receipt.state -eq 'enabled' -and @($receipt.files).Count -eq 15) 'Enable records fixed fifteen-file receipt'
    Check (@($destFiles|Where-Object {(Hash (Join-Path $data $_.name)) -ne $_.sha256}).Count -eq 0) 'All fifteen differing-length candidates installed exactly'
    Check (@($sourceFiles|Where-Object {(Hash (Join-Path $backup $_.name)) -ne $_.sha256}).Count -eq 0) 'Durable backups preserve all originals including zero-byte streams'
    Check ((Hash $unrelated) -eq $untouchedHash) 'Unlisted patch remains byte-identical'
    Trial Enable|Out-Null;Check ((Receipt).backupDirectory -eq $backup) 'Repeated Enable retains original backup'
    $receipt.state='prepared';SaveReceipt $receipt;[IO.File]::WriteAllText((Join-Path $data $last),$original[$last])
    Trial Enable|Out-Null;Check ((Receipt).state -eq 'enabled' -and (Hash (Join-Path $data $last)) -eq $destFiles[-1].sha256) 'Prepared mixed state resumes Enable'
    [IO.File]::WriteAllText((Join-Path $data $last),'changed-after-install')
    Reject {Trial Disable} 'Unexpected target refuses restoration overwrite'
    Check ((Hash (Join-Path $data $names[0])) -eq $destFiles[0].sha256) 'Restore preflight failure leaves earlier files untouched'
    [IO.File]::WriteAllText((Join-Path $data $last),$modified[$last])
    Copy-Item -LiteralPath (Join-Path $data $last) -Destination (Join-Path $off $last)
    Reject {Trial Disable} 'Duplicate active/parked target rejects restoration';Remove-Item -LiteralPath (Join-Path $off $last)
    [IO.File]::WriteAllText((Join-Path $backup $last),'broken-backup')
    Reject {Trial Disable} 'Changed backup blocks restoration'
    [IO.File]::WriteAllText((Join-Path $backup $last),$original[$last])
    $receipt=Receipt;$receipt.backupDirectory=Join-Path $fixture 'outside';SaveReceipt $receipt
    Reject {Trial Disable} 'Receipt backup path confinement enforced'
    $receipt.backupDirectory=$backup;$receipt.state='disabling';SaveReceipt $receipt
    [IO.File]::WriteAllText((Join-Path $data $names[0]),$original[$names[0]])
    foreach($name in $names){Move-Item -LiteralPath (Join-Path $data $name) -Destination (Join-Path $off $name)}
    Remove-Item -LiteralPath $candidatePath
    $partial=Join-Path $off ($names[1]+'.rc-streaming-tmp-fixture');[IO.File]::WriteAllText($partial,'partial')
    Trial Disable|Out-Null
    Check ((Receipt).state -eq 'disabled' -and @($sourceFiles|Where-Object {(Hash (Join-Path $off $_.name)) -ne $_.sha256}).Count -eq 0) 'Interrupted Disable restores all parked originals without candidate manifest'
    Check ((Get-Item (Join-Path $off $last)).Length -eq 0) 'Original empty stream restored to zero length'
    Check ((Hash $unrelated) -eq $untouchedHash -and (Get-Content $partial -Raw) -eq 'partial') 'Unrelated patch and interrupted-copy temporary file remain untouched'
    Trial Disable|Out-Null;Check ((Receipt).state -eq 'disabled') 'Repeated Disable is harmless'
    $tokens=$null;$errors=$null;[Management.Automation.Language.Parser]::ParseFile($installer,[ref]$tokens,[ref]$errors)|Out-Null
    Check ($errors.Count -eq 0) 'Trial harness parses'
    Write-Output "$checks checks passed. Fixture evidence retained at $fixture"
}finally{$env:APPDATA=$oldAppData;Remove-Variable RcStreamingFixtureRunning -Scope Global -ErrorAction SilentlyContinue}
