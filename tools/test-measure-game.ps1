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
    "vertical_fov = 85`ntexture_quality = 3`naccount = SecretName" | Set-Content (Join-Path $fixture 'game.config')
    $settings=Get-SafeBenchmarkSettings (Join-Path $fixture 'config.json') (Join-Path $fixture 'game.config')
    $json=$settings | ConvertTo-Json -Depth 5
    Check (-not $json.Contains('SecretName') -and -not $json.Contains('secret.invalid')) 'Context allowlist excludes identifiers and paths'
    Check ($settings.gameSettings.vertical_fov -eq '85' -and $settings.launcherSettings.skinny) 'Known benchmark preferences captured'
} finally {
    $resolved=[IO.Path]::GetFullPath($fixture);$tempRoot=[IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\')+'\'
    if (-not $resolved.StartsWith($tempRoot,[StringComparison]::OrdinalIgnoreCase)) {throw 'Unsafe fixture cleanup path'}
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
$tokens=$null;$errors=$null
[Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot 'measure-game.ps1'),[ref]$tokens,[ref]$errors) | Out-Null
Check ($errors.Count -eq 0) 'Capture script parses without execution'
Write-Output "$checks measurement checks passed."
