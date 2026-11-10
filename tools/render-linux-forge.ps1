param([string]$Output = 'D:\ClaudeWork\ui-shots\linux-after')
$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:AVALONIA_TELEMETRY_OPTOUT = '1'
$dotnet = Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet\dotnet.exe'
if (!(Test-Path -LiteralPath $dotnet)) { $dotnet = 'dotnet' }
$repository = Split-Path $PSScriptRoot -Parent
& $dotnet build (Join-Path $repository 'Clonedivers.Linux') -c Release --no-restore --nologo
if ($LASTEXITCODE) { throw 'Linux desktop build failed.' }
$binary = Join-Path $repository 'Clonedivers.Linux\bin\Release\net8.0\clonedivers.dll'
foreach ($mode in @('regular', 'clonedivers', 'empire')) {
    $states = if ($mode -eq 'clonedivers') { @('ready', 'setup', 'running', 'sync', 'recovery', 'error', 'disconnected', 'installing', 'update-available', 'update-checking', 'update-failed', 'details') } else { @('ready') }
    foreach ($state in $states) {
        & $dotnet $binary --gui --forge-preview $state --forge-preview-mode $mode --forge-render $Output
        if ($LASTEXITCODE) { throw "Render failed: $state / $mode" }
    }
    foreach ($state in @('ready', 'update-available')) {
        & $dotnet $binary --gui --forge-preview $state --forge-preview-mode $mode --forge-preview-size 700x600 --forge-render (Join-Path $Output 'minimum')
        if ($LASTEXITCODE) { throw "Minimum-size render failed: $state / $mode" }
    }
}
