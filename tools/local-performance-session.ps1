# Local, bounded capture worker. Started elevated once for Windows ETW permission.
# The only accepted requests are three fixed capture marker filenames.
param([Parameter(Mandatory)][string]$SessionDirectory)
$ErrorActionPreference='Stop'
$SessionDirectory=[IO.Path]::GetFullPath($SessionDirectory)
if (-not (Test-Path -LiteralPath $SessionDirectory -PathType Container)) {throw 'Create a fresh session directory first'}
$statusPath=Join-Path $SessionDirectory 'status.json'
function Status([string]$state,[string]$phase) {
    @{state=$state;phase=$phase;utc=[DateTime]::UtcNow.ToString('o');pid=$PID} | ConvertTo-Json | Set-Content -LiteralPath $statusPath -Encoding UTF8
}
try {
    $principal=[Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {throw 'Windows administrator token required for this capture worker'}
    $presentMon=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../dist/PresentMon.exe'))
    $sessionName='ClonediversPreflight-'+[guid]::NewGuid().ToString('N')
    Status 'preflight' 'permission'
    & $presentMon --process_id $PID --timed 5 --terminate_after_timed --no_console_stats --session_name $sessionName --output_file (Join-Path $SessionDirectory 'permission.csv') *> (Join-Path $SessionDirectory 'permission.log')
    if ($LASTEXITCODE -ne 0) {throw 'ETW preflight failed; inspect permission.log'}
    $deadline=[DateTime]::UtcNow.AddMinutes(45)
    foreach ($label in @('hmp-a','original-dropship','hmp-b')) {
        Status 'waiting' $label
        $request=Join-Path $SessionDirectory ($label+'.request')
        while (-not (Test-Path -LiteralPath $request)) {
            if ((Test-Path -LiteralPath (Join-Path $SessionDirectory 'stop.request')) -or [DateTime]::UtcNow -ge $deadline) {Status 'stopped' $label;return}
            Start-Sleep -Seconds 1
        }
        Status 'capturing' $label
        & (Join-Path $PSScriptRoot 'measure-game.ps1') -Label $label -Scene combat -GameplayReady -ExtendedCounters -Seconds 90 -DelaySeconds 5 -OutDir $SessionDirectory -PresentMon $presentMon *> (Join-Path $SessionDirectory ($label+'-worker.log'))
        Status 'captured' $label
    }
    Status 'complete' 'all'
} catch {
    $_.Exception.Message | Set-Content -LiteralPath (Join-Path $SessionDirectory 'worker-error.txt') -Encoding UTF8
    Status 'failed' 'inspect-worker-error'
    exit 1
}
