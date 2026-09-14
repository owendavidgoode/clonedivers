# Offline comparison only. Does not launch, install, upload or change game settings.
param(
    [Parameter(Mandatory)][string[]]$Baseline,
    [Parameter(Mandatory)][string[]]$Candidate,
    [switch]$ComparableScenes,
    [string]$OutPath
)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'measure-game-core.ps1')
if (-not $ComparableScenes) { throw 'Review raw captures for menus/loading/alt-tab and confirm the same warmed route, loadout, squad and host role with -ComparableScenes.' }
$paths=@(@($Baseline)+@($Candidate) | ForEach-Object { [IO.Path]::GetFullPath($_).ToLowerInvariant() })
if (@($paths | Select-Object -Unique).Count -ne $paths.Count) { throw 'Each capture must be used once; repeating a path is not an independent run.' }
function Read-Capture([string]$summaryPath) {
    if (-not $summaryPath.EndsWith('-summary.json')) { throw 'Pass capture -summary.json paths.' }
    $prefix=$summaryPath.Substring(0,$summaryPath.Length-13)
    if (Test-Path -LiteralPath ($prefix+'-failure.json')) { throw 'Failed capture cannot be compared.' }
    $summary=Get-Content -LiteralPath $summaryPath -Raw | ConvertFrom-Json
    $context=Get-Content -LiteralPath ($prefix+'-context.json') -Raw | ConvertFrom-Json
    if (-not $context.gameplayReady -or $context.scene -notin @('traversal','combat')) { throw 'Only explicitly marked traversal/combat captures qualify.' }
    if ($context.scene -ne $summary.scene) { throw 'Capture context and summary disagree.' }
    # Recompute from actual frames; old zero-frame summaries cannot qualify.
    $stats=Get-FrameSummary @(Import-Csv -LiteralPath ($prefix+'-frames.csv'))
    if ($stats.intervalSeconds -lt 30 -or $stats.invalidIntervals/($stats.validIntervals+$stats.invalidIntervals) -gt .01) { throw 'Capture is too short or has over 1% invalid intervals.' }
    [pscustomobject]@{context=$context;stats=$stats}
}
function Distribution($values) {
    $sorted=@($values | Sort-Object);$count=$sorted.Count
    $middle=if ($count%2) { $sorted[[int][math]::Floor($count/2)] } else { ($sorted[$count/2-1]+$sorted[$count/2])/2 }
    [pscustomobject]@{median=$middle;min=$sorted[0];max=$sorted[-1]}
}
$base=@($Baseline | ForEach-Object { Read-Capture $_ })
$trial=@($Candidate | ForEach-Object { Read-Capture $_ })
$all=@($base)+@($trial);$reference=$all[0]
foreach ($capture in $all) {
    if ($capture.context.scene -ne $reference.context.scene) { throw 'Compare traversal and combat separately.' }
    foreach ($key in @('cpu','physicalRamBytes','logicalProcessors','windows')) {
        if ([string]$capture.context.hardware.$key -ne [string]$reference.context.hardware.$key) { throw "Hardware mismatch: $key" }
    }
    if (($capture.context.settings.gameSettings | ConvertTo-Json -Compress) -ne ($reference.context.settings.gameSettings | ConvertTo-Json -Compress)) { throw 'Graphics settings differ; this is not a texture-only comparison.' }
    if ($capture.context.settings.launcherSettings.InstalledGameBuild -ne $reference.context.settings.launcherSettings.InstalledGameBuild) { throw 'Installed game build differs.' }
    if ($capture.context.extendedCounters -ne $reference.context.extendedCounters -or $capture.context.presentMonSha256 -ne $reference.context.presentMonSha256 -or $capture.stats.intervalField -ne $reference.stats.intervalField) { throw 'Capture configuration or frame schema differs.' }
}
$metrics=[ordered]@{}
foreach ($field in @('meanFps','medianMs','p95Ms','p99Ms','over50MsPerMinute','over100MsPerMinute')) {
    $left=Distribution @($base | ForEach-Object { $_.stats.$field })
    $right=Distribution @($trial | ForEach-Object { $_.stats.$field })
    $metrics[$field]=[ordered]@{baseline=$left;candidate=$right;medianChangePercent=if ($left.median -ne 0) {100*($right.median-$left.median)/$left.median} else {$null};observedRangesOverlap=($left.max -ge $right.min -and $right.max -ge $left.min)}
}
$report=[ordered]@{
    baselineRuns=$base.Count;candidateRuns=$trial.Count;scene=$reference.context.scene
    repeatedEnough=($base.Count -ge 3 -and $trial.Count -ge 3)
    metrics=$metrics
    interpretation='FPS higher is better; interval times and long-frame rates lower are better. Medians and ranges are across runs, not pooled frames. Overlapping ranges flag variability; non-overlap is not statistical significance or proof of causation.'
    limitations='Scene equivalence is human-confirmed. GPU/driver, actual loaded pack, clocks, temperature, background load and unknown graphics settings require manual verification. No automatic performance pass or publish decision.'
}
$json=$report | ConvertTo-Json -Depth 8
if ($OutPath) { $json | Set-Content -LiteralPath $OutPath -Encoding UTF8 }
$json
