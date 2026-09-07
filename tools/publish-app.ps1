# Releases a new Clonedivers exe: tests, publishes the single-file build, tags vX.Y.Z, creates the GitHub Release
# (marked latest, asset named exactly Clonedivers.exe so the README link keeps working), writes the "app" block into
# manifest.json so running copies offer the one-click self-update, commits and pushes.
#
#   powershell -ExecutionPolicy Bypass -File tools\publish-app.ps1
#
# Refuses to run on a dirty tree (manifest.json excepted), if the tag exists, or if manifest.json is missing
# (publish the pack first: tools\publish-pack.ps1). The version comes from <Version> in Clonedivers\Clonedivers.csproj
# and the notes from docs\releases\vX.Y.Z.md.
param(
    [string]$Repo = "owendavidgoode/clonedivers",
    [switch]$NoPush,
    [switch]$SkipTests
)
$ErrorActionPreference = 'Stop'
$root = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
. (Join-Path $PSScriptRoot "gh-common.ps1")

$csproj = Read-Utf8 (Join-Path $root "Clonedivers\Clonedivers.csproj")
$m = [regex]::Match($csproj, '<Version>(\d+\.\d+\.\d+)</Version>')
if (-not $m.Success) { throw "no <Version>X.Y.Z</Version> in Clonedivers.csproj" }
$version = $m.Groups[1].Value
$tag = "v$version"
$notesPath = Join-Path $root "docs\releases\$tag.md"
$manifestPath = Join-Path $root "manifest.json"
$exe = Join-Path $root "dist\Clonedivers.exe"

# --- preconditions --------------------------------------------------------------------------------------------
if (-not (Test-Path $manifestPath)) { throw "manifest.json is missing: publish the pack first (tools\publish-pack.ps1)" }
if (-not (Test-Path $notesPath)) { throw "release notes missing: $notesPath" }
# Only uncommitted CODE blocks a release (the tag must match the exe). Untracked files, manifest.json and docs edits in
# progress (another session may be writing them) do not.
$dirty = @((Invoke-Native { & git -C $root status --porcelain }).Out -split "`n" | Where-Object { $_.Trim() -and $_ -notmatch '^\?\?' -and $_ -match '^\s*\S+\s+(Clonedivers/|Clonedivers\.Tests/|tools/)' })
if ($dirty.Count) { throw ("working tree is dirty (commit first):`n  " + ($dirty -join "`n  ")) }
if ((Invoke-Native { & git -C $root tag -l $tag }).Out.Trim()) { throw "tag $tag already exists locally; bump <Version> in the csproj" }
if ((Invoke-Native { & git -C $root ls-remote --tags origin "refs/tags/$tag" }).Out.Trim()) { throw "tag $tag already exists on origin" }
if ((Invoke-Gh release view $tag --repo $Repo).Code -eq 0) { throw "release $tag already exists on GitHub" }
if (Get-Process Clonedivers -ErrorAction SilentlyContinue) { throw "Clonedivers.exe is running; close it (dist\Clonedivers.exe is about to be rebuilt)" }

$dotnet = (Get-Command dotnet -ErrorAction SilentlyContinue).Source
if (-not $dotnet) { $dotnet = Join-Path $env:LOCALAPPDATA "Microsoft\dotnet\dotnet.exe" }
if (-not (Test-Path $dotnet)) { throw "dotnet SDK not found (PATH or %LOCALAPPDATA%\Microsoft\dotnet)" }

# --- build ----------------------------------------------------------------------------------------------------
if (-not $SkipTests) {
    Write-Host "Running tests…"
    $t = Invoke-Native { & $dotnet run --project (Join-Path $root "Clonedivers.Tests") }
    if ($t.Code -ne 0) { throw "tests failed:`n$($t.Out)" }
}
Write-Host "Publishing…"
$b = Invoke-Native { & $dotnet publish -c Release (Join-Path $root "Clonedivers") -nologo -v q }
if ($b.Code -ne 0) { throw "dotnet publish failed:`n$($b.Out)" }
$vi = (Get-Item $exe).VersionInfo
if ($vi.FileVersion -ne "$version.0") { throw "built exe reports FileVersion $($vi.FileVersion), expected $version.0" }
$size = (Get-Item $exe).Length
$sha = (Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash.ToLowerInvariant()
Write-Host ("  {0} bytes, sha256 {1}" -f $size, $sha)

# --- tag, release ---------------------------------------------------------------------------------------------
$g = Invoke-Native { & git -C $root tag -a $tag -m "Clonedivers $version" }; if ($g.Code -ne 0) { throw "git tag failed: $($g.Out)" }
if (-not $NoPush) { $p = Invoke-Native { & git -C $root push -q origin main $tag }; if ($p.Code -ne 0) { throw "git push failed: $($p.Out)" } }
$r = Invoke-Gh release create $tag $exe --repo $Repo --title "Clonedivers $version" --notes-file $notesPath
if ($r.Code -ne 0) { throw "release create failed: $($r.Out)" }
Write-Host "  release $tag created"

# --- app block into manifest.json (read-modify-write; the pack block is untouched) ------------------------------
$manifest = (Read-Utf8 $manifestPath) | ConvertFrom-Json
$app = [ordered]@{ version = $version; url = "https://github.com/$Repo/releases/download/$tag/Clonedivers.exe"; size = [long]$size; sha256 = $sha }
$out = [ordered]@{ format = 2; app = $app }
if ($manifest.pack) { $out.pack = $manifest.pack }
Write-Utf8 $manifestPath ($out | ConvertTo-Json -Depth 6)
$back = (Read-Utf8 $manifestPath) | ConvertFrom-Json
if ($back.app.version -ne $version -or ($manifest.pack -and @($back.pack.files).Count -ne @($manifest.pack.files).Count)) { throw "manifest.json self-check failed after writing the app block" }

$a = Invoke-Native { & git -C $root add -- manifest.json }; if ($a.Code -ne 0) { throw "git add failed: $($a.Out)" }
$c = Invoke-Native { & git -C $root -c core.safecrlf=false commit -q -m "Release Clonedivers $version" -- manifest.json }; if ($c.Code -ne 0) { throw "git commit failed: $($c.Out)" }
if (-not $NoPush) { $p = Invoke-Native { & git -C $root push -q origin main }; if ($p.Code -ne 0) { throw "git push failed: $($p.Out)" } }

if (-not $NoPush) { Start-Sleep -Seconds 5; Assert-ReadmeRedirect $Repo $version }
Write-Host "DONE: Clonedivers $version is the latest release; running copies will offer the update." -ForegroundColor Green
