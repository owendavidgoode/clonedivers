param(
    [Parameter(Mandatory=$true)][string]$WindowsBinary,
    [Parameter(Mandatory=$true)][string]$LinuxBinary,
    [Parameter(Mandatory=$true)][string]$CompanionFolder,
    [Parameter(Mandatory=$true)][string]$PresetFile,
    [Parameter(Mandatory=$true)][string]$OutputFolder,
    [string]$NugetRoot = (Join-Path $env:USERPROFILE '.nuget/packages')
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$output = [IO.Path]::GetFullPath($OutputFolder)
function RequirePlainPath([string]$path) {
    for ($part = [IO.Path]::GetFullPath($path); $part; $part = [IO.Path]::GetDirectoryName($part)) {
        if ((Test-Path -LiteralPath $part) -and ((Get-Item -LiteralPath $part -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Linked packaging paths are unsupported.' }
    }
}
RequirePlainPath $output
if (Test-Path -LiteralPath $output) { throw 'Choose a new output folder; nothing is overwritten.' }
for ($parent = [IO.DirectoryInfo]::new($output); $null -ne $parent; $parent = $parent.Parent) {
    if ($parent.Name -eq 'steamapps' -or (Test-Path -LiteralPath (Join-Path $parent.FullName 'hellforge-install.json'))) { throw 'Package outside installed Forge and Steam.' }
}
$inputs = @(
    @{ Path=$WindowsBinary; Hash='c56da4c0b2943361caf4da47930cee974eab628a742458d4e01c25136a2762cb' },
    @{ Path=$LinuxBinary; Hash='a30a55d1341dbef9aacb57588c8d6d6798d49c9f615c63094e91d0174f085014' },
    @{ Path=(Join-Path $CompanionFolder 'HellForge.Launcher.ClonediversBridge.exe'); Hash='478a285f5a2e7d75b074f8528e97ee2e494abcf3c5d8a554c17fffd0dd0bb4ca' }
)
foreach ($entry in $inputs) {
    RequirePlainPath $entry.Path
    if ((Get-FileHash -LiteralPath $entry.Path -Algorithm SHA256).Hash.ToLowerInvariant() -ne $entry.Hash) { throw 'Reviewed binary hash mismatch.' }
}
RequirePlainPath $PresetFile
$preset = Get-Content -LiteralPath $PresetFile -Raw | ConvertFrom-Json
if ($preset.GameBuild -ne '25480438' -or $preset.Mods.Count -ne 12 -or $preset.ContainsAccountData -ne $false) { throw 'Unexpected preset document.' }
New-Item -ItemType Directory -Path $output | Out-Null
$windows = Join-Path $output 'clonedivers-forge-windows-1.9.8-review'
$linux = Join-Path $output 'clonedivers-forge-linux-x64-0.6.0-preview.8-review'
New-Item -ItemType Directory -Path $windows,$linux | Out-Null
Copy-Item -LiteralPath $WindowsBinary -Destination (Join-Path $windows 'Clonedivers.exe')
Copy-Item -LiteralPath $LinuxBinary -Destination (Join-Path $linux 'clonedivers')
$forge = Join-Path $linux 'forge'
New-Item -ItemType Directory -Path (Join-Path $forge 'linux') -Force | Out-Null
# Explicit allowlist: never copy the install/profile/mod/session tree.
foreach ($name in @('HellForge.Launcher.ClonediversBridge.exe','companion.json','LICENSE-CLONEDIVERS.txt')) {
    $from = Join-Path $CompanionFolder $name; RequirePlainPath $from
    Copy-Item -LiteralPath $from -Destination (Join-Path $forge $name)
}
$bundlePath = Join-Path $CompanionFolder 'linux/bundle.json'; RequirePlainPath $bundlePath
$bundle = Get-Content -LiteralPath $bundlePath -Raw | ConvertFrom-Json
$scriptNames = @('hellforge-linux.sh','hellforge-offline.sh','hf-inner.sh','hf-bridge-host.sh','allow-namespace.sh')
if ($bundle.Scripts.Count -ne 5 -or @($bundle.Scripts.Name | Where-Object { $_ -notin $scriptNames }).Count -ne 0 -or @($bundle.Scripts.Name|Select-Object -Unique).Count -ne 5) { throw 'Unexpected script set.' }
foreach ($script in $bundle.Scripts) {
    $from=Join-Path $CompanionFolder ('linux/'+$script.Name); RequirePlainPath $from
    if ((Get-FileHash -LiteralPath $from).Hash.ToLowerInvariant() -ne $script.BundleSha256) { throw 'Script hash mismatch.' }
    Copy-Item -LiteralPath $from -Destination (Join-Path $forge ('linux/'+$script.Name))
}
foreach ($name in @('COPYING','bundle.json')) {
    $from=Join-Path $CompanionFolder ('linux/'+$name); RequirePlainPath $from
    Copy-Item -LiteralPath $from -Destination (Join-Path $forge ('linux/'+$name))
}
foreach ($folder in @($windows,$linux)) {
    Copy-Item -LiteralPath (Join-Path $repo 'LICENSE') -Destination (Join-Path $folder 'LICENSE-CLONEDIVERS.txt')
    Copy-Item -LiteralPath (Join-Path $repo 'docs/FORGE-TESTER-ROLLOUT.md') -Destination (Join-Path $folder 'README.md')
    Copy-Item -LiteralPath (Join-Path $repo 'docs/FORGE-MOD-COVERAGE.md') -Destination (Join-Path $folder 'FORGE-MOD-COVERAGE.md')
    Copy-Item -LiteralPath (Join-Path $repo 'docs/forge-author-receipts-20261104.json') -Destination (Join-Path $folder 'author-receipts.json')
    Copy-Item -LiteralPath $PresetFile -Destination (Join-Path $folder 'preset.json')
    New-Item -ItemType Directory -Path (Join-Path $folder 'licenses') | Out-Null
    foreach ($runtime in @('win-x64','linux-x64')) {
        if ($folder -eq $windows -and $runtime -eq 'linux-x64') { continue }
        foreach ($name in @('LICENSE.TXT','THIRD-PARTY-NOTICES.TXT')) {
            Copy-Item -LiteralPath (Join-Path $NugetRoot ("microsoft.netcore.app.runtime.$runtime/8.0.30/$name")) -Destination (Join-Path $folder ("licenses/dotnet-$runtime-$name"))
        }
    }
    if ($folder -eq $windows) {
        Copy-Item -LiteralPath (Join-Path $NugetRoot 'microsoft.windowsdesktop.app.runtime.win-x64/8.0.30/LICENSE') -Destination (Join-Path $folder 'licenses/WindowsDesktop-LICENSE.txt')
        Copy-Item -LiteralPath (Join-Path $repo 'Clonedivers/Licenses/PresentMon.txt') -Destination (Join-Path $folder 'licenses/PresentMon.txt')
    } else {
        Copy-Item -LiteralPath (Join-Path $repo 'Clonedivers.Linux/THIRD-PARTY-NOTICES.txt') -Destination (Join-Path $folder 'licenses/Avalonia-Skia-HarfBuzz-NOTICES.txt')
    }
    $records=foreach($file in (Get-ChildItem -LiteralPath $folder -File -Recurse|Sort-Object FullName)) {
        [pscustomobject]@{Name=[IO.Path]::GetRelativePath($folder,$file.FullName).Replace('\','/');Bytes=$file.Length;Sha256=(Get-FileHash -LiteralPath $file.FullName).Hash.ToLowerInvariant()}
    }
    [pscustomobject]@{Format=1;Qualification='Private review only; clean-machine mod onboarding, Linux runtime and multiplayer remain open';Files=@($records)} |
        ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $folder 'package-files.json')
}
Compress-Archive -LiteralPath $windows -DestinationPath ($windows+'.zip') -CompressionLevel Optimal
# Use package-forge-portable.sh with GNU tar for a Linux archive with explicit modes.
Write-Output $output
