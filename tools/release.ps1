# Stage is offline and the default. Publish is an explicit future operation, never implicit.
param(
    [ValidateSet('Stage','Publish')][string]$Action='Stage',
    [Parameter(Mandatory=$true)][string]$Directory,
    [string]$CandidateManifest='', [string]$TestedManifest='', [string]$BaselineManifest='',
    [string]$GameDir='', [string]$AppExe='', [string]$AssetDirectory='', [string]$NotesPath='', [string]$Dotnet='',
    [string]$ProfileDirectories=''
)
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$Directory=[IO.Path]::GetFullPath($Directory)
. (Join-Path $PSScriptRoot 'release-core.ps1')
function Git-Result([string[]]$Arguments) {
    $out=& git -C $root @Arguments
    if ($LASTEXITCODE -ne 0) { throw "git failed: $($Arguments -join ' ')" }
    ($out | Out-String).Trim()
}
if ($Action -eq 'Stage') {
    foreach ($required in @($CandidateManifest,$BaselineManifest,$AppExe,$NotesPath)) { if (!$required) { throw 'Stage requires candidate and baseline manifests, executable and notes.' } }
    if (Test-Path -LiteralPath $Directory) { throw 'Use a new staging directory; prepared releases are immutable.' }
    if (!$Dotnet) { $Dotnet=Join-Path $root 'dist/dotnet-sdk/dotnet.exe' }
    $manifest=Get-Content -LiteralPath $CandidateManifest -Raw | ConvertFrom-Json
    $profiles=[int]$manifest.format -eq 3
    if ($profiles -and !$ProfileDirectories) { throw 'Format-3 Stage requires -ProfileDirectories with id/directory records.' }
    if (!$profiles -and (!$TestedManifest -or !$GameDir)) { throw 'Legacy Stage requires -TestedManifest and -GameDir.' }
    $version=(Get-Item -LiteralPath $AppExe).VersionInfo.ProductVersion
    if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'Executable has no release version' }
    $manifest | Add-Member -Force -NotePropertyName app -NotePropertyValue ([pscustomobject]@{version=$version;url="https://github.com/owendavidgoode/clonedivers/releases/download/v$version/Clonedivers.exe";size=(Get-Item $AppExe).Length;sha256=(Get-ContentHash $AppExe)})
    $assets=Get-ReleaseAssets $manifest
    & $Dotnet run --project (Join-Path $root 'Clonedivers.Tests/Clonedivers.Tests.csproj')
    if ($LASTEXITCODE -ne 0) { throw 'Native tests failed' }
    if ($profiles) {
        & $Dotnet run --project (Join-Path $root 'tools/Profile.Release.Check/Profile.Release.Check.csproj') -- $CandidateManifest $BaselineManifest $ProfileDirectories
    } else {
        & $Dotnet run --project (Join-Path $root 'tools/Release.Check/Release.Check.csproj') -- $CandidateManifest $TestedManifest $BaselineManifest $GameDir
    }
    if ($LASTEXITCODE -ne 0) { throw 'Release mode validation failed' }
    $known=@{}; $old=Get-Content (Join-Path $root 'manifest.json') -Raw | ConvertFrom-Json
    $compatibility=if ($profiles) { New-CompatibilityManifest $old $manifest } else { $null }
    foreach ($a in (Get-ReleaseAssets $old)) { $known[$a.url]=$a.sha256 }
    if ($profiles -and (Test-Path -LiteralPath (Join-Path $root 'manifest-v3.json'))) {
        $previousProfile=Get-Content -LiteralPath (Join-Path $root 'manifest-v3.json') -Raw | ConvertFrom-Json
        foreach ($a in (Get-ReleaseAssets $previousProfile)) { $known[$a.url]=$a.sha256 }
    }
    $copies=@()
    foreach ($a in $assets) {
        if ($known.ContainsKey($a.url)) { if ($known[$a.url] -ne $a.sha256) { throw 'Existing release URL would change content' }; continue }
        if ($a.name -eq 'Clonedivers.exe') { continue }
        if (!$AssetDirectory) { throw 'New pack assets require -AssetDirectory with hash-named files.' }
        $path=Join-Path $AssetDirectory $a.sha256
        if ((Get-Item $path).Length -ne $a.size -or (Get-ContentHash $path) -ne $a.sha256) { throw 'New asset is missing or invalid' }
        $copies += [pscustomobject]@{source=$path;name=$a.sha256}
    }
    New-Item -ItemType Directory -Force (Join-Path $Directory 'assets') | Out-Null
    Copy-Item -LiteralPath $AppExe -Destination (Join-Path $Directory 'Clonedivers.exe')
    Copy-Item -LiteralPath $NotesPath -Destination (Join-Path $Directory 'notes.md')
    foreach ($copy in $copies) { Copy-Item -LiteralPath $copy.source -Destination (Join-Path $Directory "assets/$($copy.name)") }
    Write-ContractJson (Join-Path $Directory 'manifest.json') $manifest
    if ($profiles) {
        Copy-Item -LiteralPath (Join-Path $root 'manifest.json') -Destination (Join-Path $Directory 'baseline-compatibility.json')
        Write-ContractJson (Join-Path $Directory 'compatibility-manifest.json') $compatibility
        $profilePath=Join-Path $root 'manifest-v3.json';$profileExists=Test-Path -LiteralPath $profilePath -PathType Leaf
        $profileHash=if ($profileExists) {Get-ContentHash $profilePath} else {$null}
        Write-ContractJson (Join-Path $Directory 'feed-plan.json') ([ordered]@{format=1;feeds=@(
            [ordered]@{target='manifest.json';prepared='compatibility-manifest.json';baselineExists=$true;baselineSha256=(Get-ContentHash (Join-Path $root 'manifest.json'))},
            [ordered]@{target='manifest-v3.json';prepared='manifest.json';baselineExists=[bool]$profileExists;baselineSha256=$profileHash}
        )})
    }
    $sealed=@(Get-ChildItem -LiteralPath $Directory -File -Recurse | ForEach-Object { [pscustomobject]@{path=$_.FullName.Substring($Directory.Length+1);sha256=(Get-ContentHash $_.FullName)} })
    $receipt=[ordered]@{format=if ($profiles) {2} else {1};phase='prepared';sourceCommit=(Git-Result @('rev-parse','HEAD'));feedCommit='';baselineManifestSha256=(Get-ContentHash (Join-Path $root 'manifest.json'));sealedFiles=$sealed}
    Assert-PreparedRelease $Directory ([pscustomobject]$receipt)
    Write-ContractJson (Join-Path $Directory 'release.json') $receipt
    'PASS prepared release. No remote changes; use -Action Publish only for an authorized release.'
    return
}
# Authentication/network code is loaded only for explicit Publish, after local sealing checks.
function Gh-Result([string[]]$Arguments) {
    $r=Invoke-Gh @Arguments
    if ($r.Code -ne 0) { throw $r.Out }
    $r.Out
}
$candidate=Get-Content (Join-Path $Directory 'manifest.json') -Raw | ConvertFrom-Json
$initialReceipt=Get-Content -LiteralPath (Join-Path $Directory 'release.json') -Raw | ConvertFrom-Json
Assert-PreparedRelease $Directory $initialReceipt
$feeds=@(Get-ReleaseFeedTargets $Directory $initialReceipt)
$assetManifest=[pscustomobject]@{app=$candidate.app;pack=[pscustomobject]@{files=@($candidate.pack.files)}}
if ([int]$initialReceipt.format -eq 2) {
    $compat=Get-Content -LiteralPath (Join-Path $Directory 'compatibility-manifest.json') -Raw | ConvertFrom-Json
    $assetManifest.pack.files+=@($compat.pack.files)
}
$assets=Get-ReleaseAssets $assetManifest
. (Join-Path $PSScriptRoot 'gh-common.ps1')
$repo='owendavidgoode/clonedivers'
function Test-CommittedFeeds([string]$Revision,$Targets) {
    try {
        foreach ($target in $Targets) {
            $committed=(Git-Result @('show',"${Revision}:$($target.target)")) | ConvertFrom-Json
            $prepared=Get-Content -LiteralPath (Join-Path $Directory $target.prepared) -Raw | ConvertFrom-Json
            if (($committed | ConvertTo-Json -Depth 12 -Compress) -cne ($prepared | ConvertTo-Json -Depth 12 -Compress)) { return $false }
        }
        return $true
    } catch { return $false }
}
$ensure={ param($receipt)
    Assert-ReleaseFeedBaseline $root $Directory $feeds
    $head=Git-Result @('rev-parse','HEAD')
    # Recover a crash after the coordinated feed-only commit but before recording its hash.
    if (!$receipt.feedCommit -and $head -ne $receipt.sourceCommit -and
        (Git-Result @('rev-parse','HEAD^')) -eq $receipt.sourceCommit) {
        $changed=@((Git-Result @('diff','--name-only','HEAD^','HEAD')) -split '\r?\n' | Where-Object {$_})
        if ($changed.Count -gt 0 -and @($changed | Where-Object {$_ -notin @($feeds.target)}).Count -eq 0 -and (Test-CommittedFeeds 'HEAD' $feeds)) {
            $receipt.feedCommit=$head
            Write-ContractJson (Join-Path $Directory 'release.json') $receipt
        }
    }
    if ($head -ne $receipt.sourceCommit -and $head -ne $receipt.feedCommit) { throw 'Checkout changed since preparation; stage a new release.' }
    $dirty=Git-Result @('status','--porcelain','--untracked-files=no')
    if ($dirty) {
        $changed=@((Git-Result @('diff','--name-only','HEAD')) -split '\r?\n' | Where-Object {$_})
        $ownCandidate=$changed.Count -gt 0 -and @($changed | Where-Object {$_ -notin @($feeds.target)}).Count -eq 0
        foreach ($name in $changed) {
            $target=$feeds | Where-Object target -CEQ $name
            if (!$target -or (Get-ContentHash (Join-Path $root $name)) -ne (Get-ContentHash (Join-Path $Directory $target.prepared))) { $ownCandidate=$false;break }
        }
        if (!$ownCandidate) { throw 'Publish requires committed changes; use an isolated release checkout if other work is in progress.' }
    }
    if (!$receipt.feedCommit) { Git-Result @('push','origin',"$($receipt.sourceCommit):refs/heads/main") | Out-Null }
    foreach ($group in ($assets | Group-Object tag | Sort-Object { $_.Name.StartsWith('v') },Name)) {
        $view=Invoke-Gh release view $group.Name --repo $repo --json 'databaseId,isDraft'
        if ($view.Code -ne 0) {
            if ($view.Out -notmatch 'release not found|HTTP 404|Not Found') { throw $view.Out }
            Gh-Result @('release','create',$group.Name,'--repo',$repo,'--draft','--latest=false','--target',$receipt.sourceCommit,'--title',$group.Name,'--notes-file',(Join-Path $Directory 'notes.md')) | Out-Null
            $view=Invoke-Gh release view $group.Name --repo $repo --json 'databaseId,isDraft'
            if ($view.Code -ne 0) { throw $view.Out }
        }
        $release=$view.Out | ConvertFrom-Json
        $endpoint="repos/$repo/releases/$($release.databaseId)/assets?per_page=100"
        $hosted=@{}
        foreach ($page in ((Gh-Result @('api','--paginate','--slurp',$endpoint)) | ConvertFrom-Json)) { foreach ($a in $page) { $hosted[$a.name]=$a } }
        foreach ($a in $group.Group) {
            if (!$hosted.ContainsKey($a.name)) {
                $path=if ($a.name -eq 'Clonedivers.exe') {Join-Path $Directory 'Clonedivers.exe'} else {Join-Path $Directory "assets/$($a.name)"}
                if (!(Test-Path -LiteralPath $path)) { throw "Previously hosted asset is unavailable: $($a.name)" }
                Gh-Result @('release','upload',$group.Name,$path,'--repo',$repo) | Out-Null
            }
        }
        $hosted=@{}
        foreach ($page in ((Gh-Result @('api','--paginate','--slurp',$endpoint)) | ConvertFrom-Json)) { foreach ($a in $page) { $hosted[$a.name]=$a } }
        foreach ($a in $group.Group) { Assert-HostedReleaseAsset $a $hosted[$a.name] }
        if ($release.isDraft) {
            $latest=if ($group.Name -eq "v$($candidate.app.version)") {'--latest'} else {'--latest=false'}
            Gh-Result @('release','edit',$group.Name,'--repo',$repo,'--draft=false',$latest) | Out-Null
        }
    }
    $download=Join-Path $Directory 'download-check.exe'
    & curl.exe --fail --location --silent --show-error --output $download $candidate.app.url
    if ($LASTEXITCODE -ne 0 -or (Get-ContentHash $download) -ne $candidate.app.sha256) { throw 'Public executable download verification failed' }
}
$feed={ param($receipt)
    $remote=Get-RemoteMainCommit (Git-Result @('ls-remote','origin','refs/heads/main'))
    if ($remote -ne $receipt.sourceCommit -and $remote -ne $receipt.feedCommit) { throw 'Remote main advanced; do not overwrite another release.' }
    if (!$receipt.feedCommit) {
        Assert-ReleaseFeedBaseline $root $Directory $feeds
        foreach ($target in $feeds) { Copy-Item -LiteralPath (Join-Path $Directory $target.prepared) -Destination (Join-Path $root $target.target) }
        Git-Result (@('add','--')+@($feeds.target)) | Out-Null
        if (Git-Result (@('diff','--cached','--name-only','--')+@($feeds.target))) {
            Git-Result (@('commit','-m',"Publish verified launcher $($candidate.app.version) and pack $($candidate.pack.version)",'--')+@($feeds.target)) | Out-Null
        }
        $receipt.feedCommit=Git-Result @('rev-parse','HEAD')
        Write-ContractJson (Join-Path $Directory 'release.json') $receipt
    }
    Git-Result @('push','origin',"$($receipt.feedCommit):refs/heads/main") | Out-Null
    foreach ($target in $feeds) {
        $live=(Gh-Result @('api',"repos/$repo/contents/$($target.target)?ref=main",'-H','Accept: application/vnd.github.raw+json')) | ConvertFrom-Json
        $prepared=Get-Content -LiteralPath (Join-Path $Directory $target.prepared) -Raw | ConvertFrom-Json
        if (($live | ConvertTo-Json -Depth 12 -Compress) -cne ($prepared | ConvertTo-Json -Depth 12 -Compress)) { throw "Live feed differs from the prepared release: $($target.target)" }
    }
}
Invoke-VerifiedRelease $Directory $ensure $feed
