# Upload a prepared content-addressed plan, verify GitHub digests, then publish its pack release.
param([Parameter(Mandatory=$true)][string]$PlanPath, [Parameter(Mandatory=$true)][string]$NotesPath)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'gh-common.ps1')
$plan = @(Get-Content -LiteralPath $PlanPath -Raw | ConvertFrom-Json)
$tags = @($plan.tag | Sort-Object -Unique)
if ($tags.Count -ne 1 -or $tags[0] -notmatch '^pack-.*-files$') { throw 'Expected one pack release tag' }
$tag = $tags[0]
$repo = 'owendavidgoode/clonedivers'
$view = Invoke-Gh release view $tag --repo $repo --json 'databaseId,isDraft'
if ($view.Code -ne 0) {
    if ($view.Out -notmatch 'release not found|HTTP 404|Not Found') { throw $view.Out }
    $created = Invoke-Gh release create $tag --repo $repo --draft --latest=false --title "Clonedivers pack $($tag -replace '^pack-|\-files$','') files" --notes-file $NotesPath
    if ($created.Code -ne 0) { throw $created.Out }
}
$view = Invoke-Gh release view $tag --repo $repo --json 'databaseId,isDraft'
if ($view.Code -ne 0) { throw $view.Out }
$release = $view.Out | ConvertFrom-Json
function Read-Assets {
    $r = Invoke-Gh api "repos/$repo/releases/$($release.databaseId)/assets?per_page=100"
    if ($r.Code -ne 0) { throw $r.Out }
    return @($r.Out | ConvertFrom-Json)
}
$assets = @(Read-Assets)
$paths = @()
foreach ($item in $plan) {
    if ((Get-Item -LiteralPath $item.localPath).Length -ne $item.size -or
        (Get-FileHash -LiteralPath $item.localPath -Algorithm SHA256).Hash.ToLowerInvariant() -ne $item.sha) { throw "Local asset mismatch: $($item.sha)" }
    $found = @($assets | Where-Object name -eq $item.sha)
    if ($found.Count -gt 0) {
        if ($found[0].size -ne $item.size -or $found[0].digest -ne "sha256:$($item.sha)") { throw "Existing hosted asset mismatch: $($item.sha)" }
    } else { $paths += $item.localPath }
}
if ($paths.Count) {
    "Uploading $($paths.Count) verified assets to $tag"
    $upload = Invoke-Gh release upload $tag @paths --repo $repo
    if ($upload.Code -ne 0) { throw $upload.Out }
}
$assets = @(Read-Assets)
foreach ($item in $plan) {
    $found = @($assets | Where-Object name -eq $item.sha)
    if ($found.Count -ne 1 -or $found[0].state -ne 'uploaded' -or $found[0].size -ne $item.size -or $found[0].digest -ne "sha256:$($item.sha)") {
        throw "Hosted asset verification failed: $($item.sha)"
    }
}
$publish = Invoke-Gh release edit $tag --repo $repo --draft=false --latest=false
if ($publish.Code -ne 0) { throw $publish.Out }
"Published $tag; all $($plan.Count) assets verified by size and GitHub SHA256 digest."
