# Optional support capture. No game/settings/protection changes or uploads.
param(
    [Parameter(Mandatory)][ValidatePattern('^[a-zA-Z0-9_-]+$')][string]$Label,
    [Parameter(Mandatory)][ValidateSet('ship','traversal','combat')][string]$Scene,
    [switch]$GameplayReady,
    [switch]$ExtendedCounters,
    [ValidateRange(1,10)][int]$Repeats=1,
    [ValidateRange(10,300)][int]$Seconds=90,
    [ValidateRange(0,60)][int]$DelaySeconds=5,
    [string]$OutDir=(Join-Path $PSScriptRoot '..\dist\benchmarks'),
    [string]$PresentMon=(Join-Path $PSScriptRoot '..\dist\PresentMon.exe')
)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'measure-game-core.ps1')
if (-not $GameplayReady) { throw 'Warm up the scene, then pass -GameplayReady for the explicitly requested capture. Menus/startup are not benchmarks.' }
if (-not (Test-Path -LiteralPath $PresentMon -PathType Leaf)) { throw 'PresentMon unavailable; no automatic download.' }
$PresentMon=[IO.Path]::GetFullPath($PresentMon); $OutDir=[IO.Path]::GetFullPath($OutDir)
New-Item -ItemType Directory -Path $OutDir -Force | Out-Null
$hardware=[ordered]@{cpu='unavailable';physicalRamBytes=$null;windows=[Environment]::OSVersion.Version.ToString();logicalProcessors=[Environment]::ProcessorCount;gpu='See launcher DXGI diagnostics; capacity not collected here'}
try { $hardware.cpu=(Get-ItemProperty 'HKLM:\HARDWARE\DESCRIPTION\System\CentralProcessor\0').ProcessorNameString } catch { }
try { $hardware.physicalRamBytes=(Get-CimInstance Win32_ComputerSystem -OperationTimeoutSec 2).TotalPhysicalMemory } catch { }
for ($run=1; $run -le $Repeats; $run++) {
    if ($run -gt 1) { Read-Host 'Return to the same warmed scene and press Enter' | Out-Null }
    $game=Get-Process helldivers2 -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $game) { throw 'Game not running; no capture started.' }
    $session='ClonediversBenchmark-'+[guid]::NewGuid().ToString('N')
    $prefix=Join-Path $OutDir ($Label+'-'+(Get-Date -Format 'yyyyMMdd-HHmmss')+'-'+$run)
    $csv=$prefix+'-frames.csv';$capture=$null;$rows=[Collections.Generic.List[object]]::new()
    $context=[ordered]@{label=$Label;scene=$Scene;gameplayReady=$true;requestedSeconds=$Seconds;delaySeconds=$DelaySeconds;run=$run;extendedCounters=[bool]$ExtendedCounters;hardware=$hardware;settings=(Get-SafeBenchmarkSettings);presentMonSha256=(Get-FileHash -LiteralPath $PresentMon).Hash.ToLowerInvariant();presentMonFileVersion=(Get-Item -LiteralPath $PresentMon).VersionInfo.FileVersion;startUtc=[DateTime]::UtcNow.ToString('o');counterLimitations='Unavailable counters=null. System memory and disk counters are machine-wide. GPU memory totals cover matching game PID instances and may span adapters. CPU normalized across logical cores cannot exclude a busy main thread. No disk latency or clock telemetry. Compare collection overhead before interpreting weak-PC results.'}
    $context | ConvertTo-Json -Depth 6 | Set-Content ($prefix+'-context.json')
    try {
        $capture=Start-Process -FilePath $PresentMon -ArgumentList @('--process_id',$game.Id,'--output_file',('"'+$csv+'"'),'--timed',$Seconds,'--delay',$DelaySeconds,'--date_time','--terminate_after_timed','--no_console_stats','--session_name',$session) -WindowStyle Hidden -PassThru -RedirectStandardOutput ($prefix+'-capture.log') -RedirectStandardError ($prefix+'-capture.err')
        $timer=[Diagnostics.Stopwatch]::StartNew();$lastCpu=$game.TotalProcessorTime.TotalSeconds;$lastTime=0.0
        while ($timer.Elapsed.TotalSeconds -lt $Seconds+$DelaySeconds) {
            Start-Sleep -Seconds 1
            $game.Refresh()
            if ($game.HasExited -or $capture.HasExited) { break }
            $elapsed=$timer.Elapsed.TotalSeconds;$cpu=$game.TotalProcessorTime.TotalSeconds;$memory=$null
            try { $memory=Get-CimInstance Win32_PerfFormattedData_PerfOS_Memory -OperationTimeoutSec 2 } catch { }
            $gpuDedicated=$null;$gpuShared=$null;$diskRead=$null;$diskQueue=$null
            if ($ExtendedCounters) {
                try {
                    $gpu=@(Get-CimInstance Win32_PerfFormattedData_GPUPerformanceCounters_GPUProcessMemory -OperationTimeoutSec 2 | Where-Object { $_.Name -match ('^pid_'+$game.Id+'_') })
                    if ($gpu.Count) { $gpuDedicated=($gpu | Measure-Object DedicatedUsage -Sum).Sum;$gpuShared=($gpu | Measure-Object SharedUsage -Sum).Sum }
                } catch { }
                try {
                    $disk=Get-CimInstance Win32_PerfFormattedData_PerfDisk_PhysicalDisk -Filter "Name='_Total'" -OperationTimeoutSec 2
                    $diskRead=$disk.DiskReadBytesPersec;$diskQueue=$disk.CurrentDiskQueueLength
                } catch { }
            }
            $rows.Add([pscustomobject][ordered]@{utc=[DateTime]::UtcNow.ToString('o');seconds=$elapsed;workingSetBytes=$game.WorkingSet64;privateBytes=$game.PrivateMemorySize64;cpuPercentAllCores=100*($cpu-$lastCpu)/($elapsed-$lastTime)/[Environment]::ProcessorCount;availableMBytes=$memory.AvailableMBytes;committedBytes=$memory.CommittedBytes;commitLimitBytes=$memory.CommitLimit;pagesInputPerSec=$memory.PagesInputPersec;pagesOutputPerSec=$memory.PagesOutputPersec;gameGpuDedicatedBytes=$gpuDedicated;gameGpuSharedBytes=$gpuShared;systemDiskReadBytesPerSec=$diskRead;systemDiskQueue=$diskQueue})
            $lastCpu=$cpu;$lastTime=$elapsed
        }
        if (-not $capture.WaitForExit(5000)) { throw 'Capture exceeded timed window.' }
        if ($game.HasExited) { throw 'Game exited; sample incomplete.' }
        if ($capture.ExitCode -ne 0) { throw "PresentMon failed (exit $($capture.ExitCode)); inspect capture.err. Use documented ETW tracing privileges if denied; do not change game protection." }
        $frames=if (Test-Path -LiteralPath $csv) { @(Import-Csv -LiteralPath $csv) } else { @() }
        $stats=Get-FrameSummary $frames
        [ordered]@{label=$Label;scene=$Scene;run=$run;frameStatistics=$stats;memorySamples=$rows.Count;sceneValidity='User-marked gameplay. Review loading/menu/alt-tab sections before comparison.'} | ConvertTo-Json -Depth 5 | Set-Content ($prefix+'-summary.json')
        $stats | ConvertTo-Json -Depth 4 | Write-Output
    } catch {
        [ordered]@{label=$Label;scene=$Scene;run=$run;valid=$false;reason='Capture incomplete or invalid; no performance conclusion. Inspect local capture output.'} | ConvertTo-Json | Set-Content ($prefix+'-failure.json')
        throw
    } finally {
        try { $rows | Export-Csv ($prefix+'-memory.csv') -NoTypeInformation } catch { Write-Warning 'Memory CSV could not be saved.' }
        if ($null -ne $capture) {
            $stop=$null
            try {
                # Unique session name ensures cleanup never targets another profiler.
                $stop=Start-Process -FilePath $PresentMon -ArgumentList @('--session_name',$session,'--terminate_existing_session','--no_console_stats') -WindowStyle Hidden -PassThru -RedirectStandardOutput ($prefix+'-cleanup.log') -RedirectStandardError ($prefix+'-cleanup.err')
                if (-not $stop.WaitForExit(5000)) { $stop.Kill() }
                if (-not $capture.WaitForExit(5000)) { $capture.Kill();Write-Warning 'Capture process stopped; inspect ETW cleanup output.' }
            } catch { Write-Warning 'ETW cleanup could not be confirmed; inspect named-session cleanup output.' }
            finally { if ($null -ne $stop) { $stop.Dispose() };$capture.Dispose() }
        }
        $game.Dispose()
    }
}
