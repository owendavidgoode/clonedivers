# Independent read-only r23 API/raw feeds, actual new asset downloads and ancestry.
param(
    [string]$ReleaseDirectory = 'dist/production-r23-2026-10-08/release-ready-v1',
    [string]$OutputDirectory = 'dist/production-r23-2026-10-08/qa/remote-peer-v1'
)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
. (Join-Path $PSScriptRoot 'release-core.ps1')
. (Join-Path $PSScriptRoot 'gh-common.ps1')
$releaseRoot = [IO.Path]::GetFullPath((Join-Path $root $ReleaseDirectory))
$outputRoot = [IO.Path]::GetFullPath((Join-Path $root $OutputDirectory))
$allowedOutput = [IO.Path]::GetFullPath((Join-Path $root 'dist/production-r23-2026-10-08/qa')) + [IO.Path]::DirectorySeparatorChar
if (!$outputRoot.StartsWith($allowedOutput, [StringComparison]::OrdinalIgnoreCase) -or (Test-Path -LiteralPath $outputRoot)) { throw 'Fresh scoped r23 QA output required.' }
$receiptPath = Join-Path $releaseRoot 'release.json'
$receipt = Get-Content -LiteralPath $receiptPath -Raw | ConvertFrom-Json
if ($receipt.phase -ne 'complete') { throw 'Publication must complete before independent readback.' }
Assert-PreparedRelease $releaseRoot $receipt
function Gh-Read([string[]]$Arguments) {
    $result = Invoke-Gh @Arguments
    if ($result.Code -ne 0) { throw $result.Out }
    $result.Out
}
function File-Pin([string]$Path) {
    @{path=[IO.Path]::GetFullPath($Path);bytes=(Get-Item -LiteralPath $Path).Length;sha256=(Get-ContentHash $Path)}
}
function Same-Text([string]$Left,[string]$Right) {
    $a=[IO.File]::ReadAllText($Left).Replace("`r`n","`n").TrimEnd([char[]]"`r`n")
    $b=[IO.File]::ReadAllText($Right).Replace("`r`n","`n").TrimEnd([char[]]"`r`n")
    $a -ceq $b
}
$candidatePath = Join-Path $releaseRoot 'manifest.json'
$candidate = Get-Content -LiteralPath $candidatePath -Raw | ConvertFrom-Json
if ($candidate.pack.version -ne '2026.10.08-r23' -or @($candidate.pack.files).Count -ne 1809 -or $candidate.app.sha256 -ne '2effe777eb2673d7237b5fc9aed1cbe85434ba86f3c31fbae9f73129a06cd51e') { throw 'Unexpected sealed r23 candidate.' }
New-Item -ItemType Directory -Path $outputRoot | Out-Null
$feeds = @()
foreach ($feed in @(Get-ReleaseFeedTargets $releaseRoot $receipt)) {
    $api = Gh-Read @('api', "repos/owendavidgoode/clonedivers/contents/$($feed.target)?ref=main", '-H', 'Accept: application/vnd.github.raw+json')
    $apiPath = Join-Path $outputRoot ('api-' + $feed.target)
    Write-Utf8 $apiPath $api
    $rawPath = Join-Path $outputRoot ('raw-' + $feed.target)
    $rawUrl = "https://raw.githubusercontent.com/owendavidgoode/clonedivers/main/$($feed.target)?t=$([DateTimeOffset]::UtcNow.ToUnixTimeSeconds())"
    Invoke-WebRequest -Uri $rawUrl -Headers @{'Cache-Control'='no-cache';'User-Agent'='Clonedivers-public-verification'} -TimeoutSec 30 -OutFile $rawPath | Out-Null
    $preparedPath = Join-Path $releaseRoot $feed.prepared
    if (!(Same-Text $apiPath $preparedPath) -or !(Same-Text $rawPath $preparedPath)) { throw "Public feed differs from sealed preparation: $($feed.target)" }
    $feeds += @{target=$feed.target;prepared=(File-Pin $preparedPath);api=(File-Pin $apiPath);raw=(File-Pin $rawPath);apiTextEqualsPrepared=$true;rawTextEqualsPrepared=$true;rawBytesEqualPrepared=((Get-ContentHash $rawPath) -ceq (Get-ContentHash $preparedPath));textComparisonNormalization='CRLF to LF; trailing CR/LF only';rawTransport='System.Net Invoke-WebRequest'}
}
if ($feeds.Count -ne 3) { throw 'Expected all three coordinated public feeds.' }
$release = (Gh-Read @('release','view','pack-2026.10.08-r23-files','--repo','owendavidgoode/clonedivers','--json','databaseId,isDraft,isPrerelease,tagName')) | ConvertFrom-Json
if ($release.isDraft -or $release.tagName -ne 'pack-2026.10.08-r23-files') { throw 'The public pack asset release is unavailable.' }
$hosted = @{}
foreach ($asset in ((Gh-Read @('api', "repos/owendavidgoode/clonedivers/releases/$($release.databaseId)/assets?per_page=100")) | ConvertFrom-Json)) { $hosted[$asset.name] = $asset }
$expectedNew = @(Get-ReleaseAssets $candidate | Where-Object tag -CEQ 'pack-2026.10.08-r23-files')
if ($expectedNew.Count -ne 3 -or ($expectedNew | Measure-Object size -Sum).Sum -ne 4559088) { throw 'Expected exactly three bounded new assets totaling4559088B.' }
$assets = @()
foreach ($expected in $expectedNew) {
    $asset = $hosted[$expected.name]
    Assert-HostedReleaseAsset $expected $asset
    $path = Join-Path $outputRoot $expected.name
    & curl.exe --fail --location --silent --show-error --retry 3 --retry-delay 1 --output $path $expected.url
    if ($LASTEXITCODE -ne 0) { throw "Independent actual public asset download failed: $($expected.name)" }
    $download = File-Pin $path
    if ($download.sha256 -cne $expected.sha256 -or $download.bytes -ne $expected.size) { throw 'Actual public download differs from sealed asset.' }
    $assets += @{name=$asset.name;size=$asset.size;state=$asset.state;serverDigest=$asset.digest;download=$download;actualPublicUrl=$expected.url;equalsPrepared=$true}
}
$main = (Gh-Read @('api', 'repos/owendavidgoode/clonedivers/git/ref/heads/main')) | ConvertFrom-Json
$source = (Gh-Read @('api', "repos/owendavidgoode/clonedivers/commits/$($receipt.sourceCommit)")) | ConvertFrom-Json
$sourceToFeed = (Gh-Read @('api', "repos/owendavidgoode/clonedivers/compare/$($receipt.sourceCommit)...$($receipt.feedCommit)")) | ConvertFrom-Json
$feedToHead = (Gh-Read @('api', "repos/owendavidgoode/clonedivers/compare/$($receipt.feedCommit)...$($main.object.sha)")) | ConvertFrom-Json
if ($source.sha -cne $receipt.sourceCommit -or $sourceToFeed.status -notin @('identical','ahead') -or $sourceToFeed.behind_by -ne 0 -or $feedToHead.status -notin @('identical','ahead') -or $feedToHead.behind_by -ne 0) { throw 'Public source -> feed -> main ancestry differs.' }
$latest = (Gh-Read @('api', 'repos/owendavidgoode/clonedivers/releases/latest')) | ConvertFrom-Json
if ($latest.tag_name -ne 'v1.7.3' -or $latest.draft) { throw 'Latest launcher no longer public1.7.3.' }
$proof = [ordered]@{
    passed=$true;utc=[DateTimeOffset]::UtcNow.ToString('o');tool=(File-Pin $PSCommandPath);releaseReceipt=(File-Pin $receiptPath)
    sourceCommit=$receipt.sourceCommit;feedCommit=$receipt.feedCommit;mainCommit=$main.object.sha
    sourceToFeed=@{status=$sourceToFeed.status;aheadBy=$sourceToFeed.ahead_by;behindBy=$sourceToFeed.behind_by;mergeBase=$sourceToFeed.merge_base_commit.sha}
    feedToHead=@{status=$feedToHead.status;aheadBy=$feedToHead.ahead_by;behindBy=$feedToHead.behind_by;mergeBase=$feedToHead.merge_base_commit.sha}
    feeds=$feeds;assets=$assets;release=$release;latestLauncher=@{tag=$latest.tag_name;draft=$latest.draft;id=$latest.id}
    independentNetworkReadback=$true;wholeLegacyAssetInventoryRepeated=$false;publicOrInstalledWrites=$false;gameplayAccepted=$false
}
Write-ContractJson (Join-Path $outputRoot 'report.json') $proof
'PASS: independently downloaded all three new r23 assets; all three API/raw feeds match sealed preparation; source/feed/main ancestry and launcher1.7.3 confirmed.'
