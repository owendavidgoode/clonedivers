$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'release-core.ps1')
$root=Join-Path (Split-Path $PSScriptRoot) ('dist/release-workflow-tests/' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force $root | Out-Null
[IO.File]::WriteAllText((Join-Path $root 'manifest.json'),'prepared')
$receipt=[ordered]@{format=1;phase='prepared';sealedFiles=@(@{path='manifest.json';sha256=(Get-ContentHash (Join-Path $root 'manifest.json'))})}
Write-ContractJson (Join-Path $root 'release.json') $receipt
$script:feedCalls=0; $script:artifactCalls=0
$feed={param($r) $script:feedCalls++}
$fail={param($r) $script:artifactCalls++; throw 'upload interrupted'}
try { Invoke-VerifiedRelease $root $fail $feed } catch { if ($_.Exception.Message -ne 'upload interrupted') {throw} }
if ($script:feedCalls -ne 0) {throw 'Feed changed after failed upload'}
'PASS failed upload cannot publish feed'
$verified={param($r) $script:artifactCalls++; if ($r.phase -ne 'verifying-artifacts') {throw 'No verification phase'} }
Invoke-VerifiedRelease $root $verified $feed
if ($script:feedCalls -ne 1 -or $script:artifactCalls -ne 2) {throw 'Resume skipped verification or repeated feed'}
'PASS resume verifies artifacts again before publishing'
Invoke-VerifiedRelease $root $verified $feed
if ($script:feedCalls -ne 1) {throw 'Completed release republished'}
'PASS completed release is idempotent'
[IO.File]::WriteAllText((Join-Path $root 'manifest.json'),'changed!')
$rejected=$false
try { Invoke-VerifiedRelease $root $verified $feed } catch {$rejected=$true}
if (!$rejected -or $script:feedCalls -ne 1) {throw 'Modified prepared release was accepted'}
'PASS changed staged input is rejected before any external operation'
$expected=[pscustomobject]@{name='abc';size=12;sha256=('a'*64)}
$bad=[pscustomobject]@{name='abc';size=12;digest=('sha256:' + ('b'*64));state='uploaded'}
$rejected=$false
try { Assert-HostedReleaseAsset $expected $bad } catch {$rejected=$true}
if (!$rejected) {throw 'Wrong digest accepted'}
'PASS right size with wrong hosted digest is rejected'
$bad.digest='sha256:' + ('a'*64)
Assert-HostedReleaseAsset $expected $bad
'PASS matching hosted digest is accepted'

# A feed push failure must retain its commit and still reverify artifacts on retry.
[IO.File]::WriteAllText((Join-Path $root 'manifest.json'),'prepared')
$receipt.phase='prepared'; $receipt.feedCommit=''
Write-ContractJson (Join-Path $root 'release.json') $receipt
$script:feedCalls=0; $script:artifactCalls=0
$interruptedFeed={param($r)
    $script:feedCalls++
    $r.feedCommit='prepared-feed-commit'
    Write-ContractJson (Join-Path $root 'release.json') $r
    throw 'push interrupted'
}
try { Invoke-VerifiedRelease $root $verified $interruptedFeed } catch {if ($_.Exception.Message -ne 'push interrupted') {throw}}
$resumedFeed={param($r)
    if ($r.feedCommit -ne 'prepared-feed-commit') {throw 'Lost feed commit'}
    $script:feedCalls++
}
Invoke-VerifiedRelease $root $verified $resumedFeed
if ($script:feedCalls -ne 2 -or $script:artifactCalls -ne 2) {throw 'Feed recovery did not reverify'}
'PASS feed failure resumes with the same commit after fresh artifact verification'
