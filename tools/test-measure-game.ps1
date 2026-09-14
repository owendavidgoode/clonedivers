$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'measure-game-core.ps1')
$checks=0
function Check($ok,$name) { if (-not $ok) { throw $name }; $script:checks++; Write-Output "PASS $name" }
$frames=@(1..200 | ForEach-Object { [pscustomobject]@{SwapChainAddress='main';MsBetweenPresents=10} })
$frames+=@(1..10 | ForEach-Object { [pscustomobject]@{SwapChainAddress='other';MsBetweenPresents=1000} })
$summary=Get-FrameSummary $frames
Check ($summary.meanFps -eq 100 -and $summary.validIntervals -eq 200) 'Primary swapchain excludes auxiliary presents'
Check ($summary.p99Ms -eq 10 -and $summary.over50Ms -eq 0) 'Nearest-rank percentiles and long frame counts'
$frames[0].MsBetweenPresents=200
$summary=Get-FrameSummary $frames
Check ($summary.over100Ms -eq 1 -and $summary.over50Ms -eq 1) 'Long frame retained'
Check ([math]::Abs($summary.over100MsPerMinute-60/$summary.intervalSeconds) -lt .00001) 'Long-frame rates normalize unequal capture lengths'
$failed=$false;try { Get-FrameSummary @([pscustomobject]@{Unexpected=42}) } catch {$failed=$true}
Check $failed 'Insufficient rows rejected'
$failed=$false;try { Get-FrameSummary @(1..200 | ForEach-Object {[pscustomobject]@{Unexpected=42}}) } catch {$failed=$true}
Check $failed 'Unknown CSV schema rejected'
$other=Get-FrameSummary @(1..200 | ForEach-Object {[pscustomobject]@{CPUFrameTime='16.5'}})
Check ($other.intervalField -eq 'CPUFrameTime' -and $other.p95Ms -eq 16.5) 'Explicit v2 schema accepted'
Check ($null -eq $other.meanGpuBusyMs) 'Missing GPU timing is unavailable, not zero'
$gpu=Get-FrameSummary @(1..200 | ForEach-Object {[pscustomobject]@{CPUFrameTime='16.5';GPUBusy='12.5'}})
Check ($gpu.meanGpuBusyMs -eq 12.5) 'GPU busy timing uses explicit documented column'
$fixture=Join-Path ([IO.Path]::GetTempPath()) ('clonedivers-diagnostics-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $fixture | Out-Null
try {
    '{"GamePath":"C:\\Users\\SecretName","ManifestUrl":"https://secret.invalid","InstalledPackVersion":"2026.09.12-r8","Options":{"skinny":true}}' | Set-Content (Join-Path $fixture 'config.json')
    "vertical_fov = 85`ntexture_quality = 3`nframerate_limit = 144`nframerate_limit_enabled = false`nupscaling_quality = 5`nobject_lod_quality = 2`nscreen_resolution = [`n1920`n1080`n]`naccount = SecretName" | Set-Content (Join-Path $fixture 'game.config')
    $settings=Get-SafeBenchmarkSettings (Join-Path $fixture 'config.json') (Join-Path $fixture 'game.config')
    $json=$settings | ConvertTo-Json -Depth 5
    Check (-not $json.Contains('SecretName') -and -not $json.Contains('secret.invalid')) 'Context allowlist excludes identifiers and paths'
    Check ($settings.gameSettings.vertical_fov -eq '85' -and $settings.launcherSettings.skinny) 'Known benchmark preferences captured'
    Check ($settings.gameSettings.framerate_limit -eq '144' -and $settings.gameSettings.framerate_limit_enabled -eq 'false' -and $settings.gameSettings.upscaling_quality -eq '5' -and $settings.gameSettings.object_lod_quality -eq '2' -and $settings.gameSettings.screen_resolution -eq '[ 1920 1080 ]') 'Actual HD2 cap, upscaling, LOD and multiline resolution settings captured'
    $context=@{scene='traversal';gameplayReady=$true;hardware=@{cpu='Fixture';physicalRamBytes=16000000000;logicalProcessors=8;windows='fixture'};settings=$settings;extendedCounters=$false;presentMonSha256='fixture'}
    foreach ($label in @('baseline','candidate')) {
        $prefix=Join-Path $fixture $label
        @{scene='traversal'} | ConvertTo-Json | Set-Content ($prefix+'-summary.json')
        $context | ConvertTo-Json -Depth 8 | Set-Content ($prefix+'-context.json')
        $interval=if ($label -eq 'baseline') {20} else {16}
        1..3000 | ForEach-Object {[pscustomobject]@{CPUFrameTime=$interval}} | Export-Csv ($prefix+'-frames.csv') -NoTypeInformation
    }
    $compare=Join-Path $PSScriptRoot 'compare-game-captures.ps1'
    $basePath=Join-Path $fixture 'baseline-summary.json';$trialPath=Join-Path $fixture 'candidate-summary.json'
    $result= & $compare -Baseline $basePath -Candidate $trialPath -ComparableScenes | ConvertFrom-Json
    Check ($result.metrics.meanFps.medianChangePercent -eq 25 -and -not $result.repeatedEnough) 'Comparison recomputes frames and marks single runs preliminary'
    $failed=$false;try { & $compare -Baseline @($basePath,$basePath) -Candidate $trialPath -ComparableScenes } catch {$failed=$true}
    Check $failed 'Repeated capture paths cannot inflate independent run counts'
    $failed=$false;try { & $compare -Baseline $basePath -Candidate $trialPath } catch {$failed=$true}
    Check $failed 'Unreviewed scene comparison rejected'
    $context.settings.gameSettings.vertical_fov='75'
    $context | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $fixture 'candidate-context.json')
    $failed=$false;try { & $compare -Baseline $basePath -Candidate $trialPath -ComparableScenes } catch {$failed=$true}
    Check $failed 'Changed graphics setting rejects texture-only comparison'
    '{}' | Set-Content (Join-Path $fixture 'candidate-failure.json')
    $failed=$false;try { & $compare -Baseline $basePath -Candidate $trialPath -ComparableScenes } catch {$failed=$true}
    Check $failed 'Failed capture cannot become benchmark evidence'
} finally {
    $resolved=[IO.Path]::GetFullPath($fixture);$tempRoot=[IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\')+'\'
    if (-not $resolved.StartsWith($tempRoot,[StringComparison]::OrdinalIgnoreCase)) {throw 'Unsafe fixture cleanup path'}
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
$tokens=$null;$errors=$null
[Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot 'measure-game.ps1'),[ref]$tokens,[ref]$errors) | Out-Null
Check ($errors.Count -eq 0) 'Capture script parses without execution'
Write-Output "$checks measurement checks passed."
