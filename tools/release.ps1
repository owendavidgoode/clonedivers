# Stage is offline and the default. Publish is an explicit future operation, never implicit.
param(
    [ValidateSet('Stage','Publish')][string]$Action='Stage',
    [Parameter(Mandatory=$true)][string]$Directory,
    [string]$CandidateManifest='', [string]$TestedManifest='', [string]$BaselineManifest='',
    [string]$GameDir='', [string]$AppExe='', [string]$AssetDirectory='', [string]$NotesPath='', [string]$Dotnet=''
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
    foreach ($required in @($CandidateManifest,$TestedManifest,$BaselineManifest,$GameDir,$AppExe,$NotesPath)) { if (!$required) { throw 'Stage requires candidate, tested and baseline manifests, game directory, executable and notes.' } }
    if (Test-Path -LiteralPath $Directory) { throw 'Use a new staging directory; prepared releases are immutable.' }
    if (!$Dotnet) { $Dotnet=Join-Path $root 'dist/dotnet-sdk/dotnet.exe' }
    $manifest=Get-Content -LiteralPath $CandidateManifest -Raw | ConvertFrom-Json
    $version=(Get-Item -LiteralPath $AppExe).VersionInfo.ProductVersion
    if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'Executable has no release version' }
    $manifest | Add-Member -Force -NotePropertyName app -NotePropertyValue ([pscustomobject]@{version=$version;url="https://github.com/owendavidgoode/clonedivers/releases/download/v$version/Clonedivers.exe";size=(Get-Item $AppExe).Length;sha256=(Get-ContentHash $AppExe)})
    $assets=Get-ReleaseAssets $manifest
    & $Dotnet run --project (Join-Path $root 'Clonedivers.Tests/Clonedivers.Tests.csproj')
    if ($LASTEXITCODE -ne 0) { throw 'Native tests failed' }
    & $Dotnet run --project (Join-Path $root 'tools/Release.Check/Release.Check.csproj') -- $CandidateManifest $TestedManifest $BaselineManifest $GameDir
    if ($LASTEXITCODE -ne 0) { throw 'Release mode validation failed' }
    $known=@{}; $old=Get-Content (Join-Path $root 'manifest.json') -Raw | ConvertFrom-Json
    foreach ($a in (Get-ReleaseAssets $old)) { $known[$a.url]=$a.sha256 }
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
    $sealed=@(Get-ChildItem -LiteralPath $Directory -File -Recurse | ForEach-Object { [pscustomobject]@{path=$_.FullName.Substring($Directory.Length+1);sha256=(Get-ContentHash $_.FullName)} })
    Write-ContractJson (Join-Path $Directory 'release.json') ([ordered]@{format=1;phase='prepared';sourceCommit=(Git-Result @('rev-parse','HEAD'));feedCommit='';baselineManifestSha256=(Get-ContentHash (Join-Path $root 'manifest.json'));sealedFiles=$sealed})
    'PASS prepared release. No remote changes; use -Action Publish only for an authorized release.'
    return
}
# Authentication/network code is loaded only for explicit Publish.
. (Join-Path $PSScriptRoot 'gh-common.ps1')
function Gh-Result([string[]]$Arguments) {
    $r=Invoke-Gh @Arguments
    if ($r.Code -ne 0) { throw $r.Out }
    $r.Out
}
$candidate=Get-Content (Join-Path $Directory 'manifest.json') -Raw | ConvertFrom-Json
$assets=Get-ReleaseAssets $candidate
$repo='owendavidgoode/clonedivers'
$ensure={ param($receipt)
    $head=Git-Result @('rev-parse','HEAD')
    # Recover a crash after our manifest-only commit but before recording its hash.
    if (!$receipt.feedCommit -and $head -ne $receipt.sourceCommit -and
        (Git-Result @('rev-parse','HEAD^')) -eq $receipt.sourceCommit -and
        (Git-Result @('diff','--name-only','HEAD^','HEAD')) -eq 'manifest.json') {
        $committed=(Git-Result @('show','HEAD:manifest.json')) | ConvertFrom-Json
        if (($committed | ConvertTo-Json -Depth 12 -Compress) -eq ($candidate | ConvertTo-Json -Depth 12 -Compress)) {
            $receipt.feedCommit=$head
            Write-ContractJson (Join-Path $Directory 'release.json') $receipt
        }
    }
    if ($head -ne $receipt.sourceCommit -and $head -ne $receipt.feedCommit) { throw 'Checkout changed since preparation; stage a new release.' }
    $dirty=Git-Result @('status','--porcelain','--untracked-files=no')
    if ($dirty) {
        $changed=Git-Result @('diff','--name-only','HEAD')
        $ownCandidate=$changed -eq 'manifest.json' -and
            (Get-ContentHash (Join-Path $root 'manifest.json')) -eq (Get-ContentHash (Join-Path $Directory 'manifest.json'))
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
    $manifestPath=Join-Path $root 'manifest.json'
    $prepared=Join-Path $Directory 'manifest.json'
    $remote=(Git-Result @('ls-remote','origin','refs/heads/main') -split '\s+')[0]
    if ($remote -ne $receipt.sourceCommit -and $remote -ne $receipt.feedCommit) { throw 'Remote main advanced; do not overwrite another release.' }
    if (!$receipt.feedCommit) {
        $localHash=Get-ContentHash $manifestPath
        if ($localHash -ne $receipt.baselineManifestSha256 -and $localHash -ne (Get-ContentHash $prepared)) { throw 'Local feed changed after preparation' }
        Copy-Item -LiteralPath $prepared -Destination $manifestPath
        Git-Result @('add','--','manifest.json') | Out-Null
        if (Git-Result @('diff','--cached','--name-only','--','manifest.json')) {
            Git-Result @('commit','-m',"Publish verified launcher $($candidate.app.version) and pack $($candidate.pack.version)",'--','manifest.json') | Out-Null
        }
        $receipt.feedCommit=Git-Result @('rev-parse','HEAD')
        Write-ContractJson (Join-Path $Directory 'release.json') $receipt
    }
    Git-Result @('push','origin',"$($receipt.feedCommit):refs/heads/main") | Out-Null
    $live=(Gh-Result @('api',"repos/$repo/contents/manifest.json?ref=main",'-H','Accept: application/vnd.github.raw+json')) | ConvertFrom-Json
    if (($live | ConvertTo-Json -Depth 12 -Compress) -ne ($candidate | ConvertTo-Json -Depth 12 -Compress)) { throw 'Live feed differs from the prepared release' }
}
Invoke-VerifiedRelease $Directory $ensure $feed
