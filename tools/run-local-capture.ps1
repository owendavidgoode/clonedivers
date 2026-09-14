# Bounded local capture wrapper: retain startup errors as well as measurement output.
param(
    [Parameter(Mandatory)][string]$SessionDirectory,
    [Parameter(Mandatory)][ValidatePattern('^[a-zA-Z0-9_-]+$')][string]$Label
)
$ErrorActionPreference='Stop'
$SessionDirectory=[IO.Path]::GetFullPath($SessionDirectory)
if (-not (Test-Path -LiteralPath $SessionDirectory -PathType Container)) {throw 'Create the session directory first'}
$statusPath=Join-Path $SessionDirectory ($Label+'-worker-status.json')
try {
    @{state='starting';pid=$PID;utc=[DateTime]::UtcNow.ToString('o')} | ConvertTo-Json | Set-Content -LiteralPath $statusPath -Encoding UTF8
    & (Join-Path $PSScriptRoot 'measure-game.ps1') -Label $Label -Scene combat -GameplayReady -ExtendedCounters -Seconds 90 -DelaySeconds 10 -OutDir $SessionDirectory *> (Join-Path $SessionDirectory ($Label+'-worker.log'))
    @{state='complete';pid=$PID;utc=[DateTime]::UtcNow.ToString('o')} | ConvertTo-Json | Set-Content -LiteralPath $statusPath -Encoding UTF8
} catch {
    $_ | Format-List * -Force | Out-String | Set-Content -LiteralPath (Join-Path $SessionDirectory ($Label+'-startup-error.txt')) -Encoding UTF8
    @{state='failed';pid=$PID;utc=[DateTime]::UtcNow.ToString('o');error=$_.Exception.Message} | ConvertTo-Json | Set-Content -LiteralPath $statusPath -Encoding UTF8
    exit 1
}
