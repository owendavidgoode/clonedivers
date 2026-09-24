param(
    [string]$Candidate='dist/vehicle-update-2026-09-23/feed/manifest.json',
    [string]$Comparison='dist/compat-7.1.1/comparison.json',
    [string]$Output='dist/release-1.6.0-inputs/feed/manifest.json',
    [string]$Version='2026.09.24-r10'
)
$ErrorActionPreference='Stop'
if (Test-Path -LiteralPath $Output) { throw 'Use a fresh output manifest.' }
$audit=Get-Content -LiteralPath $Comparison -Raw | ConvertFrom-Json
if ($audit.build -ne '25480438' -or @($audit.banks).Count -ne 65 -or
    @($audit.banks | Where-Object { !$_.unchanged }).Count -ne 0 -or
    !$audit.englishTextUnchanged -or !$audit.watcherUnchanged) { throw 'Hotfix comparison did not pass.' }
$manifest=Get-Content -LiteralPath $Candidate -Raw | ConvertFrom-Json
if ($manifest.format -ne 3 -or !$manifest.pack.combinedRoster) { throw 'Expected combined profile candidate.' }
$known=@{}
foreach ($feed in @('manifest.json','manifest-v3.json')) {
    $old=Get-Content -LiteralPath $feed -Raw | ConvertFrom-Json
    foreach ($file in $old.pack.files) { if ($file.size -gt 0) { $known[$file.sha256]=$file } }
}
$assetDir=Join-Path (Split-Path $Candidate) 'assets'
foreach ($file in $manifest.pack.files) {
    # Slots are reused by unrelated optional assets; identify the experiment by bytes.
    if ($file.sha256 -in @('1b58e8af7b6dedea18a3ff4499106819430eb78bd2e9baaaedd9bc92442def8',
        'da251c885f7e5d1e2c0fbb176b2ee1186f70433fd7a2505482b541f8401caa16')) { throw 'Camera diagnostic patch in candidate.' }
    if ($file.size -eq 0) { $file.url=''; continue }
    if ($known.ContainsKey($file.sha256)) {
        if ($known[$file.sha256].size -ne $file.size) { throw 'Known asset size mismatch.' }
        $file.url=$known[$file.sha256].url
    } else {
        $path=Join-Path $assetDir $file.sha256
        if ((Get-Item -LiteralPath $path).Length -ne $file.size -or
            (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $file.sha256) { throw 'New asset failed verification.' }
        $file.url="https://github.com/owendavidgoode/clonedivers/releases/download/pack-$Version-files/$($file.sha256)"
    }
}
$manifest.pack.version=$Version
$manifest.pack.gameBuild=$audit.build
$manifest.pack.gameDepots=@('1883032874832282552','2566883558412717494','9065746766815828060','1417949467559619757','3382210389809902281')
($manifest.pack.options | Where-Object id -EQ commandos).description='Delta Squad is elite.'
$manifest.pack.statusNotes='Startup and ADS checked locally. Supply FRV and Watcher audio are experimental; see release notes.'
$json=$manifest | ConvertTo-Json -Depth 20
if ($json -match '127\.0\.0\.1|localhost') { throw 'Local URL remains in candidate.' }
New-Item -ItemType Directory -Force (Split-Path $Output) | Out-Null
[IO.File]::WriteAllText([IO.Path]::GetFullPath($Output), $json + [Environment]::NewLine)
[ordered]@{
    candidateSha256=(Get-FileHash -LiteralPath $Candidate).Hash
    comparisonSha256=(Get-FileHash -LiteralPath $Comparison).Hash
    releaseSha256=(Get-FileHash -LiteralPath $Output).Hash
    version=$Version
    experimentalApproved=@('Supply FRV mesh port','Watcher probe loop')
    gameplay='Startup on 7.1.1; ADS passed prior 7.1 candidate. Other checks remain listed in release notes.'
} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path (Split-Path $Output) 'promotion.json')
Write-Output "Prepared $Output; file contents and selection predicates preserved."
