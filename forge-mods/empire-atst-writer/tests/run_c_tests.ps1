param(
    [string]$Zig = 'D:\ClaudeWork\workstreams-v21\code-mods\tools\zig\zig.exe'
)

$ErrorActionPreference = 'Stop'
$TestsDir = $PSScriptRoot
$ModRoot = Split-Path $TestsDir -Parent
$WriterSrc = Join-Path $ModRoot 'src'
$WriterSdk = if (Test-Path -LiteralPath 'D:\ClaudeWork\hellforge-src\native\mod-sdk\hellforge_mod.h') {
    'D:\ClaudeWork\hellforge-src\native\mod-sdk'
} elseif (Test-Path -LiteralPath (Join-Path $ModRoot 'sdk')) {
    Join-Path $ModRoot 'sdk'
} else {
    'D:\HellForge\app\sdk'
}
$BuildDir = Join-Path $ModRoot 'build'
New-Item -ItemType Directory -Force $BuildDir | Out-Null
$OutExe = Join-Path $BuildDir 'test_empire_atst_writer.exe'
$BenchExe = Join-Path $BuildDir 'd11_stress_bench.exe'

$Sources = @(
    (Join-Path $TestsDir 'test_empire_atst_writer.c'),
    (Join-Path $WriterSrc 'atst_policy.c'),
    (Join-Path $WriterSrc 'atst_walker_registry.c'),
    (Join-Path $WriterSrc 'atst_writer_core.c')
)

$BenchSources = @(
    (Join-Path $TestsDir 'd11_stress_bench.c'),
    (Join-Path $WriterSrc 'atst_policy.c'),
    (Join-Path $WriterSrc 'atst_walker_registry.c'),
    (Join-Path $WriterSrc 'atst_writer_core.c')
)

$OldGlobal = $env:ZIG_GLOBAL_CACHE_DIR
$OldLocal = $env:ZIG_LOCAL_CACHE_DIR
$Cache = 'D:\ClaudeWork\cache\tmp\atst-zig-cache'
New-Item -ItemType Directory -Force $Cache | Out-Null

try {
    $env:ZIG_GLOBAL_CACHE_DIR = $Cache
    $env:ZIG_LOCAL_CACHE_DIR = $Cache
    if (!(Test-Path -LiteralPath (Join-Path $BuildDir 'empire_atst_writer.dll'))) {
        $DllSources = @(
            (Join-Path $WriterSrc 'atst_mod_main.c'),
            (Join-Path $WriterSrc 'atst_policy.c'),
            (Join-Path $WriterSrc 'atst_walker_registry.c'),
            (Join-Path $WriterSrc 'atst_writer_core.c')
        )
        & $Zig cc -std=c11 -target x86_64-windows-gnu -shared -O2 -ffp-contract=off -fno-fast-math -Wall -Werror -I $WriterSdk -I $WriterSrc @DllSources -o (Join-Path $BuildDir 'empire_atst_writer.dll')
        if ($LASTEXITCODE -ne 0) { throw "DLL compile failed with -Wall -Werror" }
    }
    & $Zig cc -std=c11 -target x86_64-windows-gnu -O2 -ffp-contract=off -fno-fast-math -Wall -Werror -I $WriterSdk -I $WriterSrc -I $TestsDir @Sources -lm -o $OutExe
    if ($LASTEXITCODE -ne 0) { throw "C test compile failed with -Wall -Werror" }
    & $Zig cc -std=c11 -target x86_64-windows-gnu -O2 -ffp-contract=off -fno-fast-math -Wall -Werror -I $WriterSdk -I $WriterSrc -I $TestsDir @BenchSources -lm -o $BenchExe
    if ($LASTEXITCODE -ne 0) { throw "D11 bench compile failed with -Wall -Werror" }
    Push-Location $TestsDir
    try {
        & $OutExe
        if ($LASTEXITCODE -ne 0) { throw "C test execution failed" }
        & $BenchExe
        if ($LASTEXITCODE -ne 0) { throw "D11 bench execution failed" }
    } finally {
        Pop-Location
    }
} finally {
    $env:ZIG_GLOBAL_CACHE_DIR = $OldGlobal
    $env:ZIG_LOCAL_CACHE_DIR = $OldLocal
}
