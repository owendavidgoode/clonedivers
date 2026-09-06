# One command to publish the Clonedivers pack: build from the game's data\, upload to a GitHub Release,
# write the URLs into pack.json, commit and push. Friends' Clonedivers then offers "Download pack".
#
#   powershell -ExecutionPolicy Bypass -File tools\publish-pack.ps1 -Notes "Built for Helldivers 2 patch 7.0.2"
#
# Prereqs: mods deployed in <game>\data\ (Clonedivers says CLONES: ON), git, and the GitHub CLI (gh) either on PATH or
# in %LOCALAPPDATA%\Programs\gh-cli\bin. Auth: gh's own login, GH_TOKEN, or the token Git Credential Manager already
# stores for github.com (this script picks that up automatically).
#
# Release assets are capped at 2 GB each, so the pack is split into parts as needed. The pack release is never marked
# "latest", so the README's releases/latest/download/Clonedivers.exe link keeps pointing at the app.
param(
    [string]$Version = (Get-Date -Format "yyyy.MM.dd"),
    [string]$Notes = "",
    [string]$GameDir = "",
    [string]$Repo = "owendavidgoode/clonedivers",
    [string]$Tag = "",
    [double]$MaxPartGB = 1.9,
    [switch]$NoPush
)
$ErrorActionPreference = 'Stop'
$root = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
if (-not $Tag) { $Tag = "pack-$Version" }

# --- find gh and a token --------------------------------------------------------------------------------------
$gh = (Get-Command gh -ErrorAction SilentlyContinue).Source
if (-not $gh) { $gh = Join-Path $env:LOCALAPPDATA "Programs\gh-cli\bin\gh.exe" }
if (-not (Test-Path $gh)) { throw "GitHub CLI not found. Install it (winget install GitHub.cli) or drop the portable build in %LOCALAPPDATA%\Programs\gh-cli." }
# Native tools write chatter to stderr; under ErrorActionPreference=Stop that would abort the script, so run them quietly.
function Invoke-Native([scriptblock]$Block) {
    $prev = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
    try { $out = & $Block 2>&1; return @{ Code = $LASTEXITCODE; Out = ($out | Out-String) } }
    finally { $ErrorActionPreference = $prev }
}

# Borrow the token Git Credential Manager keeps for github.com (what `git push` already uses).
# Uses a raw process so git gets LF-terminated lines; PowerShell's pipeline would send CRLF, which git rejects.
function Get-GitHubTokenFromGit {
    # Feeding git's stdin from a .NET Process does not work here (git sees an empty first line), so go through a shell
    # that pipes properly: Git for Windows' own bash first, cmd as a fallback.
    $gitExe = (Get-Command git -ErrorAction SilentlyContinue).Source
    $bash = if ($gitExe) { Join-Path (Split-Path (Split-Path $gitExe)) "bin\bash.exe" } else { "" }
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    if ($bash -and (Test-Path $bash)) {
        $psi.FileName = $bash
        $psi.Arguments = "-c ""printf 'protocol=https\nhost=github.com\n\n' | git credential fill"""
    } else {
        $psi.FileName = "cmd.exe"
        $psi.Arguments = '/c "(echo protocol=https&echo host=github.com&echo.) | git credential fill"'
    }
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true; $psi.RedirectStandardError = $true
    $psi.EnvironmentVariables["GIT_TERMINAL_PROMPT"] = "0"
    $p = [System.Diagnostics.Process]::Start($psi)
    $out = $p.StandardOutput.ReadToEnd(); $p.WaitForExit()
    $line = $out -split "`n" | Where-Object { $_ -like "password=*" } | Select-Object -First 1
    if ($line) { return $line.Substring("password=".Length).Trim() }
    return $null
}

if (-not $env:GH_TOKEN) {
    $status = Invoke-Native { & $gh auth status }
    if ($status.Code -ne 0) {
        $tok = Get-GitHubTokenFromGit
        if (-not $tok) { throw "Not logged in to GitHub. Run 'gh auth login' or set GH_TOKEN." }
        $env:GH_TOKEN = $tok
    }
}

# --- build ----------------------------------------------------------------------------------------------------
$buildArgs = @{ Version = $Version; Notes = $Notes; MaxPartGB = $MaxPartGB }
if ($GameDir) { $buildArgs.GameDir = $GameDir }
& (Join-Path $PSScriptRoot "build-pack.ps1") @buildArgs
$manifestPath = Join-Path $root "pack.json"
$manifest = Get-Content $manifestPath -Raw | ConvertFrom-Json
$parts = @($manifest.files | ForEach-Object { Join-Path $root "dist\pack\$($_.file)" })
foreach ($p in $parts) { if (-not (Test-Path $p)) { throw "Expected part missing: $p" } }

# --- upload ---------------------------------------------------------------------------------------------------
$existing = Invoke-Native { & $gh release view $Tag --repo $Repo }
if ($existing.Code -eq 0) {
    Write-Host "Release $Tag exists; replacing its assets."
    foreach ($p in $parts) {
        $r = Invoke-Native { & $gh release upload $Tag $p --repo $Repo --clobber }
        if ($r.Code -ne 0) { throw "gh release upload failed: $($r.Out)" }
    }
} else {
    $relNotes = "Clonedivers mod pack $Version." + $(if ($Notes) { " $Notes" } else { "" }) +
        "`n`nInstalled automatically by Clonedivers (Download pack). Manual route: download every part and use 'Install pack from file', selecting all parts at once." +
        "`n`nThe mods inside belong to their authors; see the README for the list and links."
    $r = Invoke-Native { & $gh release create $Tag @parts --repo $Repo --title "Clonedivers pack $Version" --notes $relNotes --latest=false }
    if ($r.Code -ne 0) { throw "gh release create failed: $($r.Out)" }
}

# --- write URLs into pack.json --------------------------------------------------------------------------------
$view = Invoke-Native { & $gh release view $Tag --repo $Repo --json assets }
if ($view.Code -ne 0) { throw "gh release view failed: $($view.Out)" }
$assets = $view.Out | ConvertFrom-Json
for ($i = 0; $i -lt $manifest.files.Count; $i++) {
    $name = $manifest.files[$i].file
    $asset = $assets.assets | Where-Object { $_.name -eq $name } | Select-Object -First 1
    if (-not $asset) { throw "Uploaded asset not found on the release: $name" }
    $manifest.files[$i].url = $asset.url
    if ([long]$asset.size -ne [long]$manifest.files[$i].size) { throw "Size mismatch after upload for $name (local $($manifest.files[$i].size), GitHub $($asset.size))." }
}
$json = $manifest | ConvertTo-Json -Depth 4
[System.IO.File]::WriteAllText($manifestPath, $json + "`n", (New-Object System.Text.UTF8Encoding $false))
Write-Host "pack.json now points at release $Tag"

# --- commit + push --------------------------------------------------------------------------------------------
Push-Location $root
try {
    $body = "Release {0}: {1} part(s), {2:N1} GB. {3}" -f $Tag, $manifest.files.Count, (($manifest.files | Measure-Object size -Sum).Sum / 1GB), $Notes
    $c = Invoke-Native { & git add pack.json; & git commit -q -m "Publish pack $Version" -m $body }
    if ($c.Code -ne 0) { throw "git commit failed: $($c.Out)" }
    if (-not $NoPush) {
        $p = Invoke-Native { & git push }
        if ($p.Code -ne 0) { throw "git push failed: $($p.Out)" }
        Write-Host "Pushed. Friends' Clonedivers will offer 'Download pack v$Version' on next launch (an app older than 1.1.1 may need up to 5 minutes for GitHub's CDN to catch up)." -ForegroundColor Green
    }
    else { Write-Host "Committed (not pushed, -NoPush)." }
} finally { Pop-Location }
