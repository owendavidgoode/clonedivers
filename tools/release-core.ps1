. (Join-Path $PSScriptRoot 'deployment-contract.ps1')
function Get-RemoteMainCommit([string]$Output) {
    if ($Output.Trim() -notmatch '\A([a-f0-9]{40})\s+refs/heads/main\z') {
        throw 'Remote main did not resolve to one commit.'
    }
    $Matches[1]
}
function New-CompatibilityManifest($Legacy,$Candidate) {
    if ([int]$Candidate.format -ne 3) { throw 'Compatibility feed is only used for format 3.' }
    if ([int]$Legacy.format -ne 2 -or $Legacy.pack.version -ne '2026.09.12-r8') { throw 'The compatibility feed must retain the published format-2 r8 pack.' }
    $copy=$Legacy | ConvertTo-Json -Depth 12 | ConvertFrom-Json
    $copy | Add-Member -Force -NotePropertyName app -NotePropertyValue $Candidate.app
    $copy
}
function Get-ReleaseFeedTargets([string]$Directory,$Receipt) {
    if ([int]$Receipt.format -eq 1) {
        $legacyCandidate=Get-Content -LiteralPath (Join-Path $Directory 'manifest.json') -Raw | ConvertFrom-Json
        if ([int]$legacyCandidate.format -eq 3) { throw 'A profile manifest cannot be published through the legacy feed mapping.' }
        return @([pscustomobject]@{target='manifest.json';prepared='manifest.json';baselineSha256=$Receipt.baselineManifestSha256;baselineExists=$true})
    }
    if ([int]$Receipt.format -ne 2) { throw 'Unsupported release receipt.' }
    $plan=Get-Content -LiteralPath (Join-Path $Directory 'feed-plan.json') -Raw | ConvertFrom-Json
    $candidate=Get-Content -LiteralPath (Join-Path $Directory 'manifest.json') -Raw | ConvertFrom-Json
    $expected=@{'manifest-v3.json'='manifest.json';'manifest.json'='compatibility-manifest.json'}
    if ([int]$candidate.format -ne 3 -or [int]$plan.format -ne 1 -or @($plan.feeds).Count -ne 2) { throw 'Invalid coordinated feed plan.' }
    $seen=@{}
    foreach ($feed in $plan.feeds) {
        if (!$expected.ContainsKey([string]$feed.target) -or $seen.ContainsKey([string]$feed.target) -or $expected[$feed.target] -cne $feed.prepared) { throw 'Release feed target mapping is not allowed.' }
        if ($feed.baselineExists -isnot [bool] -or ($feed.baselineExists -and $feed.baselineSha256 -notmatch '^[a-f0-9]{64}$') -or (!$feed.baselineExists -and $feed.baselineSha256)) { throw 'Invalid feed baseline identity.' }
        $seen[$feed.target]=$true
        foreach ($required in @('feed-plan.json','baseline-compatibility.json',$feed.prepared)) {
            if (@($Receipt.sealedFiles | Where-Object path -CEQ $required).Count -ne 1) { throw "Unsealed feed input: $required" }
        }
    }
    $legacy=Get-Content -LiteralPath (Join-Path $Directory 'baseline-compatibility.json') -Raw | ConvertFrom-Json
    $compatibility=Get-Content -LiteralPath (Join-Path $Directory 'compatibility-manifest.json') -Raw | ConvertFrom-Json
    $expectedCompatibility=New-CompatibilityManifest $legacy $candidate
    if (($compatibility | ConvertTo-Json -Depth 12 -Compress) -cne ($expectedCompatibility | ConvertTo-Json -Depth 12 -Compress)) { throw 'Compatibility feed changed fields other than app metadata.' }
    $legacyFeed=$plan.feeds | Where-Object target -CEQ 'manifest.json'
    if (!$legacyFeed.baselineExists -or $legacyFeed.baselineSha256 -ne (Get-ContentHash (Join-Path $Directory 'baseline-compatibility.json'))) { throw 'Legacy feed baseline is not the preserved source.' }
    @($plan.feeds)
}
function Assert-PreparedRelease([string]$Directory,$Receipt) {
    if ([int]$Receipt.format -notin @(1,2)) { throw 'Unsupported release receipt.' }
    $seen=@{}
    foreach ($file in $Receipt.sealedFiles) {
        # Prepared identities never grant permission to access arbitrary paths.
        if ($file.path -notmatch '^(manifest\.json|compatibility-manifest\.json|baseline-compatibility\.json|feed-plan\.json|Clonedivers\.exe|notes\.md|assets[\\/][a-f0-9]{64})$' -or $seen.ContainsKey([string]$file.path)) { throw 'Invalid sealed release path.' }
        $seen[$file.path]=$true
        if ($file.sha256 -notmatch '^[a-f0-9]{64}$' -or (Get-ContentHash (Join-Path $Directory $file.path)) -ne $file.sha256) { throw "Prepared release changed: $($file.path)" }
    }
    if (!$seen.ContainsKey('manifest.json')) { throw 'Prepared release has no sealed candidate manifest.' }
    if ([int]$Receipt.format -eq 2) { Get-ReleaseFeedTargets $Directory $Receipt | Out-Null }
}
function Assert-ReleaseFeedBaseline([string]$Root,[string]$Directory,$Feeds) {
    foreach ($feed in $Feeds) {
        $path=Join-Path $Root $feed.target
        $exists=Test-Path -LiteralPath $path -PathType Leaf
        $hash=if ($exists) {Get-ContentHash $path} else {$null}
        $prepared=Get-ContentHash (Join-Path $Directory $feed.prepared)
        if (($exists -and $hash -eq $prepared) -or ($exists -eq $feed.baselineExists -and (!$exists -or $hash -eq $feed.baselineSha256))) { continue }
        throw "Local feed changed after preparation: $($feed.target)"
    }
}
function Get-ReleaseAssets($Manifest) {
    $assets=@{}
    foreach ($f in @($Manifest.pack.files) + @($Manifest.app)) {
        if (!$f -or $f.size -eq 0) { continue }
        if ($f.sha256 -notmatch '^[a-f0-9]{64}$' -or $f.size -lt 1) { throw 'Invalid release asset identity' }
        if ($f.url -notmatch '^https://github\.com/owendavidgoode/clonedivers/releases/download/([^/]+)/([^/]+)$') { throw 'Release URL outside the configured repository' }
        $tag=$Matches[1]; $name=$Matches[2]
        if ($name -eq 'Clonedivers.exe') {
            if ($tag -ne "v$($Manifest.app.version)") { throw 'App version/tag mismatch' }
        } elseif ($name -ne $f.sha256 -or $tag -notmatch '^pack-.+-files(?:-\d+)?$') { throw 'Pack asset is not content addressed' }
        $entry=[pscustomobject]@{tag=$tag;name=$name;size=$f.size;sha256=$f.sha256;url=$f.url}
        if ($assets.ContainsKey($f.url) -and ($assets[$f.url].sha256 -ne $f.sha256 -or $assets[$f.url].size -ne $f.size)) { throw 'Conflicting release asset identities' }
        $assets[$f.url]=$entry
    }
    @($assets.Values | Sort-Object tag,name)
}
function Assert-HostedReleaseAsset($Expected,$Actual) {
    if (!$Actual -or $Actual.name -ne $Expected.name -or $Actual.size -ne $Expected.size -or
        $Actual.digest -ne "sha256:$($Expected.sha256)" -or $Actual.state -ne 'uploaded') {
        throw "Hosted asset failed verification: $($Expected.name)"
    }
}
function Invoke-VerifiedRelease([string]$Directory,[scriptblock]$EnsureArtifacts,[scriptblock]$PublishFeed) {
    $receiptPath=Join-Path $Directory 'release.json'
    $receipt=Get-Content -LiteralPath $receiptPath -Raw | ConvertFrom-Json
    Assert-PreparedRelease $Directory $receipt
    if ($receipt.phase -eq 'complete') { 'Release already completed.'; return }
    # Always reverify artifacts on resume; a state label is not evidence of hosted bytes.
    $receipt.phase='verifying-artifacts'; Write-ContractJson $receiptPath $receipt
    & $EnsureArtifacts $receipt
    # Uploads may take minutes; reject any intervening change before touching either feed.
    Assert-PreparedRelease $Directory $receipt
    $receipt.phase='artifacts-verified'; Write-ContractJson $receiptPath $receipt
    & $PublishFeed $receipt
    $receipt.phase='complete'; Write-ContractJson $receiptPath $receipt
}
