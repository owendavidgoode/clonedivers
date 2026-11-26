param(
    [string]$Cli = 'D:\HellForge\app\cli\HellForge.Cli.exe',
    [string]$Source = 'D:\ClaudeWork\hellforge-src',
    [string]$Zig = 'D:\HellForge\tools\zig',
    [string]$BuildRoot = 'D:\ClaudeWork\cache\tmp\empire-tie-switch-build',
    [string]$Out = (Join-Path $PSScriptRoot 'dist'),
    [switch]$RequireFiveTiers
)
$ErrorActionPreference = 'Stop'
$mod = $PSScriptRoot
$workspace = Join-Path $BuildRoot 'workspace-0.3.1'
$stage = Join-Path $workspace 'mods\empire-tie-switch'
$sdk = Join-Path $workspace 'app\sdk'
$tools = Join-Path $workspace 'tools'
$stagedData = Join-Path $BuildRoot 'staged-data'
$assetInput = 'D:\ClaudeWork\forge-sdk\mods\empire-tie-switch\build\asset-input\data'
New-Item -ItemType Directory -Force $BuildRoot,$sdk,$tools,$stage,$Out,$stagedData | Out-Null
Get-ChildItem -LiteralPath 'D:\ClaudeWork\empire-forge\empire-forge-r45-v4' -File | ForEach-Object {
    if ($_.Name -match '^[a-f0-9]{16}\.patch_\d+(\.stream|\.gpu_resources)?$') {
        $target = Join-Path $stagedData $_.Name
        if (!(Test-Path -LiteralPath $target)) { New-Item -ItemType HardLink -Path $target -Target $_.FullName | Out-Null }
    }
}
Get-ChildItem -LiteralPath $assetInput -File | ForEach-Object {
    $target = Join-Path $stagedData ($_.Name.Replace('.patch_0','.patch_2000'))
    if (!(Test-Path -LiteralPath $target)) { Copy-Item -LiteralPath $_.FullName -Destination $target -Force }
}
Copy-Item "$Source\native\mod-sdk\*.h" $sdk -Force
$compiler = Join-Path $tools 'zig'
if (!(Test-Path -LiteralPath $compiler)) { New-Item -ItemType Junction -Path $compiler -Target $Zig | Out-Null }
if ((Get-Item -LiteralPath $compiler).Target[0] -ne $Zig) { throw 'Compiler junction target differs' }
# The installable appearance candidate owns the closure; this package is code only.
Copy-Item "$mod\src" $stage -Recurse -Force
Copy-Item "$mod\hellforge-mod.json","$mod\README.md","$mod\CREDITS.md" $stage -Force
$oldTemp = $env:TEMP; $oldTmp = $env:TMP; $oldGlobal = $env:ZIG_GLOBAL_CACHE_DIR; $oldLocal = $env:ZIG_LOCAL_CACHE_DIR
$fixture = Join-Path $BuildRoot ('fixtures-' + [guid]::NewGuid().ToString('N'))
try {
    $env:TEMP = $BuildRoot; $env:TMP = $env:TEMP
    $env:ZIG_GLOBAL_CACHE_DIR = Join-Path $tools 'zig-cache'
    $env:ZIG_LOCAL_CACHE_DIR = Join-Path $BuildRoot 'zig-local'
    & $Cli build-mod --mod $stage | Tee-Object -FilePath "$BuildRoot\build.log"
    if ($LASTEXITCODE -ne 0) { throw 'build-mod failed' }
    & "$Zig\zig.exe" cc -std=c11 -target x86_64-windows-gnu -O2 -Wall -Werror -I $sdk "$mod\tests\tie_test.c" -o "$BuildRoot\tie_test.exe" -municode
    if ($LASTEXITCODE -ne 0) { throw 'Test host compilation failed' }
    & "$BuildRoot\tie_test.exe" $stagedData $fixture | Tee-Object -FilePath "$BuildRoot\tests.log"
    if ($LASTEXITCODE -ne 0) { throw 'Regression checks failed' }
    $r52Root = 'D:\ClaudeWork\cache\tmp\tie-r52-tiers'
    if (Test-Path -LiteralPath $r52Root) {
        '' | Set-Content -LiteralPath "$BuildRoot\candidate-gate.log" -Encoding UTF8
        foreach ($tier in @('full', 'lighter', 'reduced1024', 'reduced512', 'potato256')) {
            $tierDir = Join-Path $r52Root $tier
            if (!(Test-Path -LiteralPath $tierDir)) { throw "Missing hydrated r52 tier folder: $tierDir" }
            & "$BuildRoot\tie_test.exe" --scan $tierDir | Tee-Object -FilePath "$BuildRoot\candidate-gate.log" -Append
            if ($LASTEXITCODE -ne 0) { throw "Installable r52 closure failed on tier $tier" }
        }
    } elseif ($RequireFiveTiers) {
        throw "Hydrated 5-tier r52 root '$r52Root' is required when -RequireFiveTiers is set."
    } else {
        Write-Warning "Hydrated r52 5-tier root '$r52Root' not found; scanning single full candidate fallback."
        & "$BuildRoot\tie_test.exe" --scan 'D:\ClaudeWork\empire-forge\empire-forge-r47-tie' | Tee-Object -FilePath "$BuildRoot\candidate-gate.log"
        if ($LASTEXITCODE -ne 0) { throw 'Installable candidate closure failed' }
    }
    if (Test-Path -LiteralPath (Join-Path $stage 'data')) { throw 'Code-only package unexpectedly holds data' }
    & $Cli package-mod --mod $stage --out $Out | Tee-Object -FilePath "$BuildRoot\package.log"
    if ($LASTEXITCODE -ne 0) { throw 'package-mod failed' }
    $zipPath = Join-Path $Out 'empire-tie-switch-0.3.1.zip'
    if (Test-Path -LiteralPath $zipPath) {
        $zipSha = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
        $zipLen = (Get-Item -LiteralPath $zipPath).Length
        $modHash = ((& $Cli mod-hash --mod $stage) | ConvertFrom-Json).hash
        Write-Output "ZIP=$zipPath"
        Write-Output "Bytes=$zipLen"
        Write-Output "Sha256=$zipSha"
        Write-Output "ContentHash=$modHash"
    }
} finally {
    if (Test-Path -LiteralPath $fixture) { Remove-Item -LiteralPath $fixture -Recurse -Force -ErrorAction SilentlyContinue }
    $env:TEMP = $oldTemp; $env:TMP = $oldTmp; $env:ZIG_GLOBAL_CACHE_DIR = $oldGlobal; $env:ZIG_LOCAL_CACHE_DIR = $oldLocal
}
