# Publishes the pack the per-file way (Clonedivers 1.3+): builds manifest.json from Helldivers 2\data\, uploads every
# file whose contents are not hosted yet as a SHA-256-named asset on a GitHub Release, verifies, publishes the release
# (never "latest"), commits manifest.json and pushes. Friends' Clonedivers then offers "Update pack" and downloads only
# what changed.
#
#   powershell -ExecutionPolicy Bypass -File tools\publish-pack.ps1 -Version 2026.09.05-r3 -Notes "..." -Detach
#
# -Detach runs the whole thing in a hidden background PowerShell (uploads take an hour or more) and returns at once;
# progress is in dist\publish-pack.state.json and dist\publish-pack.log. Re-running is safe: assets already on the
# release are skipped by name (the name IS the content hash), a published release skips straight to the commit.
# Never run two at once: dist\publish-pack.lock holds the PID of the live run.
#
# pack.json (the 1.2.0 zip pack) is never touched. Pack releases must never be deleted by hand: later manifests reuse
# their assets (see docs\PUBLISHING.md).
param(
    [Parameter(Mandatory = $true)][string]$Version,
    [string]$Notes = "",
    [ValidateSet('ok', 'broken')][string]$Status = 'ok',
    [string]$StatusNotes = "",
    [string]$GameDir = "",
    [string]$Repo = "owendavidgoode/clonedivers",
    [string]$VerifyZips = "",
    [switch]$AllowPendingGameUpdate,
    [int]$MaxAssetsPerRelease = 900,
    [int]$BatchSize = 40,
    [switch]$NoPush,
    [switch]$Detach,
    [switch]$PlanOnly
)
$ErrorActionPreference = 'Stop'
$root = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$dist = Join-Path $root "dist"
New-Item -ItemType Directory -Force $dist | Out-Null
$lockPath = Join-Path $dist "publish-pack.lock"
$statePath = Join-Path $dist "publish-pack.state.json"
$manifestPath = Join-Path $root "manifest.json"
$uploadPlanPath = Join-Path $dist "upload-plan.json"

if ($Detach) {
    $childArgs = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$PSCommandPath`"", '-Version', $Version, '-Repo', $Repo, '-MaxAssetsPerRelease', $MaxAssetsPerRelease, '-BatchSize', $BatchSize, '-Status', $Status)
    if ($Notes) { $childArgs += @('-Notes', "`"$Notes`"") }
    if ($StatusNotes) { $childArgs += @('-StatusNotes', "`"$StatusNotes`"") }
    if ($GameDir) { $childArgs += @('-GameDir', "`"$GameDir`"") }
    if ($VerifyZips) { $childArgs += @('-VerifyZips', "`"$VerifyZips`"") }
    if ($AllowPendingGameUpdate) { $childArgs += '-AllowPendingGameUpdate' }
    if ($NoPush) { $childArgs += '-NoPush' }
    if ($PlanOnly) { $childArgs += '-PlanOnly' }
    $p = Start-Process powershell -ArgumentList $childArgs -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $dist "publish-pack.log") -RedirectStandardError (Join-Path $dist "publish-pack.err")
    Write-Host "publish-pack running detached: PID $($p.Id); watch $statePath and dist\publish-pack.log"
    exit 0
}

# --- lock -----------------------------------------------------------------------------------------------------
if (Test-Path $lockPath) {
    $otherPid = (Get-Content $lockPath -ErrorAction SilentlyContinue | Select-Object -First 1)
    if ($otherPid -and (Get-Process -Id ([int]$otherPid) -ErrorAction SilentlyContinue)) { throw "another publish-pack (PID $otherPid) is still running; wait for it or kill it first" }
}
Set-Content -Path $lockPath -Value $PID -Encoding ASCII
$state = [ordered]@{ phase = "starting"; version = $Version; pid = $PID; releases = @(); lastError = ""; updatedAt = "" }
function Save-State {
    $state.updatedAt = (Get-Date).ToString("s")
    [System.IO.File]::WriteAllText($statePath, ($state | ConvertTo-Json -Depth 5), (New-Object System.Text.UTF8Encoding($false)))
}
function Log([string]$msg) { Write-Host ("[{0}] {1}" -f (Get-Date -Format "HH:mm:ss"), $msg) }
function Sum-Size($items) { [long]$s = 0; foreach ($i in @($items)) { if ($i) { $s += [long]$i.size } }; return $s }

try {
    . (Join-Path $PSScriptRoot "gh-common.ps1")

    # --- preconditions ----------------------------------------------------------------------------------------
    $frozen = Invoke-Native { & git -C $root diff --quiet HEAD -- pack.json }
    if ($frozen.Code -ne 0) { throw "pack.json has uncommitted changes. It is frozen for 1.2.0 clients; restore it (git checkout -- pack.json) before publishing." }
    if (Get-Process helldivers2 -ErrorAction SilentlyContinue) { Write-Warning "Helldivers 2 is running; hashing continues (read-only) but do not change mods until it exits" }

    # --- build the manifest -----------------------------------------------------------------------------------
    $state.phase = "building"; Save-State
    $bm = @{ Version = $Version; Notes = $Notes; Status = $Status; StatusNotes = $StatusNotes; Repo = $Repo; ManifestPath = $manifestPath; MaxAssetsPerRelease = $MaxAssetsPerRelease; UploadPlanPath = $uploadPlanPath }
    if ($GameDir) { $bm.GameDir = $GameDir }
    if ($VerifyZips) { $bm.VerifyZips = $VerifyZips }
    if ($AllowPendingGameUpdate) { $bm.AllowPendingGameUpdate = $true }
    & (Join-Path $PSScriptRoot "build-manifest.ps1") @bm
    # PS 5.1 hands a JSON array down the pipeline as ONE object; -InputObject keeps it an array of entries.
    $plan = @(ConvertFrom-Json -InputObject (Read-Utf8 $uploadPlanPath) | ForEach-Object { $_ })
    $byTag = @($plan | Group-Object tag)
    Log ("upload plan: {0} file(s), {1:N2} GB, {2} release(s)" -f $plan.Count, ((Sum-Size $plan) / 1GB), $byTag.Count)
    if ($PlanOnly) { $state.phase = "plan-only"; Save-State; Log "PlanOnly: stopping before any GitHub call"; return }

    # --- releases and uploads ---------------------------------------------------------------------------------
    $linkRoot = Join-Path $env:LOCALAPPDATA "Clonedivers\publish\$Version"
    New-Item -ItemType Directory -Force $linkRoot | Out-Null
    $tags = @($byTag | ForEach-Object { $_.Name })
    if ($tags.Count -eq 0) { $tags = @("pack-$Version-files") }   # nothing new to upload: still publish the (possibly existing) release

    function Get-Release([string]$tag) {
        $r = Invoke-Gh api --paginate "repos/$Repo/releases?per_page=100" --jq '.[] | [.id, .tag_name, (.draft|tostring)] | @tsv'
        if ($r.Code -ne 0) { throw "gh api releases failed: $($r.Out)" }
        foreach ($line in ($r.Out -split "`n")) { $c = $line.Trim() -split "`t"; if ($c.Count -ge 3 -and $c[1] -eq $tag) { return @{ Id = $c[0]; Draft = ($c[2] -eq 'true') } } }
        return $null
    }
    function Get-Assets([string]$id) {
        $r = Invoke-Gh api --paginate "repos/$Repo/releases/$id/assets?per_page=100" --jq '.[] | [.id, .name, .size, .state] | @tsv'
        if ($r.Code -ne 0) { throw "gh api assets failed: $($r.Out)" }
        $h = @{}
        foreach ($line in ($r.Out -split "`n")) { $c = $line.Trim() -split "`t"; if ($c.Count -ge 4) { $h[$c[1]] = @{ Id = $c[0]; Size = [long]$c[2]; State = $c[3] } } }
        return $h
    }

    foreach ($tag in $tags) {
        $wanted = @($plan | Where-Object { $_.tag -eq $tag })
        $rel = Get-Release $tag
        if (-not $rel) {
            $body = "Clonedivers pack $Version, one asset per file, named by SHA-256. Clonedivers maps them to file names through manifest.json; " +
                    "download the app from the latest release instead of grabbing these by hand. The mods belong to their authors on Nexus Mods (see docs/MODS.md); this pack is shared privately among friends."
            $c = Invoke-Gh release create $tag --repo $Repo --draft --latest=false --title "Clonedivers pack $Version files" --notes $body
            if ($c.Code -ne 0) { throw "release create failed: $($c.Out)" }
            # The list endpoint lags a few seconds behind a create; retry before giving up.
            for ($try = 0; $try -lt 20 -and -not $rel; $try++) { Start-Sleep -Seconds 3; $rel = Get-Release $tag }
            if (-not $rel) { throw "release $tag not found after creating it" }
            Log "created draft release $tag"
        }
        $entry = [ordered]@{ tag = $tag; id = $rel.Id; planned = $wanted.Count; uploaded = 0; bytesTotal = (Sum-Size $wanted); bytesDone = [long]0 }
        $state.releases = @($state.releases) + @($entry); $state.phase = "uploading $tag"; Save-State

        $assets = Get-Assets $rel.Id
        # Remove half-uploaded or wrong-size assets so a re-upload is clean.
        foreach ($w in $wanted) {
            $a = $assets[$w.sha]
            if ($a -and ($a.State -ne 'uploaded' -or $a.Size -ne [long]$w.size)) {
                Log "deleting bad asset $($w.sha) (state $($a.State), size $($a.Size))"
                $d = Invoke-Gh release delete-asset $tag $w.sha --repo $Repo -y
                if ($d.Code -ne 0) { throw "delete-asset failed: $($d.Out)" }
                $assets.Remove($w.sha)
            }
        }
        $todo = @($wanted | Where-Object { -not $assets.ContainsKey($_.sha) })
        if ($assets.Count + $todo.Count -gt 1000) { throw "release $tag would exceed 1000 assets ($($assets.Count) present + $($todo.Count) planned); lower -MaxAssetsPerRelease" }
        $entry.uploaded = $wanted.Count - $todo.Count
        $entry.bytesDone = (Sum-Size ($wanted | Where-Object { $assets.ContainsKey($_.sha) }))
        Save-State
        Log ("{0}: {1} of {2} already hosted, {3} to upload" -f $tag, $entry.uploaded, $wanted.Count, $todo.Count)

        for ($i = 0; $i -lt $todo.Count; $i += $BatchSize) {
            $batch = @($todo[$i..([math]::Min($i + $BatchSize, $todo.Count) - 1)])
            $links = @()
            foreach ($b in $batch) {
                $link = Join-Path $linkRoot $b.sha
                if (-not (Test-Path $link)) {
                    try { New-Item -ItemType HardLink -Path $link -Target $b.localPath -ErrorAction Stop | Out-Null }
                    catch { Write-Warning "hard link failed ($($_.Exception.Message)); copying"; Copy-Item -LiteralPath $b.localPath -Destination $link }
                }
                $links += $link
            }
            $started = Get-Date
            $attempt = 0
            while ($true) {
                $attempt++
                $u = Invoke-Gh release upload $tag @links --repo $Repo
                if ($u.Code -eq 0) { break }
                if ($u.Out -match 'secondary rate limit|HTTP 403|HTTP 429|abuse') {
                    $wait = 120; if ($u.Out -match 'retry-after:\s*(\d+)') { $wait = [int]$Matches[1] + 5 }
                    Log "rate limited; sleeping $wait s"; Start-Sleep -Seconds $wait
                } elseif ($u.Out -match 'already exists') {
                    break   # a previous attempt landed; the verify pass below settles it
                } elseif ($attempt -ge 3) { throw "upload failed after $attempt attempts: $($u.Out)" }
                else { Log "upload error, retrying in 15 s: $(($u.Out -split "`n")[0])"; Start-Sleep -Seconds 15 }
            }
            $entry.uploaded += $batch.Count; $entry.bytesDone += (Sum-Size $batch); Save-State
            Log ("{0}: {1}/{2} uploaded ({3:N2} of {4:N2} GB)" -f $tag, $entry.uploaded, $wanted.Count, ($entry.bytesDone / 1GB), ($entry.bytesTotal / 1GB))
            # Pacing: at most $BatchSize uploads per minute (GitHub's secondary limit is 80 content requests/min, 500/h).
            $elapsed = (Get-Date) - $started
            if ($i + $BatchSize -lt $todo.Count -and $elapsed.TotalSeconds -lt 60) { Start-Sleep -Seconds ([int](60 - $elapsed.TotalSeconds)) }
        }

        # Verify every planned asset is hosted with the right size.
        $assets = Get-Assets $rel.Id
        $missing = @($wanted | Where-Object { -not $assets.ContainsKey($_.sha) -or $assets[$_.sha].Size -ne [long]$_.size -or $assets[$_.sha].State -ne 'uploaded' })
        if ($missing.Count) { throw ("{0} asset(s) missing or wrong after upload on {1}: {2}. Re-run to resume." -f $missing.Count, $tag, (($missing | Select-Object -First 5 | ForEach-Object { $_.sha }) -join ', ')) }

        if ($rel.Draft) {
            $state.phase = "publishing $tag"; Save-State
            $e = Invoke-Gh release edit $tag --repo $Repo --draft=false --latest=false
            if ($e.Code -ne 0) { throw "release edit failed: $($e.Out)" }
            $v = Invoke-Gh release view $tag --repo $Repo --json "isDraft,isPrerelease"
            if ($v.Code -ne 0 -or $v.Out -notmatch '"isDraft":\s*false') { throw "release $tag still looks like a draft: $($v.Out)" }
            Log "published $tag"
        } else { Log "$tag was already published" }
    }
    Assert-ReadmeRedirect $Repo
    Remove-Item -LiteralPath $linkRoot -Recurse -Force -ErrorAction SilentlyContinue

    # --- commit manifest.json only ----------------------------------------------------------------------------
    $state.phase = "committing"; Save-State
    $changed = Invoke-Native { & git -C $root diff --quiet -- manifest.json }
    $untracked = (Invoke-Native { & git -C $root ls-files --error-unmatch manifest.json }).Code -ne 0
    if ($changed.Code -ne 0 -or $untracked) {
        $n = @((Read-Utf8 $manifestPath | ConvertFrom-Json).pack.files).Count
        $gb = ((Sum-Size $plan) / 1GB)
        $msg = "Publish pack $Version`n`n$n files, $($plan.Count) new asset(s) ($($gb.ToString('N2')) GB) uploaded, the rest reused from earlier releases."
        $a = Invoke-Native { & git -C $root add -- manifest.json }; if ($a.Code -ne 0) { throw "git add failed: $($a.Out)" }
        $c = Invoke-Native { & git -C $root -c core.safecrlf=false commit -q -m $msg -- manifest.json }; if ($c.Code -ne 0) { throw "git commit failed: $($c.Out)" }
        if (-not $NoPush) { $p = Invoke-Native { & git -C $root push -q origin main }; if ($p.Code -ne 0) { throw "git push failed: $($p.Out)" } }
        Log "manifest.json committed$(if ($NoPush) { '' } else { ' and pushed' })"
    } else { Log "manifest.json unchanged; nothing to commit" }
    $state.phase = "done"; Save-State
    Log "DONE: pack $Version is live"
}
catch {
    $state.phase = "failed"; $state.lastError = "$_"; Save-State
    Write-Error "$_"
    exit 1
}
finally {
    Remove-Item -LiteralPath $lockPath -Force -ErrorAction SilentlyContinue
}
