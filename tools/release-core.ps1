. (Join-Path $PSScriptRoot 'deployment-contract.ps1')
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
    foreach ($f in $receipt.sealedFiles) {
        if ((Get-ContentHash (Join-Path $Directory $f.path)) -ne $f.sha256) { throw "Prepared release changed: $($f.path)" }
    }
    if ($receipt.phase -eq 'complete') { 'Release already completed.'; return }
    # Always reverify artifacts on resume; a state label is not evidence of hosted bytes.
    $receipt.phase='verifying-artifacts'; Write-ContractJson $receiptPath $receipt
    & $EnsureArtifacts $receipt
    $receipt.phase='artifacts-verified'; Write-ContractJson $receiptPath $receipt
    & $PublishFeed $receipt
    $receipt.phase='complete'; Write-ContractJson $receiptPath $receipt
}
