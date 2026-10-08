# Independent read-only public verification after the sealed release transaction.
param(
    [Parameter(Mandatory=$true)][string]$ReleaseDirectory,
    [Parameter(Mandatory=$true)][string]$OutputDirectory
)
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
. (Join-Path $PSScriptRoot 'release-core.ps1')
. (Join-Path $PSScriptRoot 'gh-common.ps1')
if (Test-Path -LiteralPath $OutputDirectory) { throw 'Use a fresh public verification directory.' }
New-Item -ItemType Directory -Path $OutputDirectory | Out-Null
function Gh-Checked([string[]]$Arguments) {
    $result=Invoke-Gh @Arguments
    if ($result.Code -ne 0) { throw $result.Out }
    $result.Out
}
function Same-Json($Expected,$Actual) {
    ($Expected | ConvertTo-Json -Depth 12 -Compress) -ceq ($Actual | ConvertTo-Json -Depth 12 -Compress)
}
$receipt=Get-Content -LiteralPath (Join-Path $ReleaseDirectory 'release.json') -Raw | ConvertFrom-Json
if ($receipt.phase -ne 'complete') { throw 'The release transaction is not complete.' }
Assert-PreparedRelease $ReleaseDirectory $receipt
$feeds=@(Get-ReleaseFeedTargets $ReleaseDirectory $receipt)
$feedProofs=@()
foreach ($feed in $feeds) {
    $expected=Get-Content -LiteralPath (Join-Path $ReleaseDirectory $feed.prepared) -Raw | ConvertFrom-Json
    $apiText=Gh-Checked @('api',"repos/owendavidgoode/clonedivers/contents/$($feed.target)?ref=main",'-H','Accept: application/vnd.github.raw+json')
    $api=$apiText | ConvertFrom-Json
    if (!(Same-Json $expected $api)) { throw "Public API feed differs: $($feed.target)" }
    Write-Utf8 (Join-Path $OutputDirectory "api-$($feed.target)") $apiText
    $rawUrl="https://raw.githubusercontent.com/owendavidgoode/clonedivers/main/$($feed.target)?t=$([DateTimeOffset]::UtcNow.ToUnixTimeSeconds())"
    $response=Invoke-WebRequest -Uri $rawUrl -Headers @{'Cache-Control'='no-cache';'User-Agent'='Clonedivers-public-verification'} -TimeoutSec 30
    $raw=$response.Content | ConvertFrom-Json
    if (!(Same-Json $expected $raw)) { throw "Public raw feed differs: $($feed.target)" }
    Write-Utf8 (Join-Path $OutputDirectory "raw-$($feed.target)") $response.Content
    $feedProofs+=@{feed=$feed.target;apiEqualsPrepared=$true;rawEqualsPrepared=$true;packVersion=$api.pack.version;appVersion=$api.app.version;rows=@($api.pack.files).Count}
}
$candidate=Get-Content -LiteralPath (Join-Path $ReleaseDirectory 'manifest.json') -Raw | ConvertFrom-Json
$legacy=Get-Content -LiteralPath (Join-Path $ReleaseDirectory 'compatibility-manifest.json') -Raw | ConvertFrom-Json
$all=[pscustomobject]@{app=$candidate.app;pack=[pscustomobject]@{files=@($candidate.pack.files)+@($legacy.pack.files)}}
$assetProofs=@()
$releaseProofs=@()
foreach ($group in ((Get-ReleaseAssets $all) | Group-Object tag)) {
    $release=(Gh-Checked @('release','view',$group.Name,'--repo','owendavidgoode/clonedivers','--json','databaseId,isDraft,isPrerelease,tagName')) | ConvertFrom-Json
    if ($release.isDraft) { throw "Asset release is still draft: $($group.Name)" }
    $hosted=@{}
    foreach ($page in ((Gh-Checked @('api','--paginate','--slurp',"repos/owendavidgoode/clonedivers/releases/$($release.databaseId)/assets?per_page=100")) | ConvertFrom-Json)) {
        foreach ($asset in $page) { $hosted[$asset.name]=$asset }
    }
    foreach ($expected in $group.Group) {
        Assert-HostedReleaseAsset $expected $hosted[$expected.name]
        $assetProofs+=@{tag=$group.Name;name=$expected.name;size=$expected.size;sha256=$expected.sha256;serverDigestVerified=$true;uploaded=$true}
    }
    $releaseProofs+=@{tag=$group.Name;isDraft=$false;assetsVerified=$group.Count}
}
$download=Join-Path $OutputDirectory 'public-Clonedivers.exe'
& curl.exe --fail --location --silent --show-error --output $download $candidate.app.url
if ($LASTEXITCODE -ne 0 -or (Get-Item -LiteralPath $download).Length -ne $candidate.app.size -or (Get-ContentHash $download) -ne $candidate.app.sha256) {
    throw 'Independent public launcher download differs.'
}
Assert-ReadmeRedirect 'owendavidgoode/clonedivers' $candidate.app.version
$proof=[ordered]@{passed=$true;utc=[DateTimeOffset]::UtcNow.ToString('o');version=$candidate.pack.version;appVersion=$candidate.app.version;sourceCommit=$receipt.sourceCommit;feedCommit=$receipt.feedCommit;feeds=$feedProofs;releases=$releaseProofs;hostedAssetCount=$assetProofs.Count;hostedAssets=$assetProofs;publicLauncherDownloadSha256=(Get-ContentHash $download);readmeLatestDownloadVerified=$true;gameOrSettingsChanged=$false;gameplayAccepted=$false}
Write-ContractJson (Join-Path $OutputDirectory 'verification.json') $proof
"PASS public $($candidate.pack.version): three feeds, $($assetProofs.Count) hosted asset identities and launcher download verified."
