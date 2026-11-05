param(
    [Parameter(Mandatory=$true)][string]$SourceRoot,
    [Parameter(Mandatory=$true)][string]$OutputFolder
)
$ErrorActionPreference = 'Stop'
$sourcePath = [IO.Path]::GetFullPath($SourceRoot)
$outputPath = [IO.Path]::GetFullPath($OutputFolder)
if (Test-Path -LiteralPath $outputPath) { throw 'Choose a new staging folder; existing content is never overwritten.' }
for ($ancestor = [IO.DirectoryInfo]::new($outputPath); $null -ne $ancestor; $ancestor = $ancestor.Parent) {
    if ($ancestor.Name -eq 'steamapps' -or (Test-Path -LiteralPath (Join-Path $ancestor.FullName 'hellforge-install.json'))) {
        throw 'Stage outside Steam and installed HellForge folders.'
    }
    if ((Test-Path -LiteralPath $ancestor.FullName) -and ((Get-Item -LiteralPath $ancestor.FullName -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
        throw 'Linked staging paths are unsupported.'
    }
}
$pin = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'hellforge-linux/upstream-pin.json') -Raw | ConvertFrom-Json
foreach ($file in $pin.Scripts) {
    $from = Join-Path $sourcePath ('linux/' + $file.Name)
    if ((Get-FileHash -LiteralPath $from -Algorithm SHA256).Hash.ToLowerInvariant() -ne $file.UpstreamSha256) {
        throw ('Pinned upstream script mismatch: ' + $file.Name)
    }
}
$original = [IO.File]::ReadAllText((Join-Path $sourcePath 'linux/hf-inner.sh')).Replace("`r`n", "`n")
$marker = 'pat=''HellForge\.(Launcher|Session|Cli)|LocalBackend|helldivers2'''
$oldStart = 'for _ in $(seq 1 30); do pgrep -u "$(id -u)" -f "$pat" >/dev/null && break; sleep 1; done'
$oldWait = 'while pgrep -u "$(id -u)" -f "$pat" >/dev/null; do sleep 2; done'
foreach ($expected in @($marker, $oldStart, $oldWait)) {
    if (-not $original.Contains($expected)) { throw 'Unexpected pinned lifetime source; no patch applied.' }
}
$helper = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'hellforge-linux/lifetime-functions.sh')).Replace("`r`n", "`n")
$patched = $original.Replace($marker, $helper + "`n" + $marker).
    Replace($oldStart, 'for _ in $(seq 1 30); do hf_session_processes_running && break; sleep 1; done').
    Replace($oldWait, 'while hf_session_processes_running; do sleep 2; done')
New-Item -ItemType Directory -Path $outputPath | Out-Null
$records = foreach ($file in $pin.Scripts) {
    $target = Join-Path $outputPath $file.Name
    if ($file.Name -eq 'hf-inner.sh') { [IO.File]::WriteAllText($target, $patched, [Text.UTF8Encoding]::new($false)) }
    else { Copy-Item -LiteralPath (Join-Path $sourcePath ('linux/' + $file.Name)) -Destination $target }
    [pscustomobject]@{ Name=$file.Name; UpstreamSha256=$file.UpstreamSha256; BundleSha256=(Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash.ToLowerInvariant() }
}
Copy-Item -LiteralPath (Join-Path $sourcePath 'LICENSE') -Destination (Join-Path $outputPath 'COPYING')
[pscustomobject]@{
    Format=1; Source=$pin.Source; SourceCommit=$pin.SourceCommit; InstalledComparison=$pin.InstalledComparison
    Qualification='Staged script adaptation; requires shell fixtures and actual Linux runtime qualification'
    Modification='hf-inner.sh lifetime ancestor exclusion only; no namespace/network/security change'
    License='GPL-3.0; complete source scripts and COPYING retained'; Scripts=$records
} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $outputPath 'bundle.json')
Write-Output $outputPath
