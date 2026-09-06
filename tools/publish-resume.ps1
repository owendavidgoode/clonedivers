# Finishes (or retries) a pack publish without rebuilding the zips: uploads every part listed in pack.json to the
# release, publishes the release if it is still a draft, writes the download URLs into pack.json, commits and pushes.
# Safe to re-run: existing assets are replaced (--clobber), nothing is rebuilt.
#
#   powershell -ExecutionPolicy Bypass -File tools\publish-resume.ps1            # tag = pack-<version from pack.json>
param(
    [string]$Repo = "owendavidgoode/clonedivers",
    [string]$Tag = "",
    [string]$Notes = "",
    [switch]$NoPush
)
$ErrorActionPreference = 'Stop'
$root = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$manifestPath = Join-Path $root "pack.json"
$manifest = Get-Content $manifestPath -Raw | ConvertFrom-Json
if (-not $Tag) { $Tag = "pack-$($manifest.version)" }

$gh = (Get-Command gh -ErrorAction SilentlyContinue).Source
if (-not $gh) { $gh = Join-Path $env:LOCALAPPDATA "Programs\gh-cli\bin\gh.exe" }
if (-not (Test-Path $gh)) { throw "GitHub CLI not found." }
if (-not $env:GH_TOKEN) {
    $bash = Join-Path (Split-Path (Split-Path (Get-Command git).Source)) "bin\bash.exe"
    if (Test-Path $bash) { $env:GH_TOKEN = (& $bash -c "printf 'protocol=https\nhost=github.com\n\n' | git credential fill" | Where-Object { $_ -like 'password=*' } | Select-Object -First 1) -replace '^password=', '' }
    if (-not $env:GH_TOKEN) { throw "Not logged in to GitHub. Run 'gh auth login' or set GH_TOKEN." }
}
function Invoke-Native([scriptblock]$Block) {
    $prev = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
    try { $out = & $Block 2>&1; return @{ Code = $LASTEXITCODE; Out = ($out | Out-String) } } finally { $ErrorActionPreference = $prev }
}

$parts = @($manifest.files | ForEach-Object { Join-Path $root "dist\pack\$($_.file)" })
foreach ($p in $parts) { if (-not (Test-Path $p)) { throw "Part missing on disk: $p (run build-pack.ps1 first)" } }

$view = Invoke-Native { & $gh release view $Tag --repo $Repo --json isDraft,assets }
if ($view.Code -ne 0) {
    Write-Host "Release $Tag does not exist yet; creating it as a draft."
    $relNotes = "Clonedivers mod pack $($manifest.version)." + $(if ($Notes) { " $Notes" } elseif ($manifest.notes) { " $($manifest.notes)" } else { "" }) +
        "`n`nInstalled automatically by Clonedivers (Download pack). Manual route: download every part and use 'Install pack from file', selecting all parts at once." +
        "`n`nThe mods inside belong to their authors; see the README for the list and links."
    $r = Invoke-Native { & $gh release create $Tag --repo $Repo --title "Clonedivers pack $($manifest.version)" --notes $relNotes --draft --latest=false }
    if ($r.Code -ne 0) { throw "gh release create failed: $($r.Out)" }
    $view = Invoke-Native { & $gh release view $Tag --repo $Repo --json isDraft,assets }
}
$info = $view.Out | ConvertFrom-Json
$have = @{}
foreach ($a in @($info.assets)) { if ($a.state -eq 'uploaded') { $have[$a.name] = [long]$a.size } }

foreach ($i in 0..($parts.Count - 1)) {
    $p = $parts[$i]; $name = [IO.Path]::GetFileName($p); $size = (Get-Item $p).Length
    if ($have.ContainsKey($name) -and $have[$name] -eq $size) { Write-Host ("  ok       {0} ({1:N2} GB) already uploaded" -f $name, ($size / 1GB)); continue }
    Write-Host ("  upload   {0} ({1:N2} GB) ..." -f $name, ($size / 1GB))
    $attempt = 0
    do {
        $attempt++
        $u = Invoke-Native { & $gh release upload $Tag $p --repo $Repo --clobber }
        if ($u.Code -eq 0) { break }
        Write-Warning "upload attempt $attempt failed: $($u.Out.Trim())"
        Start-Sleep -Seconds 15
    } while ($attempt -lt 5)
    if ($u.Code -ne 0) { throw "Giving up on $name after $attempt attempts." }
}

# Verify every asset is present with the right size, then publish the draft.
$view = Invoke-Native { & $gh release view $Tag --repo $Repo --json isDraft,assets,url }
$info = $view.Out | ConvertFrom-Json
foreach ($f in $manifest.files) {
    $a = @($info.assets) | Where-Object { $_.name -eq $f.file } | Select-Object -First 1
    if (-not $a) { throw "Asset missing after upload: $($f.file)" }
    if ([long]$a.size -ne [long]$f.size) { throw "Size mismatch for $($f.file): GitHub $($a.size) vs local $($f.size)" }
}
if ($info.isDraft) {
    $e = Invoke-Native { & $gh release edit $Tag --repo $Repo --draft=false --latest=false }
    if ($e.Code -ne 0) { throw "gh release edit (publish) failed: $($e.Out)" }
    Write-Host "Release $Tag published."
}

# Public download URLs are deterministic once the release is published.
for ($i = 0; $i -lt $manifest.files.Count; $i++) {
    $manifest.files[$i].url = "https://github.com/$Repo/releases/download/$Tag/$([Uri]::EscapeDataString($manifest.files[$i].file))"
}
[System.IO.File]::WriteAllText($manifestPath, (($manifest | ConvertTo-Json -Depth 4) + "`n"), (New-Object System.Text.UTF8Encoding $false))
Write-Host "pack.json now points at release $Tag"

Push-Location $root
try {
    $c = Invoke-Native { & git add pack.json; & git commit -q -m "Publish pack $($manifest.version)" -m ("Release {0}: {1} part(s), {2:N1} GB." -f $Tag, $manifest.files.Count, (($manifest.files | Measure-Object size -Sum).Sum / 1GB)) }
    if ($c.Code -ne 0) { throw "git commit failed: $($c.Out)" }
    if (-not $NoPush) {
        $p = Invoke-Native { & git push }
        if ($p.Code -ne 0) { throw "git push failed: $($p.Out)" }
        Write-Host "Pushed. Friends' Clonedivers will offer 'Download pack v$($manifest.version)' on next launch." -ForegroundColor Green
    }
} finally { Pop-Location }
