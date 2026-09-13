$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'release-core.ps1')
$workspace=Join-Path (Split-Path $PSScriptRoot) ('dist/profile-release-tests/'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force $workspace | Out-Null
$script:checks=0
function Check($ok,[string]$name) { if (!$ok) {throw $name};$script:checks++;Write-Output "PASS $name" }
function Rejected([scriptblock]$Call) { try { & $Call | Out-Null;return $false } catch {return $true} }
function Seal([string]$dir) {
    $receipt=[pscustomobject][ordered]@{format=2;phase='prepared';sourceCommit='source';feedCommit='';sealedFiles=@(Get-ChildItem -LiteralPath $dir -File | Where-Object Name -ne 'release.json' | ForEach-Object { [pscustomobject]@{path=$_.Name;sha256=(Get-ContentHash $_.FullName)} })}
    Write-ContractJson (Join-Path $dir 'release.json') $receipt
    $receipt
}
function Fixture([string]$name) {
    $dir=Join-Path $workspace $name;$live=Join-Path $workspace ($name+'-live')
    New-Item -ItemType Directory -Force $dir,$live | Out-Null
    $legacy=[pscustomobject]@{format=2;app=[pscustomobject]@{version='1.4.1'};pack=[pscustomobject]@{version='2026.09.12-r8';notes='Keep legacy';files=@([pscustomobject]@{name='original';size=12;sha256=('a'*64)})};extra='preserved'}
    $candidate=[pscustomobject]@{format=3;app=[pscustomobject]@{version='1.5.0'};pack=[pscustomobject]@{version='2026.09.13-r9';files=@();textureProfiles=@('full','lighter','reduced')}}
    Write-ContractJson (Join-Path $dir 'manifest.json') $candidate
    Write-ContractJson (Join-Path $dir 'baseline-compatibility.json') $legacy
    Write-ContractJson (Join-Path $dir 'compatibility-manifest.json') (New-CompatibilityManifest $legacy $candidate)
    Copy-Item -LiteralPath (Join-Path $dir 'baseline-compatibility.json') -Destination (Join-Path $live 'manifest.json')
    Write-ContractJson (Join-Path $dir 'feed-plan.json') ([ordered]@{format=1;feeds=@(
        [ordered]@{target='manifest.json';prepared='compatibility-manifest.json';baselineExists=$true;baselineSha256=(Get-ContentHash (Join-Path $live 'manifest.json'))},
        [ordered]@{target='manifest-v3.json';prepared='manifest.json';baselineExists=$false;baselineSha256=$null})})
    $receipt=Seal $dir
    [pscustomobject]@{dir=$dir;live=$live;receipt=$receipt;legacy=$legacy;candidate=$candidate}
}
$f=Fixture 'valid';$feeds=@(Get-ReleaseFeedTargets $f.dir $f.receipt)
Assert-PreparedRelease $f.dir $f.receipt
Check ($feeds.Count -eq 2 -and ($feeds | Where-Object target -eq 'manifest.json').prepared -eq 'compatibility-manifest.json') 'Format3 maps each feed to its fixed prepared file'
$compat=Get-Content (Join-Path $f.dir 'compatibility-manifest.json') -Raw | ConvertFrom-Json
Check (($compat.pack | ConvertTo-Json -Depth 12 -Compress) -ceq ($f.legacy.pack | ConvertTo-Json -Depth 12 -Compress)) 'Legacy pack remains identical r8'
Check ($compat.app.version -eq '1.5.0' -and $compat.format -eq 2 -and $compat.extra -eq 'preserved') 'Only compatibility app metadata changes'
Assert-ReleaseFeedBaseline $f.live $f.dir $feeds
Copy-Item -LiteralPath (Join-Path $f.dir 'compatibility-manifest.json') -Destination (Join-Path $f.live 'manifest.json')
Assert-ReleaseFeedBaseline $f.live $f.dir $feeds
Check $true 'Partial prepared feed copy can resume while second feed remains absent'
[IO.File]::WriteAllText((Join-Path $f.live 'manifest-v3.json'),'unrelated edit')
Check (Rejected {Assert-ReleaseFeedBaseline $f.live $f.dir $feeds}) 'Unrelated local feed cannot be overwritten'
$f=Fixture 'mapping';$plan=Get-Content (Join-Path $f.dir 'feed-plan.json') -Raw | ConvertFrom-Json
$plan.feeds[1].target='../other.json';Write-ContractJson (Join-Path $f.dir 'feed-plan.json') $plan;$f.receipt=Seal $f.dir
Check (Rejected {Assert-PreparedRelease $f.dir $f.receipt}) 'Even resealed traversal target mapping rejected'
$f=Fixture 'compat-tamper';$compat=Get-Content (Join-Path $f.dir 'compatibility-manifest.json') -Raw | ConvertFrom-Json
$compat.pack.version='changed';Write-ContractJson (Join-Path $f.dir 'compatibility-manifest.json') $compat;$f.receipt=Seal $f.dir
Check (Rejected {Assert-PreparedRelease $f.dir $f.receipt}) 'Even resealed legacy-pack mutation rejected'
$f=Fixture 'resume';$script:ensures=0;$script:publishes=0
$ensure={param($r) $script:ensures++}
$interrupted={param($r) $script:publishes++;$r.feedCommit='both-feed-commit';Write-ContractJson (Join-Path $f.dir 'release.json') $r;throw 'push interrupted'}
Check (Rejected {Invoke-VerifiedRelease $f.dir $ensure $interrupted}) 'Interrupted feed publication remains incomplete'
$resume={param($r)
    if ($r.feedCommit -ne 'both-feed-commit') {throw 'Lost coordinated commit'}
    $targets=@(Get-ReleaseFeedTargets $f.dir $r)
    Assert-ReleaseFeedBaseline $f.live $f.dir $targets
    foreach ($target in $targets) {Copy-Item -LiteralPath (Join-Path $f.dir $target.prepared) -Destination (Join-Path $f.live $target.target)}
    foreach ($target in $targets) {if ((Get-ContentHash (Join-Path $f.live $target.target)) -ne (Get-ContentHash (Join-Path $f.dir $target.prepared))) {throw 'Live feed mismatch'}}
    $script:publishes++
}
Invoke-VerifiedRelease $f.dir $ensure $resume
Check ($script:ensures -eq 2 -and $script:publishes -eq 2) 'Resume retains one coordinated commit and reverifies artifacts before both feeds'
Invoke-VerifiedRelease $f.dir $ensure $resume
Check ($script:ensures -eq 2 -and $script:publishes -eq 2) 'Completed coordinated release is idempotent'
[IO.File]::AppendAllText((Join-Path $f.dir 'compatibility-manifest.json'),' ')
Check (Rejected {Invoke-VerifiedRelease $f.dir $ensure $resume}) 'Compatibility tamper rejected even on completed resume'
$f=Fixture 'during-upload';$script:publishes=0
$mutatingEnsure={param($r) [IO.File]::AppendAllText((Join-Path $f.dir 'manifest.json'),' ')}
Check (Rejected {Invoke-VerifiedRelease $f.dir $mutatingEnsure {param($r) $script:publishes++}}) 'Changed candidate during upload rejected before feed publication'
Check ($script:publishes -eq 0) 'No feed adapter called after mid-upload tamper'
$f=Fixture 'legacy';$legacyReceipt=[pscustomobject]@{format=1;baselineManifestSha256=('a'*64)}
Check (Rejected {Get-ReleaseFeedTargets $f.dir $legacyReceipt}) 'Downgraded receipt cannot route format3 into legacy feed'
Write-ContractJson (Join-Path $f.dir 'manifest.json') $f.legacy
$legacyFeeds=@(Get-ReleaseFeedTargets $f.dir $legacyReceipt)
Check ($legacyFeeds.Count -eq 1 -and $legacyFeeds[0].target -eq 'manifest.json' -and $legacyFeeds[0].prepared -eq 'manifest.json') 'Legacy receipt keeps single-feed mapping'
$tokens=$null;$errors=$null
[Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot 'release.ps1'),[ref]$tokens,[ref]$errors) | Out-Null
Check ($errors.Count -eq 0) 'Release entrypoint parses without invoking network or Stage'
Write-Output "$script:checks profile release checks passed. Fixtures: $workspace"
