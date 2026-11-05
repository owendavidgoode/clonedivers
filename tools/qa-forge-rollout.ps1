$ErrorActionPreference='Stop'
$root=Join-Path ([IO.Path]::GetTempPath()) ('clonedivers-forge-rollout-fixture-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $root|Out-Null
$validator=Join-Path $PSScriptRoot 'validate-forge-rollout.ps1'
$manifest=Join-Path $root 'fixture.json'
$passed=0
function Check([bool]$value,[string]$name){if(-not $value){throw ('FAIL: '+$name)};$script:passed++;Write-Output ('PASS: '+$name)}
function Reject([scriptblock]$action,[string]$name){$failed=$false;try{& $action|Out-Null}catch{$failed=$true};Check $failed $name}
try{
    $artifacts=foreach($pair in @(@('win-x64','fixture-windows.zip'),@('linux-x64','fixture-linux.tar.gz'))){
        $file=Join-Path $root $pair[1];[IO.File]::WriteAllText($file,'Rollout validator fixture; never executed.')
        [ordered]@{Platform=$pair[0];Name=$pair[1];Bytes=(Get-Item -LiteralPath $file).Length;Sha256=(Get-FileHash -LiteralPath $file).Hash.ToLowerInvariant();UrlProposal=('https://github.com/owendavidgoode/clonedivers/releases/download/forge-preview-2026-11-04/'+$pair[1])}
    }
    $m=[ordered]@{Format=1;Channel='forge-preview-build25480438';GameBuild='25480438';Engine='0.1.41';HellForge='0.3.0-beta';Sdk=15;SandboxRequired=$true;IsolationRequired=$true;LegacyFeedsUnchanged=$true;UpdateMode='manual-only';TagProposal='forge-preview-2026-11-04';Artifacts=@($artifacts);DistributionGates=[ordered]@{AuthorAcquisitionReady=$false;PrivateAdaptationPermissionReady=$false;NoPrivateDataInArchives=$true;ExplicitPublicationApproval=$false};RuntimeAcceptance=[ordered]@{WindowsMissionPersistence=$false;MarkerOffCombat=$false;LinuxRuntimeAndAccount=$false;SeparatePcIntendedSquad=$false}}
    function SaveFixture{$m|ConvertTo-Json -Depth 8|Set-Content -LiteralPath $manifest}
    SaveFixture
    $result=& $validator -Manifest $manifest -ArtifactFolder $root
    Check ($result.ArtifactsVerified-eq 2-and -not $result.Publishable-and -not $result.RuntimeQualified-and -not $result.Published) 'matching review artifacts do not imply publication approval or runtime qualification'
    Reject {& $validator -Manifest $manifest -ArtifactFolder $root -RequirePublishable} 'unapproved/incomplete author onboarding cannot pass the publication gate'
    $m.GameBuild='26251122';SaveFixture
    Reject {& $validator -Manifest $manifest -ArtifactFolder $root} 'a live-game build cannot enter the pinned Forge channel'
    $m.GameBuild='25480438';$m.Artifacts[0].UrlProposal='https://untrusted.invalid/fixture-windows.zip';SaveFixture
    Reject {& $validator -Manifest $manifest -ArtifactFolder $root} 'a foreign release asset URL cannot enter the channel proposal'
    $m.Artifacts[0].UrlProposal='https://github.com/owendavidgoode/clonedivers/releases/download/forge-preview-2026-11-04/fixture-windows.zip';SaveFixture
    [IO.File]::WriteAllText((Join-Path $root $m.Artifacts[0].Name),'X'*[int]$m.Artifacts[0].Bytes)
    Reject {& $validator -Manifest $manifest -ArtifactFolder $root} 'same-size changed artifact bytes fail the checksum gate'
    Write-Output ('ALL '+$passed+' ROLLOUT FIXTURES PASSED; no network/publication/game action')
}finally{
    $resolved=[IO.Path]::GetFullPath($root)
    if(-not $resolved.StartsWith([IO.Path]::TrimEndingDirectorySeparator([IO.Path]::GetFullPath([IO.Path]::GetTempPath()))+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)-or -not [IO.Path]::GetFileName($resolved).StartsWith('clonedivers-forge-rollout-fixture-')){throw 'Fixture cleanup escaped intended root'}
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
