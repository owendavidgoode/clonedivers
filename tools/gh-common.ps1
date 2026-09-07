# Shared plumbing for the publish scripts (dot-source it): finds the GitHub CLI, borrows the token Git Credential Manager
# already holds for github.com, and wraps native tools so their stderr chatter does not abort a script that runs with
# ErrorActionPreference = Stop. Nothing here publishes anything by itself.

$script:gh = (Get-Command gh -ErrorAction SilentlyContinue).Source
if (-not $script:gh) { $script:gh = Join-Path $env:LOCALAPPDATA "Programs\gh-cli\bin\gh.exe" }
if (-not (Test-Path $script:gh)) { throw "GitHub CLI not found. Install it (winget install GitHub.cli) or drop the portable build in %LOCALAPPDATA%\Programs\gh-cli." }

function Invoke-Native([scriptblock]$Block) {
    $prev = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
    try { $out = & $Block 2>&1; return @{ Code = $LASTEXITCODE; Out = ($out | Out-String) } }
    finally { $ErrorActionPreference = $prev }
}

# Feeding git's stdin from a .NET Process does not work here (git sees an empty first line), so go through a shell
# that pipes properly: Git for Windows' own bash first, cmd as a fallback.
function Get-GitHubTokenFromGit {
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
    $authStatus = Invoke-Native { & $script:gh auth status }
    if ($authStatus.Code -ne 0) {
        $tok = Get-GitHubTokenFromGit
        if (-not $tok) { throw "Not logged in to GitHub. Run 'gh auth login' or set GH_TOKEN." }
        $env:GH_TOKEN = $tok
    }
}

# A simple function on purpose: an advanced one would try to bind gh flags such as -y or --repo as its own parameters.
function Invoke-Gh { $ghArgs = $args; return Invoke-Native { & $script:gh @ghArgs } }

# UTF-8 without BOM in, UTF-8 without BOM out: PowerShell 5.1 would otherwise read a BOM-less file as ANSI and write a BOM.
function Read-Utf8([string]$Path) { return [System.IO.File]::ReadAllText($Path, [System.Text.Encoding]::UTF8) }
function Write-Utf8([string]$Path, [string]$Text) { [System.IO.File]::WriteAllText($Path, $Text, (New-Object System.Text.UTF8Encoding($false))) }

function Assert-ReadmeRedirect([string]$Repo, [string]$ExpectVersion = "") {
    # The README download button must keep resolving to an app release, never to a pack release.
    $curl = (Get-Command curl.exe -ErrorAction SilentlyContinue).Source
    if (-not $curl) { Write-Warning "curl.exe not found; skipping the README redirect check"; return }
    $eff = (Invoke-Native { & $curl -sIL -o NUL -w "%{url_effective}" "https://github.com/$Repo/releases/latest/download/Clonedivers.exe" }).Out.Trim()
    if ($eff -notmatch '/releases/download/v\d+\.\d+\.\d+/Clonedivers\.exe($|\?)' -and $eff -notmatch 'release-assets\.githubusercontent\.com') {
        throw "README redirect check failed: releases/latest/download/Clonedivers.exe resolves to '$eff'"
    }
    if ($ExpectVersion) {
        $view = Invoke-Gh release view "v$ExpectVersion" --repo $Repo --json tagName,isDraft,isPrerelease
        if ($view.Code -ne 0) { throw "release v$ExpectVersion not found: $($view.Out)" }
        $latest = Invoke-Gh release list --repo $Repo --limit 20
        if ($latest.Out -notmatch "v$([regex]::Escape($ExpectVersion))\s") { throw "release v$ExpectVersion is not in the release list" }
        if (($latest.Out -split "`n" | Where-Object { $_ -match "\sLatest\s" } | Out-String) -notmatch "v$([regex]::Escape($ExpectVersion))") { throw "release v$ExpectVersion is not marked Latest" }
    }
}
