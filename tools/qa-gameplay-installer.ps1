param([string]$Dotnet = 'dotnet', [string]$TestAssembly = 'Clonedivers.Tests/bin/Release/net8.0-windows/Clonedivers.Tests.dll')
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$base = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$target = Join-Path $base ('forge-gameplay-install-test-' + [guid]::NewGuid().ToString('N'))
if (-not $target.StartsWith($base, [StringComparison]::OrdinalIgnoreCase)) { throw 'Fixture root escaped temporary storage.' }
$installer = Join-Path $base 'clonedivers-pinned-HellForge-Setup.exe'
$expected = 'b815655998d6b4b31cd12e821460cd0bbde5e360c2886381ca59e051a106a7e4'
if (-not (Test-Path -LiteralPath $installer) -or (Get-Item -LiteralPath $installer).Length -ne 315829864 -or
    (Get-FileHash -LiteralPath $installer).Hash.ToLowerInvariant() -ne $expected) {
    Invoke-WebRequest 'https://github.com/SkyeShade/HellForge/releases/download/v0.3.0-beta/HellForge-Setup.exe' -OutFile $installer
}
if ((Get-Item -LiteralPath $installer).Length -ne 315829864 -or (Get-FileHash -LiteralPath $installer).Hash.ToLowerInvariant() -ne $expected) { throw 'Official test installer verification failed.' }
$start = [Diagnostics.ProcessStartInfo]::new($installer)
$start.UseShellExecute = $false; $start.CreateNoWindow = $true
$start.ArgumentList.Add('--unpack'); $start.ArgumentList.Add($target)
$process = [Diagnostics.Process]::Start($start)
try { $process.WaitForExit(); if ($process.ExitCode -ne 0) { throw 'Official test installation failed.' } }
finally { $process.Dispose() }
$cli = Join-Path $target 'app/cli/HellForge.Cli.exe'
foreach ($manifest in Get-ChildItem (Join-Path $target 'app/bundled-mods') -Filter hellforge-mod.json -Recurse) {
    & $cli mod-add --file $manifest.FullName | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Official bundled mod import failed.' }
}
& $Dotnet $TestAssembly --gameplay-install-real $target
if ($LASTEXITCODE -ne 0) { throw 'Clean real-CLI dependency installation failed.' }
& $Dotnet $TestAssembly --gameplay-recovery-real $target
if ($LASTEXITCODE -ne 0) { throw 'Real-CLI gameplay recovery/repair tests failed.' }
Write-Output 'Real official imports, rollback, restart recovery, no-op and repair passed in an isolated installation.'
Write-Output 'No game copy, ownership, authentication, network-rule setup, account import or multiplayer was tested.'
# Retain the isolated root for diagnosis. Never recursively clean a computed path.
