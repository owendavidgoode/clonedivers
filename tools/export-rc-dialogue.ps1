# Export original RC voice archives using the SWRC community UCC commandlet.
# Uses a disposable runtime copy: never installs tools into or changes the game.
# Obtain UCC.exe from https://github.com/SWRC-Modding/CT/releases
param(
    [Parameter(Mandatory = $true)][string]$GameData,
    [Parameter(Mandatory = $true)][string]$Ucc,
    [string]$OutDir = (Join-Path $PSScriptRoot '..\dist\rc-dialogue')
)
$ErrorActionPreference = 'Stop'
$GameData = (Resolve-Path -LiteralPath $GameData).Path
$Ucc = (Resolve-Path -LiteralPath $Ucc).Path
$OutDir = [IO.Path]::GetFullPath($OutDir)
if (Test-Path -LiteralPath $OutDir) { throw 'Choose a new output directory; existing exports are never overwritten.' }
if (-not (Test-Path -LiteralPath (Join-Path $GameData 'System\Core.dll'))) { throw 'GameData must be the original Republic Commando GameData directory.' }
$packages = @(Get-ChildItem -LiteralPath (Join-Path $GameData 'Sounds') -Filter '*voice.uax' -File)
if (-not $packages.Count) { throw 'No RC voice archives found.' }
$runtime = Join-Path $OutDir 'runtime'
foreach ($folder in @('System', 'Sounds', 'Properties')) {
    New-Item -ItemType Directory -Path (Join-Path $runtime $folder) -Force | Out-Null
}
Get-ChildItem -LiteralPath (Join-Path $GameData 'System') -File |
    Where-Object { $_.Extension -in '.dll', '.u', '.ini', '.int' } |
    Copy-Item -Destination (Join-Path $runtime 'System')
Copy-Item -LiteralPath $Ucc -Destination (Join-Path $runtime 'System\UCC.exe')
Get-ChildItem -LiteralPath (Join-Path $GameData 'Properties') -File |
    Copy-Item -Destination (Join-Path $runtime 'Properties')
$packages | Copy-Item -Destination (Join-Path $runtime 'Sounds')
$characters = @{ Delta_07 = 'Sev'; Delta_38 = 'Boss'; Delta_40 = 'Fixer'; Delta_62 = 'Scorch' }
$catalog = [Collections.Generic.List[object]]::new()
$reports = [Collections.Generic.List[object]]::new()
foreach ($package in $packages) {
    $audioDir = Join-Path $OutDir ('audio\' + $package.BaseName)
    New-Item -ItemType Directory -Path $audioDir -Force | Out-Null
    $logPath = Join-Path $OutDir ($package.BaseName + '.log')
    Push-Location (Join-Path $runtime 'System')
    try {
        & '.\UCC.exe' batchexport $package.Name sound wav $audioDir > $logPath 2>&1
        $code = $LASTEXITCODE
    } finally { Pop-Location }
    $lines = @(Get-Content -LiteralPath $logPath)
    # UCC returns 1 for warnings as well as errors. Require its explicit zero-error
    # summary, then validate every exported Sound payload below.
    if ($code -notin 0, 1 -or -not ($lines -match '^Success - 0 error')) { throw "UCC failed for $($package.Name); see $logPath" }
    $count = 0
    foreach ($line in $lines) {
        # SoundMultiple exports are empty selector objects, not playable clips.
        if ($line -notmatch '^Exported Sound (\S+) to (.+)$') { continue }
        $object = $Matches[1]; $file = $Matches[2]
        $relative = [IO.Path]::GetFullPath($file)
        if (-not $relative.StartsWith($audioDir + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Export escaped audio directory: $file"
        }
        $stream = [IO.File]::OpenRead($file)
        try {
            $header = New-Object byte[] 12
            if ($stream.Length -lt 44 -or $stream.Read($header, 0, 12) -ne 12 -or
                [Text.Encoding]::ASCII.GetString($header, 0, 4) -ne 'RIFF' -or
                [Text.Encoding]::ASCII.GetString($header, 8, 4) -ne 'WAVE') {
                throw "Invalid WAV export: $file"
            }
            $size = $stream.Length
        } finally { $stream.Dispose() }
        $group = ($object -split '\.')[1]
        $character = if ($characters.ContainsKey($group)) { $characters[$group] } else { $null }
        $catalog.Add([pscustomobject]@{
            package = $package.Name; object = $object; group = $group; character = $character
            file = $file; size = $size; sha256 = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant()
        })
        $count++
    }
    $reports.Add([pscustomobject]@{
        package = $package.Name; sha256 = (Get-FileHash -LiteralPath $package.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        clips = $count; result = @($lines -match 'Success - 0 error')[-1]
    })
    Write-Host "$($package.Name): $count WAV clips"
}
$catalog.ToArray() | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $OutDir 'catalog.json') -Encoding UTF8
$reports.ToArray() | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $OutDir 'sources.json') -Encoding UTF8
Write-Host "Exported $($catalog.Count) WAV clips. Non-audio dependency warnings are retained in the logs."
Write-Host 'These are source clips, not an installable Helldivers 2 voice pack.'
